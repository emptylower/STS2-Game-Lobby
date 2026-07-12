using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectLobbyChatApiTests
{
    [Fact]
    public void DeserializesProbeAndTicketFixtures()
    {
        LobbyProbeResponse probe = Deserialize<LobbyProbeResponse>("""
            {"ok":true,"capabilities":{"serverChatVersion":1,"roomChatProtocolVersion":0,"richContentVersion":0,"emojiSetVersion":0,"itemRefVersion":0,"combatRefVersion":0,"maxMessageChars":300,"maxSegments":32,"maxEntities":0,"historyLimit":50}}
            """);
        Assert.True(probe.Ok);
        Assert.True(probe.Capabilities.SupportsTextServerChat);
        Assert.False(probe.Capabilities.SupportsRichServerChat);
        Assert.False(probe.Capabilities.SupportsRoomChat);
        Assert.Equal(300, probe.Capabilities.MaxMessageChars);
        Assert.Equal(50, probe.Capabilities.HistoryLimit);

        LobbyProbeResponse futureRichProbe = Deserialize<LobbyProbeResponse>("""
            {"ok":true,"capabilities":{"serverChatVersion":1,"richContentVersion":1}}
            """);
        Assert.False(futureRichProbe.Capabilities.SupportsRichServerChat);

        ServerChatTicketResponse ticket = Deserialize<ServerChatTicketResponse>("""
            {"ticket":"one-time-secret","expiresAt":"2026-07-13T04:05:06.000Z","webSocketUrl":"wss://lobby.example/chat","protocolVersion":1}
            """);
        Assert.Equal("one-time-secret", ticket.Ticket);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T04:05:06.000Z"), ticket.ExpiresAt);
        Assert.Equal("wss://lobby.example/chat", ticket.WebSocketUrl);
        Assert.Equal(1, ticket.ProtocolVersion);
    }

    [Fact]
    public void DeserializesEveryPhaseOneInboundEnvelopeFixture()
    {
        ServerChatReadyEnvelope ready = Deserialize<ServerChatReadyEnvelope>("""
            {"type":"chat_ready","protocolVersion":1,"channel":"server","sessionId":"11111111-1111-4111-8111-111111111111","senderId":"sender_abcdefghijklmn","instanceId":"22222222-2222-4222-8222-222222222222","historyEpoch":3,"chatEnabled":true,"serverChatVersion":1,"enabledFeatures":{"richContentVersion":0,"emojiSetVersion":0,"itemRefVersion":0}}
            """);
        Assert.Equal(LanConnectChatChannel.Server, ready.Channel);
        Assert.Equal(3, ready.HistoryEpoch);
        Assert.True(ready.ChatEnabled);
        Assert.Equal(0, ready.EnabledFeatures.RichContentVersion);

        ServerChatSnapshotBeginEnvelope begin = Deserialize<ServerChatSnapshotBeginEnvelope>("""
            {"type":"chat_snapshot_begin","protocolVersion":1,"snapshotId":"33333333-3333-4333-8333-333333333333","instanceId":"22222222-2222-4222-8222-222222222222","historyEpoch":3,"totalMessages":1}
            """);
        Assert.Equal(1, begin.TotalMessages);

        const string canonical = """{"messageId":"44444444-4444-4444-8444-444444444444","senderId":"sender_abcdefghijklmn","senderName":"Ironclad","content":{"formatVersion":1,"segments":[{"kind":"text","text":"hello"}]},"plainTextFallback":"hello","sentAt":"2026-07-13T04:05:06.000Z"}""";
        ServerChatSnapshotChunkEnvelope chunk = Deserialize<ServerChatSnapshotChunkEnvelope>($$"""
            {"type":"chat_snapshot_chunk","protocolVersion":1,"snapshotId":"33333333-3333-4333-8333-333333333333","chunkIndex":0,"messages":[{{canonical}}]}
            """);
        Assert.Equal("hello", chunk.Messages.Single().Content.Segments.Single().Text);

        ServerChatSnapshotEndEnvelope end = Deserialize<ServerChatSnapshotEndEnvelope>("""
            {"type":"chat_snapshot_end","protocolVersion":1,"snapshotId":"33333333-3333-4333-8333-333333333333","historyEpoch":3}
            """);
        Assert.Equal(3, end.HistoryEpoch);

        ServerChatAckEnvelope ack = Deserialize<ServerChatAckEnvelope>($$"""
            {"type":"chat_ack","protocolVersion":1,"clientMessageId":"55555555-5555-4555-8555-555555555555","message":{{canonical}}}
            """);
        Assert.Equal("55555555-5555-4555-8555-555555555555", ack.ClientMessageId);

        ServerChatMessageEnvelope message = Deserialize<ServerChatMessageEnvelope>($$"""
            {"type":"chat_message","protocolVersion":1,"message":{{canonical}}}
            """);
        Assert.Equal("Ironclad", message.Message.SenderName);

        ServerChatErrorEnvelope error = Deserialize<ServerChatErrorEnvelope>("""
            {"type":"chat_error","protocolVersion":1,"clientMessageId":"55555555-5555-4555-8555-555555555555","code":"rate_limited","message":"请求过于频繁。","retryAfterMs":1250}
            """);
        Assert.Equal("rate_limited", error.Code);
        Assert.Equal(1250, error.RetryAfterMs);

        ServerChatStateEnvelope state = Deserialize<ServerChatStateEnvelope>("""
            {"type":"chat_state","protocolVersion":1,"chatEnabled":false,"enabledFeatures":{"richContentVersion":0,"emojiSetVersion":0,"itemRefVersion":0},"historyEpoch":4,"changedAt":"2026-07-13T06:02:03.456Z"}
            """);
        Assert.False(state.ChatEnabled);
        Assert.Equal(4, state.HistoryEpoch);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T06:02:03.456Z"), state.ChangedAt);

        ServerChatHistoryClearedEnvelope historyCleared = Deserialize<ServerChatHistoryClearedEnvelope>("""
            {"type":"chat_history_cleared","protocolVersion":1,"historyEpoch":5,"changedAt":"2026-07-13T07:08:09.123Z"}
            """);
        Assert.Equal("chat_history_cleared", historyCleared.Type);
        Assert.Equal(1, historyCleared.ProtocolVersion);
        Assert.Equal(5, historyCleared.HistoryEpoch);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T07:08:09.123Z"), historyCleared.ChangedAt);

        ServerChatInboundEnvelope projected = Deserialize<ServerChatInboundEnvelope>($$"""
            {"type":"chat_message","protocolVersion":1,"message":{{canonical}}}
            """);
        Assert.Equal("chat_message", projected.Type);
        Assert.Equal("hello", projected.CanonicalMessage!.PlainTextFallback);

        ServerChatInboundEnvelope projectedError = Deserialize<ServerChatInboundEnvelope>("""
            {"type":"chat_error","protocolVersion":1,"clientMessageId":"","code":"server_busy","message":"服务繁忙。"}
            """);
        Assert.Equal("服务繁忙。", projectedError.ErrorMessage);

        ServerChatInboundEnvelope projectedHistoryCleared = Deserialize<ServerChatInboundEnvelope>("""
            {"type":"chat_history_cleared","protocolVersion":1,"historyEpoch":5,"changedAt":"2026-07-13T07:08:09.123Z"}
            """);
        Assert.Equal("chat_history_cleared", projectedHistoryCleared.Type);
        Assert.Equal(5, projectedHistoryCleared.HistoryEpoch);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T07:08:09.123Z"), projectedHistoryCleared.ChangedAt);
    }

    [Theory]
    [InlineData("{\"type\":\"chat_ready\",\"protocolVersion\":1,\"channel\":0}")]
    [InlineData("{\"type\":\"chat_ready\",\"protocolVersion\":1}")]
    [InlineData("{\"type\":\"chat_ready\",\"protocolVersion\":1,\"channel\":\"Server\"}")]
    [InlineData("{\"type\":\"chat_ready\",\"protocolVersion\":1,\"channel\":\"unknown\"}")]
    public void ReadyRejectsNonCanonicalOrMissingChannel(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ServerChatReadyEnvelope>(json, LanConnectJson.Options));
    }

    [Fact]
    public void SerializesTextOnlyChatSendWithExactFields()
    {
        ServerChatSendEnvelope send = new()
        {
            ClientMessageId = "55555555-5555-4555-8555-555555555555",
            Content = new ServerChatContent
            {
                Segments = [new ServerChatTextSegment { Text = "hello" }]
            }
        };

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(send, LanConnectJson.Options));
        JsonElement root = document.RootElement;
        Assert.Equal(["type", "protocolVersion", "channel", "clientMessageId", "content"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("chat_send", root.GetProperty("type").GetString());
        Assert.Equal("server", root.GetProperty("channel").GetString());
        JsonElement content = root.GetProperty("content");
        Assert.Equal(1, content.GetProperty("formatVersion").GetInt32());
        JsonElement segment = content.GetProperty("segments")[0];
        Assert.Equal(["kind", "text"], segment.EnumerateObject().Select(property => property.Name));
        Assert.Equal("text", segment.GetProperty("kind").GetString());
        Assert.Equal("hello", segment.GetProperty("text").GetString());
    }

    [Fact]
    public void DeserializesOptionalRoomSessionIdFromCreateAndJoinResponses()
    {
        LobbyCreateRoomResponse created = Deserialize<LobbyCreateRoomResponse>("""
            {"roomId":"room-1","controlChannelId":"control-1","hostToken":"host-secret","heartbeatIntervalSeconds":15,"room":{},"roomSessionId":"66666666-6666-4666-8666-666666666666"}
            """);
        LobbyJoinRoomResponse joined = Deserialize<LobbyJoinRoomResponse>("""
            {"ticketId":"ticket-1","issuedAt":"2026-07-13T04:00:00Z","expiresAt":"2026-07-13T04:01:00Z","room":{},"connectionPlan":{},"roomSessionId":"66666666-6666-4666-8666-666666666666"}
            """);
        LobbyCreateRoomResponse legacy = Deserialize<LobbyCreateRoomResponse>("""{"roomId":"old","room":{}}""");

        Assert.Equal(created.RoomSessionId, joined.RoomSessionId);
        Assert.Null(legacy.RoomSessionId);
    }

    [Fact]
    public async Task ProbeAndTicketApisUseExactRoutesAndTicketAuthorization()
    {
        List<string> logs = [];
        RecordingHandler handler = new(
            """{"ok":true,"capabilities":{"serverChatVersion":1}}""",
            """{"ticket":"one-time-secret","expiresAt":"2026-07-13T04:05:06Z","webSocketUrl":"wss://lobby.example/base/chat","protocolVersion":1}""");
        using LobbyApiClient client = new("https://lobby.example/base", "lobby-secret", "create-secret", handler, logs.Add);

        LobbyProbeResponse probe = await client.GetProbeAsync();
        ServerChatTicketResponse ticket = await client.CreateServerChatTicketAsync(new ServerChatTicketRequest
        {
            ProtocolVersion = 1,
            PlayerNetId = "net-1",
            PlayerName = "Ironclad"
        });

        Assert.True(probe.Ok);
        Assert.Equal("one-time-secret", ticket.Ticket);
        Assert.Equal("https://lobby.example/base/probe", handler.Requests[0].Uri);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("https://lobby.example/base/chat/tickets", handler.Requests[1].Uri);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("application/json; charset=utf-8", handler.Requests[1].ContentType);
        Assert.Equal("lobby-secret", handler.Requests[1].LobbyToken);
        Assert.Null(handler.Requests[1].CreateToken);
        Assert.DoesNotContain("?", handler.Requests[1].Uri);
        Assert.Equal("""{"protocolVersion":1,"playerNetId":"net-1","playerName":"Ironclad"}""", handler.Requests[1].Body);

        string diagnostics = string.Join("\n", logs);
        Assert.Contains("GET https://lobby.example/base/probe", diagnostics, StringComparison.Ordinal);
        Assert.Contains("POST https://lobby.example/base/chat/tickets", diagnostics, StringComparison.Ordinal);
        Assert.Contains("-> 200", diagnostics, StringComparison.Ordinal);
        Assert.Contains("protocolVersion=1", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("one-time-secret", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ticket\":", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("net-1", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("Ironclad", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("create-secret", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("lobby-secret", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void HttpHandlerOwnershipIsConfigurable()
    {
        RecordingHandler retained = new("{}");
        using (LobbyApiClient client = new("https://lobby.example", httpMessageHandler: retained, diagnosticSink: _ => { }, disposeHttpMessageHandler: false))
        {
        }
        Assert.False(retained.IsDisposed);

        RecordingHandler owned = new("{}");
        using (LobbyApiClient client = new("https://lobby.example", httpMessageHandler: owned, diagnosticSink: _ => { }))
        {
        }
        Assert.True(owned.IsDisposed);
    }

    private static T Deserialize<T>(string json) where T : class
    {
        return JsonSerializer.Deserialize<T>(json, LanConnectJson.Options) ?? throw new InvalidOperationException("Fixture returned null.");
    }

    private sealed class RecordingHandler(params string[] responses) : HttpMessageHandler
    {
        private int _index;

        public List<RecordedRequest> Requests { get; } = [];

        public bool IsDisposed { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Content?.Headers.ContentType?.ToString(),
                request.Headers.TryGetValues("x-lobby-access-token", out IEnumerable<string>? lobby) ? lobby.Single() : null,
                request.Headers.TryGetValues("x-create-room-token", out IEnumerable<string>? create) ? create.Single() : null,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[_index++], Encoding.UTF8, "application/json")
            };
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? ContentType, string? LobbyToken, string? CreateToken, string? Body);
}
