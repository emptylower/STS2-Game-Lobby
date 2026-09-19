using System;
using System.Collections.Generic;
using Godot;

namespace Sts2LanConnect.Scripts;

/// <summary>How panels and buttons cast shadow.</summary>
internal enum LanConnectLobbyShadowStyle
{
    /// <summary>Hard-edged offset shadow (pixel / arcade look).</summary>
    HardOffset,

    /// <summary>Soft blurred glow centred on the panel (glass / modern look).</summary>
    SoftGlow,

    /// <summary>No shadow at all (flat SaaS look).</summary>
    None
}

/// <summary>What the overlay paints behind everything.</summary>
internal enum LanConnectLobbyBackdropKind
{
    /// <summary>Single flat colour (<see cref="LanConnectLobbyPalette.Backdrop"/>).</summary>
    Solid,

    /// <summary>
    /// Procedural night sky: vertical gradient, stars, moon glow, vignette. If the pck ships
    /// <c>res://assets/themes/midnight_glass_bg.png</c> it is drawn on top of the gradient.
    /// </summary>
    NightSky
}

/// <summary>
/// Every named colour the lobby surface uses. Alpha is part of the colour: glass themes carry
/// translucent <see cref="Card"/> / <see cref="Secondary"/> values so panels show the backdrop through.
/// </summary>
internal sealed record LanConnectLobbyPalette
{
    // ── surfaces ──
    public required Color Backdrop { get; init; }
    public required Color Frame { get; init; }
    public required Color Surface { get; init; }
    public required Color SurfaceMuted { get; init; }
    public required Color Card { get; init; }
    public required Color CardSelected { get; init; }
    public required Color Secondary { get; init; }
    public required Color InputBg { get; init; }
    public required Color Border { get; init; }
    public required Color BorderStrong { get; init; }

    // ── accent / semantic ──
    public required Color Accent { get; init; }
    public required Color AccentBright { get; init; }
    public required Color AccentMuted { get; init; }
    public required Color PrimaryFg { get; init; }
    public required Color Success { get; init; }
    public required Color Warning { get; init; }
    public required Color Danger { get; init; }
    public required Color DangerHover { get; init; }

    // ── text ──
    public required Color TextStrong { get; init; }
    public required Color TextMuted { get; init; }

    // ── interaction ──
    /// <summary>Background of a non-primary button while hovered.</summary>
    public required Color HoverBg { get; init; }
    /// <summary>Text / icon colour of a non-primary button while hovered.</summary>
    public required Color HoverFg { get; init; }
    /// <summary>Background of a non-primary button while pressed.</summary>
    public required Color PressedBg { get; init; }
    /// <summary>Colour used by <see cref="LanConnectLobbyShadowStyle.SoftGlow"/> shadows and focus rings.</summary>
    public required Color Glow { get; init; }
    /// <summary>Dim veil behind modal dialogs.</summary>
    public required Color ModalVeil { get; init; }

    // ── theme-specific accents ──
    /// <summary>Second accent used for gradients (accent → accent secondary), e.g. violet next to blue.</summary>
    public required Color AccentSecondary { get; init; }
    /// <summary>Background of the "SELECT" badge on the selected room card.</summary>
    public required Color SelectBadgeBg { get; init; }
    /// <summary>Text colour of the "SELECT" badge.</summary>
    public required Color SelectBadgeFg { get; init; }
}

/// <summary>Geometry knobs shared by every panel / button of a theme.</summary>
internal sealed record LanConnectLobbyShape
{
    /// <summary>Corner radius in px (0 = square pixel corners).</summary>
    public required int CornerRadius { get; init; }
    /// <summary>Border width for large panels (header, sidebar cards, dialogs).</summary>
    public required int PanelBorderWidth { get; init; }
    /// <summary>Border width for buttons, inputs and small pills.</summary>
    public required int ControlBorderWidth { get; init; }
    public required LanConnectLobbyShadowStyle Shadow { get; init; }
    /// <summary>Hard shadow offset (HardOffset) or glow radius (SoftGlow) for large panels.</summary>
    public required int PanelShadowSize { get; init; }
    /// <summary>Hard shadow offset (HardOffset) or glow radius (SoftGlow) for buttons.</summary>
    public required int ControlShadowSize { get; init; }
    /// <summary>Draw the 1px inset bevel highlight/shadow inside pixel panels.</summary>
    public required bool InsetBevel { get; init; }
    /// <summary>Buttons physically sink into their shadow on hover/press (arcade look).</summary>
    public required bool PressDepth { get; init; }
    /// <summary>
    /// Panels are frosted glass: they blur what is behind them (screen-texture mip blur), tint it,
    /// add a top highlight and a soft outer glow. Requires <see cref="CornerRadius"/> &gt; 0.
    /// </summary>
    public bool Glass { get; init; }
    /// <summary>Mip level sampled from the screen texture for the frosted blur (higher = blurrier).</summary>
    public float GlassBlurLod { get; init; } = 2.5f;
}

