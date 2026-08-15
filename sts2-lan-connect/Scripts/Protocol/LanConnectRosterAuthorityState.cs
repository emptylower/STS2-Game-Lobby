namespace Sts2LanConnect.Scripts;

internal enum LanConnectRosterSnapshotUse
{
    Bootstrap,
    CurrentState,
    MembershipMutation
}

internal sealed class LanConnectRosterAuthorityState
{
    private readonly object _sync = new();
    private readonly ulong _hostPeerId;
    private LanConnectRosterSnapshot? _current;
    private byte[]? _currentCanonicalBytes;

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
        IReadOnlyList<LanConnectRosterPlayerCarrier> players)
    {
        lock (_sync)
        {
            uint candidateRevision = _current == null ? 1u : _current.RosterRevision;
            LanConnectRosterSnapshot candidate = new(_hostPeerId, candidateRevision, players);
            byte[] candidateBytes = LanConnectRosterCodec.Encode(candidate);
            if (_currentCanonicalBytes != null
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
            return _current!;
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
        ValidateMembership(snapshot, authoritativeMembership, connectedPeerId, use);

        lock (_sync)
        {
            if (_current == null)
            {
                if (use == LanConnectRosterSnapshotUse.MembershipMutation)
                {
                    throw Invalid("Mutation snapshots cannot initialize roster authority state.");
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
                if (snapshot.RosterRevision != _current.RosterRevision
                    || !_currentCanonicalBytes!.AsSpan().SequenceEqual(candidateBytes))
                {
                    throw Invalid("Current-state snapshots must repeat the accepted revision byte-for-byte.");
                }

                return;
            }

            if (use == LanConnectRosterSnapshotUse.Bootstrap)
            {
                throw Invalid("A second bootstrap snapshot is not allowed.");
            }

            if (snapshot.RosterRevision != checked(_current.RosterRevision + 1))
            {
                throw Invalid("Mutation snapshot revision must increase by exactly one.");
            }

            Store(snapshot, candidateBytes);
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            _current = null;
            _currentCanonicalBytes = null;
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
