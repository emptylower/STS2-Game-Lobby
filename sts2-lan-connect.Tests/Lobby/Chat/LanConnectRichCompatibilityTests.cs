using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectRichCompatibilityTests
{
    private const string Features =
        "{\"richContentVersion\":1,\"emojiSetVersion\":1,\"itemRefVersion\":1,\"combatRefVersion\":0}";
    private const string Content =
        "{\"formatVersion\":1,\"segments\":[{\"kind\":\"text\",\"text\":\"look \"},{\"kind\":\"emoji\",\"emojiId\":\"heart\"},{\"kind\":\"item_ref\",\"itemType\":\"relic\",\"modelId\":\"MegaCrit.Anchor\"}]}";
    private const string RoomMessage =
        "{\"roomId\":\"room-1\",\"roomSessionId\":\"session-1\",\"messageId\":\"11111111-1111-4111-8111-111111111111\",\"senderId\":\"net:alice\",\"senderName\":\"Alice\",\"content\":" + Content + ",\"plainTextFallback\":\"look [Emoji][Relic]\",\"sentAt\":\"2026-07-15T00:00:00.000Z\"}";

    [Fact]
    public void Old_probe_ready_and_room_text_default_to_legacy_without_rich_controls()
    {
        LobbyProbeResponse probe = Deserialize<LobbyProbeResponse>("""{"ok":true}""");
        LanConnectServerChatReadyEnvelope ready = Deserialize<LanConnectServerChatReadyEnvelope>(
            """{"type":"chat_ready","protocolVersion":1,"channel":"server","sessionId":"old-session","senderId":"old-sender","instanceId":"old-instance","historyEpoch":1,"chatEnabled":true,"serverChatVersion":1}""");
        LobbyControlEnvelope legacy = Deserialize<LobbyControlEnvelope>(
            """{"type":"room_chat","roomId":"room-1","playerName":"Legacy","playerNetId":"net:legacy","messageId":"legacy-1","messageText":"legacy room text","sentAtUnixMs":1784073600000}""");

        LanConnectChatChannelState server = new(LanConnectChatChannel.Server);
        server.Apply(ready);
        LanConnectChatChannelState room = new(LanConnectChatChannel.Room);
        room.AppendLegacyConfirmed(
            legacy.MessageId!,
            legacy.PlayerName!,
            legacy.PlayerNetId,
            legacy.MessageText!,
            DateTimeOffset.FromUnixTimeMilliseconds(legacy.SentAtUnixMs!.Value),
            isLocal: false,
            confirmedMessageLimit: 50);

        Assert.False(probe.Capabilities.SupportsRichServerChat);
        Assert.False(probe.Capabilities.SupportsRoomChat);
        Assert.Equal(new LanConnectChatFeatureVersions(), server.EnabledRichFeatures);
        Assert.False(LanConnectItemLinkCapture.ItemRefsEnabled(server.EnabledRichFeatures));
        Assert.False(LanConnectChatFeatureResolver.SupportsContent(
            new LanConnectChatContent(1, [new LanConnectEmojiSegment("heart")]),
            server.EnabledRichFeatures));
        Assert.Single(room.Messages);
        Assert.Equal("legacy room text", room.Messages[0].Text);
    }

    [Theory]
    [InlineData("card", "Missing.ModCard", "Unknown card")]
    [InlineData("relic", "Missing.ModRelic", "Unknown relic")]
    [InlineData("potion", "Missing.ModPotion", "Unknown potion")]
    public void Unknown_local_models_use_type_placeholders_without_leaking_ids(
        string itemType,
        string modelId,
        string expected)
    {
        LanConnectItemModelResolver resolver = new(new MissingModelDbPort());

        LanConnectResolvedItem item = resolver.Resolve(
            new LanConnectItemRun(itemType, modelId),
            "en-US",
            "mods-a");

        Assert.Equal(LanConnectResolvedItemStatus.Unknown, item.Status);
        Assert.Equal(expected, item.AccessibleText);
        Assert.Null(item.LocalizedTitle);
        Assert.Null(item.Preview);
        Assert.DoesNotContain(modelId, item.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_emoji_and_kind_are_rejected_without_channel_state_corruption()
    {
        LanConnectChatChannelState state = EnabledRoomState();
        state.RichDraft.ReplaceAllWithText("keep draft");
        state.AppendConfirmedForTests("existing", "Existing", "keep message", 1, false);
        StateSnapshot before = Capture(state);

        LanConnectRoomChatMessageEnvelope unknownEmoji = Deserialize<LanConnectRoomChatMessageEnvelope>(
            """{"type":"room_chat_message","protocolVersion":1,"message":{"roomId":"room-1","roomSessionId":"session-1","messageId":"unknown-emoji","senderId":"net:alice","senderName":"Alice","content":{"formatVersion":1,"segments":[{"kind":"emoji","emojiId":"not-in-set"}]},"plainTextFallback":"[Emoji]","sentAt":"2026-07-15T00:00:00.000Z"}}""");
        state.Apply(unknownEmoji, "net:local");
        AssertState(before, state);

        Assert.Throws<JsonException>(() => Deserialize<LanConnectRoomChatMessageEnvelope>(
            """{"type":"room_chat_message","protocolVersion":1,"message":{"roomId":"room-1","roomSessionId":"session-1","messageId":"unknown-kind","senderId":"net:alice","senderName":"Alice","content":{"formatVersion":1,"segments":[{"kind":"future_kind"}]},"plainTextFallback":"[Entity]","sentAt":"2026-07-15T00:00:00.000Z"}}"""));
        AssertState(before, state);
    }

    [Fact]
    public void Canonical_type_table_and_room_envelopes_have_exact_names_and_wire_shapes()
    {
        Assert.Equal("LanConnectChatContent", typeof(LanConnectChatContent).Name);
        Assert.Equal("LanConnectRoomChatReadyEnvelope", typeof(LanConnectRoomChatReadyEnvelope).Name);
        Assert.Equal("LanConnectRoomChatV2Envelope", typeof(LanConnectRoomChatV2Envelope).Name);
        Assert.Equal("LanConnectRoomChatAckEnvelope", typeof(LanConnectRoomChatAckEnvelope).Name);
        Assert.Equal("LanConnectRoomChatMessageEnvelope", typeof(LanConnectRoomChatMessageEnvelope).Name);
        Assert.Equal("LanConnectRoomChatErrorEnvelope", typeof(LanConnectRoomChatErrorEnvelope).Name);
        Assert.Equal("LanConnectPowerStateSegment", typeof(LanConnectPowerStateSegment).Name);
        Assert.Equal("LanConnectTargetRefSegment", typeof(LanConnectTargetRefSegment).Name);

        AssertRoundTrip<LanConnectRoomChatReadyEnvelope>(
            "{\"type\":\"room_chat_ready\",\"protocolVersion\":1,\"roomId\":\"room-1\",\"roomSessionId\":\"session-1\",\"enabledFeatures\":" + Features + "}");
        AssertRoundTrip<LanConnectRoomChatV2Envelope>(
            "{\"type\":\"room_chat_v2\",\"protocolVersion\":1,\"clientMessageId\":\"22222222-2222-4222-8222-222222222222\",\"roomId\":\"room-1\",\"roomSessionId\":\"session-1\",\"content\":" + Content + "}");
        AssertRoundTrip<LanConnectRoomChatAckEnvelope>(
            "{\"type\":\"room_chat_ack\",\"protocolVersion\":1,\"clientMessageId\":\"22222222-2222-4222-8222-222222222222\",\"message\":" + RoomMessage + "}");
        AssertRoundTrip<LanConnectRoomChatMessageEnvelope>(
            "{\"type\":\"room_chat_message\",\"protocolVersion\":1,\"message\":" + RoomMessage + "}");
        AssertRoundTrip<LanConnectRoomChatErrorEnvelope>(
            "{\"type\":\"room_chat_error\",\"protocolVersion\":1,\"clientMessageId\":\"22222222-2222-4222-8222-222222222222\",\"code\":\"invalid_content\",\"message\":\"Rejected.\"}");

        string chatSources = string.Join(
            "\n",
            Directory.GetFiles(
                    Path.Combine(FindRepositoryRoot(), "sts2-lan-connect", "Scripts", "Lobby", "Chat"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        foreach (string forbidden in new[]
        {
            "Chat" + "Content",
            "RoomChat" + "ReadyEnvelope",
            "RoomChat" + "SendEnvelope",
            "RoomChat" + "AckEnvelope",
            "RoomChat" + "MessageEnvelope",
            "RoomChat" + "ErrorEnvelope",
            "PowerState" + "Segment",
            "TargetRef" + "Segment"
        })
        {
            Assert.DoesNotMatch(
                new Regex($@"\b(?:class|record)\s+{Regex.Escape(forbidden)}\b"),
                chatSources);
        }
    }

    [Fact]
    public void Server_switch_and_room_leave_clear_only_their_owned_rich_context()
    {
        LanConnectChatChannelState server = EnabledServerState();
        LanConnectDualChatState state = new(server);
        state.EnterRoom("room-a");
        state.Room.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 0));
        server.RichDraft.InsertEntity(new LanConnectEmojiRun("heart"));
        state.Room.RichDraft.InsertEntity(new LanConnectItemRun("relic", "MegaCrit.Anchor"));
        server.AppendConfirmedForTests("server-1", "Server", "server cache", 1, false);
        state.Room.AppendConfirmedForTests("room-1", "Room", "room cache", 2, false);

        state.ClearServerContext();

        Assert.Equal(string.Empty, server.Draft);
        Assert.DoesNotContain(server.RichDraft.Runs, run => run is LanConnectEmojiRun or LanConnectItemRun);
        Assert.Empty(server.Messages);
        Assert.Single(state.Room.RichDraft.Runs);
        Assert.Single(state.Room.Messages);

        server.RichDraft.InsertEntity(new LanConnectEmojiRun("smile"));
        server.AppendConfirmedForTests("server-2", "Server", "preserve", 3, false);
        state.LeaveRoom();

        Assert.Single(server.RichDraft.Runs);
        Assert.Single(server.Messages);
        Assert.Equal(string.Empty, state.Room.Draft);
        Assert.DoesNotContain(state.Room.RichDraft.Runs, run => run is LanConnectEmojiRun or LanConnectItemRun);
        Assert.Empty(state.Room.Messages);
    }

    [Fact]
    public void Production_owns_one_extended_input_router_and_one_shared_localizer()
    {
        string scripts = Path.Combine(FindRepositoryRoot(), "sts2-lan-connect", "Scripts");
        string[] sources = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories);
        string combined = string.Join("\n", sources.Select(File.ReadAllText));

        Assert.Single(Regex.Matches(combined, @"\bclass\s+LanConnectChatLocalizer\b").Cast<Match>());
        Assert.Single(Regex.Matches(combined, @"\bclass\s+LanConnectChatInputRouter\b").Cast<Match>());
        Assert.Single(Regex.Matches(combined, @"\bclass\s+\w*Chat\w*InputRouter\b").Cast<Match>());
        Assert.DoesNotContain("class LanConnectChatStrings", combined, StringComparison.Ordinal);
        Assert.Same(LanConnectChatUiComposition.Localizer, LanConnectChatUiComposition.Localizer);
        Assert.Equal(
            LanConnectChatInputAction.CloseEmojiPicker,
            LanConnectChatInputRouter.RouteEscape(true, true, true, true, true));
        Assert.Equal(
            LanConnectChatInputAction.ClosePreview,
            LanConnectChatInputRouter.RouteEscape(false, false, true, true, true));
    }

    [Fact]
    public async Task New_control_client_declares_phase_three_versions_in_hello()
    {
        RecordingWebSocket socket = new();
        await using LobbyControlClient client = new(socket);

        await client.ConnectClientAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "ticket-1",
            "Alice",
            "net:alice",
            "session-1",
            CancellationToken.None);

        using JsonDocument payload = JsonDocument.Parse(Assert.Single(socket.SentPayloads));
        JsonElement root = payload.RootElement;
        Assert.Equal("client_hello", root.GetProperty("type").GetString());
        Assert.Equal("net:alice", root.GetProperty("playerNetId").GetString());
        JsonElement versions = root.GetProperty("roomChatVersions");
        Assert.Equal(1, versions.GetProperty("richContentVersion").GetInt32());
        Assert.Equal(1, versions.GetProperty("emojiSetVersion").GetInt32());
        Assert.Equal(1, versions.GetProperty("itemRefVersion").GetInt32());
        Assert.Equal(0, versions.GetProperty("combatRefVersion").GetInt32());
    }

    private static LanConnectChatChannelState EnabledServerState()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(new LanConnectServerChatReadyEnvelope
        {
            InstanceId = "instance-1",
            HistoryEpoch = 1,
            ChatEnabled = true,
            ServerChatVersion = 1,
            EnabledFeatures = new LanConnectChatFeatureVersions(1, 1, 1, 0)
        });
        return state;
    }

    private static LanConnectChatChannelState EnabledRoomState()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        state.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 0));
        return state;
    }

    private static StateSnapshot Capture(LanConnectChatChannelState state) => new(
        state.Revision,
        state.Draft,
        state.Messages.Select(message => (message.MessageId ?? string.Empty, message.Text)).ToArray());

    private static void AssertState(StateSnapshot expected, LanConnectChatChannelState actual)
    {
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.Draft, actual.Draft);
        Assert.Equal(
            expected.Messages,
            actual.Messages.Select(message => (message.MessageId ?? string.Empty, message.Text)));
    }

    private static void AssertRoundTrip<T>(string json) =>
        Assert.Equal(json, JsonSerializer.Serialize(Deserialize<T>(json), LanConnectChatJson.Options));

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, LanConnectJson.Options) ??
        throw new InvalidOperationException("Fixture returned null.");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed record StateSnapshot(
        long Revision,
        string Draft,
        IReadOnlyList<(string MessageId, string Text)> Messages);

    private sealed class MissingModelDbPort : ILanConnectModelDbPort
    {
        public object DeserializeModelId(string value) => value;
        public bool TryGetCard(object id, out object model) { model = null!; return false; }
        public bool TryGetRelic(object id, out object model) { model = null!; return false; }
        public bool TryGetPotion(object id, out object model) { model = null!; return false; }
        public string GetLocalizedTitle(object model) => throw new InvalidOperationException();
        public int GetSupportedCardUpgradeLevel(object card) => throw new InvalidOperationException();
        public object CreateCardPreviewCopy(object card, int upgradeLevel) => throw new InvalidOperationException();
        public LanConnectHoverTipPreviewData CreateRelicPreviewData(object relic) => throw new InvalidOperationException();
        public LanConnectHoverTipPreviewData CreatePotionPreviewData(object potion) => throw new InvalidOperationException();
    }

    private sealed class RecordingWebSocket : ILanConnectWebSocket
    {
        private readonly Channel<byte[]> _frames = Channel.CreateUnbounded<byte[]>();

        internal List<string> SentPayloads { get; } = [];

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
            byte[] payload = await _frames.Reader.ReadAsync(cancellationToken);
            payload.CopyTo(buffer);
            return new ValueWebSocketReceiveResult(payload.Length, WebSocketMessageType.Text, true);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            SentPayloads.Add(Encoding.UTF8.GetString(buffer.Span));
            return ValueTask.CompletedTask;
        }

        public Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken)
        {
            State = WebSocketState.Closed;
            _frames.Writer.TryComplete();
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
    }
}
