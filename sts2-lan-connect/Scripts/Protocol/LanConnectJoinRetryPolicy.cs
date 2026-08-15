using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectJoinRetryPolicy
{
    public static bool IsRetryable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is LanConnectProtocolException or OperationCanceledException)
        {
            return false;
        }

        return IsRetryableClientConnectionFailure(exception);
    }

    public static bool IsRetryableReason(string? reason) => reason is
        "Timeout" or "HandshakeTimeout" or "UnknownNetworkError";

    private static bool IsRetryableClientConnectionFailure(Exception exception) =>
        exception is ClientConnectionFailedException connectionFailure
        && IsRetryableReason(connectionFailure.info.GetReason().ToString());
}
