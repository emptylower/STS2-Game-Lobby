using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectStandaloneTailCarrierTests
{
    [Fact]
    public void Aligns_with_zero_bits_and_preserves_exact_carrier_neutral_bytes()
    {
        byte[] container = ReadFixture("tail-envelope-capabilities-v1.bin");
        PacketWriter writer = new() { WarnOnGrow = false };
        writer.WriteBool(true);
        writer.WriteBool(false);
        writer.WriteBool(true);

        LanConnectStandaloneTailPlacement written = LanConnectStandaloneTailCarrier.Write(writer, container);
        byte[] packet = writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();
        PacketReader reader = new();
        reader.Reset(packet);
        Assert.True(reader.ReadBool());
        Assert.False(reader.ReadBool());
        Assert.True(reader.ReadBool());
        LanConnectStandaloneTailPlacement read = LanConnectStandaloneTailCarrier.Read(reader);

        Assert.Equal(3, written.VanillaBodyEndBit);
        Assert.Equal(5, written.PaddingBits);
        Assert.Equal(8, written.ContainerStartBit);
        Assert.Equal(container, read.ContainerBytes);
        Assert.Equal(packet.Length * 8, read.ContainerEndBit);
    }

    [Fact]
    public void Rejects_nonzero_padding_truncation_and_trailing_bytes()
    {
        byte[] container = ReadFixture("tail-envelope-capabilities-v1.bin");
        PacketWriter writer = new() { WarnOnGrow = false };
        writer.WriteBool(true);
        writer.WriteBool(false);
        writer.WriteBool(true);
        _ = LanConnectStandaloneTailCarrier.Write(writer, container);
        byte[] packet = writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();

        byte[] nonzeroPadding = packet.ToArray();
        nonzeroPadding[0] |= 1 << 3;
        AssertReadFails(nonzeroPadding);
        AssertReadFails(packet[..^1]);
        AssertReadFails([.. packet, 0]);
    }

    private static void AssertReadFails(byte[] packet)
    {
        PacketReader reader = new();
        reader.Reset(packet);
        _ = reader.ReadBool();
        _ = reader.ReadBool();
        _ = reader.ReadBool();
        Assert.Throws<InvalidDataException>(() => LanConnectStandaloneTailCarrier.Read(reader));
    }

    private static byte[] ReadFixture(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllBytes(Path.Combine(
            directory?.FullName ?? throw new DirectoryNotFoundException(),
            "test-fixtures", "protocol", "v0.6", name));
    }
}
