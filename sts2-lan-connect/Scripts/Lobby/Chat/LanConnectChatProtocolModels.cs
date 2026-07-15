using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2LanConnect.Scripts;

[JsonConverter(typeof(LanConnectChatChannelJsonConverter))]
internal enum LanConnectChatChannel
{
    Room,

    Server
}

internal sealed class LanConnectChatChannelJsonConverter : JsonConverter<LanConnectChatChannel>
{
    public override LanConnectChatChannel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Chat channel must be a string.");
        }

        return reader.GetString() switch
        {
            "room" => LanConnectChatChannel.Room,
            "server" => LanConnectChatChannel.Server,
            _ => throw new JsonException("Unsupported chat channel.")
        };
    }

    public override void Write(Utf8JsonWriter writer, LanConnectChatChannel value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            LanConnectChatChannel.Room => "room",
            LanConnectChatChannel.Server => "server",
            _ => throw new JsonException("Unsupported chat channel.")
        });
    }
}

internal static class LanConnectChatJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal sealed class ServerChatTicketRequest
{
    public int ProtocolVersion { get; set; } = 1;

    public string PlayerNetId { get; set; } = string.Empty;

    public string PlayerName { get; set; } = string.Empty;
}

internal sealed class ServerChatTicketResponse
{
    public string Ticket { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public string WebSocketUrl { get; set; } = string.Empty;

    public int ProtocolVersion { get; set; }
}

internal sealed class ServerChatContent
{
    public int FormatVersion { get; set; } = 1;

    public List<ServerChatTextSegment> Segments { get; set; } = new();
}

internal sealed class ServerChatTextSegment
{
    public string Kind { get; set; } = "text";

    public string Text { get; set; } = string.Empty;
}

internal sealed class ServerChatEnabledFeatures
{
    public int RichContentVersion { get; set; }

    public int EmojiSetVersion { get; set; }

    public int ItemRefVersion { get; set; }
}

internal sealed class ServerChatCanonicalMessage
{
    public string MessageId { get; set; } = string.Empty;

    public string SenderId { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public ServerChatContent Content { get; set; } = new();

    public string PlainTextFallback { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }
}

internal sealed class ServerChatSendEnvelope
{
    public string Type { get; set; } = "chat_send";

    public int ProtocolVersion { get; set; } = 1;

    public LanConnectChatChannel Channel { get; set; } = LanConnectChatChannel.Server;

    public string ClientMessageId { get; set; } = string.Empty;

    public ServerChatContent Content { get; set; } = new();
}

internal sealed class ServerChatReadyEnvelope
{
    public string Type { get; set; } = "chat_ready";

    public int ProtocolVersion { get; set; }

    [JsonRequired]
    public LanConnectChatChannel Channel { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public string SenderId { get; set; } = string.Empty;

    public string InstanceId { get; set; } = string.Empty;

    public int HistoryEpoch { get; set; }

    public bool ChatEnabled { get; set; }

    public int ServerChatVersion { get; set; }

    public ServerChatEnabledFeatures EnabledFeatures { get; set; } = new();
}

internal sealed class ServerChatSnapshotBeginEnvelope
{
    public string Type { get; set; } = "chat_snapshot_begin";

    public int ProtocolVersion { get; set; }

    public string SnapshotId { get; set; } = string.Empty;

    public string InstanceId { get; set; } = string.Empty;

    public int HistoryEpoch { get; set; }

    public int TotalMessages { get; set; }
}

internal sealed class ServerChatSnapshotChunkEnvelope
{
    public string Type { get; set; } = "chat_snapshot_chunk";

    public int ProtocolVersion { get; set; }

    public string SnapshotId { get; set; } = string.Empty;

    public int ChunkIndex { get; set; }

    public List<ServerChatCanonicalMessage> Messages { get; set; } = new();
}

internal sealed class ServerChatSnapshotEndEnvelope
{
    public string Type { get; set; } = "chat_snapshot_end";

    public int ProtocolVersion { get; set; }

    public string SnapshotId { get; set; } = string.Empty;

