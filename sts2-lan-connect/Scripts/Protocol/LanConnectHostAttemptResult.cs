namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectHostAttemptResult(
    bool Succeeded,
    string? FailureMessage = null,
    LanConnectProtocolFailure? ProtocolFailure = null)
{
    public static LanConnectHostAttemptResult Success() => new(true);

    public static LanConnectHostAttemptResult Failed(string? message) => new(false, message);

    public static LanConnectHostAttemptResult Failed(LanConnectProtocolFailure failure) =>
        new(false, null, failure ?? throw new ArgumentNullException(nameof(failure)));
}
