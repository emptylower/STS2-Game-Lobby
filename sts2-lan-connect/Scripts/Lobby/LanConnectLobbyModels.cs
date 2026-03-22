using System;
using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

internal sealed class LobbyRoomSummary
{
    public string RoomId { get; set; } = string.Empty;

    public string RoomName { get; set; } = string.Empty;

    public string HostPlayerName { get; set; } = string.Empty;

    public bool RequiresPassword { get; set; }

    public string Status { get; set; } = LanConnectConstants.DefaultRoomStatus;

    public string GameMode { get; set; } = LanConnectConstants.DefaultGameMode;

    public int CurrentPlayers { get; set; }

    public int MaxPlayers { get; set; } = LanConnectConstants.DefaultMaxPlayers;

    public string Version { get; set; } = string.Empty;

    public string ModVersion { get; set; } = string.Empty;

    public string RelayState { get; set; } = "disabled";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastHeartbeatAt { get; set; }

    public LobbySavedRunInfo? SavedRun { get; set; }
}

internal sealed class LobbyDirectoryServerListResponse
{
    public bool Ok { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    public List<LobbyDirectoryServerEntry> Servers { get; set; } = new();
}

internal sealed class LobbyAnnouncementResponse
{
    public bool Ok { get; set; }

    public bool Visible { get; set; }

    public LobbyAnnouncementEntry? Announcement { get; set; }
}

internal sealed class LobbyAnnouncementEntry
{
    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset? UpdatedAt { get; set; }
}

internal sealed class LobbyDirectoryServerEntry
{
    public string ServerId { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public int Rooms { get; set; }

    public DateTimeOffset LastVerifiedAt { get; set; }
}

internal sealed class LobbySavedRunInfo
{
    public string SaveKey { get; set; } = string.Empty;

    public List<LobbySavedRunSlot> Slots { get; set; } = new();

    public List<string> ConnectedPlayerNetIds { get; set; } = new();
}

internal sealed class LobbySavedRunSlot
{
    public string NetId { get; set; } = string.Empty;

    public string CharacterId { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public bool IsHost { get; set; }

    public bool IsConnected { get; set; }
}

internal sealed class LobbyHostConnectionInfo
{
    public ushort EnetPort { get; set; } = LanConnectConstants.DefaultPort;

    public List<string> LocalAddresses { get; set; } = new();
}

internal sealed class LobbyCreateRoomRequest
{
    public string RoomName { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string HostPlayerName { get; set; } = string.Empty;

    public string GameMode { get; set; } = LanConnectConstants.DefaultGameMode;

    public string Version { get; set; } = string.Empty;

    public string ModVersion { get; set; } = string.Empty;

    public List<string> ModList { get; set; } = new();

    public int MaxPlayers { get; set; } = LanConnectConstants.DefaultMaxPlayers;

    public LobbyHostConnectionInfo HostConnectionInfo { get; set; } = new();

    public LobbySavedRunInfo? SavedRun { get; set; }
}

internal sealed class LobbyCreateRoomResponse
{
    public string RoomId { get; set; } = string.Empty;

    public string ControlChannelId { get; set; } = string.Empty;

    public string HostToken { get; set; } = string.Empty;

    public int HeartbeatIntervalSeconds { get; set; } = (int)LanConnectConstants.LobbyHeartbeatIntervalSeconds;

    public LobbyRoomSummary Room { get; set; } = new();

    public LobbyRelayEndpoint? RelayEndpoint { get; set; }
}

internal sealed class LobbyJoinRoomRequest
{
    public string PlayerName { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string Version { get; set; } = string.Empty;

    public string ModVersion { get; set; } = string.Empty;

    public List<string> ModList { get; set; } = new();

    public string? DesiredSavePlayerNetId { get; set; }
}

internal sealed class LobbyDirectEndpoint
{
    public string Label { get; set; } = string.Empty;

    public string Ip { get; set; } = string.Empty;

    public ushort Port { get; set; } = LanConnectConstants.DefaultPort;
}

internal sealed class LobbyConnectionPlan
{
    public string Strategy { get; set; } = "direct-first";

    public bool RelayAllowed { get; set; }

    public string? ControlChannelId { get; set; }

    public List<LobbyDirectEndpoint> DirectCandidates { get; set; } = new();

    public LobbyRelayEndpoint? RelayEndpoint { get; set; }
}

internal sealed class LobbyRelayEndpoint
{
    public string Host { get; set; } = string.Empty;

    public ushort Port { get; set; }
}

internal sealed class LobbyJoinRoomResponse
{
    public string TicketId { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public LobbyRoomSummary Room { get; set; } = new();

    public LobbyConnectionPlan ConnectionPlan { get; set; } = new();
}

internal sealed class LobbyHeartbeatRequest
{
    public string HostToken { get; set; } = string.Empty;

    public int CurrentPlayers { get; set; }

    public string Status { get; set; } = LanConnectConstants.DefaultRoomStatus;

    public List<string>? ConnectedPlayerNetIds { get; set; }
}

internal sealed class LobbyDeleteRoomRequest
{
    public string HostToken { get; set; } = string.Empty;
}

internal sealed class LobbyConnectionEventRequest
{
    public string? TicketId { get; set; }

    public string Phase { get; set; } = string.Empty;

    public string? CandidateLabel { get; set; }

    public string? CandidateEndpoint { get; set; }

    public string? Detail { get; set; }

    public string? PlayerName { get; set; }
}

internal sealed class LobbyControlEnvelope
{
    public string Type { get; set; } = string.Empty;

    public string RoomId { get; set; } = string.Empty;

    public string? ControlChannelId { get; set; }

    public string? Role { get; set; }

    public string? TicketId { get; set; }

    public string? PlayerNetId { get; set; }

    public string? PlayerName { get; set; }

    public List<LobbyPlayerNameEntry>? PlayerNames { get; set; }

    public string? MessageId { get; set; }

    public string? MessageText { get; set; }

    public long? SentAtUnixMs { get; set; }
}

internal sealed class LobbyRoomChatEntry
{
    public string RoomId { get; set; } = string.Empty;

    public string MessageId { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string? SenderNetId { get; set; }

    public string MessageText { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }

    public bool IsLocal { get; set; }
}

internal sealed class LobbyPlayerNameEntry
{
    public string PlayerNetId { get; set; } = string.Empty;

    public string PlayerName { get; set; } = string.Empty;
}

internal sealed class LobbyErrorResponse
{
    public string Code { get; set; } = "lobby_error";

    public string Message { get; set; } = "大厅服务请求失败。";

    public LobbyErrorDetails? Details { get; set; }
}

internal sealed class LobbyErrorDetails
{
    public string? RoomModVersion { get; set; }

    public string? RequestedModVersion { get; set; }

    public List<string>? MissingModsOnLocal { get; set; }

    public List<string>? MissingModsOnHost { get; set; }
}
