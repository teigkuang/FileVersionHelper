using System.Windows;

namespace FvHelper.Desktop;

/// <summary>导出完成页：给出导出位置，提供「复制路径」「打开文件夹」两个动作。</summary>
public partial class ExportDoneWindow : Window
{
    public ExportDoneWindow()
    {
        InitializeComponent();
    }

    /// <summary>"仅文件（不含版本信息）" 之类的模式说明。</summary>
    public string Description
    {
        get => ModeText.Text;
        set => ModeText.Text = value;
    }

    public string RootPath
    {
        get => PathBox.Text;
        set => PathBox.Text = value;
    }

    public string Summary
    {
        get => SummaryText.Text;
        set => SummaryText.Text = value;
    }

    /// <summary>用户是否点了「打开文件夹」——由调用方去启动资源管理器。</summary>
    public bool RevealRequested { get; private set; }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(RootPath);
            CopyButton.Content = "已复制";
            CopyButton.IsEnabled = false;
        }
        catch
        {
            // 剪贴板偶尔被别的程序占着，静默即可——路径框本身可以手动选中复制
        }
    }

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        RevealRequested = true;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
