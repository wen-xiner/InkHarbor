using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InkHarbor.Core.Json;
using InkHarbor.Core.Models;

namespace InkHarbor.Core.Services;

/// <summary>
/// 按大纲推范文靠它：把大纲和每篇范文都变成向量，比余弦相似度。
/// 和 <see cref="OllamaClient"/> 一样只连本机 Ollama（走 /api/embed），不碰任何云端。
/// </summary>
/// <remarks>
/// 向量缓存在 cache\embeddings.json。失效判定是"文件名 + 大小 + 最后修改时间"三项，
/// 所以用户在资源管理器里改了范文，那一篇会自动重算，其余照旧走缓存。
/// 缓存里还记着用的是哪个嵌入模型，换模型整份作废——不同模型的向量不在一个空间里。
/// </remarks>
public sealed class EmbeddingService : IDisposable
{
    /// <summary>默认嵌入模型。bge-m3 中文检索最准，1.2GB。</summary>
    public const string DefaultModel = "bge-m3";

    /// <summary>一篇最多取多少个字符去嵌入。bge-m3 上下文 8192，几千字的范文本来就装得下。</summary>
    private const int MaxCharsPerDoc = 6000;

    /// <summary>一次请求最多塞几篇，别把请求体撑太大。</summary>
    private const int BatchSize = 8;

