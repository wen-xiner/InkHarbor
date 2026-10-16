namespace InkHarbor.Core.Models;

/// <summary>按大纲推荐出来的一个范文候选。</summary>
public sealed class CorpusSuggestion
{
    public string FileName { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string TagText { get; set; } = "";

    public int WordCount { get; set; }

    /// <summary>余弦相似度，-1~1，越大越像。同一个大纲下的相对高低比绝对值有意义。</summary>
    public double Score { get; set; }
}
