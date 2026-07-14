using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal interface ILanConnectInviteServerJoinPorts
{
    string CurrentServer { get; }

    CancellationToken CurrentServerContextToken { get; }

    bool IsSwitchInProgress { get; }

    Task<CancellationToken> SwitchServerAsync(string server, CancellationToken cancellationToken);

    Task<IReadOnlyList<LobbyRoomSummary>> GetRoomsAsync(CancellationToken cancellationToken);

    Task BeginJoinAsync(
        LobbyRoomSummary room,
        string? password,
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

    internal async Task<LanConnectInviteJoinResult> JoinAsync(
        LanConnectInvitePayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        string currentServer = NormalizeServer(_ports.CurrentServer, nameof(_ports.CurrentServer));
        string inviteServer = NormalizeServer(payload.S, nameof(payload));
        CancellationToken serverContext;
        if (!string.Equals(currentServer, inviteServer, StringComparison.OrdinalIgnoreCase))
        {
            serverContext = await _ports.SwitchServerAsync(inviteServer, cancellationToken);
        }
        else
        {
            serverContext = _ports.CurrentServerContextToken;
            if (_ports.IsSwitchInProgress)
            {
                throw ServerContextChanged(serverContext);
            }
        }

        serverContext.ThrowIfCancellationRequested();
        if (!string.Equals(
                NormalizeServer(_ports.CurrentServer, nameof(_ports.CurrentServer)),
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
        await _ports.BeginJoinAsync(targetRoom, payload.P, operationToken);
        operationToken.ThrowIfCancellationRequested();
        return LanConnectInviteJoinResult.JoinStarted;
    }

    private static OperationCanceledException ServerContextChanged(CancellationToken contextToken) =>
        new("Lobby server context changed while joining an invite.", contextToken);

    private static string NormalizeServer(string server, string parameterName)
    {
        string normalized = server?.Trim().TrimEnd('/') ?? string.Empty;
        if (normalized.Length == 0 ||
            !Uri.TryCreate(normalized, UriKind.Absolute, out Uri? parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Lobby server must be an absolute HTTP or HTTPS URL.",
                parameterName);
        }

        return normalized;
    }
}
