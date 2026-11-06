using System.IO;

namespace FvHelper.Core;

/// <summary>一次"提交计划"：算好这次要提交哪些文件、新树长什么样，但还没写盘。</summary>
public sealed class CommitPlan
{
    public List<ChangeRecord> Changes { get; } = new();
    public List<TreeEntry> Tree { get; } = new();
    public CommitStats Stats { get; } = new();
    public List<string> Uncertain { get; } = new();

    public bool HasChanges => Changes.Count > 0;

    public bool Contains(string path) =>
        Changes.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)
                         || (c.From is not null && string.Equals(c.From, path, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>
/// 变更识别：对比"工作区快照"与"上次提交的 tree"，产出 A/M/D/R。
/// 判定口径（`docs\03` §9.3）：size+mtime 缓存命中 → 沿用旧哈希；否则真算哈希再定"是否真的变了"。
/// 这样"只打开没保存"（mtime 变了但内容没变）不会被算成修改。
/// </summary>
public static class ChangeDetector
{
    public static CommitPlan Build(string root, IndexDocument? index, WorkspaceSnapshot snapshot, RepoStateCache cache,
        CancellationToken ct = default)
    {
        var baseline = index is null || index.Commits.Count == 0
            ? new Dictionary<string, TreeEntry>(StringComparer.OrdinalIgnoreCase)
            : index.Commits.First(c => c.Rev == index.Head).Tree
                .ToDictionary(t => t.Path, t => t, StringComparer.OrdinalIgnoreCase);

        var plan = new CommitPlan();
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var needHash = new List<WorkspaceEntry>();

        foreach (var entry in snapshot.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var known = baseline.TryGetValue(entry.Path, out var baseEntry);
            if (known && baseEntry!.Size == entry.Size)
            {
                var cached = cache.Get(entry.Path);
                if (cached is not null
                    && cached.Size == entry.Size
                    && cached.MtimeTicks == entry.MtimeTicks
                    && string.Equals(Hashing.Normalize(cached.Hash), Hashing.Normalize(baseEntry.Hash), StringComparison.Ordinal))
                {
                    hashes[entry.Path] = Hashing.Normalize(baseEntry.Hash);
                    cache.Set(entry.Path, entry.Size, entry.MtimeTicks, hashes[entry.Path]);
                    continue;
                }
            }

            needHash.Add(entry);
        }

        HashInParallel(root, needHash, hashes, ct);

        var added = new List<ChangeRecord>();
        var modified = new List<ChangeRecord>();
        var renames = new List<ChangeRecord>();

        foreach (var entry in snapshot.Entries)
        {
            var hash = hashes[entry.Path];
            plan.Tree.Add(new TreeEntry { Path = entry.Path, Hash = Hashing.Prefixed(hash), Size = entry.Size });

            if (!baseline.TryGetValue(entry.Path, out var old))
            {
                added.Add(new ChangeRecord
                {
                    Path = entry.Path,
                    Action = "A",
                    Hash = Hashing.Prefixed(hash),
                    Size = entry.Size,
                    Mtime = FormatTime(entry.MtimeTicks)
                });
                continue;
            }

            if (!string.Equals(Hashing.Normalize(old.Hash), hash, StringComparison.Ordinal))
            {
                modified.Add(new ChangeRecord
                {
                    Path = entry.Path,
                    Action = "M",
                    Hash = Hashing.Prefixed(hash),
                    Size = entry.Size,
                    Mtime = FormatTime(entry.MtimeTicks),
                    OldHash = Hashing.Normalize(old.Hash)
                });
            }
        }

        var deleted = new List<ChangeRecord>();
        foreach (var old in baseline.Values)
        {
            var stillThere = snapshot.Entries.Any(e => string.Equals(e.Path, old.Path, StringComparison.OrdinalIgnoreCase));
            if (!stillThere)
            {
                deleted.Add(new ChangeRecord
                {
                    Path = old.Path,
                    Action = "D",
                    Size = old.Size,
                    OldHash = Hashing.Normalize(old.Hash)
                });
            }
        }

        // 改名识别：删除项与新增项"内容相同"（哈希、大小都对上）即视为 R（纯启发式，宁可少判不可错判）
        foreach (var del in deleted.ToList())
        {
            var match = added.FirstOrDefault(a =>
                a.Size == del.Size
                && string.Equals(Hashing.Normalize(a.Hash), Hashing.Normalize(del.OldHash), StringComparison.Ordinal));
            if (match is null)
            {
                continue;
            }

            renames.Add(new ChangeRecord
            {
                Path = match.Path,
                Action = "R",
                From = del.Path,
                Hash = match.Hash,
                OldHash = del.OldHash,
                Size = match.Size,
                Mtime = match.Mtime
            });
            added.Remove(match);
            deleted.Remove(del);
        }

        plan.Changes.AddRange(added);
        plan.Changes.AddRange(modified);
        plan.Changes.AddRange(deleted);
        plan.Changes.AddRange(renames);

        // 展示顺序：修改 → 新增 → 删除 → 改名（与 P3 骨架一致）
        plan.Changes.Sort((a, b) =>
        {
            var byAction = Order(a.Action).CompareTo(Order(b.Action));
            return byAction != 0 ? byAction : string.CompareOrdinal(a.Path, b.Path);
        });

        plan.Stats.Files = plan.Tree.Count;
        plan.Stats.Added = added.Count;
        plan.Stats.Modified = modified.Count;
        plan.Stats.Deleted = deleted.Count;
        plan.Stats.Renamed = renames.Count;
        plan.Stats.Bytes = plan.Changes.Where(c => c.Action is "A" or "M" or "R").Sum(c => c.Size);

        return plan;
    }

    private static int Order(string action) => action switch
    {
        "M" => 0,
        "A" => 1,
        "D" => 2,
        "R" => 3,
        _ => 4
    };

    internal static string FormatTime(long mtimeTicksUtc) =>
        new DateTime(mtimeTicksUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss");

    private static void HashInParallel(string root, List<WorkspaceEntry> entries, Dictionary<string, string> hashes,
        CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var degree = Math.Min(8, Math.Max(1, Environment.ProcessorCount));
        var errors = new List<string>();

        Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = ct },
            entry =>
            {
                try
                {
                    var full = PathSafety.CombineInside(root, entry.Path);
                    var hash = Hashing.HashFile(full, ct);
                    lock (hashes)
                    {
                        hashes[entry.Path] = hash;
                    }
                }
                catch (Exception ex)
                {
                    lock (errors)
                    {
                        errors.Add(entry.Path + "：" + ex.Message);
                    }
                }
            });

        if (errors.Count > 0)
        {
            throw new IOException("以下文件读取失败，无法计算哈希：\n" + string.Join("\n", errors));
        }
    }
}
