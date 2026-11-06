using System.IO;

namespace FvHelper.Core;

/// <summary>校验结论（`docs\03` §9）：四态。</summary>
public enum VerifyLevel
{
    /// <summary>四层全过。</summary>
    Ok,

    /// <summary>数据自身没问题，但工作区与最新提交不一致（这是正常的"有待提交改动"状态）。</summary>
    Warn,

    /// <summary>数据与内容不匹配（缺内容、大小/哈希不符、索引自相矛盾）→ 禁止导出、禁止提交。</summary>
    Mismatch,

    /// <summary>`.dat` 容器本身损坏（魔数/长度/校验/尾标记不对）。</summary>
    Corrupt
}

public sealed class VerifyResult
{
    public VerifyLevel Level { get; set; } = VerifyLevel.Ok;
    public List<string> Problems { get; } = new();
    public List<string> Warnings { get; } = new();
    public int LayersPassed { get; set; }
    public int Files { get; set; }
    public long Bytes { get; set; }

    /// <summary>
    /// 全新文件夹（还没有 `.fvhelper.dat`，也没有 `.bak`）：不算损坏，只是还没有版本信息。
    /// 此时 Level 为 <see cref="VerifyLevel.Ok"/>、不产生 Problems，界面按"可以开始用"展示。
    /// </summary>
    public bool IsNewRepo { get; set; }

    public bool CanExport => Level is VerifyLevel.Ok or VerifyLevel.Warn;

    public string LevelText => Level switch
    {
        VerifyLevel.Ok => "OK",
        VerifyLevel.Warn => "WARN",
        VerifyLevel.Mismatch => "MISMATCH",
        _ => "CORRUPT"
    };
}

