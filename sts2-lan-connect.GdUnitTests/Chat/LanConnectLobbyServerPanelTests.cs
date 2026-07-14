using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectLobbyServerPanelTests
{
    [TestCase]
    public async Task Desktop_sidebar_is_room_details_above_fixed_server_chat()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        LanConnectLobbyOverlayTestState state = fixture.Overlay.TestState;

        float mainShare = state.RoomStageRect.Size.X /
                          (state.RoomStageRect.Size.X + state.SidebarRect.Size.X);
        AssertThat(mainShare).IsBetween(0.73f, 0.77f);
        AssertThat(state.RoomDetailRect.End.Y).IsLessEqual(state.ServerChatRect.Position.Y);
        AssertThat(state.ServerPanelVisible).IsTrue();
        AssertThat(state.CompactSidebarScrollVisible).IsFalse();
        AssertThat(state.RoomDetailMinimumHeight).IsGreater(0f);
        AssertThat(state.ServerChatMinimumHeight).IsGreater(0f);
    }

    [TestCase]
    public async Task Unsupported_server_chat_hides_panel_and_expands_details()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Unsupported);
        LanConnectLobbyOverlayTestState state = fixture.Overlay.TestState;

        AssertThat(state.ServerPanelVisible).IsFalse();
        AssertThat(state.RoomDetailRect.End.Y).IsEqualApprox(state.SidebarRect.End.Y, 2f);
    }

    [TestCase]
    public async Task Compact_sidebar_scrolls_with_details_before_chat_and_bounded_heights()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(960, 720),
            LanConnectServerChatPresentation.Ready);
        LanConnectLobbyOverlayTestState state = fixture.Overlay.TestState;

        AssertThat(state.CompactSidebarScrollVisible).IsTrue();
        AssertThat(state.RoomDetailRect.End.Y).IsLessEqual(state.ServerChatRect.Position.Y);
        AssertThat(state.RoomDetailMinimumHeight).IsGreater(0f);
        AssertThat(state.ServerChatMinimumHeight).IsGreater(0f);
    }

    [TestCase]
    public async Task Selection_refreshes_details_without_rebuilding_server_chat_panel()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        GodotObject panelBefore = fixture.Overlay.ServerChatPanelForTests;

        fixture.Overlay.SelectRoomForTests("room-b");
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.TestState.SelectedRoomId).IsEqual("room-b");
        AssertThat(fixture.Overlay.TestState.SelectedRoomName).IsEqual("Beta Room");
        AssertThat(ReferenceEquals(panelBefore, fixture.Overlay.ServerChatPanelForTests)).IsTrue();
    }

    [TestCase]
    public async Task Visible_home_channel_consumes_incoming_messages_without_unread()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);

        fixture.ServerState.AppendConfirmedForTests("message", "A", "hello", 1, false);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.ServerState.UnreadCount).IsEqual(0);
        AssertThat(fixture.Overlay.ServerChatPanelForTests.TestState.MessageCount).IsEqual(1);
    }

    [TestCase]
    public async Task Server_context_rotation_rebinds_the_existing_panel_to_the_new_state()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        LanConnectBasicChatPanel panel = fixture.Overlay.ServerChatPanelForTests;
        LanConnectChatChannelState replacement = ReadyState("replacement");
        replacement.AppendConfirmedForTests("new-server-message", "B", "new server", 1, false);

        fixture.Overlay.RebindServerChatForTests(replacement);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(ReferenceEquals(panel, fixture.Overlay.ServerChatPanelForTests)).IsTrue();
        AssertThat(ReferenceEquals(panel.State, replacement)).IsTrue();
        AssertThat(panel.TestState.MessageCount).IsEqual(1);
        AssertThat(fixture.ServerState.IsVisible).IsFalse();
        AssertThat(replacement.IsVisible).IsTrue();
    }

    private static LanConnectChatChannelState ReadyState(string instanceId)
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            Channel = LanConnectChatChannel.Server,
            ServerChatVersion = 1,
            InstanceId = instanceId,
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures()
        });
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }
}

internal sealed class LobbyOverlayFixture : IDisposable
{
    private LobbyOverlayFixture(
        LanConnectLobbyOverlay overlay,
        LanConnectChatChannelState serverState,
        ISceneRunner runner)
    {
        Overlay = overlay;
        ServerState = serverState;
        Runner = runner;
    }

    internal LanConnectLobbyOverlay Overlay { get; }

    internal LanConnectChatChannelState ServerState { get; }

    internal ISceneRunner Runner { get; }

    internal static async Task<LobbyOverlayFixture> Create(
        Vector2I viewportSize,
        LanConnectServerChatPresentation presentation)
    {
        LanConnectChatChannelState serverState = new(LanConnectChatChannel.Server);
        serverState.SetPresentationForTests(presentation);
        if (presentation == LanConnectServerChatPresentation.Ready)
        {
            serverState.Apply(new ServerChatInboundEnvelope
            {
                Type = "chat_ready",
                Channel = LanConnectChatChannel.Server,
                ServerChatVersion = 1,
                InstanceId = "overlay-tests",
                HistoryEpoch = 1,
                ChatEnabled = true,
                EnabledFeatures = new ServerChatEnabledFeatures()
            });
            serverState.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        }

        SubViewport sceneRoot = AutoFree(new SubViewport
        {
            Size = viewportSize,
            Disable3D = true
        })!;
        LanConnectLobbyOverlay overlay = new() { Visible = false };
        overlay.SetProcess(false);
        sceneRoot.AddChild(overlay);
        ISceneRunner runner = ISceneRunner.Load(sceneRoot, autoFree: true);
        await runner.AwaitIdleFrame();
        overlay.ConfigureForTests(
            viewportSize,
            serverState,
            Rooms(),
            send: _ => Task.CompletedTask,
            retry: _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await overlay.RefreshLayoutForTests(viewportSize);
        await runner.AwaitIdleFrame();
        return new LobbyOverlayFixture(overlay, serverState, runner);
    }

    public void Dispose() => Runner.Dispose();

    private static IReadOnlyList<LobbyRoomSummary> Rooms() =>
    [
        new LobbyRoomSummary
        {
            RoomId = "room-a",
            RoomName = "Alpha Room",
            HostPlayerName = "Ironclad",
            CurrentPlayers = 1,
            MaxPlayers = 4,
            GameMode = "standard",
            Version = "1.0",
            ModVersion = "0.4.0",
            Status = "waiting"
        },
        new LobbyRoomSummary
        {
            RoomId = "room-b",
            RoomName = "Beta Room",
            HostPlayerName = "Silent",
            CurrentPlayers = 3,
            MaxPlayers = 4,
            GameMode = "daily",
            Version = "1.0",
            ModVersion = "0.4.0",
            Status = "waiting",
            RequiresPassword = true
        }
    ];
}
