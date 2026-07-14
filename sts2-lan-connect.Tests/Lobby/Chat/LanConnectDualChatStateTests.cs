using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectDualChatStateTests
{
    [Fact]
    public void ConstructorRetainsServerAndCreatesRoomChannel()
    {
        LanConnectChatChannelState server = new(LanConnectChatChannel.Server);

        LanConnectDualChatState state = new(server);

        Assert.Same(server, state.Server);
        Assert.Equal(LanConnectChatChannel.Room, state.Room.Channel);
        Assert.Null(state.ActiveRoomId);
        Assert.False(state.RoomOverlayOpen);
        Assert.Equal(LanConnectChatChannel.Room, state.SelectedChannel);
    }

    [Fact]
    public void ConstructorRejectsNullServerState()
    {
        Assert.Throws<ArgumentNullException>(() => new LanConnectDualChatState(null!));
    }

    [Fact]
    public void ConstructorRejectsRoomStateAsServerState()
    {
        LanConnectChatChannelState room = new(LanConnectChatChannel.Room);

        Assert.Throws<ArgumentException>(() => new LanConnectDualChatState(room));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EnterRoomRejectsMissingRoomId(string? roomId)
    {
        LanConnectDualChatState state = new(new LanConnectChatChannelState(LanConnectChatChannel.Server));

        Assert.Throws<ArgumentException>(() => state.EnterRoom(roomId!));
    }

    [Fact]
    public void FirstRoomOpenDefaultsToRoomThenSelectsTheOnlyUnreadChannel()
    {
        LanConnectDualChatState state = CreateEnteredState();
        state.Server.AppendConfirmedForTests("server-before-first-open", "A", "server", 30, false);

        Assert.Equal(LanConnectChatChannel.Room, state.OpenRoomOverlay());
        Assert.True(state.Room.IsVisible);
        Assert.False(state.Server.IsVisible);
        Assert.Equal(1, state.Server.UnreadCount);

        state.CloseRoomOverlay();

        Assert.Equal(LanConnectChatChannel.Server, state.OpenRoomOverlay());
        Assert.True(state.Server.IsVisible);
        Assert.False(state.Room.IsVisible);
        Assert.Equal(0, state.Server.UnreadCount);
    }

    [Fact]
    public void BothUnreadSelectsTheChannelWithEarliestUnreadSequence()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();
        state.Server.AppendConfirmedForTests("server", "A", "server", 50, false);
        state.Room.AppendConfirmedForTests("room", "B", "room", 40, false);

        Assert.Equal(LanConnectChatChannel.Room, state.OpenRoomOverlay());
    }

    [Fact]
    public void EqualUnreadSequencesSelectRoomDeterministically()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();
        state.Server.AppendConfirmedForTests("server", "A", "server", 40, false);
        state.Room.AppendConfirmedForTests("room", "B", "room", 40, false);

        Assert.Equal(LanConnectChatChannel.Room, state.OpenRoomOverlay());
    }

    [Fact]
    public void NoUnreadRestoresLastSelectedChannel()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();
        state.Select(LanConnectChatChannel.Server);

        Assert.Equal(LanConnectChatChannel.Server, state.OpenRoomOverlay());
    }

    [Fact]
    public void EnteringSameRoomIsANoOp()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();
        state.Room.SetDraft("keep room draft");
        state.Select(LanConnectChatChannel.Server);

        state.EnterRoom("room-a");

        Assert.Equal("keep room draft", state.Room.Draft);
        Assert.Equal(LanConnectChatChannel.Server, state.OpenRoomOverlay());
    }

    [Fact]
    public void EnteringNewRoomClearsOnlyRoomAndResetsFirstOpenPolicy()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();
        state.Room.SetDraft("old room");
        state.Server.SetDraft("server");
        state.Server.AppendConfirmedForTests("server-unread", "A", "server", 30, false);

        state.EnterRoom("room-b");

        Assert.Equal("room-b", state.ActiveRoomId);
        Assert.Equal(string.Empty, state.Room.Draft);
        Assert.Equal("server", state.Server.Draft);
        Assert.Single(state.Server.Messages);
        Assert.Equal(LanConnectChatChannel.Room, state.SelectedChannel);
        Assert.Equal(LanConnectChatChannel.Room, state.OpenRoomOverlay());
    }

    [Fact]
    public void EnteringNewRoomFromOpenServerTabSelectsAndShowsClearedRoom()
    {
        LanConnectDualChatState state = CreateEnteredState();
        state.Room.SetDraft("old room");
        Assert.Equal(LanConnectChatChannel.Room, state.OpenRoomOverlay());
        state.Select(LanConnectChatChannel.Server);
        Assert.True(state.Server.IsVisible);
        long roomRevision = state.Room.Revision;
        long serverRevision = state.Server.Revision;

        state.EnterRoom("room-b");

        Assert.Equal("room-b", state.ActiveRoomId);
        Assert.True(state.RoomOverlayOpen);
        Assert.Equal(LanConnectChatChannel.Room, state.SelectedChannel);
        Assert.True(state.Room.IsVisible);
        Assert.False(state.Server.IsVisible);
        Assert.Equal(string.Empty, state.Room.Draft);
        Assert.Equal(roomRevision + 2, state.Room.Revision);
        Assert.Equal(serverRevision + 1, state.Server.Revision);

        state.CloseRoomOverlay();
        state.Server.AppendConfirmedForTests("server-after-room-change", "A", "server", 30, false);
        Assert.Equal(LanConnectChatChannel.Server, state.OpenRoomOverlay());
    }

    [Fact]
    public void EnteringNewRoomFromOpenRoomTabRestoresRoomVisibilityWithoutTouchingServer()
    {
        LanConnectDualChatState state = CreateEnteredState();
        state.Room.SetDraft("old room");
        Assert.Equal(LanConnectChatChannel.Room, state.OpenRoomOverlay());
        long roomRevision = state.Room.Revision;
        long serverRevision = state.Server.Revision;

        state.EnterRoom("room-b");

        Assert.True(state.RoomOverlayOpen);
        Assert.Equal(LanConnectChatChannel.Room, state.SelectedChannel);
        Assert.True(state.Room.IsVisible);
        Assert.False(state.Server.IsVisible);
        Assert.Equal(string.Empty, state.Room.Draft);
        Assert.Equal(roomRevision + 2, state.Room.Revision);
        Assert.Equal(serverRevision, state.Server.Revision);
    }

    [Fact]
    public void LeaveRoomClearsRoomAndOverlayButPreservesServerContext()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();
        state.Room.SetDraft("room");
        state.Server.SetDraft("server");
        state.Server.AppendConfirmedForTests("server", "A", "message", 30, false);
        Assert.Equal(LanConnectChatChannel.Server, state.OpenRoomOverlay());
        Assert.True(state.Server.IsVisible);
        long serverRevision = state.Server.Revision;

        state.LeaveRoom();

        Assert.Null(state.ActiveRoomId);
        Assert.False(state.RoomOverlayOpen);
        Assert.False(state.Room.IsVisible);
        Assert.False(state.Server.IsVisible);
        Assert.Equal(string.Empty, state.Room.Draft);
        Assert.Equal("server", state.Server.Draft);
        Assert.Single(state.Server.Messages);
        Assert.Equal(serverRevision + 1, state.Server.Revision);

        state.Server.AppendConfirmedForTests("server-after-leave", "B", "hidden", 31, false);

        Assert.Equal(1, state.Server.UnreadCount);
        Assert.Equal(2, state.Server.Messages.Count);
    }

    [Fact]
    public void LeaveRoomIsNoOpWithoutAnActiveRoom()
    {
        LanConnectChatChannelState server = new(LanConnectChatChannel.Server);
        server.SetVisible(true);
        LanConnectDualChatState state = new(server);
        state.Room.SetDraft("untouched");
        long roomRevision = state.Room.Revision;
        long serverRevision = state.Server.Revision;

        state.LeaveRoom();

        Assert.Equal(roomRevision, state.Room.Revision);
        Assert.Equal(serverRevision, state.Server.Revision);
        Assert.Equal("untouched", state.Room.Draft);
        Assert.True(state.Server.IsVisible);
        Assert.False(state.RoomOverlayOpen);
        Assert.Null(state.ActiveRoomId);
    }

    [Fact]
    public void RepeatedLeaveRoomIsNoOp()
    {
        LanConnectDualChatState state = CreateEnteredState();
        state.Room.SetDraft("clear once");
        state.OpenRoomOverlay();
        state.LeaveRoom();
        long roomRevision = state.Room.Revision;
        long serverRevision = state.Server.Revision;

        state.LeaveRoom();

        Assert.Equal(roomRevision, state.Room.Revision);
        Assert.Equal(serverRevision, state.Server.Revision);
        Assert.False(state.Room.IsVisible);
        Assert.False(state.Server.IsVisible);
    }

    [Fact]
    public void OpenRoomOverlayWithoutActiveRoomFailsWithoutMutation()
    {
        LanConnectChatChannelState server = new(LanConnectChatChannel.Server);
        server.SetVisible(true);
        LanConnectDualChatState state = new(server);
        state.Room.SetDraft("untouched");
        long roomRevision = state.Room.Revision;
        long serverRevision = state.Server.Revision;

        Assert.Throws<InvalidOperationException>(() => state.OpenRoomOverlay());

        Assert.Equal(roomRevision, state.Room.Revision);
        Assert.Equal(serverRevision, state.Server.Revision);
        Assert.Equal("untouched", state.Room.Draft);
        Assert.True(state.Server.IsVisible);
        Assert.False(state.Room.IsVisible);
        Assert.False(state.RoomOverlayOpen);
        Assert.Equal(LanConnectChatChannel.Room, state.SelectedChannel);
    }

    [Fact]
    public void OpenRoomOverlayAfterLeaveFailsWithoutMutation()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();
        state.Select(LanConnectChatChannel.Server);
        state.LeaveRoom();
        long roomRevision = state.Room.Revision;
        long serverRevision = state.Server.Revision;

        Assert.Throws<InvalidOperationException>(() => state.OpenRoomOverlay());

        Assert.Equal(roomRevision, state.Room.Revision);
        Assert.Equal(serverRevision, state.Server.Revision);
        Assert.Null(state.ActiveRoomId);
        Assert.False(state.RoomOverlayOpen);
        Assert.False(state.Room.IsVisible);
        Assert.False(state.Server.IsVisible);
        Assert.Equal(LanConnectChatChannel.Server, state.SelectedChannel);
    }

    [Fact]
    public void SelectWhileClosedUpdatesSelectionWithoutShowingEitherChannel()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();

        state.Select(LanConnectChatChannel.Server);

        Assert.Equal(LanConnectChatChannel.Server, state.SelectedChannel);
        Assert.False(state.Room.IsVisible);
        Assert.False(state.Server.IsVisible);
    }

    [Fact]
    public void OpeningMarksOnlyTheSelectedChannelRead()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();
        state.Server.AppendConfirmedForTests("server", "A", "server", 30, false);
        state.Room.AppendConfirmedForTests("room", "B", "room", 40, false);

        Assert.Equal(LanConnectChatChannel.Server, state.OpenRoomOverlay());

        Assert.Equal(0, state.Server.UnreadCount);
        Assert.Equal(1, state.Room.UnreadCount);
        Assert.True(state.Server.IsVisible);
        Assert.False(state.Room.IsVisible);
    }

    [Fact]
    public void ClearServerContextClearsOnlyServer()
    {
        LanConnectDualChatState state = CreateEnteredState();
        state.Room.SetDraft("room");
        state.Room.AppendConfirmedForTests("room", "A", "room message", 20, false);
        state.Server.SetDraft("server");
        state.Server.AppendConfirmedForTests("server", "B", "server message", 30, false);

        state.ClearServerContext();

        Assert.Equal("room", state.Room.Draft);
        Assert.Single(state.Room.Messages);
        Assert.Equal(string.Empty, state.Server.Draft);
        Assert.Empty(state.Server.Messages);
        Assert.Equal("room-a", state.ActiveRoomId);
    }

    [Fact]
    public void ClearServerContextRestoresOpenServerTabVisibility()
    {
        LanConnectDualChatState state = EnterAndCloseOnce();
        state.Server.SetDraft("server");
        state.Server.AppendConfirmedForTests("server", "A", "server", 30, false);
        Assert.Equal(LanConnectChatChannel.Server, state.OpenRoomOverlay());
        long roomRevision = state.Room.Revision;
        long serverRevision = state.Server.Revision;

        state.ClearServerContext();

        Assert.True(state.RoomOverlayOpen);
        Assert.Equal(LanConnectChatChannel.Server, state.SelectedChannel);
        Assert.False(state.Room.IsVisible);
        Assert.True(state.Server.IsVisible);
        Assert.Empty(state.Server.Messages);
        Assert.Equal(string.Empty, state.Server.Draft);
        Assert.Equal(roomRevision, state.Room.Revision);
        Assert.Equal(serverRevision + 2, state.Server.Revision);
    }

    [Fact]
    public void ClearServerContextKeepsOpenRoomTabVisible()
    {
        LanConnectDualChatState state = CreateEnteredState();
        state.Server.SetDraft("server");
        state.Server.AppendConfirmedForTests("server", "A", "server", 30, false);
        Assert.Equal(LanConnectChatChannel.Room, state.OpenRoomOverlay());
        long roomRevision = state.Room.Revision;
        long serverRevision = state.Server.Revision;

        state.ClearServerContext();

        Assert.True(state.RoomOverlayOpen);
        Assert.Equal(LanConnectChatChannel.Room, state.SelectedChannel);
        Assert.True(state.Room.IsVisible);
        Assert.False(state.Server.IsVisible);
        Assert.Empty(state.Server.Messages);
        Assert.Equal(string.Empty, state.Server.Draft);
        Assert.Equal(roomRevision, state.Room.Revision);
        Assert.Equal(serverRevision + 1, state.Server.Revision);
    }

    private static LanConnectDualChatState CreateEnteredState()
    {
        LanConnectDualChatState state = new(new LanConnectChatChannelState(LanConnectChatChannel.Server));
        state.EnterRoom("room-a");
        return state;
    }

    private static LanConnectDualChatState EnterAndCloseOnce()
    {
        LanConnectDualChatState state = CreateEnteredState();
        Assert.Equal(LanConnectChatChannel.Room, state.OpenRoomOverlay());
        state.CloseRoomOverlay();
        return state;
    }
}
