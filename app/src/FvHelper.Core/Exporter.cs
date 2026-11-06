using System.IO;
using System.Text.Json;

namespace FvHelper.Core;

public enum ExportMode
{
    /// <summary>只导出那次提交的文件内容（不含版本数据，适合"只要文件的人"）。</summary>
    FilesOnly,

    /// <summary>文件 + `.fvhelper.dat` + 内容库，对方拿到可以直接续作（D-12）。</summary>
    WithMeta
}

public sealed class ExportOptions
{
    /// <summary>导出到哪个目录下（工具会在此目录内新建 `导出_<项目名>_rev<N>_<时间>`）。</summary>
    public string TargetDirectory { get; set; } = "";

    public ExportMode Mode { get; set; } = ExportMode.WithMeta;

    /// <summary>含数据信息导出时，是否"作为新起点"：新 repoId、rev 从 1 开始，不带其它历史。</summary>
    public bool AsNewStart { get; set; }

    /// <summary>导出文件夹名（空则自动生成）。</summary>
    public string? FolderName { get; set; }
}

public sealed class ExportResult
{
    public string RootPath { get; init; } = "";
    public int FileCount { get; init; }
    public long Bytes { get; init; }
    public ExportMode Mode { get; init; }
    public int BlobsCopied { get; init; }
    public string? DatPath { get; init; }
}

/// <summary>
/// 导出：把某次提交"当时的文件"写到一个新目录（可选连数据信息一起）。
/// 铁律（D-13）：**导出前先全量校验这次提交用到的所有内容**，缺一个/错一个就整体中止，绝不产出半套。
/// </summary>
public static class Exporter
{
    public static ExportResult Export(Repository repo, int rev, ExportOptions options, CancellationToken ct = default)
    {
        var commit = repo.GetCommit(rev);
        if (commit is null)
        {
            throw new RepositoryException($"没有 rev {rev} 这次提交");
        }

        if (options.Mode == ExportMode.WithMeta && repo.Index.Commits.Count == 0)
        {
            throw new RepositoryException("该文件夹还没有任何提交，无法含版本信息导出");
        }

        // ---- 1) 预校验：这次导出要用到的每个内容都必须存在且哈希正确 ----
        // 含数据信息导出时会连历史一起带走，所以历史提交引用到的内容也必须在（否则对方拿到就是残包）
        var hashes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (options.Mode == ExportMode.WithMeta && !options.AsNewStart)
        {
            foreach (var c in repo.Index.Commits.Where(c => c.Rev <= rev))
            {
                foreach (var entry in c.Tree)
                {
                    hashes[Hashing.Normalize(entry.Hash)] = entry.Size;
                }
            }
        }
        else
        {
            foreach (var entry in commit.Tree)
            {
                hashes[Hashing.Normalize(entry.Hash)] = entry.Size;
            }
        }

        var missing = new List<string>();
        foreach (var (hex, size) in hashes)
        {
            ct.ThrowIfCancellationRequested();
            var blobPath = repo.Blobs.PathFor(hex);
            if (!File.Exists(blobPath))
            {
                missing.Add($"{hex}（内容缺失）");
                continue;
            }

            if (new FileInfo(blobPath).Length != size)
            {
                missing.Add($"{hex}（大小不符）");
                continue;
            }

            if (!repo.Blobs.VerifyContent(hex, ct))
            {
                missing.Add($"{hex}（内容哈希不符）");
            }
        }

        if (missing.Count > 0)
        {
            throw new RepositoryException(
                "导出前校验失败，**没有生成任何导出文件**。以下内容与数据不匹配：\n" + string.Join("\n", missing) +
                "\n\n建议：用「校验」功能复核，或从备份恢复该数据文件。");
        }

        // ---- 2) 准备输出目录 ----
        var parent = string.IsNullOrWhiteSpace(options.TargetDirectory)
            ? System.IO.Path.GetDirectoryName(repo.Root.TrimEnd(System.IO.Path.DirectorySeparatorChar)) ?? repo.Root
            : options.TargetDirectory;

        Directory.CreateDirectory(parent);

        var folderName = options.FolderName ?? DefaultFolderName(repo.Root, commit, options);
        var outRoot = UniqueDirectory(parent, folderName);

        // ---- 3) 写文件 ----
        var written = 0;
        long bytes = 0;
        try
        {
            foreach (var entry in commit.Tree)
            {
                ct.ThrowIfCancellationRequested();
                var destination = PathSafety.CombineInside(outRoot, entry.Path);
                repo.Blobs.CopyToFile(entry.Hash, destination, ct);
                written++;
                bytes += entry.Size;
            }

            // ---- 4) 含数据信息：写 .dat + 内容库 ----
            var datPath = options.Mode == ExportMode.WithMeta
                ? WriteMeta(repo, commit, outRoot, options.AsNewStart, ct)
                : null;

            return new ExportResult
            {
                RootPath = outRoot,
                FileCount = written,
                Bytes = bytes,
                Mode = options.Mode,
                BlobsCopied = options.Mode == ExportMode.WithMeta ? hashes.Count : 0,
                DatPath = datPath
            };
        }
        catch
        {
            // 失败就整体清掉这次的导出目录，不留半套
            TryDeleteTree(outRoot);
            throw;
        }
    }

