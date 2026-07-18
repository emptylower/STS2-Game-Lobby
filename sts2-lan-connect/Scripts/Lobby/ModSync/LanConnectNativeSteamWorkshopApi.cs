using System.Globalization;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using MegaCrit.Sts2.Core.Platform.Steam;
using Steamworks;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectNativeSteamWorkshopApi : ILanConnectSteamWorkshopApi
{
    private readonly object _sync = new();
    private readonly Dictionary<ulong, string> _downloadFailures = [];
    private Callback<DownloadItemResult_t>? _downloadCallback;
    private Callback<ItemInstalled_t>? _installedCallback;
    private bool _disposed;

    public LanConnectNativeSteamWorkshopApi(
        bool? isAndroidForTests = null,
        bool? steamInitializedForTests = null)
    {
        bool isAndroid = isAndroidForTests ?? OperatingSystem.IsAndroid();
        bool steamInitialized = steamInitializedForTests ?? SteamInitializer.Initialized;
        LanConnectModSyncAvailability availability =
            LanConnectNativeWorkshopAvailability.Resolve(isAndroid, steamInitialized);
        if (!availability.IsAvailable)
        {
            Availability = availability;
            return;
        }

        try
        {
            _downloadCallback = Callback<DownloadItemResult_t>.Create(OnDownloadCompleted);
            _installedCallback = Callback<ItemInstalled_t>.Create(OnItemInstalled);
            Availability = LanConnectModSyncAvailability.Available;
            CallbacksRegistered = true;
        }
        catch (Exception ex)
        {
            _downloadCallback?.Dispose();
            _installedCallback?.Dispose();
            _downloadCallback = null;
            _installedCallback = null;
            Availability = LanConnectModSyncAvailability.Unsupported(
                "steam_callback_unavailable",
                $"Steam Workshop callbacks could not be registered: {ex.Message}");
        }
    }

    public LanConnectModSyncAvailability Availability { get; }

    internal bool CallbacksRegistered { get; private set; }

    public event Action<ulong>? ItemChanged;

    public async Task<LanConnectWorkshopMetadata> QueryMetadataAsync(
        ulong workshopFileId,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        PublishedFileId_t publishedFileId = new(workshopFileId);
        UGCQueryHandle_t query = SteamUGC.CreateQueryUGCDetailsRequest([publishedFileId], 1);
        if (query == UGCQueryHandle_t.Invalid)
        {
            throw new InvalidOperationException("Steam could not create a Workshop metadata query.");
        }

        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                SteamInitializer.DisconnectToken);
            using SteamCallResult<SteamUGCQueryCompleted_t> callResult = new(
                SteamUGC.SendQueryUGCRequest(query),
                linked.Token);
            SteamUGCQueryCompleted_t completed = await callResult.Task;
            if (completed.m_eResult != EResult.k_EResultOK || completed.m_unNumResultsReturned < 1)
            {
                throw new InvalidOperationException(
                    $"Steam metadata query failed: {completed.m_eResult}.");
            }
            if (!SteamUGC.GetQueryUGCResult(query, 0, out SteamUGCDetails_t details) ||
                details.m_eResult != EResult.k_EResultOK)
            {
                throw new InvalidOperationException("Steam did not return Workshop item details.");
            }

            string publisher = ResolvePublisher(details.m_ulSteamIDOwner);
            return new LanConnectWorkshopMetadata(
                details.m_nPublishedFileId.m_PublishedFileId.ToString(CultureInfo.InvariantCulture),
                details.m_nConsumerAppID.m_AppId,
                details.m_rgchTitle.Trim(),
                publisher);
        }
        finally
        {
            SteamUGC.ReleaseQueryUGCRequest(query);
        }
    }

    public async Task<bool> SubscribeAsync(
        ulong workshopFileId,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        PublishedFileId_t id = new(workshopFileId);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            SteamInitializer.DisconnectToken);
        using SteamCallResult<RemoteStorageSubscribePublishedFileResult_t> callResult = new(
            SteamUGC.SubscribeItem(id),
            linked.Token);
        RemoteStorageSubscribePublishedFileResult_t result = await callResult.Task;
        return result.m_eResult == EResult.k_EResultOK &&
               result.m_nPublishedFileId.m_PublishedFileId == workshopFileId;
    }

    public async Task<bool> UnsubscribeAsync(
        ulong workshopFileId,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        PublishedFileId_t id = new(workshopFileId);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            SteamInitializer.DisconnectToken);
        using SteamCallResult<RemoteStorageUnsubscribePublishedFileResult_t> callResult = new(
            SteamUGC.UnsubscribeItem(id),
            linked.Token);
        RemoteStorageUnsubscribePublishedFileResult_t result = await callResult.Task;
        return result.m_eResult == EResult.k_EResultOK &&
               result.m_nPublishedFileId.m_PublishedFileId == workshopFileId;
    }

    public bool Download(ulong workshopFileId)
    {
        EnsureAvailable();
        lock (_sync)
        {
            _downloadFailures.Remove(workshopFileId);
        }
        return SteamUGC.DownloadItem(new PublishedFileId_t(workshopFileId), bHighPriority: true);
    }

    public LanConnectWorkshopItemStatus GetItemStatus(ulong workshopFileId)
    {
        EnsureAvailable();
        PublishedFileId_t id = new(workshopFileId);
        EItemState state = (EItemState)SteamUGC.GetItemState(id);
        bool installed = state.HasFlag(EItemState.k_EItemStateInstalled);
        bool downloading = state.HasFlag(EItemState.k_EItemStateDownloading);
        bool downloadPending = state.HasFlag(EItemState.k_EItemStateDownloadPending);
        ulong downloaded = 0;
        ulong total = 0;
        if (downloading || downloadPending)
        {
            SteamUGC.GetItemDownloadInfo(id, out downloaded, out total);
        }

        string? installFolder = null;
        if (installed && SteamUGC.GetItemInstallInfo(
                id,
                out _,
                out string folder,
                4096,
                out _))
        {
            installFolder = folder;
        }

        string? failure;
        lock (_sync)
        {
            _downloadFailures.TryGetValue(workshopFileId, out failure);
        }
        return new LanConnectWorkshopItemStatus(
            state.HasFlag(EItemState.k_EItemStateSubscribed),
            installed,
            state.HasFlag(EItemState.k_EItemStateNeedsUpdate),
            downloading,
            downloadPending,
            downloaded,
            total,
            installFolder,
            failure == null ? null : "workshop_download_callback_failed",
            failure);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _downloadCallback?.Dispose();
        _installedCallback?.Dispose();
        _downloadCallback = null;
        _installedCallback = null;
        CallbacksRegistered = false;
    }

    private void OnDownloadCompleted(DownloadItemResult_t result)
    {
        if (result.m_unAppID.m_AppId != LanConnectModSyncCapabilities.SteamAppId)
        {
            return;
        }
        ulong workshopFileId = result.m_nPublishedFileId.m_PublishedFileId;
        lock (_sync)
        {
            if (result.m_eResult == EResult.k_EResultOK)
            {
                _downloadFailures.Remove(workshopFileId);
            }
            else
            {
                _downloadFailures[workshopFileId] =
                    $"Steam Workshop download failed: {result.m_eResult}.";
            }
        }
        ItemChanged?.Invoke(workshopFileId);
    }

    private void OnItemInstalled(ItemInstalled_t result)
    {
        if (result.m_unAppID.m_AppId == LanConnectModSyncCapabilities.SteamAppId)
        {
            ItemChanged?.Invoke(result.m_nPublishedFileId.m_PublishedFileId);
        }
    }

    private static string ResolvePublisher(ulong ownerSteamId)
    {
        CSteamID owner = new(ownerSteamId);
        SteamFriends.RequestUserInformation(owner, bRequireNameOnly: true);
        string? name = SteamFriends.GetFriendPersonaName(owner)?.Trim();
        return string.IsNullOrWhiteSpace(name) || name.StartsWith("[unknown]", StringComparison.OrdinalIgnoreCase)
            ? $"Steam ID {ownerSteamId.ToString(CultureInfo.InvariantCulture)}"
            : name;
    }

    private void EnsureAvailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Availability.IsAvailable)
        {
            throw new InvalidOperationException(Availability.Message);
        }
    }
}
