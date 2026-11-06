using System.IO;
using System.Text.Json;

namespace FvHelper.Desktop;

/// <summary>
/// 项目列表持久化（`%LOCALAPPDATA%\FVHelper\projects.json`）。只存路径与显示名，
/// 真正的版本数据始终在用户自己的文件夹里（绿色版原则）。
/// </summary>
internal static class ProjectStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FVHelper");

    private static readonly string FilePath = Path.Combine(Dir, "projects.json");

    internal sealed class Entry
    {
        public string Path { get; set; } = "";
        public string? Name { get; set; }
    }

    public static List<Entry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new List<Entry>();
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath), options) ?? new List<Entry>();
        }
        catch
        {
            return new List<Entry>();
        }
    }

    public static void Save(IEnumerable<Entry> entries)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(entries.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 记住项目列表失败不应影响使用
        }
    }
}
