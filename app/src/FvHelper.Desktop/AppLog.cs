using System.IO;

namespace FvHelper.Desktop;

/// <summary>界面用到的本地目录约定（都在 `%LOCALAPPDATA%\FVHelper` 下，不写注册表）。</summary>
public static class AppPaths
{
    public static readonly string LocalDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FVHelper");
}

/// <summary>
/// 极简界面日志：`%LOCALAPPDATA%\FVHelper\logs\app.log`。
/// 只记录"用户点了什么、结果如何、异常堆栈"，方便排查"为什么没提交/为什么打不开"。
/// 写日志失败绝不影响主流程。
/// </summary>
internal static class AppLog
{
    private static readonly string Dir = Path.Combine(AppPaths.LocalDataDir, "logs");

    private static readonly string FilePath = Path.Combine(Dir, "app.log");

    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex is not null)
                {
                    line += Environment.NewLine + ex;
                }

                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志写不进去不影响使用
        }
    }
}
