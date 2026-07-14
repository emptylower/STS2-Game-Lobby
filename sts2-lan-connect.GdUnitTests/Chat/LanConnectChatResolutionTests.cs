using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectChatResolutionTests
{
    private static readonly (Vector2I Size, float Scale)[] Cases =
    {
        (new Vector2I(1280, 720), 1.0f),
        (new Vector2I(1280, 720), 1.5f),
        (new Vector2I(1920, 1080), 1.0f),
        (new Vector2I(1920, 1080), 1.5f),
        (new Vector2I(2560, 1440), 1.0f),
        (new Vector2I(2560, 1440), 1.5f),
        (new Vector2I(3840, 2160), 1.0f),
        (new Vector2I(3840, 2160), 1.5f)
    };

    [TestCase]
    public async Task Lobby_and_room_chat_remain_in_bounds_for_supported_matrix()
    {
        foreach ((Vector2I size, float scale) in Cases)
        {
            using ChatUiFixture fixture = await ChatUiFixture.Create(size, scale);
            string context = $"{size.X}x{size.Y}@{scale:0.0}";

            AssertRectInside(fixture.Lobby.TestState.SidebarRect, fixture.ViewportRect, $"{context} lobby sidebar");
            AssertRectInside(fixture.Lobby.TestState.ServerChatRect, fixture.ViewportRect, $"{context} server chat");
            AssertRectInside(fixture.Room.TestState.PanelRect, fixture.ViewportRect, $"{context} room chat");
            AssertRectInside(fixture.Room.TestState.DraftRect, fixture.ViewportRect, $"{context} room draft");
            AssertRectInside(fixture.Room.TestState.SendRect, fixture.ViewportRect, $"{context} room send");
            AssertRectInside(fixture.Lobby.TestState.ServerChatPanelState.DraftRect, fixture.ViewportRect, $"{context} lobby draft");
            AssertRectInside(fixture.Lobby.TestState.ServerChatPanelState.SendRect, fixture.ViewportRect, $"{context} lobby send");
            AssertThat(fixture.Room.TestState.TabRects.Count).IsEqual(2);
            foreach (Rect2 tabRect in fixture.Room.TestState.TabRects)
            {
                AssertRectInside(tabRect, fixture.ViewportRect, $"{context} room tab");
            }
            AssertThat(fixture.Room.TestState.RetryRects.Count).IsGreater(0);
            foreach (Rect2 retryRect in fixture.Room.TestState.RetryRects)
            {
                AssertRectInside(retryRect, fixture.ViewportRect, $"{context} room retry");
            }
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.RetryRects.Count).IsGreater(0);
            foreach (Rect2 retryRect in fixture.Lobby.TestState.ServerChatPanelState.RetryRects)
            {
                AssertRectInside(retryRect, fixture.ViewportRect, $"{context} lobby retry");
            }
            AssertThat(fixture.Room.TestState.NewMessagesBelowCount).IsGreater(0);
            AssertRectInside(fixture.Room.TestState.NewMessagesRect, fixture.ViewportRect, $"{context} new messages");
            AssertRectInside(
                fixture.Lobby.TestState.ServerChatPanelState.NewMessagesRect,
                fixture.ViewportRect,
                $"{context} lobby new messages");
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.MessageCount).IsEqual(50);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.PendingCount).IsEqual(1);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.FailedCount).IsEqual(1);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.DeliveryUnknownCount).IsEqual(1);
            AssertThat(fixture.Room.TestState.RoomUnread).IsGreaterEqual(10);
            AssertThat(fixture.RoomUnreadBadgeText.Length).IsGreaterEqual(2);
            AssertThat(fixture.ToggleUnreadBadgeText).IsEqual(fixture.RoomUnreadBadgeText);
            AssertThat(fixture.OverlappingNamedControls()).IsEmpty();
        }
    }

    [TestCase]
    public async Task Resize_preserves_room_chat_focus_channel_draft_scroll_and_focus_bounds()
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(1920, 1080), 1f);
        fixture.Room.SelectChannelForTests(LanConnectChatChannel.Server);
        fixture.Room.SetDraftForTests("resize keeps this draft\nwith a second line");
        fixture.Room.SetScrollForTests(120, atBottom: false);
        fixture.Room.FocusDraftForTests();
        await fixture.AwaitTwoFrames();
        LanConnectBasicChatPanel roomPanelBefore = fixture.Room.ChatPanelForTests;
        LanConnectChatChannelState channelStateBefore = fixture.Room.SelectedChannelStateForTests;
        LanConnectBasicChatPanel lobbyPanelBefore = fixture.Lobby.ServerChatPanelForTests;

        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(3840, 2160) })
        {
            await fixture.Resize(size, 1f);
            LanConnectRoomChatOverlayTestState state = fixture.Room.TestState;

            AssertThat(state.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
            AssertThat(state.SelectedChannel).IsEqual(LanConnectChatChannel.Server);
            AssertThat(state.Draft).IsEqual("resize keeps this draft\nwith a second line");
            AssertThat(state.ScrollOffset).IsEqual(120d);
            AssertThat(ReferenceEquals(roomPanelBefore, fixture.Room.ChatPanelForTests)).IsTrue();
            AssertThat(ReferenceEquals(channelStateBefore, fixture.Room.SelectedChannelStateForTests)).IsTrue();
            AssertThat(ReferenceEquals(lobbyPanelBefore, fixture.Lobby.ServerChatPanelForTests)).IsTrue();
            foreach (LanConnectNamedControlRect target in fixture.Room.TestState.FocusTargetRects)
            {
                AssertRectInside(target.Rect, fixture.ViewportRect, $"{size.X}x{size.Y} focus target {target.Name}");
            }
            foreach (LanConnectNamedControlRect target in fixture.Lobby.TestState.ServerChatPanelState.FocusTargetRects)
            {
                AssertRectInside(target.Rect, fixture.ViewportRect, $"{size.X}x{size.Y} lobby focus target {target.Name}");
            }
        }
    }

    private static void AssertRectInside(Rect2 rect, Rect2 viewport, string name)
    {
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f ||
            rect.Position.X < viewport.Position.X || rect.Position.Y < viewport.Position.Y ||
            rect.End.X > viewport.End.X || rect.End.Y > viewport.End.Y)
        {
            throw new InvalidOperationException($"{name}: rect {rect} is outside viewport {viewport}");
        }
    }
}

