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

        AppendCorpusBlock(sb, corpusTexts);

        sb.AppendLine("【本次情节】");
        sb.AppendLine(outline.Trim());
        sb.AppendLine();

        sb.AppendLine("【要求】");
        sb.Append("按参考文风把上面的情节写成约").Append(targetWords).AppendLine("字的文章。");
        sb.Append("情节严格按大纲展开，不新增设定，不复述范文内容。");

        return sb.ToString();
    }

    /// <summary>
    /// AI 修订用的用户消息：[文风参考]（可选）+ [当前稿件] + [修订指令] + [要求]。
    /// 带范文的目的是让 4B 在重写整篇时别把文风丢掉；不带就只按指令改。
    /// </summary>
    public static string BuildRevisionPrompt(
        IReadOnlyList<string> corpusTexts,
        string currentText,
        string instruction)
    {
        var sb = new StringBuilder();

        AppendCorpusBlock(sb, corpusTexts);

        sb.AppendLine("【当前稿件】");
        sb.AppendLine(currentText.Trim());
        sb.AppendLine();

        sb.AppendLine("【修订指令】");
        sb.AppendLine(instruction.Trim());
        sb.AppendLine();

        sb.AppendLine("【要求】");
        sb.AppendLine("以上面的当前稿件为基础，按修订指令改出一版完整的稿子。");
        sb.AppendLine("只输出修改后的全文，不要输出解释、说明、标题或任何前后缀，不要复述指令。");
        sb.Append("保持原有的段落划分和文风，除指令要求的地方外不要做无关改动。");

        return sb.ToString();
    }

    /// <summary>
    /// 自查修订轮的指令（方案第五节点名“质量关键，别省”）。
    /// 当作 <see cref="BuildRevisionPrompt"/> 的指令传进去即可，不需要用户手输。
    /// </summary>
    public static string BuildSelfCheckPrompt() =>
        "对照文风参考自查一遍：句式节奏是否贴合；有没有渗入范文的人物、桥段或短语；有没有前后矛盾或偏离情节的地方。"
        + "直接给出一版修订后的完整全文，只输出正文，不要列出问题清单，也不要任何解释。";

    /// <summary>范文块，生成和修订共用同一段措辞。</summary>
    private static void AppendCorpusBlock(StringBuilder sb, IReadOnlyList<string> corpusTexts)
    {
        if (corpusTexts.Count == 0)
        {
            return;
        }

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
    }
}
