using System.IO;
using System.Text.Json;

namespace FvHelper.Desktop;

/// <summary>
/// 本地设置（版本信息署名、大文件提示阈值、导出默认项）。写 %LOCALAPPDATA%\FVHelper\settings.json，
/// **不写注册表**（绿色版约定）。目录不存在时静默创建；读写失败不影响主流程。
/// </summary>
internal static class AppSettings
{
    private const int DefaultLargeFileMb = 50;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FVHelper");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private sealed class Model
    {
        public string Author { get; set; } = "";
        public int LargeFileWarnMb { get; set; } = DefaultLargeFileMb;
        public bool ExportAsNewStart { get; set; }
    }

    private static Model? _model;

    private static Model Current
    {
        get
        {
            if (_model is null)
            {
                _model = LoadModel();
            }

            return _model;
        }
    }

    /// <summary>当前版本信息署名；未设置过时回退到系统用户名。</summary>
    public static string Author => string.IsNullOrWhiteSpace(Current.Author) ? Environment.UserName : Current.Author;

    public static bool HasAuthor => !string.IsNullOrWhiteSpace(Current.Author);

    /// <summary>"改动文件超过这个大小就在提示条里点一下"的阈值（MB）。</summary>
    public static int LargeFileWarnMb => Current.LargeFileWarnMb <= 0 ? DefaultLargeFileMb : Current.LargeFileWarnMb;

    /// <summary>导出（含版本信息）时"作为新起点"的默认勾选状态（记住上次选择）。</summary>
    public static bool ExportAsNewStart => Current.ExportAsNewStart;

    public static void SetAuthor(string? value)
    {
        Current.Author = (value ?? "").Trim();
        Save();
    }

    public static void SaveSettings(string author, int largeFileWarnMb, bool exportAsNewStart)
    {
        Current.Author = author.Trim();
        Current.LargeFileWarnMb = largeFileWarnMb <= 0 ? DefaultLargeFileMb : largeFileWarnMb;
        Current.ExportAsNewStart = exportAsNewStart;
        Save();
    }

    public static void SetExportAsNewStart(bool value)
    {
        if (Current.ExportAsNewStart == value)
        {
            return;
        }

        Current.ExportAsNewStart = value;
        Save();
    }

    private static Model LoadModel()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new Model();
            }

            var json = File.ReadAllText(FilePath);
            var model = JsonSerializer.Deserialize<Model>(json);
            return model ?? new Model();
        }
        catch
        {
            return new Model();
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 设置写不进去不应阻塞主流程（只读环境/权限异常）
        }
    }
}
