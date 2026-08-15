using GdUnit4;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectStandaloneTailCarrierRuntimeTests
{
    [TestCase]
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
        AssertThat(reader.ReadBool()).IsTrue();
        AssertThat(reader.ReadBool()).IsFalse();
        AssertThat(reader.ReadBool()).IsTrue();
        LanConnectStandaloneTailPlacement read = LanConnectStandaloneTailCarrier.Read(reader);

        AssertThat(written.VanillaBodyEndBit).IsEqual(3);
        AssertThat(written.PaddingBits).IsEqual(5);
        AssertThat(written.ContainerStartBit).IsEqual(8);
        AssertThat(read.ContainerBytes).IsEqual(container);
        AssertThat(read.ContainerEndBit).IsEqual(packet.Length * 8);
    }

    [TestCase]
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
        AssertThat(ReadFails(nonzeroPadding)).IsTrue();
        AssertThat(ReadFails(packet[..^1])).IsTrue();
        AssertThat(ReadFails([.. packet, 0])).IsTrue();
    }

    private static bool ReadFails(byte[] packet)
    {
        try
        {
            PacketReader reader = new();
            reader.Reset(packet);
            _ = reader.ReadBool();
            _ = reader.ReadBool();
            _ = reader.ReadBool();
            _ = LanConnectStandaloneTailCarrier.Read(reader);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
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
