using System;
using System.Collections.Generic;
using System.Text.Json;

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

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ServerChatMessageState
{
    public string? MessageId { get; init; }

    public string? ClientMessageId { get; init; }

    public string SenderName { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public long Sequence { get; init; }

    public bool IsLocal { get; init; }

    public ServerChatDeliveryState Delivery { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset SentAt { get; init; }
}

internal readonly record struct LanConnectChatApplyResult(bool ReconnectRequired);

internal sealed class LanConnectChatChannelState
{
    private static readonly TimeSpan StalePendingThreshold = TimeSpan.FromSeconds(10);

    private readonly object _mutationLock = new();
    private readonly List<ServerChatMessageState> _messages = new();
    private readonly Dictionary<string, int> _clientPendingIndex = new();
    private readonly Dictionary<string, int> _serverMessageIndex = new();
    private readonly Dictionary<string, DateTimeOffset> _pendingQueueTimes = new();
    private SnapshotAssembly? _snapshotAssembly;
    private long _revision;
    private bool _chatEnabled;
    private ServerChatEnabledFeatures _enabledFeatures = new();

    internal LanConnectChatChannelState(LanConnectChatChannel channel)
    {
        Channel = channel;
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

    internal string Draft { get; private set; } = string.Empty;

    internal int UnreadCount { get; private set; }

    internal long? FirstUnreadSequence { get; private set; }

    internal double ScrollOffset { get; private set; }

    internal bool IsAtBottom { get; private set; } = true;

    internal int NewMessagesBelowCount { get; private set; }

    internal bool IsVisible { get; private set; }

    internal void SetDraft(string? value)
    {
        lock (_mutationLock)
        {
            string next = value ?? string.Empty;
            if (Draft == next)
            {
                return;
            }

            Draft = next;
            Touch();
        }
    }

    internal void SetVisible(bool value)
    {
        lock (_mutationLock)
        {
            bool mutated = IsVisible != value;
            IsVisible = value;
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
            bool mutated = ScrollOffset != nextOffset || IsAtBottom != atBottom;
            ScrollOffset = nextOffset;
            IsAtBottom = atBottom;
            if (atBottom && NewMessagesBelowCount != 0)
            {
                NewMessagesBelowCount = 0;
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

    internal void BeginPendingText(string clientMessageId, string senderName, string text)
    {
        Queue(new ServerChatPendingMessage
        {
            ClientMessageId = clientMessageId,
            SenderName = senderName,
            Text = text
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

    internal void MarkLegacySendConfirmed(string clientMessageId)
    {
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
                _messages[index] = new ServerChatMessageState
                {
                    MessageId = _messages[index].MessageId,
                    ClientMessageId = _messages[index].ClientMessageId,
                    SenderName = _messages[index].SenderName,
                    Text = _messages[index].Text,
                    Sequence = _messages[index].Sequence,
                    IsLocal = _messages[index].IsLocal,
                    Delivery = ServerChatDeliveryState.Failed,
                    ErrorCode = code,
                    ErrorMessage = message,
                    SentAt = _messages[index].SentAt
                };
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
            TrackIncoming(sequence, isLocal);
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
        }
    }

    internal void ClearForContextChange()
    {
        lock (_mutationLock)
        {
            _messages.Clear();
            _snapshotAssembly = null;
            Draft = string.Empty;
            UnreadCount = 0;
            FirstUnreadSequence = null;
            ScrollOffset = 0;
            IsAtBottom = true;
            NewMessagesBelowCount = 0;
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

        if (_clientPendingIndex.TryGetValue(clientMessageId, out int index) &&
            index < _messages.Count)
        {
            ServerChatMessageState existing = _messages[index];
            ServerChatMessageState updated = new()
            {
                MessageId = canonical.MessageId,
                ClientMessageId = existing.ClientMessageId,
                SenderName = string.IsNullOrEmpty(canonical.SenderName) ? existing.SenderName : canonical.SenderName,
                Text = text,
                Sequence = existing.Sequence,
                IsLocal = existing.IsLocal || true,
                Delivery = ServerChatDeliveryState.Confirmed,
                SentAt = canonical.SentAt == default ? existing.SentAt : canonical.SentAt
            };
            _messages[index] = updated;
            if (!string.IsNullOrEmpty(updated.MessageId))
            {
                _serverMessageIndex[updated.MessageId] = index;
            }
            _pendingQueueTimes.Remove(clientMessageId);
            Touch();
            return new LanConnectChatApplyResult(ReconnectRequired: false);
        }

        if (!string.IsNullOrEmpty(canonical.MessageId) &&
            _serverMessageIndex.ContainsKey(canonical.MessageId))
        {
            return new LanConnectChatApplyResult(ReconnectRequired: false);
        }

        ServerChatMessageState fresh = new()
        {
            MessageId = canonical.MessageId,
            ClientMessageId = clientMessageId,
            SenderName = canonical.SenderName ?? string.Empty,
            Text = text,
            IsLocal = true,
            Delivery = ServerChatDeliveryState.Confirmed,
            SentAt = canonical.SentAt == default ? DateTimeOffset.UtcNow : canonical.SentAt
        };
        _messages.Add(fresh);
        if (!string.IsNullOrEmpty(fresh.MessageId))
        {
            _serverMessageIndex[fresh.MessageId] = _messages.Count - 1;
        }
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
            _messages[index] = new ServerChatMessageState
            {
                MessageId = existing.MessageId,
                ClientMessageId = existing.ClientMessageId,
                SenderName = existing.SenderName,
                Text = existing.Text,
                Sequence = existing.Sequence,
                IsLocal = existing.IsLocal,
                Delivery = ServerChatDeliveryState.Failed,
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
            Text = text,
            IsLocal = false,
            Delivery = ServerChatDeliveryState.Confirmed,
            SentAt = canonical.SentAt == default ? DateTimeOffset.UtcNow : canonical.SentAt
        };
        _messages.Add(entry);
        _serverMessageIndex[canonical.MessageId] = _messages.Count - 1;
        TrackIncoming(entry.Sequence, entry.IsLocal);
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
        if (UnreadCount == 0 && FirstUnreadSequence == null)
        {
            return false;
        }

        UnreadCount = 0;
        FirstUnreadSequence = null;
        return true;
    }

    private void TrackIncoming(long sequence, bool isLocal)
    {
        if (isLocal)
        {
            return;
        }

        if (!IsVisible)
        {
            UnreadCount++;
            FirstUnreadSequence ??= sequence;
        }
        else if (!IsAtBottom)
        {
            NewMessagesBelowCount++;
        }
    }

    private void Touch() => _revision++;

    private static ServerChatMessageState WithDelivery(ServerChatMessageState source, ServerChatDeliveryState delivery) =>
        new()
        {
            MessageId = source.MessageId,
            ClientMessageId = source.ClientMessageId,
            SenderName = source.SenderName,
            Text = source.Text,
            Sequence = source.Sequence,
            IsLocal = source.IsLocal,
            Delivery = delivery,
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
