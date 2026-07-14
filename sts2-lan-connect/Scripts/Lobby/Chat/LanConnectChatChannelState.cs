using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

namespace Sts2LanConnect.Scripts;

internal enum ServerChatDeliveryState
{
    Pending,

    Confirmed,

    Failed,

    DeliveryUnknown
}

internal sealed class ServerChatPendingMessage
{
    public string ClientMessageId { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string? SenderNetId { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ServerChatMessageState
{
    public string? MessageId { get; init; }

    public string? ClientMessageId { get; init; }

    public string SenderName { get; init; } = string.Empty;

    public string? SenderNetId { get; init; }

    public string Text { get; init; } = string.Empty;

    public long Sequence { get; init; }

    public bool IsLocal { get; init; }

    public ServerChatDeliveryState Delivery { get; init; }

    public bool DisconnectedAfterUnknown { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset SentAt { get; init; }
}

internal readonly record struct LanConnectChatApplyResult(bool ReconnectRequired);

internal enum LanConnectServerChatPresentation
{
    Unsupported,
    Connecting,
    Reconnecting,
    Ready,
    Disabled,
    TransportFailure
}

internal sealed class LanConnectChatArrivalSequenceClock
{
    private long _current;

    internal LanConnectChatArrivalSequenceClock(long initialValue = 0)
    {
        if (initialValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialValue));
        }
        _current = initialValue;
    }

    internal long Next()
    {
        while (true)
        {
            long current = Volatile.Read(ref _current);
            if (current == long.MaxValue)
            {
                throw new InvalidOperationException("The chat arrival sequence is exhausted.");
            }

            long next = current + 1;
            if (Interlocked.CompareExchange(ref _current, next, current) == current)
            {
                return next;
            }
        }
    }

    internal void Observe(long sequence)
    {
        if (sequence == long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "The maximum sequence is reserved for overflow detection.");
        }

        long current = Volatile.Read(ref _current);
        while (sequence > current)
        {
            long observed = Interlocked.CompareExchange(ref _current, sequence, current);
            if (observed == current)
            {
                return;
            }
            current = observed;
        }
    }
}

internal sealed class LanConnectChatChannelState
{
    private static readonly TimeSpan StalePendingThreshold = TimeSpan.FromSeconds(10);
    private static readonly LanConnectChatArrivalSequenceClock SharedArrivalClock = new();

    private readonly object _mutationLock = new();
    private readonly List<ServerChatMessageState> _messages = new();
    private readonly Dictionary<string, int> _clientPendingIndex = new();
    private readonly Dictionary<string, int> _serverMessageIndex = new();
    private readonly Dictionary<string, DateTimeOffset> _pendingQueueTimes = new();
    private readonly Dictionary<string, long> _unreadIncomingSequences = new(StringComparer.Ordinal);
    private readonly HashSet<string> _belowIncomingMessageIds = new(StringComparer.Ordinal);
    private readonly LanConnectChatArrivalSequenceClock _arrivalClock;
    private SnapshotAssembly? _snapshotAssembly;
    private long _revision;
    private bool _chatEnabled;
    private ServerChatEnabledFeatures _enabledFeatures = new();
    private string _draft = string.Empty;
    private int _unreadCount;
    private long? _firstUnreadSequence;
    private double _scrollOffset;
    private bool _isAtBottom = true;
    private int _newMessagesBelowCount;
    private bool _isVisible;
    private LanConnectServerChatPresentation _presentation = LanConnectServerChatPresentation.Connecting;
    private string _presentationDetail = string.Empty;

    internal LanConnectChatChannelState(LanConnectChatChannel channel)
        : this(channel, SharedArrivalClock)
    {
    }

    internal LanConnectChatChannelState(
        LanConnectChatChannel channel,
        LanConnectChatArrivalSequenceClock arrivalClock)
    {
        ArgumentNullException.ThrowIfNull(arrivalClock);
        Channel = channel;
        _arrivalClock = arrivalClock;
        if (channel == LanConnectChatChannel.Room)
        {
            _chatEnabled = true;
            _presentation = LanConnectServerChatPresentation.Ready;
        }
    }

    internal LanConnectChatChannel Channel { get; }

    internal IReadOnlyList<ServerChatMessageState> Messages
    {
        get
        {
            lock (_mutationLock)
            {
                return _messages.ToArray();
            }
        }
    }

    internal long Revision
    {
        get
        {
            lock (_mutationLock)
            {
                return _revision;
            }
        }
    }

    internal bool ChatEnabled
    {
        get
        {
            lock (_mutationLock)
            {
                return _chatEnabled;
            }
        }
    }

    internal LanConnectServerChatPresentation Presentation
    {
        get
        {
            lock (_mutationLock)
            {
                return _presentation;
            }
        }
    }

    internal string PresentationDetail
    {
        get
        {
            lock (_mutationLock)
            {
                return _presentationDetail;
            }
        }
    }

