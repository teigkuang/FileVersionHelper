using System.IO;
using FvHelper.Core;

namespace FvHelper.Core.Tests;

public class ExportAndVerifyTests
{
    [Fact]
    public void 仅文件导出不含任何版本数据且内容为当时版本()
    {
        using var dir = new TempDir();
        using var outDir = new TempDir("fvh-export");
        dir.Write("正文.md", "v1");
        dir.Write("正文/附录.md", "附录 v1");

        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");
        dir.Write("正文.md", "v2");

        repo = Repository.Open(dir.Path);
        var result = Exporter.Export(repo, 1, new ExportOptions { TargetDirectory = outDir.Path, Mode = ExportMode.FilesOnly });

        Assert.Equal("v1", File.ReadAllText(System.IO.Path.Combine(result.RootPath, "正文.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(result.RootPath, "正文", "附录.md")));
        Assert.False(File.Exists(System.IO.Path.Combine(result.RootPath, ".fvhelper.dat")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(result.RootPath, ".fvhelper")));
    }

    [Fact]
    public void 含数据信息导出后对方可直接继续提交()
    {
        using var dir = new TempDir();
        using var outDir = new TempDir("fvh-share");
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");

        var result = Exporter.Export(Repository.Open(dir.Path), 1,
            new ExportOptions { TargetDirectory = outDir.Path, Mode = ExportMode.WithMeta });

        var shared = Repository.Open(result.RootPath);
        Assert.Equal(1, shared.Index.Head);
        Assert.Equal(repo.Index.RepoId, shared.Index.RepoId);

        File.WriteAllText(System.IO.Path.Combine(result.RootPath, "正文.md"), "v2 另一位同学改的");
        var commit = shared.Commit("继续改", "同学 B");
        Assert.Equal(2, commit.Record.Rev);
        Assert.Equal("同学 B", commit.Record.Author);
    }

    [Fact]
    public void 作为新起点导出时repoId不同且历史只有一条()
    {
        using var dir = new TempDir();
        using var outDir = new TempDir("fvh-new");
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");
        dir.Write("正文.md", "v2");
        repo.Commit("rev2", "黎广");

        var result = Exporter.Export(Repository.Open(dir.Path), 2,
            new ExportOptions { TargetDirectory = outDir.Path, Mode = ExportMode.WithMeta, AsNewStart = true });

        var fresh = Repository.Open(result.RootPath);
        Assert.Single(fresh.Index.Commits);
        Assert.Equal(1, fresh.Index.Head);
        Assert.NotEqual(repo.Index.RepoId, fresh.Index.RepoId);
        Assert.NotEmpty(fresh.Head!.Changes);
        Assert.All(fresh.Head!.Changes, c => Assert.Equal("A", c.Action));
        Assert.Equal(fresh.Head!.Tree.Count, fresh.Head!.Changes.Count);
    }

    [Fact]
    public void 内容缺失时导出整体中止且不留半套()
    {
        using var dir = new TempDir();
        using var outDir = new TempDir("fvh-abort");
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");

        var blob = Directory.EnumerateFiles(System.IO.Path.Combine(dir.Path, ".fvhelper", "objects"), "*", SearchOption.AllDirectories).First();
        File.Delete(blob);

        Assert.Throws<RepositoryException>(() =>
            Exporter.Export(Repository.Open(dir.Path), 1,
                new ExportOptions { TargetDirectory = outDir.Path, Mode = ExportMode.FilesOnly }));

        // 导出目录里不该出现任何新建的子目录
        Assert.Empty(Directory.EnumerateDirectories(outDir.Path));
    }

    [Fact]
    public void 校验四层全过()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");

        var result = Verifier.Check(dir.Path, deepHash: true);
        Assert.Equal(VerifyLevel.Ok, result.Level);
        Assert.Equal(4, result.LayersPassed);
    }

    [Fact]
    public void 全新文件夹不算损坏而是可以开始用()
    {
        using var dir = new TempDir();
        dir.Write("照片.jpg", "v1");

        var result = Verifier.Check(dir.Path);

        Assert.True(result.IsNewRepo);
        Assert.Equal(VerifyLevel.Ok, result.Level);
        Assert.Empty(result.Problems);
        Assert.True(result.CanExport);
    }

    [Fact]
    public void 工作区有未提交改动时是Warn而不是Mismatch()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");
        dir.Write("正文.md", "v2");

        var result = Verifier.Check(dir.Path);
        Assert.Equal(VerifyLevel.Warn, result.Level);
        Assert.True(result.CanExport);
    }

    [Fact]
    public void 容器损坏时是Corrupt且禁止导出()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");

        VersionData.Unhide(DatFile.PathFor(dir.Path));
        File.WriteAllBytes(DatFile.PathFor(dir.Path), new byte[] { 9, 9, 9 });

        var result = Verifier.Check(dir.Path);
        Assert.Equal(VerifyLevel.Corrupt, result.Level);
        Assert.False(result.CanExport);
    }

