namespace InkHarbor.Core.Models;

/// <summary>
/// drafts\&lt;标题&gt;\meta.json 的结构。
/// 字段按总体方案第四节预留：versions 是数组（M2 版本回退直接读它），
/// source 预留 ai 取值，corpusFiles 给 M3 的范文推荐留痕。
/// </summary>
public sealed class DraftMeta
{
    public string Title { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DraftStatus Status { get; set; } = DraftStatus.Draft;

    /// <summary>当前版本号，等于 Versions 最后一项的版本号。</summary>
    public int CurrentVersion { get; set; }

    public List<DraftVersion> Versions { get; set; } = new();

    /// <summary>生成时用户填的大纲原文，留档用。</summary>
    public string Outline { get; set; } = "";

    /// <summary>生成时选用的模型标签。</summary>
    public string Model { get; set; } = "";

    /// <summary>本次配方用到的范文文件名（不含路径），供 M3 溯源。</summary>
    public List<string> CorpusFiles { get; set; } = new();

    /// <summary>入终稿后的相对路径，如 final\雨夜当铺.md；未入终稿为 null。</summary>
    public string? FinalFile { get; set; }

    public DraftVersion? Current => Versions.LastOrDefault();
}
