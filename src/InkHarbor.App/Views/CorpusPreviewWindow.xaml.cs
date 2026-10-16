using System.Windows;
using InkHarbor.Core.Models;

namespace InkHarbor.App.Views;

public partial class CorpusPreviewWindow : Window
{
    private CorpusPreviewWindow()
    {
        InitializeComponent();
    }

    public static void Show(CorpusFile file, string text)
    {
        var window = new CorpusPreviewWindow
        {
            Title = $"范文预览 · {file.DisplayName}",
            Owner = Application.Current.MainWindow
        };

        window.FileNameText.Text = file.FileName;
        window.MetaText.Text = $"{file.TagText}｜{file.WordCount} 字｜约 {file.EstimatedTokens} token";
        window.BodyBox.Text = text;

        window.ShowDialog();
    }
}
