using System.Text;

namespace InkHarbor.Core.Services;

/// <summary>
/// 提示词拼接，严格照总体方案第五节的模板来。
/// </summary>
public static class PromptBuilder
{
    public const string DefaultSystemPrompt =
        "你是一位中文小说写作者，擅长模仿给定的文风写短篇。只输出正文，不要输出解释、标题或任何前后缀。";

    /// <summary>生成用的用户消息：[文风参考] + 范文 + [本次情节] + [要求]。</summary>
    public static string BuildGenerationPrompt(
        IReadOnlyList<string> corpusTexts,
        string outline,
        int targetWords)
    {
        var sb = new StringBuilder();

        sb.AppendLine("【文风参考】");
        sb.AppendLine("以下是范文，只学习它们的叙事节奏、句式习惯、用词风格，");
        sb.AppendLine("严禁沿用其中的人物、情节、具体设定。");
        sb.AppendLine();

        for (var i = 0; i < corpusTexts.Count; i++)
        {
            sb.Append("【范文").Append(i + 1).AppendLine("】");
            sb.AppendLine(corpusTexts[i].Trim());
            sb.AppendLine();
        }

        sb.AppendLine("【本次情节】");
        sb.AppendLine(outline.Trim());
        sb.AppendLine();

        sb.AppendLine("【要求】");
        sb.Append("按参考文风把上面的情节写成约").Append(targetWords).AppendLine("字的文章。");
        sb.Append("情节严格按大纲展开，不新增设定，不复述范文内容。");

        return sb.ToString();
    }

    /// <summary>
    /// 自查修订轮用的追问（方案第五节点名"质量关键，别省"）。
    /// M1 不触发，留给 M2 的 AI 修订直接复用。
    /// </summary>
    public static string BuildSelfCheckPrompt() =>
        "对照范文检查：1）句式节奏是否贴合；2）有没有渗入范文的人物、桥段或短语。列出问题并修订一版。";
}
