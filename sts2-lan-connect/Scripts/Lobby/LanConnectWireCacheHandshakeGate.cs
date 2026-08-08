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

    internal static bool ShouldRunHostHandler(
        Func<LanConnectWireCacheHandshakeDecision> evaluate,
        Action<LanConnectWireCacheHandshakeDecision> observeDecision,
        Action<LanConnectWireCacheHandshakeDecision> disconnectMismatch,
        Action<Exception> observeEvaluationFailure)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentNullException.ThrowIfNull(observeDecision);
        ArgumentNullException.ThrowIfNull(disconnectMismatch);
        ArgumentNullException.ThrowIfNull(observeEvaluationFailure);

        LanConnectWireCacheHandshakeDecision decision;
        try
        {
            decision = evaluate()
                ?? throw new InvalidOperationException("Wire cache handshake evaluation returned null.");
        }
        catch (Exception ex)
        {
            InvokeBestEffort(() => observeEvaluationFailure(ex));
            return true;
        }

        InvokeBestEffort(() => observeDecision(decision));
        if (decision.Kind != LanConnectWireCacheHandshakeDecisionKind.Mismatch)
        {
            return true;
        }

        InvokeBestEffort(() => disconnectMismatch(decision));
        return false;
    }

    private static void InvokeBestEffort(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Host gating is fail-open unless evaluation proved a genuine mismatch.
        }
    }
}
