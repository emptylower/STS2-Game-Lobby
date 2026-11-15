using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectLobbyFonts
{
    private const string ChineseFontPath = "res://themes/fonts/zhs/noto_sans_mono_cjksc_regular_shared.tres";
    private static Font? _chineseFallback;

    internal static void Apply(Control root, Font? primaryFont = null)
    {
        root.Theme = CreateTheme(root.Theme, primaryFont ?? root.GetThemeDefaultFont());
    }

    internal static void Apply(Window root, Font? primaryFont = null)
    {
        root.Theme = CreateTheme(root.Theme, primaryFont ?? root.GetThemeDefaultFont());
    }

    private static Theme CreateTheme(Theme? existing, Font primaryFont)
    {
        Theme theme = existing == null ? new Theme() : (Theme)existing.Duplicate();
        theme.DefaultFont = WithChineseFallback(primaryFont);
        return theme;
    }

    internal static Font WithChineseFallback(Font primaryFont)
    {
        if (!SupportsChinese(primaryFont))
        {
            // Keep the existing Latin appearance. Plain Godot labels do not run the
            // game's per-control locale substitution used by MegaRichTextLabel.
            return new FontVariation
            {
                BaseFont = primaryFont,
                Fallbacks = [GetChineseFallback()]
            };
        }

        return primaryFont;
    }

    private static Font GetChineseFallback()
    {
        if (_chineseFallback != null)
        {
            return _chineseFallback;
        }

        // Reuse the game's shared font instead of bundling or duplicating its font data.
        Font? font = ResourceLoader.Exists(ChineseFontPath)
            ? ResourceLoader.Load<Font>(ChineseFontPath, cacheMode: ResourceLoader.CacheMode.Reuse)
            : null;
        if (font == null || !SupportsChinese(font))
        {
            // Older game builds may use a different resource layout.
            font = new SystemFont
            {
                FontNames = ["Noto Sans CJK SC", "Microsoft YaHei", "PingFang SC", "sans-serif"],
                AllowSystemFallback = true
            };
        }

        _chineseFallback = font;
        Log.Info($"sts2_lan_connect ui_font: Chinese fallback ready, glyphsAvailable={SupportsChinese(font)}.");
        return font;
    }

    internal static bool SupportsChinese(Font font) =>
        font.HasChar('中') && font.HasChar('文') && font.HasChar('房') && font.HasChar('间');
}
