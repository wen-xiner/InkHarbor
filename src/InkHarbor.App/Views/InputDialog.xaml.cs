using System.Windows;

namespace InkHarbor.App.Views;

public partial class InputDialog : Window
{
    private InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Input.SelectAll();
            Input.Focus();
        };
    }

    /// <summary>弹一个小输入框，取消或留空返回 null。</summary>
    public static string? Show(string title, string prompt, string defaultValue = "")
    {
        var dialog = new InputDialog
        {
            Title = title,
            Owner = Application.Current.MainWindow
        };

        dialog.PromptText.Text = prompt;
        dialog.Input.Text = defaultValue;

        var ok = dialog.ShowDialog();
        var text = dialog.Input.Text?.Trim();
        return ok == true && !string.IsNullOrWhiteSpace(text) ? text : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
