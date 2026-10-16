using System.IO;
using System.Windows;
using InkHarbor.Core.Models;
using InkHarbor.Core.Services;

namespace InkHarbor.App.Views;

/// <summary>新建 / 编辑一篇范文的弹窗。文件名就是标签。</summary>
public partial class CorpusEditWindow : Window
{
    private readonly bool _nameEditable;

    private CorpusEditWindow(bool nameEditable)
    {
        _nameEditable = nameEditable;
        InitializeComponent();
        BodyBox.TextChanged += (_, _) => UpdateCount();
    }

    /// <summary>用户填的文件名（不含扩展名），也就是标签。</summary>
    public string FileDisplayName => NameBox.Text.Trim();

    /// <summary>范文正文。</summary>
    public string BodyText => BodyBox.Text;

    /// <summary>扩展名，.md 或 .txt。</summary>
    public string FileExtension => ExtText.Text.Trim();

    public static CorpusEditWindow ForCreate(string defaultName = "")
    {
        var window = new CorpusEditWindow(nameEditable: true)
        {
            Title = "新建范文",
            Owner = Application.Current.MainWindow
        };

        window.HintText.Text =
            "文件名就是标签，建议按「题材_人称_节奏_编号」起，比如 都市_第一人称_冷幽默_03。\n"
            + "标签会在左栏卡片上显示，方便你按味儿挑范文。";

        window.NameBox.Text = defaultName;
        window.UpdateCount();
        window.Loaded += (_, _) =>
        {
            window.NameBox.SelectAll();
            window.NameBox.Focus();
        };

        return window;
    }

    public static CorpusEditWindow ForEdit(CorpusFile file, string text)
    {
        var window = new CorpusEditWindow(nameEditable: false)
        {
            Title = "编辑范文",
            Owner = Application.Current.MainWindow
        };

        window.HintText.Text =
            "这里改的是正文。文件名（也就是标签）想改的话，用列表上的「重命名」。";
        window.ExtText.Text = Path.GetExtension(file.FileName);
        window.NameBox.Text = file.DisplayName;
        window.NameBox.IsEnabled = false;
        window.BodyBox.Text = text;
        window.UpdateCount();
        window.Loaded += (_, _) =>
        {
            window.BodyBox.Focus();
            window.BodyBox.CaretIndex = 0;
        };

        return window;
    }

    private void UpdateCount() =>
        CountText.Text = $"{TextMetrics.CountWords(BodyBox.Text)} 字（非空白字符）";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_nameEditable && string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "给这篇范文起个文件名（它就是标签）。", "InkHarbor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(BodyBox.Text))
        {
            var go = MessageBox.Show(this, "正文是空的，还是存？", "InkHarbor",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (go != MessageBoxResult.OK)
            {
                return;
            }
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