    public int HistoryEpoch { get; set; }
}

internal sealed class ServerChatAckEnvelope
{
    public string Type { get; set; } = "chat_ack";

    public int ProtocolVersion { get; set; }

    public string ClientMessageId { get; set; } = string.Empty;

    public ServerChatCanonicalMessage Message { get; set; } = new();
}

internal sealed class ServerChatMessageEnvelope
{
    public string Type { get; set; } = "chat_message";

    public int ProtocolVersion { get; set; }

    public ServerChatCanonicalMessage Message { get; set; } = new();
}

internal sealed class ServerChatErrorEnvelope
{
    public string Type { get; set; } = "chat_error";

    public int ProtocolVersion { get; set; }

    public string ClientMessageId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int? RetryAfterMs { get; set; }
}

internal sealed class ServerChatStateEnvelope
{
    public string Type { get; set; } = "chat_state";

    public int ProtocolVersion { get; set; }

    public bool ChatEnabled { get; set; }

    public ServerChatEnabledFeatures EnabledFeatures { get; set; } = new();

    public int HistoryEpoch { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal sealed class ServerChatHistoryClearedEnvelope
{
    public string Type { get; set; } = "chat_history_cleared";

    public int ProtocolVersion { get; set; }

    public int HistoryEpoch { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

// Flat projection used to inspect an inbound discriminator before mapping to a distinct envelope.
internal sealed class ServerChatInboundEnvelope
{
    public string Type { get; set; } = string.Empty;

    public int ProtocolVersion { get; set; }

    public LanConnectChatChannel? Channel { get; set; }

    public string? SessionId { get; set; }

    public string? SenderId { get; set; }

    public string? InstanceId { get; set; }

    public int? HistoryEpoch { get; set; }

    public bool? ChatEnabled { get; set; }

    public int? ServerChatVersion { get; set; }

    public ServerChatEnabledFeatures? EnabledFeatures { get; set; }

    public string? SnapshotId { get; set; }

    public int? TotalMessages { get; set; }

    public int? ChunkIndex { get; set; }

    public List<ServerChatCanonicalMessage>? Messages { get; set; }

    public string? ClientMessageId { get; set; }

    [JsonPropertyName("message")]
    public JsonElement MessagePayload { get; set; }

    [JsonIgnore]
    public ServerChatCanonicalMessage? CanonicalMessage => MessagePayload.ValueKind == JsonValueKind.Object
        ? MessagePayload.Deserialize<ServerChatCanonicalMessage>(LanConnectJson.Options)
        : null;

    public string? Code { get; set; }

    [JsonIgnore]
    public string? ErrorMessage => MessagePayload.ValueKind == JsonValueKind.String
        ? MessagePayload.GetString()
        : null;

    public int? RetryAfterMs { get; set; }

    public DateTimeOffset? ChangedAt { get; set; }
}

[JsonConverter(typeof(LanConnectChatSegmentJsonConverter))]
internal abstract record LanConnectChatSegment(string Kind);

internal sealed record LanConnectTextSegment(string Text) : LanConnectChatSegment("text");

internal sealed record LanConnectEmojiSegment(string EmojiId) : LanConnectChatSegment("emoji");

internal sealed record LanConnectItemRefSegment(
    string ItemType,
    string ModelId,
    int? UpgradeLevel = null) : LanConnectChatSegment("item_ref");

internal sealed record LanConnectPowerStateSegment(
    string ModelId,
    short Amount,
    string RoomSessionId,
    string? OwnerPlayerNetId = null,
    string? ApplierPlayerNetId = null) : LanConnectChatSegment("power_state");

internal sealed record LanConnectTargetRefSegment(
    string TargetKind,
    string TargetKey,
    string RoomSessionId) : LanConnectChatSegment("target_ref");

internal sealed class LanConnectChatSegmentJsonConverter : JsonConverter<LanConnectChatSegment>
{
    public override LanConnectChatSegment Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement element = document.RootElement;
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Chat segment must be an object.");
        }

        string kind = RequiredString(element, "kind");
        return kind switch
        {
            "text" => ReadText(element),
            "emoji" => ReadEmoji(element),
            "item_ref" => ReadItemRef(element),
            "power_state" => ReadPowerState(element),
            "target_ref" => ReadTargetRef(element),
            _ => throw new JsonException("Unsupported chat segment kind.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        LanConnectChatSegment value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);
        switch (value)
        {
            case LanConnectTextSegment text:
                writer.WriteString("text", text.Text);
                break;
            case LanConnectEmojiSegment emoji:
                writer.WriteString("emojiId", emoji.EmojiId);
                break;
            case LanConnectItemRefSegment item:
                writer.WriteString("itemType", item.ItemType);
                writer.WriteString("modelId", item.ModelId);
                if (item.UpgradeLevel.HasValue)
                {
                    writer.WriteNumber("upgradeLevel", item.UpgradeLevel.Value);
                }
                break;
            case LanConnectPowerStateSegment power:
                writer.WriteString("modelId", power.ModelId);
                writer.WriteNumber("amount", power.Amount);
                writer.WriteString("roomSessionId", power.RoomSessionId);
                if (power.OwnerPlayerNetId != null)
                {
                    writer.WriteString("ownerPlayerNetId", power.OwnerPlayerNetId);
                }
                if (power.ApplierPlayerNetId != null)
                {
                    writer.WriteString("applierPlayerNetId", power.ApplierPlayerNetId);
                }
                break;
            case LanConnectTargetRefSegment target:
                writer.WriteString("targetKind", target.TargetKind);
                writer.WriteString("targetKey", target.TargetKey);
                writer.WriteString("roomSessionId", target.RoomSessionId);
                break;
            default:
                throw new JsonException("Unsupported chat segment type.");
        }
        writer.WriteEndObject();
    }

    private static LanConnectTextSegment ReadText(JsonElement element)
    {
        AssertFields(element, ["kind", "text"], ["kind", "text"]);
        return new LanConnectTextSegment(RequiredString(element, "text"));
    }

    private static LanConnectEmojiSegment ReadEmoji(JsonElement element)
    {
        AssertFields(element, ["kind", "emojiId"], ["kind", "emojiId"]);
        return new LanConnectEmojiSegment(RequiredString(element, "emojiId"));
    }

    private static LanConnectItemRefSegment ReadItemRef(JsonElement element)
    {
        string itemType = RequiredString(element, "itemType");
        string modelId = RequiredString(element, "modelId");
        if (itemType == "card")
        {
            AssertFields(
                element,
                ["kind", "itemType", "modelId", "upgradeLevel"],
                ["kind", "itemType", "modelId"]);
            int? upgradeLevel = element.TryGetProperty("upgradeLevel", out JsonElement upgrade)
                ? RequiredInt32(upgrade, "upgradeLevel")
                : null;
            return new LanConnectItemRefSegment(itemType, modelId, upgradeLevel);
        }

        AssertFields(
            element,
            ["kind", "itemType", "modelId"],
            ["kind", "itemType", "modelId"]);
        if (itemType is not ("relic" or "potion"))
        {
            throw new JsonException("itemType must be card, relic, or potion.");
        }
        return new LanConnectItemRefSegment(itemType, modelId);
    }

    private static LanConnectPowerStateSegment ReadPowerState(JsonElement element)
    {
        AssertFields(
            element,
            ["kind", "modelId", "amount", "roomSessionId", "ownerPlayerNetId", "applierPlayerNetId"],
            ["kind", "modelId", "amount", "roomSessionId"]);
        int amount = RequiredInt32(element.GetProperty("amount"), "amount");
        if (amount < short.MinValue || amount > short.MaxValue)
        {
            throw new JsonException("power_state amount must fit Int16.");
        }
        return new LanConnectPowerStateSegment(
            RequiredString(element, "modelId"),
            (short)amount,
            RequiredString(element, "roomSessionId"),
            OptionalString(element, "ownerPlayerNetId"),
            OptionalString(element, "applierPlayerNetId"));
    }

    private static LanConnectTargetRefSegment ReadTargetRef(JsonElement element)
    {
        AssertFields(
            element,
            ["kind", "targetKind", "targetKey", "roomSessionId"],
            ["kind", "targetKind", "targetKey", "roomSessionId"]);
        string targetKind = RequiredString(element, "targetKind");
        if (targetKind is not ("player" or "monster"))
        {
            throw new JsonException("targetKind must be player or monster.");
        }
        return new LanConnectTargetRefSegment(
            targetKind,
            RequiredString(element, "targetKey"),
            RequiredString(element, "roomSessionId"));
    }

    private static void AssertFields(
        JsonElement element,
        string[] allowed,
        string[] required)
    {
        HashSet<string> allowedSet = new(allowed, StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !allowedSet.Contains(property.Name))
            {
                throw new JsonException("Chat segment has duplicate, reserved, or unknown fields.");
            }
        }
        foreach (string name in required)
        {
            if (!seen.Contains(name))
            {
                throw new JsonException($"Chat segment is missing {name}.");
            }
        }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{name} must be a string.");
        }
        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{name} must be a string.");
        }
        return value.GetString();
    }

    private static int RequiredInt32(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int value))
        {
            throw new JsonException($"{name} must be an Int32.");
        }
        return value;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectChatFeatureVersions(
    int RichContentVersion = 0,
    int EmojiSetVersion = 0,
    int ItemRefVersion = 0,
    int CombatRefVersion = 0);

internal sealed record LanConnectChatFeatureOverrides(
    int? RichContentVersion = null,
    int? EmojiSetVersion = null,
    int? ItemRefVersion = null,
    int? CombatRefVersion = null);

internal sealed record LanConnectChatFeatureInput
{
    internal LanConnectChatChannel Channel { get; init; } = LanConnectChatChannel.Server;

