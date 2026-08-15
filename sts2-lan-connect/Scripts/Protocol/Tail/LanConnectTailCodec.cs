using System.Buffers.Binary;
using System.Text;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectTailCodec
{
    internal const int MaxContainerBytes = 256 * 1024;
    internal const int MaxEntries = 32;
    internal const int MaxEntryIdBytes = 64;
    internal const int MaxEntryPayloadBytes = 64 * 1024;

    private const byte ContainerVersion = 1;
    private const byte CriticalEntryFlag = 1;
    private const int MagicLength = 8;
    private const int FixedHeaderLength = 18;
    private const int EntryFixedLength = 8;
    private static readonly byte[] Magic = "STSLAN01"u8.ToArray();
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    internal static byte[] Encode(
        ushort sessionProtocolVersion,
        IReadOnlyList<LanConnectTailEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > MaxEntries)
        {
            throw Invalid($"Entry count {entries.Count} exceeds the {MaxEntries}-entry limit.");
        }

        List<EncodedEntry> encodedEntries = new(entries.Count);
        HashSet<string> seenIds = new(StringComparer.Ordinal);
        int totalLength = FixedHeaderLength;

        foreach (LanConnectTailEntry? entry in entries)
        {
            if (entry == null)
            {
                throw Invalid("Entries cannot contain null.");
            }

            byte[] idBytes = EncodeId(entry.Id);
            ValidateEntryContract(entry.Id, entry.Version, entry.IsCritical);
            if (!seenIds.Add(entry.Id))
            {
                throw Invalid($"Duplicate entry ID '{entry.Id}'.");
            }

            int payloadLength = entry.Payload.Length;
            if (payloadLength > MaxEntryPayloadBytes)
            {
                throw Invalid(
                    $"Entry '{entry.Id}' payload is {payloadLength} bytes; maximum is {MaxEntryPayloadBytes}.");
            }

            try
            {
                totalLength = checked(totalLength + EntryFixedLength + idBytes.Length + payloadLength);
            }
            catch (OverflowException exception)
            {
                throw Invalid("Container length overflowed Int32.", exception);
            }

            if (totalLength > MaxContainerBytes)
            {
                throw Invalid($"Container exceeds the {MaxContainerBytes}-byte limit.");
            }

            encodedEntries.Add(new EncodedEntry(entry, idBytes));
        }

        encodedEntries.Sort(static (left, right) => CompareBytes(left.IdBytes, right.IdBytes));

        byte[] destination = new byte[totalLength];
        Magic.CopyTo(destination, 0);
        destination[8] = ContainerVersion;
        destination[9] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(
            destination.AsSpan(10, sizeof(uint)),
            checked((uint)(totalLength - MagicLength)));
        BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(14, sizeof(ushort)), sessionProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(
            destination.AsSpan(16, sizeof(ushort)),
            checked((ushort)encodedEntries.Count));

        int offset = FixedHeaderLength;
        foreach (EncodedEntry encoded in encodedEntries)
        {
            LanConnectTailEntry entry = encoded.Entry;
            destination[offset++] = checked((byte)encoded.IdBytes.Length);
            encoded.IdBytes.CopyTo(destination, offset);
            offset += encoded.IdBytes.Length;
            BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(offset, sizeof(ushort)), entry.Version);
            offset += sizeof(ushort);
            destination[offset++] = entry.IsCritical ? CriticalEntryFlag : (byte)0;
            BinaryPrimitives.WriteUInt32BigEndian(
                destination.AsSpan(offset, sizeof(uint)),
                checked((uint)entry.Payload.Length));
            offset += sizeof(uint);
            entry.Payload.Span.CopyTo(destination.AsSpan(offset));
            offset += entry.Payload.Length;
        }

        if (offset != destination.Length)
        {
            throw Invalid("Internal container length mismatch.");
        }

        return destination;
    }

    internal static LanConnectTailEnvelope Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length > MaxContainerBytes)
        {
            throw Invalid($"Container is {source.Length} bytes; maximum is {MaxContainerBytes}.");
        }

        if (source.Length < FixedHeaderLength)
        {
            throw Invalid("Container header is truncated.");
        }

        if (!source[..MagicLength].SequenceEqual(Magic))
        {
            throw Invalid("Container magic is invalid.");
        }

        if (source[8] != ContainerVersion)
        {
            throw Invalid($"Unsupported container version {source[8]}.");
        }

        if (source[9] != 0)
        {
            throw Invalid($"Container flags contain unknown bits: 0x{source[9]:x2}.");
        }

        uint declaredBodyLength = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(10, sizeof(uint)));
        int declaredTotalLength;
        try
        {
            declaredTotalLength = checked((int)declaredBodyLength + MagicLength);
        }
        catch (OverflowException exception)
        {
            throw Invalid("Declared container length overflows Int32.", exception);
        }

        if (declaredTotalLength > MaxContainerBytes)
        {
            throw Invalid($"Declared container length exceeds the {MaxContainerBytes}-byte limit.");
        }

        if (declaredTotalLength != source.Length)
        {
            throw Invalid(declaredTotalLength < source.Length
                ? "Container has undeclared trailing bytes."
                : "Container is truncated before its declared end.");
        }

        ushort sessionProtocolVersion = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(14, sizeof(ushort)));
        ushort entryCount = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(16, sizeof(ushort)));
        if (entryCount > MaxEntries)
        {
            throw Invalid($"Entry count {entryCount} exceeds the {MaxEntries}-entry limit.");
        }

        int offset = FixedHeaderLength;
        HashSet<string> seenIds = new(StringComparer.Ordinal);
        List<LanConnectTailEntry> knownEntries = new(entryCount);

        for (int index = 0; index < entryCount; index++)
        {
            byte idByteLength = ReadByte(source, ref offset, $"entry {index} ID length");
            if (idByteLength is 0 or > MaxEntryIdBytes)
            {
                throw Invalid($"Entry {index} ID length {idByteLength} is outside 1..{MaxEntryIdBytes}.");
            }

            ReadOnlySpan<byte> idBytes = ReadBytes(source, ref offset, idByteLength, $"entry {index} ID");
            string id;
            try
            {
                id = StrictUtf8.GetString(idBytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw Invalid($"Entry {index} ID is not valid UTF-8.", exception);
            }

            if (!seenIds.Add(id))
            {
                throw Invalid($"Duplicate entry ID '{id}'.");
            }

            ushort version = ReadUInt16(source, ref offset, $"entry '{id}' version");
            byte flags = ReadByte(source, ref offset, $"entry '{id}' flags");
            if ((flags & ~CriticalEntryFlag) != 0)
            {
                throw Invalid($"Entry '{id}' flags contain unknown bits: 0x{flags:x2}.");
            }

            bool isCritical = (flags & CriticalEntryFlag) != 0;
            uint payloadByteLength = ReadUInt32(source, ref offset, $"entry '{id}' payload length");
            if (payloadByteLength > MaxEntryPayloadBytes)
            {
                throw Invalid(
                    $"Entry '{id}' payload length {payloadByteLength} exceeds {MaxEntryPayloadBytes} bytes.");
            }

            ReadOnlySpan<byte> payload = ReadBytes(
                source,
                ref offset,
                checked((int)payloadByteLength),
                $"entry '{id}' payload");

            if (!IsKnownId(id))
            {
                if (isCritical)
                {
                    throw Invalid($"Unknown critical entry '{id}'.");
                }

                continue;
            }

            ValidateEntryContract(id, version, isCritical);
            knownEntries.Add(new LanConnectTailEntry(id, version, isCritical, payload));
        }

        if (offset != source.Length)
        {
            throw Invalid("Container has bytes not declared by entryCount.");
        }

        knownEntries.Sort(static (left, right) => CompareBytes(
            StrictUtf8.GetBytes(left.Id),
            StrictUtf8.GetBytes(right.Id)));
        return new LanConnectTailEnvelope(sessionProtocolVersion, knownEntries);
    }

    private static byte[] EncodeId(string id)
    {
        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(id);
        }
        catch (EncoderFallbackException exception)
        {
            throw Invalid("Entry ID is not valid UTF-16/UTF-8 text.", exception);
        }

        if (bytes.Length is 0 or > MaxEntryIdBytes)
        {
            throw Invalid($"Entry ID length {bytes.Length} is outside 1..{MaxEntryIdBytes} UTF-8 bytes.");
        }

        return bytes;
    }

    private static void ValidateEntryContract(string id, ushort version, bool isCritical)
    {
        if (IsKnownId(id))
        {
            if (version != 1)
            {
                throw Invalid($"Reserved entry '{id}' must use version 1, not {version}.");
            }

            if (!isCritical)
            {
                throw Invalid($"Reserved entry '{id}' must be critical.");
            }

            return;
        }

        if (isCritical)
        {
            throw Invalid($"Unknown critical entry '{id}'.");
        }
    }

    private static bool IsKnownId(string id) => id is
        LanConnectTailEntry.CapabilitiesId or
        LanConnectTailEntry.RejectionId or
        LanConnectTailEntry.RosterId;

    private static byte ReadByte(ReadOnlySpan<byte> source, ref int offset, string field)
    {
        if ((uint)offset >= (uint)source.Length)
        {
            throw Invalid($"Container is truncated at {field}.");
        }

        return source[offset++];
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, ref int offset, string field)
    {
        ReadOnlySpan<byte> bytes = ReadBytes(source, ref offset, sizeof(ushort), field);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, ref int offset, string field)
    {
        ReadOnlySpan<byte> bytes = ReadBytes(source, ref offset, sizeof(uint), field);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static ReadOnlySpan<byte> ReadBytes(
        ReadOnlySpan<byte> source,
        ref int offset,
        int length,
        string field)
    {
        int end;
        try
        {
            end = checked(offset + length);
        }
        catch (OverflowException exception)
        {
            throw Invalid($"Container length overflowed while reading {field}.", exception);
        }

        if (length < 0 || end > source.Length)
        {
            throw Invalid($"Container is truncated at {field}.");
        }

        ReadOnlySpan<byte> result = source.Slice(offset, length);
        offset = end;
        return result;
    }

    private static int CompareBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        int commonLength = Math.Min(left.Length, right.Length);
        for (int index = 0; index < commonLength; index++)
        {
            int comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) =>
        new(message, inner);

    private sealed record EncodedEntry(LanConnectTailEntry Entry, byte[] IdBytes);
}
