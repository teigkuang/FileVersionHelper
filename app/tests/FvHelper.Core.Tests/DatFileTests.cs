using System.IO;
using FvHelper.Core;

namespace FvHelper.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string prefix = "fvh-test")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Write(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // 测试清理失败不影响结论
        }
    }
}

public class DatFileTests
{
    [Fact]
    public void 往返序列化保留全部字段()
    {
        var doc = new IndexDocument
        {
            RepoId = "repo-1",
            Head = 1,
            Author = "黎广",
            Commits = new List<CommitRecord>
            {
                new()
                {
                    Rev = 1,
                    Id = "abc12345",
                    ParentRev = 0,
                    Message = "初稿",
                    Author = "黎广",
                    CreatedAt = "2026-11-06T19:00:00",
                    Stats = new CommitStats { Files = 2, Added = 2, Bytes = 40 },
                    Changes = new List<ChangeRecord>
                    {
                        new() { Path = "正文/第一章.md", Action = "A", Hash = Hashing.Prefixed(new string('a', 64)), Size = 12 }
                    },
                    Tree = new List<TreeEntry>
                    {
                        new() { Path = "正文/第一章.md", Hash = Hashing.Prefixed(new string('a', 64)), Size = 12 }
                    }
                }
            }
        };

        var bytes = DatFile.Build(doc);
        var result = DatFile.Parse(bytes);

        Assert.Equal(DatStatus.Ok, result.Status);
        Assert.Equal("repo-1", result.Index!.RepoId);
        Assert.Equal(1, result.Index.Head);
        Assert.Single(result.Index.Commits);
        Assert.Equal("正文/第一章.md", result.Index.Commits[0].Tree[0].Path);
        Assert.Equal(12, result.Index.Commits[0].Changes[0].Size);
    }

    [Fact]
    public void 魔数不对判定为损坏()
    {
        var bytes = DatFile.Build(new IndexDocument());
        bytes[0] = (byte)'X';

        var result = DatFile.Parse(bytes);
        Assert.Equal(DatStatus.Corrupt, result.Status);
        Assert.Contains("魔数", result.Error);
    }

    [Fact]
    public void 截断判定为损坏()
    {
        var bytes = DatFile.Build(new IndexDocument());
        var truncated = bytes[..(bytes.Length / 2)];

        var result = DatFile.Parse(truncated);
        Assert.Equal(DatStatus.Corrupt, result.Status);
    }

    [Fact]
    public void 改动载荷一个字节判定校验和不匹配()
    {
        var bytes = DatFile.Build(new IndexDocument { RepoId = "r" });
        var offset = 12 + 1; // 头部之后第一个载荷字节
        bytes[offset] ^= 0xFF;

        var result = DatFile.Parse(bytes);
        Assert.Equal(DatStatus.Corrupt, result.Status);
    }

    [Fact]
    public void 尾标记缺失判定为损坏()
    {
        var bytes = DatFile.Build(new IndexDocument());

        // 保持长度不变，只把最后 4 字节（END!）改成别的
        for (var i = 1; i <= 4; i++)
        {
            bytes[^i] = (byte)'X';
        }

        var result = DatFile.Parse(bytes);
        Assert.Equal(DatStatus.Corrupt, result.Status);
        Assert.Contains("END!", result.Error);
    }

    [Fact]
    public void 缺失文件返回Missing并给出提示()
    {
        using var dir = new TempDir();
        var result = DatFile.Read(dir.Path);

        Assert.Equal(DatStatus.Missing, result.Status);
        Assert.False(result.IsUsable);
    }
}