    internal void SetPresentation(
        LanConnectServerChatPresentation presentation,
        string? detail = null)
    {
        lock (_mutationLock)
        {
            string normalizedDetail = detail?.Trim() ?? string.Empty;
            if (_presentation == presentation &&
                string.Equals(_presentationDetail, normalizedDetail, StringComparison.Ordinal))
            {
                return;
            }

            _presentation = presentation;
            _presentationDetail = normalizedDetail;
            Touch();
        }
    }

    internal void SetPresentationForTests(
        LanConnectServerChatPresentation presentation,
        string? detail = null) =>
        SetPresentation(presentation, detail);

    internal string Draft
    {
        get
        {
            lock (_mutationLock)
            {
                return _draft;
            }
        }
        private set => _draft = value;
    }

    internal int UnreadCount
    {
        get
        {
            lock (_mutationLock)
            {
                return _unreadCount;
            }
        }
        private set => _unreadCount = value;
    }

    internal long? FirstUnreadSequence
    {
        get
        {
            lock (_mutationLock)
            {
                return _firstUnreadSequence;
            }
        }
        private set => _firstUnreadSequence = value;
    }

    internal double ScrollOffset
    {
        get
        {
            lock (_mutationLock)
            {
                return _scrollOffset;
            }
        }
        private set => _scrollOffset = value;
    }

    internal bool IsAtBottom
    {
        get
        {
            lock (_mutationLock)
            {
                return _isAtBottom;
            }
        }
        private set => _isAtBottom = value;
    }

    internal int NewMessagesBelowCount
    {
        get
        {
            lock (_mutationLock)
            {
                return _newMessagesBelowCount;
            }
        }
        private set => _newMessagesBelowCount = value;
    }

    internal bool IsVisible
    {
        get
        {
            lock (_mutationLock)
            {
                return _isVisible;
            }
        }
        private set => _isVisible = value;
    }

    internal void SetDraft(string? value)
    {
        lock (_mutationLock)
        {
            string next = value ?? string.Empty;
            if (_draft == next)
            {
                return;
            }

            _draft = next;
            Touch();
        }
    }

    internal void SetVisible(bool value)
    {
        lock (_mutationLock)
        {
            bool mutated = _isVisible != value;
            _isVisible = value;
            if (value)
            {
                mutated |= MarkReadCore();
            }

            if (mutated)
            {
                Touch();
            }
        }
    }

    internal void SetScrollState(double offset, bool atBottom)
    {
        lock (_mutationLock)
        {
            double nextOffset = Math.Max(0, offset);
            bool mutated = _scrollOffset != nextOffset || _isAtBottom != atBottom;
            _scrollOffset = nextOffset;
            _isAtBottom = atBottom;
            if (atBottom && _newMessagesBelowCount != 0)
            {
                _newMessagesBelowCount = 0;
                _belowIncomingMessageIds.Clear();
                mutated = true;
            }

            if (mutated)
            {
                Touch();
            }
        }
    }

    internal void MarkRead()
    {
        lock (_mutationLock)
        {
            if (MarkReadCore())
            {
                Touch();
            }
        }
    }

    internal void Queue(ServerChatPendingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_mutationLock)
        {
            string key = message.ClientMessageId ?? string.Empty;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            int existingIndex = -1;
            if (_clientPendingIndex.TryGetValue(key, out int knownIndex))
            {
                existingIndex = knownIndex;
            }

            DateTimeOffset queuedAt = message.QueuedAt == default ? DateTimeOffset.UtcNow : message.QueuedAt;
            ServerChatMessageState entry = new()
            {
                ClientMessageId = message.ClientMessageId,
                SenderName = message.SenderName ?? string.Empty,
                SenderNetId = message.SenderNetId,
                Text = message.Text ?? string.Empty,
                Delivery = ServerChatDeliveryState.Pending,
                IsLocal = true,
                SentAt = queuedAt
            };
            if (existingIndex >= 0 && existingIndex < _messages.Count &&
                _messages[existingIndex].ClientMessageId == message.ClientMessageId)
            {
                _messages[existingIndex] = entry;
            }
            else
            {
                _messages.Add(entry);
                _clientPendingIndex[key] = _messages.Count - 1;
            }
            _pendingQueueTimes[key] = queuedAt;
            Touch();
        }
    }

    internal void BeginPendingText(
        string clientMessageId,
        string senderName,
        string text,
        string? senderNetId = null,
        DateTimeOffset queuedAt = default)
    {
        Queue(new ServerChatPendingMessage
        {
            ClientMessageId = clientMessageId,
            SenderName = senderName,
            SenderNetId = senderNetId,
            Text = text,
            QueuedAt = queuedAt == default ? DateTimeOffset.UtcNow : queuedAt
        });
    }

