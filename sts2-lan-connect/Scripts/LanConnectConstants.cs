namespace Sts2LanConnect.Scripts;

internal static class LanConnectConstants
{
    public const ushort DefaultPort = 33771;

    public const int DefaultMaxPlayers = 8;

    public const int MinMaxPlayers = 4;

    public const int MaxMaxPlayers = 16;

    public const int VanillaSlotIdBits = 2;

    public const int ExtendedSlotIdBits = 4;

    public const int VanillaLobbyListBits = 3;

    public const int ExtendedLobbyListBits = 5;

    public const int LobbyRoomsPerPage = 5;

    public static readonly string DefaultLobbyServerBaseUrl = LanConnectLobbyEndpointDefaults.GetDefaultBaseUrl();

    public const string DefaultCompatibilityProfile = "test_relaxed";

    public const string DefaultConnectionStrategy = "relay-only";

    public const string DefaultRoomStatus = "open";

    public const string DefaultGameMode = "standard";

    public const double LobbyRefreshIntervalSeconds = 8d;

    public const double LobbyHeartbeatIntervalSeconds = 10d;

    public const double LobbyHeartbeatTimeoutSeconds = 35d;

    public const string JoinContainerName = "LanConnectJoinContainer";

    public const string EndpointInputName = "LanConnectEndpointInput";

    public const string JoinButtonName = "LanConnectJoinButton";

    public const string HostButtonName = "LanConnectHostButton";

    public const string LobbyEntryButtonName = "LanConnectLobbyEntryButton";

    public const string LobbyOverlayName = "LanConnectLobbyOverlay";

    public const string RoomChatOverlayName = "LanConnectRoomChatOverlay";

    public const string SafeLoadButtonName = "LanConnectSafeLoadButton";

    public const string SafeAbandonButtonName = "LanConnectSafeAbandonButton";

    public const string RoomManagementButtonName = "LanConnectRoomManagementButton";

    public const string RoomManagementPanelName = "LanConnectRoomManagementPanel";
}
