namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectModSyncAvailability(
    bool IsAvailable,
    string Code,
    string Message)
{
    public static LanConnectModSyncAvailability Available { get; } =
        new(true, "available", string.Empty);

    public static LanConnectModSyncAvailability Unsupported(string code, string message) =>
        new(false, code, message);
}

internal static class LanConnectNativeWorkshopAvailability
{
    public static LanConnectModSyncAvailability Resolve(bool isAndroid, bool steamInitialized)
    {
        if (isAndroid)
        {
            return LanConnectModSyncAvailability.Unsupported(
                "android_manual_only",
                "Android does not support Steam Workshop automation; handle MODs manually.");
        }
        return steamInitialized
            ? LanConnectModSyncAvailability.Available
            : LanConnectModSyncAvailability.Unsupported(
                "steam_api_unavailable",
                "SteamAPI is unavailable; handle Workshop MODs manually.");
    }
}

internal sealed record LanConnectWorkshopMetadata(
    string WorkshopFileId,
    uint ConsumerAppId,
    string Title,
    string Publisher);

internal sealed record LanConnectWorkshopMetadataQueryResult
{
    public bool Success { get; init; }
    public LanConnectModSyncAvailability Availability { get; init; } = LanConnectModSyncAvailability.Available;
    public LanConnectWorkshopMetadata? Metadata { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }

    public static LanConnectWorkshopMetadataQueryResult Succeeded(LanConnectWorkshopMetadata metadata) => new()
    {
        Success = true,
        Metadata = metadata
    };

    public static LanConnectWorkshopMetadataQueryResult Failed(
        LanConnectModSyncAvailability availability,
        string errorCode,
        string message) => new()
    {
        Availability = availability,
        ErrorCode = errorCode,
        Message = message
    };
}

internal sealed record LanConnectWorkshopItemStatus(
    bool IsSubscribed,
    bool IsInstalled,
    bool NeedsUpdate,
    bool IsDownloading,
    bool IsDownloadPending,
    ulong BytesDownloaded,
    ulong BytesTotal,
    string? InstallFolder,
    string? ErrorCode = null,
    string? ErrorMessage = null);

internal interface ILanConnectSteamWorkshopApi : IDisposable
{
    LanConnectModSyncAvailability Availability { get; }

    event Action<ulong>? ItemChanged;

    Task<LanConnectWorkshopMetadata> QueryMetadataAsync(
        ulong workshopFileId,
        CancellationToken cancellationToken);

    Task<bool> SubscribeAsync(ulong workshopFileId, CancellationToken cancellationToken);

    Task<bool> UnsubscribeAsync(ulong workshopFileId, CancellationToken cancellationToken);

    bool Download(ulong workshopFileId);

    LanConnectWorkshopItemStatus GetItemStatus(ulong workshopFileId);
}

internal interface ILanConnectWorkshopClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class LanConnectSystemWorkshopClock : ILanConnectWorkshopClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

internal interface ILanConnectModSyncProvider : IDisposable
{
    LanConnectModSyncAvailability Availability { get; }

    Task<LanConnectWorkshopMetadataQueryResult> QueryMetadataAsync(
        LobbyModDescriptor descriptor,
        CancellationToken cancellationToken = default);

    Task<LanConnectWorkshopJobSnapshot> SubmitAsync(
        LobbyModDescriptor descriptor,
        LanConnectWorkshopMetadata metadata,
        CancellationToken cancellationToken = default);

    LanConnectWorkshopJobSnapshot Poll(Guid jobId);

    LanConnectWorkshopJobSnapshot Snapshot(Guid jobId);

    bool Cancel(Guid jobId);

    Task<LanConnectWorkshopJobSnapshot> RetryAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
