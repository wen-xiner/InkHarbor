namespace InkHarbor.Core.Models;

/// <summary>
/// 范文库里的一篇范文。标签直接来自文件名：题材_人称_节奏_编号.md
/// </summary>
public sealed class CorpusFile
{
    /// <summary>文件名（含扩展名）。</summary>
    public string FileName { get; init; } = "";

    /// <summary>绝对路径。</summary>
    public string FullPath { get; init; } = "";

    /// <summary>去掉扩展名的显示名。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>文件名按 "_" 切出来的标签段（去掉末尾编号）。</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public long SizeBytes { get; init; }

    /// <summary>字数（非空白字符数），用来估 token。</summary>
    public int WordCount { get; init; }

    /// <summary>估出来的 token 数（估算值，见 TokenEstimator）。</summary>
    public int EstimatedTokens { get; init; }

    /// <summary>前若干字的预览，卡片上显示。</summary>
    public string Preview { get; init; } = "";

    /// <summary>列表卡片上的一行标签文案。</summary>
    public string TagText => Tags.Count == 0 ? "未打标签" : string.Join(" · ", Tags);
}
