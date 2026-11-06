using System.Security.Cryptography;
using System.Text;

namespace FvHelper.Core;

/// <summary>
/// SHA-256 相关工具。约定：**存储与比较一律用裸 64 位小写十六进制**，
/// 展示时可自行加 `sha256:` 前缀（`docs\03` 的 JSON 里带前缀，写盘与内容库路径不带）。
/// </summary>
public static class Hashing
{
    public const string Prefix = "sha256:";

    /// <summary>把可能带 `sha256:` 前缀的字符串规整成裸 hex（小写）。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var v = value.Trim();
        if (v.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            v = v[Prefix.Length..];
        }

        return v.ToLowerInvariant();
    }

    /// <summary>加 `sha256:` 前缀（写 JSON 用）。</summary>
    public static string Prefixed(string hex) => Prefix + hex;

    public static string HashBytes(byte[] data)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
    }

    /// <summary>
    /// 读文件算哈希。用 `FileShare.ReadWrite | Delete`：文件被 Word/PDF 开着也能读（P1 V3 / I-04）。
    /// </summary>
    public static string HashFile(string path, CancellationToken ct = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>用指定盐/内容算短 id（提交展示用）。</summary>
    public static string ShortId(params object?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            sb.Append(p?.ToString() ?? "").Append('\u001f');
        }

        var hash = HashBytes(Encoding.UTF8.GetBytes(sb.ToString()));
        return hash[..8];
    }
}
