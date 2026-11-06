using System.IO;
using System.Text.Json;

namespace FvHelper.Core;

public sealed class RepositoryException : Exception
{
    public RepositoryException(string message) : base(message)
    {
    }
}

/// <summary>打开仓库时发现的异常情况，供 UI 决定提示文案。</summary>
public enum RepoHealth
{
    Healthy,
    RecoveredFromBackup,
    EmptyNoCommits
}

public sealed class StatusResult
{
    public CommitPlan Plan { get; init; } = new();
    public RepoHealth Health { get; init; } = RepoHealth.Healthy;
    public string? Notice { get; init; }

    /// <summary>被其它程序占着、但共享读仍然成功（能记录快照，仅提示）。</summary>
    public List<string> InUse { get; } = new();
}

public sealed class CommitResult
{
    public CommitRecord Record { get; init; } = new();
    public string DatPath { get; init; } = "";
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// 版本库门面：把"一个文件夹 + 其中的 .fvhelper.dat + .fvhelper\objects"当成一个项目。
/// 上层（WPF / CLI）只跟这个类打交道。
/// </summary>
public sealed class Repository
{
    private readonly string _root;
    private readonly BlobStore _blobs;

    private Repository(string root, IndexDocument index, RepoHealth health, string? notice)
    {
        _root = root;
        _blobs = new BlobStore(root);
        Index = index;
        Health = health;
        Notice = notice;
    }

    public string Root => _root;

    public IndexDocument Index { get; }

    public RepoHealth Health { get; }

    public string? Notice { get; }

    public BlobStore Blobs => _blobs;

    public static string DatPathFor(string root) => DatFile.PathFor(root);

    /// <summary>项目打开：有 `.dat` 就必须能校验通过（坏则回退 `.bak`），没有则视为"待初始化"的空项目。</summary>
    public static Repository Open(string root)
    {
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
        {
            throw new RepositoryException("文件夹不存在：" + full);
        }

        // 老版本建出来的库还没有隐藏属性：打开一次就补上（幂等，改不了属性也不影响读取）
        VersionData.Hide(full);

        var result = DatFile.Read(full);
        if (result.IsUsable)
        {
            var health = result.Index!.Commits.Count == 0 ? RepoHealth.EmptyNoCommits : RepoHealth.Healthy;
            return new Repository(full, result.Index!, health, null);
        }

        if (result.Status == DatStatus.Missing)
        {
            return new Repository(full, NewIndex(), RepoHealth.EmptyNoCommits, "该文件夹尚无版本库，首次提交时建立");
        }

        // 主文件坏了：试 .bak
        var backup = DatFile.BackupPathFor(full);
        if (File.Exists(backup))
        {
            byte[] bytes;
            using (var fs = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }

            var bakResult = DatFile.Parse(bytes);
            if (bakResult.IsUsable)
            {
                return new Repository(full, bakResult.Index!, RepoHealth.RecoveredFromBackup,
                    "主数据文件损坏，已从 .bak 备份恢复（建议提交一次以重建主文件）：" + result.Error);
            }
        }

        throw new RepositoryException("数据文件损坏且无法从备份恢复：" + result.Error);
    }

    public static bool HasRepository(string root) => File.Exists(DatFile.PathFor(root));

    /// <summary>初始化：写第一份空索引（rev 0、无提交）。</summary>
    public static Repository Init(string root, string author, IEnumerable<string>? ignore = null)
    {
        var full = Path.GetFullPath(root);
        Directory.CreateDirectory(full);

        if (File.Exists(DatFile.PathFor(full)))
        {
            var existing = DatFile.Read(full);
            if (existing.IsUsable)
            {
                VersionData.Hide(full);
                return new Repository(full, existing.Index!, RepoHealth.Healthy, null);
            }

            throw new RepositoryException("已存在数据文件但无法读取：" + existing.Error);
        }

        var doc = NewIndex();
        doc.Author = author ?? "";
        doc.CreatedAt = NowIso();
        if (ignore is not null)
        {
            doc.Ignore.AddRange(ignore);
        }

        Directory.CreateDirectory(new BlobStore(full).ObjectsDirectory);
        WriteIndex(full, doc);
        VersionData.Hide(full);

        return new Repository(full, doc, RepoHealth.EmptyNoCommits, null);
    }

    private static IndexDocument NewIndex() => new()
    {
        Schema = IndexDocument.SchemaId,
        // repoId 用随机 id：导出/分享后能认出"同一仓库的另一个副本"（D-12）
        RepoId = Guid.NewGuid().ToString("N"),
        CreatedAt = NowIso(),
        AppVersion = AppVersionText,
        Head = 0,
        Commits = new List<CommitRecord>(),
        Ignore = new List<string>()
    };

