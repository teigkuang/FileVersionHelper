using System.IO;

namespace FvHelper.Core;

public sealed class WorkspaceEntry
{
    public string Path { get; init; } = "";
    public long Size { get; init; }
    public long MtimeTicks { get; init; }
}

public sealed class WorkspaceSnapshot
{
    public List<WorkspaceEntry> Entries { get; } = new();

    /// <summary>读不出来的文件（权限/被独占），提交时需要跳过并提示。</summary>
    public List<string> Unreadable { get; } = new();
}

/// <summary>
/// 工作区快照：只做"枚举 + size/mtime"，**不算哈希**（哈希在变更识别阶段按需做，避免每次扫描全盘读）。
/// </summary>
public sealed class WorkspaceScanner
{
    private readonly string _root;
    private readonly IgnoreRules _ignore;

    public WorkspaceScanner(string root, IEnumerable<string>? extraIgnore = null)
    {
        _root = root;
        _ignore = new IgnoreRules(extraIgnore);
    }

    public WorkspaceSnapshot Scan()
    {
        var snapshot = new WorkspaceSnapshot();
        if (!Directory.Exists(_root))
        {
            return snapshot;
        }

        foreach (var full in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var rel = System.IO.Path.GetRelativePath(_root, full).Replace('\\', '/');
            if (_ignore.IsIgnored(rel))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(full);
                if (!info.Exists)
                {
                    continue;
                }

                snapshot.Entries.Add(new WorkspaceEntry
                {
                    Path = rel,
                    Size = info.Length,
                    MtimeTicks = info.LastWriteTimeUtc.Ticks
                });
            }
            catch
            {
                snapshot.Unreadable.Add(rel);
            }
        }

        snapshot.Entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return snapshot;
    }
}