/// <summary>
/// 四层校验：L1 容器完整性 → L2 索引自洽 → L3 内容库存在性/大小/（可选）哈希 → L4 工作区一致性。
/// </summary>
public static class Verifier
{
    public static VerifyResult Check(string root, bool deepHash = false, bool checkWorkspace = true,
        CancellationToken ct = default)
    {
        var result = new VerifyResult();

        // L1：容器
        var dat = DatFile.Read(root);

        // 全新文件夹：没有 .fvhelper.dat，也没有可救的 .bak —— 这不是损坏，是还没开始用
        // （第一次提交会自动建立版本信息）。这里直接给"可以开始"的结论，别把用户的红条吓住。
        if (dat.Status == DatStatus.Missing && !File.Exists(DatFile.BackupPathFor(root)))
        {
            result.IsNewRepo = true;
            result.Level = VerifyLevel.Ok;
            result.LayersPassed = 0;
            result.Warnings.Add("这是全新文件夹：还没有版本信息，第一次提交时自动建立");
            return result;
        }

        if (!dat.IsUsable)
        {
            var backupUsable = false;
            if (File.Exists(DatFile.BackupPathFor(root)))
            {
                try
                {
                    using var fs = new FileStream(DatFile.BackupPathFor(root), FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var ms = new MemoryStream();
                    fs.CopyTo(ms);
                    backupUsable = DatFile.Parse(ms.ToArray()).IsUsable;
                }
                catch
                {
                    backupUsable = false;
                }
            }

            if (backupUsable)
            {
                result.Warnings.Add("主数据文件不可用（" + dat.Error + "），但 .bak 备份可读，可用备份恢复");
            }
            else if (File.Exists(DatFile.BackupPathFor(root)))
            {
                result.Warnings.Add("主数据文件与 .bak 备份都不可用，只能依赖别处留存的副本");
            }

            result.Level = VerifyLevel.Corrupt;
            result.Problems.Add("L1 容器：" + dat.Error);
            return result;
        }

        result.LayersPassed = 1;
        var index = dat.Index!;

        // L2：索引自洽
        var l2 = CheckIndexConsistency(index, out var l2Problems);
        if (!l2)
        {
            result.Level = VerifyLevel.Mismatch;
            result.Problems.AddRange(l2Problems);
            return result;
        }

        result.LayersPassed = 2;

        var rev = index.Commits.Count == 0 ? 0 : index.Head;
        var commit = index.Commits.FirstOrDefault(c => c.Rev == index.Head);

        // L3：内容库——**检查全部历史提交引用到的内容**，不只是 head
        // （导出完整历史时要用到旧版本内容，缺一份也算数据与内容不匹配）
        var blobs = new BlobStore(root);
        var l3Problems = new List<string>();
        var checkedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var needed = new Dictionary<string, (long Size, string SamplePath)>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in index.Commits)
        {
            foreach (var entry in c.Tree)
            {
                var hex = Hashing.Normalize(entry.Hash);
                if (!needed.ContainsKey(hex))
                {
                    needed[hex] = (entry.Size, entry.Path);
                }
            }
        }

        foreach (var (hex, info) in needed)
        {
            ct.ThrowIfCancellationRequested();
            checkedHashes.Add(hex);
            var path = blobs.PathFor(hex);
            if (!File.Exists(path))
            {
                l3Problems.Add($"L3 内容库缺少文件内容：{info.SamplePath} → {hex}");
                continue;
            }

            var actualSize = new FileInfo(path).Length;
            if (actualSize != info.Size)
            {
                l3Problems.Add($"L3 内容大小不符：{info.SamplePath}（记录 {info.Size} 字节，实际 {actualSize} 字节）");
                continue;
            }

            if (deepHash && !blobs.VerifyContent(hex, ct))
            {
                l3Problems.Add($"L3 内容哈希不符：{info.SamplePath} → {hex}（内容库里的字节已被改动）");
            }
        }

        if (l3Problems.Count > 0)
        {
            result.Level = VerifyLevel.Mismatch;
            result.Problems.AddRange(l3Problems);
            return result;
        }

        result.LayersPassed = 3;
        result.Files = commit?.Tree.Count ?? 0;
        result.Bytes = commit?.Tree.Sum(t => t.Size) ?? 0;

        // L4：工作区一致性（与最新提交比：多了/少了/改了都正常，只是"有待提交"，记为 WARN）
        if (checkWorkspace)
        {
            if (commit is null)
            {
                var snapshot = new WorkspaceScanner(root, index.Ignore).Scan();
                if (snapshot.Entries.Count > 0)
                {
                    result.Level = VerifyLevel.Warn;
                    result.Warnings.Add($"L4 尚未有提交，工作区有 {snapshot.Entries.Count} 个文件待首次提交");
                }
            }
            else
            {
                var cache = RepoStateCache.Load(root);
                var plan = ChangeDetector.Build(root, index, new WorkspaceScanner(root, index.Ignore).Scan(), cache, ct);
                if (plan.Changes.Count > 0)
                {
                    result.Level = VerifyLevel.Warn;
                    result.Warnings.Add(
                        $"L4 工作区与 rev {rev} 不一致：修改 {plan.Stats.Modified} / 新增 {plan.Stats.Added} / 删除 {plan.Stats.Deleted} / 改名 {plan.Stats.Renamed}（属正常的未提交改动）");
                }
                else
                {
                    result.LayersPassed = 4;
                }
            }
        }

        return result;
    }

    /// <summary>只校验某一次提交用到的内容（界面看单条 changelist 详情时用），不做 L4 与索引全局检查。</summary>
    public static VerifyResult CheckCommit(string root, CommitRecord commit, bool deepHash = false,
        CancellationToken ct = default)
    {
        var result = new VerifyResult { Files = commit.Tree.Count, Bytes = commit.Tree.Sum(t => t.Size) };
        var blobs = new BlobStore(root);
        var needed = new Dictionary<string, (long Size, string Path)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in commit.Tree)
        {
            var hex = Hashing.Normalize(entry.Hash);
            if (!needed.ContainsKey(hex))
            {
                needed[hex] = (entry.Size, entry.Path);
            }
        }

        foreach (var (hex, info) in needed)
        {
            ct.ThrowIfCancellationRequested();
            var path = blobs.PathFor(hex);
            if (!File.Exists(path))
            {
                result.Problems.Add($"内容库缺少文件内容：{info.Path} → {hex}");
                continue;
            }

            if (new FileInfo(path).Length != info.Size)
            {
                result.Problems.Add($"内容大小不符：{info.Path}（记录 {info.Size} 字节，实际 {new FileInfo(path).Length} 字节）");
                continue;
            }

            if (deepHash && !blobs.VerifyContent(hex, ct))
            {
                result.Problems.Add($"内容哈希不符：{info.Path} → {hex}");
            }
        }

