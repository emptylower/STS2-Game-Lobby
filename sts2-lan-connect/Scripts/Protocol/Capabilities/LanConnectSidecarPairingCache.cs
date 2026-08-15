namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectPairedSidecarMessage(
    ulong SenderPeerId,
    ulong RecipientPeerId,
    uint Sequence,
    LanConnectSidecarMessageKind MessageKind,
    LanConnectSidecarFrame Frame,
    object VanillaMessage);

internal sealed class LanConnectSidecarPairingCache
{
    internal const int MaxPendingPairsPerDirection = 16;
    internal static readonly TimeSpan PairTimeout = TimeSpan.FromSeconds(5);

    private readonly object _sync = new();
    private readonly Dictionary<FlowKey, DirectionState> _directions = [];

    internal void BindFlow(
        ulong senderPeerId,
        ulong recipientPeerId,
        ReadOnlySpan<byte> flowNonce,
        uint initialSequence = 1)
    {
        if (flowNonce.Length != LanConnectSidecarFrameCodec.FlowNonceBytes || initialSequence == 0)
        {
            throw Invalid("Sidecar flow binding has an invalid nonce or initial sequence.");
        }

        FlowKey key = new(senderPeerId, recipientPeerId, Convert.ToHexString(flowNonce));
        lock (_sync)
        {
            if (!_directions.TryAdd(key, new DirectionState(initialSequence)))
            {
                throw Invalid("Sidecar flow is already bound.");
            }
        }
    }

