namespace Sts2LanConnect.Scripts;

internal enum LanConnectRosterSnapshotUse
{
    Bootstrap,
    CurrentState,
    StateTransition,
    MembershipMutation
}

internal sealed class LanConnectRosterAuthorityState
{
    private readonly object _sync = new();
    private readonly ulong _hostPeerId;
    private LanConnectRosterSnapshot? _current;
    private byte[]? _currentCanonicalBytes;
    private readonly HashSet<ulong> _pendingDepartures = [];
    private bool _hostDepartureDirty;

    internal LanConnectRosterAuthorityState(ulong hostPeerId)
    {
        _hostPeerId = hostPeerId;
    }

    internal LanConnectRosterSnapshot? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    internal LanConnectRosterSnapshot CommitHostSnapshot(
        IReadOnlyList<LanConnectRosterPlayerCarrier> players,
        bool includeLobbyDepartures = true)
    {
        lock (_sync)
        {
            uint candidateRevision = _current == null ? 1u : _current.RosterRevision;
            LanConnectRosterSnapshot candidate = new(_hostPeerId, candidateRevision, players);
            byte[] candidateBytes = LanConnectRosterCodec.Encode(candidate);
            if (_currentCanonicalBytes != null
                && (!includeLobbyDepartures || !_hostDepartureDirty)
                && SameSnapshotIgnoringRevision(candidateBytes, _currentCanonicalBytes))
            {
                return _current!;
            }

            if (_current != null)
            {
                candidate = candidate with { RosterRevision = checked(_current.RosterRevision + 1) };
                candidateBytes = LanConnectRosterCodec.Encode(candidate);
            }

            Store(candidate, candidateBytes);
            _hostDepartureDirty = false;
            return _current!;
        }
    }

    internal bool RecordHostLobbyDeparture(ulong departedPeerId)
    {
        lock (_sync)
        {
            ValidateDepartedPeer(departedPeerId);
            if (_current?.Players.Any(player => player.PlayerId == departedPeerId) != true)
            {
                return false;
            }

            bool changed = !_hostDepartureDirty;
            _hostDepartureDirty = true;
            return changed;
        }
    }

    internal bool RecordLobbyDeparture(ulong transportSenderPeerId, ulong departedPeerId)
    {
        if (transportSenderPeerId != _hostPeerId)
        {
            throw Invalid("Lobby departure was not sent by the current transport host.");
        }

        lock (_sync)
        {
            ValidateDepartedPeer(departedPeerId);
            return _current?.Players.Any(player => player.PlayerId == departedPeerId) == true
                   && _pendingDepartures.Add(departedPeerId);
        }
    }

    internal void ClearLobbyDepartureEvidence()
    {
        lock (_sync)
        {
            _pendingDepartures.Clear();
            _hostDepartureDirty = false;
        }
    }

    internal void Accept(
        ulong transportSenderPeerId,
        LanConnectRosterSnapshot snapshot,
        LanConnectRosterSnapshotUse use,
        IReadOnlyCollection<ulong>? authoritativeMembership = null,
        ulong? connectedPeerId = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        LanConnectRosterCodec.ValidateAuthority(snapshot, transportSenderPeerId, _hostPeerId);
        byte[] candidateBytes = LanConnectRosterCodec.Encode(snapshot);
        snapshot = LanConnectRosterCodec.Decode(candidateBytes);

        lock (_sync)
        {
            ValidateMembership(snapshot, authoritativeMembership, connectedPeerId, use);
            if (_current == null)
            {
                if (use is LanConnectRosterSnapshotUse.StateTransition
                    or LanConnectRosterSnapshotUse.MembershipMutation)
                {
                    throw Invalid("Transition and mutation snapshots cannot initialize roster authority state.");
                }

                Store(snapshot, candidateBytes);
                return;
            }

            if (snapshot.RosterRevision < _current.RosterRevision)
            {
                throw Invalid("Roster revision moved backwards.");
            }

            if (use == LanConnectRosterSnapshotUse.CurrentState)
            {
                if (snapshot.RosterRevision == _current.RosterRevision
                    && _currentCanonicalBytes!.AsSpan().SequenceEqual(candidateBytes))
                {
                    return;
                }
                throw Invalid("Current-state snapshots must repeat the accepted revision byte-for-byte.");
            }

            if (use == LanConnectRosterSnapshotUse.Bootstrap)
            {
                throw Invalid("A second bootstrap snapshot is not allowed.");
            }

            if (use == LanConnectRosterSnapshotUse.StateTransition)
            {
                HashSet<ulong> removals = ValidateObservedMembershipChange(snapshot, connectedPeerId: null);
                if (snapshot.RosterRevision == _current.RosterRevision)
                {
                    if (!_currentCanonicalBytes!.AsSpan().SequenceEqual(candidateBytes))
                    {
                        throw Invalid("An unchanged transition revision must repeat the accepted snapshot byte-for-byte.");
                    }

                    return;
                }

                ValidateNextRevision(snapshot);
                Store(snapshot, candidateBytes);
                _pendingDepartures.ExceptWith(removals);
                return;
            }

            HashSet<ulong> joinedRemovals = connectedPeerId.HasValue
                ? ValidateObservedMembershipChange(snapshot, connectedPeerId)
                : [];
            ValidateNextRevision(snapshot);
            Store(snapshot, candidateBytes);
            _pendingDepartures.ExceptWith(joinedRemovals);
            _pendingDepartures.IntersectWith(snapshot.Players.Select(static player => player.PlayerId));
        }
    }

