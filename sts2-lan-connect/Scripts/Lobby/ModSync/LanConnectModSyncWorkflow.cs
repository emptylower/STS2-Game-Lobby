namespace Sts2LanConnect.Scripts;

internal enum LanConnectModSyncWorkflowStatus
{
    Ready,
    RequiresDisableConfirmation,
    RestartRequired,
    Failed,
    Canceled,
    NoChanges
}

internal sealed record LanConnectModSyncWorkflowResult
{
    public LanConnectModSyncWorkflowStatus Status { get; init; }
    public LanConnectModSyncViewState State { get; init; } = LanConnectModSyncViewState.Checking();
    public string? Message { get; init; }
}

internal sealed class LanConnectModSyncWorkflow : IDisposable
{
    private readonly ILanConnectModSyncProvider _provider;
    private readonly LanConnectModDisableApplier _disableApplier;
    private readonly LanConnectPendingModSyncJoinStore _pendingStore;
    private readonly LobbyModPreflightResponse _response;
    private readonly Func<CancellationToken, Task> _pollDelay;
    private readonly Dictionary<string, LanConnectWorkshopMetadata> _metadata = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _activeJobs = new(StringComparer.Ordinal);
    private IReadOnlyList<LanConnectWorkshopJobSnapshot> _lastSnapshots = [];
    private LobbyRoomSummary? _lastRoom;
    private string? _lastServerBaseUrl;
    private string? _lastDesiredSavePlayerNetId;
    private IReadOnlyCollection<string> _lastSelectedExtraIds = [];
    private bool _lastDisableConfirmed;
    private int _cancelRequested;
    private bool _disposed;

    public LanConnectModSyncWorkflow(
        ILanConnectModSyncProvider provider,
        LanConnectModDisableApplier disableApplier,
        LanConnectPendingModSyncJoinStore pendingStore,
        LobbyModPreflightResponse response,
        Func<CancellationToken, Task>? pollDelay = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _disableApplier = disableApplier ?? throw new ArgumentNullException(nameof(disableApplier));
        _pendingStore = pendingStore ?? throw new ArgumentNullException(nameof(pendingStore));
        _response = response ?? throw new ArgumentNullException(nameof(response));
        _pollDelay = pollDelay ?? (token => Task.Delay(100, token));
    }

    internal event Action<LanConnectModSyncViewState>? ProgressChanged;

    public async Task<LanConnectModSyncViewState> PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        LanConnectModSyncViewState state = LanConnectModSyncViewState.FromPreflight(
            _response,
            _provider.Availability);
        if (!_provider.Availability.IsAvailable || _response.MissingWorkshopMods.Count == 0)
        {
            return state;
        }

        HashSet<string> failed = new(StringComparer.Ordinal);
        foreach (LobbyModDescriptor descriptor in _response.MissingWorkshopMods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LanConnectWorkshopMetadataQueryResult result = await _provider.QueryMetadataAsync(
                descriptor,
                cancellationToken);
            if (result.Success && result.Metadata != null)
            {
                _metadata[descriptor.Id] = result.Metadata;
            }
            else
            {
                failed.Add(descriptor.Id);
            }
        }

