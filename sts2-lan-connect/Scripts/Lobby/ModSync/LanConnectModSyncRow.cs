using System;
using System.Collections.Generic;
using Godot;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectModSyncRow : PanelContainer
{
    // Colours follow the lobby theme. The default arcade theme keeps this dialog's original
    // "printed manifest" palette (darker brown rules than the lobby chrome) so existing installs
    // look exactly as before; every other theme maps onto its palette.
    private static Color CardColor => LanConnectModSyncPalette.Card;
    private static Color SurfaceMutedColor => LanConnectModSyncPalette.SurfaceMuted;
    private static Color BorderColor => LanConnectModSyncPalette.Border;
    private static Color TextStrongColor => LanConnectModSyncPalette.TextStrong;
    private static Color TextMutedColor => LanConnectModSyncPalette.TextMuted;

    private readonly LanConnectModSyncRowState _state;
    private CheckBox? _selector;

    internal LanConnectModSyncRow(LanConnectModSyncRowState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        Name = "ModSyncRow_" + SanitizeName(state.Descriptor.Id);
        CustomMinimumSize = new Vector2(0f, state.Job == null ? 68f : 88f);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", PixelStyle(
            state.Selectable ? CardColor : SurfaceMutedColor,
            BorderColor,
            borderWidth: 2,
            padding: 10));
    }

    internal event Action<LanConnectModSyncRow, bool>? SelectionChanged;

    internal LobbyModDescriptor Descriptor => _state.Descriptor;

    internal bool Selected => _selector?.ButtonPressed == true;

    public override void _Ready()
    {
        HBoxContainer layout = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        layout.AddThemeConstantOverride("separation", 10);
        AddChild(layout);

        if (_state.Selectable)
        {
            _selector = new CheckBox
            {
                Name = "ModSyncSelect_" + SanitizeName(_state.Descriptor.Id),
                ButtonPressed = _state.Selected,
                FocusMode = FocusModeEnum.All,
                AccessibilityName = $"选择禁用 MOD {_state.Descriptor.Id}",
                TooltipText = $"选择后将在二次确认时禁用 {_state.Descriptor.Id}",
                CustomMinimumSize = new Vector2(42f, 42f),
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            LanConnectModSyncPalette.ApplyCheckBoxIcons(_selector);
            _selector.Toggled += selected => SelectionChanged?.Invoke(this, selected);
            layout.AddChild(_selector);
        }

        VBoxContainer text = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        text.AddThemeConstantOverride("separation", 3);
        layout.AddChild(text);

        string visibleTitle = _state.Job?.Metadata.Title is { Length: > 0 } jobTitle
            ? jobTitle
            : _state.Metadata?.Title is { Length: > 0 } metadataTitle
                ? metadataTitle
                : _state.Descriptor.Id;
        Label title = LabelFor(
            LimitVisibleText(visibleTitle, 36),
            17,
            TextStrongColor);
        title.Name = "ModSyncRowTitle";
        title.AccessibilityName = $"MOD {_state.Descriptor.Id}";
        text.AddChild(title);

        string source = _state.Descriptor.Source == LanConnectModSources.SteamWorkshop
            ? $"Steam Workshop {_state.Descriptor.WorkshopFileId}"
            : "本地 MOD";
        string detailText = $"{_state.Descriptor.Id}  ·  {source}  ·  房主版本 {_state.Descriptor.Version}";
        LanConnectWorkshopMetadata? metadata = _state.Metadata ?? _state.Job?.Metadata;
        if (metadata != null)
        {
            detailText += $"  ·  发布者 {metadata.Publisher}";
        }
        if (_state.Job != null)
        {
            detailText += $"  ·  {DescribeJob(_state.Job)}";
        }
        Label detail = LabelFor(LimitVisibleText(detailText, 52), 13, TextMutedColor);
        detail.Name = "ModSyncRowDetail";
        detail.AccessibilityName = detailText;
        detail.TooltipText = detailText;
        text.AddChild(detail);

        if (_state.Job != null)
        {
            ProgressBar progress = new()
            {
                Name = "ModSyncRowProgress",
                MinValue = 0,
                MaxValue = Math.Max(1d, _state.Job.BytesTotal),
                Value = _state.Job.BytesDownloaded,
                ShowPercentage = _state.Job.BytesTotal > 0,
                CustomMinimumSize = new Vector2(0f, 12f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AccessibilityName = $"{_state.Descriptor.Id} 下载进度"
            };
            text.AddChild(progress);
        }
    }

    internal void SetSelectedForTests(bool selected)
    {
        if (_selector == null)
        {
            throw new InvalidOperationException("Row is not selectable or is not ready.");
        }
        _selector.ButtonPressed = selected;
    }

    private static Label LabelFor(string value, int size, Color color)
    {
        Label label = new()
        {
            Text = value,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.CustomMinimumSize = new Vector2(0f, size + 6f);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static string DescribeJob(LanConnectWorkshopJobSnapshot job)
    {
        if (job.State == LanConnectWorkshopJobState.Downloading && job.BytesTotal > 0)
        {
            double percent = Math.Clamp(job.BytesDownloaded * 100d / job.BytesTotal, 0d, 100d);
            return $"下载 {percent:0}%";
        }
        return job.State switch
        {
            LanConnectWorkshopJobState.Pending => "等待中",
            LanConnectWorkshopJobState.Validating => "正在验证",
            LanConnectWorkshopJobState.Subscribing => "正在订阅",
            LanConnectWorkshopJobState.Downloading => "正在下载",
            LanConnectWorkshopJobState.WaitingInstall => "正在验证安装",
            LanConnectWorkshopJobState.Installed => "已安装",
            LanConnectWorkshopJobState.Failed => "失败，可重试",
            LanConnectWorkshopJobState.TimedOut => "已超时，可重试",
            LanConnectWorkshopJobState.Canceled => "已取消",
            _ => job.State.ToString()
        };
    }

    private static string SanitizeName(string value)
    {
        string result = new(value.Where(character => char.IsLetterOrDigit(character) || character is '_' or '-').Take(48).ToArray());
        return string.IsNullOrEmpty(result) ? "item" : result;
    }

    private static string LimitVisibleText(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : value[..Math.Max(1, maximumCharacters - 1)] + "…";

    internal static StyleBoxFlat PixelStyle(
        Color background,
        Color border,
        int borderWidth,
        int padding,
        int shadowSize = 0)
    {
        LanConnectLobbyShape shape = LanConnectLobbyThemes.Current.Shape;
        bool pixel = shape.PressDepth;
        int width = pixel
            ? borderWidth
            : borderWidth >= 3 ? Math.Max(1, shape.PanelBorderWidth) : borderWidth > 0 ? Math.Max(1, shape.ControlBorderWidth) : 0;
        int radius = shape.CornerRadius;
        StyleBoxFlat style = new()
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = width,
            BorderWidthTop = width,
            BorderWidthRight = width,
            BorderWidthBottom = width,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            AntiAliasing = radius > 0,
            // Keep the content box identical across themes: a thinner border gets its pixels back as padding.
            ContentMarginLeft = padding + (borderWidth - width),
            ContentMarginTop = padding + (borderWidth - width),
            ContentMarginRight = padding + (borderWidth - width),
            ContentMarginBottom = padding + (borderWidth - width)
        };
        switch (shape.Shadow)
        {
            case LanConnectLobbyShadowStyle.HardOffset:
                style.ShadowColor = new Color(0.12f, 0.06f, 0.02f, 0.24f);
                style.ShadowSize = shadowSize;
                break;
            case LanConnectLobbyShadowStyle.SoftGlow:
                style.ShadowColor = LanConnectLobbyThemes.Current.Palette.Glow;
                style.ShadowSize = shadowSize >= 5 ? shape.PanelShadowSize : shadowSize > 0 ? shape.ControlShadowSize : 0;
                break;
            default:
                style.ShadowSize = 0;
                break;
        }

        return style;
    }
}

/// <summary>
/// Palette shared by the MOD preflight dialog and its rows. Arcade keeps the dialog's original
/// colours; other lobby themes map onto their palette, with an opaque panel so a modal full of
/// small text stays readable over translucent glass.
/// </summary>
internal static class LanConnectModSyncPalette
{
    private static LanConnectLobbyTheme Theme => LanConnectLobbyThemes.Current;
    private static LanConnectLobbyPalette P => Theme.Palette;
    private static bool Legacy => ReferenceEquals(Theme, LanConnectLobbyThemes.ArcadeRetro);

    private static Color Opaque(Color color, Color over)
    {
        float a = color.A;
        return new Color(color.R * a + over.R * (1f - a), color.G * a + over.G * (1f - a), color.B * a + over.B * (1f - a), 1f);
    }

    public static Color Page => Legacy ? new Color(0.94f, 0.92f, 0.87f, 1f) : Opaque(P.SurfaceMuted, P.Backdrop);
    public static Color Card => Legacy ? new Color(0.99f, 0.97f, 0.93f, 1f) : Opaque(P.Surface, P.Backdrop);
    public static Color SurfaceMuted => Legacy ? new Color(0.89f, 0.87f, 0.81f, 1f) : Opaque(P.Secondary, P.Backdrop);
    public static Color Border => Legacy ? new Color(0.28f, 0.16f, 0.08f, 1f) : P.Border;
    public static Color TextStrong => Legacy ? new Color(0.21f, 0.10f, 0.04f, 1f) : P.TextStrong;
    public static Color TextMuted => Legacy ? new Color(0.42f, 0.34f, 0.25f, 1f) : P.TextMuted;
    public static Color Accent => Legacy ? new Color(0.67f, 0.24f, 0.12f, 1f) : P.Accent;
    public static Color AccentHover => Legacy ? new Color(0.16f, 0.42f, 0.23f, 1f) : P.AccentBright;
    public static Color Success => Legacy ? new Color(0.16f, 0.42f, 0.23f, 1f) : P.Success;
    public static Color PrimaryFg => Legacy ? new Color(0.99f, 0.97f, 0.93f, 1f) : P.PrimaryFg;
    /// <summary>Icon on the accent-filled primary button (arcade kept its original dark glyph).</summary>
    public static Color PrimaryIcon => Legacy ? new Color(0.21f, 0.10f, 0.04f, 1f) : P.PrimaryFg;
    /// <summary>
    /// Godot's stock checkbox glyphs are dark grey and vanish on dark themes. Non-arcade themes get
    /// palette-coloured glyphs: a bright hollow box, and an accent-filled box with a tick.
    /// </summary>
    public static void ApplyCheckBoxIcons(CheckBox box)
    {
        if (Legacy)
        {
            return;
        }

        ImageTexture off = CheckGlyph(false);
        ImageTexture on = CheckGlyph(true);
        foreach (string name in new[] { "unchecked", "unchecked_disabled" })
        {
            box.AddThemeIconOverride(name, off);
        }

        foreach (string name in new[] { "checked", "checked_disabled" })
        {
            box.AddThemeIconOverride(name, on);
        }
    }

    private static readonly Dictionary<string, ImageTexture> CheckGlyphCache = new();

    private static ImageTexture CheckGlyph(bool isChecked)
    {
        string key = $"{Theme.Id}:{isChecked}";
        if (CheckGlyphCache.TryGetValue(key, out ImageTexture? cached))
        {
            return cached;
        }

        const int size = 20;
        const int radius = 4;
        Color rim = isChecked ? P.Accent : new Color(P.TextMuted, 0.9f);
        Color fill = isChecked ? P.Accent : new Color(P.TextMuted, 0.10f);
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // distance outside the rounded square, in px
                float dx = Math.Max(Math.Max(radius - x, x - (size - 1 - radius)), 0);
                float dy = Math.Max(Math.Max(radius - y, y - (size - 1 - radius)), 0);
                float corner = MathF.Sqrt(dx * dx + dy * dy);
                if (corner > radius + 0.5f)
                {
                    continue;
                }

                int edge = Math.Min(Math.Min(x, size - 1 - x), Math.Min(y, size - 1 - y));
                bool onRim = edge < 2 || corner > radius - 1.5f;
                image.SetPixel(x, y, onRim ? rim : fill);
            }
        }

        if (isChecked)
        {
            // tick: (5,10) → (8,13) → (14,6), 2px thick
            (int X, int Y)[] points = [(5, 10), (8, 13), (14, 6)];
            for (int i = 0; i < points.Length - 1; i++)
            {
                (int x0, int y0) = points[i];
                (int x1, int y1) = points[i + 1];
                int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 2;
                for (int step = 0; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    int px = (int)MathF.Round(x0 + (x1 - x0) * t);
                    int py = (int)MathF.Round(y0 + (y1 - y0) * t);
                    image.SetPixel(px, py, P.PrimaryFg);
                    image.SetPixel(px, Math.Min(size - 1, py + 1), P.PrimaryFg);
                }
            }
        }

        ImageTexture texture = ImageTexture.CreateFromImage(image);
        CheckGlyphCache[key] = texture;
        return texture;
    }

    public static Color Veil => Legacy ? new Color(0f, 0f, 0f, 0.48f) : P.ModalVeil;
}
