using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectWebSocketTransportTests
{
    private static readonly CancellationToken TestCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task ReassemblesTextFragmentsBeforeRaisingOnePayload()
    {
        FakeWebSocket socket = new();
        socket.QueueText("hel", endOfMessage: false);
        socket.QueueText("lo ", endOfMessage: false);
        socket.QueueText("world", endOfMessage: true);
        await using LanConnectWebSocketTransport transport = new(socket);
        List<string> payloads = [];
        TaskCompletionSource received = NewSignal();
        transport.PayloadReceived += payload =>
        {
            payloads.Add(payload);
            received.TrySetResult();
        };

        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        await received.Task.WaitAsync(TestCancellation);

        Assert.Equal(["hello world"], payloads);
    }

    [Fact]
    public async Task RejectsBinaryMessagesWithUnsupportedDataClose()
    {
        FakeWebSocket socket = new();
        socket.QueueBinary([1, 2, 3]);
        await using LanConnectWebSocketTransport transport = new(socket);

        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        CloseCall close = await socket.NextCloseAsync(TestCancellation);

        Assert.Equal(WebSocketCloseStatus.InvalidMessageType, close.Status);
    }

    [Fact]
    public async Task RejectsMalformedUtf8AfterAssembly()
    {
        FakeWebSocket socket = new();
        socket.QueueTextBytes([0xC3], endOfMessage: false);
        socket.QueueTextBytes([0x28], endOfMessage: true);
        await using LanConnectWebSocketTransport transport = new(socket);

        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        CloseCall close = await socket.NextCloseAsync(TestCancellation);

        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, close.Status);
    }

    [Fact]
    public async Task RejectsAssembledPayloadLargerThan8192Bytes()
    {
        FakeWebSocket socket = new();
        socket.QueueTextBytes(new byte[4096], endOfMessage: false);
        socket.QueueTextBytes(new byte[4096], endOfMessage: false);
        socket.QueueTextBytes([0], endOfMessage: true);
        await using LanConnectWebSocketTransport transport = new(socket);

        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        CloseCall close = await socket.NextCloseAsync(TestCancellation);

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, close.Status);
    }

    [Fact]
    public async Task PropagatesRemoteCloseAndCompletesCloseHandshake()
    {
        FakeWebSocket socket = new();
        socket.QueueClose();
        await using LanConnectWebSocketTransport transport = new(socket);
        TaskCompletionSource closed = NewSignal();
        transport.Closed += () => closed.TrySetResult();

        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        await closed.Task.WaitAsync(TestCancellation);
        CloseCall close = await socket.NextCloseAsync(TestCancellation);

        Assert.Equal(WebSocketCloseStatus.NormalClosure, close.Status);
        Assert.Equal("remote_close", close.Description);
    }

    [Fact]
    public async Task CancellationStopsReceiveWithoutProtocolClose()
    {
        FakeWebSocket socket = new();
        await using LanConnectWebSocketTransport transport = new(socket);
        using CancellationTokenSource cancellation = new();

        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: cancellation.Token);
        await socket.ReceiveStarted.Task.WaitAsync(TestCancellation);
        cancellation.Cancel();
        await socket.ReceiveCancelled.Task.WaitAsync(TestCancellation);

        Assert.Empty(socket.CloseCalls);
    }

    [Fact]
    public async Task SetsHeadersBeforeConnectingAndStartsOnlyOneReceiveLoop()
    {
        FakeWebSocket socket = new();
        await using LanConnectWebSocketTransport transport = new(socket);

        await transport.ConnectAsync(
            new Uri("wss://lobby.example/chat"),
            new Dictionary<string, string> { ["Authorization"] = "Bearer ticket" },
            TestCancellation);

        Assert.Equal(["header:Authorization", "connect"], socket.Operations.Take(2));
        Assert.Equal("Bearer ticket", socket.Headers["Authorization"]);
        Assert.Equal(1, socket.ReceiveCallCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.ConnectAsync(new Uri("wss://lobby.example/chat")));
    }

    [Fact]
    public async Task SerializesConcurrentSends()
    {
        FakeWebSocket socket = new() { HoldFirstSend = true };
        await using LanConnectWebSocketTransport transport = new(socket);
        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);

        Task first = transport.SendAsync("first", TestCancellation);
        await socket.FirstSendStarted.Task.WaitAsync(TestCancellation);
        Task second = transport.SendAsync("second", TestCancellation);
        await Task.Yield();

        Assert.Equal(1, socket.SendCallCount);
        socket.ReleaseFirstSend();
        await Task.WhenAll(first, second);
        Assert.Equal(["first", "second"], socket.SentPayloads);
    }

    [Fact]
    public async Task RemoteCloseWaitsForInflightSend()
    {
        FakeWebSocket socket = new() { HoldFirstSend = true };
        await using LanConnectWebSocketTransport transport = new(socket);
        TaskCompletionSource closed = NewSignal();
        transport.Closed += () => closed.TrySetResult();
        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        Task send = transport.SendAsync("pending", TestCancellation);
        await socket.FirstSendStarted.Task.WaitAsync(TestCancellation);

        socket.QueueClose();
        await socket.FrameRead.Task.WaitAsync(TestCancellation);
        try
        {
            Task observationWindow = Task.Delay(TimeSpan.FromMilliseconds(100), TestCancellation);
            Assert.Same(observationWindow, await Task.WhenAny(socket.CloseStarted.Task, observationWindow));
        }
        finally
        {
            socket.ReleaseFirstSend();
        }
        await Task.WhenAll(send, socket.CloseStarted.Task, closed.Task).WaitAsync(TestCancellation);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, Assert.Single(socket.CloseCalls).Status);
    }

    [Fact]
    public async Task ProtocolCloseWaitsForInflightSend()
    {
        FakeWebSocket socket = new() { HoldFirstSend = true };
        await using LanConnectWebSocketTransport transport = new(socket);
        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        Task send = transport.SendAsync("pending", TestCancellation);
        await socket.FirstSendStarted.Task.WaitAsync(TestCancellation);

        socket.QueueBinary([1]);
        await socket.FrameRead.Task.WaitAsync(TestCancellation);
        try
        {
            Task observationWindow = Task.Delay(TimeSpan.FromMilliseconds(100), TestCancellation);
            Assert.Same(observationWindow, await Task.WhenAny(socket.CloseStarted.Task, observationWindow));
        }
        finally
        {
            socket.ReleaseFirstSend();
        }
        await Task.WhenAll(send, socket.CloseStarted.Task).WaitAsync(TestCancellation);
        Assert.Equal(WebSocketCloseStatus.InvalidMessageType, Assert.Single(socket.CloseCalls).Status);
    }

    [Fact]
    public async Task DisposeCancelsAndWaitsForConnectBeforeDisposingSocket()
    {
        FakeWebSocket socket = new() { HoldConnect = true };
        LanConnectWebSocketTransport transport = new(socket);
        Task connect = transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        await socket.ConnectReachedGate.Task.WaitAsync(TestCancellation);

        Task dispose = transport.DisposeAsync().AsTask();
        try
        {
            Assert.False(dispose.IsCompleted);
            Assert.Equal(0, socket.DisposeCount);
        }
        finally
        {
            socket.ReleaseConnect();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        await dispose.WaitAsync(TestCancellation);
        Assert.Equal(0, socket.ReceiveCallCount);
        Assert.Equal(1, socket.DisposeCount);
    }

    [Fact]
    public async Task CallerCancellationAfterSocketOpensDoesNotAllowSecondConnectAttempt()
    {
        FakeWebSocket socket = new() { HoldConnect = true };
        LanConnectWebSocketTransport transport = new(socket);
        using CancellationTokenSource cancellation = new();
        Task firstConnect = transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: cancellation.Token);
        await socket.ConnectReachedGate.Task.WaitAsync(TestCancellation);

        cancellation.Cancel();
        socket.ReleaseConnect();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstConnect);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation));
            Assert.Equal(1, socket.ConnectCallCount);
        }
        finally
        {
            await transport.DisposeAsync();
        }
        Assert.Equal(1, socket.DisposeCount);
    }

    [Fact]
    public async Task DisposeCancelsReceiveAndWaitsForInflightSend()
    {
        FakeWebSocket socket = new() { HoldFirstSend = true };
        LanConnectWebSocketTransport transport = new(socket);
        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        Task send = transport.SendAsync("pending", TestCancellation);
        await socket.FirstSendStarted.Task.WaitAsync(TestCancellation);

        Task dispose = transport.DisposeAsync().AsTask();
        await socket.ReceiveCancelled.Task.WaitAsync(TestCancellation);
        Assert.False(dispose.IsCompleted);

        socket.ReleaseFirstSend();
        await Task.WhenAll(send, dispose);
        await transport.DisposeAsync();
        Assert.Equal(1, socket.DisposeCount);
    }

    [Fact]
    public async Task ThrowingPayloadHandlerDoesNotStopLaterDeliveryOrCleanup()
    {
        FakeWebSocket socket = new();
        socket.QueueText("first");
        socket.QueueText("second");
        socket.QueueClose();
        await using LanConnectWebSocketTransport transport = new(socket);
        List<string> received = [];
        TaskCompletionSource closed = NewSignal();
        transport.PayloadReceived += _ => throw new InvalidOperationException("handler failed");
        transport.PayloadReceived += received.Add;
        transport.Closed += () => closed.TrySetResult();

        await transport.ConnectAsync(new Uri("wss://lobby.example/chat"), cancellationToken: TestCancellation);
        await closed.Task.WaitAsync(TestCancellation);

        Assert.Equal(["first", "second"], received);
        Assert.Single(socket.CloseCalls);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FakeWebSocket : ILanConnectWebSocket
    {
        private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>();
        private readonly Channel<CloseCall> _closes = Channel.CreateUnbounded<CloseCall>();
        private readonly TaskCompletionSource _connectRelease = NewSignal();
        private readonly TaskCompletionSource _sendRelease = NewSignal();
        private int _receiveCallCount;
        private int _sendCallCount;
        private int _connectCallCount;

        public WebSocketState State { get; private set; } = WebSocketState.None;
        public Dictionary<string, string> Headers { get; } = [];
        public List<string> Operations { get; } = [];
        public List<string> SentPayloads { get; } = [];
        public List<CloseCall> CloseCalls { get; } = [];
        public TaskCompletionSource ReceiveStarted { get; } = NewSignal();
        public TaskCompletionSource ReceiveCancelled { get; } = NewSignal();
        public TaskCompletionSource FrameRead { get; } = NewSignal();
        public TaskCompletionSource ConnectReachedGate { get; } = NewSignal();
        public TaskCompletionSource FirstSendStarted { get; } = NewSignal();
        public TaskCompletionSource CloseStarted { get; } = NewSignal();
        public bool HoldConnect { get; init; }
        public bool HoldFirstSend { get; init; }
        public int ReceiveCallCount => Volatile.Read(ref _receiveCallCount);
        public int SendCallCount => Volatile.Read(ref _sendCallCount);
        public int ConnectCallCount => Volatile.Read(ref _connectCallCount);
        public int DisposeCount { get; private set; }

        public void SetRequestHeader(string headerName, string headerValue)
        {
            Operations.Add($"header:{headerName}");
            Headers.Add(headerName, headerValue);
        }

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _connectCallCount);
            Operations.Add("connect");
            State = WebSocketState.Open;
            ConnectReachedGate.TrySetResult();
            if (HoldConnect)
            {
                await _connectRelease.Task;
            }
        }

        public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _receiveCallCount);
            ReceiveStarted.TrySetResult();
            try
            {
                Frame frame = await _frames.Reader.ReadAsync(cancellationToken);
                frame.Payload.CopyTo(buffer);
                FrameRead.TrySetResult();
                if (frame.Type == WebSocketMessageType.Close)
                {
                    State = WebSocketState.CloseReceived;
                }
                return new ValueWebSocketReceiveResult(frame.Payload.Length, frame.Type, frame.EndOfMessage);
            }
            catch (OperationCanceledException)
            {
                ReceiveCancelled.TrySetResult();
                throw;
            }
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _sendCallCount);
            FirstSendStarted.TrySetResult();
            if (HoldFirstSend && call == 1)
            {
                await _sendRelease.Task.WaitAsync(cancellationToken);
            }
            SentPayloads.Add(Encoding.UTF8.GetString(buffer.Span));
        }

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            CloseStarted.TrySetResult();
            CloseCall call = new(closeStatus, statusDescription);
            CloseCalls.Add(call);
            _closes.Writer.TryWrite(call);
            State = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            State = WebSocketState.Closed;
            return ValueTask.CompletedTask;
        }

        public void QueueText(string payload, bool endOfMessage = true) => QueueTextBytes(Encoding.UTF8.GetBytes(payload), endOfMessage);
        public void QueueTextBytes(byte[] payload, bool endOfMessage) => _frames.Writer.TryWrite(new Frame(payload, WebSocketMessageType.Text, endOfMessage));
        public void QueueBinary(byte[] payload) => _frames.Writer.TryWrite(new Frame(payload, WebSocketMessageType.Binary, true));
        public void QueueClose() => _frames.Writer.TryWrite(new Frame([], WebSocketMessageType.Close, true));
        public void ReleaseConnect() => _connectRelease.TrySetResult();
        public void ReleaseFirstSend() => _sendRelease.TrySetResult();
        public ValueTask<CloseCall> NextCloseAsync(CancellationToken cancellationToken) => _closes.Reader.ReadAsync(cancellationToken);
    }

    private sealed record Frame(byte[] Payload, WebSocketMessageType Type, bool EndOfMessage);
    private sealed record CloseCall(WebSocketCloseStatus Status, string Description);
}
