using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomChatHudLegibilityTests
{
    [TestCase]
    public void Text_outline_is_applied_to_labels()
    {
        Label label = AutoFree(new Label { Text = "房间" })!;

        LanConnectHudLegibility.ApplyTextOutline(label);

        AssertThat(label.HasThemeColorOverride("font_outline_color")).IsTrue();
        AssertThat(label.GetThemeColor("font_outline_color").A)
            .IsEqual(LanConnectHudLegibility.OutlineColor.A);
        AssertThat(label.GetThemeConstant("outline_size"))
            .IsEqual(LanConnectHudLegibility.OutlineSize);
    }

    [TestCase]
    public void Text_outline_is_applied_to_buttons()
    {
        Button button = AutoFree(new Button { Text = "收起" })!;

        LanConnectHudLegibility.ApplyTextOutline(button);

        AssertThat(button.GetThemeConstant("outline_size"))
            .IsEqual(LanConnectHudLegibility.OutlineSize);
    }

    [TestCase]
    public void Hud_button_carries_all_four_state_styleboxes_including_focus()
    {
        Button button = AutoFree(new Button { Text = "固定" })!;

        LanConnectHudLegibility.ApplyHudButtonStyle(button, new Color(0.88f, 0.58f, 0.17f, 1f));

        AssertThat(button.HasThemeStyleboxOverride("normal")).IsTrue();
        AssertThat(button.HasThemeStyleboxOverride("hover")).IsTrue();
        AssertThat(button.HasThemeStyleboxOverride("pressed")).IsTrue();
        AssertThat(button.HasThemeStyleboxOverride("focus")).IsTrue();

        StyleBoxFlat normal = (StyleBoxFlat)button.GetThemeStylebox("normal");
        StyleBoxFlat hover = (StyleBoxFlat)button.GetThemeStylebox("hover");
        StyleBoxFlat focus = (StyleBoxFlat)button.GetThemeStylebox("focus");

        AssertThat(normal.BgColor.A).IsEqual(LanConnectHudLegibility.RestPlateColor.A);
        AssertThat(hover.BgColor.A).IsEqual(LanConnectHudLegibility.HoverPlateColor.A);
        AssertThat(focus.BorderWidthTop).IsEqual(2);
    }

    [TestCase]
    public void Hud_button_style_raises_touch_target_floor_and_enables_full_focus_mode()
    {
        Button button = AutoFree(new Button { Text = "收起", CustomMinimumSize = new Vector2(68f, 36f) })!;

        LanConnectHudLegibility.ApplyHudButtonStyle(button, new Color(0.88f, 0.58f, 0.17f, 1f));

        AssertThat(button.CustomMinimumSize.X).IsEqual(68f);
        AssertThat(button.CustomMinimumSize.Y).IsEqual(LanConnectHudLegibility.MinTouchTargetPixels);
        AssertThat(button.FocusMode).IsEqual(Control.FocusModeEnum.All);
    }
}
