using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace P1Spike;

/// <summary>
/// FVHelper P1 专项验证（V2–V6）。
/// 目的：把 Core 引擎依赖的 Windows 文件系统行为先实测一遍，避免在 P2 里踩坑。
///   V2 长路径（>260 字符）能否读写 / File.Replace
///   V3 文件被占用（Word 打开论文）时的读取与替换
///   V4 SHA-256 哈希性能（100MB 单文件、300×2MB 串行与并行）
///   V5 File.Replace 原子替换的四种场景（存在/不存在/只读/跨盘）
///   V6 FileSystemWatcher 递归监听的事件计数与重复触发情况
/// 运行：dotnet build 后 &lt;runtime&gt;\dotnet P1Spike.dll
/// 结果：控制台 + 同目录 spike-result.txt
/// </summary>
internal static class Program
{
    private static readonly StringBuilder Sb = new();
    private static string Root = "";
    private static readonly string CrossDriveRoot = @"D:\FVHTemp";

    private static void Main()
    {
        Root = Path.Combine(Path.GetTempPath(), "fvhelper-p1");
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
        catch { /* 忽略 */ }
        Directory.CreateDirectory(Root);

        W("=== FVHelper P1 专项验证 ===");
        W("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        W("运行时: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        W("架构: " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture + " / OS: " + Environment.OSVersion.VersionString);
        W("临时根: " + Root);

        Run("V2 长路径", V2);
        Run("V3 文件占用", V3);
        Run("V4 哈希性能", V4);
        Run("V5 原子替换", V5);
        Run("V6 FileSystemWatcher 递归监听", V6);

        W("");
        W("=== 结束 ===");
        var outPath = Path.Combine(AppContext.BaseDirectory, "spike-result.txt");
        try
        {
            File.WriteAllText(outPath, Sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("结果已写入: " + outPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("写结果失败: " + ex.Message);
        }

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
        catch { /* 忽略 */ }
        try
        {
            if (Directory.Exists(CrossDriveRoot))
            {
                Directory.Delete(CrossDriveRoot, true);
            }
        }
        catch { /* 忽略 */ }
    }

    private static void Run(string name, Action action)
    {
        W("");
        W("## " + name);
        try
        {
            action();
        }
        catch (Exception ex)
        {
            W("  未捕获异常: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void W(string line)
    {
        Sb.AppendLine(line);
        Console.WriteLine(line);
    }

    private static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    // ---------- V2 长路径 ----------
    private static void V2()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
                W("  LongPathsEnabled = " + (key?.GetValue("LongPathsEnabled")?.ToString() ?? "(未设置)"));
            }
            catch (Exception ex)
            {
                W("  读注册表失败: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
        else
        {
            W("  非 Windows，跳过注册表检查");
        }

        var deep = Path.Combine(Root, new string('a', 100), new string('b', 100), new string('c', 60));
        try
        {
            Directory.CreateDirectory(deep);
            W("  创建 >260 目录: OK");
        }
        catch (Exception ex)
        {
            W("  创建 >260 目录: 失败 " + ex.GetType().Name + ": " + ex.Message);
            return;
        }

        var file = Path.Combine(deep, "长路径测试.txt");
        W("  目标文件路径长度 = " + file.Length);

        try
        {
            File.WriteAllText(file, "v1");
            W("  普通路径写入: OK");
        }
        catch (Exception ex)
        {
            W("  普通路径写入: 失败 " + ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            File.WriteAllText(@"\\?\" + file, "v2");
            W(@"  \\?\ 前缀写入: OK");
        }
        catch (Exception ex)
        {
            W(@"  \\?\ 前缀写入: 失败 " + ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            W("  FileInfo 读取(普通路径): OK len=" + new FileInfo(file).Length);
        }
        catch (Exception ex)
        {
            W("  FileInfo 读取: 失败 " + ex.GetType().Name);
        }

        var src = Path.Combine(deep, "new.txt");
        try
        {
            File.WriteAllText(src, "new-content");
            File.Replace(src, file, null);
            W("  File.Replace(长路径): OK，替换后长度=" + new FileInfo(file).Length);
        }
        catch (Exception ex)
        {
            W("  File.Replace(长路径): 失败 " + ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            Directory.Delete(Path.Combine(Root, new string('a', 100)), true);
            W("  删除 >260 目录树: OK（含长路径清理可用）");
        }
        catch (Exception ex)
        {
            W("  删除 >260 目录树: 失败 " + ex.GetType().Name);
        }
    }

    // ---------- V3 文件占用 ----------
    private static void V3()
    {
        var dir = Path.Combine(Root, "v3");
        Directory.CreateDirectory(dir);
        var doc = Path.Combine(dir, "论文.docx");
        File.WriteAllText(doc, "content-A");

        using (var fs = new FileStream(doc, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            W("  FileShare.ReadWrite 共享读打开: OK len=" + fs.Length);
            var src = Path.Combine(dir, "replace-src.txt");
            File.WriteAllText(src, "content-B");
            try
            {
                File.Replace(src, doc, null);
                W("  共享读占用中 File.Replace: OK（占用时也能替换，Content=" + File.ReadAllText(doc) + "）");
            }
            catch (Exception ex)
            {
                W("  共享读占用中 File.Replace: 失败（符合预期）" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        using (var fs = new FileStream(doc, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            W("  FileShare.None 独占打开: OK");
            try
            {
                File.ReadAllText(doc);
                W("  独占时普通读取: OK（居然成功）");
            }
            catch (Exception ex)
            {
                W("  独占时普通读取: 失败（符合预期）" + ex.GetType().Name);
            }

            try
            {
                var src2 = Path.Combine(dir, "replace-src2.txt");
                File.WriteAllText(src2, "content-C");
                File.Replace(src2, doc, null);
                W("  独占时 File.Replace: OK（居然成功）");
            }
            catch (Exception ex)
            {
                W("  独占时 File.Replace: 失败（符合预期）" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        var lockFile = Path.Combine(dir, "~$论文.docx");
        File.WriteAllText(lockFile, "lock");
        W("  枚举到 ~$ 锁文件: " + (File.Exists(lockFile) ? "是" : "否") + "（提交时需忽略 ~$* 与 .tmp）");
    }

    // ---------- V4 哈希性能 ----------
    private static void V4()
    {
        var dir = Path.Combine(Root, "v4");
        Directory.CreateDirectory(dir);

        var big = Path.Combine(dir, "big-100mb.bin");
        var buf = new byte[1024 * 1024];
        RandomNumberGenerator.Fill(buf);
        using (var fs = new FileStream(big, FileMode.Create, FileAccess.Write))
        {
            for (var i = 0; i < 100; i++)
            {
                fs.Write(buf, 0, buf.Length);
            }
        }

        var mb = new FileInfo(big).Length / 1024.0 / 1024.0;
        var sw = Stopwatch.StartNew();
        var h = HashFile(big);
        sw.Stop();
        var mbs = sw.ElapsedMilliseconds == 0 ? 0 : mb / (sw.ElapsedMilliseconds / 1000.0);
        W($"  100MB 单文件 SHA-256: {sw.ElapsedMilliseconds} ms（约 {mbs:F0} MB/s）hash={h[..16]}...");

        var many = Path.Combine(dir, "many");
        Directory.CreateDirectory(many);
        var buf2 = new byte[2 * 1024 * 1024];
        RandomNumberGenerator.Fill(buf2);
        for (var i = 0; i < 300; i++)
        {
            File.WriteAllBytes(Path.Combine(many, $"f{i:D3}.bin"), buf2);
        }

        W("  300×2MB（共 600MB）:");

        sw.Restart();
        for (var i = 0; i < 300; i++)
        {
            HashFile(Path.Combine(many, $"f{i:D3}.bin"));
        }
        sw.Stop();
        W($"    串行: {sw.ElapsedMilliseconds} ms");

        foreach (var deg in new[] { 2, 4, 8 })
        {
            sw.Restart();
            Parallel.For(0, 300, new ParallelOptions { MaxDegreeOfParallelism = deg }, i =>
            {
                HashFile(Path.Combine(many, $"f{i:D3}.bin"));
            });
            sw.Stop();
            W($"    并行 {deg} 线程: {sw.ElapsedMilliseconds} ms");
        }

        try
        {
            Directory.Delete(dir, true);
        }
        catch { /* 忽略 */ }
    }

    // ---------- V5 原子替换 ----------
    private static void V5()
    {
        var dir = Path.Combine(Root, "v5");
        Directory.CreateDirectory(dir);

        // A 目标存在，带备份
        var targetA = Path.Combine(dir, "targetA.txt");
        var srcA = Path.Combine(dir, "srcA.txt");
        var bakA = Path.Combine(dir, "targetA.bak");
        File.WriteAllText(targetA, "old");
        File.WriteAllText(srcA, "new");
        try
        {
            File.Replace(srcA, targetA, bakA);
            W("  A 目标存在+备份: OK target=" + File.ReadAllText(targetA) + " backup=" + File.ReadAllText(bakA));
        }
        catch (Exception ex)
        {
            W("  A 目标存在+备份: 失败 " + ex.GetType().Name + ": " + ex.Message);
        }

        // B 目标不存在
        var targetB = Path.Combine(dir, "targetB.txt");
        var srcB = Path.Combine(dir, "srcB.txt");
        File.WriteAllText(srcB, "newB");
        try
        {
            File.Replace(srcB, targetB, null);
            W("  B 目标不存在 File.Replace: OK（意外，说明 Replace 会创建目标）");
        }
        catch (Exception ex)
        {
            W("  B 目标不存在 File.Replace: 失败（符合预期）" + ex.GetType().Name);
            try
            {
                File.Move(srcB, targetB);
                W("  B 回退 File.Move: OK（首次提交走这条路径）");
            }
            catch (Exception ex2)
            {
                W("  B 回退 File.Move 也失败: " + ex2.GetType().Name);
            }
        }

        // C 目标只读
        var targetC = Path.Combine(dir, "targetC.txt");
        var srcC = Path.Combine(dir, "srcC.txt");
        File.WriteAllText(targetC, "oldC");
        File.SetAttributes(targetC, FileAttributes.ReadOnly);
        File.WriteAllText(srcC, "newC");
        try
        {
            File.Replace(srcC, targetC, null);
            W("  C 只读目标 File.Replace: OK（说明只读属性不阻止替换）");
        }
        catch (Exception ex)
        {
            W("  C 只读目标 File.Replace: 失败 " + ex.GetType().Name + "（恢复属性后重试）");
            try
            {
                File.SetAttributes(targetC, FileAttributes.Normal);
                File.Replace(srcC, targetC, null);
                W("  C 清属性后重试: OK");
            }
            catch (Exception ex2)
            {
                W("  C 重试仍失败: " + ex2.GetType().Name);
            }
        }

        // D 跨盘
        try
        {
            Directory.CreateDirectory(CrossDriveRoot);
            var targetD = Path.Combine(CrossDriveRoot, "targetD.txt");
            var srcC2 = Path.Combine(dir, "srcD.txt");
            File.WriteAllText(targetD, "oldD");
            File.WriteAllText(srcC2, "newD");
            try
            {
                File.Replace(srcC2, targetD, null);
                W("  D 跨盘(C:→D:) File.Replace: OK（意外，支持跨盘）");
            }
            catch (Exception ex)
            {
                W("  D 跨盘(C:→D:) File.Replace: 失败（符合预期）" + ex.GetType().Name + ": " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            W("  D 跨盘测试准备失败: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // ---------- V6 FileSystemWatcher ----------
    private static void V6()
    {
        var dir = Path.Combine(Root, "v6");
        Directory.CreateDirectory(dir);

        var gate = new object();
        var counts = new Dictionary<WatcherChangeTypes, int>();
        var total = 0;

        void Bump(WatcherChangeTypes type)
        {
            lock (gate)
            {
                total++;
                counts[type] = (counts.TryGetValue(type, out var v) ? v : 0) + 1;
            }
        }

        using var fsw = new FileSystemWatcher(dir);
        fsw.IncludeSubdirectories = true;
        fsw.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
        fsw.InternalBufferSize = 64 * 1024;

        fsw.Created += (_, e) => Bump(e.ChangeType);
        fsw.Changed += (_, e) => Bump(e.ChangeType);
        fsw.Deleted += (_, e) => Bump(e.ChangeType);
        fsw.Renamed += (_, e) => Bump(e.ChangeType);
        fsw.Error += (_, e) => W("  watcher 内部缓冲溢出(Error 事件): " + e.GetException().GetType().Name);

        fsw.EnableRaisingEvents = true;

        // 30 新建
        for (var i = 0; i < 30; i++)
        {
            File.WriteAllText(Path.Combine(dir, $"c{i:D2}.txt"), "x");
            Thread.Sleep(40);
        }
        // 15 修改
        for (var i = 0; i < 15; i++)
        {
            File.AppendAllText(Path.Combine(dir, $"c{i:D2}.txt"), "+");
            Thread.Sleep(40);
        }
        // 10 删除
        for (var i = 15; i < 25; i++)
        {
            File.Delete(Path.Combine(dir, $"c{i:D2}.txt"));
            Thread.Sleep(40);
        }
        // 3 改名
        for (var i = 25; i < 28; i++)
        {
            File.Move(Path.Combine(dir, $"c{i:D2}.txt"), Path.Combine(dir, $"r{i:D2}.txt"));
            Thread.Sleep(40);
        }

        Thread.Sleep(2000);
        fsw.EnableRaisingEvents = false;

        W("  期望: Created=30 Changed=15 Deleted=10 Renamed=3（合计 58 次操作）");
        W($"  实测: Created={counts.GetValueOrDefault(WatcherChangeTypes.Created)} " +
          $"Changed={counts.GetValueOrDefault(WatcherChangeTypes.Changed)} " +
          $"Deleted={counts.GetValueOrDefault(WatcherChangeTypes.Deleted)} " +
          $"Renamed={counts.GetValueOrDefault(WatcherChangeTypes.Renamed)}（事件总数 {total}）");
        W("  说明: Changed 通常多于操作次数（写入会同时触发 Size/LastWrite），实际实现需按路径去抖合并");
    }
}