internal sealed class ChatUiFixture : IDisposable
{
    private static readonly string LongNickname = new('N', 32);
    private readonly SubViewport _root;
    private readonly ISceneRunner _runner;

    private ChatUiFixture(
        SubViewport root,
        ISceneRunner runner,
        LanConnectLobbyOverlay lobby,
        LanConnectRoomChatOverlay room)
    {
        _root = root;
        _runner = runner;
        Lobby = lobby;
        Room = room;
    }

    internal LanConnectLobbyOverlay Lobby { get; }

    internal LanConnectRoomChatOverlay Room { get; }

    internal Rect2 ViewportRect => new(Vector2.Zero, _root.Size);

    internal string RoomUnreadBadgeText => Find<Label>(Room, "RoomUnreadBadge").Text;

    internal string ToggleUnreadBadgeText =>
        Find<Control>(Room, "ChatToggleUnreadBadge").GetChildOrNull<Label>(0) is Label label
            ? label.Text
            : string.Empty;

    internal static async Task<ChatUiFixture> Create(Vector2I physicalSize, float uiScale)
    {
        Vector2I logicalSize = LogicalSize(physicalSize, uiScale);
        LanConnectChatChannelState server = PopulatedState(LanConnectChatChannel.Server, "server");
        LanConnectDualChatState dual = new(server);
        dual.EnterRoom("resolution-room");
        Populate(dual.Room, "room");

        SubViewport root = AutoFree(new SubViewport
        {
            Size = logicalSize,
            Disable3D = true
        })!;
        LanConnectLobbyOverlay lobby = new() { Visible = false };
        lobby.SetProcess(false);
        root.AddChild(lobby);
        LanConnectRoomChatOverlay room = new();
        root.AddChild(room);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.AwaitIdleFrame();

        lobby.ConfigureForTests(
            logicalSize,
            server,
            Rooms(),
            send: _ => Task.CompletedTask,
            retry: _ => Task.CompletedTask,
            uiScale: uiScale);
        room.ConfigureForTests(
            dual,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);
        await room.OpenForTests();
        room.SelectChannelForTests(LanConnectChatChannel.Server);

        // The selected channel is read; lock the hidden room-channel and aggregate badges at two digits.
        for (int index = 0; index < 12; index++)
        {
            dual.Room.AppendConfirmedForTests($"room-unread-{index}", LongNickname, $"unread {index}", 1000 + index, false);
        }
        room.SetScrollForTests(120, atBottom: false);
        server.AppendConfirmedForTests("server-below", LongNickname, "new message below", 2000, false);
        await room.RefreshForTests();
        await lobby.RefreshLayoutForTests(logicalSize);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        lobby.ScrollCompactSidebarToChatForTests();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        return new ChatUiFixture(root, runner, lobby, room);
    }