        IReadOnlyList<LanConnectModSyncRowState> rows = state.Rows.Select(row =>
            _metadata.TryGetValue(row.Descriptor.Id, out LanConnectWorkshopMetadata? metadata)
                ? row with { Metadata = metadata }
                : row).ToArray();
        if (failed.Count > 0)
        {
            return state with
            {
                Kind = LanConnectModSyncViewKind.ManualAction,
                Title = LanConnectModSyncLocalizer.Title(LanConnectModSyncViewKind.ManualAction),
                Message = "Steam 元数据验证失败，相关项目只能手动处理。",
                Rows = rows,
                PrimaryAction = _response.ExtraGameplayMods.Count > 0
                    ? LanConnectModSyncAction.ApplyChanges
                    : LanConnectModSyncAction.Cancel
            };
        }
        return state with { Rows = rows };
    }

    public async Task<LanConnectModSyncWorkflowResult> ApplyAsync(
        LobbyRoomSummary room,
        string serverBaseUrl,
        string? desiredSavePlayerNetId,
        IReadOnlyCollection<string> selectedExtraIds,
        bool disableConfirmed,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(selectedExtraIds);
        if (selectedExtraIds.Count > 0 && !disableConfirmed)
        {
            return new LanConnectModSyncWorkflowResult
            {
                Status = LanConnectModSyncWorkflowStatus.RequiresDisableConfirmation,
                State = LanConnectModSyncViewState.FromPreflight(_response, _provider.Availability)
            };
        }

        _lastRoom = room;
        _lastServerBaseUrl = serverBaseUrl;
        _lastDesiredSavePlayerNetId = desiredSavePlayerNetId;
        _lastSelectedExtraIds = selectedExtraIds.ToArray();
        _lastDisableConfirmed = disableConfirmed;
        Volatile.Write(ref _cancelRequested, 0);

        try
        {
            if (_response.MissingWorkshopMods.Count > 0 && _metadata.Count == 0)
            {
                await PrepareAsync(cancellationToken);
            }
            if (_response.MissingWorkshopMods.Any(descriptor => !_metadata.ContainsKey(descriptor.Id)))
            {
                return Failed("workshop_metadata_unavailable", "Steam 元数据未通过验证，请手动处理缺失项。");
            }

            _activeJobs.Clear();
            List<LanConnectWorkshopJobSnapshot> snapshots = [];
            foreach (LobbyModDescriptor descriptor in _response.MissingWorkshopMods)
            {
                LanConnectWorkshopJobSnapshot submitted = await _provider.SubmitAsync(
                    descriptor,
                    _metadata[descriptor.Id],
                    cancellationToken);
                _activeJobs[descriptor.Id] = submitted.JobId;
                snapshots.Add(submitted);
            }

            while (snapshots.Any(snapshot => !snapshot.IsTerminal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshots = _activeJobs.Values.Select(_provider.Poll).ToList();
                ProgressChanged?.Invoke(LanConnectModSyncViewState.Progress(snapshots));
                if (snapshots.Any(snapshot => !snapshot.IsTerminal))
                {
                    await _pollDelay(cancellationToken);
                }
            }
            if (snapshots.Any(snapshot => snapshot.State != LanConnectWorkshopJobState.Installed))
            {
                if (Volatile.Read(ref _cancelRequested) != 0 ||
                    snapshots.Any(snapshot => snapshot.State == LanConnectWorkshopJobState.Canceled))
                {
                    return Canceled();
                }
                LanConnectWorkshopJobSnapshot failed = snapshots.First(snapshot =>
                    snapshot.State != LanConnectWorkshopJobState.Installed);
                _lastSnapshots = snapshots;
                return Failed(
                    failed.FailureCode ?? "workshop_sync_failed",
                    failed.Message ?? "Steam Workshop 同步失败，可重试或手动处理。",
                    snapshots);
            }

            return CompleteChanges(
                snapshots,
                room,
                serverBaseUrl,
                desiredSavePlayerNetId,
                selectedExtraIds,
                disableConfirmed);
        }
        catch (OperationCanceledException)
        {
            Cancel();
            return new LanConnectModSyncWorkflowResult
            {
                Status = LanConnectModSyncWorkflowStatus.Canceled,
                State = LanConnectModSyncViewState.FromPreflight(_response, _provider.Availability)
            };
        }
    }

    public async Task<LanConnectModSyncWorkflowResult> RetryAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_lastSnapshots.Count == 0 ||
            _lastRoom == null ||
            string.IsNullOrWhiteSpace(_lastServerBaseUrl))
        {
            throw new InvalidOperationException("No failed MOD synchronization is available to retry.");
        }

        try
        {
            Volatile.Write(ref _cancelRequested, 0);
            List<LanConnectWorkshopJobSnapshot> snapshots = [];
            _activeJobs.Clear();
            foreach (LanConnectWorkshopJobSnapshot previous in _lastSnapshots)
            {
                LanConnectWorkshopJobSnapshot current = previous.State == LanConnectWorkshopJobState.Installed
                    ? previous
                    : await _provider.RetryAsync(previous.JobId, cancellationToken);
                _activeJobs[current.Descriptor.Id] = current.JobId;
                snapshots.Add(current);
            }
            while (snapshots.Any(snapshot => !snapshot.IsTerminal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshots = snapshots.Select(snapshot => snapshot.IsTerminal
                    ? snapshot
                    : _provider.Poll(snapshot.JobId)).ToList();
                ProgressChanged?.Invoke(LanConnectModSyncViewState.Progress(snapshots));
                if (snapshots.Any(snapshot => !snapshot.IsTerminal))
                {
                    await _pollDelay(cancellationToken);
                }
            }
            if (snapshots.Any(snapshot => snapshot.State != LanConnectWorkshopJobState.Installed))
            {
                if (Volatile.Read(ref _cancelRequested) != 0 ||
                    snapshots.Any(snapshot => snapshot.State == LanConnectWorkshopJobState.Canceled))
                {
                    return Canceled();
                }
                _lastSnapshots = snapshots;
                LanConnectWorkshopJobSnapshot failed = snapshots.First(snapshot =>
                    snapshot.State != LanConnectWorkshopJobState.Installed);
                return Failed(
                    failed.FailureCode ?? "workshop_sync_failed",
                    failed.Message ?? "Steam Workshop 同步重试失败。",
                    snapshots);
            }
            _lastSnapshots = [];
            return CompleteChanges(
                snapshots,
                _lastRoom,
                _lastServerBaseUrl,
                _lastDesiredSavePlayerNetId,
                _lastSelectedExtraIds,
                _lastDisableConfirmed);
        }
        catch (OperationCanceledException)
        {
            Cancel();
            return new LanConnectModSyncWorkflowResult
            {
                Status = LanConnectModSyncWorkflowStatus.Canceled,
                State = LanConnectModSyncViewState.FromPreflight(_response, _provider.Availability)
            };
        }
    }

    public void Cancel()
    {
        Volatile.Write(ref _cancelRequested, 1);
        foreach (Guid jobId in _activeJobs.Values)
        {
            try
            {
                _provider.Cancel(jobId);
            }
            catch (KeyNotFoundException)
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Cancel();
        _provider.Dispose();
    }

    private HashSet<string> RequiredDependencyIds() => _response.MissingWorkshopMods
        .Concat(_response.MissingManualMods)
        .SelectMany(descriptor => descriptor.Dependencies.Append(
            descriptor.Role == LanConnectModRoles.Dependency ? descriptor.Id : string.Empty))
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .ToHashSet(StringComparer.Ordinal);

    private LanConnectModSyncWorkflowResult CompleteChanges(
        IReadOnlyList<LanConnectWorkshopJobSnapshot> snapshots,
        LobbyRoomSummary room,
        string serverBaseUrl,
        string? desiredSavePlayerNetId,
        IReadOnlyCollection<string> selectedExtraIds,
        bool disableConfirmed)
    {
        LanConnectModDisableResult disableResult = _disableApplier.ApplyDisableSelection(
            _response.ExtraGameplayMods,
            selectedExtraIds,
            RequiredDependencyIds(),
            confirmed: disableConfirmed);
        if (disableResult.Status is LanConnectModDisableStatus.Rejected or LanConnectModDisableStatus.Failed)
        {
            return Failed("mod_disable_failed", disableResult.Message ?? "禁用所选 MOD 失败。");
        }

        bool changed = snapshots.Count > 0 || disableResult.Status == LanConnectModDisableStatus.Applied;
        if (!changed)
        {
            return new LanConnectModSyncWorkflowResult
            {
                Status = LanConnectModSyncWorkflowStatus.NoChanges,
                State = LanConnectModSyncViewState.FromPreflight(_response, _provider.Availability)
            };
        }

        _pendingStore.Save(serverBaseUrl, room.RoomId, room.RoomName, desiredSavePlayerNetId);
        _lastSnapshots = [];
        return new LanConnectModSyncWorkflowResult
        {
            Status = LanConnectModSyncWorkflowStatus.RestartRequired,
            State = LanConnectModSyncViewState.RestartRequired()
        };
    }

    private static LanConnectModSyncWorkflowResult Failed(
        string code,
        string message,
        IReadOnlyList<LanConnectWorkshopJobSnapshot>? snapshots = null) => new()
        {
            Status = LanConnectModSyncWorkflowStatus.Failed,
            Message = $"{message} ({code})",
            State = snapshots == null
                ? LanConnectModSyncViewState.Checking() with
                {
                    Kind = LanConnectModSyncViewKind.ManualAction,
                    Title = LanConnectModSyncLocalizer.Title(LanConnectModSyncViewKind.ManualAction),
                    Message = message,
                    PrimaryAction = LanConnectModSyncAction.Cancel
                }
                : LanConnectModSyncViewState.Progress(snapshots) with
                {
                    Message = message,
                    PrimaryAction = LanConnectModSyncAction.Retry,
                    SecondaryActions = [LanConnectModSyncAction.Cancel]
                }
        };

    private LanConnectModSyncWorkflowResult Canceled() => new()
    {
        Status = LanConnectModSyncWorkflowStatus.Canceled,
        State = LanConnectModSyncViewState.FromPreflight(_response, _provider.Availability)
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