        result.Level = result.Problems.Count > 0 ? VerifyLevel.Mismatch : VerifyLevel.Ok;
        result.LayersPassed = result.Problems.Count == 0 ? 3 : 0;
        return result;
    }

    private static bool CheckIndexConsistency(IndexDocument index, out List<string> problems)
    {
        problems = new List<string>();

        if (index.Commits.Count == 0)
        {
            if (index.Head != 0)
            {
                problems.Add($"L2 head 指向 rev {index.Head}，但没有任何提交");
            }

            return problems.Count == 0;
        }

        var revs = index.Commits.Select(c => c.Rev).ToList();
        var expected = Enumerable.Range(1, revs.Count).ToList();
        if (!revs.SequenceEqual(expected))
        {
            problems.Add("L2 提交序号不连续（应为 1..N 顺序递增）");
        }

        var head = index.Commits.FirstOrDefault(c => c.Rev == index.Head);
        if (head is null)
        {
            problems.Add($"L2 head 指向的 rev {index.Head} 不存在");
        }

        foreach (var commit in index.Commits)
        {
            if (commit.ParentRev != commit.Rev - 1)
            {
                problems.Add($"L2 rev {commit.Rev} 的 parent 应为 {commit.Rev - 1}，实际 {commit.ParentRev}");
            }

            if (string.IsNullOrWhiteSpace(commit.Message))
            {
                problems.Add($"L2 rev {commit.Rev} 没有提交说明");
            }

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in commit.Tree)
            {
                try
                {
                    var normalized = PathSafety.NormalizeRelative(entry.Path);
                    if (normalized != entry.Path)
                    {
                        problems.Add($"L2 rev {commit.Rev} 的路径未规范化：{entry.Path}");
                    }
                }
                catch (ArgumentException ex)
                {
                    problems.Add($"L2 rev {commit.Rev} 非法路径：{ex.Message}");
                }

                if (!seenPaths.Add(entry.Path))
                {
                    problems.Add($"L2 rev {commit.Rev} 出现重复路径：{entry.Path}");
                }

                if (Hashing.Normalize(entry.Hash).Length != 64)
                {
                    problems.Add($"L2 rev {commit.Rev} 的文件哈希格式不对：{entry.Path}");
                }

                if (entry.Size < 0)
                {
                    problems.Add($"L2 rev {commit.Rev} 的文件大小非法：{entry.Path}");
                }
            }

            foreach (var change in commit.Changes)
            {
                if (change.Action is not ("A" or "M" or "D" or "R"))
                {
                    problems.Add($"L2 rev {commit.Rev} 的动作用法非法：{change.Action}（{change.Path}）");
                }

                if (change.Action is "A" or "M" && Hashing.Normalize(change.Hash).Length != 64)
                {
                    problems.Add($"L2 rev {commit.Rev} 的改动缺少有效哈希：{change.Path}");
                }

                if (change.Action == "R" && string.IsNullOrWhiteSpace(change.From))
                {
                    problems.Add($"L2 rev {commit.Rev} 的改名记录缺少原路径：{change.Path}");
                }

                // 动作与树的一致性：A/M/R 必须在树里；D 必不在树里
                var inTree = commit.Tree.Any(t => string.Equals(t.Path, change.Path, StringComparison.OrdinalIgnoreCase));
                if (change.Action is "A" or "M" or "R")
                {
                    if (!inTree)
                    {
                        problems.Add($"L2 rev {commit.Rev} 的 {change.Action} 记录在树里找不到：{change.Path}");
                    }
                    else
                    {
                        var treeEntry = commit.Tree.First(t => string.Equals(t.Path, change.Path, StringComparison.OrdinalIgnoreCase));
                        if (!string.Equals(Hashing.Normalize(treeEntry.Hash), Hashing.Normalize(change.Hash), StringComparison.Ordinal))
                        {
                            problems.Add($"L2 rev {commit.Rev} 的改动哈希与树不一致：{change.Path}");
                        }
                    }
                }
                else if (inTree)
                {
                    problems.Add($"L2 rev {commit.Rev} 的删除记录仍在树里：{change.Path}");
                }
            }
        }

        return problems.Count == 0;
    }
}
