namespace InkHarbor.Core.Models;

/// <summary>
/// 范文嵌入向量缓存，存在 cache\embeddings.json。删了整个文件也会自动重建。
/// 失效判定靠 文件名 + 大小 + 最后修改时间 三项比对，另外整份缓存记得住用的哪个模型，
/// 换了嵌入模型整份作废（不同模型的向量不在一个空间里，不能混着比）。
/// </summary>
public sealed class EmbeddingCache
{
    /// <summary>写这份缓存时用的嵌入模型标签。</summary>
    public string Model { get; set; } = "";

    public List<EmbeddingEntry> Entries { get; set; } = new();
}

/// <summary>某一篇范文的向量与它的失效指纹。</summary>
public sealed class EmbeddingEntry
{
    public string FileName { get; set; } = "";

    public long SizeBytes { get; set; }

    public DateTimeOffset LastWriteUtc { get; set; }

    public float[] Vector { get; set; } = Array.Empty<float>();

    /// <summary>这份记录是不是还能用：文件没被换过。</summary>
    public bool Matches(string fileName, long sizeBytes, DateTimeOffset lastWriteUtc) =>
        string.Equals(FileName, fileName, StringComparison.OrdinalIgnoreCase)
        && SizeBytes == sizeBytes
        && LastWriteUtc == lastWriteUtc
        && Vector.Length > 0;
}
