using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal interface ILanConnectInviteServerJoinPorts
{
    string CurrentServer { get; }

    Task SwitchServerAsync(string server, CancellationToken cancellationToken);

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
        if (!string.Equals(currentServer, inviteServer, StringComparison.OrdinalIgnoreCase))
        {
            await _ports.SwitchServerAsync(inviteServer, cancellationToken);
        }

        IReadOnlyList<LobbyRoomSummary> rooms = await _ports.GetRoomsAsync(cancellationToken);
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
            return LanConnectInviteJoinResult.RoomNotFound;
        }

        await _ports.BeginJoinAsync(targetRoom, payload.P, cancellationToken);
        return LanConnectInviteJoinResult.JoinStarted;
    }

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
