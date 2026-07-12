using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal interface ILanConnectWebSocket : IAsyncDisposable
{
    WebSocketState State { get; }

    void SetRequestHeader(string headerName, string headerValue);

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken);

    Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken);
}

internal sealed class LanConnectClientWebSocket : ILanConnectWebSocket
{
    private readonly ClientWebSocket _socket = new();

    public WebSocketState State => _socket.State;

    public void SetRequestHeader(string headerName, string headerValue) => _socket.Options.SetRequestHeader(headerName, headerValue);

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) => _socket.ConnectAsync(uri, cancellationToken);

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _socket.ReceiveAsync(buffer, cancellationToken);

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken) =>
        _socket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) =>
        _socket.CloseAsync(closeStatus, statusDescription, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class LanConnectWebSocketTransport : IAsyncDisposable
{
    private const int MaxPayloadBytes = 8192;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ILanConnectWebSocket _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _receiveCancellation;
    private Task _receiveLoop = Task.CompletedTask;
    private Task? _disposeTask;
    private int _connectStarted;
    private int _disposed;

    public LanConnectWebSocketTransport()
        : this(new LanConnectClientWebSocket())
    {
    }

    internal LanConnectWebSocketTransport(ILanConnectWebSocket socket)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }

    public event Action<string>? PayloadReceived;

    public event Action? Closed;

    public async Task ConnectAsync(
        Uri uri,
        IReadOnlyDictionary<string, string>? requestHeaders = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref _connectStarted, 1, 0) != 0)
        {
            throw new InvalidOperationException("The WebSocket transport can only be connected once.");
        }

        try
        {
            if (requestHeaders != null)
            {
                foreach ((string name, string value) in requestHeaders)
                {
                    _socket.SetRequestHeader(name, value);
                }
            }

            await _socket.ConnectAsync(uri, cancellationToken);
            ThrowIfDisposed();
            _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
            _receiveLoop = ReceiveLoopAsync(_receiveCancellation.Token);
        }
        catch
        {
            Interlocked.Exchange(ref _connectStarted, 0);
            throw;
        }
    }

    public async Task SendAsync(string payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ThrowIfDisposed();
        byte[] bytes = StrictUtf8.GetBytes(payload);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("The WebSocket transport is not connected.");
            }

            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] receiveBuffer = new byte[4096];
        ArrayBufferWriter<byte> messageBuffer = new();

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived))
            {
                ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(receiveBuffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CompleteRemoteCloseAsync(cancellationToken);
                    RaiseClosed();
                    return;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    await CloseForProtocolErrorAsync(WebSocketCloseStatus.InvalidMessageType, "text_messages_only", cancellationToken);
                    return;
                }

                if (messageBuffer.WrittenCount + result.Count > MaxPayloadBytes)
                {
                    await CloseForProtocolErrorAsync(WebSocketCloseStatus.MessageTooBig, "message_too_big", cancellationToken);
                    return;
                }

                messageBuffer.Write(receiveBuffer.AsSpan(0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                string payload;
                try
                {
                    payload = StrictUtf8.GetString(messageBuffer.WrittenSpan);
                }
                catch (DecoderFallbackException)
                {
                    await CloseForProtocolErrorAsync(WebSocketCloseStatus.InvalidPayloadData, "invalid_utf8", cancellationToken);
                    return;
                }

                RaisePayload(payload);
                messageBuffer.Clear();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
        }
    }

    private async Task CompleteRemoteCloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State == WebSocketState.CloseReceived || _socket.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "remote_close", cancellationToken);
        }
    }

    private async Task CloseForProtocolErrorAsync(
        WebSocketCloseStatus closeStatus,
        string description,
        CancellationToken cancellationToken)
    {
        if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
        {
            await _socket.CloseAsync(closeStatus, LimitCloseDescription(description), cancellationToken);
        }

        RaiseClosed();
    }

    private void RaisePayload(string payload)
    {
        Action<string>? handlers = PayloadReceived;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(payload);
            }
            catch
            {
            }
        }
    }

    private void RaiseClosed()
    {
        Action? handlers = Closed;
        if (handlers == null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch
            {
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Cancel();
        try
        {
            await _receiveLoop;
        }
        catch (OperationCanceledException)
        {
        }

        await _sendLock.WaitAsync();
        try
        {
            await _socket.DisposeAsync();
        }
        finally
        {
            _sendLock.Release();
            _receiveCancellation?.Dispose();
            _disposeCancellation.Dispose();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static string LimitCloseDescription(string description)
    {
        if (StrictUtf8.GetByteCount(description) <= 123)
        {
            return description;
        }

        int length = description.Length;
        while (length > 0 && StrictUtf8.GetByteCount(description.AsSpan(0, length)) > 123)
        {
            length--;
        }
        return description[..length];
    }
}
