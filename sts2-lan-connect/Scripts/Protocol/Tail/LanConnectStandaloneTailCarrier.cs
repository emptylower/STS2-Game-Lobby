using System.Buffers.Binary;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectStandaloneTailPlacement(
    int VanillaBodyEndBit,
    int PaddingBits,
    int ContainerStartBit,
    int ContainerEndBit,
    byte[] ContainerBytes,
    LanConnectTailEnvelope Envelope);

internal static class LanConnectStandaloneTailCarrier
{
    private const int ByteBits = 8;
    private const int ContainerLengthOffset = 10;
    private const int MinimumLengthPrefixBytes = 14;

    internal static LanConnectStandaloneTailPlacement Write(
        PacketWriter writer,
        ReadOnlySpan<byte> container,
        LanConnectProtocolSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ValidateStandaloneSelection(selection);
        LanConnectTailEnvelope envelope = LanConnectTailCodec.Decode(container);
        int vanillaEnd = writer.BitPosition;
        int paddingBits = (ByteBits - (writer.BitPosition % ByteBits)) % ByteBits;
        for (int index = 0; index < paddingBits; index++)
        {
            writer.WriteBool(false);
        }

        int containerStart = writer.BitPosition;
        byte[] ownedContainer = container.ToArray();
        writer.WriteBytes(ownedContainer, ownedContainer.Length);
        return new LanConnectStandaloneTailPlacement(
            vanillaEnd,
            paddingBits,
            containerStart,
            writer.BitPosition,
            ownedContainer,
            envelope);
    }

    internal static LanConnectStandaloneTailPlacement Read(
        PacketReader reader,
        LanConnectProtocolSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ValidateStandaloneSelection(selection);
        byte[] buffer = reader.Buffer
            ?? throw new InvalidDataException("PacketReader has no input buffer.");
        int vanillaEnd = reader.BitPosition;
        int paddingBits = (ByteBits - (reader.BitPosition % ByteBits)) % ByteBits;
        RequireAvailableBits(reader, buffer, paddingBits, "standalone alignment padding");
        for (int index = 0; index < paddingBits; index++)
        {
            if (reader.ReadBool())
            {
                throw new InvalidDataException("Standalone alignment padding must be zero.");
            }
        }

        int containerStart = reader.BitPosition;
        int startByte = containerStart / ByteBits;
        if (buffer.Length - startByte < MinimumLengthPrefixBytes)
        {
            throw new InvalidDataException("Standalone container length prefix is truncated.");
        }

        uint bodyLength = BinaryPrimitives.ReadUInt32BigEndian(
            buffer.AsSpan(startByte + ContainerLengthOffset, sizeof(uint)));
        int containerLength;
        try
        {
            containerLength = checked((int)bodyLength + 8);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Standalone container length overflows Int32.", exception);
        }

        if (containerLength > LanConnectTailCodec.MaxContainerBytes
            || checked(startByte + containerLength) != buffer.Length)
        {
            throw new InvalidDataException(
                "Standalone container length is out of bounds or does not exactly consume the packet.");
        }

        byte[] container = buffer.AsSpan(startByte, containerLength).ToArray();
        LanConnectTailEnvelope envelope = LanConnectTailCodec.Decode(container);
        RequireAvailableBits(reader, buffer, checked(containerLength * ByteBits), "standalone container");
        byte[] consumed = new byte[containerLength];
        reader.ReadBytes(consumed, consumed.Length);
        if (!consumed.AsSpan().SequenceEqual(container))
        {
            throw new InvalidDataException("Standalone container cursor did not consume the expected bytes.");
        }

        return new LanConnectStandaloneTailPlacement(
            vanillaEnd,
            paddingBits,
            containerStart,
            reader.BitPosition,
            container,
            envelope);
    }

    private static void ValidateStandaloneSelection(LanConnectProtocolSelection? selection)
    {
        if (selection == null)
        {
            return;
        }

        if (selection.Profile != LanConnectProtocolProfile.TailV1
            || selection.Carrier != LanConnectProtocolCarrier.StandaloneTailV1
            || selection.RitsuLibPresent)
        {
            throw new InvalidDataException("Frozen selection does not permit the standalone Tail carrier.");
        }
    }

    private static void RequireAvailableBits(PacketReader reader, byte[] buffer, int bits, string field)
    {
        long end = checked((long)reader.BitPosition + bits);
        if (bits < 0 || end > checked((long)buffer.Length * ByteBits))
        {
            throw new InvalidDataException($"Packet is truncated at {field}.");
        }
    }
}
