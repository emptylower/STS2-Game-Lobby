namespace Sts2LanConnect.Scripts;

internal enum LanConnectSessionProtocolRole
{
    Host,
    Client
}

internal enum LanConnectSessionProtocolPhase
{
    Empty,
    Tentative,
    Frozen,
    Closing
}

internal sealed record LanConnectSessionProtocolSnapshot(
    LanConnectSessionProtocolPhase Phase,
    LanConnectSessionProtocolRole? Role,
    string? OwnerId,
    long Generation,
    LanConnectProtocolSelection? Selection)
{
    public static LanConnectSessionProtocolSnapshot Empty { get; } =
        new(LanConnectSessionProtocolPhase.Empty, null, null, 0, null);

    public bool IsActive => Phase is LanConnectSessionProtocolPhase.Tentative or LanConnectSessionProtocolPhase.Frozen;
}

internal sealed class LanConnectSessionProtocolState
{
    private readonly object _sync = new();
    private LanConnectSessionProtocolSnapshot _current = LanConnectSessionProtocolSnapshot.Empty;
    private long _nextGeneration;

    public static LanConnectSessionProtocolState Shared { get; } = new();

    public LanConnectSessionProtocolSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public LanConnectSessionProtocolLease FreezeHost(
        LanConnectProtocolSelection selection,
        string ownerId) =>
        Freeze(selection, ownerId, LanConnectSessionProtocolRole.Host, LanConnectSessionProtocolPhase.Frozen);

    public LanConnectSessionProtocolLease FreezeClient(
        LanConnectProtocolSelection selection,
        string ownerId) =>
        Freeze(selection, ownerId, LanConnectSessionProtocolRole.Client, LanConnectSessionProtocolPhase.Tentative);

    public void MarkClosing(string ownerId)
    {
        lock (_sync)
        {
            EnsureOwner(ownerId);
            if (_current.Phase == LanConnectSessionProtocolPhase.Closing)
            {
                return;
            }

            if (!_current.IsActive)
            {
                throw Conflict("Only an active protocol session can enter closing state.");
            }

            _current = _current with { Phase = LanConnectSessionProtocolPhase.Closing };
        }
    }

    public bool TryReset(string ownerId)
    {
        lock (_sync)
        {
            if (!string.Equals(_current.OwnerId, NormalizeOwner(ownerId), StringComparison.Ordinal))
            {
                return false;
            }

            _current = LanConnectSessionProtocolSnapshot.Empty with { Generation = _current.Generation };
            return true;
        }
    }

    internal void Attach(long generation, string ownerId, LanConnectProtocolSelection selection)
    {
        lock (_sync)
        {
            EnsureLease(generation, ownerId, selection);
            if (_current.Phase == LanConnectSessionProtocolPhase.Frozen)
            {
                return;
            }

            if (_current.Phase != LanConnectSessionProtocolPhase.Tentative)
            {
                throw Conflict("Only a tentative client selection can attach to transport.");
            }

            _current = _current with { Phase = LanConnectSessionProtocolPhase.Frozen };
        }
    }

    internal void Release(long generation, string ownerId, LanConnectProtocolSelection selection)
    {
        lock (_sync)
        {
            if (_current.Generation != generation
                || !string.Equals(_current.OwnerId, NormalizeOwner(ownerId), StringComparison.Ordinal)
                || _current.Selection != selection)
            {
                return;
            }

            _current = LanConnectSessionProtocolSnapshot.Empty with { Generation = generation };
        }
    }

    private LanConnectSessionProtocolLease Freeze(
        LanConnectProtocolSelection selection,
        string ownerId,
        LanConnectSessionProtocolRole role,
        LanConnectSessionProtocolPhase phase)
    {
        ArgumentNullException.ThrowIfNull(selection);
        string normalizedOwner = NormalizeOwner(ownerId);
        lock (_sync)
        {
            if (_current.Phase == LanConnectSessionProtocolPhase.Empty)
            {
                long generation = checked(++_nextGeneration);
                _current = new LanConnectSessionProtocolSnapshot(
                    phase,
                    role,
                    normalizedOwner,
                    generation,
                    selection);
                return new LanConnectSessionProtocolLease(this, generation, normalizedOwner, selection, ownsState: true);
            }

            if (_current.Role == role
                && string.Equals(_current.OwnerId, normalizedOwner, StringComparison.Ordinal)
                && _current.Selection == selection
                && _current.Phase != LanConnectSessionProtocolPhase.Closing)
            {
                return new LanConnectSessionProtocolLease(
                    this,
                    _current.Generation,
                    normalizedOwner,
                    selection,
                    ownsState: false);
            }

            throw Conflict(
                $"Protocol selection is already owned by {_current.Role}:{_current.OwnerId} " +
                $"in phase {_current.Phase}.");
        }
    }

    private void EnsureLease(long generation, string ownerId, LanConnectProtocolSelection selection)
    {
        if (_current.Generation != generation
            || !string.Equals(_current.OwnerId, NormalizeOwner(ownerId), StringComparison.Ordinal)
            || _current.Selection != selection)
        {
            throw Conflict("The protocol lease no longer owns the current selection.");
        }
    }

    private void EnsureOwner(string ownerId)
    {
        if (!string.Equals(_current.OwnerId, NormalizeOwner(ownerId), StringComparison.Ordinal))
        {
            throw Conflict("Only the current protocol owner can mutate session state.");
        }
    }

    private static string NormalizeOwner(string ownerId) =>
        !string.IsNullOrWhiteSpace(ownerId)
            ? ownerId.Trim()
            : throw new ArgumentException("Protocol lease owner is required.", nameof(ownerId));

    private static LanConnectProtocolException Conflict(string detail) =>
        LanConnectProtocolFailureMapper.FromLocalException("protocol_selection_conflict", detail);
}

internal sealed class LanConnectSessionProtocolLease : IDisposable
{
    private readonly LanConnectSessionProtocolState _state;
    private readonly long _generation;
    private readonly string _ownerId;
    private readonly LanConnectProtocolSelection _selection;
    private readonly bool _ownsState;
    private int _disposed;

    internal LanConnectSessionProtocolLease(
        LanConnectSessionProtocolState state,
        long generation,
        string ownerId,
        LanConnectProtocolSelection selection,
        bool ownsState)
    {
        _state = state;
        _generation = generation;
        _ownerId = ownerId;
        _selection = selection;
        _ownsState = ownsState;
    }

    public LanConnectProtocolSelection Selection => _selection;

    public string OwnerId => _ownerId;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _state.Attach(_generation, _ownerId, _selection);
    }

    public void MarkClosing()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _state.MarkClosing(_ownerId);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsState)
        {
            _state.Release(_generation, _ownerId, _selection);
        }
    }
}
