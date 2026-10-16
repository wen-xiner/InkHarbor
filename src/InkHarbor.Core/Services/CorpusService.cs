using InkHarbor.Core.Models;

namespace InkHarbor.Core.Services;

/// <summary>
/// 扫描范文库。软件不维护独立标签库，标签就是文件名切成的那几段。
/// </summary>
public sealed class CorpusService
{
    private static readonly string[] Extensions = { "*.md", "*.txt" };

    private readonly WorkspaceService _ws;

    public CorpusService(WorkspaceService ws) => _ws = ws;

    /// <summary>重新扫描 corpus 目录（用户可能在资源管理器里直接改过文件）。</summary>
    public IReadOnlyList<CorpusFile> Scan()
    {
        var list = new List<CorpusFile>();
        if (!Directory.Exists(_ws.CorpusDir))
        {
            return list;
        }

        foreach (var ext in Extensions)
        {
            foreach (var path in Directory.EnumerateFiles(_ws.CorpusDir, ext, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    list.Add(Build(path));
                }
                catch (IOException)
                {
                    // 单个文件被占用/读坏不该让整个库扫不出来，跳过。
                }
            }
        }

        return list
            .OrderBy(f => f.DisplayName, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>读范文全文，供拼提示词和预览用。</summary>
    public string ReadText(CorpusFile file) => File.ReadAllText(file.FullPath);

    /// <summary>按文件名找一篇，找不到返回 null。</summary>
    public CorpusFile? Find(string fileName) =>
        Scan().FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    private static CorpusFile Build(string path)
    {
        var text = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);
        var display = Path.GetFileNameWithoutExtension(fileName);
        var info = new FileInfo(path);

        return new CorpusFile
        {
            FileName = fileName,
            FullPath = path,
            DisplayName = display,
            Tags = ParseTags(display),
            SizeBytes = info.Length,
            WordCount = TextMetrics.CountWords(text),
            EstimatedTokens = TokenEstimator.Estimate(text),
            Preview = TextMetrics.Preview(text, 80)
        };
    }

    /// <summary>
    /// 约定命名：题材_人称_节奏_编号。最后一段只要含数字就按编号丢掉。
    /// </summary>
    private static IReadOnlyList<string> ParseTags(string displayName)
    {
        var parts = displayName
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (parts.Count > 1 && parts[^1].Any(char.IsDigit))
        {
            parts.RemoveAt(parts.Count - 1);
        }

        return parts;
    }
}
