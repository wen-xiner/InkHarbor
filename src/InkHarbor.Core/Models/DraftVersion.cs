namespace InkHarbor.Core.Models;

/// <summary>
/// 一个版本的文件记录。所有版本文件一经写入就不再修改，回退靠"复制成新版本"实现。
/// </summary>
public sealed class DraftVersion
{
    /// <summary>版本号，从 1 开始。</summary>
    public int Version { get; set; }

    /// <summary>版本文件名，如 v1.md。</summary>
    public string File { get; set; } = "";

    /// <summary>来源，取 <see cref="VersionSource"/> 中的常量。</summary>
    public string Source { get; set; } = VersionSource.Generate;

    /// <summary>该版本的生成/保存时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>产出该版本所用的模型标签（手动编辑时沿用上一版）。</summary>
    public string Model { get; set; } = "";

    /// <summary>字数（非空白字符数）。</summary>
    public int WordCount { get; set; }

    /// <summary>备注，M2 用来记修订指令。</summary>
    public string Note { get; set; } = "";
}
