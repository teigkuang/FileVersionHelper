using System.IO;
using System.Text;
using System.Windows;

namespace FvHelper.Desktop;

public partial class App : Application
{
    /// <summary>
    /// --selftest：不开窗口，仅检查 OpenFolderDialog 等 WPF API 是否可用，
    /// 结果写到 %TEMP%\fvhelper-selftest.txt，供 P1 自动化验证读取。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest"))
        {
            var sb = new StringBuilder();
            try
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "自检" };
                sb.AppendLine("OpenFolderDialog: OK");
                sb.AppendLine("  FolderName(empty) = [" + dlg.FolderName + "]");
                sb.AppendLine("  Multiselect settable: OK");
            }
            catch (Exception ex)
            {
                sb.AppendLine("OpenFolderDialog: FAIL " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                sb.AppendLine("OSVersion = " + Environment.OSVersion.VersionString);
                sb.AppendLine("ProcessPath = " + Environment.ProcessPath);
            }
            catch { /* 忽略 */ }

            var outPath = Path.Combine(Path.GetTempPath(), "fvhelper-selftest.txt");
            try
            {
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { /* 忽略 */ }

            Shutdown(0);
            return;
        }

        // --scan <目录>：不开窗口，用真实引擎跑一次 status 并写文件，供自动化验证读
        if (e.Args.Length >= 2 && e.Args[0] == "--scan")
        {
            var sb = new StringBuilder();
            var target = e.Args[1];
            sb.AppendLine("target = " + target);

            try
            {
                var repo = Core.Repository.Open(target);
                var status = repo.Status();
                sb.AppendLine("repoId  = " + repo.Index.RepoId);
                sb.AppendLine("head    = " + repo.Index.Head);
                sb.AppendLine("commits = " + repo.Index.Commits.Count);
                sb.AppendLine("health  = " + status.Health);
                sb.AppendLine("files   = " + status.Plan.Tree.Count);
                sb.AppendLine("changes = " + status.Plan.Changes.Count);
                sb.AppendLine("inUse   = " + status.InUse.Count);
                sb.AppendLine("uncertain = " + status.Plan.Uncertain.Count);

                foreach (var change in status.Plan.Changes.OrderBy(c => c.Path, StringComparer.Ordinal))
                {
                    sb.AppendLine($"{change.Action}\t{change.Size}\t{change.Path}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR " + ex.GetType().Name + ": " + ex.Message);
            }

            var scanOut = Path.Combine(Path.GetTempPath(), "fvhelper-scan.txt");
            try
            {
                File.WriteAllText(scanOut, sb.ToString(), new UTF8Encoding(false));
            }
            catch { /* 忽略 */ }

            Shutdown(0);
            return;
        }

        // --msgtest：验证本机是否能正常弹出模态窗口（诊断用，写结果到 %TEMP%\fvhelper-msgtest.txt）
        if (e.Args.Contains("--msgtest"))
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "fvhelper-msgtest.txt"), "before show", new UTF8Encoding(false));
            MessageBox.Show("消息框测试：看到这一句说明模态窗口正常。", "FVHelper 自检");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "fvhelper-msgtest.txt"), "after show", new UTF8Encoding(false));
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
    }
}
