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

    private readonly LanConnectWebSocketTransport _transport;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _lifecycleLock = new();
    private readonly Action<string> _warningSink;
    private readonly TimeSpan _closeTimeout;
    private string _role = "host";
    private Task? _disposeTask;
    private int _lifecycleState;

    public LobbyControlClient()
        : this(new LanConnectClientWebSocket())
    {
    }

    internal LobbyControlClient(
        ILanConnectWebSocket socket,
        Action<string>? warningSink = null,
        TimeSpan? closeTimeout = null)
    {
        TimeSpan configuredCloseTimeout = closeTimeout ?? TimeSpan.FromSeconds(2);
        if (configuredCloseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(closeTimeout), "Close timeout must be positive.");
        }

        _transport = new LanConnectWebSocketTransport(socket ?? throw new ArgumentNullException(nameof(socket)));
        _warningSink = warningSink ?? SafeLogWarn;
        _closeTimeout = configuredCloseTimeout;
        _transport.PayloadReceived += OnPayloadReceived;
        _transport.Faulted += OnTransportFaulted;
        _transport.Closed += OnTransportClosed;
    }

    public bool IsConnected => Volatile.Read(ref _lifecycleState) == Connected;

    public event Action<LobbyControlEnvelope>? EnvelopeReceived;

    public async Task ConnectHostAsync(Uri controlUri, string roomId, string controlChannelId, string playerName, CancellationToken cancellationToken)
    {
        _role = "host";
        await ConnectAsync(controlUri, new LobbyControlEnvelope
        {
            Type = "host_hello",
            RoomId = roomId,
            ControlChannelId = controlChannelId,
            Role = "host",
            PlayerName = playerName
        }, cancellationToken);
    }

    public async Task ConnectClientAsync(
        Uri controlUri,
        string roomId,
        string controlChannelId,
        string ticketId,
        string playerName,
        string playerNetId,
        CancellationToken cancellationToken)
    {
        _role = "client";
        await ConnectAsync(controlUri, new LobbyControlEnvelope
        {
            Type = "client_hello",
            RoomId = roomId,
            ControlChannelId = controlChannelId,
            Role = "client",
            TicketId = ticketId,
            PlayerName = playerName,
            PlayerNetId = playerNetId
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
        int previousState = Interlocked.Exchange(ref _lifecycleState, Terminal);
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
        Interlocked.Exchange(ref _lifecycleState, Terminal);
        TryWarn($"sts2_lan_connect lobby control channel receive loop stopped: {exception.Message}");
    }

    private void OnTransportClosed()
    {
        Interlocked.Exchange(ref _lifecycleState, Terminal);
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
