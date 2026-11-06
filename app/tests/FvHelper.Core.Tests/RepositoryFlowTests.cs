using System.IO;
using FvHelper.Core;

namespace FvHelper.Core.Tests;

public class RepositoryFlowTests
{
    [Fact]
    public void 首次提交后状态为空且历史有一条()
    {
        using var dir = new TempDir();
        dir.Write("论文正文.docx", "v1");
        dir.Write("正文/综述.md", "# 综述");

        var repo = Repository.Init(dir.Path, "黎广");
        var result = repo.Commit("初稿", "黎广");

        Assert.Equal(1, result.Record.Rev);
        Assert.Equal(2, result.Record.Stats.Added);
        Assert.Equal(2, result.Record.Stats.Files);

        repo = Repository.Open(dir.Path);
        var status = repo.Status();
        Assert.Empty(status.Plan.Changes);
        Assert.Single(repo.History());
        Assert.Equal("初稿", repo.Head!.Message);
    }

    [Fact]
    public void 全新文件夹首次提交自动建库不报不一致()
    {
        // 回归：之前 Commit 无条件跑 L1–L3，「没有 .fvhelper.dat」被判定为 CORRUPT → 新文件夹永远提交不了
        using var dir = new TempDir();
        dir.Write("readme.txt", "新项目第一份内容");

        Assert.False(Repository.HasRepository(dir.Path));

        var repo = Repository.Open(dir.Path);
        Assert.Equal(RepoHealth.EmptyNoCommits, repo.Health);

        var result = repo.Commit("初始化", "黎广");

        Assert.Equal(1, result.Record.Rev);
        Assert.Equal(1, result.Record.Stats.Added);
        Assert.True(File.Exists(Repository.DatPathFor(dir.Path)));

        var reopened = Repository.Open(dir.Path);
        Assert.Empty(reopened.Status().Plan.Changes);
        Assert.Single(reopened.History());
        Assert.Equal(VerifyLevel.Ok, Verifier.Check(dir.Path, deepHash: true, checkWorkspace: true).Level);
    }

    [Fact]
    public void 修改新增删除改名都能识别()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        dir.Write("综述.md", "综述 v1");
        dir.Write("历史.md", "会被删掉");

        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("初稿", "黎广");

        dir.Write("正文.md", "v2 改了");
        dir.Write("方法.md", "新增的");
        File.Delete(System.IO.Path.Combine(dir.Path, "历史.md"));
        File.Move(System.IO.Path.Combine(dir.Path, "综述.md"), System.IO.Path.Combine(dir.Path, "综述-改名.md"));

        var status = Repository.Open(dir.Path).Status();
        var actions = status.Plan.Changes.Select(c => c.Action).OrderBy(a => a).ToList();

        Assert.Equal(new[] { "A", "D", "M", "R" }, actions);
        Assert.Contains(status.Plan.Changes, c => c.Action == "R" && c.From == "综述.md" && c.Path == "综述-改名.md");
    }

    [Fact]
    public void 只改时间不改内容不算修改()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "同样的内容");

        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("初稿", "黎广");

        // 只动 mtime，内容不变
        var file = System.IO.Path.Combine(dir.Path, "正文.md");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));

        var status = Repository.Open(dir.Path).Status();
        Assert.Empty(status.Plan.Changes);
    }

    [Fact]
    public void 忽略规则生效不把锁文件和临时文件算进去()
    {
        using var dir = new TempDir();
        dir.Write("正文.docx", "v1");
        dir.Write("~$正文.docx", "word 锁文件");
        dir.Write("临时.tmp", "tmp");
        dir.Write("Thumbs.db", "x");
        dir.Write("desktop.ini", "x");

        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("初稿", "黎广");

        var tree = Repository.Open(dir.Path).Head!.Tree.Select(t => t.Path).ToList();
        Assert.Single(tree);
        Assert.Equal("正文.docx", tree[0]);
    }

    [Fact]
    public void 提交只带选定路径时其它改动保持未提交()
    {
        using var dir = new TempDir();
        dir.Write("a.md", "1");
        dir.Write("b.md", "1");

        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("初稿", "黎广");

        dir.Write("a.md", "2");
        dir.Write("b.md", "2");

        var repo2 = Repository.Open(dir.Path);
        var result = repo2.Commit("只提交 a", "黎广", new[] { "a.md" });
        Assert.Equal(1, result.Record.Stats.Modified);

        var status = Repository.Open(dir.Path).Status();
        Assert.Single(status.Plan.Changes);
        Assert.Equal("b.md", status.Plan.Changes[0].Path);
    }

    [Fact]
    public void 内容库缺失时禁止提交()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("初稿", "黎广");

        var blob = Directory.EnumerateFiles(System.IO.Path.Combine(dir.Path, ".fvhelper", "objects"), "*", SearchOption.AllDirectories).First();
        File.Delete(blob);
        dir.Write("正文.md", "v2");

        var reopened = Repository.Open(dir.Path);
        Assert.Throws<RepositoryException>(() => reopened.Commit("不该成功", "黎广"));
    }

    [Fact]
    public void 主文件损坏时从备份恢复()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");
        dir.Write("正文.md", "v2");
        repo.Commit("rev2", "黎广");

        // 主文件截断，.bak 里是 rev1 的状态。版本数据默认带 Hidden，覆盖写前先解除（Windows 的 CREATE_ALWAYS 语义）
        var dat = DatFile.PathFor(dir.Path);
        var bytes = File.ReadAllBytes(dat);
        VersionData.Unhide(dat);
        File.WriteAllBytes(dat, bytes[..(bytes.Length / 2)]);

        var recovered = Repository.Open(dir.Path);
        Assert.Equal(RepoHealth.RecoveredFromBackup, recovered.Health);
        Assert.Equal(1, recovered.Index.Head);
        Assert.Contains("备份", recovered.Notice);
    }

    [Fact]
    public void 双损坏时打开应报错而不是静默()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");

        VersionData.Unhide(DatFile.PathFor(dir.Path));
        VersionData.Unhide(DatFile.BackupPathFor(dir.Path));
        File.WriteAllBytes(DatFile.PathFor(dir.Path), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(DatFile.BackupPathFor(dir.Path), new byte[] { 1, 2, 3 });

        Assert.Throws<RepositoryException>(() => Repository.Open(dir.Path));
    }

    [Fact]
    public void 提交后dat能重复读取且头部一致()
    {
        using var dir = new TempDir();
        dir.Write("正文.md", "v1");
        var repo = Repository.Init(dir.Path, "黎广");
        repo.Commit("rev1", "黎广");
        dir.Write("正文.md", "v2");
        repo.Commit("rev2", "黎广");

        var load = DatFile.Read(dir.Path);
        Assert.True(load.IsUsable);
        Assert.Equal(2, load.Index!.Head);
        Assert.Equal(2, load.Index.Commits.Count);
        Assert.True(File.Exists(DatFile.BackupPathFor(dir.Path)));
    }
}
