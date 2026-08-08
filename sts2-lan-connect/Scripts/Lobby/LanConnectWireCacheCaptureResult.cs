namespace Sts2LanConnect.Scripts;

internal enum LanConnectWireCacheCaptureStatus
{
    Available,
    Unavailable
}

internal enum LanConnectWireCacheSignatureComparisonStatus
{
    Match,
    Mismatch,
    Unavailable
}

internal sealed record LanConnectWireCacheSignatureComparison(
    LanConnectWireCacheSignatureComparisonStatus Status,
    string? ActualSignature,
    string? FailureReason);

internal sealed record LanConnectWireCacheCaptureResult
{
    private LanConnectWireCacheCaptureResult(
        LanConnectWireCacheCaptureStatus status,
        LanConnectWireCacheSnapshot? snapshot,
        string? failureReason,
        Exception? failureException)
    {
        Status = status;
        Snapshot = snapshot;
        FailureReason = failureReason;
        FailureException = failureException;
    }

    public LanConnectWireCacheCaptureStatus Status { get; }

    public LanConnectWireCacheSnapshot? Snapshot { get; }

    public string? FailureReason { get; }

    public Exception? FailureException { get; }

    public bool IsAvailable => Status == LanConnectWireCacheCaptureStatus.Available;

    public static LanConnectWireCacheCaptureResult Available(LanConnectWireCacheSnapshot snapshot) =>
        new(LanConnectWireCacheCaptureStatus.Available, snapshot, null, null);

    public static LanConnectWireCacheCaptureResult Unavailable(Exception exception) =>
        new(
            LanConnectWireCacheCaptureStatus.Unavailable,
            null,
            $"{exception.GetType().Name}: {exception.Message}",
            exception);

    public LanConnectWireCacheSnapshot GetRequiredSnapshot()
    {
        if (Snapshot != null)
        {
            return Snapshot;
        }

        throw new LanConnectWireCacheUnavailableException(
            FailureReason ?? "WireCacheSignatureV1 capture is unavailable.",
            FailureException);
    }

    public LanConnectWireCacheSignatureComparison CompareSignature(string expectedSignature)
    {
        if (!IsAvailable)
        {
            return new LanConnectWireCacheSignatureComparison(
                LanConnectWireCacheSignatureComparisonStatus.Unavailable,
                null,
                FailureReason);
        }

        string actualSignature = Snapshot!.Signature;
        return new LanConnectWireCacheSignatureComparison(
            string.Equals(actualSignature, expectedSignature, StringComparison.Ordinal)
                ? LanConnectWireCacheSignatureComparisonStatus.Match
                : LanConnectWireCacheSignatureComparisonStatus.Mismatch,
            actualSignature,
            null);
    }
}

internal sealed class LanConnectWireCacheUnavailableException : InvalidOperationException
{
    internal LanConnectWireCacheUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
