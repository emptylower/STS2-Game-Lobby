using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectChatTextProtocol
{
    internal const int MaxUnicodeScalars = 300;

    private const string WorstCaseMessageId = "01234567-89ab-cdef-0123-456789abcdef";
    private const string WorstCaseSenderId = "ABCDEFGHIJKLMNOPQRSTUV";
    private static readonly DateTimeOffset WorstCaseSentAt =
        DateTimeOffset.Parse("2026-07-12T12:00:00.123Z", CultureInfo.InvariantCulture);

    internal static ServerChatContent CanonicalizeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string normalized = text.Normalize(NormalizationForm.FormC)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        AssertNoForbiddenControls(normalized);
        normalized = normalized.Trim();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("text content must not be blank.");
        }

        int scalarCount = CountUnicodeScalars(normalized);
        if (scalarCount > MaxUnicodeScalars)
        {
            throw new InvalidOperationException(
                $"text protocol budget exceeded: {scalarCount} Unicode scalars, max {MaxUnicodeScalars}.");
        }

        return new ServerChatContent
        {
            FormatVersion = 1,
            Segments = [new ServerChatTextSegment { Kind = "text", Text = normalized }]
        };
    }

    internal static int CountUnicodeScalars(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int count = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            count++;
        }
        return count;
    }

    internal static int MeasureWorstCaseInboundBytes(ServerChatContent content, string senderName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(senderName);

        string normalizedSenderName = senderName.Normalize(NormalizationForm.FormC).Trim();
        ServerChatCanonicalMessage canonical = new()
        {
            MessageId = WorstCaseMessageId,
            SenderId = WorstCaseSenderId,
            SenderName = normalizedSenderName,
            Content = content,
            PlainTextFallback = ExtractPlainFallback(content),
            SentAt = WorstCaseSentAt
        };

        ServerChatAckEnvelope ack = new()
        {
            ProtocolVersion = 1,
            ClientMessageId = WorstCaseMessageId,
            Message = canonical
        };
        ServerChatMessageEnvelope message = new()
        {
            ProtocolVersion = 1,
            Message = canonical
        };
        ServerChatSnapshotChunkEnvelope snapshot = new()
        {
            ProtocolVersion = 1,
            SnapshotId = WorstCaseMessageId,
            ChunkIndex = 0,
            Messages = [canonical]
        };

        return Math.Max(MeasureUtf8Bytes(ack), Math.Max(MeasureUtf8Bytes(message), MeasureUtf8Bytes(snapshot)));
    }

    internal static void AssertSendBudget(ServerChatContent content, string senderName, int maxPayloadBytes = 8192)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Segments == null || content.Segments.Count != 1)
        {
            throw new InvalidOperationException("text-only chat accepts exactly one segment.");
        }

        ServerChatTextSegment segment = content.Segments[0];
        if (segment == null || !string.Equals(segment.Kind, "text", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("text-only chat rejects non-text segments.");
        }

        string text = segment.Text ?? string.Empty;
        AssertNoForbiddenControls(text);
        int scalars = CountUnicodeScalars(text);
        if (scalars > MaxUnicodeScalars)
        {
            throw new InvalidOperationException(
                $"text protocol budget exceeded: {scalars} Unicode scalars, max {MaxUnicodeScalars}.");
        }

        int measured = MeasureWorstCaseInboundBytes(content, senderName);
        if (measured > maxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"text protocol budget exceeded: {measured} UTF-8 bytes, max {maxPayloadBytes}.");
        }
    }

    private static string ExtractPlainFallback(ServerChatContent content)
    {
        if (content.Segments == null || content.Segments.Count == 0)
        {
            return string.Empty;
        }

        ServerChatTextSegment first = content.Segments[0];
        return first?.Text ?? string.Empty;
    }

    private static int MeasureUtf8Bytes<T>(T value)
    {
        return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value, LanConnectJson.Options));
    }

    private static void AssertNoForbiddenControls(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            int code = rune.Value;
            if (code == '\n')
            {
                continue;
            }

            bool forbidden = code <= 0x1f || code == 0x7f ||
                             (code >= 0x80 && code <= 0x9f) ||
                             (code >= 0x202a && code <= 0x202e) ||
                             (code >= 0x2066 && code <= 0x206f) ||
                             (code >= 0xfe00 && code <= 0xfe0f) ||
                             (code >= 0xe0100 && code <= 0xe01ef) ||
                             (code >= 0xe0000 && code <= 0xe007f) ||
                             code is 0x00ad or 0x061c or 0x180e or 0x200b or 0x200c or 0x200d or
                                 0x200e or 0x200f or 0x2060 or 0x2061 or 0x2062 or 0x2063 or 0x2064 or
                                 0xfeff or 0xfff9 or 0xfffa or 0xfffb;
            if (forbidden)
            {
                throw new InvalidOperationException($"text contains forbidden control U+{code:X4}.");
            }
        }
    }
}
