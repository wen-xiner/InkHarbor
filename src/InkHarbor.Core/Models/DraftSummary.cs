namespace InkHarbor.Core.Models;

/// <summary>
/// 左栏"作品"列表用的轻量摘要，不加载正文。
/// </summary>
public sealed class DraftSummary
{
    public string Title { get; init; } = "";

    public DraftStatus Status { get; init; }

    public int CurrentVersion { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>作品目录的绝对路径。</summary>
    public string Directory { get; init; } = "";

    /// <summary>列表第二行的状态文案，如"修改中 · 刚刚"。</summary>
    public string StatusText => Status switch
    {
        DraftStatus.Final => "已入终稿",
        DraftStatus.Editing => "修改中",
        _ => "底稿"
    };
}
