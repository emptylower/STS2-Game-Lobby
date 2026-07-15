using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal interface ILanConnectServerChatApi : IDisposable
{
    Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken);

    Task<ServerChatTicketResponse> CreateServerChatTicketAsync(
        ServerChatTicketRequest request,
        CancellationToken cancellationToken);
}

internal interface ILanConnectServerChatTransport : IAsyncDisposable
{
    event Action<string>? PayloadReceived;

    event Action<Exception>? Faulted;

    event Action? Closed;

    Task ConnectAsync(
        Uri uri,
        IReadOnlyDictionary<string, string>? requestHeaders,
        CancellationToken connectCancellationToken,
        CancellationToken receiveLifetimeCancellationToken);

    Task SendAsync(string payload, CancellationToken cancellationToken);
}

internal enum LanConnectServerChatClientCheckpoint
{
    ReconnectBeforeDispose,
    ReconnectAfterConnectAttempt,
    ReconnectBeforeIdleHandoff,
    PayloadAfterCurrentConnectionValidation,
    PermanentStopAfterCleanupOwnership,
    PermanentCleanupBeforeDispose
}

internal sealed class LanConnectServerChatClient : ILanConnectServerChatClient
{
    private const int ProtocolVersion = 1;
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<Uri, ILanConnectServerChatApi> _apiFactory;
    private readonly Func<ILanConnectServerChatTransport> _transportFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<double> _random;
    private readonly Func<Guid> _uuidFactory;
    private readonly Action<LanConnectServerChatClientCheckpoint>? _checkpoint;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _resourceGate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private readonly object _timeoutLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _deliveryTimeouts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoredMessage> _messagesById = new(StringComparer.Ordinal);
    private ILanConnectServerChatApi? _api;
    private ILanConnectServerChatTransport? _transport;
    private Action<string>? _transportPayloadHandler;
    private Action<Exception>? _transportFaultHandler;
    private Action? _transportClosedHandler;
    private string _playerName = string.Empty;
    private string _playerNetId = string.Empty;
    private Uri? _lobbyBaseUri;
    private Task? _stopTask;
    private Task? _reconnectTask;
    private Task? _permanentStopCleanupTask;
    private int _backoffAttempt;
    private int _reconnectRequested;
    private int _lifecycleGeneration;
    private int _connectStarted;
    private int _readySeen;
    private int _snapshotComplete;
    private int _permanentlyStopped;
    private int _disposed;
    private LanConnectChatFeatureVersions _enabledFeatures = new();
    private long _connectionOrdinal;
    private long _currentConnectionOrdinal;
    private long _activeSessionOrdinal;
    private long _reconnectLoopOrdinal;
    private long _activeReconnectLoopOrdinal;

    internal LanConnectServerChatClient()
        : this(
            uri => new ServerChatApiAdapter(new LobbyApiClient(uri.AbsoluteUri)),
            () => new ServerChatTransportAdapter(new LanConnectWebSocketTransport()),
            () => DateTimeOffset.UtcNow,
            Task.Delay,
            Random.Shared.NextDouble,
            Guid.NewGuid)
    {
    }

    internal LanConnectServerChatClient(
        Func<Uri, ILanConnectServerChatApi> apiFactory,
        Func<ILanConnectServerChatTransport> transportFactory,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<double>? random = null,
        Func<Guid>? uuidFactory = null,
        Action<LanConnectServerChatClientCheckpoint>? checkpoint = null)
    {
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
        _random = random ?? Random.Shared.NextDouble;
        _uuidFactory = uuidFactory ?? Guid.NewGuid;
        _checkpoint = checkpoint;
        State = new LanConnectChatChannelState(LanConnectChatChannel.Server);
    }

    internal LanConnectChatChannelState State { get; }

    internal bool CanSend =>
        Volatile.Read(ref _readySeen) != 0 &&
        Volatile.Read(ref _snapshotComplete) != 0 &&
        State.ChatEnabled &&
        Volatile.Read(ref _permanentlyStopped) == 0 &&
        Volatile.Read(ref _disposed) == 0 &&
        Volatile.Read(ref _connectStarted) != 0;

    internal bool IsPermanentlyStopped => Volatile.Read(ref _permanentlyStopped) != 0;

    internal event Action? StateChanged;

    LanConnectChatChannelState ILanConnectServerChatClient.State => State;

    event Action? ILanConnectServerChatClient.StateChanged
    {
        add => StateChanged += value;
        remove => StateChanged -= value;
    }

    Task ILanConnectServerChatClient.ConnectAsync(
        Uri lobbyBaseUri,
        string playerNetId,
        string playerName,
        CancellationToken cancellationToken) =>
        ConnectAsync(lobbyBaseUri, playerNetId, playerName, cancellationToken);

    Task ILanConnectServerChatClient.SendTextAsync(string text, CancellationToken cancellationToken) =>
        SendTextAsync(text, cancellationToken);

