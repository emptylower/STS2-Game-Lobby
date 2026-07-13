using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectLobbyControlClientTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task FragmentedRoomChatRaisesExactlyOneCompleteEnvelope()
    {
        FakeWebSocket socket = new();
        await using LobbyControlClient client = new(socket);
        List<LobbyControlEnvelope> envelopes = [];
        TaskCompletionSource received = NewSignal();
        using CancellationTokenSource connectCancellation = new();
        client.EnvelopeReceived += envelope =>
        {
            envelopes.Add(envelope);
            received.TrySetResult();
        };

        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            connectCancellation.Token);
        connectCancellation.Cancel();
        socket.QueueText("{\"type\":\"room_chat\",\"roomId\":\"room-1\",", endOfMessage: false);
        socket.QueueText("\"messageId\":\"message-1\",\"messageText\":\"hello\"}", endOfMessage: true);

        await received.Task.WaitAsync(TestTimeout);

        LobbyControlEnvelope envelope = Assert.Single(envelopes);
        Assert.Equal("room_chat", envelope.Type);
        Assert.Equal("room-1", envelope.RoomId);
        Assert.Equal("message-1", envelope.MessageId);
        Assert.Equal("hello", envelope.MessageText);
    }

    [Fact]
    public async Task PingSendsExactlyOneLegacyPongWithHostRole()
    {
        FakeWebSocket socket = new();
        await using LobbyControlClient client = new(socket);
        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);

        socket.QueueText("{\"type\":\"ping\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\"}");
        string pong = await socket.NextSentPayloadAsync().WaitAsync(TestTimeout);

        Assert.Equal(
            "{\"type\":\"pong\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\",\"role\":\"host\"}",
            pong);
        Assert.Equal(2, socket.SentPayloads.Count);
    }

    [Fact]
    public async Task PrequeuedPingCannotSendPongBeforeHello()
    {
        FakeWebSocket socket = new();
        socket.QueueText("{\"type\":\"ping\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\"}");
        await using LobbyControlClient client = new(socket);

        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);
        await socket.PongSent.Task.WaitAsync(TestTimeout);

        Assert.Equal("{\"type\":\"host_hello\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\",\"role\":\"host\",\"playerName\":\"Host\"}", socket.SentPayloads[0]);
        Assert.Equal("{\"type\":\"pong\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\",\"role\":\"host\"}", socket.SentPayloads[1]);
    }

    [Fact]
    public async Task PrequeuedRoomEnvelopeIsDispatchedOnlyAfterHello()
    {
        FakeWebSocket socket = new();
        socket.QueueText("{\"type\":\"room_chat\",\"roomId\":\"room-1\",\"messageText\":\"hello\"}");
        await using LobbyControlClient client = new(socket);
        string[]? payloadsAtDispatch = null;
        TaskCompletionSource dispatched = NewSignal();
        client.EnvelopeReceived += _ =>
        {
            payloadsAtDispatch = socket.SentPayloads.ToArray();
            dispatched.TrySetResult();
        };

        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);
        await dispatched.Task.WaitAsync(TestTimeout);

        Assert.NotNull(payloadsAtDispatch);
        Assert.Equal(["{\"type\":\"host_hello\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\",\"role\":\"host\",\"playerName\":\"Host\"}"], payloadsAtDispatch);
    }

    [Fact]
    public async Task HostHelloPreservesExactLegacyJsonFixture()
    {
        FakeWebSocket socket = new();
        await using LobbyControlClient client = new(socket);

        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);

        Assert.Equal(
            "{\"type\":\"host_hello\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\",\"role\":\"host\",\"playerName\":\"Host\"}",
            Assert.Single(socket.SentPayloads));
    }

    [Fact]
    public async Task ClientHelloPreservesExactLegacyJsonFixture()
    {
        FakeWebSocket socket = new();
        await using LobbyControlClient client = new(socket);

        await client.ConnectClientAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "ticket-1",
            "Guest",
            "net-1",
            CancellationToken.None);

        Assert.Equal(
            "{\"type\":\"client_hello\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\",\"role\":\"client\",\"ticketId\":\"ticket-1\",\"playerNetId\":\"net-1\",\"playerName\":\"Guest\"}",
            Assert.Single(socket.SentPayloads));
    }

    [Fact]
    public async Task DisposePreservesLegacyCloseAndDisposesSocketOnce()
    {
        FakeWebSocket socket = new();
        LobbyControlClient client = new(socket);
        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);

        await client.DisposeAsync();
        await client.DisposeAsync();

        CloseCall close = Assert.Single(socket.CloseCalls);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, close.Status);
        Assert.Equal("client_shutdown", close.Description);
        Assert.Equal(1, socket.DisposeCount);
    }

    [Fact]
    public async Task RemoteCloseBeforeConnectReturnsCannotLeaveClientConnected()
    {
        FakeWebSocket socket = new() { CloseBeforeConnectReturns = true };
        await using LobbyControlClient client = new(socket);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None));

        Assert.False(client.IsConnected);
        Assert.Equal(
            "{\"type\":\"host_hello\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\",\"role\":\"host\",\"playerName\":\"Host\"}",
            Assert.Single(socket.SentPayloads));
    }

    [Fact]
    public async Task DisposeDuringConnectKeepsTerminalStateAndDisposesSocketOnce()
    {
        FakeWebSocket socket = new() { HoldConnect = true };
        LobbyControlClient client = new(socket);
        Task connect = client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);
        await socket.ConnectStarted.Task.WaitAsync(TestTimeout);

        await client.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        socket.ReleaseConnect();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        Assert.False(client.IsConnected);
        Assert.Empty(socket.SentPayloads);
        Assert.Equal(1, socket.DisposeCount);
    }

    [Fact]
    public async Task DisposeDuringInitialHelloSendLeavesTerminalAndCleansUpOnce()
    {
        FakeWebSocket socket = new() { HoldFirstSend = true };
        LobbyControlClient client = new(socket, closeTimeout: TimeSpan.FromMilliseconds(25));
        Task connect = client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);
        await socket.FirstSendStarted.Task.WaitAsync(TestTimeout);

        await client.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        Assert.False(client.IsConnected);
        Assert.Empty(socket.SentPayloads);
        Assert.Equal(1, socket.AbortCount);
        Assert.Equal(1, socket.DisposeCount);
    }

    [Fact]
    public async Task SendDuringConnectCannotOvertakeInitialHello()
    {
        FakeWebSocket socket = new() { HoldConnect = true };
        await using LobbyControlClient client = new(socket);
        Task connect = client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);
        await socket.ConnectStarted.Task.WaitAsync(TestTimeout);

        Task send = client.SendAsync(new LobbyControlEnvelope
        {
            Type = "room_chat",
            RoomId = "room-1",
            MessageText = "queued"
        });
        await Task.Yield();

        try
        {
            Assert.Empty(socket.SentPayloads);
        }
        finally
        {
            socket.ReleaseConnect();
        }
        await Task.WhenAll(connect, send).WaitAsync(TestTimeout);
        Assert.Contains("\"type\":\"host_hello\"", socket.SentPayloads[0], StringComparison.Ordinal);
        Assert.Contains("\"type\":\"room_chat\"", socket.SentPayloads[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitialHelloSendFailureTerminatesAndCleansUpSocket()
    {
        FakeWebSocket socket = new() { FailSendCall = 1 };
        LobbyControlClient client = new(socket);

        await Assert.ThrowsAsync<WebSocketException>(() => client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None));

        Assert.False(client.IsConnected);
        Assert.Equal(1, socket.AbortCount);
        Assert.Equal(1, socket.DisposeCount);
    }

    [Fact]
    public async Task ThrowingWarningSinkCannotEscapePongSendObserver()
    {
        FakeWebSocket socket = new() { FailSendCall = 2 };
        TaskCompletionSource warned = NewSignal();
        TaskCompletionSource dispatched = NewSignal();
        LobbyControlClient client = new(socket, warningSink: _ =>
        {
            warned.TrySetResult();
            throw new InvalidOperationException("warning sink failed");
        });
        client.EnvelopeReceived += envelope =>
        {
            if (envelope.Type == "room_chat")
            {
                dispatched.TrySetResult();
            }
        };
        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);

        socket.QueueText("{\"type\":\"ping\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\"}");
        await warned.Task.WaitAsync(TestTimeout);
        socket.QueueText("{\"type\":\"room_chat\",\"roomId\":\"room-1\",\"messageText\":\"still running\"}");
        await dispatched.Task.WaitAsync(TestTimeout);
        await client.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.False(client.IsConnected);
        Assert.Equal(1, socket.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseTimeoutAbortsAndDisposesHungClose(bool ignoresCancellation)
    {
        FakeWebSocket socket = new()
        {
            CooperativeHungClose = !ignoresCancellation,
            NonCooperativeHungClose = ignoresCancellation
        };
        LobbyControlClient client = new(socket, closeTimeout: TimeSpan.FromMilliseconds(25));
        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);

        await client.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.Single(socket.CloseCalls);
        Assert.Equal(1, socket.AbortCount);
        Assert.Equal(1, socket.DisposeCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CloseTimeoutMustBePositive(int timeoutMilliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LobbyControlClient(new FakeWebSocket(), closeTimeout: TimeSpan.FromMilliseconds(timeoutMilliseconds)));
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FakeWebSocket : ILanConnectWebSocket
    {
        private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>();
        private readonly Channel<string> _sentPayloads = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource _connectRelease = NewSignal();
        private readonly TaskCompletionSource _abortSignal = NewSignal();
        private int _sendCallCount;
        private int _abortCount;

        public WebSocketState State { get; private set; } = WebSocketState.None;

        public List<string> SentPayloads { get; } = [];

        public List<CloseCall> CloseCalls { get; } = [];

        public int DisposeCount { get; private set; }

        public int AbortCount => Volatile.Read(ref _abortCount);

        public bool CloseBeforeConnectReturns { get; init; }

        public bool HoldConnect { get; init; }

        public bool HoldFirstSend { get; init; }

        public int FailSendCall { get; init; }

        public bool CooperativeHungClose { get; init; }

        public bool NonCooperativeHungClose { get; init; }

        public TaskCompletionSource ConnectStarted { get; } = NewSignal();

        public TaskCompletionSource FirstSendStarted { get; } = NewSignal();

        public TaskCompletionSource PongSent { get; } = NewSignal();

        public void SetRequestHeader(string headerName, string headerValue)
        {
        }

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            State = WebSocketState.Open;
            ConnectStarted.TrySetResult();
            if (HoldConnect)
            {
                await _connectRelease.Task.WaitAsync(cancellationToken);
            }
            if (CloseBeforeConnectReturns)
            {
                QueueClose();
            }
        }

        public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            Frame frame = await _frames.Reader.ReadAsync(cancellationToken);
            frame.Payload.CopyTo(buffer);
            if (frame.MessageType == WebSocketMessageType.Close)
            {
                State = WebSocketState.CloseReceived;
            }
            return new ValueWebSocketReceiveResult(frame.Payload.Length, frame.MessageType, frame.EndOfMessage);
        }

        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _sendCallCount);
            FirstSendStarted.TrySetResult();
            if (FailSendCall == call)
            {
                throw new WebSocketException(WebSocketError.Faulted);
            }
            if (HoldFirstSend && call == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            string payload = Encoding.UTF8.GetString(buffer.Span);
            SentPayloads.Add(payload);
            _sentPayloads.Writer.TryWrite(payload);
            if (payload.Contains("\"type\":\"pong\"", StringComparison.Ordinal))
            {
                PongSent.TrySetResult();
            }
        }

        public async Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken)
        {
            CloseCalls.Add(new CloseCall(closeStatus, statusDescription));
            if (CooperativeHungClose)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            if (NonCooperativeHungClose)
            {
                await _abortSignal.Task;
            }
            State = WebSocketState.Closed;
        }

        public void Abort()
        {
            Interlocked.Increment(ref _abortCount);
            State = WebSocketState.Aborted;
            _abortSignal.TrySetResult();
            _frames.Writer.TryComplete(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            State = WebSocketState.Closed;
            return ValueTask.CompletedTask;
        }

        public void QueueText(string payload, bool endOfMessage = true) =>
            _frames.Writer.TryWrite(new Frame(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, endOfMessage));

        public void QueueClose() =>
            _frames.Writer.TryWrite(new Frame([], WebSocketMessageType.Close, true));

        public void ReleaseConnect() => _connectRelease.TrySetResult();

        public async Task<string> NextSentPayloadAsync()
        {
            while (true)
            {
                string payload = await _sentPayloads.Reader.ReadAsync();
                if (payload.Contains("\"type\":\"pong\"", StringComparison.Ordinal))
                {
                    return payload;
                }
            }
        }
    }

    private sealed record Frame(byte[] Payload, WebSocketMessageType MessageType, bool EndOfMessage);

    private sealed record CloseCall(WebSocketCloseStatus Status, string Description);
}
