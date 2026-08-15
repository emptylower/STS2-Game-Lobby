namespace Sts2LanConnect.Scripts;

internal static class LanConnectTailMessageTypeMatrix
{
    internal static bool TryGetKind(string typeName, out LanConnectSidecarMessageKind kind)
    {
        foreach (LanConnectSidecarMessageKind candidate in Enum.GetValues<LanConnectSidecarMessageKind>())
        {
            if (string.Equals(typeName, GetTypeName(candidate), StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    internal static string GetTypeName(LanConnectSidecarMessageKind kind) => kind switch
    {
        LanConnectSidecarMessageKind.InitialGameInfo => "InitialGameInfoMessage",
        LanConnectSidecarMessageKind.LobbyJoinRequest => "ClientLobbyJoinRequestMessage",
        LanConnectSidecarMessageKind.LobbyJoinResponse => "ClientLobbyJoinResponseMessage",
        LanConnectSidecarMessageKind.LoadJoinRequest => "ClientLoadJoinRequestMessage",
        LanConnectSidecarMessageKind.LoadJoinResponse => "ClientLoadJoinResponseMessage",
        LanConnectSidecarMessageKind.RejoinRequest => "ClientRejoinRequestMessage",
        LanConnectSidecarMessageKind.RejoinResponse => "ClientRejoinResponseMessage",
        LanConnectSidecarMessageKind.ConnectionFailed => "InitialGameInfoMessage",
        LanConnectSidecarMessageKind.PlayerJoined => "PlayerJoinedMessage",
        LanConnectSidecarMessageKind.LobbyBeginRun => "LobbyBeginRunMessage",
        _ => throw new InvalidDataException($"Unknown LAN message kind {kind}.")
    };
}
