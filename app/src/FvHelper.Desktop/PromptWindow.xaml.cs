using System.Windows;

namespace FvHelper.Desktop;

/// <summary>通用的一行输入小窗（目前用于项目重命名）。</summary>
public partial class PromptWindow : Window
{
    public PromptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ValueBox.Text = Value;
            ValueBox.SelectAll();
            ValueBox.Focus();
        };
    }

    public string WindowTitle
    {
        get => Title;
        set
        {
            Title = value;
            TitleText.Text = value;
        }
    }

    public string Label
    {
        get => LabelText.Text;
        set => LabelText.Text = value;
    }

    public string Value
    {
        get => ValueBox.Text;
        set => ValueBox.Text = value;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (ValueBox.Text.Trim().Length == 0)
        {
            MessageBox.Show(this, "名字不能为空。", "FVHelper", MessageBoxButton.OK, MessageBoxImage.Information);
            ValueBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
