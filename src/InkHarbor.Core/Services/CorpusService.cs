using System.Text;
using InkHarbor.Core.Models;

namespace InkHarbor.Core.Services;

/// <summary>
/// 扫描范文库。软件不维护独立标签库，标签就是文件名切成的那几段。
/// </summary>
public sealed class CorpusService
{
    private static readonly string[] Extensions = { "*.md", "*.txt" };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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

    // ---- 库管理（M3） ----

    /// <summary>新建一篇范文。重名直接抛错，不覆盖。</summary>
    public CorpusFile Create(string displayName, string text, string extension = ".md")
    {
        var name = WorkspaceService.SanitizeTitle(displayName);
        var ext = NormalizeExtension(extension);
        var path = Path.Combine(_ws.CorpusDir, name + ext);

        if (File.Exists(path))
        {
            throw new IOException($"范文库里已经有「{name + ext}」了。换个名字，或者直接改那一篇。");
        }

        Directory.CreateDirectory(_ws.CorpusDir);
        File.WriteAllText(path, text, Utf8NoBom);
        return Build(path);
    }

    /// <summary>改一篇范文的正文。文件名（也就是标签）不动。</summary>
    public CorpusFile SaveText(CorpusFile file, string text)
    {
        File.WriteAllText(file.FullPath, text, Utf8NoBom);
        return Build(file.FullPath);
    }

    /// <summary>
    /// 改标签其实就是改文件名。
    /// 注意：作品 meta.json 里的 corpusFiles 记的是文件名，调用方还得去
    /// <see cref="DraftService.ReplaceCorpusReference"/> 把老作品的引用一起改掉。
    /// </summary>
    public CorpusFile Rename(CorpusFile file, string newDisplayName)
    {
        var name = WorkspaceService.SanitizeTitle(newDisplayName);
        var ext = Path.GetExtension(file.FileName);
        var target = Path.Combine(_ws.CorpusDir, name + ext);

        if (string.Equals(target, file.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return file;
        }

        if (File.Exists(target))
        {
            throw new IOException($"范文库里已经有「{name + ext}」了。");
        }

        File.Move(file.FullPath, target);
        return Build(target);
    }

    /// <summary>
    /// 软删除：移到 corpus\_trash\ 里，不是真删。同名就加序号。
    /// 这样软件里点错了还能自己去捞回来。
    /// </summary>
    public string SoftDelete(CorpusFile file)
    {
        Directory.CreateDirectory(_ws.CorpusTrashDir);

        var target = MakeUniquePath(
            _ws.CorpusTrashDir,
            Path.GetFileNameWithoutExtension(file.FileName),
            Path.GetExtension(file.FileName));

        File.Move(file.FullPath, target);
        return target;
    }

    private static string NormalizeExtension(string extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? ".md" : extension.Trim();
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        return ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ? ".txt" : ".md";
    }

    private static string MakeUniquePath(string dir, string baseName, string ext)
    {
        var candidate = Path.Combine(dir, baseName + ext);
        var n = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, $"{baseName} ({n++}){ext}");
        }

        return candidate;
    }

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
