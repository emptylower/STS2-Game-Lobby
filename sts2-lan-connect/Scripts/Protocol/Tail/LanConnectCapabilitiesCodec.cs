using System.Buffers.Binary;
using System.Text;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectCapabilitiesSelection(
    ushort SelectedLanProtocolVersion,
    LanConnectProtocolCarrier Carrier,
    bool RitsuLibPresent);

internal static class LanConnectCapabilitiesCodec
{
    private const byte PeerOfferKind = 1;
    private const byte SessionSelectionKind = 2;
    private const ushort RitsuLibPresentFlag = 1;
    private const ushort TypedSidecarAvailableFlag = 2;
    private const int MaxClientVersionBytes = 32;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    internal static byte[] EncodePeerOffer(LanConnectProtocolOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        offer.Validate();
        if (offer.LanProtocolMin > ushort.MaxValue || offer.LanProtocolMax > ushort.MaxValue)
        {
            throw Invalid("Peer-offer protocol range exceeds UInt16.");
        }

        byte[] versionBytes = EncodeClientVersion(offer.ClientVersion);
        byte[] payload = new byte[8 + versionBytes.Length];
        payload[0] = PeerOfferKind;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1, 2), checked((ushort)offer.LanProtocolMin));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(3, 2), checked((ushort)offer.LanProtocolMax));
        payload[5] = checked((byte)versionBytes.Length);
        versionBytes.CopyTo(payload, 6);
        ushort flags = 0;
        if (offer.RitsuLibPresent)
        {
            flags |= RitsuLibPresentFlag;
        }

        if (offer.RitsuLibSidecarAvailable)
        {
            flags |= TypedSidecarAvailableFlag;
        }

        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6 + versionBytes.Length, 2), flags);
        return payload;
    }

    internal static LanConnectProtocolOffer DecodePeerOffer(
        ReadOnlySpan<byte> payload,
        ushort sessionProtocolVersion)
    {
        if (sessionProtocolVersion != 0)
        {
            throw Invalid("Peer offers require sessionProtocolVersion 0.");
        }

        if (payload.Length < 8 || payload[0] != PeerOfferKind)
        {
            throw Invalid("Capabilities payload is not a complete peer offer.");
        }

        ushort minimum = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2));
        ushort maximum = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(3, 2));
        int versionLength = payload[5];
        int expectedLength = checked(8 + versionLength);
        if (versionLength is 0 or > MaxClientVersionBytes || payload.Length != expectedLength)
        {
            throw Invalid("Peer-offer client-version length is invalid or payload has trailing bytes.");
        }

        string clientVersion = DecodeUtf8(payload.Slice(6, versionLength), "peer-offer client version");
        LanConnectClientVersion parsed = LanConnectClientVersion.ParseSupported(clientVersion);
        if (!string.Equals(clientVersion, parsed.Canonical, StringComparison.Ordinal))
        {
            throw Invalid("Peer-offer client version is not canonical.");
        }

        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(6 + versionLength, 2));
        if ((flags & ~(RitsuLibPresentFlag | TypedSidecarAvailableFlag)) != 0)
        {
            throw Invalid($"Peer-offer flags contain unknown bits: 0x{flags:x4}.");
        }

        // native_bus_v1：sidecar 可用性只是诊断位，presence 与 availability 不再耦合
        // （0.5.18 事故状态 = present 但 sidecar 不可用，必须照常通过）。
        bool present = (flags & RitsuLibPresentFlag) != 0;
        bool sidecar = (flags & TypedSidecarAvailableFlag) != 0;

        return new LanConnectProtocolOffer(minimum, maximum, clientVersion, present, sidecar).Validate();
    }

    internal static byte[] EncodeSessionSelection(LanConnectProtocolSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Profile != LanConnectProtocolProfile.TailV1
            || selection.SelectedLanProtocolVersion is <= 0 or > ushort.MaxValue)
        {
            throw Invalid("Session selection must be a nonzero Tail v1 selection.");
        }

        ValidateCarrierPresence(selection.Carrier, selection.RitsuLibPresent);
        byte[] payload = new byte[6];
        payload[0] = SessionSelectionKind;
        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(1, 2),
            checked((ushort)selection.SelectedLanProtocolVersion));
        payload[3] = EncodeCarrier(selection.Carrier);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4, 2), selection.RitsuLibPresent ? (ushort)1 : (ushort)0);
        return payload;
    }

    internal static LanConnectCapabilitiesSelection DecodeSessionSelection(
        ReadOnlySpan<byte> payload,
        ushort sessionProtocolVersion)
    {
        if (sessionProtocolVersion == 0)
        {
            throw Invalid("Session selections require a nonzero sessionProtocolVersion.");
        }

        if (payload.Length != 6 || payload[0] != SessionSelectionKind)
        {
            throw Invalid("Capabilities payload is not an exact session selection.");
        }

        ushort selectedVersion = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2));
        if (selectedVersion != sessionProtocolVersion)
        {
            throw Invalid(
                $"Selection version {selectedVersion} differs from container version {sessionProtocolVersion}.");
        }

        LanConnectProtocolCarrier carrier = DecodeCarrier(payload[3]);
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(4, 2));
        if ((flags & ~RitsuLibPresentFlag) != 0)
        {
            throw Invalid($"Session-selection flags contain unknown bits: 0x{flags:x4}.");
        }

        bool present = (flags & RitsuLibPresentFlag) != 0;
        ValidateCarrierPresence(carrier, present);
        return new LanConnectCapabilitiesSelection(selectedVersion, carrier, present);
    }

    internal static void ValidateMatches(
        LanConnectCapabilitiesSelection containerSelection,
        LanConnectProtocolSelection frozenSelection)
    {
        ArgumentNullException.ThrowIfNull(containerSelection);
        ArgumentNullException.ThrowIfNull(frozenSelection);
        if (frozenSelection.Profile != LanConnectProtocolProfile.TailV1
            || containerSelection.SelectedLanProtocolVersion != frozenSelection.SelectedLanProtocolVersion
            || containerSelection.Carrier != frozenSelection.Carrier
            || containerSelection.RitsuLibPresent != frozenSelection.RitsuLibPresent)
        {
            throw Invalid("Container session selection differs from the frozen HTTP selection.");
        }
    }

    private static byte[] EncodeClientVersion(string clientVersion)
    {
        LanConnectClientVersion parsed = LanConnectClientVersion.ParseSupported(clientVersion);
        if (!string.Equals(clientVersion, parsed.Canonical, StringComparison.Ordinal))
        {
            throw Invalid("Client version is not canonical.");
        }

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(clientVersion);
        }
        catch (EncoderFallbackException exception)
        {
            throw Invalid("Client version is not valid UTF-8 text.", exception);
        }

        if (bytes.Length is 0 or > MaxClientVersionBytes)
        {
            throw Invalid($"Client version must occupy 1..{MaxClientVersionBytes} UTF-8 bytes.");
        }

        return bytes;
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> bytes, string field)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid($"{field} is not valid UTF-8.", exception);
        }
    }

    private static byte EncodeCarrier(LanConnectProtocolCarrier carrier) => carrier switch
    {
        LanConnectProtocolCarrier.LegacyTailV1 => 1,
        LanConnectProtocolCarrier.LegacySidecarV1 => 2,
        LanConnectProtocolCarrier.NativeBusV1 => 3,
        _ => throw Invalid($"Carrier {carrier} is invalid for Tail v1.")
    };

    private static LanConnectProtocolCarrier DecodeCarrier(byte value) => value switch
    {
        1 => LanConnectProtocolCarrier.LegacyTailV1,
        2 => LanConnectProtocolCarrier.LegacySidecarV1,
        3 => LanConnectProtocolCarrier.NativeBusV1,
        _ => throw Invalid($"Unknown Tail carrier value {value}.")
    };

    private static void ValidateCarrierPresence(LanConnectProtocolCarrier carrier, bool ritsuLibPresent)
    {
        // native_bus_v1 与 Ritsu presence 无耦合：有无 RitsuLib 的房间走同一条载体代码路径。
        // 旧载体值仅允许在旧容器解码路径出现（同版本对端不会构造它们）。
        if (carrier == LanConnectProtocolCarrier.NativeBusV1)
        {
            return;
        }

        LanConnectProtocolCarrier expected = ritsuLibPresent
            ? LanConnectProtocolCarrier.LegacySidecarV1
            : LanConnectProtocolCarrier.LegacyTailV1;
        if (carrier != expected)
        {
            throw Invalid($"Carrier {carrier} conflicts with frozen RitsuLib presence {ritsuLibPresent}.");
        }
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);
}
