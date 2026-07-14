using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomChatTabsTests
{
    [TestCase]
    public async Task Builds_stable_fixed_width_tab_panel_and_pin_nodes()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();

        Button roomTab = FindNode<Button>(fixture.Overlay, "RoomChatTab");
        Button serverTab = FindNode<Button>(fixture.Overlay, "ServerChatTab");
        AssertThat(roomTab.CustomMinimumSize.X).IsGreater(0f);
        AssertThat(serverTab.CustomMinimumSize.X).IsEqual(roomTab.CustomMinimumSize.X);
        AssertThat(FindNode<Label>(fixture.Overlay, "RoomUnreadBadge")).IsNotNull();
        AssertThat(FindNode<Label>(fixture.Overlay, "ServerUnreadBadge")).IsNotNull();
        AssertThat(FindNode<LanConnectBasicChatPanel>(fixture.Overlay, "RoomChatPanel")).IsNotNull();

        Button pin = FindNode<Button>(fixture.Overlay, "ChatPinButton");
        AssertThat(pin.Visible).IsTrue();
        AssertThat(pin.Text).Contains("📌");
        AssertThat(pin.TooltipText).IsNotEmpty();
        pin.EmitSignal(Button.SignalName.Pressed);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.TestState.Pinned).IsTrue();
    }

    [TestCase]
    public async Task Tabs_preserve_drafts_scroll_and_badges_independently()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        LanConnectRoomChatOverlay overlay = fixture.Overlay;
        for (int index = 0; index < 20; index++)
        {
            fixture.State.Room.AppendConfirmedForTests(
                $"room-seed-{index}", "Me", $"room seed {index}", index + 1, true);
            fixture.State.Server.AppendConfirmedForTests(
                $"server-seed-{index}", "Me", $"server seed {index}", index + 1, true);
        }
        await overlay.RefreshForTests();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        overlay.SelectChannelForTests(LanConnectChatChannel.Room);
        overlay.SetDraftForTests("room draft");
        overlay.SetScrollForTests(33, false);
        overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        overlay.SetDraftForTests("server draft");
        overlay.SetScrollForTests(88, false);
        overlay.InjectRemoteForTests(LanConnectChatChannel.Room, sequence: 10);
        await overlay.RefreshForTests();

        AssertThat(overlay.TestState.RoomUnread).IsEqual(1);
        AssertThat(overlay.TestState.ServerUnread).IsEqual(0);
        overlay.SelectChannelForTests(LanConnectChatChannel.Room);
        await overlay.RefreshForTests();

        AssertThat(overlay.TestState.Draft).IsEqual("room draft");
        AssertThat(overlay.TestState.ScrollOffset).IsEqual(33d);
        AssertThat(overlay.TestState.RoomUnread).IsEqual(0);
        AssertThat(fixture.State.Server.Draft).IsEqual("server draft");
        AssertThat(fixture.State.Server.ScrollOffset).IsEqual(88d);
    }

    [TestCase]
    public async Task Reopen_uses_only_unread_then_earliest_unread_then_last_tab()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        LanConnectRoomChatOverlay overlay = fixture.Overlay;
        AssertThat(overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Room);

        await overlay.CloseForTests();
        overlay.InjectRemoteForTests(LanConnectChatChannel.Server, sequence: 20);
        await overlay.OpenForTests();
        AssertThat(overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Server);

        await overlay.CloseForTests();
        overlay.InjectRemoteForTests(LanConnectChatChannel.Server, sequence: 50);
        overlay.InjectRemoteForTests(LanConnectChatChannel.Room, sequence: 40);
        await overlay.OpenForTests();
        AssertThat(overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Room);

        overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        await overlay.CloseForTests();
        await overlay.OpenForTests();
        AssertThat(overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Server);
    }

    [TestCase]
    public async Task Unsupported_server_hides_tab_forces_room_and_preserves_server_state()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        fixture.Overlay.SetDraftForTests("keep server draft");
        fixture.State.Server.AppendConfirmedForTests("server-kept", "A", "kept", 5, false);

        fixture.State.Server.SetPresentationForTests(LanConnectServerChatPresentation.Unsupported);
        await fixture.Overlay.RefreshForTests();

        AssertThat(FindNode<Button>(fixture.Overlay, "ServerChatTab").Visible).IsFalse();
        AssertThat(fixture.Overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Room);
        AssertThat(fixture.State.Server.Draft).IsEqual("keep server draft");
        AssertThat(fixture.State.Server.Messages.Count).IsEqual(1);
    }

    [TestCase]
    public async Task Reopening_with_hidden_server_preserves_its_only_unread_message()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        await fixture.Overlay.CloseForTests();
        fixture.State.Server.SetDraft("keep hidden draft");
        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Server, sequence: 20);
        fixture.State.Server.SetPresentationForTests(LanConnectServerChatPresentation.Unsupported);

        await fixture.Overlay.OpenForTests();

        AssertThat(fixture.Overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Room);
        AssertThat(fixture.Overlay.TestState.ServerUnread).IsEqual(1);
        AssertThat(fixture.State.Server.IsVisible).IsFalse();
        AssertThat(fixture.State.Server.Draft).IsEqual("keep hidden draft");
        AssertThat(fixture.State.Server.Messages.Count).IsEqual(1);
    }

    [TestCase]
    public async Task Hidden_server_unread_is_excluded_from_toggle_badge_until_support_returns()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        await fixture.Overlay.CloseForTests();
        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Server, sequence: 20);
        await fixture.Overlay.RefreshForTests();
        Control toggleBadge = FindNode<Control>(fixture.Overlay, "ChatToggleUnreadBadge");
        AssertThat(toggleBadge.Visible).IsTrue();

        fixture.State.Server.SetPresentationForTests(LanConnectServerChatPresentation.Unsupported);
        await fixture.Overlay.RefreshForTests();
        AssertThat(fixture.Overlay.TestState.ServerUnread).IsEqual(1);
        AssertThat(toggleBadge.Visible).IsFalse();

        fixture.State.Server.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        await fixture.Overlay.RefreshForTests();
        AssertThat(fixture.Overlay.TestState.ServerUnread).IsEqual(1);
        AssertThat(toggleBadge.Visible).IsTrue();
    }

    [TestCase]
    public async Task Leaving_room_closes_overlay_and_clears_only_room_state()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.State.Room.SetDraft("room draft");
        fixture.State.Room.AppendConfirmedForTests("room-message", "A", "room", 1, false);
        fixture.State.Server.SetDraft("server draft");
        fixture.State.Server.AppendConfirmedForTests("server-message", "B", "server", 2, false);

        await fixture.Overlay.LeaveRoomForTests();

        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();
        AssertThat(fixture.State.Room.Draft).IsEqual(string.Empty);
        AssertThat(fixture.State.Room.Messages.Count).IsEqual(0);
        AssertThat(fixture.State.Server.Draft).IsEqual("server draft");
        AssertThat(fixture.State.Server.Messages.Count).IsEqual(1);
    }

    [TestCase]
    public async Task Disabled_room_chat_keeps_overlay_visible_but_disables_room_input()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();

        fixture.State.Room.SetChatEnabled(false);
        await fixture.Overlay.RefreshForTests();

        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        AssertThat(FindNode<LanConnectBasicChatPanel>(fixture.Overlay, "RoomChatPanel")
            .TestState.InputEditable).IsFalse();
        AssertThat(FindNode<Button>(fixture.Overlay, "ServerChatTab").Visible).IsTrue();
    }

    [TestCase]
    public async Task Selected_panel_keeps_delivery_states_and_does_not_force_scroll()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        LanConnectRoomChatOverlay overlay = fixture.Overlay;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        fixture.State.Room.BeginPendingText("pending", "Me", "pending text", queuedAt: now);
        fixture.State.Room.BeginPendingText("failed", "Me", "failed text", queuedAt: now);
        fixture.State.Room.MarkFailed("failed", "send_failed", "offline");
        overlay.SetScrollForTests(24, false);
        overlay.InjectRemoteForTests(LanConnectChatChannel.Room, sequence: 100);
        await overlay.RefreshForTests();

        LanConnectBasicChatPanel panel = FindNode<LanConnectBasicChatPanel>(overlay, "RoomChatPanel");
        AssertThat(panel.TestState.PendingCount).IsEqual(1);
        AssertThat(panel.TestState.FailedCount).IsEqual(1);
        AssertThat(overlay.TestState.ScrollOffset).IsEqual(24d);
        AssertThat(overlay.TestState.NewMessagesBelowCount).IsEqual(1);
    }

    private static T FindNode<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);
}