internal sealed record LanConnectLobbyTheme
{
    /// <summary>Stable id persisted in config. Never rename.</summary>
    public required string Id { get; init; }
    /// <summary>Menu label (Chinese, matches the design prototypes).</summary>
    public required string DisplayName { get; init; }
    /// <summary>True when text is light on a dark surface.</summary>
    public required bool IsDark { get; init; }
    public required LanConnectLobbyPalette Palette { get; init; }
    public required LanConnectLobbyShape Shape { get; init; }
    public required LanConnectLobbyBackdropKind Backdrop { get; init; }
    /// <summary>
    /// Colour-code semantic tags on room cards (compat = green, unsupported = red, password = amber)
    /// instead of drawing every tag as a neutral outlined pill.
    /// </summary>
    public bool SemanticTags { get; init; }
}

/// <summary>
/// Registry + current selection for lobby themes. The overlay (and every sibling lobby surface)
/// reads colours through <see cref="Current"/>; a change is applied by rebuilding the overlay tree.
/// </summary>
internal static class LanConnectLobbyThemes
{
    public const string ArcadeRetroId = "arcade_retro";
    public const string MidnightGlassId = "midnight_glass";
    public const string SaasCleanId = "saas_clean";

    /// <summary>The look shipped before themes existed; stays the default for existing installs.</summary>
    public static readonly LanConnectLobbyTheme ArcadeRetro = new()
    {
        Id = ArcadeRetroId,
        DisplayName = "街机复古",
        IsDark = false,
        Backdrop = LanConnectLobbyBackdropKind.Solid,
        Shape = new LanConnectLobbyShape
        {
            CornerRadius = 0,
            PanelBorderWidth = 3,
            ControlBorderWidth = 2,
            Shadow = LanConnectLobbyShadowStyle.HardOffset,
            PanelShadowSize = 4,
            ControlShadowSize = 3,
            InsetBevel = true,
            PressDepth = true
        },
        Palette = new LanConnectLobbyPalette
        {
            Backdrop = new Color(0.97f, 0.95f, 0.89f, 1f),      // #F8F1E3
            Frame = new Color(0.80f, 0.65f, 0.53f, 1f),         // #CBA688
            Surface = new Color(0.99f, 0.97f, 0.93f, 1f),       // #FDF8ED
            SurfaceMuted = new Color(0.89f, 0.87f, 0.81f, 1f),  // #E4DDCF
            Card = new Color(0.99f, 0.97f, 0.93f, 1f),          // #FDF8ED
            CardSelected = new Color(0.978f, 0.914f, 0.837f, 1f), // card blended 10% with accent
            Secondary = new Color(0.93f, 0.89f, 0.82f, 1f),     // #ECE4D2
            InputBg = new Color(0.95f, 0.92f, 0.86f, 1f),       // #F1EADC
            Border = new Color(0.80f, 0.65f, 0.53f, 1f),        // #CBA688
            BorderStrong = new Color(0.80f, 0.65f, 0.53f, 1f),
            Accent = new Color(0.87f, 0.41f, 0.00f, 1f),        // #DF6900
            AccentBright = new Color(0.93f, 0.50f, 0.08f, 1f),  // #ED7F14
            AccentMuted = new Color(0.87f, 0.41f, 0.00f, 0.10f),
            PrimaryFg = new Color(0.15f, 0.05f, 0.00f, 1f),     // #270E01
            Success = new Color(0.10f, 0.60f, 0.19f, 1f),       // #189A30
            Warning = new Color(0.72f, 0.34f, 0.02f, 1f),
            Danger = new Color(0.80f, 0.15f, 0.18f, 1f),        // #CC272E
            DangerHover = new Color(0.65f, 0.10f, 0.12f, 1f),
            TextStrong = new Color(0.21f, 0.10f, 0.04f, 1f),    // #341A09
            TextMuted = new Color(0.46f, 0.36f, 0.31f, 1f),     // #775D4F
            HoverBg = new Color(0.10f, 0.60f, 0.19f, 1f),       // green hover (arcade signature)
            HoverFg = new Color(0.99f, 0.97f, 0.93f, 1f),
            PressedBg = new Color(0.10f, 0.60f, 0.19f, 0.8f),
            Glow = new Color(0.80f, 0.65f, 0.53f, 0.55f),
            ModalVeil = new Color(0f, 0f, 0f, 0.45f),
            AccentSecondary = new Color(0.93f, 0.50f, 0.08f, 1f),
            SelectBadgeBg = new Color(0.87f, 0.41f, 0.00f, 1f),
            SelectBadgeFg = new Color(0.15f, 0.05f, 0.00f, 1f)
        }
    };

