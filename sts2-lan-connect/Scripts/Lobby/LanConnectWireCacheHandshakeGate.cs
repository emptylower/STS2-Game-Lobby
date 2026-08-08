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
}
