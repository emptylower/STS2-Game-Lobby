using System.Buffers.Binary;
using System.Text;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectRejectionCodec
{
    private const byte SchemaVersion = 1;
    private const int MaxVersionBytes = 32;
    private const int MaxDetailBytes = 512;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    // 线上协议：reasonCode = index + 1，前 10 项顺序不得变动；11..17 为 0.6.1 新增码。
    // 与 LanConnectProtocolFailureMapper 共用此表；旧客户端收到 11..17 会解码为
    // unknown_tail_rejection_N 的通用协议失败，不会崩溃。
    internal static readonly string[] ReasonCodes =
    [
        "client_update_required",
        "protocol_profile_unsupported",
        "ritsulib_not_allowed_in_compat_mode",
        "ritsulib_presence_mismatch",
        "game_version_mismatch",
        "wire_cache_mismatch",
        "lan_tail_required",
        "lan_tail_malformed",
        "lan_protocol_version_mismatch",
        "ritsulib_sidecar_unavailable",
        "lan_legacy_carrier_unsupported",
        "lan_registry_fingerprint_required",
        "lan_registry_fingerprint_mismatch",
        "lan_native_frame_invalid",
        "lan_client_version_too_old",
        "lan_type_id_mismatch",
        "lan_extension_missing"
    ];

    internal static byte[] Encode(LanConnectProtocolFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        failure.Validate();
        int reasonIndex = Array.IndexOf(ReasonCodes, failure.Code);
        if (reasonIndex < 0)
        {
            throw Invalid($"Cannot encode unknown rejection code '{failure.Code}'.");
        }

        byte[] version = EncodeOptional(failure.RequiredClientVersion, MaxVersionBytes, "required client version");
        if (version.Length > 0)
        {
            LanConnectClientVersion parsed = LanConnectClientVersion.ParseSupported(failure.RequiredClientVersion);
            if (!string.Equals(parsed.Canonical, failure.RequiredClientVersion, StringComparison.Ordinal))
            {
                throw Invalid("Required client version is not canonical.");
            }
        }

        byte[] detail = EncodeOptional(failure.Detail, MaxDetailBytes, "rejection detail");
        byte[] payload = new byte[7 + version.Length + detail.Length];
        payload[0] = SchemaVersion;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1, 2), checked((ushort)(reasonIndex + 1)));
        payload[3] = checked((byte)version.Length);
        version.CopyTo(payload, 4);
        payload[4 + version.Length] = failure.RequiredRitsuLibPresent switch
        {
            null => 0,
            false => 1,
            true => 2
        };
        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(5 + version.Length, 2),
            checked((ushort)detail.Length));
        detail.CopyTo(payload, 7 + version.Length);
        return payload;
    }

    internal static LanConnectProtocolFailure Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 7 || payload[0] != SchemaVersion)
        {
            throw Invalid("Rejection payload is truncated or has an unsupported schema version.");
        }

        ushort reasonCode = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2));
        int versionLength = payload[3];
        if (versionLength > MaxVersionBytes || payload.Length < 7 + versionLength)
        {
            throw Invalid("Required-client-version length is invalid.");
        }

        string? version = versionLength == 0
            ? null
            : DecodeUtf8(payload.Slice(4, versionLength), "required client version");
        if (version is not null)
        {
            LanConnectClientVersion parsed = LanConnectClientVersion.ParseSupported(version);
            if (!string.Equals(parsed.Canonical, version, StringComparison.Ordinal))
            {
                throw Invalid("Required client version is not canonical.");
            }
        }

        byte presenceValue = payload[4 + versionLength];
        bool? presence = presenceValue switch
        {
            0 => null,
            1 => false,
            2 => true,
            _ => throw Invalid($"Unknown required RitsuLib presence value {presenceValue}.")
        };

        int detailLength = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(5 + versionLength, 2));
        int expectedLength = checked(7 + versionLength + detailLength);
        if (detailLength > MaxDetailBytes || payload.Length != expectedLength)
        {
            throw Invalid("Rejection detail length is invalid or payload has trailing bytes.");
        }

        string? detail = detailLength == 0
            ? null
            : DecodeUtf8(payload.Slice(7 + versionLength, detailLength), "rejection detail");
        return LanConnectProtocolFailureMapper.FromTail(reasonCode, version, presence, detail);
    }

    private static byte[] EncodeOptional(string? value, int maxBytes, string field)
    {
        if (value == null)
        {
            return [];
        }

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw Invalid($"{field} is not valid UTF-8 text.", exception);
        }

        if (bytes.Length > maxBytes)
        {
            throw Invalid($"{field} exceeds {maxBytes} UTF-8 bytes.");
        }

        return bytes;
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> value, string field)
    {
        try
        {
            return StrictUtf8.GetString(value);
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid($"{field} is not valid UTF-8.", exception);
        }
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);
}
