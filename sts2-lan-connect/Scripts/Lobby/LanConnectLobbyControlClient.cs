using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal sealed class LobbyControlClient : IAsyncDisposable
{
    private const int NotConnected = 0;
    private const int Connected = 1;
    private const int Terminal = 2;
    private static readonly LanConnectChatFeatureVersions CurrentRoomChatVersions =
        new(1, 1, 1, 1);

    private readonly LanConnectWebSocketTransport _transport;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _lifecycleLock = new();
    private readonly Action<string> _warningSink;
    private readonly Action? _beforeRoomChatCommit;
    private readonly TimeSpan _closeTimeout;
    private string _role = "host";
    private string _activeRoomId = string.Empty;
    private string _activeRoomSessionId = string.Empty;
    private LanConnectRoomChatReadyEnvelope? _latestRoomChatReady;
    private Task? _disposeTask;
    private int _lifecycleState;

    public LobbyControlClient()
        : this(new LanConnectClientWebSocket())
    {
    }

    internal LobbyControlClient(
        ILanConnectWebSocket socket,
        Action<string>? warningSink = null,
        TimeSpan? closeTimeout = null,
        Action? beforeRoomChatCommit = null)
    {
        TimeSpan configuredCloseTimeout = closeTimeout ?? TimeSpan.FromSeconds(2);
        if (configuredCloseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(closeTimeout), "Close timeout must be positive.");
        }

        _transport = new LanConnectWebSocketTransport(socket ?? throw new ArgumentNullException(nameof(socket)));
        _warningSink = warningSink ?? SafeLogWarn;
        _beforeRoomChatCommit = beforeRoomChatCommit;
        _closeTimeout = configuredCloseTimeout;
        _transport.PayloadReceived += OnPayloadReceived;
        _transport.Faulted += OnTransportFaulted;
        _transport.Closed += OnTransportClosed;
    }

    public bool IsConnected => Volatile.Read(ref _lifecycleState) == Connected;

    public event Action<LobbyControlEnvelope>? EnvelopeReceived;

    internal event Action<LanConnectRoomChatAckEnvelope>? RoomChatAckReceived;

    internal event Action<LanConnectRoomChatReadyEnvelope>? RoomChatReadyReceived;

    internal event Action<LanConnectRoomChatMessageEnvelope>? RoomChatMessageReceived;

    internal event Action<LanConnectRoomChatErrorEnvelope>? RoomChatErrorReceived;

    internal event Action? Disconnected;

    internal LanConnectRoomChatReadyEnvelope? LatestRoomChatReady
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _latestRoomChatReady;
            }
        }
    }

    internal void HandlePayloadForTests(string payload) => OnPayloadReceived(payload);

    internal void HandleTransportClosedForTests() => OnTransportClosed();

    internal void HandleTransportFaultedForTests(Exception exception) => OnTransportFaulted(exception);

    public Task ConnectHostAsync(
        Uri controlUri,
        string roomId,
        string controlChannelId,
        string playerName,
        CancellationToken cancellationToken) =>
        ConnectHostCoreAsync(
            controlUri,
            roomId,
            controlChannelId,
            playerName,
            playerNetId: null,
            roomSessionId: null,
            declareRich: false,
            cancellationToken);

    internal Task ConnectHostAsync(
        Uri controlUri,
        string roomId,
        string controlChannelId,
        string playerName,
        string? roomSessionId,
        CancellationToken cancellationToken) =>
        ConnectHostCoreAsync(
            controlUri,
            roomId,
            controlChannelId,
            playerName,
            playerNetId: null,
            roomSessionId,
            declareRich: false,
            cancellationToken);

    internal Task ConnectHostAsync(
        Uri controlUri,
        string roomId,
        string controlChannelId,
        string playerName,
        string playerNetId,
        string? roomSessionId,
        CancellationToken cancellationToken) =>
        ConnectHostCoreAsync(
            controlUri,
            roomId,
            controlChannelId,
            playerName,
            playerNetId,
            roomSessionId,
            declareRich: !string.IsNullOrWhiteSpace(roomSessionId),
            cancellationToken);

    private Task ConnectHostCoreAsync(
        Uri controlUri,
        string roomId,
        string controlChannelId,
        string playerName,
        string? playerNetId,
        string? roomSessionId,
        bool declareRich,
        CancellationToken cancellationToken)
    {
        _role = "host";
        SetActiveRoom(roomId, roomSessionId);
        return ConnectAsync(controlUri, new LobbyControlEnvelope
        {
            Type = "host_hello",
            RoomId = roomId,
            ControlChannelId = controlChannelId,
            Role = "host",
            PlayerNetId = declareRich ? playerNetId : null,
            PlayerName = playerName,
            RoomChatVersions = declareRich ? CurrentRoomChatVersions : null
        }, cancellationToken);
    }

    public Task ConnectClientAsync(
        Uri controlUri,
        string roomId,
        string controlChannelId,
        string ticketId,
        string playerName,
        string playerNetId,
        CancellationToken cancellationToken) =>
        ConnectClientCoreAsync(
            controlUri,
            roomId,
            controlChannelId,
            ticketId,
            playerName,
            playerNetId,
            roomSessionId: null,
            declareRich: false,
            cancellationToken);

    internal Task ConnectClientAsync(
        Uri controlUri,
        string roomId,
        string controlChannelId,
        string ticketId,
        string playerName,
        string playerNetId,
        string? roomSessionId,
        CancellationToken cancellationToken) =>
        ConnectClientCoreAsync(
            controlUri,
            roomId,
            controlChannelId,
            ticketId,
            playerName,
            playerNetId,
            roomSessionId,
            declareRich: !string.IsNullOrWhiteSpace(roomSessionId),
            cancellationToken);

    private Task ConnectClientCoreAsync(
        Uri controlUri,
        string roomId,
        string controlChannelId,
        string ticketId,
        string playerName,
        string playerNetId,
        string? roomSessionId,
        bool declareRich,
        CancellationToken cancellationToken)
    {
        _role = "client";
        SetActiveRoom(roomId, roomSessionId);
        return ConnectAsync(controlUri, new LobbyControlEnvelope
        {
            Type = "client_hello",
            RoomId = roomId,
            ControlChannelId = controlChannelId,
            Role = "client",
            TicketId = declareRich ? null : ticketId,
            PlayerName = playerName,
            PlayerNetId = playerNetId,
            RoomChatVersions = declareRich ? CurrentRoomChatVersions : null
        }, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    public Task SendAsync(LobbyControlEnvelope envelope, CancellationToken cancellationToken = default)
    {
        string payload = JsonSerializer.Serialize(envelope, LanConnectJson.Options);
        return _transport.SendAsync(payload, cancellationToken);
    }

    internal Task SendRoomChatV2Async(
        LanConnectRoomChatV2Envelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_lifecycleLock)
        {
            LanConnectRoomChatReadyEnvelope? ready = _latestRoomChatReady;
            if (Volatile.Read(ref _lifecycleState) != Connected ||
                ready == null ||
                !MatchesActiveRoom(envelope.RoomId, envelope.RoomSessionId) ||
                !LanConnectRoomChatSessionContext.ContentMatches(envelope.Content, _activeRoomSessionId) ||
                !LanConnectChatFeatureResolver.SupportsContent(envelope.Content, ready.EnabledFeatures))
            {
                throw new InvalidOperationException("The rich room chat envelope does not match the active room session.");
            }
        }
        string payload = JsonSerializer.Serialize(envelope, LanConnectJson.Options);
        return _transport.SendAsync(payload, cancellationToken);
    }

    private async Task ConnectAsync(Uri controlUri, LobbyControlEnvelope helloEnvelope, CancellationToken cancellationToken)
    {
        string helloPayload = JsonSerializer.Serialize(helloEnvelope, LanConnectJson.Options);
        try
        {
            await _transport.ConnectAsync(
                controlUri,
                requestHeaders: null,
                helloPayload,
                PublishConnected,
                cancellationToken,
                _lifetimeCancellation.Token);
            if (!IsConnected)
            {
                throw new InvalidOperationException("The lobby control channel closed before connection completed.");
            }
        }
        catch
        {
            Interlocked.Exchange(ref _lifecycleState, Terminal);
            try
            {
                await DisposeAsync();
            }
            catch
            {
            }
            throw;
        }
    }

    private void PublishConnected()
    {
        if (Interlocked.CompareExchange(ref _lifecycleState, Connected, NotConnected) != NotConnected)
        {
            throw new InvalidOperationException("The lobby control channel closed before connection completed.");
        }
    }

    private async Task DisposeCoreAsync()
    {
        int previousState;
        lock (_lifecycleLock)
        {
            ClearRoomChatReadyLocked();
            previousState = Interlocked.Exchange(ref _lifecycleState, Terminal);
        }
        if (previousState == Connected)
        {
            try
            {
                using CancellationTokenSource closeCancellation =
                    new(_closeTimeout);
                await _transport.CloseAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                    "client_shutdown",
                    closeCancellation.Token);
            }
            catch
            {
            }
        }

        _lifetimeCancellation.Cancel();
        _transport.PayloadReceived -= OnPayloadReceived;
        _transport.Faulted -= OnTransportFaulted;
        _transport.Closed -= OnTransportClosed;
        try
        {
            await _transport.DisposeAsync();
        }
        finally
        {
            lock (_lifecycleLock)
            {
                ClearRoomChatReadyLocked();
            }
            _lifetimeCancellation.Dispose();
        }
    }

    private void OnPayloadReceived(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            if (TryHandleRichRoomPayload(payload))
            {
                return;
            }
            LobbyControlEnvelope? envelope =
                JsonSerializer.Deserialize<LobbyControlEnvelope>(payload, LanConnectJson.Options);
            if (envelope == null)
            {
                return;
            }

            if (envelope.Type == "ping")
            {
                Task send = SendAsync(new LobbyControlEnvelope
                {
                    Type = "pong",
                    RoomId = envelope.RoomId,
                    ControlChannelId = envelope.ControlChannelId,
                    Role = _role
                });
                if (!send.IsCompletedSuccessfully)
                {
                    _ = ObservePongSendAsync(send);
                }
                return;
            }

            EnvelopeReceived?.Invoke(envelope);
        }
        catch (Exception ex)
        {
            TryWarn($"sts2_lan_connect failed to parse control payload: {ex.Message}");
        }
    }

    private bool TryHandleRichRoomPayload(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("type", out JsonElement typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        string? type = typeElement.GetString();
        try
        {
            switch (type)
            {
                case "room_chat_ready":
                    LanConnectRoomChatReadyEnvelope? ready =
                        JsonSerializer.Deserialize<LanConnectRoomChatReadyEnvelope>(payload, LanConnectJson.Options);
                    _beforeRoomChatCommit?.Invoke();
                    bool readyAccepted;
                    lock (_lifecycleLock)
                    {
                        readyAccepted = Volatile.Read(ref _lifecycleState) == Connected &&
                            ready?.ProtocolVersion == 1 &&
                            string.Equals(ready.RoomId, _activeRoomId, StringComparison.Ordinal) &&
                            MatchesActiveRoom(ready.RoomId, ready.RoomSessionId);
                        if (readyAccepted)
                        {
                            _latestRoomChatReady = ready;
                            RoomChatReadyReceived?.Invoke(ready!);
                        }
                    }
                    if (!readyAccepted)
                    {
                        WarnStale("room_chat_ready");
                    }
                    return true;
                case "room_chat_ack":
                    LanConnectRoomChatAckEnvelope? ack =
                        JsonSerializer.Deserialize<LanConnectRoomChatAckEnvelope>(payload, LanConnectJson.Options);
                    _beforeRoomChatCommit?.Invoke();
                    bool ackAccepted;
                    lock (_lifecycleLock)
                    {
                        ackAccepted = Volatile.Read(ref _lifecycleState) == Connected &&
                            ack?.ProtocolVersion == 1 &&
                            MatchesActiveRoom(ack.Message.RoomId, ack.Message.RoomSessionId) &&
                            LanConnectRoomChatSessionContext.ContentMatches(
                                ack.Message.Content,
                                _activeRoomSessionId) &&
                            _latestRoomChatReady != null &&
                            LanConnectChatFeatureResolver.SupportsContent(
                                ack.Message.Content,
                                _latestRoomChatReady.EnabledFeatures);
                        if (ackAccepted)
                        {
                            RoomChatAckReceived?.Invoke(ack!);
                        }
                    }
                    if (!ackAccepted)
                    {
                        WarnStale("room_chat_ack");
                    }
                    return true;
                case "room_chat_message":
                    LanConnectRoomChatMessageEnvelope? message =
                        JsonSerializer.Deserialize<LanConnectRoomChatMessageEnvelope>(payload, LanConnectJson.Options);
                    _beforeRoomChatCommit?.Invoke();
                    bool messageAccepted;
                    lock (_lifecycleLock)
                    {
                        messageAccepted = Volatile.Read(ref _lifecycleState) == Connected &&
                            message?.ProtocolVersion == 1 &&
                            MatchesActiveRoom(message.Message.RoomId, message.Message.RoomSessionId) &&
                            LanConnectRoomChatSessionContext.ContentMatches(
                                message.Message.Content,
                                _activeRoomSessionId) &&
                            _latestRoomChatReady != null &&
                            LanConnectChatFeatureResolver.SupportsContent(
                                message.Message.Content,
                                _latestRoomChatReady.EnabledFeatures);
                        if (messageAccepted)
                        {
                            RoomChatMessageReceived?.Invoke(message!);
                        }
                    }
                    if (!messageAccepted)
                    {
                        WarnStale("room_chat_message");
                    }
                    return true;
                case "room_chat_error":
                    LanConnectRoomChatErrorEnvelope? error =
                        JsonSerializer.Deserialize<LanConnectRoomChatErrorEnvelope>(payload, LanConnectJson.Options);
                    _beforeRoomChatCommit?.Invoke();
                    lock (_lifecycleLock)
                    {
                        if (Volatile.Read(ref _lifecycleState) == Connected &&
                            error?.ProtocolVersion == 1 &&
                            _latestRoomChatReady != null)
                        {
                            RoomChatErrorReceived?.Invoke(error);
                        }
                    }
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException exception)
        {
            TryWarn($"sts2_lan_connect ignored invalid rich room chat payload: {exception.Message}");
            return true;
        }
    }

    private void SetActiveRoom(string roomId, string? roomSessionId)
    {
        lock (_lifecycleLock)
        {
            _activeRoomId = roomId ?? string.Empty;
            _activeRoomSessionId = roomSessionId ?? string.Empty;
            ClearRoomChatReadyLocked();
        }
    }

    private bool MatchesActiveRoom(string roomId, string roomSessionId) =>
        !string.IsNullOrEmpty(_activeRoomId) &&
        !string.IsNullOrEmpty(_activeRoomSessionId) &&
        string.Equals(roomId, _activeRoomId, StringComparison.Ordinal) &&
        string.Equals(roomSessionId, _activeRoomSessionId, StringComparison.Ordinal);

    private void ClearRoomChatReadyLocked() => _latestRoomChatReady = null;

    private void WarnStale(string type) =>
        TryWarn($"sts2_lan_connect ignored stale {type} for inactive room generation.");

    private async Task ObservePongSendAsync(Task send)
    {
        try
        {
            await send;
        }
        catch (Exception ex) when (Volatile.Read(ref _lifecycleState) != Terminal)
        {
            TryWarn($"sts2_lan_connect failed to send lobby control pong: {ex.Message}");
        }
        catch
        {
        }
    }

    private void OnTransportFaulted(Exception exception)
    {
        int previousState;
        lock (_lifecycleLock)
        {
            ClearRoomChatReadyLocked();
            previousState = Interlocked.Exchange(ref _lifecycleState, Terminal);
        }
        TryWarn($"sts2_lan_connect lobby control channel receive loop stopped: {exception.Message}");
        if (previousState == Connected)
        {
            Disconnected?.Invoke();
        }
    }

    private void OnTransportClosed()
    {
        int previousState;
        lock (_lifecycleLock)
        {
            ClearRoomChatReadyLocked();
            previousState = Interlocked.Exchange(ref _lifecycleState, Terminal);
        }
        if (previousState == Connected)
        {
            Disconnected?.Invoke();
        }
    }

    private void TryWarn(string message)
    {
        try
        {
            _warningSink(message);
        }
        catch
        {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SafeLogWarn(string message) => Log.Warn(message);
}
