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
}
