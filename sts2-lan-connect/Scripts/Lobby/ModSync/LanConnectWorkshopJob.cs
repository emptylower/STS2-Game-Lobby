namespace Sts2LanConnect.Scripts;

internal enum LanConnectWorkshopJobState
{
    Pending,
    Validating,
    Subscribing,
    Downloading,
    WaitingInstall,
    Installed,
    Failed,
    TimedOut,
    Canceled
}

internal sealed record LanConnectWorkshopJobSnapshot
{
    public Guid JobId { get; init; }
    public int Attempt { get; init; }
    public LanConnectWorkshopJobState State { get; init; }
    public IReadOnlyList<LanConnectWorkshopJobState> StateHistory { get; init; } = [];
    public LobbyModDescriptor Descriptor { get; init; } = new();
    public LanConnectWorkshopMetadata Metadata { get; init; } = new(string.Empty, 0, string.Empty, string.Empty);
    public ulong BytesDownloaded { get; init; }
    public ulong BytesTotal { get; init; }
    public string? FailureCode { get; init; }
    public string? Message { get; init; }
    public bool RequiresRestart { get; init; }
    public bool RequiresRepreflight { get; init; }

    public bool IsTerminal => State is
        LanConnectWorkshopJobState.Installed or
        LanConnectWorkshopJobState.Failed or
        LanConnectWorkshopJobState.TimedOut or
        LanConnectWorkshopJobState.Canceled;
}

internal sealed class LanConnectWorkshopJob
{
    private readonly object _sync = new();
    private readonly List<LanConnectWorkshopJobState> _history = [LanConnectWorkshopJobState.Pending];
    private TaskCompletionSource _itemChanged = NewSignal();
    private bool _cancelRequested;
    private ulong _bytesDownloaded;
    private ulong _bytesTotal;
    private string? _failureCode;
    private string? _message;
    private bool _requiresRestart;
    private bool _requiresRepreflight;

    public LanConnectWorkshopJob(
        LobbyModDescriptor descriptor,
        LanConnectWorkshopMetadata metadata,
        int attempt,
        DateTimeOffset startedAt)
    {
        Descriptor = CloneDescriptor(descriptor);
        Metadata = metadata;
        Attempt = attempt;
        StartedAt = startedAt;
    }

    public Guid JobId { get; } = Guid.NewGuid();
    public LobbyModDescriptor Descriptor { get; }
    public LanConnectWorkshopMetadata Metadata { get; }
    public int Attempt { get; }
    public DateTimeOffset StartedAt { get; }
    public CancellationTokenSource Cancellation { get; } = new();
    public bool WasSubscribedInitially { get; set; }

    public LanConnectWorkshopJobState State
    {
        get
        {
            lock (_sync)
            {
                return _history[^1];
            }
        }
    }

    public bool Transition(LanConnectWorkshopJobState state)
    {
        lock (_sync)
        {
            if (SnapshotUnsafe().IsTerminal || _history[^1] == state)
            {
                return false;
            }
            _history.Add(state);
            return true;
        }
    }

    public void UpdateProgress(ulong downloaded, ulong total)
    {
        lock (_sync)
        {
            _bytesDownloaded = downloaded;
            _bytesTotal = total;
        }
    }

    public void Complete(
        LanConnectWorkshopJobState state,
        string? failureCode = null,
        string? message = null,
        bool requiresRestart = false,
        bool requiresRepreflight = false)
    {
        lock (_sync)
        {
            if (SnapshotUnsafe().IsTerminal)
            {
                return;
            }
            _failureCode = failureCode;
            _message = message;
            _requiresRestart = requiresRestart;
            _requiresRepreflight = requiresRepreflight;
            _history.Add(state);
        }
    }

    public bool Cancel()
    {
        lock (_sync)
        {
            if (SnapshotUnsafe().IsTerminal)
            {
                return _history[^1] == LanConnectWorkshopJobState.Canceled;
            }
            if (!_cancelRequested)
            {
                _cancelRequested = true;
                Cancellation.Cancel();
            }
            return true;
        }
    }

    public void SignalItemChanged()
    {
        TaskCompletionSource signal;
        lock (_sync)
        {
            signal = _itemChanged;
        }
        signal.TrySetResult();
    }

    public async Task WaitForItemChangeOrDelayAsync(
        ILanConnectWorkshopClock clock,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource signal;
        lock (_sync)
        {
            signal = _itemChanged;
        }
        Task delayTask = clock.DelayAsync(delay, cancellationToken);
        await Task.WhenAny(signal.Task, delayTask);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (ReferenceEquals(signal, _itemChanged) && signal.Task.IsCompleted)
            {
                _itemChanged = NewSignal();
            }
        }
    }

    public LanConnectWorkshopJobSnapshot Snapshot()
    {
        lock (_sync)
        {
            return SnapshotUnsafe();
        }
    }

    private LanConnectWorkshopJobSnapshot SnapshotUnsafe() => new()
    {
        JobId = JobId,
        Attempt = Attempt,
        State = _history[^1],
        StateHistory = _history.ToArray(),
        Descriptor = CloneDescriptor(Descriptor),
        Metadata = Metadata,
        BytesDownloaded = _bytesDownloaded,
        BytesTotal = _bytesTotal,
        FailureCode = _failureCode,
        Message = _message,
        RequiresRestart = _requiresRestart,
        RequiresRepreflight = _requiresRepreflight
    };

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static LobbyModDescriptor CloneDescriptor(LobbyModDescriptor descriptor) => new()
    {
        Id = descriptor.Id,
        Version = descriptor.Version,
        Role = descriptor.Role,
        Source = descriptor.Source,
        WorkshopFileId = descriptor.WorkshopFileId,
        Dependencies = descriptor.Dependencies.ToList()
    };
}
