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

    void Abort();
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

    public void Abort() => _socket.Abort();

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
    private Task _connectTask = Task.CompletedTask;
    private Task _receiveLoop = Task.CompletedTask;
    private Task? _disposeTask;
    private int _connectStarted;
    private int _closedRaised;
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

    public event Action<Exception>? Faulted;

    public event Action? Closed;

    public Task ConnectAsync(
        Uri uri,
        IReadOnlyDictionary<string, string>? requestHeaders = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        lock (_lifecycleLock)
        {
            ThrowIfDisposed();
            if (_connectStarted != 0)
            {
                throw new InvalidOperationException("The WebSocket transport can only be connected once.");
            }

            _connectStarted = 1;
            CancellationTokenSource receiveCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
            _receiveCancellation = receiveCancellation;

            TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectTask = ConnectCoreAsync(uri, requestHeaders, receiveCancellation, start.Task);
            start.SetResult();
            return _connectTask;
        }
    }

    public async Task SendAsync(string payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        byte[] bytes = StrictUtf8.GetBytes(payload);
        CancellationTokenSource sendCancellation;
        lock (_lifecycleLock)
        {
            ThrowIfDisposed();
            sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposeCancellation.Token);
        }

        using (sendCancellation)
        {
            await _sendLock.WaitAsync(sendCancellation.Token);
            try
            {
                ThrowIfDisposed();
                if (_socket.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("The WebSocket transport is not connected.");
                }

                await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, sendCancellation.Token);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (_disposeTask == null)
            {
                Interlocked.Exchange(ref _disposed, 1);
                _disposeCancellation.Cancel();
                try
                {
                    _socket.Abort();
                }
                catch
                {
                }
                _disposeTask = DisposeCoreAsync(_connectTask);
            }
            return new ValueTask(_disposeTask);
        }
    }

    private async Task ConnectCoreAsync(
        Uri uri,
        IReadOnlyDictionary<string, string>? requestHeaders,
        CancellationTokenSource receiveCancellation,
        Task start)
    {
        await start;
        if (requestHeaders != null)
        {
            foreach ((string name, string value) in requestHeaders)
            {
                _socket.SetRequestHeader(name, value);
            }
        }

        await _socket.ConnectAsync(uri, receiveCancellation.Token);
        receiveCancellation.Token.ThrowIfCancellationRequested();
        Task receiveLoop = ReceiveLoopAsync(receiveCancellation.Token);
        lock (_lifecycleLock)
        {
            _receiveLoop = receiveLoop;
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
                    RaiseClosedOnce();
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
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RaiseFaulted(exception);
            RaiseClosedOnce();
        }
    }

    private async Task CompleteRemoteCloseAsync(CancellationToken cancellationToken)
    {
        await CloseSocketAsync(WebSocketCloseStatus.NormalClosure, "remote_close", cancellationToken);
    }

    private async Task CloseForProtocolErrorAsync(
        WebSocketCloseStatus closeStatus,
        string description,
        CancellationToken cancellationToken)
    {
        await CloseSocketAsync(closeStatus, description, cancellationToken);

        RaiseClosedOnce();
    }

    private async Task CloseSocketAsync(
        WebSocketCloseStatus closeStatus,
        string description,
        CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
            {
                await _socket.CloseAsync(closeStatus, LimitCloseDescription(description), cancellationToken);
            }
        }
        finally
        {
            _sendLock.Release();
        }
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

    private void RaiseFaulted(Exception exception)
    {
        Action<Exception>? handlers = Faulted;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<Exception> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(exception);
            }
            catch
            {
            }
        }
    }

    private void RaiseClosedOnce()
    {
        if (Interlocked.Exchange(ref _closedRaised, 1) != 0)
        {
            return;
        }

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

    private async Task DisposeCoreAsync(Task connectTask)
    {
        try
        {
            await connectTask;
        }
        catch
        {
        }

        Task receiveLoop;
        lock (_lifecycleLock)
        {
            receiveLoop = _receiveLoop;
        }
        try
        {
            await receiveLoop;
        }
        catch
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
            _sendLock.Dispose();
            _receiveCancellation?.Dispose();
            _disposeCancellation.Dispose();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    internal static string LimitCloseDescription(string description)
    {
        if (StrictUtf8.GetByteCount(description) <= 123)
        {
            return description;
        }

        StringBuilder result = new(description.Length);
        int byteCount = 0;
        foreach (Rune rune in description.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > 123)
            {
                break;
            }

            result.Append(rune);
            byteCount += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}
