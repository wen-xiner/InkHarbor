namespace InkHarbor.Core.Models;

/// <summary>
/// final\ 里封存的一份终稿。只读快照：只看文件本身，不读正文、不碰 meta.json。
/// 终稿是"看一眼就走"的东西，所以这里不带任何可编辑的语义。
/// </summary>
public sealed class FinalFile
{
    /// <summary>文件名（含扩展名），如「夜晚乡村小道.md」。</summary>
    public string FileName { get; init; } = "";

    /// <summary>绝对路径。</summary>
    public string FullPath { get; init; } = "";

    /// <summary>去掉扩展名的显示名。</summary>
    public string DisplayName { get; init; } = "";

    public long SizeBytes { get; init; }

    /// <summary>最后写入时间，列表按它倒序。</summary>
    public DateTimeOffset SavedAt { get; init; }

    /// <summary>列表卡片第二行：大小 + 封存时间。</summary>
    public string MetaText => $"{FormatSize(SizeBytes)} · {SavedAt.LocalDateTime:MM-dd HH:mm}";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / 1024.0 / 1024.0:F1} MB"
    };
}