    private static string WriteMeta(Repository repo, CommitRecord commit, string outRoot, bool asNewStart,
        CancellationToken ct)
    {
        IndexDocument doc;
        List<CommitRecord> included;

        if (asNewStart)
        {
            var clone = JsonSerializer.Deserialize<CommitRecord>(
                JsonSerializer.Serialize(commit, Repository.JsonOptions), Repository.JsonOptions)!;
            clone.Rev = 1;
            clone.ParentRev = 0;

            // "作为新起点"= 这是新人仓库的第一次提交，所有文件都应该按"新增"算，
            // 而不是把原仓库里那条提交的差异（可能是 M/D/R）照搬过去，否则新仓库读起来自相矛盾。
            clone.Changes = clone.Tree.Select(t => new ChangeRecord
            {
                Path = t.Path,
                Action = "A",
                Hash = Hashing.Prefixed(Hashing.Normalize(t.Hash)),
                Size = t.Size,
                Mtime = null
            }).ToList();
            clone.Stats = new CommitStats
            {
                Files = clone.Tree.Count,
                Added = clone.Tree.Count,
                Modified = 0,
                Deleted = 0,
                Renamed = 0,
                Bytes = clone.Tree.Sum(t => t.Size)
            };

            doc = new IndexDocument
            {
                Schema = IndexDocument.SchemaId,
                RepoId = Guid.NewGuid().ToString("N"),
                CreatedAt = Repository.NowIso(),
                AppVersion = Repository.AppVersionText,
                Head = 1,
                Author = clone.Author,
                Ignore = new List<string>(),
                Commits = new List<CommitRecord> { clone }
            };
            included = doc.Commits;
        }
        else
        {
            var all = JsonSerializer.Deserialize<List<CommitRecord>>(
                JsonSerializer.Serialize(repo.Index.Commits, Repository.JsonOptions), Repository.JsonOptions)!;
            included = all.Where(c => c.Rev <= commit.Rev).OrderBy(c => c.Rev).ToList();

            doc = new IndexDocument
            {
                Schema = IndexDocument.SchemaId,
                RepoId = repo.Index.RepoId,
                CreatedAt = repo.Index.CreatedAt,
                AppVersion = Repository.AppVersionText,
                Head = commit.Rev,
                Author = commit.Author,
                Ignore = new List<string>(repo.Index.Ignore),
                Commits = included
            };
        }

        // 复制这次导出需要的内容（按需去重）
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in included)
        {
            foreach (var t in c.Tree)
            {
                needed.Add(Hashing.Normalize(t.Hash));
            }
        }

        foreach (var hex in needed)
        {
            ct.ThrowIfCancellationRequested();
            var source = repo.Blobs.PathFor(hex);
            if (!File.Exists(source))
            {
                throw new RepositoryException("复制内容库时缺少内容：" + hex);
            }

            var relative = BlobStore.BlobRelativePath(hex);
            var destination = System.IO.Path.Combine(outRoot, BlobStore.StoreDirName, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }

        var datPath = DatFile.PathFor(outRoot);
        var bytes = DatFile.Build(doc);
        var check = DatFile.Parse(bytes);
        if (!check.IsUsable)
        {
            throw new RepositoryException("生成的导出数据文件无法读回：" + check.Error);
        }

        File.WriteAllBytes(datPath, bytes);
        VersionData.Hide(outRoot);
        return datPath;
    }

    /// <summary>
    /// 目录名自带"含不含版本信息"：`导出_<项目名>_rev<N>_<时间>_仅文件` / `…_含版本信息`（新起点再加 `-新起点`）。
    /// 用户拿到一堆导出目录时，光看名字就能分清哪个能继续提交、哪个只是当版文件。
    /// </summary>
    private static string DefaultFolderName(string repoRoot, CommitRecord commit, ExportOptions options)
    {
        var projectName = new DirectoryInfo(repoRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar)).Name;
        var time = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var kind = options.Mode == ExportMode.WithMeta
            ? (options.AsNewStart ? "含版本信息-新起点" : "含版本信息")
            : "仅文件";
        return Sanitize($"导出_{projectName}_rev{commit.Rev}_{time}_{kind}");
    }

    private static string Sanitize(string name)
    {
        var bad = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        var cleaned = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            cleaned.Append(bad.Contains(c) ? '_' : c);
        }

        return cleaned.ToString().Trim().TrimEnd('.');
    }

    private static string UniqueDirectory(string parent, string name)
    {
        var path = System.IO.Path.Combine(parent, name);
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return Directory.CreateDirectory(path).FullName;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = System.IO.Path.Combine(parent, $"{name}-{i}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return Directory.CreateDirectory(candidate).FullName;
            }
        }

        throw new RepositoryException("同名导出目录太多，请清理后再试：" + name);
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 清不掉就算了，至少要保证不宣称成功
        }
    }
}