    internal LanConnectPairedSidecarMessage? SubmitFrame(
        ulong senderPeerId,
        ulong recipientPeerId,
        LanConnectSidecarFrame frame,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_sync)
        {
            DirectionState state = Resolve(senderPeerId, recipientPeerId, frame.FlowNonce.Span);
            ThrowIfExpired(state, now);
            if (state.FrameSequenceExhausted || frame.MessageSequence != state.NextFrameSequence)
            {
                throw Invalid(
                    $"Sidecar frame sequence {frame.MessageSequence} does not equal expected {state.NextFrameSequence}.");
            }

            PendingPair pair = GetOrCreate(state, frame.MessageSequence, now);
            if (pair.Frame != null)
            {
                throw Invalid("Duplicate sidecar frame.");
            }

            pair.Frame = frame;
            AdvanceFrameSequence(state);
            return TryComplete(senderPeerId, recipientPeerId, state, pair);
        }
    }

    internal LanConnectPairedSidecarMessage? SubmitVanilla(
        ulong senderPeerId,
        ulong recipientPeerId,
        ReadOnlySpan<byte> flowNonce,
        LanConnectSidecarMessageKind messageKind,
        object vanillaMessage,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(vanillaMessage);
        if (!Enum.IsDefined(messageKind))
        {
            throw Invalid("Vanilla message kind is unknown.");
        }

        lock (_sync)
        {
            DirectionState state = Resolve(senderPeerId, recipientPeerId, flowNonce);
            ThrowIfExpired(state, now);
            if (state.VanillaSequenceExhausted)
            {
                throw Invalid("Vanilla sidecar pairing sequence is exhausted.");
            }

            uint sequence = state.NextVanillaSequence;
            PendingPair pair = GetOrCreate(state, sequence, now);
            if (pair.VanillaMessage != null)
            {
                throw Invalid("Duplicate vanilla message placeholder.");
            }

            pair.VanillaKind = messageKind;
            pair.VanillaMessage = vanillaMessage;
            AdvanceVanillaSequence(state);
            return TryComplete(senderPeerId, recipientPeerId, state, pair);
        }
    }

    internal void UnbindFlow(ulong senderPeerId, ulong recipientPeerId, ReadOnlySpan<byte> flowNonce)
    {
        FlowKey key = new(senderPeerId, recipientPeerId, Convert.ToHexString(flowNonce));
        lock (_sync)
        {
            _directions.Remove(key);
        }
    }

    internal void ClearPeer(ulong peerId)
    {
        lock (_sync)
        {
            foreach (FlowKey key in _directions.Keys
                         .Where(key => key.SenderPeerId == peerId || key.RecipientPeerId == peerId)
                         .ToArray())
            {
                _directions.Remove(key);
            }
        }
    }

    internal void Clear()
    {
        lock (_sync)
        {
            _directions.Clear();
        }
    }

    private DirectionState Resolve(
        ulong senderPeerId,
        ulong recipientPeerId,
        ReadOnlySpan<byte> flowNonce)
    {
        FlowKey key = new(senderPeerId, recipientPeerId, Convert.ToHexString(flowNonce));
        return _directions.TryGetValue(key, out DirectionState? state)
            ? state
            : throw Invalid("Sidecar frame does not match a trusted bound flow.");
    }

    private static PendingPair GetOrCreate(DirectionState state, uint sequence, DateTimeOffset now)
    {
        if (state.Pending.TryGetValue(sequence, out PendingPair? existing))
        {
            return existing;
        }

        if (state.Pending.Count >= MaxPendingPairsPerDirection)
        {
            throw Invalid("Sidecar pairing cache exceeds 16 pending pairs for one direction.");
        }

        PendingPair created = new(sequence, now);
        state.Pending.Add(sequence, created);
        return created;
    }

    private static LanConnectPairedSidecarMessage? TryComplete(
        ulong senderPeerId,
        ulong recipientPeerId,
        DirectionState state,
        PendingPair pair)
    {
        if (pair.Frame == null || pair.VanillaMessage == null)
        {
            return null;
        }

        if (pair.Frame.MessageKind != pair.VanillaKind)
        {
            throw Invalid(
                $"Sidecar frame kind {pair.Frame.MessageKind} conflicts with vanilla kind {pair.VanillaKind}.");
        }

        state.Pending.Remove(pair.Sequence);
        return new LanConnectPairedSidecarMessage(
            senderPeerId,
            recipientPeerId,
            pair.Sequence,
            pair.Frame.MessageKind,
            pair.Frame,
            pair.VanillaMessage);
    }

    private static void ThrowIfExpired(DirectionState state, DateTimeOffset now)
    {
        PendingPair? expired = state.Pending.Values
            .OrderBy(static pair => pair.CreatedAt)
            .FirstOrDefault(pair => now - pair.CreatedAt > PairTimeout);
        if (expired != null)
        {
            state.Pending.Clear();
            throw Invalid($"Sidecar pair {expired.Sequence} exceeded the five-second handler barrier.");
        }
    }

    private static void AdvanceFrameSequence(DirectionState state)
    {
        if (state.NextFrameSequence == uint.MaxValue)
        {
            state.FrameSequenceExhausted = true;
            return;
        }

        state.NextFrameSequence++;
    }

    private static void AdvanceVanillaSequence(DirectionState state)
    {
        if (state.NextVanillaSequence == uint.MaxValue)
        {
            state.VanillaSequenceExhausted = true;
            return;
        }

        state.NextVanillaSequence++;
    }

    private static InvalidDataException Invalid(string message) => new(message);

    private readonly record struct FlowKey(ulong SenderPeerId, ulong RecipientPeerId, string FlowNonceHex);

    private sealed class DirectionState
    {
        internal DirectionState(uint initialSequence)
        {
            NextFrameSequence = initialSequence;
            NextVanillaSequence = initialSequence;
        }

        internal uint NextFrameSequence { get; set; }
        internal uint NextVanillaSequence { get; set; }
        internal bool FrameSequenceExhausted { get; set; }
        internal bool VanillaSequenceExhausted { get; set; }
        internal Dictionary<uint, PendingPair> Pending { get; } = [];
    }

    private sealed class PendingPair(uint sequence, DateTimeOffset createdAt)
    {
        internal uint Sequence { get; } = sequence;
        internal DateTimeOffset CreatedAt { get; } = createdAt;
        internal LanConnectSidecarFrame? Frame { get; set; }
        internal LanConnectSidecarMessageKind VanillaKind { get; set; }
        internal object? VanillaMessage { get; set; }
    }
}
