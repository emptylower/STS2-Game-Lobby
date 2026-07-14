using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatFeatureResolverTests
{
    private static readonly LanConnectChatFeatureVersions All = new(1, 1, 1, 1);
    private static readonly LanConnectChatFeatureVersions None = new(0, 0, 0, 0);

    [Fact]
    public void ResolverMirrorsOperatorChannelAndPeerPrecedence()
    {
        Assert.Equal(None, Resolve(compiled: new(0, 1, 1, 1)));
        Assert.Equal(new LanConnectChatFeatureVersions(1, 1, 1, 0),
            Resolve(configured: None, admin: All));
        Assert.Equal(new LanConnectChatFeatureVersions(1, 0, 1, 0), Resolve(
            configured: All,
            adminOverrides: new LanConnectChatFeatureOverrides(EmojiSetVersion: 0)));
        Assert.Equal(new LanConnectChatFeatureVersions(1, 1, 0, 0),
            LanConnectChatFeatureResolver.Resolve(new LanConnectChatFeatureInput
            {
                Channel = LanConnectChatChannel.Server,
                Compiled = All,
                Configured = new LanConnectChatFeatureVersions(1, 1, 0, 1),
                Admin = null,
                ChannelEnabled = true
            }));
        Assert.Equal(new LanConnectChatFeatureVersions(1, 0, 1, 0),
            Resolve(compiled: new(1, 0, 1, 1)));
        Assert.Equal(None, Resolve(channelEnabled: false));
        Assert.Equal(None, Resolve(channel: LanConnectChatChannel.Room, roomV2Enabled: false));
        Assert.Equal(new LanConnectChatFeatureVersions(1, 1, 0, 0), Resolve(
            channel: LanConnectChatChannel.Room,
            receiver: new(1, 1, 0, 1)));
        Assert.Equal(None, Resolve(
            channel: LanConnectChatChannel.Room,
            sender: new(0, 1, 1, 1)));
        Assert.Equal(0, Resolve(channel: LanConnectChatChannel.Room).CombatRefVersion);
    }

    [Fact]
    public void SupportsContentChecksWholeMessageAndTreatsTextAsLegacySafe()
    {
        LanConnectChatContent mixed = new(1,
        [
            new LanConnectTextSegment("look "),
            new LanConnectEmojiSegment("heart"),
            new LanConnectItemRefSegment("card", "MegaCrit.Strike", 1)
        ]);

        Assert.True(LanConnectChatFeatureResolver.SupportsContent(mixed, new(1, 1, 1, 0)));
        Assert.False(LanConnectChatFeatureResolver.SupportsContent(mixed, new(1, 0, 1, 0)));
        Assert.False(LanConnectChatFeatureResolver.SupportsContent(mixed, new(1, 1, 0, 0)));
        Assert.True(LanConnectChatFeatureResolver.SupportsContent(
            new(1, [new LanConnectTextSegment("legacy")]), None));
        Assert.Equal(3, mixed.Segments.Count);
    }

    [Fact]
    public void CanonicalizeNormalizesMergesBoundsAndRendersDeterministicFallback()
    {
        LanConnectChatContent content = new(1,
        [
            new LanConnectTextSegment("  e\u0301\r\n"),
            new LanConnectTextSegment("look "),
            new LanConnectEmojiSegment("thumbs-up"),
            new LanConnectItemRefSegment("card", "MegaCrit.Strike", 1),
            new LanConnectItemRefSegment("relic", "MegaCrit.Anchor"),
            new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion"),
            new LanConnectTextSegment("  ")
        ]);

        LanConnectChatContent canonical = LanConnectServerChatProtocol.Canonicalize(content, new(1, 1, 1, 0));

        Assert.Equal("é\nlook ", Assert.IsType<LanConnectTextSegment>(canonical.Segments[0]).Text);
        Assert.Equal("é\nlook [Emoji][Card][Relic][Potion]",
            LanConnectServerChatProtocol.RenderGenericFallback(canonical));
        Assert.Equal(
            "{\"formatVersion\":1,\"segments\":[{\"kind\":\"text\",\"text\":\"é\\nlook \"},{\"kind\":\"emoji\",\"emojiId\":\"thumbs-up\"},{\"kind\":\"item_ref\",\"itemType\":\"card\",\"modelId\":\"MegaCrit.Strike\",\"upgradeLevel\":1},{\"kind\":\"item_ref\",\"itemType\":\"relic\",\"modelId\":\"MegaCrit.Anchor\"},{\"kind\":\"item_ref\",\"itemType\":\"potion\",\"modelId\":\"MegaCrit.FirePotion\"}]}",
            LanConnectServerChatProtocol.DeterministicContentJson(canonical));

        Assert.Equal(300, LanConnectServerChatProtocol.CountUnicodeScalars(
            Assert.IsType<LanConnectTextSegment>(LanConnectServerChatProtocol.Canonicalize(
                new(1, [new LanConnectTextSegment(string.Concat(Enumerable.Repeat("\U00010000", 300)))]),
                None).Segments[0]).Text));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectTextSegment(string.Concat(Enumerable.Repeat("\U00010000", 301)))]), None));
        _ = LanConnectServerChatProtocol.Canonicalize(
            new(1, Enumerable.Range(0, 32).Select(index =>
                (LanConnectChatSegment)new LanConnectTextSegment(index == 31 ? "x" : string.Empty)).ToArray()), None);
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, Enumerable.Range(0, 33).Select(_ =>
                (LanConnectChatSegment)new LanConnectTextSegment(string.Empty)).ToArray()), None));
        _ = LanConnectServerChatProtocol.Canonicalize(
            new(1, Enumerable.Range(0, 12).Select(index =>
                (LanConnectChatSegment)new LanConnectEmojiSegment(index % 2 == 0 ? "smile" : "x")).ToArray()),
            new(1, 1, 0, 0));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, Enumerable.Range(0, 13).Select(_ =>
                (LanConnectChatSegment)new LanConnectEmojiSegment("smile")).ToArray()), new(1, 1, 0, 0)));
    }

    [Fact]
    public void CanonicalizeRejectsFeatureSchemaAndTextViolations()
    {
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectEmojiSegment("smile")]), None));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectEmojiSegment("not-in-set")]), new(1, 1, 0, 0)));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectEmojiSegment("not-in-set")]), None));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectItemRefSegment("card", "bad/model")]), new(1, 0, 1, 0)));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectItemRefSegment("card", "bad/model")]), None));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectItemRefSegment("card", "MegaCrit.Strike\n")]),
            new(1, 0, 1, 0)));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectItemRefSegment("card", new string('M', 160) + "\n")]),
            new(1, 0, 1, 0)));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectItemRefSegment("card", "MegaCrit.Strike\r\n")]),
            new(1, 0, 1, 0)));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectItemRefSegment("card", new string('M', 161))]),
            new(1, 0, 1, 0)));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectItemRefSegment("card", "MegaCrit.Strike", 10)]), new(1, 0, 1, 0)));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectItemRefSegment("relic", "MegaCrit.Anchor", 1)]), new(1, 0, 1, 0)));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectTextSegment(" \r\n ")]), None));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectTextSegment("a\u202eb")]), None));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectTextSegment("a\ud800b")]), None));
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.Canonicalize(
            new(1, Enumerable.Range(0, 13).Select(_ =>
                (LanConnectChatSegment)new LanConnectEmojiSegment("smile")).ToArray()), None));
    }

    [Fact]
    public void EmojiSetOneWhitelistIsExact()
    {
        string[] expected =
        [
            "smile", "laugh", "heart", "thumbs-up", "thumbs-down", "sparkles",
            "flame", "zap", "shield", "swords", "target", "crown",
            "skull", "ghost", "eye", "message-circle", "check", "x"
        ];

        Assert.Equal(expected, LanConnectServerChatProtocol.EmojiSet1);
        Assert.Equal(18, LanConnectServerChatProtocol.EmojiSet1.Distinct(StringComparer.Ordinal).Count());
        foreach (string emojiId in expected)
        {
            _ = LanConnectServerChatProtocol.Canonicalize(
                new(1, [new LanConnectEmojiSegment(emojiId)]),
                new(1, 1, 0, 0));
        }
    }

    [Fact]
    public void WorstCaseInboundMeasurementCoversAckMessageAndSnapshotIndex999AtBoundary()
    {
        LanConnectChatContent content = LanConnectServerChatProtocol.Canonicalize(
            new(1, [new LanConnectTextSegment("wire")]), None);
        string senderAt8192 = FindExactSenderName(content, 8192);
        string senderAt8193 = senderAt8192 + "S";

        Assert.Equal(8192, LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(content, senderAt8192));
        Assert.Equal(8193, LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(content, senderAt8193));
        LanConnectServerChatProtocol.AssertInboundBudget(content, senderAt8192);
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectServerChatProtocol.AssertInboundBudget(content, senderAt8193));

        LanConnectServerChatMessagePayload payload = new()
        {
            MessageId = LanConnectServerChatProtocol.WorstCaseMessageId,
            SenderId = LanConnectServerChatProtocol.WorstCaseSenderId,
            SenderName = senderAt8192,
            Content = content,
            PlainTextFallback = LanConnectServerChatProtocol.RenderGenericFallback(content),
            SentAt = LanConnectServerChatProtocol.WorstCaseSentAt
        };
        int ack = Utf8Bytes(new LanConnectServerChatAckEnvelope
        {
            ClientMessageId = LanConnectServerChatProtocol.WorstCaseMessageId,
            Message = payload
        });
        int message = Utf8Bytes(new LanConnectServerChatMessageEnvelope { Message = payload });
        int snapshot = Utf8Bytes(new
        {
            type = "chat_snapshot_chunk",
            protocolVersion = 1,
            snapshotId = LanConnectServerChatProtocol.WorstCaseMessageId,
            chunkIndex = 999,
            messages = new[] { payload }
        });
        Assert.Equal(Math.Max(ack, Math.Max(message, snapshot)),
            LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(content, senderAt8192));
        Assert.Equal(8192, snapshot);

        LanConnectChatContent maxLegal = BuildMaximumLegalContent();
        Assert.True(LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(
            maxLegal, new string('S', 32)) < 8192);
    }

    private static LanConnectChatFeatureVersions Resolve(
        LanConnectChatChannel channel = LanConnectChatChannel.Server,
        LanConnectChatFeatureVersions? compiled = null,
        LanConnectChatFeatureVersions? configured = null,
        LanConnectChatFeatureVersions? admin = null,
        LanConnectChatFeatureOverrides? adminOverrides = null,
        bool channelEnabled = true,
        bool roomV2Enabled = true,
        LanConnectChatFeatureVersions? sender = null,
        LanConnectChatFeatureVersions? receiver = null) =>
        LanConnectChatFeatureResolver.Resolve(new LanConnectChatFeatureInput
        {
            Channel = channel,
            Compiled = compiled ?? All,
            Configured = configured ?? All,
            Admin = adminOverrides ?? new LanConnectChatFeatureOverrides(
                (admin ?? All).RichContentVersion,
                (admin ?? All).EmojiSetVersion,
                (admin ?? All).ItemRefVersion,
                (admin ?? All).CombatRefVersion),
            ChannelEnabled = channelEnabled,
            RoomV2Enabled = roomV2Enabled,
            Sender = sender ?? All,
            Receiver = receiver ?? All
        });

    private static string FindExactSenderName(LanConnectChatContent content, int target)
    {
        int baseline = LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(content, "S");
        int padding = target - baseline;
        Assert.True(padding >= 0);
        string senderName = new('S', padding + 1);
        Assert.Equal(target, LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(content, senderName));
        return senderName;
    }

    private static LanConnectChatContent BuildMaximumLegalContent()
    {
        List<LanConnectChatSegment> segments =
        [new LanConnectTextSegment(new string('T', 300))];
        for (int index = 0; index < 12; index++)
        {
            segments.Add(new LanConnectItemRefSegment("card", new string('M', 160), 9));
        }
        return LanConnectServerChatProtocol.Canonicalize(new(1, segments), new(1, 1, 1, 0));
    }

    private static int Utf8Bytes<T>(T value) =>
        System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value, LanConnectChatJson.Options));
}
