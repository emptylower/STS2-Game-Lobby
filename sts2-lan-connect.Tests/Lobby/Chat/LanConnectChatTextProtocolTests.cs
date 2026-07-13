using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatTextProtocolTests
{
    private static readonly DateTimeOffset FixedIsoTimestamp = DateTimeOffset.Parse("2026-07-13T04:05:06.123Z");
    private const string SenderIdFixture = "ABCDEFGHIJKLMNOPQRSTUV"; // 22 ASCII bytes
    private const string MessageIdFixture = "01234567-89ab-cdef-0123-456789abcdef"; // 36 ASCII bytes
    private const string SenderNameFixture = "Ironclad"; // 8 bytes
    private const string TimestampFixture = "2026-07-13T04:05:06.123Z"; // 24 bytes
    [Fact]
    public void CanonicalizeTextNormalizesNfcNewlinesAndTrims()
    {
        string input = "  e\u0301\r\n\rworld  ";
        string expected = "é\n\nworld";

        ServerChatContent content = LanConnectChatTextProtocol.CanonicalizeText(input);

        ServerChatTextSegment segment = Assert.Single(content.Segments);
        Assert.Equal("text", segment.Kind);
        Assert.Equal(expected, segment.Text);
        Assert.Equal(1, content.FormatVersion);
    }

    [Fact]
    public void CanonicalizeTextCollapsesCrlfAndTrims()
    {
        ServerChatContent content = LanConnectChatTextProtocol.CanonicalizeText("  hello\r\nworld  ");

        ServerChatTextSegment segment = Assert.Single(content.Segments);
        Assert.Equal("hello\nworld", segment.Text);
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\t")]
    [InlineData("\u0085")]
    [InlineData("\u202e")]
    [InlineData("\u200b")]
    [InlineData("\ufe0f")]
    public void CanonicalizeTextRejectsForbiddenControls(string forbidden)
    {
        Assert.Throws<InvalidOperationException>(() => LanConnectChatTextProtocol.CanonicalizeText($"a{forbidden}b"));
    }

    [Fact]
    public void CanonicalizeTextRejectsOverThreeHundredScalars()
    {
        string big = string.Concat(Enumerable.Repeat("中", 301));

        Assert.Throws<InvalidOperationException>(() => LanConnectChatTextProtocol.CanonicalizeText(big));
    }

    [Fact]
    public void AssertSendBudgetRejectsNonTextSegmentContent()
    {
        ServerChatContent rich = new()
        {
            Segments =
            [
                new ServerChatTextSegment { Kind = "text", Text = "hello" },
                new ServerChatTextSegment { Kind = "emoji", Text = ":smile:" }
            ]
        };

        Assert.Throws<InvalidOperationException>(() => LanConnectChatTextProtocol.AssertSendBudget(rich, SenderNameFixture));
    }

    [Fact]
    public void CountUnicodeScalarsUsesRuneEnumeration()
    {
        Assert.Equal(1, LanConnectChatTextProtocol.CountUnicodeScalars("a"));
        Assert.Equal(2, LanConnectChatTextProtocol.CountUnicodeScalars("e\u0301"));
        Assert.Equal(1, LanConnectChatTextProtocol.CountUnicodeScalars("é"));
        Assert.Equal(1, LanConnectChatTextProtocol.CountUnicodeScalars("\uD801\uDC37")); // surrogate pair = 1 rune
        Assert.Equal(300, LanConnectChatTextProtocol.CountUnicodeScalars(new string('a', 300)));
    }

    [Fact]
    public void AckEnvelopeAtExactBudgetRoundTrips()
    {
        ExactProjection projection = BuildExactProjection(8192);

        Assert.Equal(8192, projection.MaxBytes);
        Assert.Equal(projection.AckBytes, Utf8Bytes(projection.Ack));
        Assert.Equal(MessageIdFixture, projection.Ack.ClientMessageId);
    }

    [Fact]
    public void MessageEnvelopeAtExactBudgetRoundTrips()
    {
        ExactProjection projection = BuildExactProjection(8192);

        Assert.Equal(8192, projection.MaxBytes);
        Assert.Equal(projection.MessageBytes, Utf8Bytes(projection.Message));
        Assert.Equal(36, projection.Message.Message.MessageId.Length);
        Assert.Equal(22, projection.Message.Message.SenderId.Length);
        Assert.Equal(TimestampFixture, projection.Message.Message.SentAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"));
    }

    [Fact]
    public void MessageEnvelopeOverBudgetMeasuredTo8193()
    {
        ExactProjection projection = BuildExactProjection(8193);

        Assert.Equal(8193, projection.MaxBytes);
    }

    [Fact]
    public void SingleMessageSnapshotAtExactBudgetRoundTrips()
    {
        ExactProjection projection = BuildExactProjection(8192);

        Assert.Equal(8192, projection.MaxBytes);
        Assert.Equal(projection.SnapshotBytes, Utf8Bytes(projection.Snapshot));
        Assert.Single(projection.Snapshot.Messages);
    }

    [Fact]
    public void MeasureWorstCaseUsesLargestActualProjection()
    {
        ServerChatContent content = LanConnectChatTextProtocol.CanonicalizeText("hello");
        ServerChatCanonicalMessage canonical = BuildCanonical("hello", "hello");
        int expected = MeasureProjection(canonical).MaxBytes;

        Assert.Equal(expected, LanConnectChatTextProtocol.MeasureWorstCaseInboundBytes(content, SenderNameFixture));
    }

    private static ExactProjection BuildExactProjection(int targetBytes)
    {
        int low = 0;
        int high = targetBytes;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            ExactProjection candidate = MeasureProjection(BuildCanonical("hello", $"hello{new string('a', mid)}"));
            if (candidate.MaxBytes <= targetBytes)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        ExactProjection projection = MeasureProjection(BuildCanonical("hello", $"hello{new string('a', low)}"));
        Assert.Equal(targetBytes, projection.MaxBytes);
        return projection;
    }

    private static ExactProjection MeasureProjection(ServerChatCanonicalMessage canonical)
    {
        ServerChatAckEnvelope ack = new() { ClientMessageId = MessageIdFixture, Message = canonical };
        ServerChatMessageEnvelope message = new() { Message = canonical };
        ServerChatSnapshotChunkEnvelope snapshot = new()
        {
            SnapshotId = MessageIdFixture,
            ChunkIndex = 0,
            Messages = [canonical]
        };
        int ackBytes = Utf8Bytes(ack);
        int messageBytes = Utf8Bytes(message);
        int snapshotBytes = Utf8Bytes(snapshot);
        return new ExactProjection(ack, message, snapshot, ackBytes, messageBytes, snapshotBytes,
            Math.Max(ackBytes, Math.Max(messageBytes, snapshotBytes)));
    }

    private static int Utf8Bytes<T>(T value) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value, LanConnectJson.Options));

    private static ServerChatCanonicalMessage BuildCanonical(string text, string? fallback = null) =>
        new()
        {
            MessageId = MessageIdFixture,
            SenderId = SenderIdFixture,
            SenderName = SenderNameFixture,
            Content = new ServerChatContent
            {
                FormatVersion = 1,
                Segments = [new ServerChatTextSegment { Kind = "text", Text = text }]
            },
            PlainTextFallback = fallback ?? text,
            SentAt = FixedIsoTimestamp
        };

    private sealed record ExactProjection(
        ServerChatAckEnvelope Ack,
        ServerChatMessageEnvelope Message,
        ServerChatSnapshotChunkEnvelope Snapshot,
        int AckBytes,
        int MessageBytes,
        int SnapshotBytes,
        int MaxBytes);
}
