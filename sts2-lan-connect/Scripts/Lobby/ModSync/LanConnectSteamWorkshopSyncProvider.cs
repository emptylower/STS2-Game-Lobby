namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectSteamWorkshopSyncProvider : ILanConnectModSyncProvider
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _sync = new();
    private readonly ILanConnectSteamWorkshopApi _api;
    private readonly ILanConnectWorkshopManifestVerifier _manifestVerifier;
    private readonly ILanConnectWorkshopClock _clock;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollInterval;
    private readonly Dictionary<Guid, LanConnectWorkshopJob> _jobs = [];
    private bool _disposed;

    public LanConnectSteamWorkshopSyncProvider(
        ILanConnectSteamWorkshopApi api,
        ILanConnectWorkshopManifestVerifier manifestVerifier,
        ILanConnectWorkshopClock clock,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _manifestVerifier = manifestVerifier ?? throw new ArgumentNullException(nameof(manifestVerifier));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeout = timeout ?? DefaultTimeout;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _api.ItemChanged += OnItemChanged;
    }

    public LanConnectModSyncAvailability Availability => _api.Availability;

    public static LanConnectSteamWorkshopSyncProvider CreateNative() => new(
        new LanConnectNativeSteamWorkshopApi(),
        new LanConnectWorkshopMetadataVerifier(),
        new LanConnectSystemWorkshopClock());

    public async Task<LanConnectWorkshopMetadataQueryResult> QueryMetadataAsync(
        LobbyModDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!Availability.IsAvailable)
        {
            return LanConnectWorkshopMetadataQueryResult.Failed(
                Availability,
                Availability.Code,
                Availability.Message);
        }
        if (!TryCanonicalizeDescriptor(descriptor, out LobbyModDescriptor canonicalDescriptor))
        {
            return LanConnectWorkshopMetadataQueryResult.Failed(
                Availability,
                "workshop_descriptor_invalid",
                "Workshop MOD descriptor failed canonical validation.");
        }
        descriptor = canonicalDescriptor;
        if (descriptor.Source != LanConnectModSources.SteamWorkshop)
        {
            return LanConnectWorkshopMetadataQueryResult.Failed(
                Availability,
                "workshop_source_required",
                "Only Steam Workshop descriptors can be synchronized automatically.");
        }
        if (descriptor.Role is not (LanConnectModRoles.Gameplay or LanConnectModRoles.Dependency))
        {
            return LanConnectWorkshopMetadataQueryResult.Failed(
                Availability,
                "workshop_role_invalid",
                "Only gameplay MODs and their required dependencies can be synchronized.");
        }
        if (!ulong.TryParse(descriptor.WorkshopFileId, out ulong workshopFileId) || workshopFileId == 0)
        {
            return LanConnectWorkshopMetadataQueryResult.Failed(
                Availability,
                "workshop_invalid_id",
                "Workshop file ID is invalid.");
        }

        try
        {
            LanConnectWorkshopMetadata metadata = await _api.QueryMetadataAsync(
                workshopFileId,
                cancellationToken);
            LanConnectWorkshopMetadataValidation validation =
                new LanConnectWorkshopMetadataVerifier().VerifyMetadata(descriptor, metadata);
            return validation.Success
                ? LanConnectWorkshopMetadataQueryResult.Succeeded(metadata)
                : LanConnectWorkshopMetadataQueryResult.Failed(
                    Availability,
                    validation.ErrorCode ?? "workshop_metadata_invalid",
                    validation.Message ?? "Workshop metadata is invalid.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return LanConnectWorkshopMetadataQueryResult.Failed(
                Availability,
                "workshop_metadata_failed",
                ex.Message);
        }
    }

    public Task<LanConnectWorkshopJobSnapshot> SubmitAsync(
        LobbyModDescriptor descriptor,
        LanConnectWorkshopMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        LanConnectWorkshopJob job = CreateJob(descriptor, metadata, attempt: 1);
        _ = RunJobAsync(job, cancellationToken);
        return Task.FromResult(job.Snapshot());
    }

    public LanConnectWorkshopJobSnapshot Poll(Guid jobId) => Snapshot(jobId);

    public LanConnectWorkshopJobSnapshot Snapshot(Guid jobId)
    {
        lock (_sync)
        {
            return GetJob(jobId).Snapshot();
        }
    }

    public bool Cancel(Guid jobId)
    {
        lock (_sync)
        {
            return GetJob(jobId).Cancel();
        }
    }

    public Task<LanConnectWorkshopJobSnapshot> RetryAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        LanConnectWorkshopJob prior;
        lock (_sync)
        {
            prior = GetJob(jobId);
            if (!prior.Snapshot().IsTerminal)
            {
                throw new InvalidOperationException("Only terminal Workshop jobs can be retried.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        LanConnectWorkshopJob retry = CreateJob(
            prior.Descriptor,
            prior.Metadata,
            prior.Attempt + 1);
        _ = RunJobAsync(retry, cancellationToken);
        return Task.FromResult(retry.Snapshot());
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (LanConnectWorkshopJob job in _jobs.Values)
            {
                if (!job.Snapshot().IsTerminal)
                {
                    job.Cancel();
                }
            }
        }
        _api.ItemChanged -= OnItemChanged;
        _api.Dispose();
    }

    private LanConnectWorkshopJob CreateJob(
        LobbyModDescriptor descriptor,
        LanConnectWorkshopMetadata metadata,
        int attempt)
    {
        LanConnectWorkshopJob job = new(descriptor, metadata, attempt, _clock.UtcNow);
        lock (_sync)
        {
            _jobs.Add(job.JobId, job);
        }
        return job;
    }

    private async Task RunJobAsync(
        LanConnectWorkshopJob job,
        CancellationToken externalCancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            job.Cancellation.Token,
            externalCancellationToken);
        CancellationToken cancellationToken = linked.Token;
        try
        {
            job.Transition(LanConnectWorkshopJobState.Validating);
            if (!Availability.IsAvailable)
            {
                job.Complete(
                    LanConnectWorkshopJobState.Failed,
                    Availability.Code,
                    Availability.Message);
                return;
            }
            if (!TryCanonicalizeDescriptor(job.Descriptor, out LobbyModDescriptor descriptor))
            {
                job.Complete(
                    LanConnectWorkshopJobState.Failed,
                    "workshop_descriptor_invalid",
                    "Workshop MOD descriptor failed canonical validation.");
                return;
            }
            LanConnectWorkshopMetadataValidation validation =
                new LanConnectWorkshopMetadataVerifier().VerifyMetadata(descriptor, job.Metadata);
            if (!validation.Success)
            {
                job.Complete(
                    LanConnectWorkshopJobState.Failed,
                    validation.ErrorCode,
                    validation.Message);
                return;
            }
            if (!ulong.TryParse(descriptor.WorkshopFileId, out ulong workshopFileId) || workshopFileId == 0)
            {
                job.Complete(
                    LanConnectWorkshopJobState.Failed,
                    "workshop_invalid_id",
                    "Workshop file ID is invalid.");
                return;
            }

            LanConnectWorkshopItemStatus initialStatus = _api.GetItemStatus(workshopFileId);
            job.WasSubscribedInitially = initialStatus.IsSubscribed;
            job.Transition(LanConnectWorkshopJobState.Subscribing);
            if (!initialStatus.IsSubscribed)
            {
                if (!await _api.SubscribeAsync(workshopFileId, cancellationToken))
                {
                    job.Complete(
                        LanConnectWorkshopJobState.Failed,
                        "workshop_subscribe_failed",
                        "Steam rejected the Workshop subscription.");
                    return;
                }
            }

            job.Transition(LanConnectWorkshopJobState.Downloading);
            if ((!initialStatus.IsInstalled || initialStatus.NeedsUpdate) && !_api.Download(workshopFileId))
            {
                job.Complete(
                    LanConnectWorkshopJobState.Failed,
                    "workshop_download_failed",
                    "Steam rejected the Workshop download request.");
                return;
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_clock.UtcNow - job.StartedAt >= _timeout)
                {
                    job.Complete(
                        LanConnectWorkshopJobState.TimedOut,
                        "workshop_timeout",
                        "Workshop download did not finish within five minutes.");
                    return;
                }

                LanConnectWorkshopItemStatus status = _api.GetItemStatus(workshopFileId);
                job.UpdateProgress(status.BytesDownloaded, status.BytesTotal);
                if (status.ErrorCode != null)
                {
                    job.Complete(
                        LanConnectWorkshopJobState.Failed,
                        status.ErrorCode,
                        status.ErrorMessage ?? "Steam reported a Workshop download failure.");
                    return;
                }
                if (status.IsInstalled && !status.NeedsUpdate && !string.IsNullOrWhiteSpace(status.InstallFolder))
                {
                    job.Transition(LanConnectWorkshopJobState.WaitingInstall);
                    LanConnectWorkshopManifestVerification verification =
                        await _manifestVerifier.VerifyInstalledManifestAsync(
                            status.InstallFolder,
                            descriptor,
                            cancellationToken);
                    if (!verification.Success)
                    {
                        job.Complete(
                            LanConnectWorkshopJobState.Failed,
                            verification.ErrorCode,
                            verification.Message,
                            verification.RequiresRestart,
                            verification.RequiresRepreflight);
                        return;
                    }
                    job.Complete(
                        LanConnectWorkshopJobState.Installed,
                        requiresRestart: verification.RequiresRestart,
                        requiresRepreflight: verification.RequiresRepreflight);
                    return;
                }

                await job.WaitForItemChangeOrDelayAsync(
                    _clock,
                    _pollInterval,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bool cleanupFailed = false;
            if (!job.WasSubscribedInitially &&
                ulong.TryParse(job.Descriptor.WorkshopFileId, out ulong workshopFileId))
            {
                try
                {
                    cleanupFailed = !await _api.UnsubscribeAsync(workshopFileId, CancellationToken.None);
                }
                catch
                {
                    cleanupFailed = true;
                }
            }
            job.Complete(
                LanConnectWorkshopJobState.Canceled,
                cleanupFailed ? "workshop_cancel_cleanup_failed" : "workshop_canceled",
                cleanupFailed
                    ? "Workshop synchronization was canceled, but Steam subscription cleanup needs manual verification."
                    : "Workshop synchronization was canceled.");
        }
        catch (Exception ex)
        {
            job.Complete(LanConnectWorkshopJobState.Failed, "workshop_failed", ex.Message);
        }
    }

    private void OnItemChanged(ulong workshopFileId)
    {
        lock (_sync)
        {
            foreach (LanConnectWorkshopJob job in _jobs.Values)
            {
                if (!job.Snapshot().IsTerminal &&
                    ulong.TryParse(job.Descriptor.WorkshopFileId, out ulong jobFileId) &&
                    jobFileId == workshopFileId)
                {
                    job.SignalItemChanged();
                }
            }
        }
    }

    private LanConnectWorkshopJob GetJob(Guid jobId) =>
        _jobs.TryGetValue(jobId, out LanConnectWorkshopJob? job)
            ? job
            : throw new KeyNotFoundException($"Unknown Workshop job: {jobId}");

    private static bool TryCanonicalizeDescriptor(
        LobbyModDescriptor descriptor,
        out LobbyModDescriptor canonical)
    {
        try
        {
            canonical = LanConnectModInventoryValidator.Canonicalize([descriptor]).Single();
            return true;
        }
        catch (LanConnectModInventoryException)
        {
            canonical = new LobbyModDescriptor();
            return false;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