    [Fact]
    public void 历史版本内容缺失也算Mismatch()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");
        dir.Write("正文.md", "v2");
        repo.Commit("rev2", "黎广");

        // 删掉 rev1 独有的内容
        var head = repo.Head!;
        var oldHash = Hashing.Normalize(head.Tree.First().Hash);
        var blobs = Directory.EnumerateFiles(System.IO.Path.Combine(dir.Path, ".fvhelper", "objects"), "*", SearchOption.AllDirectories).ToList();
        var oldBlob = blobs.First(b => Hashing.Normalize(new FileInfo(b).Name).StartsWith("", StringComparison.Ordinal) && b.Contains(oldHash[..2]));
        File.Delete(oldBlob);

        var result = Verifier.Check(dir.Path);
        Assert.Equal(VerifyLevel.Mismatch, result.Level);
    }

    [Fact]
    public void 版本数据写完自动隐藏()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");

        // 建库当时就该藏起来（用户打开文件夹看不到），提交后仍然保持隐藏
        Assert.True(VersionData.IsHidden(dir.Path));
        repo.Commit("rev1", "黎广");
        Assert.True(VersionData.IsHidden(dir.Path));
        Assert.True((File.GetAttributes(DatFile.PathFor(dir.Path)) & FileAttributes.Hidden) != 0);
        Assert.True((File.GetAttributes(System.IO.Path.Combine(dir.Path, ".fvhelper")) & FileAttributes.Hidden) != 0);

        // 再提交一次也不会因为"替换文件"丢属性
        dir.Write("正文.md", "v2");
        Repository.Open(dir.Path).Commit("rev2", "黎广");
        Assert.True(VersionData.IsHidden(dir.Path));
    }

    [Fact]
    public void 导出目录名区分是否含版本信息()
    {
        using var dirFiles = new TempDir();
        using var dirMeta = new TempDir();
        using var dirNew = new TempDir();
        using var outDir = new TempDir("fvh-name");
        dirFiles.Write("正文.md", "v1");
        dirMeta.Write("正文.md", "v1");
        dirNew.Write("正文.md", "v1");

        var a = Repository.Init(dirFiles.Path, "黎广");
        a.Commit("rev1", "黎广");
        var b = Repository.Init(dirMeta.Path, "黎广");
        b.Commit("rev1", "黎广");
        var c = Repository.Init(dirNew.Path, "黎广");
        c.Commit("rev1", "黎广");

        var files = Exporter.Export(Repository.Open(dirFiles.Path), 1,
            new ExportOptions { TargetDirectory = outDir.Path, Mode = ExportMode.FilesOnly });
        var meta = Exporter.Export(Repository.Open(dirMeta.Path), 1,
            new ExportOptions { TargetDirectory = outDir.Path, Mode = ExportMode.WithMeta });
        var fresh = Exporter.Export(Repository.Open(dirNew.Path), 1,
            new ExportOptions { TargetDirectory = outDir.Path, Mode = ExportMode.WithMeta, AsNewStart = true });

        Assert.EndsWith("_仅文件", System.IO.Path.GetFileName(files.RootPath));
        Assert.EndsWith("_含版本信息", System.IO.Path.GetFileName(meta.RootPath));
        Assert.EndsWith("_含版本信息-新起点", System.IO.Path.GetFileName(fresh.RootPath));

        // 含版本信息的导出目录里，版本数据同样是隐藏的
        Assert.True(VersionData.IsHidden(meta.RootPath));
        Assert.True(VersionData.IsHidden(fresh.RootPath));
    }
}
