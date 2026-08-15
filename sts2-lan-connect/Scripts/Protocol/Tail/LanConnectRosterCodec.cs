using System.Buffers.Binary;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectRosterPlayerCarrier
{
    private readonly byte[] _vanillaPlayerBytes;

    internal LanConnectRosterPlayerCarrier(
        ulong playerId,
        byte realSlotId,
        uint vanillaPlayerBitLength,
        ReadOnlySpan<byte> vanillaPlayerBytes)
    {
        PlayerId = playerId;
        RealSlotId = realSlotId;
        VanillaPlayerBitLength = vanillaPlayerBitLength;
        _vanillaPlayerBytes = vanillaPlayerBytes.ToArray();
    }

    internal ulong PlayerId { get; }
    internal byte RealSlotId { get; }
    internal uint VanillaPlayerBitLength { get; }
    internal ReadOnlyMemory<byte> VanillaPlayerBytes => _vanillaPlayerBytes;
}

internal sealed record LanConnectRosterSnapshot(
    ulong AuthorityPeerId,
    uint RosterRevision,
    IReadOnlyList<LanConnectRosterPlayerCarrier> Players);

internal static class LanConnectRosterCodec
{
    internal const int MaxVanillaPlayerBytes = 16 * 1024;
    private const byte SchemaVersion = 1;
    private const byte FullSnapshotKind = 1;
    private const int FixedHeaderBytes = 15;
    private const int PlayerFixedBytes = 13;

    internal static byte[] Encode(LanConnectRosterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IReadOnlyList<LanConnectRosterPlayerCarrier> players = CanonicalizeAndValidate(snapshot.Players);
        if (snapshot.RosterRevision == 0)
        {
            throw Invalid("Roster revision must be greater than zero.");
        }

        int totalLength = FixedHeaderBytes;
        foreach (LanConnectRosterPlayerCarrier player in players)
        {
            totalLength = checked(totalLength + PlayerFixedBytes + player.VanillaPlayerBytes.Length);
        }

        if (totalLength > LanConnectTailCodec.MaxEntryPayloadBytes)
        {
            throw Invalid("Roster payload exceeds the Tail entry payload limit.");
        }

        byte[] payload = new byte[totalLength];
        payload[0] = SchemaVersion;
        payload[1] = FullSnapshotKind;
        BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(2, 8), snapshot.AuthorityPeerId);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(10, 4), snapshot.RosterRevision);
        payload[14] = checked((byte)players.Count);
        int offset = FixedHeaderBytes;
        foreach (LanConnectRosterPlayerCarrier player in players)
        {
            BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(offset, 8), player.PlayerId);
            offset += 8;
            payload[offset++] = player.RealSlotId;
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(offset, 4), player.VanillaPlayerBitLength);
            offset += 4;
            player.VanillaPlayerBytes.Span.CopyTo(payload.AsSpan(offset));
            offset += player.VanillaPlayerBytes.Length;
        }

        return payload;
    }

    internal static LanConnectRosterSnapshot Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < FixedHeaderBytes
            || payload[0] != SchemaVersion
            || payload[1] != FullSnapshotKind)
        {
            throw Invalid("Roster header is truncated or has an unsupported schema/snapshot kind.");
        }

        ulong authority = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(2, 8));
        uint revision = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(10, 4));
        if (revision == 0)
        {
            throw Invalid("Roster revision must be greater than zero.");
        }

        int playerCount = payload[14];
        if (playerCount is < LanConnectConstants.ProtocolMinPlayers or > LanConnectConstants.ProtocolMaxPlayers)
        {
            throw Invalid("Roster player count must be 2..8.");
        }

        int offset = FixedHeaderBytes;
        List<LanConnectRosterPlayerCarrier> players = new(playerCount);
        for (int index = 0; index < playerCount; index++)
        {
            Require(payload, offset, PlayerFixedBytes, $"player {index} header");
            ulong playerId = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(offset, 8));
            offset += 8;
            byte realSlot = payload[offset++];
            uint bitLength = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, 4));
            offset += 4;
            int byteLength = checked((int)((bitLength + 7u) / 8u));
            Require(payload, offset, byteLength, $"player {index} carrier");
            players.Add(new LanConnectRosterPlayerCarrier(
                playerId,
                realSlot,
                bitLength,
                payload.Slice(offset, byteLength)));
            offset += byteLength;
        }

        if (offset != payload.Length)
        {
            throw Invalid("Roster payload has undeclared trailing bytes.");
        }

        IReadOnlyList<LanConnectRosterPlayerCarrier> canonical = CanonicalizeAndValidate(players);
        for (int index = 0; index < players.Count; index++)
        {
            if (!ReferenceEquals(players[index], canonical[index]))
            {
                throw Invalid("Roster players are not in canonical slot/ID order.");
            }
        }

        return new LanConnectRosterSnapshot(authority, revision, players.AsReadOnly());
    }

    internal static void ValidateAuthority(
        LanConnectRosterSnapshot snapshot,
        ulong transportSenderPeerId,
        ulong currentHostPeerId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.AuthorityPeerId != transportSenderPeerId || snapshot.AuthorityPeerId != currentHostPeerId)
        {
            throw Invalid("Roster authority, transport sender, and current host must be identical.");
        }
    }

    private static IReadOnlyList<LanConnectRosterPlayerCarrier> CanonicalizeAndValidate(
        IReadOnlyList<LanConnectRosterPlayerCarrier> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count is < LanConnectConstants.ProtocolMinPlayers or > LanConnectConstants.ProtocolMaxPlayers)
        {
            throw Invalid("Roster player count must be 2..8.");
        }

        HashSet<ulong> playerIds = [];
        HashSet<byte> slots = [];
        foreach (LanConnectRosterPlayerCarrier? player in source)
        {
            if (player == null)
            {
                throw Invalid("Roster players cannot contain null.");
            }

            if (player.RealSlotId >= LanConnectConstants.ProtocolMaxPlayers
                || !playerIds.Add(player.PlayerId)
                || !slots.Add(player.RealSlotId))
            {
                throw Invalid("Roster player IDs and real slots must be unique; slots must be 0..7.");
            }

            ValidateCarrier(player);
        }

        return source
            .OrderBy(static player => player.RealSlotId)
            .ThenBy(static player => player.PlayerId)
            .ToArray();
    }

    private static void ValidateCarrier(LanConnectRosterPlayerCarrier player)
    {
        int byteLength = player.VanillaPlayerBytes.Length;
        if (player.VanillaPlayerBitLength == 0
            || player.VanillaPlayerBitLength > checked((uint)byteLength * 8u)
            || byteLength != checked((int)((player.VanillaPlayerBitLength + 7u) / 8u))
            || byteLength > MaxVanillaPlayerBytes)
        {
            throw Invalid("Vanilla player carrier bit/byte length is invalid.");
        }

        int usedBits = checked((int)(player.VanillaPlayerBitLength % 8u));
        if (usedBits != 0)
        {
            byte unusedMask = unchecked((byte)(0xff << usedBits));
            if ((player.VanillaPlayerBytes.Span[^1] & unusedMask) != 0)
            {
                throw Invalid("Vanilla player carrier has nonzero unused high bits.");
            }
        }
    }

    private static void Require(ReadOnlySpan<byte> payload, int offset, int length, string field)
    {
        int end;
        try
        {
            end = checked(offset + length);
        }
        catch (OverflowException exception)
        {
            throw Invalid($"Roster length overflow at {field}.", exception);
        }

        if (length < 0 || end > payload.Length)
        {
            throw Invalid($"Roster payload is truncated at {field}.");
        }
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);
}