    internal LanConnectChatFeatureVersions Compiled { get; init; } = new();

    internal LanConnectChatFeatureVersions Configured { get; init; } = new();

    internal LanConnectChatFeatureOverrides? Admin { get; init; }

    internal bool ChannelEnabled { get; init; }

    internal bool RoomV2Enabled { get; init; } = true;

    internal LanConnectChatFeatureVersions? Sender { get; init; }

    internal LanConnectChatFeatureVersions? Receiver { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectChatContent
{
    [JsonConstructor]
    public LanConnectChatContent()
    {
    }

    internal LanConnectChatContent(int formatVersion, IReadOnlyList<LanConnectChatSegment> segments)
    {
        FormatVersion = formatVersion;
        Segments = segments;
    }

    [JsonRequired]
    public int FormatVersion { get; init; } = 1;

    [JsonRequired]
    public IReadOnlyList<LanConnectChatSegment> Segments { get; init; } = Array.Empty<LanConnectChatSegment>();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatMessagePayload
{
    public string MessageId { get; init; } = string.Empty;
    public string SenderId { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;
    public LanConnectChatContent Content { get; init; } = new();
    public string PlainTextFallback { get; init; } = string.Empty;
    public string SentAt { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatReadyEnvelope
{
    public string Type { get; init; } = "chat_ready";
    public int ProtocolVersion { get; init; } = 1;
    public LanConnectChatChannel Channel { get; init; } = LanConnectChatChannel.Server;
    public string SessionId { get; init; } = string.Empty;
    public string SenderId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public int HistoryEpoch { get; init; }
    public bool ChatEnabled { get; init; }
    public int ServerChatVersion { get; init; }
    public LanConnectChatFeatureVersions EnabledFeatures { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatStateEnvelope
{
    public string Type { get; init; } = "chat_state";
    public int ProtocolVersion { get; init; } = 1;
    public bool ChatEnabled { get; init; }
    public LanConnectChatFeatureVersions EnabledFeatures { get; init; } = new();
    public int HistoryEpoch { get; init; }
    public string ChangedAt { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatSnapshotBeginEnvelope
{
    [JsonRequired]
    public string Type { get; init; } = "chat_snapshot_begin";
    [JsonRequired]
    public int ProtocolVersion { get; init; } = 1;
    [JsonRequired]
    public string SnapshotId { get; init; } = string.Empty;
    [JsonRequired]
    public string InstanceId { get; init; } = string.Empty;
    [JsonRequired]
    public int HistoryEpoch { get; init; }
    [JsonRequired]
    public int TotalMessages { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatSnapshotChunkEnvelope
{
    [JsonRequired]
    public string Type { get; init; } = "chat_snapshot_chunk";
    [JsonRequired]
    public int ProtocolVersion { get; init; } = 1;
    [JsonRequired]
    public string SnapshotId { get; init; } = string.Empty;
    [JsonRequired]
    public int ChunkIndex { get; init; }
    [JsonRequired]
    public IReadOnlyList<LanConnectServerChatMessagePayload> Messages { get; init; } =
        Array.Empty<LanConnectServerChatMessagePayload>();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatSnapshotEndEnvelope
{
    [JsonRequired]
    public string Type { get; init; } = "chat_snapshot_end";
    [JsonRequired]
    public int ProtocolVersion { get; init; } = 1;
    [JsonRequired]
    public string SnapshotId { get; init; } = string.Empty;
    [JsonRequired]
    public int HistoryEpoch { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatSendEnvelope
{
    public string Type { get; init; } = "chat_send";
    public int ProtocolVersion { get; init; } = 1;
    public LanConnectChatChannel Channel { get; init; } = LanConnectChatChannel.Server;
    public string ClientMessageId { get; init; } = string.Empty;
    public LanConnectChatContent Content { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatAckEnvelope
{
    public string Type { get; init; } = "chat_ack";
    public int ProtocolVersion { get; init; } = 1;
    public string ClientMessageId { get; init; } = string.Empty;
    public LanConnectServerChatMessagePayload Message { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatMessageEnvelope
{
    public string Type { get; init; } = "chat_message";
    public int ProtocolVersion { get; init; } = 1;
    public LanConnectServerChatMessagePayload Message { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectServerChatErrorEnvelope
{
    public string Type { get; init; } = "chat_error";
    public int ProtocolVersion { get; init; } = 1;
    public string ClientMessageId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int? RetryAfterMs { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectRoomChatMessagePayload
{
    public string RoomId { get; init; } = string.Empty;
    public string RoomSessionId { get; init; } = string.Empty;
    public string MessageId { get; init; } = string.Empty;
    public string SenderId { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;
    public LanConnectChatContent Content { get; init; } = new();
    public string PlainTextFallback { get; init; } = string.Empty;
    public string SentAt { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectRoomChatReadyEnvelope
{
    public string Type { get; init; } = "room_chat_ready";
    public int ProtocolVersion { get; init; } = 1;
    public string RoomId { get; init; } = string.Empty;
    public string RoomSessionId { get; init; } = string.Empty;
    public LanConnectChatFeatureVersions EnabledFeatures { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectRoomChatV2Envelope
{
    public string Type { get; init; } = "room_chat_v2";
    public int ProtocolVersion { get; init; } = 1;
    public string ClientMessageId { get; init; } = string.Empty;
    public string RoomId { get; init; } = string.Empty;
    public string RoomSessionId { get; init; } = string.Empty;
    public LanConnectChatContent Content { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectRoomChatAckEnvelope
{
    public string Type { get; init; } = "room_chat_ack";
    public int ProtocolVersion { get; init; } = 1;
    public string ClientMessageId { get; init; } = string.Empty;
    public LanConnectRoomChatMessagePayload Message { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectRoomChatMessageEnvelope
{
    public string Type { get; init; } = "room_chat_message";
    public int ProtocolVersion { get; init; } = 1;
    public LanConnectRoomChatMessagePayload Message { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LanConnectRoomChatErrorEnvelope
{
    public string Type { get; init; } = "room_chat_error";
    public int ProtocolVersion { get; init; } = 1;
    public string ClientMessageId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int? RetryAfterMs { get; init; }
}
