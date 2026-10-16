namespace InkHarbor.Core.Services;

/// <summary>
/// 本地 token 估算，不联网、不引 tokenizer 依赖。
/// </summary>
/// <remarks>
/// 说明：这是**估算**，不是精确值。Qwen3 的 BPE 对常用汉字大多 1 字 1 token、
/// 生僻字 2 token，取 0.75 作经验系数；ASCII 串按空白分词、每词约 1.3 token。
/// 一次真实生成后可以拿 Ollama 回传的 prompt_eval_count 反向校准
/// <see cref="CjkFactor"/>，M1 先用默认值。
/// </remarks>
public static class TokenEstimator
{
    /// <summary>每个汉字的经验 token 数。</summary>
    public static double CjkFactor { get; set; } = 0.75;

    /// <summary>每个英文/数字词的 token 数。</summary>
    public static double AsciiWordFactor { get; set; } = 1.3;

    public static int Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        double tokens = 0;
        var inAsciiRun = false;

        foreach (var ch in text)
        {
            if (IsCjk(ch))
            {
                tokens += CjkFactor;
                inAsciiRun = false;
            }
            else if (char.IsWhiteSpace(ch))
            {
                inAsciiRun = false;
            }
            else if (IsAsciiWordChar(ch))
            {
                if (!inAsciiRun)
                {
                    tokens += AsciiWordFactor;
                    inAsciiRun = true;
                }
            }
            else
            {
                tokens += 0.5;
                inAsciiRun = false;
            }
        }

        return (int)Math.Ceiling(tokens);
    }

    public static int EstimateMany(IEnumerable<string> texts)
    {
        var total = 0;
        foreach (var t in texts)
        {
            total += Estimate(t);
        }

        return total;
    }

    private static bool IsCjk(char ch)
    {
        return ch is >= '\u4e00' and <= '\u9fff'      // 基本汉字
            or >= '\u3400' and <= '\u4dbf'            // 扩展 A
            or >= '\uf900' and <= '\ufaff'            // 兼容汉字
            or >= '\u3000' and <= '\u303f'            // 中文标点
            or >= '\uff00' and <= '\uffef';           // 全角字符
    }

    private static bool IsAsciiWordChar(char ch)
    {
        return ch is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '\'';
    }
}
