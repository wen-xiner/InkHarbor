namespace InkHarbor.Core.Models;

/// <summary>
/// 版本来源，决定版本历史里那一条显示成什么。
/// </summary>
public static class VersionSource
{
    /// <summary>模型生成，即 v1。</summary>
    public const string Generate = "generate";

    /// <summary>用户在编辑框里手动改完保存。</summary>
    public const string Manual = "manual";

    /// <summary>AI 修订采纳后保存。</summary>
    public const string Ai = "ai";

    /// <summary>回退到旧版本：把旧版本内容复制成的新版本（旧版本文件不动）。</summary>
    public const string Revert = "revert";
}
