using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectDualChatState
{
    private bool _openedOnce;
    private LanConnectChatChannel _lastSelected = LanConnectChatChannel.Room;

    internal LanConnectDualChatState(LanConnectChatChannelState server)
    {
        ArgumentNullException.ThrowIfNull(server);
        Server = server;
        Room = new LanConnectChatChannelState(LanConnectChatChannel.Room);
    }

    internal LanConnectChatChannelState Room { get; }

    internal LanConnectChatChannelState Server { get; }

    internal string? ActiveRoomId { get; private set; }

    internal bool RoomOverlayOpen { get; private set; }

    internal LanConnectChatChannel SelectedChannel { get; private set; } = LanConnectChatChannel.Room;

    internal void EnterRoom(string roomId)
    {
        if (string.IsNullOrEmpty(roomId))
        {
            throw new ArgumentException("Room ID is required.", nameof(roomId));
        }
        if (string.Equals(ActiveRoomId, roomId, StringComparison.Ordinal))
        {
            return;
        }

        Room.ClearForContextChange();
        ActiveRoomId = roomId;
        _openedOnce = false;
        SelectedChannel = LanConnectChatChannel.Room;
    }

    internal void LeaveRoom()
    {
        RoomOverlayOpen = false;
        Room.SetVisible(false);
        Room.ClearForContextChange();
        ActiveRoomId = null;
        _openedOnce = false;
    }

    internal LanConnectChatChannel OpenRoomOverlay()
    {
        SelectedChannel = ChooseForOpen();
        RoomOverlayOpen = true;
        Select(SelectedChannel);
        _openedOnce = true;
        return SelectedChannel;
    }

    internal void CloseRoomOverlay()
    {
        RoomOverlayOpen = false;
        Room.SetVisible(false);
        Server.SetVisible(false);
    }

    internal void Select(LanConnectChatChannel channel)
    {
        SelectedChannel = channel;
        _lastSelected = channel;
        Room.SetVisible(RoomOverlayOpen && channel == LanConnectChatChannel.Room);
        Server.SetVisible(RoomOverlayOpen && channel == LanConnectChatChannel.Server);
    }

    internal void ClearServerContext() => Server.ClearForContextChange();

    private LanConnectChatChannel ChooseForOpen()
    {
        if (!_openedOnce)
        {
            return LanConnectChatChannel.Room;
        }

        int roomUnread = Room.UnreadCount;
        int serverUnread = Server.UnreadCount;
        if (roomUnread > 0 && serverUnread == 0)
        {
            return LanConnectChatChannel.Room;
        }
        if (serverUnread > 0 && roomUnread == 0)
        {
            return LanConnectChatChannel.Server;
        }
        if (roomUnread > 0 && serverUnread > 0)
        {
            long roomFirst = Room.FirstUnreadSequence ?? long.MaxValue;
            long serverFirst = Server.FirstUnreadSequence ?? long.MaxValue;
            return roomFirst <= serverFirst
                ? LanConnectChatChannel.Room
                : LanConnectChatChannel.Server;
        }

        return _lastSelected;
    }
}