internal sealed class RoomChatFixture : IDisposable
{
    private RoomChatFixture(
        LanConnectRoomChatOverlay overlay,
        LanConnectDualChatState state,
        ISceneRunner runner)
    {
        Overlay = overlay;
        State = state;
        Runner = runner;
    }

    internal LanConnectRoomChatOverlay Overlay { get; }

    internal LanConnectDualChatState State { get; }

    internal ISceneRunner Runner { get; }

    internal static async Task<RoomChatFixture> OpenWithServerSupport()
    {
        LanConnectChatChannelState server = new(LanConnectChatChannel.Server);
        server.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            Channel = LanConnectChatChannel.Server,
            ServerChatVersion = 1,
            InstanceId = "room-tabs-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures()
        });
        server.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        LanConnectDualChatState state = new(server);
        state.EnterRoom("room-a");

        SubViewport root = AutoFree(new SubViewport
        {
            Size = new Vector2I(1920, 1080),
            Disable3D = true
        })!;
        LanConnectRoomChatOverlay overlay = new();
        root.AddChild(overlay);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.AwaitIdleFrame();
        overlay.ConfigureForTests(
            state,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);
        await overlay.OpenForTests();
        await runner.AwaitIdleFrame();
        return new RoomChatFixture(overlay, state, runner);
    }

    public void Dispose() => Runner.Dispose();
}
