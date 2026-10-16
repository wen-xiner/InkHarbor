using System.Windows.Media;
using InkHarbor.Core.Models;

namespace InkHarbor.App.ViewModels;

/// <summary>版本历史列表里的一条。</summary>
public sealed class VersionItemViewModel : ObservableObject
{
    public VersionItemViewModel(DraftVersion version, bool isCurrent)
    {
        Number = version.Version;
        Source = version.Source;
        CreatedAt = version.CreatedAt;
        WordCount = version.WordCount;
        Model = version.Model;
        Note = version.Note;
        Instruction = version.Instruction;
        FromVersion = version.FromVersion;
        IsCurrent = isCurrent;
    }

    public int Number { get; }

    public string Source { get; }

    public DateTimeOffset CreatedAt { get; }

    public int WordCount { get; }

    public string Model { get; }

    public string Note { get; }

    public string Instruction { get; }

    public int? FromVersion { get; }

    public bool IsCurrent { get; }

    public string DisplayTitle => IsCurrent ? $"v{Number}（当前）" : $"v{Number}";

    public string SourceText => Source switch
    {
        VersionSource.Generate => "模型生成",
        VersionSource.Manual => "手动编辑",
        VersionSource.Ai => "AI 修订",
        VersionSource.Revert => "回退",
        _ => Source
    };

    public string MetaLine => $"{SourceText} · {CreatedAt.LocalDateTime:MM-dd HH:mm} · {WordCount} 字";

    /// <summary>列表第三行：回退版显示来源版本，AI 修订显示当初那条指令。</summary>
    public string DetailLine => !string.IsNullOrWhiteSpace(Note)
        ? Note
        : string.IsNullOrWhiteSpace(Instruction)
            ? ""
            : "指令：" + Instruction;

    public bool HasDetail => DetailLine.Length > 0;

    /// <summary>当前版本用蓝色高亮，其余灰。</summary>
    public Brush AccentBrush => IsCurrent
        ? new SolidColorBrush(Color.FromRgb(0x37, 0x8A, 0xDD))
        : new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));

    public double AccentWidth => IsCurrent ? 3 : 1;
}
