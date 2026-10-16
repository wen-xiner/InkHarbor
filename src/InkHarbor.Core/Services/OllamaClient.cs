using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InkHarbor.Core.Models;

namespace InkHarbor.Core.Services;

public sealed class OllamaException : Exception
{
    public OllamaException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// 只连本机 Ollama（默认 localhost:11434）。
/// 这里**不出现任何云端地址或 API Key 字段**，是软件的一条红线。
/// </summary>
/// <remarks>
/// 关于 Qwen3 的思考块（2026-10-16 在本机 0.40.5 + qwen3:4b 实测）：
/// 这条模型的模板会在 assistant 回合开头无条件塞一个 <c>&lt;think&gt;</c>，
/// 所以模型**必然先思考一段**，再吐正文。
/// 实测结论：
/// 1. 请求里带 <c>think:false</c> → Ollama 不再分离思考，思考原文连同收尾标签
///    一起混进 content，正文被污染（实测输出里出现整段推理 + 收尾标签）。
/// 2. 请求里**不带** think → 思考走 message.thinking，content 是干净正文。
/// 3. 在消息里加 /no_think 软开关 → 无效，模型照想。
/// 所以这里**不发 think 字段**，并且只取 content、把 thinking 单独计数。
/// 代价是思考也占 num_predict 预算，NumPredict 要给足。
/// </remarks>
public sealed class OllamaClient : IDisposable
{
    public const string DefaultBaseUrl = "http://localhost:11434";

    /// <summary>Qwen3 思考块的收尾标签。收不到它说明思考还没结束。</summary>
    private const string ThinkClose = "</think>";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public OllamaClient(string baseUrl = DefaultBaseUrl, HttpClient? http = null)
    {
        BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        if (http is null)
        {
            // 流式生成可能跑几分钟，HttpClient 不能有整体超时；探测连接另用短超时。
            _http = new HttpClient { BaseAddress = BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _http.BaseAddress ??= BaseAddress;
            _ownsHttp = false;
        }
    }

    public Uri BaseAddress { get; }

    /// <summary>顶栏状态灯用。3 秒探不到就当离线。</summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var resp = await _http.GetAsync("api/tags", cts.Token).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>文风模式下拉用：本机已有模型标签。</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await _http.GetAsync("api/tags", cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var models = JsonNode.Parse(json)?["models"]?.AsArray();
            if (models is null)
            {
                return Array.Empty<string>();
            }

            return models
                .Select(m => m?["name"]?.GetValue<string>() ?? m?["model"]?.GetValue<string>() ?? "")
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 流式对话。只吐正文；思考块（Qwen3 会有）单独计数、不上屏。
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        GenerationOptions options,
        GenerationStats? stats = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var resp = await SendChatAsync(model, systemPrompt, userPrompt, options, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new OllamaException($"Ollama 返回 {(int)resp.StatusCode}：{Trim(body)}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var content = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonNode? chunk;
            try
            {
                chunk = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk?["error"] is { } err)
            {
                throw new OllamaException($"Ollama 报错：{err.GetValue<string>()}");
            }

            var message = chunk?["message"];

            // 思考内容不计入正文，只累计字数，让界面能显示"模型思考中…"。
            var thinking = message?["thinking"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(thinking) && stats is not null)
            {
                stats.ThinkingChars += thinking.Length;
            }

            var piece = message?["content"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(piece))
            {
                content.Append(piece);
                yield return piece;
            }

            if (chunk?["done"]?.GetValue<bool>() == true)
            {
                if (stats is not null)
                {
                    stats.PromptTokens = ReadInt(chunk, "prompt_eval_count");
                    stats.OutputTokens = ReadInt(chunk, "eval_count");
                    stats.DoneReason = chunk["done_reason"]?.GetValue<string>();
                    var evalNs = ReadLong(chunk, "eval_duration");
                    if (evalNs is > 0)
                    {
                        stats.EvalSeconds = evalNs / 1_000_000_000.0;
                    }
                }

                break;
            }
        }
    }

    private async Task<HttpResponseMessage> SendChatAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        GenerationOptions options,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["stream"] = true,
            ["messages"] = new object[]
            {
                new Dictionary<string, object?> { ["role"] = "system", ["content"] = systemPrompt },
                new Dictionary<string, object?> { ["role"] = "user", ["content"] = userPrompt }
            },
            ["options"] = new Dictionary<string, object?>
            {
                ["temperature"] = options.Temperature,
                ["num_ctx"] = options.NumCtx,
                ["num_predict"] = options.NumPredict,
                ["repeat_penalty"] = options.RepeatPenalty
            }
            // 故意不发 "think"：见类注释里的实测结论。
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat") { Content = content };

        return await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
    }

    private static int? ReadInt(JsonNode node, string key) =>
        node[key] is { } v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<int>() : null;

    private static long? ReadLong(JsonNode node, string key) =>
        node[key] is { } v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<long>() : null;

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300] + "…";

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
