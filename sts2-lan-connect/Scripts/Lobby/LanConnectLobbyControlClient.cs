using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal sealed class LobbyControlClient : IAsyncDisposable
{
    private readonly LanConnectWebSocketTransport _transport;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _lifecycleLock = new();
    private string _role = "host";
    private Task? _disposeTask;
    private int _isConnected;
    private int _disposed;

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

    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

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
        Interlocked.Exchange(ref _isConnected, 1);
        await SendAsync(helloEnvelope, cancellationToken);
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        if (IsConnected)
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

        Interlocked.Exchange(ref _isConnected, 0);
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
        catch (Exception ex) when (Volatile.Read(ref _disposed) == 0)
        {
            LogPongSendFailure(ex);
        }
        catch
        {
        }
    }

    private void OnTransportFaulted(Exception exception)
    {
        Interlocked.Exchange(ref _isConnected, 0);
        LogTransportFailure(exception);
    }

    private void OnTransportClosed()
    {
        Interlocked.Exchange(ref _isConnected, 0);
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