    private HashSet<ulong> ValidateObservedMembershipChange(
        LanConnectRosterSnapshot candidate,
        ulong? connectedPeerId)
    {
        Dictionary<ulong, byte> currentSlots = _current!.Players.ToDictionary(
            static player => player.PlayerId,
            static player => player.RealSlotId);
        HashSet<ulong> candidateIds = candidate.Players.Select(static player => player.PlayerId).ToHashSet();
        HashSet<ulong> removals = currentSlots.Keys.Where(id => !candidateIds.Contains(id)).ToHashSet();
        if (removals.Any(id => !_pendingDepartures.Contains(id)))
        {
            throw Invalid("Roster snapshot removed a player without a verified lobby departure.");
        }

        bool reconnecting = connectedPeerId.HasValue
                            && currentSlots.ContainsKey(connectedPeerId.Value)
                            && _pendingDepartures.Contains(connectedPeerId.Value);
        if (connectedPeerId.HasValue
            && (!candidateIds.Contains(connectedPeerId.Value)
                || (currentSlots.ContainsKey(connectedPeerId.Value) && !reconnecting)))
        {
            throw Invalid("PlayerJoined does not name a new or verified reconnecting player.");
        }
        if (candidate.Players.Any(player =>
                !currentSlots.ContainsKey(player.PlayerId) && player.PlayerId != connectedPeerId))
        {
            throw Invalid("Roster snapshot added a player without its verified PlayerJoined message.");
        }
        if (candidate.Players.Any(player =>
                currentSlots.TryGetValue(player.PlayerId, out byte slot)
                && slot != player.RealSlotId
                && !(reconnecting && player.PlayerId == connectedPeerId)))
        {
            throw Invalid("Roster snapshot changed a surviving player's real slot assignment.");
        }
        if (reconnecting)
        {
            removals.Add(connectedPeerId!.Value);
        }

        return removals;
    }

    private void ValidateNextRevision(LanConnectRosterSnapshot snapshot)
    {
        if (snapshot.RosterRevision != checked(_current!.RosterRevision + 1))
        {
            throw Invalid("Roster mutation or transition revision must increase by exactly one.");
        }
    }

    private void ValidateDepartedPeer(ulong departedPeerId)
    {
        if (departedPeerId == _hostPeerId)
        {
            throw Invalid("The current host cannot be removed by a lobby departure message.");
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            _current = null;
            _currentCanonicalBytes = null;
            _pendingDepartures.Clear();
            _hostDepartureDirty = false;
        }
    }

    private static void ValidateMembership(
        LanConnectRosterSnapshot snapshot,
        IReadOnlyCollection<ulong>? authoritativeMembership,
        ulong? connectedPeerId,
        LanConnectRosterSnapshotUse use)
    {
        HashSet<ulong> snapshotIds = snapshot.Players.Select(static player => player.PlayerId).ToHashSet();
        if (authoritativeMembership != null
            && !snapshotIds.SetEquals(authoritativeMembership))
        {
            throw Invalid("Roster snapshot differs from the authoritative connected-peer set.");
        }

        if (use == LanConnectRosterSnapshotUse.MembershipMutation
            && connectedPeerId.HasValue
            && !snapshotIds.Contains(connectedPeerId.Value))
        {
            throw Invalid("PlayerJoined snapshot does not contain the connected peer.");
        }
    }

    private static bool SameSnapshotIgnoringRevision(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        const int revisionOffset = 10;
        const int revisionBytes = 4;
        return left.Length == right.Length
            && left[..revisionOffset].SequenceEqual(right[..revisionOffset])
            && left[(revisionOffset + revisionBytes)..].SequenceEqual(right[(revisionOffset + revisionBytes)..]);
    }

    private void Store(LanConnectRosterSnapshot snapshot, byte[] canonicalBytes)
    {
        _current = LanConnectRosterCodec.Decode(canonicalBytes);
        _currentCanonicalBytes = canonicalBytes;
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
