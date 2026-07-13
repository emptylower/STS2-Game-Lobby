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
    private string _role = "host";
    private Task? _disposeTask;
    private int _lifecycleState;

    public LobbyControlClient()
        : this(new LanConnectClientWebSocket())
    {
    }

    internal LobbyControlClient(ILanConnectWebSocket socket)
    {
        _transport = new LanConnectWebSocketTransport(socket ?? throw new ArgumentNullException(nameof(socket)));
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
        await _transport.ConnectAsync(
            controlUri,
            requestHeaders: null,
            cancellationToken,
            _lifetimeCancellation.Token);
        if (Interlocked.CompareExchange(ref _lifecycleState, Connected, NotConnected) != NotConnected)
        {
            throw new InvalidOperationException("The lobby control channel closed before connection completed.");
        }
        await SendAsync(helloEnvelope, cancellationToken);
    }

    private async Task DisposeCoreAsync()
    {
        int previousState = Interlocked.Exchange(ref _lifecycleState, Terminal);
        if (previousState == Connected)
        {
            try
            {
                using CancellationTokenSource closeCancellation =
                    new(TimeSpan.FromSeconds(2));
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
            LogParseFailure(ex);
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
            LogPongSendFailure(ex);
        }
        catch
        {
        }
    }

    private void OnTransportFaulted(Exception exception)
    {
        Interlocked.Exchange(ref _lifecycleState, Terminal);
        LogTransportFailure(exception);
    }

    private void OnTransportClosed()
    {
        Interlocked.Exchange(ref _lifecycleState, Terminal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LogParseFailure(Exception exception) =>
        Log.Warn($"sts2_lan_connect failed to parse control payload: {exception.Message}");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LogPongSendFailure(Exception exception) =>
        Log.Warn($"sts2_lan_connect failed to send lobby control pong: {exception.Message}");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LogTransportFailure(Exception exception) =>
        Log.Warn($"sts2_lan_connect lobby control channel receive loop stopped: {exception.Message}");
}
