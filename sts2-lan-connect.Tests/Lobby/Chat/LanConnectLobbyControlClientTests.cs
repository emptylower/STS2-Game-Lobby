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

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FakeWebSocket : ILanConnectWebSocket
    {
        private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>();
        private readonly Channel<string> _sentPayloads = Channel.CreateUnbounded<string>();

        public WebSocketState State { get; private set; } = WebSocketState.None;

        public List<string> SentPayloads { get; } = [];

        public List<CloseCall> CloseCalls { get; } = [];

        public int DisposeCount { get; private set; }

        public void SetRequestHeader(string headerName, string headerValue)
        {
        }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            State = WebSocketState.Open;
            return Task.CompletedTask;
        }

        public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            Frame frame = await _frames.Reader.ReadAsync(cancellationToken);
            frame.Payload.CopyTo(buffer);
            return new ValueWebSocketReceiveResult(frame.Payload.Length, WebSocketMessageType.Text, frame.EndOfMessage);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            string payload = Encoding.UTF8.GetString(buffer.Span);
            SentPayloads.Add(payload);
            _sentPayloads.Writer.TryWrite(payload);
            return ValueTask.CompletedTask;
        }

        public Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken)
        {
            CloseCalls.Add(new CloseCall(closeStatus, statusDescription));
            State = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public void Abort()
        {
            State = WebSocketState.Aborted;
            _frames.Writer.TryComplete(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            State = WebSocketState.Closed;
            return ValueTask.CompletedTask;
        }

        public void QueueText(string payload, bool endOfMessage = true) =>
            _frames.Writer.TryWrite(new Frame(Encoding.UTF8.GetBytes(payload), endOfMessage));

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

    private sealed record Frame(byte[] Payload, bool EndOfMessage);

    private sealed record CloseCall(WebSocketCloseStatus Status, string Description);
}