    /// <summary>"午夜玻璃": dark indigo night sky, translucent glass panels, blue-violet accent.</summary>
    public static readonly LanConnectLobbyTheme MidnightGlass = new()
    {
        Id = MidnightGlassId,
        DisplayName = "午夜玻璃",
        IsDark = true,
        Backdrop = LanConnectLobbyBackdropKind.NightSky,
        Shape = new LanConnectLobbyShape
        {
            CornerRadius = 12,
            PanelBorderWidth = 1,
            ControlBorderWidth = 1,
            Shadow = LanConnectLobbyShadowStyle.SoftGlow,
            PanelShadowSize = 18,
            ControlShadowSize = 8,
            InsetBevel = false,
            PressDepth = false,
            Glass = true,
            GlassBlurLod = 1.1f
        },
        Palette = new LanConnectLobbyPalette
        {
            Backdrop = new Color(0.043f, 0.055f, 0.145f, 1f),          // #0B0E25
            Frame = new Color(0.36f, 0.42f, 0.85f, 0.30f),
            Surface = new Color(0.06f, 0.08f, 0.20f, 0.40f),
            SurfaceMuted = new Color(0.10f, 0.12f, 0.28f, 0.45f),
            Card = new Color(0.07f, 0.09f, 0.22f, 0.36f),              // light glass tint
            CardSelected = new Color(0.30f, 0.34f, 0.80f, 0.42f),      // lit glass
            Secondary = new Color(0.12f, 0.15f, 0.32f, 0.42f),
            InputBg = new Color(0.05f, 0.07f, 0.18f, 0.60f),
            Border = new Color(0.66f, 0.74f, 1.00f, 0.55f),            // bright glass rim
            BorderStrong = new Color(0.80f, 0.85f, 1.00f, 0.95f),
            Accent = new Color(0.36f, 0.42f, 1.00f, 1f),               // #5B6BFF
            AccentBright = new Color(0.48f, 0.53f, 1.00f, 1f),         // #7A88FF
            AccentMuted = new Color(0.36f, 0.42f, 1.00f, 0.16f),
            PrimaryFg = new Color(0.97f, 0.97f, 1.00f, 1f),
            Success = new Color(0.20f, 0.83f, 0.60f, 1f),              // #34D399
            Warning = new Color(0.98f, 0.75f, 0.30f, 1f),              // #FBBF4D
            Danger = new Color(0.97f, 0.44f, 0.44f, 1f),               // #F87171
            DangerHover = new Color(0.90f, 0.30f, 0.30f, 1f),
            TextStrong = new Color(0.91f, 0.93f, 1.00f, 1f),           // #E8ECFF
            TextMuted = new Color(0.60f, 0.64f, 0.78f, 1f),            // #9AA3C7
            HoverBg = new Color(0.36f, 0.42f, 1.00f, 0.22f),           // accent tint
            HoverFg = new Color(0.97f, 0.97f, 1.00f, 1f),
            PressedBg = new Color(0.36f, 0.42f, 1.00f, 0.38f),
            Glow = new Color(0.50f, 0.60f, 1.00f, 0.32f),
            ModalVeil = new Color(0.02f, 0.03f, 0.08f, 0.60f),
            AccentSecondary = new Color(0.55f, 0.36f, 1.00f, 1f),          // #8C5CFF violet
            SelectBadgeBg = new Color(0.20f, 0.83f, 0.60f, 1f),            // green badge like the prototype
            SelectBadgeFg = new Color(0.04f, 0.06f, 0.16f, 1f)
        }
    };

