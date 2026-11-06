using System.IO;
using System.Text.Json;

namespace FvHelper.Core;

/// <summary>
/// 本地加速缓存：`.fvhelper\state.json`，记录上次提交时每个文件的 (size, mtime, hash)。
/// **它不是历史数据**（不进 `.dat`），只是"扫描时能不能省掉一次全文件哈希"的筛子；
/// 丢了也没关系（下次扫描退回全量哈希），所以读写失败一律降级处理，不影响正确性。
/// </summary>
public sealed class RepoStateCache
{
    private const string FileName = "state.json";

    public sealed class Entry
    {
        public long Size { get; set; }
        public long MtimeTicks { get; set; }
        public string Hash { get; set; } = "";
    }

    private readonly Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _map.Count;

    public Entry? Get(string path) => _map.TryGetValue(path, out var e) ? e : null;

    public void Set(string path, long size, long mtimeTicks, string hash) =>
        _map[path] = new Entry { Size = size, MtimeTicks = mtimeTicks, Hash = hash };

    public static string PathFor(string repoRoot) =>
        Path.Combine(repoRoot, BlobStore.StoreDirName, FileName);

    public static RepoStateCache Load(string repoRoot)
    {
        var cache = new RepoStateCache();
        var path = PathFor(repoRoot);
        if (!File.Exists(path))
        {
            return cache;
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json);
            if (doc is not null)
            {
                foreach (var (k, v) in doc)
                {
                    cache._map[k.Replace('\\', '/')] = v;
                }
            }
        }
        catch
        {
            // 缓存坏了不影响正确性，当成空缓存
        }

        return cache;
    }

    public void Save(string repoRoot)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, BlobStore.StoreDirName));
            var json = JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = false });
            var tmp = PathFor(repoRoot) + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, PathFor(repoRoot), overwrite: true);
        }
        catch
        {
            // 缓存写不进去不影响功能
        }
    }
}
