namespace InkHarbor.Core.Models;

/// <summary>
/// 传给 Ollama /api/chat 的 options。核显机器上 numCtx 别开太大，否则 prompt 处理会很慢。
/// </summary>
public sealed class GenerationOptions
{
    public double Temperature { get; set; } = 0.8;

    public int NumCtx { get; set; } = 8192;

    public int NumPredict { get; set; } = 2048;

    /// <summary>重复惩罚，小模型仿写时能压一压复读。</summary>
    public double RepeatPenalty { get; set; } = 1.05;
}

/// <summary>
/// 一次生成结束后 Ollama 回传的统计，用来校准 token 估算。
/// </summary>
public sealed class GenerationStats
{
    /// <summary>实际输入 token 数（prompt_eval_count），未回传时为 null。</summary>
    public int? PromptTokens { get; set; }

    /// <summary>实际输出 token 数（eval_count，**含思考块**）。</summary>
    public int? OutputTokens { get; set; }

    /// <summary>思考块的字数（Qwen3 会有；只统计不上屏）。</summary>
    public int ThinkingChars { get; set; }

    /// <summary>停止原因，Ollama 原样回传：stop / length / 等。length = 被 num_predict 截断。</summary>
    public string? DoneReason { get; set; }

    /// <summary>是否被 num_predict 截断（截断说明字数没写够）。</summary>
    public bool Truncated => string.Equals(DoneReason, "length", StringComparison.OrdinalIgnoreCase);

    /// <summary>输出阶段耗时（秒）。</summary>
    public double? EvalSeconds { get; set; }

    /// <summary>输出速度（token/s）。</summary>
    public double? TokensPerSecond =>
        EvalSeconds is > 0 && OutputTokens is not null ? OutputTokens / EvalSeconds : null;
}