    internal async Task Resize(Vector2I physicalSize, float uiScale)
    {
        Vector2I logicalSize = LogicalSize(physicalSize, uiScale);
        _root.Size = logicalSize;
        await Lobby.RefreshLayoutForTests(logicalSize);
        await Room.RefreshForTests();
        await AwaitTwoFrames();
        Lobby.ScrollCompactSidebarToChatForTests();
        await AwaitTwoFrames();
    }

    internal async Task AwaitTwoFrames()
    {
        await _runner.AwaitIdleFrame();
        await _runner.AwaitIdleFrame();
    }

    internal IReadOnlyList<string> OverlappingNamedControls()
    {
        List<string> overlaps = new();
        AddPairwiseOverlaps(overlaps, "room", Room.TestState.FocusTargetRects);
        AddPairwiseOverlaps(overlaps, "lobby", Lobby.TestState.ServerChatPanelState.FocusTargetRects);
        return overlaps;
    }

    public void Dispose() => _runner.Dispose();

    private static LanConnectChatChannelState PopulatedState(LanConnectChatChannel channel, string prefix)
    {
        LanConnectChatChannelState state = new(channel);
        if (channel == LanConnectChatChannel.Server)
        {
            state.Apply(new ServerChatInboundEnvelope
            {
                Type = "chat_ready",
                Channel = channel,
                ServerChatVersion = 1,
                InstanceId = "resolution-tests",
                HistoryEpoch = 1,
                ChatEnabled = true,
                EnabledFeatures = new ServerChatEnabledFeatures()
            });
            state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        }
        Populate(state, prefix);
        return state;
    }

    private static void Populate(LanConnectChatChannelState state, string prefix)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.BeginPendingText($"{prefix}-pending", LongNickname, "pending message\nwith two lines", queuedAt: now);
        state.BeginPendingText($"{prefix}-failed", LongNickname, "failed message", queuedAt: now);
        state.MarkFailed($"{prefix}-failed", "send_failed", "offline");
        state.BeginPendingText($"{prefix}-unknown", LongNickname, "unknown message", queuedAt: now - TimeSpan.FromSeconds(20));
        state.MarkTimedOut(now);

        for (int index = 0; index < 46; index++)
        {
            state.AppendConfirmedForTests(
                $"{prefix}-confirmed-{index}",
                index == 0 ? LongNickname : $"Player {index}",
                index == 1 ? "first line\nsecond line with wrapped content" : $"message {index}",
                index + 1,
                false);
        }
    }

    private static IReadOnlyList<LobbyRoomSummary> Rooms() =>
    [
        new LobbyRoomSummary
        {
            RoomId = "resolution-room",
            RoomName = "Resolution Fixture Room",
            HostPlayerName = LongNickname,
            CurrentPlayers = 3,
            MaxPlayers = 4,
            GameMode = "standard",
            Version = "1.0",
            ModVersion = "0.4.0",
            Status = "waiting"
        }
    ];

    private static Vector2I LogicalSize(Vector2I physicalSize, float uiScale) => new(
        Mathf.Max(1, Mathf.FloorToInt(physicalSize.X / uiScale)),
        Mathf.Max(1, Mathf.FloorToInt(physicalSize.Y / uiScale)));

    private static T Find<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);

    private static void AddPairwiseOverlaps(
        List<string> overlaps,
        string surface,
        IReadOnlyList<LanConnectNamedControlRect> controls)
    {
        LanConnectNamedControlRect[] peers = controls
            .Where(control => control.Name != LanConnectConstants.ChatMessagesScrollName)
            .ToArray();
        for (int first = 0; first < peers.Length; first++)
        {
            for (int second = first + 1; second < peers.Length; second++)
            {
                if (peers[first].Rect.Intersects(peers[second].Rect, includeBorders: false))
                {
                    overlaps.Add($"{surface}: {peers[first].Name}/{peers[second].Name}");
                }
            }
        }
    }
}