    private readonly WorkspaceService _ws;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EmbeddingService(
        WorkspaceService ws,
        string baseUrl = OllamaClient.DefaultBaseUrl,
        HttpClient? http = null)
    {
        _ws = ws;

        if (http is null)
        {
            // 首次调用要把 1.2GB 的模型读进内存，给足时间。
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromMinutes(10)
            };
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _http.BaseAddress ??= new Uri(baseUrl.TrimEnd('/') + "/");
            _ownsHttp = false;
        }
    }

    public string CachePath => Path.Combine(_ws.CacheDir, "embeddings.json");

    // ---- 对外 ----

    /// <summary>嵌入模型装了没。没装就别往下走，省得等半天报个看不懂的错。</summary>
    public async Task<bool> IsModelReadyAsync(string model, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("api/tags", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var models = JsonNode.Parse(json)?["models"]?.AsArray();
            if (models is null)
            {
                return false;
            }

            return models.Any(m =>
            {
                var name = m?["name"]?.GetValue<string>() ?? m?["model"]?.GetValue<string>() ?? "";
                return name.Equals(model, StringComparison.OrdinalIgnoreCase)
                       || name.StartsWith(model + ":", StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 按大纲推荐范文。返回按相似度倒序的候选。
    /// </summary>
    /// <param name="files">范文库里的全部范文（一般十几篇，全算一遍也就几百毫秒）。</param>
    /// <param name="readText">读范文全文，通常传 CorpusService.ReadText。</param>
    /// <param name="topN">取前几篇。</param>
    /// <param name="progress">进度文字，界面拿去显示"正在算第 n/总共 m 篇"。</param>
    public async Task<IReadOnlyList<CorpusSuggestion>> SuggestAsync(
        string model,
        IReadOnlyList<CorpusFile> files,
        Func<CorpusFile, string> readText,
        string outline,
        int topN,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (files.Count == 0 || string.IsNullOrWhiteSpace(outline))
        {
            return Array.Empty<CorpusSuggestion>();
        }

        var vectors = await EnsureVectorsAsync(model, files, readText, progress, ct).ConfigureAwait(false);

        progress?.Report("正在把大纲变成向量…");
        var queryVector = (await EmbedAsync(model, outline, ct).ConfigureAwait(false)).FirstOrDefault();

        if (queryVector is null || queryVector.Length == 0)
        {
            return Array.Empty<CorpusSuggestion>();
        }

        var result = new List<CorpusSuggestion>();
        foreach (var file in files)
        {
            if (!vectors.TryGetValue(file.FileName, out var vector) || vector.Length != queryVector.Length)
            {
                continue;
            }

            result.Add(new CorpusSuggestion
            {
                FileName = file.FileName,
                DisplayName = file.DisplayName,
                TagText = file.TagText,
                WordCount = file.WordCount,
                Score = Cosine(queryVector, vector)
            });
        }

        return result
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.DisplayName, StringComparer.CurrentCulture)
            .Take(topN)
            .ToList();
    }

    /// <summary>余弦相似度。零向量返回 0，不抛异常。</summary>
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }

        if (na <= 0 || nb <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    // ---- 向量与缓存 ----

    private async Task<Dictionary<string, float[]>> EnsureVectorsAsync(
        string model,
        IReadOnlyList<CorpusFile> files,
        Func<CorpusFile, string> readText,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = LoadCache(model);
            var map = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            var stale = new List<CorpusFile>();

            foreach (var file in files)
            {
                var hit = cache.Entries.FirstOrDefault(e =>
                    e.Matches(file.FileName, file.SizeBytes, LastWriteUtc(file)));

                if (hit is not null)
                {
                    map[file.FileName] = hit.Vector;
                }
                else
                {
                    stale.Add(file);
                }
            }

            if (stale.Count > 0)
            {
                var fresh = new List<EmbeddingEntry>();

                for (var i = 0; i < stale.Count; i += BatchSize)
                {
                    ct.ThrowIfCancellationRequested();

                    var slice = stale.Skip(i).Take(BatchSize).ToList();
                    progress?.Report($"正在算范文向量（{Math.Min(i + BatchSize, stale.Count)}/{stale.Count} 篇要新算，其余走缓存）…");

                    var texts = slice.Select(f => Clip(readText(f))).ToList();
                    var vectors = await EmbedAsync(model, texts, ct).ConfigureAwait(false);

                    for (var k = 0; k < slice.Count && k < vectors.Count; k++)
                    {
                        map[slice[k].FileName] = vectors[k];
                        fresh.Add(new EmbeddingEntry
                        {
                            FileName = slice[k].FileName,
                            SizeBytes = slice[k].SizeBytes,
                            LastWriteUtc = LastWriteUtc(slice[k]),
                            Vector = vectors[k]
                        });
                    }
                }

                SaveCache(model, fresh);
            }

            return map;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 缓存只保留本次用到的那些篇（已经删掉的范文记录顺手清掉），
    /// 再和刚算好的合并——这样没参与本次推荐的老缓存不会丢。
    /// </summary>
    private void SaveCache(string model, IReadOnlyList<EmbeddingEntry> fresh)
    {
        try
        {
            var existing = LoadCache(model);
            var merged = new List<EmbeddingEntry>();

            foreach (var entry in fresh)
            {
                merged.Add(entry);
            }

            var freshNames = fresh.Select(e => e.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 老记录里，文件还在磁盘上、又不在本次重算之列的，保留。
            foreach (var entry in existing.Entries)
            {
                if (freshNames.Contains(entry.FileName))
                {
                    continue;
                }

                if (File.Exists(Path.Combine(_ws.CorpusDir, entry.FileName)))
                {
                    merged.Add(entry);
                }
            }

            JsonStore.Write(CachePath, new EmbeddingCache { Model = model, Entries = merged });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 缓存写不进去不该让推荐失败，下次重算就是了。
        }
    }

    private EmbeddingCache LoadCache(string model)
    {
        try
        {
            var cache = JsonStore.Read<EmbeddingCache>(CachePath);
            if (cache is null || !string.Equals(cache.Model, model, StringComparison.OrdinalIgnoreCase))
            {
                // 没有缓存，或者缓存是别的模型算的——整份作废。
                return new EmbeddingCache { Model = model };
            }

            return cache;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new EmbeddingCache { Model = model };
        }
    }

    private static DateTimeOffset LastWriteUtc(CorpusFile file)
    {
        var info = new FileInfo(file.FullPath);
        return info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) : DateTimeOffset.MinValue;
    }

    private static string Clip(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= MaxCharsPerDoc ? trimmed : trimmed[..MaxCharsPerDoc];
    }

    // ---- HTTP ----

    private Task<IReadOnlyList<float[]>> EmbedAsync(string model, string text, CancellationToken ct) =>
        EmbedAsync(model, new[] { text }, ct);

    private async Task<IReadOnlyList<float[]>> EmbedAsync(
        string model,
        IReadOnlyList<string> texts,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["input"] = texts.ToArray()
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.PostAsync("api/embed", content, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            if (body.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                throw new OllamaException(
                    $"Ollama 里没有嵌入模型「{model}」。先在终端跑一条：ollama pull {model}");
            }

            throw new OllamaException($"嵌入接口返回 {(int)resp.StatusCode}：{Shorten(body)}");
        }

        var embeddings = JsonNode.Parse(body)?["embeddings"]?.AsArray();
        if (embeddings is null)
        {
            throw new OllamaException("嵌入接口没有返回 embeddings 字段。");
        }

        var result = new List<float[]>(embeddings.Count);
        foreach (var item in embeddings)
        {
            if (item is not JsonArray arr)
            {
                continue;
            }

            var vector = new float[arr.Count];
            for (var i = 0; i < arr.Count; i++)
            {
                vector[i] = arr[i]!.GetValue<float>();
            }

            result.Add(vector);
        }

        return result;
    }

    private static string Shorten(string s) => s.Length <= 200 ? s : s[..200] + "…";

    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