    /// <summary>"SaaS 简洁": light, flat, rounded, blue accent.</summary>
    public static readonly LanConnectLobbyTheme SaasClean = new()
    {
        Id = SaasCleanId,
        DisplayName = "SaaS 简洁",
        IsDark = false,
        Backdrop = LanConnectLobbyBackdropKind.Solid,
        SemanticTags = true,
        Shape = new LanConnectLobbyShape
        {
            CornerRadius = 10,
            PanelBorderWidth = 1,
            ControlBorderWidth = 1,
            Shadow = LanConnectLobbyShadowStyle.None,
            PanelShadowSize = 0,
            ControlShadowSize = 0,
            InsetBevel = false,
            PressDepth = false
        },
        Palette = new LanConnectLobbyPalette
        {
            Backdrop = new Color(0.96f, 0.97f, 0.98f, 1f),      // #F5F7FB
            Frame = new Color(0.89f, 0.91f, 0.94f, 1f),
            Surface = new Color(1f, 1f, 1f, 1f),
            SurfaceMuted = new Color(0.95f, 0.96f, 0.98f, 1f),  // #F2F4F8
            Card = new Color(1f, 1f, 1f, 1f),
            CardSelected = new Color(0.93f, 0.95f, 1.00f, 1f),  // #EEF3FF
            Secondary = new Color(0.96f, 0.97f, 0.99f, 1f),
            InputBg = new Color(1f, 1f, 1f, 1f),
            Border = new Color(0.89f, 0.91f, 0.94f, 1f),        // #E3E8F0
            BorderStrong = new Color(0.15f, 0.39f, 0.92f, 1f),  // accent
            Accent = new Color(0.15f, 0.39f, 0.92f, 1f),        // #2563EB
            AccentBright = new Color(0.23f, 0.47f, 0.96f, 1f),  // #3B77F5
            AccentMuted = new Color(0.15f, 0.39f, 0.92f, 0.10f),
            PrimaryFg = new Color(1f, 1f, 1f, 1f),
            Success = new Color(0.09f, 0.64f, 0.29f, 1f),       // #16A34A
            Warning = new Color(0.85f, 0.47f, 0.02f, 1f),       // #D97706
            Danger = new Color(0.86f, 0.15f, 0.15f, 1f),        // #DC2626
            DangerHover = new Color(0.73f, 0.11f, 0.11f, 1f),
            TextStrong = new Color(0.06f, 0.09f, 0.16f, 1f),    // #0F172A
            TextMuted = new Color(0.39f, 0.45f, 0.55f, 1f),     // #64748B
            HoverBg = new Color(0.93f, 0.95f, 1.00f, 1f),       // #EEF3FF
            HoverFg = new Color(0.15f, 0.39f, 0.92f, 1f),
            PressedBg = new Color(0.86f, 0.90f, 1.00f, 1f),
            Glow = new Color(0.15f, 0.39f, 0.92f, 0.18f),
            ModalVeil = new Color(0.06f, 0.09f, 0.16f, 0.40f),
            AccentSecondary = new Color(0.23f, 0.47f, 0.96f, 1f),
            SelectBadgeBg = new Color(0.15f, 0.39f, 0.92f, 1f),
            SelectBadgeFg = new Color(1f, 1f, 1f, 1f)
        }
    };

    public static IReadOnlyList<LanConnectLobbyTheme> All { get; } = [ArcadeRetro, MidnightGlass, SaasClean];

    public static LanConnectLobbyTheme Default => ArcadeRetro;

    private static readonly object Sync = new();
    private static LanConnectLobbyTheme _current = Default;
    private static bool _loadedFromConfig;

    /// <summary>Raised on the caller's thread after <see cref="Current"/> changes.</summary>
    public static event Action<LanConnectLobbyTheme>? Changed;

    public static LanConnectLobbyTheme Current
    {
        get
        {
            lock (Sync)
            {
                if (!_loadedFromConfig)
                {
                    _loadedFromConfig = true;
                    if (TryFind(LanConnectConfig.LobbyThemeId, out LanConnectLobbyTheme? persisted))
                    {
                        _current = persisted;
                    }
                }

                return _current;
            }
        }
    }

    public static bool TryFind(string? id, out LanConnectLobbyTheme theme)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            foreach (LanConnectLobbyTheme candidate in All)
            {
                if (string.Equals(candidate.Id, id.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    theme = candidate;
                    return true;
                }
            }
        }

        theme = Default;
        return false;
    }

    /// <summary>
    /// Switches the active theme, persists it, and raises <see cref="Changed"/>.
    /// Returns false when the id is unknown or already active.
    /// </summary>
    public static bool Select(string id, bool persist = true)
    {
        if (!TryFind(id, out LanConnectLobbyTheme next))
        {
            return false;
        }

        lock (Sync)
        {
            _loadedFromConfig = true;
            if (ReferenceEquals(_current, next))
            {
                return false;
            }

            _current = next;
        }

        if (persist)
        {
            LanConnectConfig.LobbyThemeId = next.Id;
        }

        Changed?.Invoke(next);
        return true;
    }

    /// <summary>Test hook: force a theme without touching config.</summary>
    internal static void SetForTests(LanConnectLobbyTheme theme)
    {
        lock (Sync)
        {
            _loadedFromConfig = true;
            _current = theme;
        }
    }
}
