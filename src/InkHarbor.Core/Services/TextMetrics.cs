namespace InkHarbor.Core.Services;

/// <summary>
/// 文本计量。中文语境下"字数"按非空白字符数算，不含空格和换行。
/// </summary>
public static class TextMetrics
{
    /// <summary>非空白字符数，即界面上说的"字数"。</summary>
    public static int CountWords(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var n = 0;
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>取正文前若干字的预览，把换行压成空格。</summary>
    public static string Preview(string? text, int maxChars = 80)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var flat = text.Replace("\r", " ").Replace("\n", " ");
        while (flat.Contains("  "))
        {
            flat = flat.Replace("  ", " ");
        }

        flat = flat.Trim();
        return flat.Length <= maxChars ? flat : flat[..maxChars] + "…";
    }
}
