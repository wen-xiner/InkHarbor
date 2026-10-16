namespace InkHarbor.Core.Models;

/// <summary>
/// 配方预设：一组常用范文的组合，存在 presets\&lt;名字&gt;.json。
/// </summary>
public sealed class Preset
{
    public string Name { get; set; } = "";

    /// <summary>范文文件名列表（不含路径），允许引用已被删掉的文件，加载时再过滤。</summary>
    public List<string> CorpusFiles { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
}
