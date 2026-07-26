using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

// Room chat HUD redesign spec §7: the chat bubble and the send button exist solely so touch
// players -- who have no keyboard fallback -- can reopen and use chat. This suite locks the
// overlay/panel wiring that consumes LanConnectPointerModeTracker (built in cfae08a) to decide
// their visibility.
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectPointerModeOverlayTests
{
    [TestCase]
    public async Task Touch_mode_keeps_the_bubble_visible_after_collapse_and_pressing_it_reopens_chat()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.ConfigurePointerModeForTests(LanConnectPointerMode.Touch);
        await fixture.Overlay.CloseForTests();

        Button toggle = FindNode<Button>(fixture.Overlay, "RoomChatToggleButton");
        AssertThat(toggle.Visible).IsTrue();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();

        toggle.EmitSignal(Button.SignalName.Pressed);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
    }

    [TestCase]
    public async Task Mouse_mode_hides_the_bubble_after_collapse_but_enter_still_reopens_chat()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.ConfigurePointerModeForTests(LanConnectPointerMode.Mouse);
        await fixture.Overlay.CloseForTests();

        Button toggle = FindNode<Button>(fixture.Overlay, "RoomChatToggleButton");
        AssertThat(toggle.Visible).IsFalse();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();

        fixture.Overlay.RouteKeyForTests(Key.Enter);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
    }

    [TestCase]
    public async Task Send_button_is_hidden_in_mouse_mode_and_visible_in_touch_mode()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();

        fixture.Overlay.ConfigurePointerModeForTests(LanConnectPointerMode.Mouse);
        AssertThat(FindNode<Button>(fixture.Overlay, LanConnectConstants.ChatSendButtonName).Visible).IsFalse();

        fixture.Overlay.ConfigurePointerModeForTests(LanConnectPointerMode.Touch);
        AssertThat(FindNode<Button>(fixture.Overlay, LanConnectConstants.ChatSendButtonName).Visible).IsTrue();
    }

    // Guard: the lobby sidebar is explicitly out of scope for pointer-mode adaptivity (spec
    // §2.2) -- its send button has no keyboard-shortcut affordance to fall back on, so it must
    // stay visible no matter what LanConnectBasicChatPanel.SetPointerMode is told. If someone
    // later drops the `UsesLobbyStyle ||` clause guarding _sendButton.Visible, this fails.
    [TestCase]
    public async Task Lobby_sidebar_send_button_stays_visible_regardless_of_pointer_mode()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel
        {
            ChatVisualStyle = LanConnectChatVisualStyle.LobbySidebar
        })!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        panel.SetPointerMode(LanConnectPointerMode.Mouse);
        AssertThat(FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).Visible).IsTrue();

        panel.SetPointerMode(LanConnectPointerMode.Touch);
        AssertThat(FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).Visible).IsTrue();
    }

    [TestCase]
    public async Task Touch_to_mouse_switch_is_withheld_while_panel_is_open_and_lands_once_it_closes()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.ConfigurePointerModeForTests(LanConnectPointerMode.Touch);
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        Button toggle = FindNode<Button>(fixture.Overlay, "RoomChatToggleButton");

        // A real (not synthetic) key event unconditionally requests Mouse mode (tracker rule,
        // locked by LanConnectPointerModeTrackerTests). The panel is open, so the switch must
        // be withheld rather than yanking the bubble out from under the player.
        fixture.Overlay.GetViewport().PushInput(new InputEventKey
        {
            Keycode = Key.Shift,
            Pressed = true,
            Echo = false
        });
        await fixture.Runner.AwaitInputProcessed();

        AssertThat(toggle.Visible).IsTrue();

        await fixture.Overlay.CloseForTests();

        AssertThat(toggle.Visible).IsFalse();
    }

    [TestCase]
    public async Task Fade_guard_holds_fully_faded_panel_reaches_zero_alpha_while_bubble_stays_opaque()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.ConfigurePointerModeForTests(LanConnectPointerMode.Touch);
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => true);
        clock.NowSeconds = 5d;

        fixture.Overlay.RefreshFadeForTests();

        AssertThat(fixture.Overlay.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Faded);
        CanvasItem panel = FindNode<PanelContainer>(fixture.Overlay, "RoomChatPanelFrame");
        CanvasItem toggle = FindNode<Button>(fixture.Overlay, "RoomChatToggleButton");
        AssertThat(EffectiveAlpha(panel)).IsEqual(0f);
        AssertThat(EffectiveAlpha(toggle)).IsEqual(1f);
    }

    private static float EffectiveAlpha(CanvasItem node)
    {
        float alpha = node.Modulate.A * node.SelfModulate.A;
        Node? parent = node.GetParent();
        while (parent is CanvasItem canvasParent)
        {
            alpha *= canvasParent.Modulate.A;
            parent = parent.GetParent();
        }
        return alpha;
    }

    private static T FindNode<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);

    private sealed class FakeClock : ILanConnectMonotonicClock
    {
        public double NowSeconds { get; set; }
    }
}
