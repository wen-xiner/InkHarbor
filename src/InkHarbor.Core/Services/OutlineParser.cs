namespace InkHarbor.Core.Services;

/// <summary>
/// 从大纲里抠"目标字数"。界面只有一个大纲输入框（按设计图），
/// 所以约定用户在大纲里写一句"约1500字"，这里解析出来拼进提示词。
/// </summary>
public static class OutlineParser
{
    /// <summary>没写目标字数时用这个值。</summary>
    public const int DefaultTargetWords = 1200;

    /// <summary>解析不出来返回 null。</summary>
    public static int? ExtractTargetWords(string? outline)
    {
        if (string.IsNullOrWhiteSpace(outline))
        {
            return null;
        }

        // 只认"1200字""约 1500 字""目标字数：1500"这类写法，别的数字（年份、年龄）不误伤。
        var m = System.Text.RegularExpressions.Regex.Match(
            outline,
            @"(\d{2,5})\s*(?:字|个字)");

        if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n is > 0 and <= 20000)
        {
            return n;
        }

        return null;
    }

    public static int ResolveTargetWords(string? outline) =>
        ExtractTargetWords(outline) ?? DefaultTargetWords;
}
