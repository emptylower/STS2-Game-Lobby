using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectDualChatState
{
    private bool _openedOnce;
    private LanConnectChatChannel _lastSelected = LanConnectChatChannel.Room;

    internal LanConnectDualChatState(LanConnectChatChannelState server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (server.Channel != LanConnectChatChannel.Server)
        {
            throw new ArgumentException("Server state must use the server channel.", nameof(server));
        }

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
        _openedOnce = RoomOverlayOpen;
        SelectedChannel = LanConnectChatChannel.Room;
        if (RoomOverlayOpen)
        {
            _lastSelected = LanConnectChatChannel.Room;
            ApplyVisibility();
        }
    }

    internal void LeaveRoom()
    {
        if (ActiveRoomId == null)
        {
            return;
        }

        RoomOverlayOpen = false;
        Room.SetVisible(false);
        Server.SetVisible(false);
        Room.ClearForContextChange();
        ActiveRoomId = null;
        _openedOnce = false;
    }

    internal LanConnectChatChannel OpenRoomOverlay(bool serverSelectable = true)
    {
        if (ActiveRoomId == null)
        {
            throw new InvalidOperationException("A room must be active before opening room chat.");
        }

        SelectedChannel = serverSelectable
            ? ChooseForOpen()
            : LanConnectChatChannel.Room;
        RoomOverlayOpen = true;
        Select(SelectedChannel);
        _openedOnce = true;
        return SelectedChannel;
    }

    internal LanConnectChatChannel ShowRoomOverlayPreservingSelection(bool serverSelectable = true)
    {
        if (ActiveRoomId == null)
        {
            throw new InvalidOperationException("A room must be active before showing room chat.");
        }

        if (!serverSelectable)
        {
            SelectedChannel = LanConnectChatChannel.Room;
        }
        RoomOverlayOpen = true;
        Select(SelectedChannel);
        _openedOnce = true;
        return SelectedChannel;
    }

    internal void CloseRoomOverlay()
    {
        RoomOverlayOpen = false;
        ApplyVisibility();
    }

    internal void Select(LanConnectChatChannel channel)
    {
        SelectedChannel = channel;
        _lastSelected = channel;
        ApplyVisibility();
    }

    internal void ClearServerContext()
    {
        Server.ClearForContextChange();
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        Room.SetVisible(RoomOverlayOpen && SelectedChannel == LanConnectChatChannel.Room);
        Server.SetVisible(RoomOverlayOpen && SelectedChannel == LanConnectChatChannel.Server);
    }

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
