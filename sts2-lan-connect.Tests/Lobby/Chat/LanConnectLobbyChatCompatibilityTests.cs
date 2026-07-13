using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectLobbyChatCompatibilityTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task OldProbeWithoutCapabilitiesDisablesServerChatBeforeTicket()
    {
        LegacyProbeApi api = new();
        int transportCreations = 0;
        await using LanConnectServerChatClient client = new(
            _ => api,
            () =>
            {
                transportCreations++;
                throw new InvalidOperationException("old-server fallback must not create a transport");
            });

        await Assert.ThrowsAsync<NotSupportedException>(() => client.ConnectAsync(
            new Uri("https://legacy-lobby.example/"),
            "net-1",
            "Silent",
            CancellationToken.None));

        Assert.Equal(1, api.ProbeCalls);
        Assert.Equal(0, api.TicketCalls);
        Assert.Equal(0, transportCreations);
        Assert.True(client.IsPermanentlyStopped);
    }

    [Fact]
    public void LegacyCreateAndJoinResponsesRemainValidWithoutRoomSessionId()
    {
        LobbyCreateRoomResponse created = Deserialize<LobbyCreateRoomResponse>("""
            {"roomId":"room-1","controlChannelId":"control-1","hostToken":"host-secret","heartbeatIntervalSeconds":15,"room":{"roomId":"room-1","roomName":"Legacy","modVersion":"0.2.2","maxPlayers":4},"futureCreateField":{"ignored":true}}
            """);
        LobbyJoinRoomResponse joined = Deserialize<LobbyJoinRoomResponse>("""
            {"ticketId":"ticket-1","issuedAt":"2026-07-13T04:00:00Z","expiresAt":"2026-07-13T04:01:00Z","room":{"roomId":"room-1","roomName":"Legacy","modVersion":"0.2.2","maxPlayers":4},"connectionPlan":{"strategy":"direct-first","relayAllowed":true,"controlChannelId":"control-1","directCandidates":[],"futurePlanField":"ignored"},"futureJoinField":[1,2,3]}
            """);

        Assert.Equal("room-1", created.RoomId);
        Assert.Equal("control-1", created.ControlChannelId);
        Assert.Equal("0.2.2", created.Room.ModVersion);
        Assert.Equal(4, created.Room.MaxPlayers);
        Assert.Null(created.RoomSessionId);
        Assert.Equal("ticket-1", joined.TicketId);
        Assert.Equal("control-1", joined.ConnectionPlan.ControlChannelId);
        Assert.True(joined.ConnectionPlan.RelayAllowed);
        Assert.Null(joined.RoomSessionId);
    }

    [Fact]
    public async Task FragmentedLegacyRoomChatRetainsTextAndIgnoresUnknownFields()
    {
        CompatibilityWebSocket socket = new();
        await using LobbyControlClient client = new(socket);
        TaskCompletionSource<LobbyControlEnvelope> received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EnvelopeReceived += envelope =>
        {
            if (envelope.Type == "room_chat")
            {
                received.TrySetResult(envelope);
            }
        };

        await client.ConnectHostAsync(
            new Uri("wss://legacy-lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            CancellationToken.None);

        socket.QueueText(
            "{\"type\":\"room_chat\",\"roomId\":\"room-1\",\"controlChannelId\":\"control-1\",\"role\":\"client\",",
            endOfMessage: false);
        socket.QueueText(
            "\"ticketId\":\"ticket-1\",\"playerName\":\"Silent\",\"playerNetId\":\"222\",\"messageId\":\"legacy-message-1\",\"messageText\":\"legacy text\",\"sentAtUnixMs\":1783857600000,\"futureEnvelopeField\":{\"ignored\":true}}",
            endOfMessage: true);

        LobbyControlEnvelope envelope = await received.Task.WaitAsync(TestTimeout);

        Assert.Equal("room_chat", envelope.Type);
        Assert.Equal("room-1", envelope.RoomId);
        Assert.Equal("control-1", envelope.ControlChannelId);
        Assert.Equal("client", envelope.Role);
        Assert.Equal("ticket-1", envelope.TicketId);
        Assert.Equal("Silent", envelope.PlayerName);
        Assert.Equal("222", envelope.PlayerNetId);
        Assert.Equal("legacy-message-1", envelope.MessageId);
        Assert.Equal("legacy text", envelope.MessageText);
        Assert.Equal(1_783_857_600_000, envelope.SentAtUnixMs);
    }

    private static T Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, LanConnectJson.Options)!;

    private sealed class LegacyProbeApi : ILanConnectServerChatApi
    {
        public int ProbeCalls { get; private set; }

        public int TicketCalls { get; private set; }

        public Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken)
        {
            ProbeCalls++;
            return Task.FromResult(Deserialize<LobbyProbeResponse>("""{"ok":true,"futureProbeField":"ignored"}"""));
        }

        public Task<ServerChatTicketResponse> CreateServerChatTicketAsync(
            ServerChatTicketRequest request,
            CancellationToken cancellationToken)
        {
            TicketCalls++;
            throw new InvalidOperationException("ticket must not be requested from an old server");
        }

        public void Dispose()
        {
        }
    }

    private sealed class CompatibilityWebSocket : ILanConnectWebSocket
    {
        private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>();

        public WebSocketState State { get; private set; } = WebSocketState.None;

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
            return new ValueWebSocketReceiveResult(
                frame.Payload.Length,
                WebSocketMessageType.Text,
                frame.EndOfMessage);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken)
        {
            State = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public void Abort()
        {
            State = WebSocketState.Aborted;
            _frames.Writer.TryComplete();
        }

        public ValueTask DisposeAsync()
        {
            State = WebSocketState.Closed;
            _frames.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public void QueueText(string payload, bool endOfMessage) =>
            _frames.Writer.TryWrite(new Frame(Encoding.UTF8.GetBytes(payload), endOfMessage));

        private sealed record Frame(byte[] Payload, bool EndOfMessage);
    }
}
