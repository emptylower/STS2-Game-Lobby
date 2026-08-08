namespace Sts2LanConnect.Scripts;

internal static class LanConnectWireCacheHandshakeGate
{
    internal static bool ShouldAllowJoin(
        LanConnectWireCacheHandshakeDecision decision,
        string diagnostic,
        Action<string> logInfo,
        Action<string> logWarning)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(logInfo);
        ArgumentNullException.ThrowIfNull(logWarning);

        try
        {
            if (decision.Kind == LanConnectWireCacheHandshakeDecisionKind.Match)
            {
                logInfo(diagnostic);
            }
            else
            {
                logWarning(diagnostic);
            }
        }
        catch
        {
            // Diagnostics must not change the handshake decision.
        }

        return decision.IsAllowed;
    }

    internal static void ObserveHostDecision(
        LanConnectWireCacheHandshakeDecision decision,
        LanConnectWireCacheHandshakeTokenStatus remoteStatus,
        ulong senderId,
        string path,
        Action<string> logInfo,
        Action<string> logWarning)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(logInfo);
        ArgumentNullException.ThrowIfNull(logWarning);

        string decisionName = decision.Kind switch
        {
            LanConnectWireCacheHandshakeDecisionKind.Match => "match",
            LanConnectWireCacheHandshakeDecisionKind.Mismatch => "mismatch",
            LanConnectWireCacheHandshakeDecisionKind.LocalUnavailable => "local-unavailable",
            LanConnectWireCacheHandshakeDecisionKind.RemoteAbsent => "remote-absent",
            _ => decision.Kind.ToString()
        };
        string diagnostic =
            $"sts2_lan_connect wire_handshake host: path={path}, senderId={senderId}, " +
            $"localSignature={decision.LocalToken?.Signature ?? "unavailable"}, " +
            $"remoteSignature={decision.RemoteToken?.Signature ?? "absent"}, " +
            $"decision={decisionName}, " +
            $"localWidths={decision.LocalToken?.FormatWidths() ?? "unavailable"}, " +
            $"remoteWidths={decision.RemoteToken?.FormatWidths() ?? "unavailable"}, " +
            $"remoteSentinelStatus={remoteStatus}";
        try
        {
            if (decision.Kind == LanConnectWireCacheHandshakeDecisionKind.Match)
            {
                logInfo(diagnostic);
            }
            else
            {
                logWarning(diagnostic);
            }
        }
        catch
        {
            // Host diagnostics must never affect the game handler lifecycle.
        }
    }
}
