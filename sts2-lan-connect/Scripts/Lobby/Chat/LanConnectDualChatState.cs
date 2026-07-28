using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectDualChatState
{
    private bool _openedOnce;
    private LanConnectChatChannel _lastSelected = LanConnectChatChannel.Room;
    private long _seenRoomRemoteArrivalRevision;
    private long _seenServerRemoteArrivalRevision;

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

    /// <summary>
    /// True when a remote (never local -- see LanConnectChatChannelState.TrackIncoming) message
    /// has landed on the room channel since the overlay was last closed, or since construction if
    /// it has never been opened. Room chat HUD redesign regression fix: touch hides the reopen
    /// bubble on collapse and desktop never had one, so nothing used to bring the overlay back
    /// when a message arrived while it was closed. The "seen" baseline only advances here and in
    /// CloseRoomOverlay/LeaveRoom (not on every read), so a caller that finds this true and can't
    /// act on it yet (drag in progress, fade tween running) can just check again next frame --
    /// see LanConnectRoomChatOverlay.MaybeSurfaceForRemoteArrival, the only reader.
    ///
    /// This is deliberately room-only: server/频道 arrivals no longer reopen the overlay (players
    /// reported the panel popping back up on every channel message, making it impossible to keep
    /// closed). Channel unread is surfaced as a red dot on the toggle bubble and on the 频道 tab
    /// instead. The server seen-baseline is still maintained in MarkRemoteArrivalsSeen so a later
    /// close/leave keeps the bookkeeping symmetric.
    /// </summary>
    internal bool HasUnseenRoomRemoteArrival =>
        !RoomOverlayOpen &&
        ActiveRoomId != null &&
        Room.RemoteArrivalRevision != _seenRoomRemoteArrivalRevision;

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

        MarkRemoteArrivalsSeen();
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

    internal LanConnectChatChannel ShowRoomOverlayForRemoteArrival(
        LanConnectChatChannel arrivalChannel,
        bool serverSelectable = true)
    {
        if (ActiveRoomId == null)
        {
            throw new InvalidOperationException("A room must be active before showing room chat.");
        }

        SelectedChannel = arrivalChannel == LanConnectChatChannel.Server && !serverSelectable
            ? LanConnectChatChannel.Room
            : arrivalChannel;
        RoomOverlayOpen = true;
        Select(SelectedChannel);
        _openedOnce = true;
        return SelectedChannel;
    }

    internal void CloseRoomOverlay()
    {
        MarkRemoteArrivalsSeen();
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

    private void MarkRemoteArrivalsSeen()
    {
        _seenRoomRemoteArrivalRevision = Room.RemoteArrivalRevision;
        _seenServerRemoteArrivalRevision = Server.RemoteArrivalRevision;
    }

    private LanConnectChatChannel ChooseForOpen()
    {
        if (!_openedOnce)
        {
            return LanConnectChatChannel.Room;
        }

        if (Room.UnreadCount > 0)
        {
            return LanConnectChatChannel.Room;
        }

        return _lastSelected;
    }
}
