using System.Windows.Media;
using InkHarbor.Core.Models;

namespace InkHarbor.App.ViewModels;

public sealed class DraftItemViewModel : ObservableObject
{
    public DraftItemViewModel(DraftSummary summary)
    {
        Title = summary.Title;
        Status = summary.Status;
        CurrentVersion = summary.CurrentVersion;
        UpdatedAt = summary.UpdatedAt;
        Directory = summary.Directory;
    }

    public string Title { get; }

    public DraftStatus Status { get; }

    public int CurrentVersion { get; }

    public DateTimeOffset UpdatedAt { get; }

    public string Directory { get; }

    public string DisplayTitle => $"{Title} v{CurrentVersion}";

    public string StatusLine => $"{StatusText} · {RelativeTimeText}";

    public string StatusText => Status switch
    {
        DraftStatus.Final => "● 已入终稿",
        DraftStatus.Editing => "修改中",
        _ => "底稿"
    };

    /// <summary>状态标颜色：底稿灰 / 修改中蓝 / 已入终稿绿（对齐设计图）。</summary>
    public Brush StatusBrush => Status switch
    {
        DraftStatus.Final => new SolidColorBrush(Color.FromRgb(0x0F, 0x6E, 0x56)),
        DraftStatus.Editing => new SolidColorBrush(Color.FromRgb(0x0C, 0x44, 0x7C)),
        _ => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
    };

    /// <summary>卡片左边那道竖条的粗细：修改中的那篇高亮。</summary>
    public double AccentWidth => Status == DraftStatus.Editing ? 3 : 1;

    public Brush AccentBrush => Status switch
    {
        DraftStatus.Final => new SolidColorBrush(Color.FromRgb(0x0F, 0x6E, 0x56)),
        DraftStatus.Editing => new SolidColorBrush(Color.FromRgb(0x37, 0x8A, 0xDD)),
        _ => new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6))
    };

    private string RelativeTimeText
    {
        get
        {
            var span = DateTimeOffset.Now - UpdatedAt;
            if (span.TotalMinutes < 1)
            {
                return "刚刚";
            }

            if (span.TotalHours < 1)
            {
                return $"{(int)span.TotalMinutes} 分钟前";
            }

            if (span.TotalDays < 1)
            {
                return $"{(int)span.TotalHours} 小时前";
            }

            return UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm");
        }
    }
}
