using System;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectRichProtocolFixtureTests
{
    private const string Features =
        "{\"richContentVersion\":1,\"emojiSetVersion\":1,\"itemRefVersion\":1,\"combatRefVersion\":0}";
    private const string Content =
        "{\"formatVersion\":1,\"segments\":[{\"kind\":\"text\",\"text\":\"look \"},{\"kind\":\"emoji\",\"emojiId\":\"thumbs-up\"},{\"kind\":\"item_ref\",\"itemType\":\"card\",\"modelId\":\"MegaCrit.Strike\",\"upgradeLevel\":1},{\"kind\":\"item_ref\",\"itemType\":\"relic\",\"modelId\":\"MegaCrit.Anchor\"},{\"kind\":\"item_ref\",\"itemType\":\"potion\",\"modelId\":\"MegaCrit.FirePotion\"}]}";
    private const string ServerMessage =
        "{\"messageId\":\"00000000-0000-4000-8000-000000000001\",\"senderId\":\"ABCDEFGHIJKLMNOPQRSTUV\",\"senderName\":\"Ironclad\",\"content\":" + Content + ",\"plainTextFallback\":\"look [Emoji][Card][Relic][Potion]\",\"sentAt\":\"2026-07-12T12:00:00.123Z\"}";
    private const string RoomMessage =
        "{\"roomId\":\"room-1\",\"roomSessionId\":\"room-session-1\",\"messageId\":\"00000000-0000-4000-8000-000000000001\",\"senderId\":\"net:ironclad\",\"senderName\":\"Ironclad\",\"content\":" + Content + ",\"plainTextFallback\":\"look [Emoji][Card][Relic][Potion]\",\"sentAt\":\"2026-07-12T12:00:00.123Z\"}";

    [Fact]
    public void MixedContentRoundTripsWithExactCamelCaseAndTypedSegments()
    {
        LanConnectChatContent content = Deserialize<LanConnectChatContent>(Content);

        Assert.Collection(content.Segments,
            segment => Assert.IsType<LanConnectTextSegment>(segment),
            segment => Assert.IsType<LanConnectEmojiSegment>(segment),
            segment => Assert.IsType<LanConnectItemRefSegment>(segment),
            segment => Assert.IsType<LanConnectItemRefSegment>(segment),
            segment => Assert.IsType<LanConnectItemRefSegment>(segment));
        Assert.Equal(Content, Serialize(content));
        Assert.DoesNotContain("display", Serialize(content), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url", Serialize(content), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerEnvelopeFamilyRoundTripsExactly()
    {
        AssertRoundTrip<LanConnectServerChatReadyEnvelope>(
            "{\"type\":\"chat_ready\",\"protocolVersion\":1,\"channel\":\"server\",\"sessionId\":\"session-1\",\"senderId\":\"ABCDEFGHIJKLMNOPQRSTUV\",\"instanceId\":\"instance-1\",\"historyEpoch\":3,\"chatEnabled\":true,\"serverChatVersion\":1,\"enabledFeatures\":" + Features + "}");
        AssertRoundTrip<LanConnectServerChatStateEnvelope>(
            "{\"type\":\"chat_state\",\"protocolVersion\":1,\"chatEnabled\":true,\"enabledFeatures\":" + Features + ",\"historyEpoch\":3,\"changedAt\":\"2026-07-12T12:00:00.123Z\"}");
        AssertRoundTrip<LanConnectServerChatSendEnvelope>(
            "{\"type\":\"chat_send\",\"protocolVersion\":1,\"channel\":\"server\",\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"content\":" + Content + "}");
        AssertRoundTrip<LanConnectServerChatAckEnvelope>(
            "{\"type\":\"chat_ack\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"message\":" + ServerMessage + "}");
        AssertRoundTrip<LanConnectServerChatMessageEnvelope>(
            "{\"type\":\"chat_message\",\"protocolVersion\":1,\"message\":" + ServerMessage + "}");
        AssertRoundTrip<LanConnectServerChatErrorEnvelope>(
            "{\"type\":\"chat_error\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"code\":\"rate_limited\",\"message\":\"Try later.\",\"retryAfterMs\":2000}");
        AssertRoundTrip<LanConnectServerChatMessagePayload>(ServerMessage);
    }

    [Fact]
    public void RoomEnvelopeFamilyRoundTripsExactly()
    {
        AssertRoundTrip<LanConnectRoomChatReadyEnvelope>(
            "{\"type\":\"room_chat_ready\",\"protocolVersion\":1,\"roomId\":\"room-1\",\"roomSessionId\":\"room-session-1\",\"enabledFeatures\":" + Features + "}");
        AssertRoundTrip<LanConnectRoomChatV2Envelope>(
            "{\"type\":\"room_chat_v2\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"roomId\":\"room-1\",\"roomSessionId\":\"room-session-1\",\"content\":" + Content + "}");
        AssertRoundTrip<LanConnectRoomChatAckEnvelope>(
            "{\"type\":\"room_chat_ack\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"message\":" + RoomMessage + "}");
        AssertRoundTrip<LanConnectRoomChatMessageEnvelope>(
            "{\"type\":\"room_chat_message\",\"protocolVersion\":1,\"message\":" + RoomMessage + "}");
        AssertRoundTrip<LanConnectRoomChatErrorEnvelope>(
            "{\"type\":\"room_chat_error\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"code\":\"invalid_content\",\"message\":\"Rejected.\"}");
        AssertRoundTrip<LanConnectRoomChatMessagePayload>(RoomMessage);
    }

    [Fact]
    public void OldTextContentAndAbsentCapabilityFieldsRemainCompatible()
    {
        const string oldContent =
            "{\"formatVersion\":1,\"segments\":[{\"kind\":\"text\",\"text\":\"legacy\"}]}";
        AssertRoundTrip<LanConnectChatContent>(oldContent);

        LanConnectChatFeatureVersions features = Deserialize<LanConnectChatFeatureVersions>("{}");
        Assert.Equal(new LanConnectChatFeatureVersions(0, 0, 0, 0), features);
    }

    [Fact]
    public void CanonicalRichRootsRejectAlternateCasingAndCaseCollisions()
    {
        AssertStrictRoot<LanConnectChatFeatureVersions>(Features, "richContentVersion");
        AssertStrictRoot<LanConnectChatContent>(Content, "formatVersion");
        AssertStrictRoot<LanConnectServerChatReadyEnvelope>(
            "{\"type\":\"chat_ready\",\"protocolVersion\":1,\"channel\":\"server\",\"sessionId\":\"session-1\",\"senderId\":\"ABCDEFGHIJKLMNOPQRSTUV\",\"instanceId\":\"instance-1\",\"historyEpoch\":3,\"chatEnabled\":true,\"serverChatVersion\":1,\"enabledFeatures\":" + Features + "}", "type");
        AssertStrictRoot<LanConnectServerChatStateEnvelope>(
            "{\"type\":\"chat_state\",\"protocolVersion\":1,\"chatEnabled\":true,\"enabledFeatures\":" + Features + ",\"historyEpoch\":3,\"changedAt\":\"2026-07-12T12:00:00.123Z\"}", "type");
        AssertStrictRoot<LanConnectServerChatSendEnvelope>(
            "{\"type\":\"chat_send\",\"protocolVersion\":1,\"channel\":\"server\",\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"content\":" + Content + "}", "type");
        AssertStrictRoot<LanConnectServerChatAckEnvelope>(
            "{\"type\":\"chat_ack\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"message\":" + ServerMessage + "}", "type");
        AssertStrictRoot<LanConnectServerChatMessageEnvelope>(
            "{\"type\":\"chat_message\",\"protocolVersion\":1,\"message\":" + ServerMessage + "}", "type");
        AssertStrictRoot<LanConnectServerChatErrorEnvelope>(
            "{\"type\":\"chat_error\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"code\":\"invalid_content\",\"message\":\"Rejected.\"}", "type");
        AssertStrictRoot<LanConnectServerChatMessagePayload>(ServerMessage, "messageId");
        AssertStrictRoot<LanConnectRoomChatReadyEnvelope>(
            "{\"type\":\"room_chat_ready\",\"protocolVersion\":1,\"roomId\":\"room-1\",\"roomSessionId\":\"room-session-1\",\"enabledFeatures\":" + Features + "}", "type");
        AssertStrictRoot<LanConnectRoomChatV2Envelope>(
            "{\"type\":\"room_chat_v2\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"roomId\":\"room-1\",\"roomSessionId\":\"room-session-1\",\"content\":" + Content + "}", "type");
        AssertStrictRoot<LanConnectRoomChatAckEnvelope>(
            "{\"type\":\"room_chat_ack\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"message\":" + RoomMessage + "}", "type");
        AssertStrictRoot<LanConnectRoomChatMessageEnvelope>(
            "{\"type\":\"room_chat_message\",\"protocolVersion\":1,\"message\":" + RoomMessage + "}", "type");
        AssertStrictRoot<LanConnectRoomChatErrorEnvelope>(
            "{\"type\":\"room_chat_error\",\"protocolVersion\":1,\"clientMessageId\":\"00000000-0000-4000-8000-000000000002\",\"code\":\"invalid_content\",\"message\":\"Rejected.\"}", "type");
        AssertStrictRoot<LanConnectRoomChatMessagePayload>(RoomMessage, "roomId");
    }

    [Theory]
    [InlineData("{\"formatVersion\":1,\"segments\":[{\"kind\":\"unknown\"}]}")]
    [InlineData("{\"formatVersion\":1,\"segments\":[{\"kind\":\"text\",\"text\":\"x\",\"senderName\":\"spoof\"}]}")]
    [InlineData("{\"formatVersion\":1,\"segments\":[{\"kind\":\"emoji\",\"emojiId\":1}]}")]
    [InlineData("{\"formatVersion\":1,\"segments\":[{\"kind\":\"item_ref\",\"itemType\":\"relic\",\"modelId\":\"A\",\"upgradeLevel\":1}]}")]
    [InlineData("{\"formatVersion\":1,\"segments\":[{\"kind\":\"text\"}]}")]
    [InlineData("{\"formatVersion\":1,\"segments\":[],\"senderId\":\"spoof\"}")]
    [InlineData("{\"segments\":[]}")]
    public void SegmentConverterRejectsUnknownKindsFieldsAndWrongTypes(string json)
    {
        Assert.Throws<JsonException>(() => Deserialize<LanConnectChatContent>(json));
    }

    [Fact]
    public void ReservedCombatSegmentsDeserializeButPhaseThreeRejectsBothChannels()
    {
        const string json =
            "{\"formatVersion\":1,\"segments\":[{\"kind\":\"power_state\",\"modelId\":\"MegaCrit.Strength\",\"amount\":-2,\"roomSessionId\":\"room-session-1\",\"ownerPlayerNetId\":\"net:a\",\"applierPlayerNetId\":\"net:b\"},{\"kind\":\"target_ref\",\"targetKind\":\"monster\",\"targetKey\":\"monster-1\",\"roomSessionId\":\"room-session-1\"}]}";
        LanConnectChatContent content = Deserialize<LanConnectChatContent>(json);

        Assert.IsType<LanConnectPowerStateSegment>(content.Segments[0]);
        Assert.IsType<LanConnectTargetRefSegment>(content.Segments[1]);
        Assert.Equal(json, Serialize(content));
        LanConnectChatFeatureVersions phaseThree = new(1, 1, 1, 0);
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectServerChatProtocol.Canonicalize(content, phaseThree));
        Assert.False(LanConnectChatFeatureResolver.SupportsContent(content, phaseThree));

        LanConnectChatFeatureVersions room = LanConnectChatFeatureResolver.Resolve(new LanConnectChatFeatureInput
        {
            Channel = LanConnectChatChannel.Room,
            Compiled = new(1, 1, 1, 1),
            Configured = new(1, 1, 1, 1),
            ChannelEnabled = true,
            RoomV2Enabled = true,
            Sender = new(1, 1, 1, 1),
            Receiver = new(1, 1, 1, 1)
        });
        Assert.Equal(0, room.CombatRefVersion);
        Assert.False(LanConnectChatFeatureResolver.SupportsContent(content, room));
    }

    private static void AssertRoundTrip<T>(string json) => Assert.Equal(json, Serialize(Deserialize<T>(json)));

    private static void AssertStrictRoot<T>(string json, string field)
    {
        string alternate = char.ToUpperInvariant(field[0]) + field[1..];
        string token = $"\"{field}\":";
        string alternateToken = $"\"{alternate}\":";
        Assert.Throws<JsonException>(() => Deserialize<T>(
            json.Replace(token, alternateToken, StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => Deserialize<T>(
            json.Replace(token, $"{alternateToken}null,{token}", StringComparison.Ordinal)));
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, LanConnectChatJson.Options) ??
        throw new InvalidOperationException("Fixture returned null.");

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, LanConnectChatJson.Options);
}
