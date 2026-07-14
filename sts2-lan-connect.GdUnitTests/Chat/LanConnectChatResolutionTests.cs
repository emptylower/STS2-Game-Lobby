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
            AssertThat(fixture.Room.TestState.RetryRects.Count).IsEqual(2);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.RetryRects.Count).IsEqual(2);
            AssertThat(fixture.Room.TestState.NewMessagesBelowCount).IsGreater(0);
            AssertRectInside(fixture.Room.TestState.NewMessagesRect, fixture.ViewportRect, $"{context} new messages");
            AssertRectInside(
                fixture.Lobby.TestState.ServerChatPanelState.NewMessagesRect,
                fixture.ViewportRect,
                $"{context} lobby new messages");
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.MessageCount).IsEqual(50);
            AssertThat(fixture.Room.ChatPanelForTests.TestState.MessageCount).IsEqual(50);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.PendingCount).IsEqual(1);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.FailedCount).IsEqual(1);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.DeliveryUnknownCount).IsEqual(1);
            AssertThat(fixture.Room.TestState.RoomUnread).IsGreaterEqual(10);
            AssertThat(fixture.Room.TestState.ServerUnread).IsGreaterEqual(10);
            AssertThat(fixture.RoomUnreadBadgeText.Length).IsGreaterEqual(2);
            AssertThat(fixture.ServerUnreadBadgeText.Length).IsGreaterEqual(2);
            AssertThat(fixture.ToggleUnreadBadgeText)
                .IsEqual((fixture.Room.TestState.RoomUnread + fixture.Room.TestState.ServerUnread).ToString());
            IReadOnlyList<string> overlaps = fixture.OverlappingNamedControls();
            if (overlaps.Count > 0)
            {
                throw new InvalidOperationException($"{context} overlapping controls: {string.Join(", ", overlaps)}");
            }
        }
    }

    [TestCase]
    public async Task Every_retry_focus_target_scrolls_fully_into_its_messages_viewport()
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(1280, 720), 1f);

        foreach (string retryName in fixture.RoomRetryNames())
        {
            Control retry = fixture.FindRoomControl(retryName);
            retry.GrabFocus();
            await fixture.AwaitTwoFrames();
            AssertThat(fixture.Room.TestState.FocusOwnerName).IsEqual(retryName);
            AssertRectInside(retry.GetGlobalRect(), fixture.Room.TestState.MessagesRect, $"room focused retry {retryName}");
            AssertRectInside(retry.GetGlobalRect(), fixture.ViewportRect, $"room viewport retry {retryName}");
        }

        foreach (string retryName in fixture.LobbyRetryNames())
        {
            Control retry = fixture.FindLobbyControl(retryName);
            retry.GrabFocus();
            await fixture.AwaitTwoFrames();
            AssertThat(fixture.Lobby.ServerChatPanelForTests.TestState.FocusOwnerName).IsEqual(retryName);
            AssertRectInside(
                retry.GetGlobalRect(),
                fixture.Lobby.TestState.ServerChatPanelState.MessagesRect,
                $"lobby focused retry {retryName}");
            AssertRectInside(retry.GetGlobalRect(), fixture.ViewportRect, $"lobby viewport retry {retryName}");
        }
    }

    [TestCase]
    public async Task Production_window_scale_enters_compact_layout_without_matrix_override()
    {
        Window window = ((SceneTree)Engine.GetMainLoop()).Root;
        float originalScale = window.ContentScaleFactor;
        try
        {
            window.ContentScaleFactor = 1.5f;
            using ChatUiFixture fixture = await ChatUiFixture.Create(
                new Vector2I(2560, 1440),
                1.5f,
                useUiScaleOverride: false);
            await fixture.TriggerRealResize(new Vector2I(2560, 1440), 1.5f);

            AssertThat(fixture.Lobby.TestState.CompactSidebarScrollVisible).IsTrue();
        }
        finally
        {
            window.ContentScaleFactor = originalScale;
        }
    }

    [TestCase]
    public async Task Every_lobby_named_focus_target_can_be_scrolled_into_the_viewport()
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(1280, 720), 1f);
        IReadOnlyList<LanConnectNamedControlRect> targets = fixture.Lobby.TestState.FocusTargetRects;
        AssertThat(targets.Count).IsGreater(0);

        for (int index = 0; index < targets.Count; index++)
        {
            string expectedName = targets[index].Name;
            fixture.Lobby.FocusLobbyTargetForTests(index);
            await fixture.AwaitTwoFrames();

            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.FocusOwnerName).IsEqual(expectedName);
            AssertRectInside(
                fixture.Lobby.FocusedControlRectForTests(),
                fixture.ViewportRect,
                $"lobby focused target {expectedName}");
        }
    }

    [TestCase]
    public async Task Lobby_draft_focus_and_state_survive_real_breakpoint_resize()
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(1920, 1080), 1f);
        const string draft = "lobby resize draft\nsecond line";
        fixture.ServerState.SetDraft(draft);
        await fixture.Lobby.ServerChatPanelForTests.RefreshForTests();
        fixture.Lobby.ServerChatPanelForTests.SetScrollForTests(120, atBottom: false);
        fixture.Lobby.ServerChatPanelForTests.FocusDraft();
        await fixture.AwaitTwoFrames();
        LanConnectBasicChatPanel panelBefore = fixture.Lobby.ServerChatPanelForTests;
        LanConnectChatChannelState stateBefore = fixture.ServerState;

        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(3840, 2160) })
        {
            await fixture.Resize(size, 1f);
            LanConnectBasicChatPanelTestState state = fixture.Lobby.TestState.ServerChatPanelState;

            AssertThat(fixture.Lobby.ServerChatPanelForTests.DraftHasFocus).IsTrue();
            AssertThat(fixture.ServerState.Draft).IsEqual(draft);
            AssertThat(fixture.ServerState.ScrollOffset).IsEqual(120d);
            AssertThat(state.RenderedScrollOffset).IsEqual(120d);
            AssertThat(ReferenceEquals(panelBefore, fixture.Lobby.ServerChatPanelForTests)).IsTrue();
            AssertThat(ReferenceEquals(stateBefore, fixture.ServerState)).IsTrue();
            AssertThat(fixture.Lobby.TestState.FocusTargetRects.Count).IsGreater(0);
            foreach (Rect2 rect in fixture.Lobby.TestState.VisibleFocusTargetRects)
            {
                AssertRectInside(rect, fixture.ViewportRect, $"{size.X}x{size.Y} lobby focus target");
            }
            AssertThat(state.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
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
            AssertThat(state.RenderedScrollOffset).IsEqual(120d);
            AssertThat(ReferenceEquals(roomPanelBefore, fixture.Room.ChatPanelForTests)).IsTrue();
            AssertThat(ReferenceEquals(channelStateBefore, fixture.Room.SelectedChannelStateForTests)).IsTrue();
            AssertThat(ReferenceEquals(lobbyPanelBefore, fixture.Lobby.ServerChatPanelForTests)).IsTrue();
            foreach (LanConnectNamedControlRect target in fixture.Room.TestState.FocusTargetRects
                         .Where(target => !target.Name.StartsWith(
                             LanConnectConstants.ChatRetryButtonPrefix,
                             StringComparison.Ordinal)))
            {
                AssertRectInside(target.Rect, fixture.ViewportRect, $"{size.X}x{size.Y} focus target {target.Name}");
            }
            AssertThat(fixture.Lobby.TestState.FocusTargetRects.Count).IsGreater(0);
            foreach (Rect2 target in fixture.Lobby.TestState.VisibleFocusTargetRects)
            {
                AssertRectInside(target, fixture.ViewportRect, $"{size.X}x{size.Y} lobby focus target");
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
        LanConnectRoomChatOverlay room,
        LanConnectChatChannelState lobbyServerState,
        LanConnectChatChannelState roomServerState)
    {
        _root = root;
        _runner = runner;
        Lobby = lobby;
        Room = room;
        ServerState = lobbyServerState;
        RoomServerState = roomServerState;
    }

    internal LanConnectLobbyOverlay Lobby { get; }

    internal LanConnectRoomChatOverlay Room { get; }

    internal LanConnectChatChannelState ServerState { get; }

    internal LanConnectChatChannelState RoomServerState { get; }

    internal Rect2 ViewportRect => new(Vector2.Zero, _root.Size);

    internal string RoomUnreadBadgeText => Find<Label>(Room, "RoomUnreadBadge").Text;

    internal string ServerUnreadBadgeText => Find<Label>(Room, "ServerUnreadBadge").Text;

    internal string ToggleUnreadBadgeText =>
        Find<Control>(Room, "ChatToggleUnreadBadge").GetChildOrNull<Label>(0) is Label label
            ? label.Text
            : string.Empty;

    internal static async Task<ChatUiFixture> Create(
        Vector2I physicalSize,
        float uiScale,
        bool useUiScaleOverride = true)
    {
        Vector2I logicalSize = LogicalSize(physicalSize, uiScale);
        LanConnectChatChannelState lobbyServer = PopulatedState(LanConnectChatChannel.Server, "lobby-server", 46);
        LanConnectChatChannelState roomServer = PopulatedState(LanConnectChatChannel.Server, "room-server", 36);
        LanConnectDualChatState dual = new(roomServer);
        dual.EnterRoom("resolution-room");
        Populate(dual.Room, "room", 46);

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
            lobbyServer,
            Rooms(),
            send: _ => Task.CompletedTask,
            retry: _ => Task.CompletedTask,
            uiScale: useUiScaleOverride ? uiScale : null);
        room.ConfigureForTests(
            dual,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);
        await room.OpenForTests();
        room.SelectChannelForTests(LanConnectChatChannel.Server);

        // Produce new-messages while visible, then use normal hidden-channel mechanics for both unread badges.
        room.SetScrollForTests(120, atBottom: false);
        roomServer.AppendConfirmedForTests("room-server-below", LongNickname, "new message below", 2000, false);
        roomServer.SetVisible(false);
        for (int index = 0; index < 10; index++)
        {
            roomServer.AppendConfirmedForTests($"server-unread-{index}", LongNickname, $"server unread {index}", 2100 + index, false);
        }
        for (int index = 0; index < 12; index++)
        {
            dual.Room.AppendConfirmedForTests($"room-unread-{index}", LongNickname, $"unread {index}", 1000 + index, false);
        }
        lobbyServer.SetScrollState(120, atBottom: false);
        lobbyServer.AppendConfirmedForTests("lobby-server-below", LongNickname, "lobby new message below", 3000, false);
        await room.RefreshForTests();
        await lobby.RefreshLayoutForTests(logicalSize);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        lobby.ScrollCompactSidebarToChatForTests();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        return new ChatUiFixture(root, runner, lobby, room, lobbyServer, roomServer);
    }

    internal async Task Resize(Vector2I physicalSize, float uiScale)
    {
        Vector2I logicalSize = LogicalSize(physicalSize, uiScale);
        _root.Size = logicalSize;
        await AwaitTwoFrames();
    }

    internal async Task TriggerRealResize(Vector2I physicalSize, float uiScale)
    {
        Vector2I target = LogicalSize(physicalSize, uiScale);
        _root.Size = target + Vector2I.One;
        await AwaitTwoFrames();
        _root.Size = target;
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
        AddPairwiseOverlaps(
            overlaps,
            "room",
            Room.TestState.FocusTargetRects,
            Room.TestState.MessagesRect);
        AddPairwiseOverlaps(
            overlaps,
            "lobby",
            Lobby.TestState.ServerChatPanelState.FocusTargetRects,
            Lobby.TestState.ServerChatPanelState.MessagesRect);
        return overlaps;
    }

    internal IReadOnlyList<string> RoomRetryNames() => RetryNames(Room);

    internal IReadOnlyList<string> LobbyRetryNames() => RetryNames(Lobby.ServerChatPanelForTests);

    internal Control FindRoomControl(string name) => Find<Control>(Room, name);

    internal Control FindLobbyControl(string name) => Find<Control>(Lobby.ServerChatPanelForTests, name);

    public void Dispose() => _runner.Dispose();

    private static LanConnectChatChannelState PopulatedState(
        LanConnectChatChannel channel,
        string prefix,
        int confirmedCount)
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
        Populate(state, prefix, confirmedCount);
        return state;
    }

    private static void Populate(LanConnectChatChannelState state, string prefix, int confirmedCount)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.BeginPendingText($"{prefix}-pending", LongNickname, "pending message\nwith two lines", queuedAt: now);
        state.BeginPendingText($"{prefix}-failed", LongNickname, "failed message", queuedAt: now);
        state.MarkFailed($"{prefix}-failed", "send_failed", "offline");
        state.BeginPendingText($"{prefix}-unknown", LongNickname, "unknown message", queuedAt: now - TimeSpan.FromSeconds(20));
        state.MarkTimedOut(now);

        for (int index = 0; index < confirmedCount; index++)
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

    private static IReadOnlyList<string> RetryNames(Node root) => root
        .FindChildren(LanConnectConstants.ChatRetryButtonPrefix + "*", "Button", recursive: true, owned: false)
        .Select(node => node.Name.ToString())
        .ToArray();

    private static void AddPairwiseOverlaps(
        List<string> overlaps,
        string surface,
        IReadOnlyList<LanConnectNamedControlRect> controls,
        Rect2 messagesRect)
    {
        LanConnectNamedControlRect[] peers = controls
            .Select(control => control.Name.StartsWith(
                    LanConnectConstants.ChatRetryButtonPrefix,
                    StringComparison.Ordinal)
                ? control with { Rect = control.Rect.Intersection(messagesRect) }
                : control)
            .ToArray();
        for (int first = 0; first < peers.Length; first++)
        {
            for (int second = first + 1; second < peers.Length; second++)
            {
                bool legalMessagesRetryContainment =
                    (peers[first].Name == LanConnectConstants.ChatMessagesScrollName &&
                     peers[second].Name.StartsWith(LanConnectConstants.ChatRetryButtonPrefix, StringComparison.Ordinal) ||
                     peers[second].Name == LanConnectConstants.ChatMessagesScrollName &&
                     peers[first].Name.StartsWith(LanConnectConstants.ChatRetryButtonPrefix, StringComparison.Ordinal));
                if (!legalMessagesRetryContainment &&
                    peers[first].Rect.Intersects(peers[second].Rect, includeBorders: false))
                {
                    overlaps.Add($"{surface}: {peers[first].Name}/{peers[second].Name}");
                }
            }
        }
    }
}
