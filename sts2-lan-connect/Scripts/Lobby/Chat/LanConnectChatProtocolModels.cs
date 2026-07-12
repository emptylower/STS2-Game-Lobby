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
