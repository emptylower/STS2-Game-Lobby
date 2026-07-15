using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectServerChatProtocol
{
    internal const int MaxSegments = 32;
    internal const int MaxEntities = 12;
    internal const int MaxTextScalars = 300;
    internal const int MaxPayloadBytes = 8192;
    internal const string WorstCaseMessageId = "00000000-0000-0000-0000-000000000000";
    internal const string WorstCaseSenderId = "ABCDEFGHIJKLMNOPQRSTUV";
    internal const string WorstCaseSentAt = "2026-07-12T12:00:00.123Z";

    private static readonly string[] EmojiIds =
        LanConnectChatEmojiSet.Version1.Select(emoji => emoji.Id).ToArray();
    private static readonly HashSet<string> EmojiIdSet = new(EmojiIds, StringComparer.Ordinal);
    private static readonly Regex ModelIdPattern = new(
        "^[A-Za-z0-9._-]{1,160}\\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly JsonSerializerOptions WireJson = new(LanConnectChatJson.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static IReadOnlyList<string> EmojiSet1 { get; } = Array.AsReadOnly(EmojiIds);

    internal static bool IsValidModelId(string? modelId) =>
        modelId != null && ModelIdPattern.IsMatch(modelId);

    internal static LanConnectChatContent Canonicalize(
        LanConnectChatContent content,
        LanConnectChatFeatureVersions enabled)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(enabled);
        if (content.FormatVersion != 1)
        {
            throw Invalid("formatVersion must be 1.");
        }
        if (content.Segments == null)
        {
            throw Invalid("segments must be present.");
        }
        if (content.Segments.Count > MaxSegments)
        {
            throw Invalid($"segments must be at most {MaxSegments}.");
        }

        List<LanConnectChatSegment> canonical = [];
        int entities = 0;
        bool requiresEmoji = false;
        bool requiresItem = false;
        foreach (LanConnectChatSegment? segment in content.Segments)
        {
            switch (segment)
            {
                case LanConnectTextSegment text:
                    string normalized = NormalizeText(text.Text);
                    AssertNoForbiddenControls(normalized);
                    if (normalized.Length == 0)
                    {
                        break;
                    }
                    if (canonical.Count > 0 && canonical[^1] is LanConnectTextSegment previous)
                    {
                        canonical[^1] = new LanConnectTextSegment(
                            (previous.Text + normalized).Normalize(NormalizationForm.FormC));
                    }
                    else
                    {
                        canonical.Add(new LanConnectTextSegment(normalized));
                    }
                    break;
                case LanConnectEmojiSegment emoji:
                    if (emoji.EmojiId == null || !EmojiIdSet.Contains(emoji.EmojiId))
                    {
                        throw Invalid("emojiId must be from Emoji Set 1.");
                    }
                    entities++;
                    requiresEmoji = true;
                    canonical.Add(new LanConnectEmojiSegment(emoji.EmojiId));
                    break;
                case LanConnectItemRefSegment item:
                    ValidateItem(item);
                    entities++;
                    requiresItem = true;
                    canonical.Add(new LanConnectItemRefSegment(
                        item.ItemType,
                        item.ModelId,
                        item.UpgradeLevel));
                    break;
                case LanConnectPowerStateSegment:
                case LanConnectTargetRefSegment:
                    throw Invalid("Combat reference segments are reserved for a later protocol version.");
                case null:
                    throw Invalid("segment must not be null.");
                default:
                    throw Invalid("Unsupported chat segment type.");
            }
        }

        if (entities > MaxEntities)
        {
            throw Invalid($"content must contain at most {MaxEntities} entities.");
        }

        if (canonical.Count > 0 && canonical[0] is LanConnectTextSegment first)
        {
            string trimmed = first.Text.TrimStart();
            if (trimmed.Length == 0)
            {
                canonical.RemoveAt(0);
            }
            else
            {
                canonical[0] = new LanConnectTextSegment(trimmed);
            }
        }
        if (canonical.Count > 0 && canonical[^1] is LanConnectTextSegment last)
        {
            string trimmed = last.Text.TrimEnd();
            if (trimmed.Length == 0)
            {
                canonical.RemoveAt(canonical.Count - 1);
            }
            else
            {
                canonical[^1] = new LanConnectTextSegment(trimmed);
            }
        }
        if (canonical.Count == 0)
        {
            throw Invalid("content must not be blank-only.");
        }

        int textScalars = 0;
        foreach (LanConnectChatSegment segment in canonical)
        {
            if (segment is LanConnectTextSegment text)
            {
                textScalars += CountUnicodeScalars(text.Text);
            }
        }
        if (textScalars > MaxTextScalars)
        {
            throw Invalid($"text must be at most {MaxTextScalars} Unicode scalars.");
        }

        // Schema and all structural limits are authoritative even when a feature is off.
        if (requiresEmoji &&
            (enabled.RichContentVersion != 1 || enabled.EmojiSetVersion != 1))
        {
            throw Invalid("Emoji segments are not enabled.");
        }
        if (requiresItem &&
            (enabled.RichContentVersion != 1 || enabled.ItemRefVersion != 1))
        {
            throw Invalid("Item reference segments are not enabled.");
        }

        return new LanConnectChatContent(1, canonical);
    }

    internal static string RenderGenericFallback(LanConnectChatContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        StringBuilder builder = new();
        foreach (LanConnectChatSegment segment in content.Segments)
        {
            switch (segment)
            {
                case LanConnectTextSegment text:
                    builder.Append(text.Text);
                    break;
                case LanConnectEmojiSegment:
                    builder.Append("[Emoji]");
                    break;
                case LanConnectItemRefSegment { ItemType: "card" }:
                    builder.Append("[Card]");
                    break;
                case LanConnectItemRefSegment { ItemType: "relic" }:
                    builder.Append("[Relic]");
                    break;
                case LanConnectItemRefSegment { ItemType: "potion" }:
                    builder.Append("[Potion]");
                    break;
                default:
                    throw Invalid("Content contains an unsupported fallback segment.");
            }
        }
        return builder.ToString();
    }

    internal static string DeterministicContentJson(LanConnectChatContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return JsonSerializer.Serialize(content, WireJson);
    }

    internal static int MeasureWorstCaseInboundBytes(
        LanConnectChatContent content,
        string senderName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(senderName);
        string normalizedSender = NormalizeText(senderName).Trim();
        AssertNoForbiddenControls(normalizedSender);
        LanConnectServerChatMessagePayload payload = new()
        {
            MessageId = WorstCaseMessageId,
            SenderId = WorstCaseSenderId,
            SenderName = normalizedSender,
            Content = content,
            PlainTextFallback = RenderGenericFallback(content),
            SentAt = WorstCaseSentAt
        };
        LanConnectServerChatAckEnvelope ack = new()
        {
            ClientMessageId = WorstCaseMessageId,
            Message = payload
        };
        LanConnectServerChatMessageEnvelope message = new() { Message = payload };
        var snapshot = new
        {
            type = "chat_snapshot_chunk",
            protocolVersion = 1,
            snapshotId = WorstCaseMessageId,
            chunkIndex = 999,
            messages = new[] { payload }
        };
        return Math.Max(
            MeasureUtf8(ack),
            Math.Max(MeasureUtf8(message), MeasureUtf8(snapshot)));
    }

    internal static void AssertInboundBudget(
        LanConnectChatContent content,
        string senderName,
        int maxPayloadBytes = MaxPayloadBytes)
    {
        int measured = MeasureWorstCaseInboundBytes(content, senderName);
        if (measured > maxPayloadBytes)
        {
            throw Invalid($"wire envelope exceeds budget: {measured} > {maxPayloadBytes}.");
        }
    }

    internal static int CountUnicodeScalars(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        AssertWellFormedUnicode(text);
        int count = 0;
        foreach (Rune _ in text.EnumerateRunes())
        {
            count++;
        }
        return count;
    }

    private static void ValidateItem(LanConnectItemRefSegment item)
    {
        if (item.ItemType is not ("card" or "relic" or "potion"))
        {
            throw Invalid("itemType must be card, relic, or potion.");
        }
        if (!IsValidModelId(item.ModelId))
        {
            throw Invalid("modelId must be 1 to 160 allowed ASCII characters.");
        }
        if (item.ItemType == "card")
        {
            if (item.UpgradeLevel is < 0 or > 9)
            {
                throw Invalid("card upgradeLevel must be from 0 to 9.");
            }
        }
        else if (item.UpgradeLevel.HasValue)
        {
            throw Invalid("Only cards may include upgradeLevel.");
        }
    }

    private static string NormalizeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        AssertWellFormedUnicode(text);
        return text.Normalize(NormalizationForm.FormC)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static void AssertWellFormedUnicode(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                {
                    throw Invalid("text contains an unpaired surrogate.");
                }
                index++;
            }
            else if (char.IsLowSurrogate(current))
            {
                throw Invalid("text contains an unpaired surrogate.");
            }
        }
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
                throw Invalid($"text contains forbidden control U+{code:X4}.");
            }
        }
    }

    private static int MeasureUtf8<T>(T value) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value, WireJson));

    private static InvalidOperationException Invalid(string message) => new(message);
}
