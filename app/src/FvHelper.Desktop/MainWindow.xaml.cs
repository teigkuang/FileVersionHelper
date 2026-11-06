using FvHelper.Core;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FvHelper.Desktop;

/// <summary>
/// 主窗口：左＝项目列表 / 中＝两态（项目文件视图 ↔ 提交详情）/ 右＝该项目 changelist。
/// 所有版本操作都走 <see cref="FvHelper.Core.Repository"/>，界面自己不留任何状态副本。
/// </summary>
public partial class MainWindow : Window
{
    private const string MessagePlaceholder = "填写本次修改标题，例如：按导师意见改结论与图表编号";

    private readonly List<ProjectItem> _projects = new();

    private Repository? _repo;
    private StatusResult? _status;
    private VerifyResult? _integrity;
    private CommitRecord? _selectedCommit;

    private bool _suppressCommitSelection;
    private bool _isPlaceholder = true;
    private bool _busy;
    private bool _suppressMessageChanged;

    private DateTime _lastAutoScanAt = DateTime.MinValue;
    private int _scanGeneration;

    /// <summary>两次自动扫描的最小间隔：窗口反复获得焦点时不至于每次都全盘枚举。</summary>
    private static readonly TimeSpan AutoScanInterval = TimeSpan.FromSeconds(2);

    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x2F, 0xA3, 0x6B));
    private static readonly SolidColorBrush Orange = new(Color.FromRgb(0xD9, 0x77, 0x06));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly SolidColorBrush Blue = new(Color.FromRgb(0x3B, 0x72, 0xF6));
    private static readonly SolidColorBrush Grey = new(Color.FromRgb(0x8A, 0x90, 0x99));
    private static readonly SolidColorBrush Faint = new(Color.FromRgb(0xB0, 0xB6, 0xBE));

    public MainWindow()
    {
        InitializeComponent();

        RestoreWindowState();
        LoadProjects();

        Activated += OnWindowActivated;
        Closing += OnWindowClosing;
        UpdateCommitButton();
    }

    // ================= 窗口位置记忆 =================

    private void RestoreWindowState()
    {
        if (!UiStateStore.TryLoad(out var left, out var top, out var width, out var height, out var maximized))
        {
            return;
        }

        // 换过显示器/改过分辨率时，旧坐标可能整块落在屏幕外：先夹回可见区域
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        width = Math.Min(width, virtualRight - virtualLeft);
        height = Math.Min(height, virtualBottom - virtualTop);
        left = Math.Clamp(left, virtualLeft, Math.Max(virtualLeft, virtualRight - width));
        top = Math.Clamp(top, virtualTop, Math.Max(virtualTop, virtualBottom - height));

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        if (maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        UiStateStore.Save(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            WindowState == WindowState.Maximized);
    }

    // ================= 快捷键 =================

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (e.Key == Key.O && ctrl)
        {
            e.Handled = true;
            OnAddProject(sender, new RoutedEventArgs());
            return;
        }

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            _ = ScanAsync(auto: false);
            return;
        }

        if (e.Key == Key.F2 && ProjectList.IsKeyboardFocusWithin && CommitPanel.Visibility != Visibility.Visible)
        {
            e.Handled = true;
            RenameSelectedProject();
            return;
        }

        if (e.Key == Key.E && ctrl && CommitPanel.Visibility == Visibility.Visible && _selectedCommit is not null)
        {
            e.Handled = true;
            OnExportFiles(sender, new RoutedEventArgs());
            return;
        }

        if (e.Key == Key.C && ctrl && CommitPanel.Visibility == Visibility.Visible
            && Keyboard.FocusedElement is not TextBox)
        {
            e.Handled = true;
            CopyChangeList();
            return;
        }

        if (e.Key == Key.Escape && CommitPanel.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            OnBackToProject(sender, new RoutedEventArgs());
        }
    }

    // ================= 项目 =================

    private void LoadProjects()
    {
        var saved = ProjectStore.Load();
        foreach (var entry in saved)
        {
            var path = entry.Path;
            if (string.IsNullOrWhiteSpace(path) || _projects.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _projects.Add(new ProjectItem
            {
                Name = string.IsNullOrWhiteSpace(entry.Name) ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) : entry.Name!,
                Path = path
            });
        }

        ProjectList.ItemsSource = _projects;
        if (_projects.Count > 0)
        {
            ProjectList.SelectedIndex = 0;
        }
        else
        {
            ShowEmptyState();
        }

        RefreshAuthorText();
    }

    private void ShowEmptyState()
    {
        PathText.Text = "未选择项目";
        ProjectTitle.Text = "还没有项目";
        ScanStatus.Text = "点左栏右上角的 ＋，选择一个论文文件夹开始";
        ScanStatus.Foreground = Grey;
        EmptyHint.Text = "第一个项目建议选你正在写的论文文件夹。\n如果里面已有版本信息（隐藏文件，资源管理器里看不到），会直接读出历次提交；\n如果是全新文件夹，第一次提交时自动建立版本库。";
        EmptyHint.Visibility = Visibility.Visible;
        FileListView.ItemsSource = null;
        CommitList.ItemsSource = null;
        CommitCount.Text = "0 条";
        ChangedBar.Visibility = Visibility.Collapsed;
        WholeIntegrityBar.Visibility = Visibility.Collapsed;
        ProjectPanel.Visibility = Visibility.Visible;
        CommitPanel.Visibility = Visibility.Collapsed;
        UpdateCommitButton();
    }

    private void OnAddProject(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "添加项目：选择一个文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = dialog.FolderName.TrimEnd(Path.DirectorySeparatorChar);
        AddProjectFromPath(path);
    }

    /// <summary>把一个文件夹加进左栏列表（＋ 按钮、拖拽进来都走这里）。</summary>
    private void AddProjectFromPath(string path)
    {
        path = path.TrimEnd(Path.DirectorySeparatorChar);
        var existing = _projects.FirstOrDefault(p =>
            string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            MessageBox.Show(this, "该项目已在列表中。", "FVHelper", MessageBoxButton.OK, MessageBoxImage.Information);
            ProjectList.SelectedItem = existing;
            return;
        }

        var item = new ProjectItem
        {
            Name = Path.GetFileName(path),
            Path = path
        };

        _projects.Add(item);
        ProjectList.Items.Refresh();
        SaveProjects();
        ProjectList.SelectedItem = item;
    }

    /// <summary>拖拽：只接受一个文件夹（拖文件进来不处理）。</summary>
    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        var path = DraggedFolder(e);
        e.Effects = path is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        var path = DraggedFolder(e);
        e.Handled = true;
        if (path is null)
        {
            return;
        }

        AddProjectFromPath(path);
        AppLog.Info("拖拽添加项目：" + path);
    }

    /// <summary>取出拖进来的那个文件夹；不是"单个文件夹"就返回 null。</summary>
    private static string? DraggedFolder(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } items)
        {
            return null;
        }

        var first = items[0];
        return Directory.Exists(first) ? first.TrimEnd(Path.DirectorySeparatorChar) : null;
    }

    private void SaveProjects() =>
        ProjectStore.Save(_projects.Select(p => new ProjectStore.Entry { Path = p.Path, Name = p.Name }));

    /// <summary>从项目列表里移除（只删列表条目，绝不删文件夹内容）。</summary>
    private void OnRemoveProject(object sender, RoutedEventArgs e)
    {
        if (ProjectList.SelectedItem is not ProjectItem project)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"从列表里移除「{project.Name}」？\n\n" +
            $"只删列表里的这一条，不会碰这个文件夹里的任何文件（{project.Path}）。\n" +
            "以后想再打开，点 ＋ 重新选这个文件夹即可。",
            "移除项目", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var index = _projects.IndexOf(project);
        _projects.Remove(project);
        SaveProjects();
        AppLog.Info($"移除项目：{project.Path}");

        ProjectList.Items.Refresh();
        if (_projects.Count == 0)
        {
            _repo = null;
            _status = null;
            _integrity = null;
            _selectedCommit = null;
            ShowEmptyState();
            return;
        }

        ProjectList.SelectedIndex = Math.Min(index, _projects.Count - 1);
    }

    /// <summary>重命名项目（只改列表里显示的名字，不动文件夹、不动任何数据）。入口在中栏标题旁。</summary>
    private void OnRenameProject(object sender, RoutedEventArgs e) => RenameSelectedProject();

    private void RenameSelectedProject()
    {
        if (ProjectList.SelectedItem is not ProjectItem project)
        {
            return;
        }

        var dialog = new PromptWindow
        {
            Owner = this,
            WindowTitle = "重命名项目",
            Label = "列表里显示的名字（只影响本软件里的显示，不改文件夹名）",
            Value = project.Name
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var name = dialog.Value.Trim();
        if (name.Length == 0)
        {
            return;
        }

        project.Name = name;
        SaveProjects();
        ProjectList.Items.Refresh();
        if (ProjectTitle.Text.Length > 0)
        {
            ProjectTitle.Text = name;
        }

        AppLog.Info($"重命名项目：{project.Path} → {name}");
    }

    private void OnProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectList.SelectedItem is ProjectItem project)
        {
            OpenProject(project);
        }
    }

    private void OpenProject(ProjectItem project)
    {
        _suppressCommitSelection = true;
        _scanGeneration++;
        _status = null;
        _integrity = null;
        _selectedCommit = null;
        _repo = null;
        WholeIntegrityBar.Visibility = Visibility.Collapsed;

        PathText.Text = project.Path;
        ProjectTitle.Text = project.Name;
        FileListView.ItemsSource = project.Files;
        CommitList.ItemsSource = project.Commits;
        CommitList.SelectedIndex = -1;
        CommitCount.Text = project.Commits.Count == 0 ? "0 条" : $"{project.Commits.Count} 条";
        ProjectPanel.Visibility = Visibility.Visible;
        CommitPanel.Visibility = Visibility.Collapsed;
        _suppressCommitSelection = false;

        try
        {
            _repo = Repository.Open(project.Path);
            project.HasRepository = Repository.HasRepository(project.Path);
            project.Broken = false;
            LoadCommits(project);
            EmptyHint.Visibility = Visibility.Collapsed;
            _ = ScanAsync(auto: true);
        }
        catch (DirectoryNotFoundException)
        {
            MarkProjectBroken(project, "文件夹不在了（可能被移动或删除）");
        }
        catch (Exception ex)
        {
            project.Broken = true;
            MarkProjectBroken(project, ex.Message);
        }
    }

    /// <summary>项目打不开时把左栏那一条标红，并在中栏说明怎么办（移除后重新添加，或把文件夹放回原位）。</summary>
    private void MarkProjectBroken(ProjectItem project, string reason)
    {
        AppLog.Error($"项目不可用：{project.Path} — {reason}");
        project.Broken = true;
        project.LastCommit = "路径失效 —— 可从列表移除后重新添加";
        project.LastCommitBrush = Red;
        ProjectList.Items.Refresh();

        PathText.Text = project.Path;
        ProjectTitle.Text = project.Name;
        ScanStatus.Foreground = Red;
        ScanStatus.Text = "项目不可用：" + reason;
        EmptyHint.Text = "这个项目现在打不开。\n把文件夹放回原来的位置，或者点列表上方的「－」从列表移除（不会删任何文件），\n再用「＋」把这个文件夹重新加进来。";
        EmptyHint.Visibility = Visibility.Visible;
        FileListView.ItemsSource = null;
        CommitList.ItemsSource = null;
        CommitCount.Text = "0 条";
        ChangedBar.Visibility = Visibility.Collapsed;
        _status = null;
        UpdateCommitButton();
    }

    private void LoadCommits(ProjectItem project)
    {
        project.Commits.Clear();
        if (_repo is not null)
        {
            foreach (var record in _repo.History())
            {
                project.Commits.Add(new CommitItem(record));
            }
        }

        CommitList.Items.Refresh();
        CommitCount.Text = project.Commits.Count == 0 ? "0 条" : $"{project.Commits.Count} 条";
        if (_repo is not null)
        {
            project.LastCommit = _repo.Index.Commits.Count == 0
                ? "尚无提交记录"
                : $"上次提交 rev {_repo.Index.Head} · {_repo.Head!.CreatedAt[5..].Replace('T', ' ')}";
            project.LastCommitBrush = Faint;
        }
    }

    // ================= 扫描 =================

    private async Task ScanAsync(bool auto)
    {
        if (_repo is null || ProjectList.SelectedItem is not ProjectItem project || _busy)
        {
            return;
        }

        var generation = ++_scanGeneration;
        var repo = _repo;
        SetBusy(true);
        var prefix = auto ? "上次扫描（自动）" : "上次扫描（手动）";

        StatusResult? result = null;
        VerifyResult? integrity = null;
        string? error = null;
        var startedAt = DateTime.Now;

        await Task.Run(() =>
        {
            try
            {
                result = repo.Status();
                integrity = Verifier.Check(repo.Root);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        });

        SetBusy(false);

        if (generation != _scanGeneration || ProjectList.SelectedItem != project)
        {
            return;
        }

        if (error is not null)
        {
            AppLog.Error("扫描失败：" + error);
            ScanStatus.Text = "扫描失败：" + error;
            ScanStatus.Foreground = Red;
            ShowError("扫描失败", error);
            return;
        }

        _status = result!;
        _integrity = integrity;
        BuildRows(project, result!);
        UpdateChangedBar(result!);
        UpdateWholeIntegrity(integrity!);
        UpdateScanStatus(prefix, startedAt, result!);
        UpdateCommitButton();
    }

    /// <summary>把"工作区全部文件 + 改动动作"铺成表格行；改动行带勾选框，默认全选。</summary>
    private void BuildRows(ProjectItem project, StatusResult status)
    {
        var plan = status.Plan;
        var changeByPath = new Dictionary<string, ChangeRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in plan.Changes)
        {
            changeByPath[change.Path] = change;
        }

        var rows = new List<FileRow>();
        foreach (var entry in plan.Tree)
        {
            var change = changeByPath.TryGetValue(entry.Path, out var found) ? found : null;
            rows.Add(new FileRow
            {
                Name = entry.Path,
                Path = entry.Path,
                Action = change?.Action,
                IsChanged = change is not null,
                IsSelected = change is not null,
                Status = StatusText(change?.Action),
                Size = FormatSize(entry.Size),
                SizeBytes = entry.Size,
                Time = change?.Mtime is null ? TryFormatMtime(project.Path, entry.Path) : FormatTime(change.Mtime),
                StatusBrush = StatusBrush(change?.Action)
            });
        }

        foreach (var change in plan.Changes.Where(c => c.Action == "D"))
        {
            rows.Add(new FileRow
            {
                Name = change.Path,
                Path = change.Path,
                Action = "D",
                IsChanged = true,
                IsSelected = true,
                Status = "删除",
                Size = FormatSize(change.Size),
                SizeBytes = change.Size,
                Time = "",
                StatusBrush = Red
            });
        }

        rows.Sort((a, b) =>
        {
            var byStatus = StatusOrder(a.Status).CompareTo(StatusOrder(b.Status));
            return byStatus != 0 ? byStatus : string.CompareOrdinal(a.Name, b.Name);
        });

        project.Files.Clear();
        project.Files.AddRange(rows);
        FileListView.Items.Refresh();

        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Count == 0)
        {
            EmptyHint.Text = "这个文件夹里还没有可纳入版本管理的文件。\n（软件会自动跳过 ~$ 临时文件、.tmp、Thumbs.db 等）";
        }
    }

    private void UpdateChangedBar(StatusResult status)
    {
        var plan = status.Plan;
        var changed = plan.Changes.Count;
        ChangedBar.Visibility = changed > 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectAllButton.IsEnabled = changed > 0;
        SelectNoneButton.IsEnabled = changed > 0;

        if (changed == 0)
        {
            ChangedText.Text = "✓ 与上次提交一致，没有需要提交的改动。";
            return;
        }

        var selected = SelectedChanges().Count;
        ChangedText.Text = $"检测到 {changed} 个文件有改动（修改 {plan.Stats.Modified} / 新增 {plan.Stats.Added} / " +
                           $"删除 {plan.Stats.Deleted} / 改名 {plan.Stats.Renamed}），已选 {selected} 个待提交。";

        // 大文件提醒（阈值可在设置里改，不影响提交）
        var threshold = AppSettings.LargeFileWarnMb;
        var big = FileListView.Items.OfType<FileRow>().Count(f => f.IsChanged && f.SizeBytes >= threshold * 1024L * 1024);
        if (big > 0)
        {
            ChangedText.Text += $" 其中 {big} 个文件超过 {threshold} MB，提交和导出会慢一些。";
        }
    }

    private ProjectItem? SelectedProject => ProjectList.SelectedItem as ProjectItem;

    private List<FileRow> SelectedChangeRows()
    {
        if (ProjectList.SelectedItem is not ProjectItem project)
        {
            return new List<FileRow>();
        }

        return project.Files.Where(f => f.IsChanged && f.IsSelected).ToList();
    }

    private List<string> SelectedChanges() => SelectedChangeRows().Select(f => f.Path).ToList();

    private void OnSelectAllChanges(object sender, RoutedEventArgs e) => SetAllChangedSelected(true);

    private void OnSelectNoChanges(object sender, RoutedEventArgs e) => SetAllChangedSelected(false);

    private void SetAllChangedSelected(bool selected)
    {
        if (ProjectList.SelectedItem is not ProjectItem project)
        {
            return;
        }

        foreach (var row in project.Files.Where(f => f.IsChanged))
        {
            row.IsSelected = selected;
        }

        FileListView.Items.Refresh();
        if (_status is not null)
        {
            UpdateChangedBar(_status);
        }

        UpdateCommitButton();
    }

    /// <summary>勾选状态显式写回模型（不依赖 TwoWay 绑定；键盘、鼠标、自动化切换都走这里）。</summary>
    private void OnChangeCheckToggled(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box && box.DataContext is FileRow row)
        {
            row.IsSelected = box.IsChecked == true;
        }

        if (_status is not null && !_busy)
        {
            UpdateChangedBar(_status);
        }

        UpdateCommitButton();
    }

    /// <summary>整库自检的四态呈现（`docs\03` §9 / D-13）。</summary>
    private void UpdateWholeIntegrity(VerifyResult integrity)
    {
        WholeIntegrityBar.Visibility = Visibility.Visible;

        switch (integrity.Level)
        {
            case VerifyLevel.Ok when integrity.IsNewRepo:
                SetWholeBar("· 这是全新文件夹：还没有版本信息，第一次提交时自动建立（现在没有可校验的版本记录）",
                    "#F7F9FC", "#E6E8EB", Grey);
                break;
            case VerifyLevel.Ok:
                SetWholeBar("✓ 版本信息自检通过（容器 / 索引 / 内容库 / 工作区 四层一致）",
                    "#F0F7F2", "#CBE4D6", Green);
                break;
            case VerifyLevel.Warn:
                SetWholeBar("· 版本信息完整；工作区与最新提交有差异（正常，等你提交）",
                    "#F7F9FC", "#E6E8EB", Grey);
                break;
            case VerifyLevel.Mismatch:
                SetWholeBar("✗ 数据与内容不匹配，已禁止提交与导出：\n" + string.Join("\n", integrity.Problems),
                    "#FDF1F1", "#F3C6C6", Red);
                break;
            default:
                SetWholeBar("✗ 版本信息损坏（版本信息文件读不出来），已禁止提交与导出：\n" + string.Join("\n", integrity.Problems),
                    "#FBEAEA", "#EFB4B4", Red);
                break;
        }
    }

    private void SetWholeBar(string text, string background, string border, Brush foreground)
    {
        WholeIntegrityBar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
        WholeIntegrityBar.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
        WholeIntegrityText.Text = text;
        WholeIntegrityText.Foreground = foreground;
    }

    private void UpdateScanStatus(string prefix, DateTime startedAt, StatusResult status)
    {
        var parts = new List<string> { $"{prefix} {startedAt:MM-dd HH:mm}" };

        if (_repo is not null)
        {
            parts.Add(_repo.Index.Commits.Count == 0
                ? "尚无提交"
                : $"rev {_repo.Index.Head} · 共 {_repo.Index.Commits.Count} 次提交");
        }

        if (status.Plan.Tree.Count > 0)
        {
            parts.Add($"{status.Plan.Tree.Count} 个文件");
        }

        ScanStatus.Foreground = Grey;
        ScanStatus.Text = string.Join(" · ", parts);

        if (status.Health == RepoHealth.RecoveredFromBackup)
        {
            ScanStatus.Foreground = Orange;
            ScanStatus.Text += " · 上次扫描时从备份恢复到较早状态，建议尽快再提交一次";
        }
        else if (status.Health == RepoHealth.EmptyNoCommits && _repo is not null && _repo.Index.Commits.Count == 0)
        {
            ScanStatus.Text += " · 尚未建立版本库，首次提交时自动建立";
        }

        if (status.Plan.Uncertain.Count > 0)
        {
            ScanStatus.Foreground = Red;
            ScanStatus.Text += " · 有文件读不出来：" + status.Plan.Uncertain.Count + " 个（无法提交）";
        }
        else if (status.InUse.Count > 0)
        {
            ScanStatus.Text += $" · {status.InUse.Count} 个文件正被其它程序打开（仍可提交）";
        }
    }

    private void OnRescan(object sender, RoutedEventArgs e) => _ = ScanAsync(auto: false);

    /// <summary>「打开文件目录」：在资源管理器里打开当前项目的文件夹（文件都在这儿，可直接改）。</summary>
    private void OnOpenProjectFolder(object sender, RoutedEventArgs e)
    {
        var root = _repo?.Root ?? (ProjectList.SelectedItem as ProjectItem)?.Path;
        if (string.IsNullOrEmpty(root))
        {
            MessageBox.Show(this, "先在左侧选一个项目。", "FVHelper", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (Directory.Exists(root))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{root}\"") { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show(this, "这个文件夹不在了（可能被移动或删除）。\n可以把它放回原位，或者用上面的「－」从列表移除后重新添加。",
                    "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("打开文件目录失败", ex);
            MessageBox.Show(this, "打开文件夹失败：" + ex.Message, "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (CommitPanel.Visibility == Visibility.Visible || _repo is null)
        {
            return;
        }

        var now = DateTime.Now;
        if (now - _lastAutoScanAt < AutoScanInterval)
        {
            return;
        }

        _lastAutoScanAt = now;
        _ = ScanAsync(auto: true);
    }

    // ================= 提交 =================

    private void OnCommitMessageGotFocus(object sender, RoutedEventArgs e)
    {
        if (_isPlaceholder)
        {
            _suppressMessageChanged = true;
            CommitMessageBox.Text = "";
            _suppressMessageChanged = false;
            CommitMessageBox.Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x29));
            _isPlaceholder = false;
        }
    }

    private void OnCommitMessageLostFocus(object sender, RoutedEventArgs e)
    {
        RestorePlaceholderIfEmpty();
        UpdateCommitButton();
    }

    private void OnCommitMessageChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressMessageChanged)
        {
            return;
        }

        UpdateCommitButton();
    }

    private void RestorePlaceholderIfEmpty()
    {
        if (CommitMessageBox.Text.Trim().Length > 0)
        {
            return;
        }

        _suppressMessageChanged = true;
        CommitMessageBox.Text = MessagePlaceholder;
        _suppressMessageChanged = false;
        CommitMessageBox.Foreground = Grey;
        _isPlaceholder = true;
    }

    private string CurrentMessage()
    {
        if (_isPlaceholder)
        {
            return "";
        }

        return CommitMessageBox.Text.Trim();
    }

    /// <summary>数据自身坏了（MISMATCH / CORRUPT）时禁止提交与导出（D-13）。</summary>
    private bool DataBlocksWrite => _integrity is not null
                                   && _integrity.Level is VerifyLevel.Mismatch or VerifyLevel.Corrupt;

    private void UpdateCommitButton()
    {
        if (CommitButton is null)
        {
            return;
        }

        var selected = SelectedChanges().Count;
        var canCommit = _repo is not null
                        && _status is not null
                        && _status.Plan.HasChanges
                        && _status.Plan.Uncertain.Count == 0
                        && selected > 0
                        && CurrentMessage().Length > 0
                        && !DataBlocksWrite
                        && !_busy;

        CommitButton.IsEnabled = canCommit;

        if (canCommit)
        {
            CommitButton.ToolTip = $"提交已勾选的 {selected} 个改动";
            return;
        }

        CommitButton.ToolTip = DataBlocksWrite
            ? "数据与内容不匹配（或数据损坏），先修好再提交"
            : _status?.Plan.HasChanges != true
                ? "没有需要提交的改动"
                : selected == 0
                    ? "没有勾选任何改动"
                    : "先填写本次修改标题";
    }

    private async void OnCommit(object sender, RoutedEventArgs e)
    {
        AppLog.Info($"点击提交：repo={_repo?.Root} hasChanges={_status?.Plan.HasChanges} " +
                    $"uncertain={_status?.Plan.Uncertain.Count} msgLen={CurrentMessage().Length} " +
                    $"busy={_busy} hasAuthor={AppSettings.HasAuthor}");

        if (_repo is null || _status is null || !_status.Plan.HasChanges)
        {
            AppLog.Info("提交被跳过：没有可用状态");
            return;
        }

        if (DataBlocksWrite)
        {
            MessageBox.Show(this, "版本信息与内容不匹配（或版本信息文件损坏），不能提交。\n先把问题修好再试。",
                "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CurrentMessage().Length == 0)
        {
            MessageBox.Show(this, "请先填写本次修改标题。", "FVHelper", MessageBoxButton.OK, MessageBoxImage.Information);
            CommitMessageBox.Focus();
            return;
        }

        var includePaths = SelectedChanges();
        if (includePaths.Count == 0)
        {
            MessageBox.Show(this, "没有勾选任何改动。勾上要提交的文件，或点「全选」。",
                "FVHelper", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AppLog.Info($"提交已勾选 {includePaths.Count} 个：" + string.Join("、", includePaths));

        if (!AppSettings.HasAuthor)
        {
            try
            {
                AppLog.Info("提交前要求先设置版本信息署名");
                var settings = new AuthorSettingsWindow { Owner = this };
                if (settings.ShowDialog() != true)
                {
                    AppLog.Info("用户取消了署名设置，提交中止");
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("设置对话框异常", ex);
                MessageBox.Show(this, "打开设置失败：" + ex.Message, "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefreshAuthorText();
        }

        var repo = _repo;
        var message = CurrentMessage();
        var author = AppSettings.Author;
        var project = ProjectList.SelectedItem as ProjectItem;

        // 提交确认弹窗：列出这次要提交的文件，标题（预填底部输入框）+ 详情两个字段
        var pickedRows = SelectedChangeRows();
        string detail;
        try
        {
            var dialogRows = pickedRows.Select(f => new CommitWindow.CommitFileRow
            {
                Action = f.Action ?? "M",
                Name = f.Name,
                Size = f.Size,
                ActionBrush = f.StatusBrush
            }).ToList();
            var description = $"{pickedRows.Count} 个文件 · {FormatSize(pickedRows.Sum(f => f.SizeBytes))}";
            var dialog = new CommitWindow(message, "", dialogRows, description) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                AppLog.Info("用户在提交确认窗取消，提交中止");
                return;
            }

            message = dialog.CommitTitle;
            detail = dialog.CommitDetail;
        }
        catch (Exception ex)
        {
            AppLog.Error("提交确认窗异常", ex);
            MessageBox.Show(this, "打开提交确认窗失败：" + ex.Message, "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CommitResult? result = null;
        Exception? failure = null;

        SetBusy(true);
        ShowBusy($"正在提交 {includePaths.Count} 个文件…");
        try
        {
            result = await Task.Run(() => repo.Commit(message, author, includePaths, default, detail));
            AppLog.Info($"提交成功 rev {result.Record.Rev}，{result.Record.Stats.Files} 个文件，详情 {detail.Length} 字");
        }
        catch (Exception ex)
        {
            failure = ex;
            AppLog.Error("提交失败", ex);
        }
        finally
        {
            HideBusy();
            SetBusy(false);
        }

        if (failure is not null)
        {
            MessageBox.Show(this, "提交失败：" + failure.Message, "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
            await ScanAsync(auto: false);
            return;
        }

        // 提交成功：清空说明框 → 刷新 changelist → 重新扫一遍（此时按钮已恢复可用）
        ResetMessageBox();
        AppLog.Info("提交成功流程：已清空说明框");
        LoadCommits(project!);
        AppLog.Info("提交成功流程：changelist 已刷新");
        await ScanAsync(auto: false);
        AppLog.Info("提交成功流程：重扫完成");
        RevealCommit(result!.Record.Rev);
        AppLog.Info("提交成功流程：已定位到新版本");

        var warning = result.Warnings.Count > 0 ? "\n\n" + string.Join("\n", result.Warnings) : "";
        AppLog.Info("提交成功流程：准备弹提示框");
        MessageBox.Show(this,
            $"已提交 rev {result.Record.Rev}：{result.Record.Message}\n" +
            $"版本信息署名 {result.Record.Author} · {result.Record.Stats.Files} 个文件 · {FormatSize(result.Record.Stats.Bytes)}{warning}",
            "提交成功", MessageBoxButton.OK, MessageBoxImage.Information);
        AppLog.Info("提交成功流程：提示框已关闭");
    }

    private void ResetMessageBox()
    {
        _suppressMessageChanged = true;
        CommitMessageBox.Text = MessagePlaceholder;
        _suppressMessageChanged = false;
        CommitMessageBox.Foreground = Grey;
        _isPlaceholder = true;
        UpdateCommitButton();
    }

    // ================= changelist / 提交详情 =================

    private void OnCommitSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCommitSelection || CommitList.SelectedItem is not CommitItem item)
        {
            return;
        }

        ShowCommitDetail(item.Record.Rev);
    }

    private void ShowCommitDetail(int rev)
    {
        if (_repo is null)
        {
            return;
        }

        var record = _repo.GetCommit(rev);
        if (record is null)
        {
            return;
        }

        _selectedCommit = record;

        CommitTitle.Text = $"rev {record.Rev} · {record.Message}";
        CommitMeta.Text = $"版本信息署名 {record.Author} · {FormatTime(record.CreatedAt)} · {record.Tree.Count} 个文件 · {FormatSize(record.Stats.Bytes)}";

        // 版本详细信息（导出模块上方）：标题 / 详情 / 事实统计
        VersionTitleText.Text = record.Message;
        VersionDetailText.Text = string.IsNullOrWhiteSpace(record.Detail) ? "（这一版没有填写详情）" : record.Detail;
        VersionDetailText.FontStyle = string.IsNullOrWhiteSpace(record.Detail) ? FontStyles.Italic : FontStyles.Normal;

        var actionParts = new List<string>();
        if (record.Stats.Added > 0) actionParts.Add($"新增 {record.Stats.Added}");
        if (record.Stats.Modified > 0) actionParts.Add($"修改 {record.Stats.Modified}");
        if (record.Stats.Deleted > 0) actionParts.Add($"删除 {record.Stats.Deleted}");
        if (record.Stats.Renamed > 0) actionParts.Add($"改名 {record.Stats.Renamed}");
        var actionText = actionParts.Count > 0 ? "本次改动：" + string.Join(" / ", actionParts) : "本次改动：无";

        VersionFactsText.Text =
            $"版本信息署名 {record.Author} · {FormatTime(record.CreatedAt)}\n" +
            $"版本 rev {record.Rev}（上一版 rev {record.ParentRev}）· 版本文件共 {record.Tree.Count} 个 · 这一版改动 {record.Changes.Count} 项\n" +
            $"{actionText} · 改动文件合计 {FormatSize(record.Stats.Bytes)} · 版本标识 {record.Id}";

        ChangeListView.ItemsSource = record.Changes.Select(c => new ChangeRow
        {
            Action = c.Action,
            Name = c.From is null ? c.Path : $"{c.Path}　← {c.From}",
            Path = c.Path,
            Size = FormatSize(c.Size)
        }).ToList();

        ProjectPanel.Visibility = Visibility.Collapsed;
        CommitPanel.Visibility = Visibility.Visible;

        UpdateExportButtons();
        VerifyCommitAsync(deep: false);
    }

    private void OnBackToProject(object sender, RoutedEventArgs e)
    {
        _suppressCommitSelection = true;
        CommitList.SelectedIndex = -1;
        _suppressCommitSelection = false;
        _selectedCommit = null;
        CommitPanel.Visibility = Visibility.Collapsed;
        ProjectPanel.Visibility = Visibility.Visible;
    }

    private async void VerifyCommitAsync(bool deep)
    {
        if (_repo is null || _selectedCommit is null)
        {
            return;
        }

        var record = _selectedCommit;
        var repo = _repo;
        SetIntegrity("正在校验…", "#FFFFFF", "#E6E8EB", Grey);
        VerifyCommitButton.IsEnabled = false;

        VerifyResult verified;
        try
        {
            verified = await Task.Run(() => Verifier.CheckCommit(repo.Root, record, deep));
        }
        catch (Exception ex)
        {
            AppLog.Error("单条校验失败", ex);
            SetIntegrity("✗ 校验没能完成：" + ex.Message, "#FDF1F1", "#F3C6C6", Red);
            VerifyCommitButton.IsEnabled = true;
            return;
        }

        VerifyCommitButton.IsEnabled = true;
        if (_selectedCommit != record)
        {
            return;
        }

        switch (verified.Level)
        {
            case VerifyLevel.Ok:
                SetIntegrity("✓ 已校验：该版本记录的文件与版本信息一致（文件数、大小、哈希全部匹配）",
                    "#F0F7F2", "#CBE4D6", Green);
                break;
            case VerifyLevel.Warn:
                SetIntegrity("· 数据本身没问题；工作区和这个版本不完全一样（正常，说明你后来又改了文件）",
                    "#F7F9FC", "#E6E8EB", Grey);
                break;
            case VerifyLevel.Mismatch:
                SetIntegrity("✗ 数据与内容不匹配：\n" + string.Join("\n", verified.Problems),
                    "#FDF1F1", "#F3C6C6", Red);
                break;
            default:
                SetIntegrity("✗ 版本信息损坏：\n" + string.Join("\n", verified.Problems),
                    "#FBEAEA", "#EFB4B4", Red);
                break;
        }

        UpdateExportButtons();
    }

    private void OnVerifyCommit(object sender, RoutedEventArgs e) => VerifyCommitAsync(deep: true);

    private void SetIntegrity(string text, string background, string border, Brush foreground)
    {
        IntegrityBar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
        IntegrityBar.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
        IntegrityText.Text = text;
        IntegrityText.Foreground = foreground;
    }

    private void RevealCommit(int rev)
    {
        var item = _projects.FirstOrDefault(p => p == ProjectList.SelectedItem)?
            .Commits.FirstOrDefault(c => c.Record.Rev == rev);
        if (item is null)
        {
            return;
        }

        CommitList.SelectedItem = item;
        CommitList.ScrollIntoView(item);
    }

    // ================= 详情态：定位与复制清单 =================

    private void OnChangeListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ChangeListView.SelectedItem is ChangeRow row)
        {
            RevealChange(row);
        }
    }

    private void OnRevealSelectedChange(object sender, RoutedEventArgs e)
    {
        if (ChangeListView.SelectedItem is ChangeRow row)
        {
            RevealChange(row);
        }
    }

    /// <summary>在当前工作区里定位这个文件（历史版本的文件可能已被后来的改动覆盖掉）。</summary>
    private void RevealChange(ChangeRow row)
    {
        if (_repo is null)
        {
            return;
        }

        var full = Path.Combine(_repo.Root, row.Path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            MessageBox.Show(this,
                $"这个文件现在不在工作区里：\n{row.Path}\n\n" +
                "（它可能在这一版之后被改名或删除了。想看当时的内容，请用「导出」把这个版本导出来。）",
                "FVHelper", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{full}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("定位文件失败", ex);
            MessageBox.Show(this, "打开资源管理器失败：" + ex.Message, "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCopyChangeList(object sender, RoutedEventArgs e) => CopyChangeList();

    private void CopyChangeList()
    {
        if (_selectedCommit is null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"rev {_selectedCommit.Rev} · {_selectedCommit.Message}");
        builder.AppendLine($"版本信息署名 {_selectedCommit.Author} · {FormatTime(_selectedCommit.CreatedAt)} · " +
                           $"{_selectedCommit.Tree.Count} 个文件 · {FormatSize(_selectedCommit.Stats.Bytes)}");
        if (!string.IsNullOrWhiteSpace(_selectedCommit.Detail))
        {
            builder.AppendLine();
            builder.AppendLine("详情：");
            builder.AppendLine(_selectedCommit.Detail);
        }

        builder.AppendLine();
        foreach (var change in _selectedCommit.Changes)
        {
            builder.AppendLine($"{change.Action}\t{(change.From is null ? change.Path : change.Path + " ← " + change.From)}\t{FormatSize(change.Size)}");
        }

        try
        {
            Clipboard.SetText(builder.ToString());
            ScanStatus.Text = "已复制本次提交清单到剪贴板";
        }
        catch (Exception ex)
        {
            AppLog.Error("复制清单失败", ex);
        }
    }

    // ================= 导出 =================

    private void UpdateExportButtons()
    {
        var blocked = DataBlocksWrite;
        ExportFilesButton.IsEnabled = !_busy && !blocked;
        ExportMetaButton.IsEnabled = !_busy && !blocked;
        var tip = blocked ? "数据与内容不匹配（或数据损坏），先修好再导出" : null;
        if (tip is not null)
        {
            ExportFilesButton.ToolTip = tip;
            ExportMetaButton.ToolTip = tip;
        }
    }

    private async void OnExportFiles(object sender, RoutedEventArgs e)
    {
        if (_repo is null || _selectedCommit is null || DataBlocksWrite)
        {
            return;
        }

        var target = PickExportFolder("导出（仅文件）：选择保存到哪个文件夹");
        if (target is null)
        {
            return;
        }

        await RunExport(ExportMode.FilesOnly, asNewStart: false, target);
    }

    private async void OnExportMeta(object sender, RoutedEventArgs e)
    {
        if (_repo is null || _selectedCommit is null || DataBlocksWrite)
        {
            return;
        }

        var dialog = new ExportWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var target = PickExportFolder("导出（含版本信息）：选择保存到哪个文件夹");
        if (target is null)
        {
            return;
        }

        await RunExport(ExportMode.WithMeta, dialog.AsNewStart, target);
    }

    private string? PickExportFolder(string title)
    {
        try
        {
            AppLog.Info("选择导出目录：" + title);
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            var result = dialog.ShowDialog(this);
            AppLog.Info($"导出目录选择返回：{result} → {dialog.FolderName}");
            return result == true ? dialog.FolderName : null;
        }
        catch (Exception ex)
        {
            AppLog.Error("打开导出目录选择失败", ex);
            MessageBox.Show(this, "打开目录选择失败：" + ex.Message, "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private async Task RunExport(ExportMode mode, bool asNewStart, string target)
    {
        if (_repo is null || _selectedCommit is null)
        {
            return;
        }

        var repo = _repo;
        var rev = _selectedCommit.Rev;
        AppLog.Info($"开始导出 rev {rev}，mode={mode} newStart={asNewStart} → {target}");
        SetBusy(true);
        ShowBusy("正在导出…");
        try
        {
            var result = await Task.Run(() => Exporter.Export(repo, rev, new ExportOptions
            {
                TargetDirectory = target,
                Mode = mode,
                AsNewStart = asNewStart
            }));

            var description = mode == ExportMode.FilesOnly
                ? "仅文件（不含版本信息）"
                : "含版本信息" + (asNewStart ? "（作为新起点）" : "");
            AppLog.Info($"导出完成 → {result.RootPath}，{result.FileCount} 个文件");

            HideBusy();
            SetBusy(false);

            var done = new ExportDoneWindow
            {
                Owner = this,
                Description = description,
                RootPath = result.RootPath,
                Summary = $"rev {rev} · 文件 {result.FileCount} 个 · {FormatSize(result.Bytes)}" +
                          (mode == ExportMode.WithMeta ? " · 对方用本软件打开即可继续编辑并提交" : "")
            };
            done.ShowDialog();

            if (done.RevealRequested)
            {
                OpenInExplorer(result.RootPath);
            }
        }
        catch (Exception ex)
        {
            HideBusy();
            AppLog.Error("导出失败", ex);
            MessageBox.Show(this, "导出失败：" + ex.Message, "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ================= 设置 / 顶栏 =================

    private void RefreshAuthorText()
    {
        AuthorText.Text = AppSettings.HasAuthor ? $"版本信息署名 {AppSettings.Author}" : "版本信息署名 未设置";
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLog.Info("打开设置对话框");
            var dialog = new AuthorSettingsWindow
            {
                Owner = this,
                IgnoreRulesText = _repo is null ? "" : string.Join("、", _repo.Index.Ignore)
            };
            AppLog.Info("设置对话框已构造，准备 ShowDialog");
            var result = dialog.ShowDialog();
            AppLog.Info($"设置对话框返回：{result}");
            if (result == true)
            {
                RefreshAuthorText();
                UpdateCommitButton();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("设置对话框异常", ex);
            MessageBox.Show(this, "打开设置失败：" + ex.Message, "FVHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ================= 公共小工具 =================

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
        ScanButton.IsEnabled = !busy;
        if (ExportFilesButton is not null)
        {
            UpdateExportButtons();
        }

        if (busy)
        {
            CommitButton.IsEnabled = false;
        }
        else
        {
            UpdateCommitButton();
        }
    }

    /// <summary>中栏盖一层"正在处理"遮罩（长操作时用；操作本身不可中断，所以这里不放假取消按钮）。</summary>
    private void ShowBusy(string text)
    {
        BusyText.Text = text;
        BusyOverlay.Visibility = Visibility.Visible;
    }

    private void HideBusy()
    {
        BusyOverlay.Visibility = Visibility.Collapsed;
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            // 打不开就算了，路径已经在界面/剪贴板里
        }
    }

    private void ShowError(string title, string message)
    {
        ProjectTitle.Text = ProjectList.SelectedItem is ProjectItem p ? p.Name : "（未选择项目）";
        ScanStatus.Foreground = Red;
        ScanStatus.Text = message;
        EmptyHint.Text = "这个项目打不开。\n常见原因：文件夹被移走、数据文件损坏且备份也不可用。";
        EmptyHint.Visibility = Visibility.Visible;
        FileListView.ItemsSource = null;
        _status = null;
        UpdateCommitButton();
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string StatusText(string? action) => action switch
    {
        "A" => "新增",
        "M" => "修改",
        "D" => "删除",
        "R" => "改名",
        _ => "无变化"
    };

    private static Brush StatusBrush(string? action) => action switch
    {
        "A" => Green,
        "M" => Orange,
        "D" => Red,
        "R" => Blue,
        _ => Grey
    };

    private static int StatusOrder(string status) => status switch
    {
        "修改" => 0,
        "新增" => 1,
        "删除" => 2,
        "改名" => 3,
        _ => 4
    };

    private static string TryFormatMtime(string root, string relative)
    {
        try
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.GetLastWriteTime(full).ToString("MM-dd HH:mm") : "";
        }
        catch
        {
            return "";
        }
    }

    internal static string FormatTime(string iso)
    {
        try
        {
            var parsed = DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);
            return parsed.ToString("MM-dd HH:mm");
        }
        catch
        {
            return iso;
        }
    }

    internal static string FormatSize(long bytes) =>
        bytes < 1024 ? bytes + " B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB"
        : bytes < 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:F1} MB"
        : $"{bytes / 1024.0 / 1024.0 / 1024.0:F1} GB";
}

internal sealed class ProjectItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string LastCommit { get; set; } = "尚未扫描";
    public bool HasRepository { get; set; }

    /// <summary>文件夹已经不在了（被移动/删除），左栏那一行标红。</summary>
    public bool Broken { get; set; }

    public Brush LastCommitBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0xB0, 0xB6, 0xBE));

    public List<FileRow> Files { get; } = new();
    public List<CommitItem> Commits { get; } = new();
}

internal sealed class FileRow
{
    public string Name { get; init; } = "";

    /// <summary>仓库里的相对路径（提交时按它传 includePaths）。</summary>
    public string Path { get; init; } = "";

    public string? Action { get; init; }
    public bool IsChanged { get; init; }

    /// <summary>勾选状态：只有改动过的行才显示勾选框，默认全选。</summary>
    public bool IsSelected { get; set; }

    public string Status { get; init; } = "";
    public string Size { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Time { get; init; } = "";
    public Brush StatusBrush { get; init; } = Brushes.Gray;
}

internal sealed class CommitItem
{
    public CommitItem(CommitRecord record)
    {
        Record = record;
    }

    public CommitRecord Record { get; }

    public string Label => $"rev {Record.Rev} · {Record.Message}";

    public string Time => MainWindow.FormatTime(Record.CreatedAt);
}

internal sealed class ChangeRow
{
    public string Action { get; init; } = "";
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Size { get; init; } = "";

    public Brush ActionBrush => Action switch
    {
        "A" => new SolidColorBrush(Color.FromRgb(0x2F, 0xA3, 0x6B)),
        "D" => new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
        "R" => new SolidColorBrush(Color.FromRgb(0x3B, 0x72, 0xF6)),
        _ => new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06))
    };
}