    public static string AppVersionText
    {
        get
        {
            var v = typeof(Repository).Assembly.GetName().Version;
            return v is null ? "0.1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string NowIso() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

    /// <summary>扫一次并算出"如果要提交，会提交什么"。</summary>
    public StatusResult Status(CancellationToken ct = default)
    {
        var snapshot = new WorkspaceScanner(_root, Index.Ignore).Scan();
        var cache = RepoStateCache.Load(_root);
        var plan = ChangeDetector.Build(_root, Index.Commits.Count == 0 ? null : Index, snapshot, cache, ct);

        var status = new StatusResult { Plan = plan, Health = Health, Notice = Notice };

        // I-03 预检：先试独占，失败再试共享读——共享读得到说明只是"被打开"，仍可记录快照
        foreach (var change in plan.Changes.Where(c => c.Action is "A" or "M"))
        {
            var full = PathSafety.CombineInside(_root, change.Path);
            try
            {
                using var exclusive = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                try
                {
                    using var shared = new FileStream(full, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    status.InUse.Add(change.Path);
                }
                catch (Exception ex)
                {
                    status.InUse.Add(change.Path);
                    plan.Uncertain.Add(change.Path + "（无法读取：" + ex.Message + "）");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                plan.Uncertain.Add(change.Path + "（无读取权限：" + ex.Message + "）");
            }
        }

        return status;
    }

    /// <summary>
    /// 提交。`includePaths` 为 null 表示提交全部改动；给定时只提交这些路径（其余改动保留在工作区）。
    /// `detail` 是可选的提交详情（多行），写进版本信息里，与标题（`message`）分开存。
    /// 返回的记录已写入 `.dat`（原子替换 + `.bak`）。
    /// </summary>
    public CommitResult Commit(string message, string author, IReadOnlyCollection<string>? includePaths = null,
        CancellationToken ct = default, string detail = "")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new RepositoryException("提交说明不能为空");
        }

        // 数据与内容不自洽时禁止提交（D-13：MISMATCH 禁提交/禁导出），避免把坏状态继续往前推。
        // 例外：全新文件夹还没有 `.fvhelper.dat`，没有"旧状态"需要保护 —— 首次提交就是建库（rev 1），跳过预检。
        if (HasRepository(_root))
        {
            var health = Verifier.Check(_root, deepHash: false, checkWorkspace: false, ct);
            if (health.Level is VerifyLevel.Mismatch or VerifyLevel.Corrupt)
            {
                throw new RepositoryException("当前数据文件与内容库不一致，已禁止提交：\n" + string.Join("\n", health.Problems));
            }
        }

        var status = Status(ct);
        if (status.Plan.Uncertain.Count > 0)
        {
            throw new RepositoryException("有文件读不出来，已中止（不会留半套数据）：\n" + string.Join("\n", status.Plan.Uncertain));
        }

        var selected = status.Plan.Changes;
        if (includePaths is not null)
        {
            var set = new HashSet<string>(includePaths.Select(p => p.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
            selected = status.Plan.Changes
                .Where(c => set.Contains(c.Path) || (c.From is not null && set.Contains(c.From)))
                .ToList();
        }

        if (selected.Count == 0)
        {
            throw new RepositoryException("没有需要提交的改动");
        }

        var warnings = new List<string>();
        if (status.InUse.Count > 0)
        {
            warnings.Add("以下文件正被其它程序打开，已按当前磁盘内容记录：" + string.Join("、", status.InUse));
        }

        // 1) 先写内容库（只写本次要提交的 A/M/R）
        foreach (var change in selected.Where(c => c.Action is "A" or "M" or "R"))
        {
            ct.ThrowIfCancellationRequested();
            var full = PathSafety.CombineInside(_root, change.Path);
            var hash = _blobs.PutFile(full, out var size, ct);
            if (size != change.Size)
            {
                throw new RepositoryException($"提交过程中文件大小变了（{change.Path}），请重新扫描后再提交");
            }

            if (!string.Equals(hash, Hashing.Normalize(change.Hash), StringComparison.Ordinal))
            {
                throw new RepositoryException($"提交过程中文件内容变了（{change.Path}），请重新扫描后再提交");
            }
        }

        // 2) 组成提交后的全量 tree：以"上次提交的树"为底，套用本次选中的改动
        //    （部分提交时，未选中的改动不会落库，保持 head 的状态）
        var baseTree = Index.Commits.Count == 0 ? new List<TreeEntry>() : Head!.Tree;
        var newTree = BuildTree(baseTree, selected);

        var rev = Index.Head + 1;
        var record = new CommitRecord
        {
            Rev = rev,
            Id = Hashing.ShortId(Index.RepoId, rev, message, author, NowIso()),
            ParentRev = Index.Head,
            Message = message.Trim(),
            Detail = detail.Trim(),
            Author = string.IsNullOrWhiteSpace(author) ? "未署名" : author.Trim(),
            CreatedAt = NowIso(),
            Stats = new CommitStats
            {
                Files = newTree.Count,
                Added = selected.Count(c => c.Action == "A"),
                Modified = selected.Count(c => c.Action == "M"),
                Deleted = selected.Count(c => c.Action == "D"),
                Renamed = selected.Count(c => c.Action == "R"),
                Bytes = selected.Where(c => c.Action is "A" or "M" or "R").Sum(c => c.Size)
            },
            Changes = selected,
            Tree = newTree
        };

        // 3) 追加到索引并原子落盘
        Index.Commits.Add(record);
        Index.Head = rev;
        Index.Author = record.Author;
        WriteIndex(_root, Index);

        // 4) 更新本地加速缓存（不算历史，纯优化）
        //    只缓存"工作区内容确实等于新树"的文件：部分提交时，未提交的改动不能进缓存，
        //    否则下次扫描会误判为"没变化"。
        var cache = RepoStateCache.Load(_root);
        var workingHashes = status.Plan.Tree.ToDictionary(t => t.Path, t => Hashing.Normalize(t.Hash), StringComparer.OrdinalIgnoreCase);
        var treeHashes = newTree.ToDictionary(t => t.Path, t => Hashing.Normalize(t.Hash), StringComparer.OrdinalIgnoreCase);
        var fresh = new WorkspaceScanner(_root, Index.Ignore).Scan();
        foreach (var e in fresh.Entries)
        {
            if (!treeHashes.TryGetValue(e.Path, out var treeHash)
                || !workingHashes.TryGetValue(e.Path, out var workingHash)
                || !string.Equals(treeHash, workingHash, StringComparison.Ordinal))
            {
                continue;
            }

            cache.Set(e.Path, e.Size, e.MtimeTicks, treeHash);
        }

        cache.Save(_root);

        return new CommitResult { Record = record, DatPath = DatFile.PathFor(_root), Warnings = warnings };
    }

    private static List<TreeEntry> BuildTree(List<TreeEntry> workingTree, List<ChangeRecord> selected)
    {
        var map = new Dictionary<string, TreeEntry>(StringComparer.OrdinalIgnoreCase);

        // 以"上次提交的树"为底，再套用本次选中的改动（部分提交时未选中的改动不落库）
        var baseline = new List<TreeEntry>();
        // 调用方传入的 workingTree 是全量工作区，但部分提交只能用 baseline 做底
        // 这里由 Commit 决定：见下方 override
        baseline.AddRange(workingTree);

        foreach (var entry in baseline)
        {
            map[entry.Path] = entry;
        }

        foreach (var change in selected)
        {
            switch (change.Action)
            {
                case "D":
                    map.Remove(change.Path);
                    break;
                case "R":
                    if (change.From is not null)
                    {
                        map.Remove(change.From);
                    }

                    map[change.Path] = new TreeEntry { Path = change.Path, Hash = change.Hash!, Size = change.Size };
                    break;
                default:
                    map[change.Path] = new TreeEntry { Path = change.Path, Hash = change.Hash!, Size = change.Size };
                    break;
            }
        }

        var result = map.Values.OrderBy(t => t.Path, StringComparer.Ordinal).ToList();
        return result;
    }

    private static void WriteIndex(string root, IndexDocument document)
    {
        var bytes = DatFile.Build(document);

        // 写前自检：能解析回来才算数（防序列化 bug 写坏用户数据）
        var verify = DatFile.Parse(bytes);
        if (!verify.IsUsable)
        {
            throw new RepositoryException("生成的索引无法读回，已中止写入：" + verify.Error);
        }

        var tmp = DatFile.TempPathFor(root);
        File.WriteAllBytes(tmp, bytes);

        var dat = DatFile.PathFor(root);
        var bak = DatFile.BackupPathFor(root);

        try
        {
            AtomicFile.Replace(tmp, dat, bak);
        }
        catch (Exception ex)
        {
            TryDelete(tmp);
            throw new RepositoryException("写入数据文件失败（可能被占用或只读）：" + ex.Message);
        }

        // 版本数据对用户不可见：写完统一打 Hidden（替换会带走旧属性，所以放在替换之后）
        VersionData.Hide(root);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 忽略
        }
    }

    public List<CommitRecord> History() =>
        Index.Commits.OrderByDescending(c => c.Rev).ToList();

    public CommitRecord? GetCommit(int rev) => Index.Commits.FirstOrDefault(c => c.Rev == rev);

    public CommitRecord? Head => Index.Commits.FirstOrDefault(c => c.Rev == Index.Head);

    /// <summary>把仓库信息写成可读文本（CLI / 诊断用）。</summary>
    public string Describe()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"项目：{_root}");
        sb.AppendLine($"repoId：{Index.RepoId}");
        sb.AppendLine($"head：rev {Index.Head}（共 {Index.Commits.Count} 次提交）");
        sb.AppendLine($"作者：{Index.Author}");
        sb.AppendLine($"内容库：{_blobs.TotalBytes():N0} 字节");
        return sb.ToString();
    }

    public static JsonSerializerOptions JsonOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
