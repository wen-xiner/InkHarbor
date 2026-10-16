using System.Text;
using InkHarbor.Core.Json;
using InkHarbor.Core.Models;

namespace InkHarbor.Core.Services;

/// <summary>
/// 底稿的落盘纪律，全部集中在这里：
/// 生成即落 v1，v1 只读永不覆盖；之后每保存一次产生一个新版本文件，谁都不许改老的；
/// 入终稿只是把当前版本复制进 final\，原文件原封不动。
/// </summary>
public sealed class DraftService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly WorkspaceService _ws;

    public DraftService(WorkspaceService ws) => _ws = ws;

    /// <summary>左栏列表用，按更新时间倒序。</summary>
    public IReadOnlyList<DraftSummary> ListDrafts()
    {
        var list = new List<DraftSummary>();
        if (!Directory.Exists(_ws.DraftsDir))
        {
            return list;
        }

        foreach (var dir in Directory.EnumerateDirectories(_ws.DraftsDir))
        {
            var meta = JsonStore.Read<DraftMeta>(Path.Combine(dir, "meta.json"));
            if (meta is null || string.IsNullOrWhiteSpace(meta.Title))
            {
                continue;
            }

            list.Add(new DraftSummary
            {
                Title = meta.Title,
                Status = meta.Status,
                CurrentVersion = meta.CurrentVersion,
                UpdatedAt = meta.UpdatedAt,
                Directory = dir
            });
        }

        return list.OrderByDescending(d => d.UpdatedAt).ToList();
    }

    public string DraftDir(string title) =>
        Path.Combine(_ws.DraftsDir, WorkspaceService.SanitizeTitle(title));

    public string MetaPath(string title) => Path.Combine(DraftDir(title), "meta.json");

    public string VersionPath(string title, int version) =>
        Path.Combine(DraftDir(title), $"v{version}.md");

    public DraftMeta? LoadMeta(string title) => JsonStore.Read<DraftMeta>(MetaPath(title));

    public string LoadVersionText(string title, int version)
    {
        var path = VersionPath(title, version);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    /// <summary>
    /// 生成完成后的第一步：建目录、写 v1.md、写 meta.json。
    /// 标题重名时自动加 " (2)"，绝不覆盖已有作品。
    /// </summary>
    public DraftMeta CreateDraft(
        string title,
        string outline,
        string model,
        IReadOnlyList<string> corpusFiles,
        string v1Text)
    {
        var dirName = MakeUniqueDirName(WorkspaceService.SanitizeTitle(title));
        var dir = Path.Combine(_ws.DraftsDir, dirName);
        Directory.CreateDirectory(dir);

        WriteVersionFile(Path.Combine(dir, "v1.md"), v1Text);

        var now = DateTimeOffset.Now;
        var meta = new DraftMeta
        {
            Title = dirName,
            CreatedAt = now,
            UpdatedAt = now,
            Status = DraftStatus.Draft,
            CurrentVersion = 1,
            Outline = outline,
            Model = model,
            CorpusFiles = corpusFiles.ToList(),
            Versions =
            {
                new DraftVersion
                {
                    Version = 1,
                    File = "v1.md",
                    Source = VersionSource.Generate,
                    CreatedAt = now,
                    Model = model,
                    WordCount = TextMetrics.CountWords(v1Text)
                }
            }
        };

        JsonStore.Write(Path.Combine(dir, "meta.json"), meta);
        return meta;
    }

    /// <summary>
    /// 把文本存成一个新版本（v2、v3…）。老版本文件绝不动。
    /// </summary>
    public DraftMeta AddVersion(
        DraftMeta meta,
        string text,
        string source,
        string note = "",
        string? model = null,
        string instruction = "",
        int? fromVersion = null)
    {
        var dir = DraftDir(meta.Title);
        var next = meta.CurrentVersion + 1;
        var fileName = $"v{next}.md";

        WriteVersionFile(Path.Combine(dir, fileName), text);

        var now = DateTimeOffset.Now;
        meta.Versions.Add(new DraftVersion
        {
            Version = next,
            File = fileName,
            Source = source,
            CreatedAt = now,
            Model = model ?? meta.Model,
            WordCount = TextMetrics.CountWords(text),
            Note = note,
            Instruction = instruction,
            FromVersion = fromVersion
        });

        meta.CurrentVersion = next;
        meta.Status = DraftStatus.Editing;
        meta.UpdatedAt = now;
        JsonStore.Write(MetaPath(meta.Title), meta);
        return meta;
    }

    /// <summary>
    /// 回退到某个旧版本：把那一版的内容**复制成新版本**落盘，当前版本前进一位。
    /// 旧版本文件一个都不删——「误改可回退」就是靠这个实现的：回退也是一次前进。
    /// </summary>
    public DraftMeta RevertToVersion(DraftMeta meta, int targetVersion)
    {
        var target = meta.Versions.FirstOrDefault(v => v.Version == targetVersion)
            ?? throw new InvalidOperationException($"《{meta.Title}》里没有 v{targetVersion}。");

        if (targetVersion == meta.CurrentVersion)
        {
            throw new InvalidOperationException($"v{targetVersion} 本来就是当前版本，不用回退。");
        }

        var text = LoadVersionText(meta.Title, targetVersion);
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException(
                $"v{target.Version} 的正文文件读不到：{VersionPath(meta.Title, targetVersion)}");
        }

        return AddVersion(
            meta,
            text,
            VersionSource.Revert,
            $"回退自 v{targetVersion}",
            meta.Model,
            instruction: "",
            fromVersion: targetVersion);
    }

    /// <summary>确认入终稿：把文本复制进 final\，同名加序号，原终稿永不覆盖。</summary>
    public string SaveAsFinal(DraftMeta meta, string text)
    {
        var target = MakeUniqueFilePath(_ws.FinalDir, WorkspaceService.SanitizeTitle(meta.Title), ".md");
        File.WriteAllText(target, text, Utf8NoBom);

        meta.Status = DraftStatus.Final;
        meta.FinalFile = Path.Combine("final", Path.GetFileName(target));
        meta.UpdatedAt = DateTimeOffset.Now;
        JsonStore.Write(MetaPath(meta.Title), meta);
        return target;
    }

    /// <summary>放弃本轮修改：磁盘上什么都没变，只把作品状态退回上一次落盘时的样子。</summary>
    public DraftMeta ReloadMeta(string title) => LoadMeta(title) ?? throw new InvalidOperationException($"作品 {title} 的 meta.json 读不到。");

    /// <summary>有多少部作品的记录里还引用着这篇范文（删范文前提醒用）。</summary>
    public int CountCorpusReferences(string fileName)
    {
        if (!Directory.Exists(_ws.DraftsDir))
        {
            return 0;
        }

        var count = 0;

        foreach (var dir in Directory.EnumerateDirectories(_ws.DraftsDir))
        {
            var meta = JsonStore.Read<DraftMeta>(Path.Combine(dir, "meta.json"));
            if (meta is not null && meta.CorpusFiles.Any(f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 范文改了名，把作品的 meta.json 里对它的引用一起改掉。
    /// 不做这一步的话，以后打开老稿会发现当初用的范文"不见了"。
    /// 返回改了几部作品。
    /// </summary>
    public int ReplaceCorpusReference(string oldFileName, string newFileName)
    {
        if (!Directory.Exists(_ws.DraftsDir))
        {
            return 0;
        }

        var changed = 0;

        foreach (var dir in Directory.EnumerateDirectories(_ws.DraftsDir))
        {
            var metaPath = Path.Combine(dir, "meta.json");
            var meta = JsonStore.Read<DraftMeta>(metaPath);
            if (meta is null)
            {
                continue;
            }

            var hit = false;
            for (var i = 0; i < meta.CorpusFiles.Count; i++)
            {
                if (string.Equals(meta.CorpusFiles[i], oldFileName, StringComparison.OrdinalIgnoreCase))
                {
                    meta.CorpusFiles[i] = newFileName;
                    hit = true;
                }
            }

            if (hit)
            {
                JsonStore.Write(metaPath, meta);
                changed++;
            }
        }

        return changed;
    }

    // ---- 内部工具 ----

    /// <summary>写版本文件。文件已存在就抛错——这是底稿不可变红线的最后一道闸。</summary>
    private static void WriteVersionFile(string path, string text)
    {
        if (File.Exists(path))
        {
            throw new IOException($"版本文件已存在，拒绝覆盖：{path}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Utf8NoBom);
    }

    private string MakeUniqueDirName(string baseName)
    {
        var candidate = baseName;
        var n = 2;
        while (Directory.Exists(Path.Combine(_ws.DraftsDir, candidate)))
        {
            candidate = $"{baseName} ({n++})";
        }

        return candidate;
    }

    private static string MakeUniqueFilePath(string dir, string baseName, string ext)
    {
        Directory.CreateDirectory(dir);
        var candidate = Path.Combine(dir, baseName + ext);
        var n = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, $"{baseName} ({n++}){ext}");
        }

        return candidate;
    }
}
