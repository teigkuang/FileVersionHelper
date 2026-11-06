using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FvHelper.Desktop;

/// <summary>提交确认弹窗：列出这次要提交的文件，填写标题与详情。标题必填，详情可空。</summary>
public partial class CommitWindow : Window
{
    /// <summary>列表行（只用于本窗展示）。</summary>
    public sealed class CommitFileRow
    {
        public string Action { get; init; } = "M";
        public string Name { get; init; } = "";
        public string Size { get; init; } = "";
        public Brush ActionBrush { get; init; } = Brushes.Black;
    }

    public CommitWindow(string title, string detail, IReadOnlyList<CommitFileRow> files, string filesDescription)
    {
        InitializeComponent();

        TitleBox.Text = title;
        DetailBox.Text = detail;

        FileList.ItemsSource = files;
        FilesHeader.Text = $"待提交的文件（{files.Count} 个）";
        FilesSummary.Text = filesDescription;

        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.CaretIndex = TitleBox.Text.Length;
        };
        UpdateOk();
    }

    public string CommitTitle => TitleBox.Text.Trim();

    public string CommitDetail => DetailBox.Text.Trim();

    private void OnTitleChanged(object sender, TextChangedEventArgs e) => UpdateOk();

    private void UpdateOk()
    {
        if (OkButton is null)
        {
            return;
        }

        var can = CommitTitle.Length > 0;
        OkButton.IsEnabled = can;
        HintText.Text = can ? "提交后标题与详情都会写进版本信息" : "标题不能为空";
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (CommitTitle.Length == 0)
        {
            MessageBox.Show(this, "标题不能为空。", "FVHelper", MessageBoxButton.OK, MessageBoxImage.Information);
            TitleBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
