namespace InkHarbor.Core.Models;

/// <summary>
/// 版本来源。M1 只会产生 generate / manual；ai 给 M2 的 AI 修订预留，先在这里占位。
/// </summary>
public static class VersionSource
{
    /// <summary>模型生成，即 v1。</summary>
    public const string Generate = "generate";

    /// <summary>用户手动编辑保存。</summary>
    public const string Manual = "manual";

    /// <summary>AI 修订采纳后保存（M2 用，M1 不产生）。</summary>
    public const string Ai = "ai";
}
