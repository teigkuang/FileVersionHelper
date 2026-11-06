using System.Windows;

namespace FvHelper.Desktop;

/// <summary>"导出（含版本信息）"前的确认对话框：是否作为新起点。</summary>
public partial class ExportWindow : Window
{
    public ExportWindow()
    {
        InitializeComponent();
        // 默认勾选状态跟随设置里"记住上次选择"
        NewStartCheckBox.IsChecked = AppSettings.ExportAsNewStart;
    }

    public bool AsNewStart => NewStartCheckBox.IsChecked == true;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        AppSettings.SetExportAsNewStart(AsNewStart);
        DialogResult = true;
    }
}
