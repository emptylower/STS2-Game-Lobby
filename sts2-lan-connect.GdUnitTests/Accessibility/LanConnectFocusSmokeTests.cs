using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Accessibility;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectFocusSmokeTests
{
    [TestCase]
    public void PanelContainer_can_be_configured_as_focusable_room_card_surface()
    {
        PanelContainer card = AutoFree(new PanelContainer
        {
            Name = "RoomCard_room_123",
            FocusMode = Control.FocusModeEnum.All
        })!;
        Label label = new()
        {
            Name = "Label",
            Text = "房间 盲测房间，房主 房主A，人数 2/4，可加入",
            TopLevel = true,
            CustomMinimumSize = Vector2.Zero,
            Modulate = new Color(1f, 1f, 1f, 0f)
        };
        card.AddChild(label);

        AssertThat(card.FocusMode).IsEqual(Control.FocusModeEnum.All);
        AssertThat(card.Name.ToString()).IsEqual("RoomCard_room_123");
        AssertThat(label.Text).Contains("盲测房间");
        AssertThat(label.Visible).IsTrue();
        AssertThat(label.Modulate.A).IsEqual(0f);
    }

    [TestCase]
    public void Explicit_focus_neighbors_can_be_assigned_between_controls()
    {
        Button first = AutoFree(new Button { Name = "First", FocusMode = Control.FocusModeEnum.All })!;
        Button second = AutoFree(new Button { Name = "Second", FocusMode = Control.FocusModeEnum.All })!;
        Window root = ((SceneTree)Engine.GetMainLoop()).Root;
        root.AddChild(first);
        root.AddChild(second);

        first.FocusNext = second.GetPath();
        second.FocusPrevious = first.GetPath();

        AssertThat(first.FocusNext).IsEqual(second.GetPath());
        AssertThat(second.FocusPrevious).IsEqual(first.GetPath());
    }
}
