using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

/// <summary>
/// Regression suite for the "no chat at all, ever" desktop bug: the room chat HUD redesign gave
/// the overlay an explicit open/closed state (unlike the reference mod, whose message log is
/// simply visible whenever there are messages) but nothing ever reopened it when a message
/// arrived while it was closed, and on desktop the touch-only toggle bubble is hidden too, so a
/// closed overlay had no way back. <see cref="LanConnectDualChatState.HasUnseenRemoteArrival"/>
/// and <see cref="LanConnectRoomChatOverlay"/>'s private MaybeSurfaceForRemoteArrival reproduce
/// the reference mod's behaviour (surface on arrival, let the existing idle fade take it away
/// again) without restructuring the open/close model.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomChatAutoSurfaceTests
{
    [TestCase]
    public async Task Remote_arrival_while_closed_surfaces_the_overlay()
    {
        using RoomChatFixture fixture = await RoomChatFixture.CreateNeverOpenedWithServerSupport();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();

        fixture.State.Room.AppendConfirmedForTests("remote-arrival-1", "Ally", "hello", 1, isLocal: false);
        await fixture.Overlay.RefreshForTests();

        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        AssertThat(fixture.Overlay.TestState.PanelVisible).IsTrue();
    }

    [TestCase]
    public async Task Local_outgoing_message_while_closed_does_not_surface_the_overlay()
    {
        using RoomChatFixture fixture = await RoomChatFixture.CreateNeverOpenedWithServerSupport();

        fixture.State.Room.AppendConfirmedForTests("local-outgoing-1", "Me", "hi", 1, isLocal: true);
        await fixture.Overlay.RefreshForTests();

        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();
        AssertThat(fixture.Overlay.TestState.PanelVisible).IsFalse();
    }

    [TestCase]
    public async Task Surfacing_preserves_the_selected_server_channel_instead_of_switching_to_room()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        await fixture.Overlay.CloseForTests();
        AssertThat(fixture.Overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Server);

        fixture.State.Server.AppendConfirmedForTests("server-arrival-1", "Ally", "频道消息", 1, isLocal: false);
        await fixture.Overlay.RefreshForTests();

        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        AssertThat(fixture.Overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Server);
    }

    [TestCase]
    public async Task Idle_fade_still_takes_a_surfaced_panel_to_zero_alpha()
    {
        using RoomChatFixture fixture = await RoomChatFixture.CreateNeverOpenedWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => true);

        fixture.State.Room.AppendConfirmedForTests("remote-arrival-1", "Ally", "hello", 1, isLocal: false);
        await fixture.Overlay.RefreshForTests();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();

        clock.NowSeconds = 5d;
        fixture.Overlay.RefreshFadeForTests();

        AssertThat(fixture.Overlay.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Faded);
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(0f);
    }

    // The regression that matters most: on desktop, IsTouchPointerMode is false, so the toggle
    // bubble (the only touch reopen affordance) is hidden by ApplyPointerModeVisibility. Before
    // this fix there was nothing else on screen and no event that could bring chat back -- a
    // player would never see any incoming message. This must fail before the fix.
    [TestCase]
    public async Task Remote_arrival_surfaces_the_overlay_in_mouse_pointer_mode_even_with_the_bubble_hidden()
    {
        using RoomChatFixture fixture = await RoomChatFixture.CreateNeverOpenedWithServerSupport();
        fixture.Overlay.ConfigurePointerModeForTests(LanConnectPointerMode.Mouse);
        Button toggle = FindNode<Button>(fixture.Overlay, "RoomChatToggleButton");
        AssertThat(toggle.Visible).IsFalse();

        fixture.State.Room.AppendConfirmedForTests("remote-arrival-1", "Ally", "hello", 1, isLocal: false);
        await fixture.Overlay.RefreshForTests();

        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        AssertThat(fixture.Overlay.TestState.PanelVisible).IsTrue();
        AssertThat(toggle.Visible).IsFalse();
    }

    private static T FindNode<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);

    private sealed class FakeClock : ILanConnectMonotonicClock
    {
        public double NowSeconds { get; set; }
    }
}
