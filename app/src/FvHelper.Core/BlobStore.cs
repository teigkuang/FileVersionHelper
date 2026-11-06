using System.IO;

namespace FvHelper.Core;

/// <summary>
/// 内容库：`.fvhelper\objects\&lt;hash前2位&gt;\&lt;hash后62位&gt;`，内容 = 原文件完整字节（不压缩）。
/// 写入走 "tmp → 校验 → 重命名"，已存在同名 blob 直接跳过（内容寻址天然幂等）。
/// </summary>
public sealed class BlobStore
{
    public const string StoreDirName = ".fvhelper";
    private const string ObjectsDirName = "objects";
    private const string TempDirName = "tmp";

    private readonly string _root;

    public BlobStore(string repoRoot)
    {
        _root = Path.Combine(repoRoot, StoreDirName);
    }

    public string RootDirectory => _root;

    public string ObjectsDirectory => Path.Combine(_root, ObjectsDirName);

    public static string BlobRelativePath(string hash)
    {
        var hex = Hashing.Normalize(hash);
        if (hex.Length != 64)
        {
            throw new ArgumentException("哈希必须是 64 位十六进制", nameof(hash));
        }

        return Path.Combine(ObjectsDirName, hex[..2], hex[2..]);
    }

    public string PathFor(string hash) => Path.Combine(_root, BlobRelativePath(hash));

    public bool Exists(string hash) => File.Exists(PathFor(hash));

    /// <summary>
    /// 把源文件内容写入内容库，返回裸 hex 哈希（同时给出字节数）。
    /// 实现：**边读边写临时分片边算哈希**——一次全量 IO 就同时得到"内容 + 哈希"，
    /// 比"先算哈希再拷贝再校验"少读一遍（100MB 文件约省 0.5 s）。
    /// </summary>
    public string PutFile(string sourcePath, out long size, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ObjectsDirectory);
        var tempDir = Path.Combine(_root, TempDirName);
        Directory.CreateDirectory(tempDir);

        var tmp = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".part");
        string hash;
        long written = 0;

        try
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var destination = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    destination.Write(buffer, 0, read);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    written += read;
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            }

            size = written;
            var target = PathFor(hash);

            if (File.Exists(target))
            {
                // 已有同内容 blob，扔掉临时分片即可
                File.Delete(tmp);
                return hash;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(tmp, target);
            return hash;
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try
                {
                    File.Delete(tmp);
                }
                catch
                {
                    // 残留分片由 CleanTemp 清理
                }
            }
        }
    }

    /// <summary>把 blob 内容写到目标文件（导出用）。返回写入内容的哈希。</summary>
    public string CopyToFile(string hash, string destinationPath, CancellationToken ct = default)
    {
        var source = PathFor(hash);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("内容库缺少该版本的文件内容：" + hash, source);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(source, destinationPath, overwrite: true);

        var written = Hashing.HashFile(destinationPath, ct);
        if (!string.Equals(written, Hashing.Normalize(hash), StringComparison.Ordinal))
        {
            throw new IOException("写出后哈希不一致：" + destinationPath);
        }

        return written;
    }

    /// <summary>校验 blob 是否存在且大小匹配（不做全量哈希，打开项目时的第一道筛子）。</summary>
    public bool CheckExistsWithSize(string hash, long expectedSize)
    {
        var path = PathFor(hash);
        if (!File.Exists(path))
        {
            return false;
        }

        return new FileInfo(path).Length == expectedSize;
    }

    /// <summary>全量校验：存在 + 内容哈希 == 文件名里的哈希。导出前必做。</summary>
    public bool VerifyContent(string hash, CancellationToken ct = default)
    {
        var path = PathFor(hash);
        if (!File.Exists(path))
        {
            return false;
        }

        return string.Equals(Hashing.HashFile(path, ct), Hashing.Normalize(hash), StringComparison.Ordinal);
    }

    /// <summary>清理 tmp 目录里的残留分片。</summary>
    public int CleanTemp()
    {
        var tempDir = Path.Combine(_root, TempDirName);
        if (!Directory.Exists(tempDir))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(tempDir))
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch
            {
                // 正在被占用的跳过
            }
        }

        return removed;
    }

    /// <summary>整个内容库占用的字节数（含 tmp）。</summary>
    public long TotalBytes()
    {
        if (!Directory.Exists(_root))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // 忽略统计不到的
            }
        }

        return total;
    }
}
