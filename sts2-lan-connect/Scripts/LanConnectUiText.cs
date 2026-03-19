namespace Sts2LanConnect.Scripts;

internal static class LanConnectUiText
{
    private static readonly bool RequiresGlyphFallback = OperatingSystem.IsAndroid();

    public static string NormalizeForDisplay(string text)
    {
        if (!RequiresGlyphFallback || string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Some Android builds render these punctuation/symbol glyphs as tofu boxes in the game's default font stack.
        return text
            .Replace("● ", "* ")
            .Replace("●", "*")
            .Replace("。", ".")
            .Replace("，", ",")
            .Replace("：", ":")
            .Replace("；", ";")
            .Replace("！", "!")
            .Replace("？", "?")
            .Replace("（", "(")
            .Replace("）", ")")
            .Replace("“", "\"")
            .Replace("”", "\"")
            .Replace("‘", "'")
            .Replace("’", "'")
            .Replace("·", " | ")
            .Replace("→", " -> ")
            .Replace("…", "...");
    }
}
