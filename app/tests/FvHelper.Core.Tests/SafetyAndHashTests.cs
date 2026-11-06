using System.IO;
using FvHelper.Core;

namespace FvHelper.Core.Tests;

public class SafetyAndHashTests
{
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/abs/path.txt")]
    [InlineData("C:/windows.txt")]
    [InlineData("C:\\windows.txt")]
    [InlineData("正文/a:b.txt")]
    [InlineData("正文/尾点.")]
    [InlineData("")]
    public void 非法相对路径被拒绝(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => PathSafety.NormalizeRelative(path));
    }

    [Fact]
    public void 反斜杠会被规整成正斜杠()
    {
        Assert.Equal("正文/第一章.md", PathSafety.NormalizeRelative(@"正文\第一章.md"));
        Assert.Equal("正文/第一章.md", PathSafety.NormalizeRelative("./正文/第一章.md"));
    }

    [Fact]
    public void 拼接后越界的路径被拒绝()
    {
        using var dir = new TempDir();
        Assert.ThrowsAny<ArgumentException>(() => PathSafety.CombineInside(dir.Path, "..\\外面.txt"));
        Assert.EndsWith("正文.md", PathSafety.CombineInside(dir.Path, "正文.md"));
    }

    [Fact]
    public void 哈希大小写与前缀归一()
    {
        var hex = Hashing.HashBytes(new byte[] { 1, 2, 3 });
        Assert.Equal(64, hex.Length);
        Assert.Equal(hex, Hashing.Normalize("sha256:" + hex.ToUpperInvariant()));
        Assert.Equal("sha256:" + hex, Hashing.Prefixed(hex));
    }

    [Fact]
    public void 忽略规则匹配文件名与目录()
    {
        var rules = new IgnoreRules(new[] { "草稿/*", "*.bak", "私密/**" });
        Assert.True(rules.IsIgnored(".fvhelper.dat"));
        Assert.True(rules.IsIgnored(".fvhelper/objects/aa/bb"));
        Assert.True(rules.IsIgnored("~$论文.docx"));
        Assert.True(rules.IsIgnored("临时.tmp"));
        Assert.True(rules.IsIgnored("正文/草稿/x.md"));
        Assert.True(rules.IsIgnored("正文/备份.bak"));
        Assert.False(rules.IsIgnored("正文/第一章.md"));
    }

    [Fact]
    public void 被占用文件仍可共享读出哈希()
    {
        using var dir = new TempDir();
        var file = dir.Write("占用.txt", "内容");
        using var exclusive = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var hash = Hashing.HashFile(file);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void blob内容库路径为前两位分层()
    {
        var hash = new string('a', 64);
        var relative = BlobStore.BlobRelativePath(hash);
        Assert.Equal(System.IO.Path.Combine("objects", "aa", new string('a', 62)), relative);
    }
}
