using System.IO;
using System.Text.Json;

namespace FvHelper.Desktop;

/// <summary>
/// 记住主窗口的位置与大小，存 <c>%LOCALAPPDATA%\FVHelper\ui-state.json</c>（不写注册表）。
/// 读失败或文件不存在就当没记过，不影响启动。
/// </summary>
public static class UiStateStore
{
    private sealed class Payload
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    private static string FilePath => Path.Combine(AppPaths.LocalDataDir, "ui-state.json");

    public static bool TryLoad(out double left, out double top, out double width, out double height, out bool maximized)
    {
        left = top = 0;
        width = height = 0;
        maximized = false;

        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(FilePath));
            if (payload is null || payload.Width < 400 || payload.Height < 300)
            {
                return false;
            }

            left = payload.Left;
            top = payload.Top;
            width = payload.Width;
            height = payload.Height;
            maximized = payload.Maximized;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("读取窗口位置记忆失败（忽略）", ex);
            return false;
        }
    }

    public static void Save(double left, double top, double width, double height, bool maximized)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LocalDataDir);
            var payload = new Payload { Left = left, Top = top, Width = width, Height = height, Maximized = maximized };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(payload));
        }
        catch (Exception ex)
        {
            AppLog.Error("保存窗口位置记忆失败（忽略）", ex);
        }
    }
}
