using System.IO;
using InkHarbor.Core.Models;

namespace InkHarbor.App.ViewModels;

/// <summary>预设列表里的一条。</summary>
public sealed class PresetItemViewModel : ObservableObject
{
    public PresetItemViewModel(Preset preset)
    {
        Name = preset.Name;
        CorpusFiles = preset.CorpusFiles.ToList();
        CreatedAt = preset.CreatedAt;
    }

    public string Name { get; }

    public IReadOnlyList<string> CorpusFiles { get; }

    public DateTimeOffset CreatedAt { get; }

    public string MetaLine => $"{CorpusFiles.Count} 篇 · {CreatedAt.LocalDateTime:MM-dd HH:mm}";

    /// <summary>把文件名去掉扩展名连起来，当预览。</summary>
    public string PreviewText => CorpusFiles.Count == 0
        ? "（空配方）"
        : string.Join("、", CorpusFiles.Select(Path.GetFileNameWithoutExtension));
}
