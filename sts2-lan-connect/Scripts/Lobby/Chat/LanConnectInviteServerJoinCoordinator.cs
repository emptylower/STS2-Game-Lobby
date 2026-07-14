using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal interface ILanConnectInviteServerJoinPorts
{
    string CurrentServer { get; }

    bool IsSwitchInProgress { get; }

    LanConnectServerContextLease AcquireCurrentServerContext();

    Task<LanConnectServerContextLease> SwitchServerAsync(
        string server,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LobbyRoomSummary>> GetRoomsAsync(CancellationToken cancellationToken);

    Task BeginJoinAsync(
        LobbyRoomSummary room,
        string? password,
        LanConnectServerContextLease contextLease,
        CancellationToken cancellationToken);
}

internal enum LanConnectInviteJoinResult
{
    JoinStarted,
    RoomNotFound
}

internal sealed class LanConnectInviteServerJoinCoordinator
{
    private readonly ILanConnectInviteServerJoinPorts _ports;

    internal LanConnectInviteServerJoinCoordinator(ILanConnectInviteServerJoinPorts ports)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
    }

    internal static bool TargetsActiveRoom(
        LanConnectInvitePayload payload,
        string? activeRoomId,
        string currentServer)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!string.Equals(activeRoomId, payload.R, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(
            LanConnectLobbyServerAddress.NormalizeAuthority(currentServer, nameof(currentServer)),
            LanConnectLobbyServerAddress.NormalizeAuthority(payload.S, nameof(payload)),
            StringComparison.OrdinalIgnoreCase);
    }

    internal async Task<LanConnectInviteJoinResult> JoinAsync(
        LanConnectInvitePayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        string currentServer = LanConnectLobbyServerAddress.NormalizeAuthority(
            _ports.CurrentServer,
            nameof(_ports.CurrentServer));
        string inviteServer = LanConnectLobbyServerAddress.NormalizeAuthority(
            payload.S,
            nameof(payload));
        bool crossServer = !string.Equals(
            currentServer,
            inviteServer,
            StringComparison.OrdinalIgnoreCase);
        LanConnectServerContextLease? serverContextLease = null;
        try
        {
            if (crossServer)
            {
                serverContextLease = await _ports.SwitchServerAsync(inviteServer, cancellationToken);
            }
            else
            {
                serverContextLease = _ports.AcquireCurrentServerContext();
                if (_ports.IsSwitchInProgress)
                {
                    throw ServerContextChanged(serverContextLease.Token);
                }
                if (serverContextLease.Token.IsCancellationRequested)
                {
                    serverContextLease.Dispose();
                    serverContextLease = await _ports.SwitchServerAsync(inviteServer, cancellationToken);
                }
            }

            CancellationToken serverContext = serverContextLease.Token;
            serverContext.ThrowIfCancellationRequested();
            if (!string.Equals(
                    LanConnectLobbyServerAddress.NormalizeAuthority(
                        _ports.CurrentServer,
                        nameof(_ports.CurrentServer)),
                    inviteServer,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw ServerContextChanged(serverContext);
            }
            serverContext.ThrowIfCancellationRequested();

            using CancellationTokenSource operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, serverContext);
            CancellationToken operationToken = operationCancellation.Token;
            IReadOnlyList<LobbyRoomSummary> rooms = await _ports.GetRoomsAsync(operationToken);
            operationToken.ThrowIfCancellationRequested();
            LobbyRoomSummary? targetRoom = null;
            foreach (LobbyRoomSummary room in rooms)
            {
                if (string.Equals(room.RoomId, payload.R, StringComparison.Ordinal))
                {
                    targetRoom = room;
                    break;
                }
            }

            if (targetRoom == null)
            {
                operationToken.ThrowIfCancellationRequested();
                return LanConnectInviteJoinResult.RoomNotFound;
            }

            operationToken.ThrowIfCancellationRequested();
            LanConnectServerContextLease transferredLease = serverContextLease;
            serverContextLease = null;
            await _ports.BeginJoinAsync(
                targetRoom,
                payload.P,
                transferredLease,
                operationToken);
            operationToken.ThrowIfCancellationRequested();
            return LanConnectInviteJoinResult.JoinStarted;
        }
        finally
        {
            serverContextLease?.Dispose();
        }
    }

    private static OperationCanceledException ServerContextChanged(CancellationToken contextToken) =>
        new("Lobby server context changed while joining an invite.", contextToken);

}
