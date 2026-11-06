using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FvHelper.Core;

public enum DatStatus
{
    Ok,
    Missing,
    Corrupt,
    UnsupportedSchema
}

public sealed class DatLoadResult
{
    public DatStatus Status { get; init; }
    public IndexDocument? Index { get; init; }
    public string Error { get; init; } = "";
    public bool IsUsable => Status == DatStatus.Ok && Index is not null;
}

/// <summary>
/// `.fvhelper.dat` 二进制容器：`docs\03` §2 的唯一实现。
/// 结构 = 魔数 FVH1(4) + formatVersion(u16) + flags(u16) + payloadLength(u32) + gzip(JSON) + SHA-256(payload 原文) + 尾魔数 END!(4)。
/// 读取时**先**校验上述每一项，任一不符即"损坏"，由调用方决定是否回退 `.bak`。
/// </summary>
public static class DatFile
{
    public const string FileName = ".fvhelper.dat";
    public const string BackupFileName = ".fvhelper.dat.bak";
    public const string TempFileName = ".fvhelper.dat.tmp";

    private const string Magic = "FVH1";
    private const string TailMagic = "END!";
    private const ushort FormatVersion = 1;

    private const ushort FlagGzip = 0x0001;
    private const ushort FlagChecksum = 0x0002;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string PathFor(string repoRoot) => System.IO.Path.Combine(repoRoot, FileName);

    public static string BackupPathFor(string repoRoot) => System.IO.Path.Combine(repoRoot, BackupFileName);

    public static string TempPathFor(string repoRoot) => System.IO.Path.Combine(repoRoot, TempFileName);

    /// <summary>把索引序列化成完整容器字节（含头、压缩载荷、校验、尾）。</summary>
    public static byte[] Build(IndexDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var rawPayload = Encoding.UTF8.GetBytes(json);

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(rawPayload, 0, rawPayload.Length);
        }

        var payload = compressed.ToArray();
        var checksum = Hashing.HashBytes(rawPayload);

        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes(Magic));

        WriteUInt16(output, FormatVersion);
        WriteUInt16(output, (ushort)(FlagGzip | FlagChecksum));
        WriteUInt32(output, (uint)payload.Length);

        output.Write(payload, 0, payload.Length);
        output.Write(Convert.FromHexString(checksum));
        output.Write(Encoding.ASCII.GetBytes(TailMagic));

        return output.ToArray();
    }

    public static DatLoadResult Read(string repoRoot)
    {
        var path = PathFor(repoRoot);
        if (!File.Exists(path))
        {
            return new DatLoadResult
            {
                Status = DatStatus.Missing,
                Error = "未找到 " + FileName + "（这是全新文件夹，可以初始化为新项目）"
            };
        }

        byte[] bytes;
        try
        {
            // 共享读，避免别的进程占着读不出来
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            bytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            return new DatLoadResult { Status = DatStatus.Corrupt, Error = "无法读取 " + FileName + "：" + ex.Message };
        }

        return Parse(bytes);
    }

    /// <summary>从原始字节解析并校验。任何结构性问题都返回 Corrupt 而不是抛异常。</summary>
    public static DatLoadResult Parse(byte[] bytes)
    {
        const int headerLength = 4 + 2 + 2 + 4;
        const int checksumLength = 32;
        const int tailLength = 4;

        if (bytes.Length < headerLength + checksumLength + tailLength)
        {
            return Corrupt($"文件太短（{bytes.Length} 字节），不是有效的版本数据文件");
        }

        if (!AsciiEquals(bytes, 0, Magic))
        {
            return Corrupt("文件头魔数不是 FVH1（可能不是本软件生成，或文件被损坏）");
        }

        var version = ReadUInt16(bytes, 4);
        if (version != FormatVersion)
        {
            return new DatLoadResult
            {
                Status = DatStatus.UnsupportedSchema,
                Error = $"数据格式版本 {version} 不受当前程序支持（当前支持 {FormatVersion}）"
            };
        }

        var flags = ReadUInt16(bytes, 6);
        var payloadLength = (int)ReadUInt32(bytes, 8);

        var expected = headerLength + payloadLength + checksumLength + tailLength;
        if (payloadLength <= 0 || bytes.Length != expected)
        {
            return Corrupt($"长度不符：声明 {payloadLength} 字节载荷，文件实际 {bytes.Length} 字节，应 {expected} 字节");
        }

        if (!AsciiEquals(bytes, bytes.Length - tailLength, TailMagic))
        {
            return Corrupt("文件尾标记 END! 缺失（文件被截断或覆盖写入中断）");
        }

        var payload = new byte[payloadLength];
        Buffer.BlockCopy(bytes, headerLength, payload, 0, payloadLength);

        byte[] raw;
        try
        {
            raw = (flags & FlagGzip) != 0 ? Decompress(payload) : payload;
        }
        catch (Exception ex)
        {
            return Corrupt("载荷解压失败：" + ex.Message);
        }

        if ((flags & FlagChecksum) != 0)
        {
            var stored = new byte[checksumLength];
            Buffer.BlockCopy(bytes, headerLength + payloadLength, stored, 0, checksumLength);
            var computed = Hashing.HashBytes(raw);
            if (!string.Equals(Convert.ToHexString(stored).ToLowerInvariant(), computed, StringComparison.Ordinal))
            {
                return Corrupt("载荷校验和不匹配（内容被修改或写入不完整）");
            }
        }

        IndexDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<IndexDocument>(DecodeUtf8(raw), JsonOptions);
        }
        catch (Exception ex)
        {
            return Corrupt("索引 JSON 解析失败：" + ex.Message);
        }

        if (doc is null)
        {
            return Corrupt("索引内容为空");
        }

        if (!string.Equals(doc.Schema, IndexDocument.SchemaId, StringComparison.Ordinal))
        {
            return new DatLoadResult
            {
                Status = DatStatus.UnsupportedSchema,
                Error = $"索引 schema “{doc.Schema}” 不受支持（期望 {IndexDocument.SchemaId}）"
            };
        }

        return new DatLoadResult { Status = DatStatus.Ok, Index = doc };
    }

    private static DatLoadResult Corrupt(string error) => new() { Status = DatStatus.Corrupt, Error = error };

    private static string DecodeUtf8(byte[] raw)
    {
        // 先去掉可能的 BOM
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
        }

        return Encoding.UTF8.GetString(raw);
    }

    private static byte[] Decompress(byte[] payload)
    {
        using var input = new MemoryStream(payload);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static bool AsciiEquals(byte[] bytes, int offset, string text)
    {
        if (offset + text.Length > bytes.Length)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (bytes[offset + i] != (byte)text[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.Write(BitConverter.GetBytes(value));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.Write(BitConverter.GetBytes(value));
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
}
