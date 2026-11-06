using System.Windows;

namespace FvHelper.Desktop;

/// <summary>设置小弹窗（%LOCALAPPDATA%\FVHelper\settings.json）：版本信息署名、大文件阈值、导出默认项。</summary>
public partial class AuthorSettingsWindow : Window
{
    public AuthorSettingsWindow()
    {
        InitializeComponent();
        AuthorBox.Text = AppSettings.Author;
        LargeFileBox.Text = AppSettings.LargeFileWarnMb.ToString();
        NewStartCheckBox.IsChecked = AppSettings.ExportAsNewStart;

        // 忽略规则属于项目数据（.dat 里），这里只读展示当前项目（值由调用方在构造后填入，放到 Loaded 里读）
        Loaded += (_, _) =>
        {
            IgnoreText.Text = string.IsNullOrWhiteSpace(IgnoreRulesText)
                ? "（未打开项目，或该项目没有任何自定义忽略规则）"
                : IgnoreRulesText;
            AuthorBox.Focus();
            AuthorBox.SelectAll();
        };
    }

    /// <summary>由主窗口在构造后填入：当前项目 .dat 索引里的忽略规则，逗号分隔。</summary>
    public string IgnoreRulesText { get; set; } = "";

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var value = AuthorBox.Text.Trim();
        if (string.IsNullOrEmpty(value))
        {
            MessageBox.Show(this, "版本信息署名不能为空。提交记录需要写明是谁改的。", "FVHelper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(LargeFileBox.Text.Trim(), out var mb) || mb <= 0)
        {
            MessageBox.Show(this, "大文件提示阈值要填一个大于 0 的整数（MB）。", "FVHelper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            LargeFileBox.Focus();
            return;
        }

        AppSettings.SaveSettings(value, mb, NewStartCheckBox.IsChecked == true);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
