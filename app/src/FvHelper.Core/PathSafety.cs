using System.IO;

namespace FvHelper.Core;

/// <summary>
/// 路径安全：索引里的 path 一律"相对路径 + 正斜杠"，读写时逐条校验（`docs\03` §8）。
/// 拒绝：绝对路径、盘符、`..` 段、Windows 保留字符、控制字符、段尾空格/点、符号链接越界。
/// 不做 `\\?\` 前缀（P1 V2 / I-02）。
/// </summary>
public static class PathSafety
{
    private static readonly char[] InvalidChars = { '<', '>', ':', '"', '|', '?', '*' };

    /// <summary>把用户/索引里的路径规整成"正斜杠相对路径"。不合法时抛 <see cref="ArgumentException"/>。</summary>
    public static string NormalizeRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("路径为空", nameof(path));
        }

        var p = path.Replace('\\', '/').Trim();

        while (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        if (p.StartsWith('/'))
        {
            throw new ArgumentException("不允许绝对路径：" + path, nameof(path));
        }

        if (p.Length >= 2 && p[1] == ':')
        {
            throw new ArgumentException("不允许盘符路径：" + path, nameof(path));
        }

        var segments = p.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                throw new ArgumentException("路径含空段：" + path, nameof(path));
            }

            if (segment is "." or "..")
            {
                throw new ArgumentException("路径不允许包含 . 或 .. 段：" + path, nameof(path));
            }

            if (segment.IndexOfAny(InvalidChars) >= 0)
            {
                throw new ArgumentException("路径含 Windows 保留字符：" + path, nameof(path));
            }

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                throw new ArgumentException("路径段不允许以空格或点结尾：" + path, nameof(path));
            }

            foreach (var c in segment)
            {
                if (char.IsControl(c))
                {
                    throw new ArgumentException("路径含控制字符：" + path, nameof(path));
                }
            }
        }

        return string.Join('/', segments);
    }

    /// <summary>把相对路径拼到仓库根上，并确认结果没跑出根目录（防 zip-slip 类穿越）。</summary>
    public static string CombineInside(string root, string relative)
    {
        var normalized = NormalizeRelative(relative);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));

        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("路径越界：" + relative);
        }

        // 不跟随符号链接/联接：目标已存在且是重解析点时拒绝
        try
        {
            if (File.Exists(full) || Directory.Exists(full))
            {
                var attributes = File.GetAttributes(full);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ArgumentException("路径是符号链接/联接，已跳过：" + relative);
                }
            }
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch
        {
            // 查属性失败（权限等）不阻断，交给后续读写报错
        }

        return full;
    }

    /// <summary>是否是本工具自己的数据文件/目录（永久忽略）。</summary>
    public static bool IsInternalPath(string relative)
    {
        var p = relative.Replace('\\', '/');
        return p.StartsWith(".fvhelper/", StringComparison.OrdinalIgnoreCase)
               || string.Equals(p, ".fvhelper.dat", StringComparison.OrdinalIgnoreCase)
               || string.Equals(p, ".fvhelper.dat.bak", StringComparison.OrdinalIgnoreCase)
               || string.Equals(p, ".fvhelper.dat.tmp", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(".fvhelper", StringComparison.OrdinalIgnoreCase);
    }
}
