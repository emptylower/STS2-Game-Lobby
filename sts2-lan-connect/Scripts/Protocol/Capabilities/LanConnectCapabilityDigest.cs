using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectCapabilityDigest
{
    private static readonly byte[] Magic = "LANSEL01"u8.ToArray();

    public static string Compute(LanConnectProtocolSelection selection) =>
        Convert.ToHexString(SHA256.HashData(EncodeCanonical(selection))).ToLowerInvariant();

    public static byte[] EncodeCanonical(LanConnectProtocolSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        using MemoryStream stream = new();
        stream.Write(Magic);
        stream.WriteByte(1);
        stream.WriteByte(selection.Profile switch
        {
            LanConnectProtocolProfile.Compat4x5V1 => 1,
            LanConnectProtocolProfile.TailV1 => 2,
            _ => throw LanConnectProtocolFailureMapper.FromLocalException("protocol_profile_unsupported")
        });
        Span<byte> protocolVersion = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(protocolVersion, checked((ushort)selection.SelectedLanProtocolVersion));
        stream.Write(protocolVersion);
        stream.WriteByte(selection.Carrier switch
        {
            LanConnectProtocolCarrier.None => 0,
            LanConnectProtocolCarrier.LegacyTailV1 => 1,
            LanConnectProtocolCarrier.LegacySidecarV1 => 2,
            LanConnectProtocolCarrier.NativeBusV1 => 3,
            _ => throw LanConnectProtocolFailureMapper.FromLocalException("protocol_profile_unsupported")
        });
        stream.WriteByte(checked((byte)selection.MaxPlayers));
        WriteLengthPrefixedUtf8(stream, selection.MinimumClientVersion, 32, "minimumClientVersion", required: true);
        WriteLengthPrefixedUtf8(stream, selection.GameVersion, 32, "gameVersion", required: true);
        WriteLengthPrefixedAscii(stream, selection.WireCacheSignature, 64, "wireCacheSignature");
        stream.WriteByte(selection.RitsuLibPresent ? (byte)1 : (byte)0);
        return stream.ToArray();
    }

    private static void WriteLengthPrefixedUtf8(
        Stream stream,
        string? value,
        int maxBytes,
        string name,
        bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_protocol_version_mismatch",
                $"{name} is required.");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length > maxBytes)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_protocol_version_mismatch",
                $"{name} exceeds {maxBytes} bytes.");
        }

        stream.WriteByte(checked((byte)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteLengthPrefixedAscii(
        Stream stream,
        string? value,
        int maxBytes,
        string name)
    {
        if (value?.Any(static character => !char.IsAscii(character)) == true)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_protocol_version_mismatch",
                $"{name} must contain ASCII only.");
        }

        WriteLengthPrefixedUtf8(stream, value, maxBytes, name, required: false);
    }
}
