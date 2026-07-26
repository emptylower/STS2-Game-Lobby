using Godot;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// 浮在游戏画面之上的控件必须满足的可读性契约。
/// 局内背景不可预测（洞穴、火把亮区、米黄地图、战斗特效），不得假定背景为深色。
/// 详见 docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md §6。
/// </summary>
internal static class LanConnectHudLegibility
{
    internal const int OutlineSize = 3;
    internal const int MinTouchTargetPixels = 44;

    internal static readonly Color OutlineColor = new(0f, 0f, 0f, 0.8f);
    internal static readonly Color RestPlateColor = new(0.04f, 0.04f, 0.07f, 0.35f);
    internal static readonly Color HoverPlateColor = new(0.04f, 0.04f, 0.07f, 0.7f);
    internal static readonly Color PressedPlateColor = new(0.04f, 0.04f, 0.07f, 0.85f);

    internal static Vector2 EnsureTouchTarget(Vector2 requested) => new(
        Mathf.Max(requested.X, MinTouchTargetPixels),
        Mathf.Max(requested.Y, MinTouchTargetPixels));

    internal static void ApplyTextOutline(Control control)
    {
        control.AddThemeColorOverride("font_outline_color", OutlineColor);
        control.AddThemeConstantOverride("outline_size", OutlineSize);
    }

    internal static void ApplyHudButtonStyle(Button button, Color accent)
    {
        button.AddThemeStyleboxOverride("normal", Plate(RestPlateColor, Colors.Transparent, 0));
        button.AddThemeStyleboxOverride("hover", Plate(HoverPlateColor, accent, 1));
        button.AddThemeStyleboxOverride("pressed", Plate(PressedPlateColor, accent, 1));
        button.AddThemeStyleboxOverride("hover_pressed", Plate(PressedPlateColor, accent, 1));
        button.AddThemeStyleboxOverride("focus", Plate(RestPlateColor, accent, 2));
        button.CustomMinimumSize = EnsureTouchTarget(button.CustomMinimumSize);
        button.FocusMode = Control.FocusModeEnum.All;
        ApplyTextOutline(button);
    }

    private static StyleBoxFlat Plate(Color background, Color border, int borderWidth) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = borderWidth,
        BorderWidthTop = borderWidth,
        BorderWidthRight = borderWidth,
        BorderWidthBottom = borderWidth,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 10,
        ContentMarginTop = 6,
        ContentMarginRight = 10,
        ContentMarginBottom = 6
    };
}