    Task ILanConnectServerChatClient.SendAsync(
        LanConnectChatContent content,
        string clientMessageId,
        CancellationToken cancellationToken) =>
        SendAsync(content, clientMessageId, cancellationToken);

    Task ILanConnectServerChatClient.RetryAsync(string clientMessageId, CancellationToken cancellationToken) =>
        RetryAsync(clientMessageId, cancellationToken);

    Task ILanConnectServerChatClient.StopAsync(CancellationToken cancellationToken) =>
        StopAsync(cancellationToken);

    private async Task ConnectAttemptAsync(int generation, CancellationToken cancellationToken)
    {
        if (generation != Volatile.Read(ref _lifecycleGeneration) || IsPermanentlyStopped)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        await _resourceGate.WaitAsync(cancellationToken);
        try
        {
            await DisposeCurrentResourcesCoreAsync();
            cancellationToken.ThrowIfCancellationRequested();
            Uri lobbyBaseUri = _lobbyBaseUri ?? throw new InvalidOperationException("Lobby base URI is unavailable.");
            ILanConnectServerChatApi api = _apiFactory(lobbyBaseUri);
            _api = api;
            LobbyProbeResponse probe = await api.GetProbeAsync(cancellationToken);
            if (!probe.Ok || probe.Capabilities == null || !probe.Capabilities.SupportsTextServerChat)
            {
                MarkPermanentStop(
                    LanConnectServerChatPresentation.Unsupported,
                    "当前服务器未声明频道聊天协议 v1。",
                    connectionOrdinal: null,
                    generation);
                throw new NotSupportedException("The selected lobby does not support server chat protocol version 1.");
            }

            ServerChatTicketResponse ticket = await api.CreateServerChatTicketAsync(new ServerChatTicketRequest
            {
                ProtocolVersion = ProtocolVersion,
                PlayerNetId = _playerNetId,
                PlayerName = _playerName
            }, cancellationToken);
            if (ticket.ProtocolVersion != ProtocolVersion)
            {
                MarkPermanentStop(
                    LanConnectServerChatPresentation.Unsupported,
                    "服务器返回了不兼容的频道聊天 ticket。",
                    connectionOrdinal: null,
                    generation);
                throw new NotSupportedException("The server chat ticket uses an unsupported protocol version.");
            }
            if (string.IsNullOrWhiteSpace(ticket.Ticket) ||
                !Uri.TryCreate(ticket.WebSocketUrl, UriKind.Absolute, out Uri? chatUri))
            {
                throw new InvalidOperationException("The server chat ticket response is incomplete.");
            }

            ILanConnectServerChatTransport transport = _transportFactory();
            _transport = transport;
            Volatile.Write(ref _readySeen, 0);
            Volatile.Write(ref _snapshotComplete, 0);
            long connectionOrdinal = Interlocked.Increment(ref _connectionOrdinal);
            Interlocked.Exchange(ref _currentConnectionOrdinal, connectionOrdinal);
            _transportPayloadHandler = payload => OnPayloadReceived(connectionOrdinal, generation, payload);
            _transportFaultHandler = exception => OnTransportFaulted(connectionOrdinal, generation, exception);
            _transportClosedHandler = () => OnTransportClosed(connectionOrdinal, generation);
            transport.PayloadReceived += _transportPayloadHandler;
            transport.Faulted += _transportFaultHandler;
            transport.Closed += _transportClosedHandler;
            await transport.ConnectAsync(
                chatUri,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Authorization"] = $"Bearer {ticket.Ticket}"
                },
                cancellationToken,
                _lifetimeCancellation.Token);
        }
        catch
        {
            await DisposeCurrentResourcesCoreAsync();
            throw;
        }
        finally
        {
            _resourceGate.Release();
        }
    }

    internal async Task ConnectAsync(
        Uri lobbyBaseUri,
        string playerNetId,
        string playerName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lobbyBaseUri);
        ArgumentNullException.ThrowIfNull(playerNetId);
        ArgumentNullException.ThrowIfNull(playerName);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.CompareExchange(ref _connectStarted, 1, 0) != 0)
        {
            throw new InvalidOperationException("The server chat client can only be connected once.");
        }

        _playerName = playerName.Normalize().Trim();
        _playerNetId = playerNetId.Trim();
        _lobbyBaseUri = lobbyBaseUri;
        int generation = Interlocked.Increment(ref _lifecycleGeneration);
        using CancellationTokenSource connectCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
        SetPresentation(LanConnectServerChatPresentation.Connecting);
        try
        {
            await ConnectAttemptAsync(generation, connectCancellation.Token);
        }
        catch (NotSupportedException ex)
        {
            Volatile.Write(ref _snapshotComplete, 0);
            if (!IsPermanentlyStopped)
            {
                SetPresentation(LanConnectServerChatPresentation.TransportFailure, ex.Message);
            }
            throw;
        }
        catch (OperationCanceledException) when (connectCancellation.IsCancellationRequested)
        {
            Volatile.Write(ref _snapshotComplete, 0);
            throw;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _snapshotComplete, 0);
            SetPresentation(LanConnectServerChatPresentation.TransportFailure, ex.Message);
            throw;
        }
    }

    internal async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        EnsureCanSend();
        LanConnectChatContent content = LanConnectServerChatProtocol.Canonicalize(
            new LanConnectChatContent(1, [new LanConnectTextSegment(text)]),
            _enabledFeatures);
        LanConnectServerChatProtocol.AssertInboundBudget(content, _playerName);
        string clientMessageId = _uuidFactory().ToString("D");
        await SendContentAsync(clientMessageId, content, rememberContent: true, cancellationToken);
    }

    internal async Task SendAsync(
        LanConnectChatContent content,
        string clientMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(clientMessageId);
        EnsureCanSend();
        LanConnectChatContent canonical = LanConnectServerChatProtocol.Canonicalize(content, _enabledFeatures);
        LanConnectServerChatProtocol.AssertInboundBudget(canonical, _playerName);
        await SendContentAsync(clientMessageId, canonical, rememberContent: true, cancellationToken);
    }

    internal Task RetryAsync(string clientMessageId, CancellationToken cancellationToken = default)
    {
        return RetryUnknownAsync(clientMessageId, startNewSession: false, cancellationToken);
    }

    internal async Task RetryUnknownAsync(
        string clientMessageId,
        bool startNewSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientMessageId);
        EnsureCanSend();
        StoredMessage stored;
        lock (_timeoutLock)
        {
            if (!_messagesById.TryGetValue(clientMessageId, out stored))
            {
                throw new InvalidOperationException("The server chat message is not available for retry.");
            }
        }
        if (!startNewSession && stored.SessionOrdinal != Volatile.Read(ref _activeSessionOrdinal))
        {
            throw new InvalidOperationException("A message ID cannot be reused across server chat sessions.");
        }

        LanConnectChatContent content = LanConnectServerChatProtocol.Canonicalize(
            stored.Content,
            _enabledFeatures);
        LanConnectServerChatProtocol.AssertInboundBudget(content, _playerName);
        string retryId = startNewSession ? _uuidFactory().ToString("D") : clientMessageId;
        await SendContentAsync(retryId, content, rememberContent: true, cancellationToken);
    }

    internal Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stop;
        lock (_lifecycleLock)
        {
            _stopTask ??= StopCoreAsync();
            stop = _stopTask;
        }
        return stop.WaitAsync(cancellationToken);
    }

    internal Task DisconnectAsync(CancellationToken cancellationToken = default) => StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            Task? existing;
            lock (_lifecycleLock)
            {
                existing = _stopTask;
            }
            if (existing != null)
            {
                await existing;
            }
            return;
        }

        await StopAsync();
        _sendGate.Dispose();
        _resourceGate.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private async Task SendContentAsync(
        string clientMessageId,
        LanConnectChatContent content,
        bool rememberContent,
        CancellationToken cancellationToken)
    {
        if (!CanSend)
        {
            throw new InvalidOperationException("Server chat is not ready for sending.");
        }

        string text = LanConnectServerChatProtocol.RenderGenericFallback(content);
        long revision = State.Revision;
        State.Queue(new ServerChatPendingMessage
        {
            ClientMessageId = clientMessageId,
            SenderName = _playerName,
            SenderNetId = _playerNetId,
            Text = text,
            Content = content,
            QueuedAt = _clock()
        });
        RaiseStateChangedIfNeeded(revision);
        if (rememberContent)
        {
            lock (_timeoutLock)
            {
                _messagesById[clientMessageId] = new StoredMessage(
                    content,
                    Volatile.Read(ref _activeSessionOrdinal));
            }
        }

        StartDeliveryTimeout(clientMessageId);
        string payload = JsonSerializer.Serialize(new LanConnectServerChatSendEnvelope
        {
            ProtocolVersion = ProtocolVersion,
            Channel = LanConnectChatChannel.Server,
            ClientMessageId = clientMessageId,
            Content = content
        }, LanConnectJson.Options);

        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            ILanConnectServerChatTransport transport = _transport ??
                throw new InvalidOperationException("Server chat transport is not connected.");
            await transport.SendAsync(payload, cancellationToken);
        }
        catch (Exception exception)
        {
            CancelDeliveryTimeout(clientMessageId);
            revision = State.Revision;
            State.MarkFailed(clientMessageId, "send_failed", exception.Message);
            RaiseStateChangedIfNeeded(revision);
            throw;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private CancellationTokenSource StartDeliveryTimeout(string clientMessageId)
    {
        CancellationTokenSource timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        lock (_timeoutLock)
        {
            if (_deliveryTimeouts.Remove(clientMessageId, out CancellationTokenSource? prior))
            {
                prior.Cancel();
            }
            _deliveryTimeouts[clientMessageId] = timeoutCancellation;
        }
        _ = ObserveDeliveryTimeoutAsync(clientMessageId, timeoutCancellation);
        return timeoutCancellation;
    }

    private async Task ObserveDeliveryTimeoutAsync(string clientMessageId, CancellationTokenSource timeoutCancellation)
    {
        try
        {
            await _delay(DeliveryTimeout, timeoutCancellation.Token);
            timeoutCancellation.Token.ThrowIfCancellationRequested();
            long revision = State.Revision;
            State.MarkTimedOut(_clock());
            RaiseStateChangedIfNeeded(revision);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_timeoutLock)
            {
                if (_deliveryTimeouts.TryGetValue(clientMessageId, out CancellationTokenSource? current) &&
                    ReferenceEquals(current, timeoutCancellation))
                {
                    _deliveryTimeouts.Remove(clientMessageId);
                }
            }
            timeoutCancellation.Dispose();
        }
    }

    private void OnPayloadReceived(long connectionOrdinal, int generation, string payload)
    {
        if (!IsCurrentConnection(connectionOrdinal, generation) ||
            string.IsNullOrWhiteSpace(payload) || IsPermanentlyStopped)
        {
            return;
        }
        _checkpoint?.Invoke(LanConnectServerChatClientCheckpoint.PayloadAfterCurrentConnectionValidation);

        try
        {
            using (JsonDocument discriminator = JsonDocument.Parse(payload))
            {
                JsonElement root = discriminator.RootElement;
                string? declaredType = root.TryGetProperty("type", out JsonElement typeElement) &&
                                       typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : null;
                if (declaredType is "chat_snapshot_begin" or "chat_snapshot_chunk" or "chat_snapshot_end")
                {
                    int declaredProtocol = root.TryGetProperty("protocolVersion", out JsonElement protocolElement) &&
                                           protocolElement.ValueKind == JsonValueKind.Number &&
                                           protocolElement.TryGetInt32(out int value)
                        ? value
                        : 0;
                    if (declaredProtocol != ProtocolVersion)
                    {
                        MarkPermanentStop(
                            LanConnectServerChatPresentation.TransportFailure,
                            "服务器返回了意外的频道聊天协议版本。",
                            connectionOrdinal,
                            generation);
                        return;
                    }
                    if (declaredType == "chat_snapshot_begin")
                    {
                        Volatile.Write(ref _snapshotComplete, 0);
                    }
                    long snapshotRevision = State.Revision;
                    _ = TryApplyCanonicalEnvelope(
                        payload,
                        declaredType,
                        out _,
                        out bool snapshotReconnectRequired);
                    if (snapshotReconnectRequired)
                    {
                        if (TryClaimCurrentConnection(connectionOrdinal, generation))
                        {
                            HandleUnexpectedDisconnect("Server chat snapshot was invalid.");
                        }
                        return;
                    }
                    RaiseStateChangedIfNeeded(snapshotRevision);
                    return;
                }
            }

            ServerChatInboundEnvelope? envelope =
                JsonSerializer.Deserialize<ServerChatInboundEnvelope>(payload, LanConnectJson.Options);
            if (envelope == null)
            {
                return;
            }
            if (envelope.ProtocolVersion != ProtocolVersion)
            {
                MarkPermanentStop(
                    LanConnectServerChatPresentation.TransportFailure,
                    "服务器返回了意外的频道聊天协议版本。",
                    connectionOrdinal,
                    generation);
                return;
            }
            if (envelope.Type == "chat_ready" &&
                (envelope.Channel != LanConnectChatChannel.Server || envelope.ServerChatVersion != ProtocolVersion))
            {
                MarkPermanentStop(
                    LanConnectServerChatPresentation.TransportFailure,
                    "服务器返回了无效的频道聊天 ready。",
                    connectionOrdinal,
                    generation);
                return;
            }
            if (envelope.Type == "chat_error" &&
                string.Equals(envelope.Code, "protocol_mismatch", StringComparison.Ordinal))
            {
                MarkPermanentStop(
                    LanConnectServerChatPresentation.Unsupported,
                    "服务器拒绝了当前频道聊天协议。",
                    connectionOrdinal,
                    generation);
                return;
            }

            if (envelope.Type == "chat_snapshot_begin")
            {
                Volatile.Write(ref _snapshotComplete, 0);
            }

            long revision = State.Revision;
            if (TryApplyCanonicalEnvelope(
                    payload,
                    envelope.Type,
                    out string? completedClientMessageId,
                    out bool reconnectRequired))
            {
                if (!string.IsNullOrEmpty(completedClientMessageId))
                {
                    CancelDeliveryTimeout(completedClientMessageId);
                }
                if (reconnectRequired)
                {
                    if (TryClaimCurrentConnection(connectionOrdinal, generation))
                    {
                        HandleUnexpectedDisconnect("Server chat snapshot was invalid.");
                    }
                    return;
                }
                RaiseStateChangedIfNeeded(revision);
                return;
            }
            LanConnectChatApplyResult result = State.Apply(envelope);
            if (result.ReconnectRequired)
            {
                if (TryClaimCurrentConnection(connectionOrdinal, generation))
                {
                    HandleUnexpectedDisconnect();
                }
                return;
            }
            if (envelope.Type == "chat_ready")
            {
                _enabledFeatures = envelope.EnabledFeatures == null
                    ? new LanConnectChatFeatureVersions()
                    : new LanConnectChatFeatureVersions(
                        envelope.EnabledFeatures.RichContentVersion,
                        envelope.EnabledFeatures.EmojiSetVersion,
                        envelope.EnabledFeatures.ItemRefVersion,
                        0);
                Volatile.Write(ref _readySeen, 1);
            }
            else if (envelope.Type == "chat_snapshot_end")
            {
                Volatile.Write(ref _snapshotComplete, 1);
                if (Volatile.Read(ref _readySeen) != 0)
                {
                    Interlocked.Exchange(ref _activeSessionOrdinal, Interlocked.Read(ref _currentConnectionOrdinal));
                    Interlocked.Exchange(ref _backoffAttempt, 0);
                    State.SetPresentation(
                        State.ChatEnabled
                            ? LanConnectServerChatPresentation.Ready
                            : LanConnectServerChatPresentation.Disabled);
                }
            }
            else if (envelope.Type == "chat_state" &&
                     Volatile.Read(ref _readySeen) != 0 &&
                     Volatile.Read(ref _snapshotComplete) != 0)
            {
                _enabledFeatures = envelope.EnabledFeatures == null
                    ? new LanConnectChatFeatureVersions()
                    : new LanConnectChatFeatureVersions(
                        envelope.EnabledFeatures.RichContentVersion,
                        envelope.EnabledFeatures.EmojiSetVersion,
                        envelope.EnabledFeatures.ItemRefVersion,
                        0);
                State.SetPresentation(
                    State.ChatEnabled
                        ? LanConnectServerChatPresentation.Ready
                        : LanConnectServerChatPresentation.Disabled);
            }
            RaiseStateChangedIfNeeded(revision);
        }
        catch (JsonException)
        {
            MarkPermanentStop(
                LanConnectServerChatPresentation.TransportFailure,
                "服务器返回了无法解析的频道聊天协议消息。",
                connectionOrdinal,
                generation);
        }
    }

    private void CancelDeliveryTimeout(string clientMessageId)
    {
        CancellationTokenSource? timeoutCancellation = null;
        lock (_timeoutLock)
        {
            if (_deliveryTimeouts.Remove(clientMessageId, out CancellationTokenSource? found))
            {
                timeoutCancellation = found;
            }
        }
        timeoutCancellation?.Cancel();
    }

    private bool TryApplyCanonicalEnvelope(
        string payload,
        string type,
        out string? completedClientMessageId,
        out bool reconnectRequired)
    {
        completedClientMessageId = null;
        reconnectRequired = false;
        try
        {
            switch (type)
            {
                case "chat_snapshot_begin":
                    LanConnectServerChatSnapshotBeginEnvelope? begin =
                        JsonSerializer.Deserialize<LanConnectServerChatSnapshotBeginEnvelope>(payload, LanConnectJson.Options);
                    if (begin == null || begin.ProtocolVersion != ProtocolVersion)
                    {
                        reconnectRequired = true;
                        return true;
                    }
                    reconnectRequired = State.Apply(begin).ReconnectRequired;
                    return true;
                case "chat_snapshot_chunk":
                    LanConnectServerChatSnapshotChunkEnvelope? chunk =
                        JsonSerializer.Deserialize<LanConnectServerChatSnapshotChunkEnvelope>(payload, LanConnectJson.Options);
                    if (chunk == null || chunk.ProtocolVersion != ProtocolVersion)
                    {
                        reconnectRequired = true;
                        return true;
                    }
                    reconnectRequired = State.Apply(chunk).ReconnectRequired;
                    return true;
                case "chat_snapshot_end":
                    LanConnectServerChatSnapshotEndEnvelope? end =
                        JsonSerializer.Deserialize<LanConnectServerChatSnapshotEndEnvelope>(payload, LanConnectJson.Options);
                    if (end == null || end.ProtocolVersion != ProtocolVersion)
                    {
                        reconnectRequired = true;
                        return true;
                    }
                    reconnectRequired = State.Apply(end).ReconnectRequired;
                    if (!reconnectRequired)
                    {
                        Volatile.Write(ref _snapshotComplete, 1);
                        if (Volatile.Read(ref _readySeen) != 0)
                        {
                            Interlocked.Exchange(ref _activeSessionOrdinal, Interlocked.Read(ref _currentConnectionOrdinal));
                            Interlocked.Exchange(ref _backoffAttempt, 0);
                            State.SetPresentation(
                                State.ChatEnabled
                                    ? LanConnectServerChatPresentation.Ready
                                    : LanConnectServerChatPresentation.Disabled);
                        }
                    }
                    return true;
                case "chat_ready":
                    LanConnectServerChatReadyEnvelope? ready =
                        JsonSerializer.Deserialize<LanConnectServerChatReadyEnvelope>(payload, LanConnectJson.Options);
                    if (ready == null ||
                        ready.ProtocolVersion != ProtocolVersion ||
                        ready.Channel != LanConnectChatChannel.Server ||
                        ready.ServerChatVersion != ProtocolVersion)
                    {
                        return true;
                    }
                    _enabledFeatures = ready.EnabledFeatures;
                    State.Apply(ready);
                    Volatile.Write(ref _readySeen, 1);
                    return true;
                case "chat_state":
                    LanConnectServerChatStateEnvelope? state =
                        JsonSerializer.Deserialize<LanConnectServerChatStateEnvelope>(payload, LanConnectJson.Options);
                    if (state == null || state.ProtocolVersion != ProtocolVersion)
                    {
                        return true;
                    }
                    _enabledFeatures = state.EnabledFeatures;
                    State.Apply(state);
                    if (Volatile.Read(ref _readySeen) != 0 &&
                        Volatile.Read(ref _snapshotComplete) != 0)
                    {
                        State.SetPresentation(
                            State.ChatEnabled
                                ? LanConnectServerChatPresentation.Ready
                                : LanConnectServerChatPresentation.Disabled);
                    }
                    return true;
                case "chat_ack":
                    LanConnectServerChatAckEnvelope? ack =
                        JsonSerializer.Deserialize<LanConnectServerChatAckEnvelope>(payload, LanConnectJson.Options);
                    if (ack == null || ack.ProtocolVersion != ProtocolVersion)
                    {
                        return true;
                    }
                    State.Apply(ack, _playerNetId);
                    if (IsClientMessageCompleted(ack.ClientMessageId))
                    {
                        completedClientMessageId = ack.ClientMessageId;
                    }
                    return true;
                case "chat_message":
                    LanConnectServerChatMessageEnvelope? message =
                        JsonSerializer.Deserialize<LanConnectServerChatMessageEnvelope>(payload, LanConnectJson.Options);
                    if (message == null || message.ProtocolVersion != ProtocolVersion)
                    {
                        return true;
                    }
                    State.Apply(message, _playerNetId);
                    return true;
                case "chat_error":
                    LanConnectServerChatErrorEnvelope? error =
                        JsonSerializer.Deserialize<LanConnectServerChatErrorEnvelope>(payload, LanConnectJson.Options);
                    if (error == null || error.ProtocolVersion != ProtocolVersion)
                    {
                        return true;
                    }
                    State.Apply(error);
                    if (IsClientMessageCompleted(error.ClientMessageId))
                    {
                        completedClientMessageId = error.ClientMessageId;
                    }
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            // A malformed non-snapshot rich envelope is isolated to that payload.
            // Snapshot corruption rejects the active assembly and reconnects.
            if (type is "chat_snapshot_begin" or "chat_snapshot_chunk" or "chat_snapshot_end")
            {
                State.RejectActiveSnapshot();
                reconnectRequired = true;
            }
            return true;
        }
    }

    private void CancelAllDeliveryTimeouts()
    {
        List<CancellationTokenSource> timeouts;
        lock (_timeoutLock)
        {
            timeouts = _deliveryTimeouts.Values.ToList();
            _deliveryTimeouts.Clear();
        }
        foreach (CancellationTokenSource timeout in timeouts)
        {
            timeout.Cancel();
        }
    }

    private bool IsClientMessageCompleted(string clientMessageId)
    {
        ServerChatMessageState? message = State.Messages.FirstOrDefault(entry =>
            string.Equals(entry.ClientMessageId, clientMessageId, StringComparison.Ordinal));
        return message?.Delivery is ServerChatDeliveryState.Confirmed or ServerChatDeliveryState.Failed;
    }

    private void OnTransportFaulted(long connectionOrdinal, int generation, Exception exception)
    {
        if (exception is NotSupportedException)
        {
            MarkPermanentStop(
                LanConnectServerChatPresentation.TransportFailure,
                exception.Message,
                connectionOrdinal,
                generation);
            return;
        }
        if (!TryClaimCurrentConnection(connectionOrdinal, generation))
        {
            return;
        }
        HandleUnexpectedDisconnect(exception.Message);
    }

    private void OnTransportClosed(long connectionOrdinal, int generation)
    {
        if (TryClaimCurrentConnection(connectionOrdinal, generation))
        {
            HandleUnexpectedDisconnect("频道连接已断开。");
        }
    }

    private bool IsCurrentConnection(long connectionOrdinal, int generation) =>
        connectionOrdinal != 0 &&
        connectionOrdinal == Interlocked.Read(ref _currentConnectionOrdinal) &&
        generation == Volatile.Read(ref _lifecycleGeneration);

    private bool TryClaimCurrentConnection(long connectionOrdinal, int generation) =>
        connectionOrdinal != 0 &&
        generation == Volatile.Read(ref _lifecycleGeneration) &&
        Interlocked.CompareExchange(ref _currentConnectionOrdinal, 0, connectionOrdinal) == connectionOrdinal;

    private void HandleUnexpectedDisconnect(string? reason = null)
    {
        MarkDisconnected();
        SetPresentation(LanConnectServerChatPresentation.TransportFailure, reason);
        ScheduleReconnect();
    }

    private void MarkDisconnected()
    {
        Volatile.Write(ref _snapshotComplete, 0);
        CancelAllDeliveryTimeouts();
        long revision = State.Revision;
        State.MarkTimedOut(_clock() + DeliveryTimeout);
        State.MarkDisconnected();
        RaiseStateChangedIfNeeded(revision);
    }

    private void MarkPermanentStop(
        LanConnectServerChatPresentation presentation,
        string reason,
        long? connectionOrdinal,
        int generation)
    {
        long revision;
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _permanentlyStopped) != 0 ||
                _stopTask != null ||
                Volatile.Read(ref _disposed) != 0 ||
                connectionOrdinal.HasValue && !IsCurrentConnection(connectionOrdinal.Value, generation) ||
                _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }
            Volatile.Write(ref _permanentlyStopped, 1);
            Volatile.Write(ref _readySeen, 0);
            Volatile.Write(ref _snapshotComplete, 0);
            _lifetimeCancellation.Cancel();
            _permanentStopCleanupTask = DisposeAfterPermanentStopAsync();
            revision = State.Revision;
            State.Apply(new ServerChatInboundEnvelope
            {
                Type = "chat_state",
                ProtocolVersion = ProtocolVersion,
                ChatEnabled = false,
                EnabledFeatures = new ServerChatEnabledFeatures()
            });
            State.SetPresentation(presentation, reason);
            _checkpoint?.Invoke(LanConnectServerChatClientCheckpoint.PermanentStopAfterCleanupOwnership);
        }
        RaiseStateChangedIfNeeded(revision);
    }

    private async Task DisposeAfterPermanentStopAsync()
    {
        await Task.Yield();
        _checkpoint?.Invoke(LanConnectServerChatClientCheckpoint.PermanentCleanupBeforeDispose);
        await DisposeCurrentResourcesAsync();
    }

    private void ScheduleReconnect()
    {
        lock (_lifecycleLock)
        {
            if (IsPermanentlyStopped || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }
            if (_activeReconnectLoopOrdinal != 0)
            {
                _reconnectRequested = 1;
                return;
            }
            int generation = Volatile.Read(ref _lifecycleGeneration);
            long loopOrdinal = ++_reconnectLoopOrdinal;
            _activeReconnectLoopOrdinal = loopOrdinal;
            _reconnectRequested = 0;
            _reconnectTask = ReconnectLoopAsync(loopOrdinal, generation, _lifetimeCancellation.Token);
        }
    }

    private async Task ReconnectLoopAsync(
        long loopOrdinal,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Yield();
            while (!cancellationToken.IsCancellationRequested &&
                   generation == Volatile.Read(ref _lifecycleGeneration) &&
                   !IsPermanentlyStopped)
            {
                SetPresentation(LanConnectServerChatPresentation.Reconnecting);
                _checkpoint?.Invoke(LanConnectServerChatClientCheckpoint.ReconnectBeforeDispose);
                await DisposeCurrentResourcesAsync();
                int attempt = Interlocked.Increment(ref _backoffAttempt) - 1;
                double baseSeconds = attempt switch
                {
                    0 => 1d,
                    1 => 2d,
                    2 => 4d,
                    3 => 8d,
                    _ => 15d
                };
                double random = Math.Clamp(_random(), 0d, 1d);
                TimeSpan reconnectDelay = TimeSpan.FromSeconds(baseSeconds * (0.8d + (random * 0.4d)));
                try
                {
                    await _delay(reconnectDelay, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    await ConnectAttemptAsync(generation, cancellationToken);
                    _checkpoint?.Invoke(LanConnectServerChatClientCheckpoint.ReconnectAfterConnectAttempt);
                    lock (_lifecycleLock)
                    {
                        _checkpoint?.Invoke(LanConnectServerChatClientCheckpoint.ReconnectBeforeIdleHandoff);
                        if (_reconnectRequested == 0)
                        {
                            _activeReconnectLoopOrdinal = 0;
                            _reconnectTask = null;
                            return;
                        }
                        _reconnectRequested = 0;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (NotSupportedException ex)
                {
                    if (!IsPermanentlyStopped)
                    {
                        MarkPermanentStop(
                            LanConnectServerChatPresentation.TransportFailure,
                            ex.Message,
                            connectionOrdinal: null,
                            generation);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    SetPresentation(LanConnectServerChatPresentation.TransportFailure, ex.Message);
                    lock (_lifecycleLock)
                    {
                        _reconnectRequested = 0;
                    }
                    // A failed reconnect attempt advances to the next capped delay.
                }
            }
        }
        finally
        {
            lock (_lifecycleLock)
            {
                if (_activeReconnectLoopOrdinal == loopOrdinal)
                {
                    _activeReconnectLoopOrdinal = 0;
                    _reconnectRequested = 0;
                    _reconnectTask = null;
                }
            }
        }
    }

    private void RaiseStateChangedIfNeeded(long revisionBefore)
    {
        if (State.Revision == revisionBefore)
        {
            return;
        }
        StateChanged?.Invoke();
    }

    private void SetPresentation(
        LanConnectServerChatPresentation presentation,
        string? detail = null)
    {
        long revision = State.Revision;
        State.SetPresentation(presentation, detail);
        RaiseStateChangedIfNeeded(revision);
    }

    private void EnsureCanSend()
    {
        if (!CanSend)
        {
            throw new InvalidOperationException("Server chat is not ready for sending.");
        }
    }

    private async Task DisposeCurrentResourcesAsync()
    {
        await _resourceGate.WaitAsync();
        try
        {
            await DisposeCurrentResourcesCoreAsync();
        }
        finally
        {
            _resourceGate.Release();
        }
    }

    private async Task DisposeCurrentResourcesCoreAsync()
    {
        ILanConnectServerChatTransport? transport = _transport;
        ILanConnectServerChatApi? api = _api;
        Action<string>? payloadHandler = _transportPayloadHandler;
        Action<Exception>? faultHandler = _transportFaultHandler;
        Action? closedHandler = _transportClosedHandler;
        _transport = null;
        _api = null;
        _transportPayloadHandler = null;
        _transportFaultHandler = null;
        _transportClosedHandler = null;
        Interlocked.Exchange(ref _currentConnectionOrdinal, 0);
        if (transport != null)
        {
            if (payloadHandler != null)
            {
                transport.PayloadReceived -= payloadHandler;
            }
            if (faultHandler != null)
            {
                transport.Faulted -= faultHandler;
            }
            if (closedHandler != null)
            {
                transport.Closed -= closedHandler;
            }
            await transport.DisposeAsync();
        }
        api?.Dispose();
    }

    private async Task StopCoreAsync()
    {
        Volatile.Write(ref _snapshotComplete, 0);
        Interlocked.Increment(ref _lifecycleGeneration);
        _lifetimeCancellation.Cancel();
        CancelAllDeliveryTimeouts();
        long revision = State.Revision;
        State.MarkTimedOut(_clock() + DeliveryTimeout);
        State.MarkDisconnected();
        RaiseStateChangedIfNeeded(revision);

        Task? reconnect;
        Task? permanentCleanup;
        lock (_lifecycleLock)
        {
            reconnect = _reconnectTask;
            permanentCleanup = _permanentStopCleanupTask;
        }
        if (reconnect != null)
        {
            try
            {
                await reconnect;
            }
            catch
            {
            }
        }
        if (permanentCleanup != null)
        {
            try
            {
                await permanentCleanup;
            }
            catch
            {
            }
        }
        await DisposeCurrentResourcesAsync();
    }

    private readonly record struct StoredMessage(LanConnectChatContent Content, long SessionOrdinal);

    private sealed class ServerChatApiAdapter(LobbyApiClient inner) : ILanConnectServerChatApi
    {
        public Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken) =>
            inner.GetProbeAsync(cancellationToken);

        public Task<ServerChatTicketResponse> CreateServerChatTicketAsync(
            ServerChatTicketRequest request,
            CancellationToken cancellationToken) =>
            inner.CreateServerChatTicketAsync(request, cancellationToken);

        public void Dispose() => inner.Dispose();
    }

    private sealed class ServerChatTransportAdapter(LanConnectWebSocketTransport inner) : ILanConnectServerChatTransport
    {
        public event Action<string>? PayloadReceived
        {
            add => inner.PayloadReceived += value;
            remove => inner.PayloadReceived -= value;
        }

        public event Action<Exception>? Faulted
        {
            add => inner.Faulted += value;
            remove => inner.Faulted -= value;
        }

        public event Action? Closed
        {
            add => inner.Closed += value;
            remove => inner.Closed -= value;
        }

        public Task ConnectAsync(
            Uri uri,
            IReadOnlyDictionary<string, string>? requestHeaders,
            CancellationToken connectCancellationToken,
            CancellationToken receiveLifetimeCancellationToken) =>
            inner.ConnectAsync(uri, requestHeaders, connectCancellationToken, receiveLifetimeCancellationToken);

        public Task SendAsync(string payload, CancellationToken cancellationToken) =>
            inner.SendAsync(payload, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