    internal LanConnectChatApplyResult Apply(ServerChatInboundEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_mutationLock)
        {
            return envelope.Type switch
            {
                "chat_ack" => ApplyAck(envelope),
                "chat_error" => ApplyError(envelope),
                "chat_message" => ApplyMessage(envelope),
                "chat_snapshot_begin" => ApplySnapshotBegin(envelope),
                "chat_snapshot_chunk" => ApplySnapshotChunk(envelope),
                "chat_snapshot_end" => ApplySnapshotEnd(envelope),
                "chat_ready" => ApplyReady(envelope),
                "chat_state" => ApplyState(envelope),
                "chat_history_cleared" => ApplyHistoryCleared(envelope),
                _ => new LanConnectChatApplyResult(ReconnectRequired: false)
            };
        }
    }

    internal void MarkLegacySendConfirmed(string clientMessageId, int confirmedMessageLimit = int.MaxValue)
    {
        if (confirmedMessageLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedMessageLimit));
        }
        if (string.IsNullOrEmpty(clientMessageId))
        {
            return;
        }

        lock (_mutationLock)
        {
            if (_clientPendingIndex.TryGetValue(clientMessageId, out int index) &&
                index < _messages.Count &&
                _messages[index].Delivery == ServerChatDeliveryState.Pending)
            {
                _messages[index] = WithDelivery(_messages[index], ServerChatDeliveryState.Confirmed);
                _pendingQueueTimes.Remove(clientMessageId);
                PruneOldestConfirmed(confirmedMessageLimit);
                Touch();
            }
        }
    }

    internal void MarkFailed(string clientMessageId, string code, string message)
    {
        if (string.IsNullOrEmpty(clientMessageId))
        {
            return;
        }

        lock (_mutationLock)
        {
            if (_clientPendingIndex.TryGetValue(clientMessageId, out int index) &&
                index < _messages.Count)
            {
                ServerChatMessageState existing = _messages[index];
                if (HasFailure(existing, code, message))
                {
                    return;
                }

                _messages[index] = new ServerChatMessageState
                {
                    MessageId = existing.MessageId,
                    ClientMessageId = existing.ClientMessageId,
                    SenderName = existing.SenderName,
                    SenderNetId = existing.SenderNetId,
                    Text = existing.Text,
                    Sequence = existing.Sequence,
                    IsLocal = existing.IsLocal,
                    Delivery = ServerChatDeliveryState.Failed,
                    DisconnectedAfterUnknown = existing.DisconnectedAfterUnknown,
                    ErrorCode = code,
                    ErrorMessage = message,
                    SentAt = existing.SentAt
                };
                _pendingQueueTimes.Remove(clientMessageId);
                Touch();
            }
        }
    }

    internal void AppendConfirmedForTests(string messageId, string senderName, string text, long sequence, bool isLocal)
    {
        lock (_mutationLock)
        {
            if (!string.IsNullOrEmpty(messageId) && _serverMessageIndex.ContainsKey(messageId))
            {
                return;
            }

            _arrivalClock.Observe(sequence);

            ServerChatMessageState entry = new()
            {
                MessageId = messageId,
                SenderName = senderName ?? string.Empty,
                Text = text ?? string.Empty,
                Sequence = sequence,
                IsLocal = isLocal,
                Delivery = ServerChatDeliveryState.Confirmed,
                SentAt = DateTimeOffset.UtcNow
            };
            _messages.Add(entry);
            if (!string.IsNullOrEmpty(messageId))
            {
                _serverMessageIndex[messageId] = _messages.Count - 1;
            }
            TrackIncoming(messageId, sequence, isLocal);
            Touch();
        }
    }

    internal void AppendLegacyConfirmed(
        string messageId,
        string senderName,
        string? senderNetId,
        string text,
        DateTimeOffset sentAt,
        bool isLocal,
        int confirmedMessageLimit)
    {
        if (confirmedMessageLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedMessageLimit));
        }

        lock (_mutationLock)
        {
            if (!string.IsNullOrEmpty(messageId) && _serverMessageIndex.ContainsKey(messageId))
            {
                return;
            }

            long sequence = _arrivalClock.Next();
            ServerChatMessageState entry = new()
            {
                MessageId = messageId,
                SenderName = senderName ?? string.Empty,
                SenderNetId = senderNetId,
                Text = text ?? string.Empty,
                Sequence = sequence,
                IsLocal = isLocal,
                Delivery = ServerChatDeliveryState.Confirmed,
                SentAt = sentAt
            };
            _messages.Add(entry);
            if (!string.IsNullOrEmpty(messageId))
            {
                _serverMessageIndex[messageId] = _messages.Count - 1;
            }
            TrackIncoming(messageId, sequence, isLocal);
            PruneOldestConfirmed(confirmedMessageLimit);
            Touch();
        }
    }

    internal void MarkTimedOut(DateTimeOffset now)
    {
        lock (_mutationLock)
        {
            bool mutated = false;
            foreach (KeyValuePair<string, DateTimeOffset> pair in _pendingQueueTimes)
            {
                if (now - pair.Value < StalePendingThreshold)
                {
                    continue;
                }

                if (_clientPendingIndex.TryGetValue(pair.Key, out int index) &&
                    index < _messages.Count &&
                    _messages[index].Delivery == ServerChatDeliveryState.Pending)
                {
                    _messages[index] = WithDelivery(_messages[index], ServerChatDeliveryState.DeliveryUnknown);
                    mutated = true;
                }
            }
            if (mutated)
            {
                Touch();
            }
        }
    }

    internal void MarkDisconnected()
    {
        lock (_mutationLock)
        {
            // The next connection starts a new snapshot stream; visible message state is retained.
            _snapshotAssembly = null;
            bool mutated = false;
            for (int index = 0; index < _messages.Count; index++)
            {
                ServerChatMessageState message = _messages[index];
                if (message.Delivery != ServerChatDeliveryState.DeliveryUnknown || message.DisconnectedAfterUnknown)
                {
                    continue;
                }

                _messages[index] = WithDisconnectedAfterUnknown(message);
                mutated = true;
            }

            if (mutated)
            {
                Touch();
            }
        }
    }

    internal void ClearForContextChange()
    {
        lock (_mutationLock)
        {
            _messages.Clear();
            _snapshotAssembly = null;
            _draft = string.Empty;
            ResetIncomingIndicators();
            _scrollOffset = 0;
            _isAtBottom = true;
            _isVisible = false;
            RebuildIndices();
            Touch();
        }
    }

    private LanConnectChatApplyResult ApplyAck(ServerChatInboundEnvelope envelope)
    {
        string clientMessageId = envelope.ClientMessageId ?? string.Empty;
        ServerChatCanonicalMessage? canonical = envelope.CanonicalMessage;
        if (string.IsNullOrEmpty(clientMessageId) || canonical == null || string.IsNullOrEmpty(canonical.MessageId))
        {
            return new LanConnectChatApplyResult(ReconnectRequired: true);
        }
        string text = ResolveDisplayText(canonical);
        if (string.IsNullOrEmpty(text))
        {
            return new LanConnectChatApplyResult(ReconnectRequired: false);
        }

        bool hasClientEntry = _clientPendingIndex.TryGetValue(clientMessageId, out int clientIndex) &&
                              clientIndex < _messages.Count &&
                              _messages[clientIndex].ClientMessageId == clientMessageId;
        bool hasServerEntry = _serverMessageIndex.TryGetValue(canonical.MessageId, out int serverIndex) &&
                              serverIndex < _messages.Count &&
                              _messages[serverIndex].MessageId == canonical.MessageId;

        if (hasClientEntry)
        {
            bool removedDuplicate = hasServerEntry && serverIndex != clientIndex;
            if (removedDuplicate)
            {
                RollBackIncoming(canonical.MessageId);
                _messages.RemoveAt(serverIndex);
                if (serverIndex < clientIndex)
                {
                    clientIndex--;
                }
            }

            ServerChatMessageState existing = _messages[clientIndex];
            ServerChatMessageState updated = CreateAcknowledged(existing, canonical, clientMessageId, text);
            bool mutated = removedDuplicate || !MessagesEqual(existing, updated);
            if (!mutated)
            {
                return new LanConnectChatApplyResult(ReconnectRequired: false);
            }

            _messages[clientIndex] = updated;
            _pendingQueueTimes.Remove(clientMessageId);
            RebuildIndices();
            Touch();
            return new LanConnectChatApplyResult(ReconnectRequired: false);
        }

        if (hasServerEntry)
        {
            ServerChatMessageState existing = _messages[serverIndex];
            ServerChatMessageState updated = CreateAcknowledged(existing, canonical, clientMessageId, text);
            if (MessagesEqual(existing, updated))
            {
                return new LanConnectChatApplyResult(ReconnectRequired: false);
            }

            RollBackIncoming(canonical.MessageId);
            _messages[serverIndex] = updated;
            RebuildIndices();
            Touch();
            return new LanConnectChatApplyResult(ReconnectRequired: false);
        }

        ServerChatMessageState fresh = new()
        {
            MessageId = canonical.MessageId,
            ClientMessageId = clientMessageId,
            SenderName = canonical.SenderName ?? string.Empty,
            SenderNetId = canonical.SenderId,
            Text = text,
            IsLocal = true,
            Delivery = ServerChatDeliveryState.Confirmed,
            SentAt = canonical.SentAt == default ? DateTimeOffset.UtcNow : canonical.SentAt
        };
        _messages.Add(fresh);
        RebuildIndices();
        Touch();
        return new LanConnectChatApplyResult(ReconnectRequired: false);
    }

    private LanConnectChatApplyResult ApplyError(ServerChatInboundEnvelope envelope)
    {
        string clientMessageId = envelope.ClientMessageId ?? string.Empty;
        string code = envelope.Code ?? string.Empty;
        string message = envelope.ErrorMessage ?? string.Empty;
        if (string.IsNullOrEmpty(clientMessageId))
        {
            return new LanConnectChatApplyResult(ReconnectRequired: false);
        }

        if (_clientPendingIndex.TryGetValue(clientMessageId, out int index) &&
            index < _messages.Count)
        {
            ServerChatMessageState existing = _messages[index];
            if (HasFailure(existing, code, message))
            {
                return new LanConnectChatApplyResult(ReconnectRequired: false);
            }

            _messages[index] = new ServerChatMessageState
            {
                MessageId = existing.MessageId,
                ClientMessageId = existing.ClientMessageId,
                SenderName = existing.SenderName,
                SenderNetId = existing.SenderNetId,
                Text = existing.Text,
                Sequence = existing.Sequence,
                IsLocal = existing.IsLocal,
                Delivery = ServerChatDeliveryState.Failed,
                DisconnectedAfterUnknown = existing.DisconnectedAfterUnknown,
                ErrorCode = code,
                ErrorMessage = message,
                SentAt = existing.SentAt
            };
            _pendingQueueTimes.Remove(clientMessageId);
            Touch();
        }
        return new LanConnectChatApplyResult(ReconnectRequired: false);
    }

    private LanConnectChatApplyResult ApplyMessage(ServerChatInboundEnvelope envelope)
    {
        ServerChatCanonicalMessage? canonical = envelope.CanonicalMessage;
        if (canonical == null || string.IsNullOrEmpty(canonical.MessageId))
        {
            return new LanConnectChatApplyResult(ReconnectRequired: false);
        }

        if (_serverMessageIndex.TryGetValue(canonical.MessageId, out int existingIndex) &&
            existingIndex < _messages.Count)
        {
            return new LanConnectChatApplyResult(ReconnectRequired: false);
        }

        string text = ResolveDisplayText(canonical);
        if (string.IsNullOrEmpty(text))
        {
            return new LanConnectChatApplyResult(ReconnectRequired: false);
        }

        ServerChatMessageState entry = new()
        {
            MessageId = canonical.MessageId,
            SenderName = canonical.SenderName ?? string.Empty,
            SenderNetId = canonical.SenderId,
            Text = text,
            Sequence = _arrivalClock.Next(),
            IsLocal = false,
            Delivery = ServerChatDeliveryState.Confirmed,
            SentAt = canonical.SentAt == default ? DateTimeOffset.UtcNow : canonical.SentAt
        };
        _messages.Add(entry);
        _serverMessageIndex[canonical.MessageId] = _messages.Count - 1;
        TrackIncoming(entry.MessageId, entry.Sequence, entry.IsLocal);
        Touch();
        return new LanConnectChatApplyResult(ReconnectRequired: false);
    }

    private LanConnectChatApplyResult ApplySnapshotBegin(ServerChatInboundEnvelope envelope)
    {
        string snapshotId = envelope.SnapshotId ?? string.Empty;
        string instanceId = envelope.InstanceId ?? string.Empty;
        int historyEpoch = envelope.HistoryEpoch ?? -1;
        int totalMessages = envelope.TotalMessages ?? -1;
        if (_snapshotAssembly != null || string.IsNullOrEmpty(snapshotId) || string.IsNullOrEmpty(instanceId) ||
            historyEpoch < 0 || totalMessages < 0)
        {
            return RejectSnapshot();
        }

        _snapshotAssembly = new SnapshotAssembly(snapshotId, instanceId, historyEpoch, totalMessages);
        return new LanConnectChatApplyResult(ReconnectRequired: false);
    }

    private LanConnectChatApplyResult ApplySnapshotChunk(ServerChatInboundEnvelope envelope)
    {
        string snapshotId = envelope.SnapshotId ?? string.Empty;
        if (_snapshotAssembly == null || _snapshotAssembly.SnapshotId != snapshotId)
        {
            return RejectSnapshot();
        }

        int chunkIndex = envelope.ChunkIndex ?? -1;
        if (chunkIndex != _snapshotAssembly.Chunks.Count)
        {
            return RejectSnapshot();
        }

        List<ServerChatCanonicalMessage>? messages = envelope.Messages;
        if (messages == null || _snapshotAssembly.MessageCount + messages.Count > _snapshotAssembly.TotalMessages)
        {
            return RejectSnapshot();
        }

        HashSet<string> chunkIds = new(StringComparer.Ordinal);
        foreach (ServerChatCanonicalMessage message in messages)
        {
            if (string.IsNullOrEmpty(message.MessageId) ||
                _snapshotAssembly.MessageIds.Contains(message.MessageId) ||
                !chunkIds.Add(message.MessageId))
            {
                return RejectSnapshot();
            }
        }

        _snapshotAssembly.MessageIds.UnionWith(chunkIds);
        _snapshotAssembly.MessageCount += messages.Count;
        _snapshotAssembly.Chunks.Add(messages);
        return new LanConnectChatApplyResult(ReconnectRequired: false);
    }

    private LanConnectChatApplyResult ApplySnapshotEnd(ServerChatInboundEnvelope envelope)
    {
        string snapshotId = envelope.SnapshotId ?? string.Empty;
        SnapshotAssembly? assembly = _snapshotAssembly;
        if (assembly == null || assembly.SnapshotId != snapshotId ||
            envelope.HistoryEpoch != assembly.HistoryEpoch || assembly.MessageCount != assembly.TotalMessages)
        {
            return RejectSnapshot();
        }

        _snapshotAssembly = null;
        ReplaceConfirmedFromSnapshot(assembly);
        return new LanConnectChatApplyResult(ReconnectRequired: false);
    }

    private LanConnectChatApplyResult ApplyReady(ServerChatInboundEnvelope envelope)
    {
        string? newInstance = envelope.InstanceId;
        int newEpoch = envelope.HistoryEpoch ?? 0;
        bool differentInstance = !string.IsNullOrEmpty(newInstance) &&
                                  !string.IsNullOrEmpty(_instanceId) &&
                                  !string.Equals(newInstance, _instanceId, StringComparison.Ordinal);
        bool greaterEpoch = newEpoch > _lastSnapshotEpoch;
        bool newChatEnabled = envelope.ChatEnabled == true;
        ServerChatEnabledFeatures newFeatures = envelope.EnabledFeatures ?? new ServerChatEnabledFeatures();
        bool metadataChanged = (!string.IsNullOrEmpty(newInstance) &&
                                !string.Equals(newInstance, _instanceId, StringComparison.Ordinal)) || greaterEpoch ||
                               _chatEnabled != newChatEnabled || !FeaturesEqual(_enabledFeatures, newFeatures);
        if (differentInstance || greaterEpoch)
        {
            ClearConfirmedKeepPendingAndUnknown();
        }
        if (!string.IsNullOrEmpty(newInstance))
        {
            _instanceId = newInstance;
        }
        if (greaterEpoch)
        {
            _lastSnapshotEpoch = newEpoch;
        }
        _chatEnabled = newChatEnabled;
        _enabledFeatures = CopyFeatures(newFeatures);
        if (metadataChanged)
        {
            Touch();
        }
        return new LanConnectChatApplyResult(ReconnectRequired: false);
    }

    private LanConnectChatApplyResult ApplyState(ServerChatInboundEnvelope envelope)
    {
        if (!envelope.ChatEnabled.HasValue)
        {
            return new LanConnectChatApplyResult(ReconnectRequired: true);
        }

        bool mutated = false;
        int newEpoch = envelope.HistoryEpoch ?? _lastSnapshotEpoch;
        if (newEpoch > _lastSnapshotEpoch)
        {
            ClearConfirmedKeepPendingAndUnknown();
            _lastSnapshotEpoch = newEpoch;
            mutated = true;
        }

        if (_chatEnabled != envelope.ChatEnabled.Value)
        {
            _chatEnabled = envelope.ChatEnabled.Value;
            mutated = true;
        }

        ServerChatEnabledFeatures newFeatures = envelope.EnabledFeatures ?? new ServerChatEnabledFeatures();
        if (!FeaturesEqual(_enabledFeatures, newFeatures))
        {
            _enabledFeatures = CopyFeatures(newFeatures);
            mutated = true;
        }

        if (mutated)
        {
            Touch();
        }
        return new LanConnectChatApplyResult(ReconnectRequired: false);
    }

    private LanConnectChatApplyResult ApplyHistoryCleared(ServerChatInboundEnvelope envelope)
    {
        int newEpoch = envelope.HistoryEpoch ?? 0;
        if (newEpoch > _lastSnapshotEpoch)
        {
            ClearConfirmedKeepPendingAndUnknown();
            _lastSnapshotEpoch = newEpoch;
            Touch();
        }
        return new LanConnectChatApplyResult(ReconnectRequired: false);
    }

    private int _lastSnapshotEpoch;
    private string? _instanceId;

    private bool ClearConfirmedKeepPendingAndUnknown()
    {
        bool mutated = false;
        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].Delivery == ServerChatDeliveryState.Confirmed)
            {
                _messages.RemoveAt(i);
                mutated = true;
            }
        }
        RebuildIndices();
        ResetIncomingIndicators();
        return mutated;
    }

    private void ReplaceConfirmedFromSnapshot(SnapshotAssembly assembly)
    {
        ClearConfirmedKeepPendingAndUnknown();
        long sequence = 0;
        foreach (List<ServerChatCanonicalMessage> chunk in assembly.Chunks)
        {
            foreach (ServerChatCanonicalMessage message in chunk)
            {
                if (string.IsNullOrEmpty(message.MessageId))
                {
                    continue;
                }
                string text = ResolveDisplayText(message);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                ServerChatMessageState entry = new()
                {
                    MessageId = message.MessageId,
                    SenderName = message.SenderName ?? string.Empty,
                    SenderNetId = message.SenderId,
                    Text = text,
                    Sequence = sequence++,
                    IsLocal = false,
                    Delivery = ServerChatDeliveryState.Confirmed,
                    SentAt = message.SentAt
                };
                _messages.Add(entry);
                _serverMessageIndex[message.MessageId] = _messages.Count - 1;
            }
        }
        _instanceId = assembly.InstanceId;
        _lastSnapshotEpoch = assembly.HistoryEpoch;
        RebuildIndices();
        Touch();
    }

    private LanConnectChatApplyResult RejectSnapshot()
    {
        _snapshotAssembly = null;
        return new LanConnectChatApplyResult(ReconnectRequired: true);
    }

    private void RebuildIndices()
    {
        _clientPendingIndex.Clear();
        _serverMessageIndex.Clear();
        for (int i = 0; i < _messages.Count; i++)
        {
            ServerChatMessageState entry = _messages[i];
            if (!string.IsNullOrEmpty(entry.ClientMessageId))
            {
                _clientPendingIndex[entry.ClientMessageId] = i;
            }
            if (!string.IsNullOrEmpty(entry.MessageId))
            {
                _serverMessageIndex[entry.MessageId] = i;
            }
        }
        RebuildPendingQueueTimes();
    }

    private void PruneOldestConfirmed(int confirmedMessageLimit)
    {
        int confirmedCount = 0;
        foreach (ServerChatMessageState message in _messages)
        {
            if (message.Delivery == ServerChatDeliveryState.Confirmed)
            {
                confirmedCount++;
            }
        }

        bool removed = false;
        while (confirmedCount > confirmedMessageLimit)
        {
            int index = _messages.FindIndex(message => message.Delivery == ServerChatDeliveryState.Confirmed);
            if (index < 0)
            {
                break;
            }

            string? messageId = _messages[index].MessageId;
            if (!string.IsNullOrEmpty(messageId))
            {
                RollBackIncoming(messageId);
            }
            _messages.RemoveAt(index);
            confirmedCount--;
            removed = true;
        }

        if (removed)
        {
            RebuildIndices();
        }
    }

    private void RebuildPendingQueueTimes()
    {
        _pendingQueueTimes.Clear();
        foreach (ServerChatMessageState entry in _messages)
        {
            if (entry.Delivery == ServerChatDeliveryState.Pending && !string.IsNullOrEmpty(entry.ClientMessageId))
            {
                _pendingQueueTimes[entry.ClientMessageId] = entry.SentAt;
            }
        }
    }

    private bool MarkReadCore()
    {
        if (_unreadCount == 0 && _firstUnreadSequence == null)
        {
            return false;
        }

        _unreadCount = 0;
        _firstUnreadSequence = null;
        _unreadIncomingSequences.Clear();
        return true;
    }

    private void TrackIncoming(string? messageId, long sequence, bool isLocal)
    {
        if (isLocal || string.IsNullOrEmpty(messageId))
        {
            return;
        }

        if (!_isVisible)
        {
            _unreadIncomingSequences.Add(messageId, sequence);
            _unreadCount = _unreadIncomingSequences.Count;
            if (!_firstUnreadSequence.HasValue || sequence < _firstUnreadSequence.Value)
            {
                _firstUnreadSequence = sequence;
            }
        }
        else if (!_isAtBottom)
        {
            _belowIncomingMessageIds.Add(messageId);
            _newMessagesBelowCount = _belowIncomingMessageIds.Count;
        }
    }

    private void RollBackIncoming(string messageId)
    {
        if (_unreadIncomingSequences.Remove(messageId, out long removedSequence))
        {
            _unreadCount = _unreadIncomingSequences.Count;
            if (_unreadCount == 0)
            {
                _firstUnreadSequence = null;
            }
            else if (_firstUnreadSequence == removedSequence)
            {
                RecomputeFirstUnreadSequence();
            }
        }
        if (_belowIncomingMessageIds.Remove(messageId))
        {
            _newMessagesBelowCount = _belowIncomingMessageIds.Count;
        }
    }

    private void RecomputeFirstUnreadSequence()
    {
        _firstUnreadSequence = null;
        foreach (long sequence in _unreadIncomingSequences.Values)
        {
            if (!_firstUnreadSequence.HasValue || sequence < _firstUnreadSequence.Value)
            {
                _firstUnreadSequence = sequence;
            }
        }
    }

    private void ResetIncomingIndicators()
    {
        _unreadCount = 0;
        _firstUnreadSequence = null;
        _newMessagesBelowCount = 0;
        _unreadIncomingSequences.Clear();
        _belowIncomingMessageIds.Clear();
    }

    private void Touch() => _revision++;

    private static ServerChatMessageState CreateAcknowledged(
        ServerChatMessageState existing,
        ServerChatCanonicalMessage canonical,
        string clientMessageId,
        string text) =>
        new()
        {
            MessageId = canonical.MessageId,
            ClientMessageId = clientMessageId,
            SenderName = string.IsNullOrEmpty(canonical.SenderName) ? existing.SenderName : canonical.SenderName,
            SenderNetId = string.IsNullOrEmpty(canonical.SenderId) ? existing.SenderNetId : canonical.SenderId,
            Text = text,
            Sequence = existing.Sequence,
            IsLocal = true,
            Delivery = ServerChatDeliveryState.Confirmed,
            DisconnectedAfterUnknown = existing.DisconnectedAfterUnknown,
            SentAt = canonical.SentAt == default ? existing.SentAt : canonical.SentAt
        };

    private static bool HasFailure(ServerChatMessageState message, string code, string errorMessage) =>
        message.Delivery == ServerChatDeliveryState.Failed &&
        string.Equals(message.ErrorCode, code, StringComparison.Ordinal) &&
        string.Equals(message.ErrorMessage, errorMessage, StringComparison.Ordinal);

    private static bool MessagesEqual(ServerChatMessageState left, ServerChatMessageState right) =>
        string.Equals(left.MessageId, right.MessageId, StringComparison.Ordinal) &&
        string.Equals(left.ClientMessageId, right.ClientMessageId, StringComparison.Ordinal) &&
        string.Equals(left.SenderName, right.SenderName, StringComparison.Ordinal) &&
        string.Equals(left.SenderNetId, right.SenderNetId, StringComparison.Ordinal) &&
        string.Equals(left.Text, right.Text, StringComparison.Ordinal) &&
        left.Sequence == right.Sequence &&
        left.IsLocal == right.IsLocal &&
        left.Delivery == right.Delivery &&
        left.DisconnectedAfterUnknown == right.DisconnectedAfterUnknown &&
        string.Equals(left.ErrorCode, right.ErrorCode, StringComparison.Ordinal) &&
        string.Equals(left.ErrorMessage, right.ErrorMessage, StringComparison.Ordinal) &&
        left.SentAt == right.SentAt;

    private static ServerChatMessageState WithDelivery(ServerChatMessageState source, ServerChatDeliveryState delivery) =>
        new()
        {
            MessageId = source.MessageId,
            ClientMessageId = source.ClientMessageId,
            SenderName = source.SenderName,
            SenderNetId = source.SenderNetId,
            Text = source.Text,
            Sequence = source.Sequence,
            IsLocal = source.IsLocal,
            Delivery = delivery,
            DisconnectedAfterUnknown = source.DisconnectedAfterUnknown,
            ErrorCode = source.ErrorCode,
            ErrorMessage = source.ErrorMessage,
            SentAt = source.SentAt
        };

    private static ServerChatMessageState WithDisconnectedAfterUnknown(ServerChatMessageState source) =>
        new()
        {
            MessageId = source.MessageId,
            ClientMessageId = source.ClientMessageId,
            SenderName = source.SenderName,
            SenderNetId = source.SenderNetId,
            Text = source.Text,
            Sequence = source.Sequence,
            IsLocal = source.IsLocal,
            Delivery = source.Delivery,
            DisconnectedAfterUnknown = true,
            ErrorCode = source.ErrorCode,
            ErrorMessage = source.ErrorMessage,
            SentAt = source.SentAt
        };

    private static string ResolveDisplayText(ServerChatCanonicalMessage message)
    {
        if (message.Content?.FormatVersion == 1 && message.Content.Segments is { Count: 1 })
        {
            ServerChatTextSegment segment = message.Content.Segments[0];
            if (segment != null && string.Equals(segment.Kind, "text", StringComparison.Ordinal))
            {
                string text = segment.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }

        if (!string.IsNullOrEmpty(message.PlainTextFallback))
        {
            return message.PlainTextFallback;
        }

        return string.Empty;
    }

    private static bool FeaturesEqual(ServerChatEnabledFeatures left, ServerChatEnabledFeatures right) =>
        left.RichContentVersion == right.RichContentVersion &&
        left.EmojiSetVersion == right.EmojiSetVersion &&
        left.ItemRefVersion == right.ItemRefVersion;

    private static ServerChatEnabledFeatures CopyFeatures(ServerChatEnabledFeatures source) =>
        new()
        {
            RichContentVersion = source.RichContentVersion,
            EmojiSetVersion = source.EmojiSetVersion,
            ItemRefVersion = source.ItemRefVersion
        };

    private sealed class SnapshotAssembly
    {
        public SnapshotAssembly(string snapshotId, string instanceId, int historyEpoch, int totalMessages)
        {
            SnapshotId = snapshotId;
            InstanceId = instanceId;
            HistoryEpoch = historyEpoch;
            TotalMessages = totalMessages;
        }

        public string SnapshotId { get; }

        public string InstanceId { get; }

        public int HistoryEpoch { get; }

        public int TotalMessages { get; }

        public int MessageCount { get; set; }

        public HashSet<string> MessageIds { get; } = new(StringComparer.Ordinal);

        public List<List<ServerChatCanonicalMessage>> Chunks { get; } = new();
    }
}
