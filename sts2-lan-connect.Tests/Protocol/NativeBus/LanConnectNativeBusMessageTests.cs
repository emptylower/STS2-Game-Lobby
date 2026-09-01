using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol.NativeBus;

public sealed class LanConnectNativeBusMessageTests
{
    private const uint LocalTypeId = 0x000000C8; // 200

    [Fact]
    public void Serialize_emits_exact_big_endian_outer_header_and_frame()
    {
        byte[] frame = FrameBytes(kind: LanConnectSidecarMessageKind.LobbyJoinRequest, sequence: 3, nonceSeed: 7);
        LanConnectNativeBusMessage message = new();
        message.Configure(LocalTypeId, frame);

        MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter writer =
            new() { WarnOnGrow = false };
        message.Serialize(writer);

        byte[] payload = writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();
        Assert.Equal(
        [
            0x4C, 0x42,                                     // magic "LB"
            0x01,                                           // ver
            0x00, 0x00, 0x00, 0xC8,                         // localTypeId 大端
            (byte)(frame.Length >> 24),                     // frameLen 大端
            (byte)(frame.Length >> 16),
            (byte)(frame.Length >> 8),
            (byte)frame.Length,
        ],
            payload.AsSpan(0, LanConnectNativeBusMessage.OuterHeaderBytes).ToArray());
        Assert.Equal(frame, payload.AsSpan(LanConnectNativeBusMessage.OuterHeaderBytes).ToArray());
        Assert.Equal(LanConnectNativeBusMessage.OuterHeaderBytes + frame.Length, payload.Length);
    }

    [Fact]
    public void Round_trips_through_vanilla_wire_header_with_little_endian_sender()
    {
        const ulong senderId = 0x0102030405060708UL;
        byte typeId = 0xC8;
        byte[] frame = FrameBytes(kind: LanConnectSidecarMessageKind.InitialGameInfo, sequence: 1, nonceSeed: 1);
        byte[] packet = BuildVanillaPacket(typeId, senderId, LocalTypeId, frame, trailing: []);

        MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader reader = new();
        reader.Reset(packet);
        Assert.Equal(typeId, reader.ReadByte());
        // 原版线头 senderId 由 PacketWriter.WriteULong 写出（小端）；ReadULong 读回验证。
        Assert.Equal(senderId, reader.ReadULong());

        LanConnectNativeBusMessage message = new();
        message.Deserialize(reader);
        Assert.Null(message.InvalidReason);
        Assert.Equal(LocalTypeId, message.LocalTypeId);
        Assert.Equal(frame, message.Frame);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(36)]
    public void Deserialize_ignores_trailing_bytes_after_frame(int trailingCount)
    {
        byte[] frame = FrameBytes(kind: LanConnectSidecarMessageKind.LobbyJoinResponse, sequence: 9, nonceSeed: 4);
        byte[] trailing = Enumerable.Range(0, trailingCount).Select(static value => (byte)(value * 7 + 1)).ToArray();
        byte[] packet = BuildVanillaPacket(0xC8, 1, LocalTypeId, frame, trailing);

        LanConnectNativeBusMessage decoded = DeserializePacket(packet);
        Assert.Null(decoded.InvalidReason);
        Assert.Equal(frame, decoded.Frame);
        Assert.Equal(LocalTypeId, decoded.LocalTypeId);
    }

    [Fact]
    public void Deserialize_accepts_frame_at_exactly_65000_bytes()
    {
        byte[] frame = new byte[LanConnectNativeBusMessage.MaxFrameBytes];
        Random.Shared.NextBytes(frame);
        byte[] packet = BuildVanillaPacket(0xC8, 1, LocalTypeId, frame, trailing: []);

        LanConnectNativeBusMessage decoded = DeserializePacket(packet);
        Assert.Null(decoded.InvalidReason);
        Assert.Equal(frame, decoded.Frame);
    }

    [Fact]
    public void Serialize_rejects_frame_above_65000_bytes_before_encoding()
    {
        LanConnectNativeBusMessage message = new();
        message.Configure(LocalTypeId, new byte[LanConnectNativeBusMessage.MaxFrameBytes + 1]);

        LanConnectProtocolException exception = Assert.Throws<LanConnectProtocolException>(
            () => message.Serialize(new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter { WarnOnGrow = false }));
        Assert.Equal("lan_native_frame_invalid", exception.Failure.Code);
    }

    [Fact]
    public void Deserialize_rejects_frame_length_above_receive_bound_without_throwing()
    {
        // 外层声明 frameLen = 65001，实际 frame 较短：接收边界拒绝且不抛。
        byte[] frame = new byte[64];
        byte[] packet = BuildVanillaPacket(0xC8, 1, localTypeId: LocalTypeId, frame, trailing: []);
        uint oversize = LanConnectNativeBusMessage.MaxFrameBytes + 1;
        packet[9 + 8] = (byte)(oversize >> 24);
        packet[9 + 9] = (byte)(oversize >> 16);
        packet[9 + 10] = (byte)(oversize >> 8);
        packet[9 + 11] = (byte)oversize;

        LanConnectNativeBusMessage decoded = DeserializePacket(packet);
        Assert.NotNull(decoded.InvalidReason);
        Assert.Null(decoded.Frame);
    }

    [Fact]
    public void Deserialize_rejects_truncated_packet_without_throwing()
    {
        byte[] frame = FrameBytes(kind: LanConnectSidecarMessageKind.InitialGameInfo, sequence: 1, nonceSeed: 2);
        byte[] packet = BuildVanillaPacket(0xC8, 1, LocalTypeId, frame, trailing: []);
        byte[] truncated = packet.AsSpan(0, LanConnectNativeBusMessage.VanillaWireHeaderBytes + 4).ToArray();

        LanConnectNativeBusMessage decoded = DeserializePacket(truncated);
        Assert.NotNull(decoded.InvalidReason);
        Assert.Null(decoded.Frame);
    }

    [Fact]
    public void Deserialize_rejects_bad_magic_and_bad_version_without_throwing()
    {
        byte[] frame = FrameBytes(kind: LanConnectSidecarMessageKind.InitialGameInfo, sequence: 1, nonceSeed: 3);

        byte[] badMagic = BuildVanillaPacket(0xC8, 1, LocalTypeId, frame, trailing: []);
        badMagic[9] = 0x4C;
        badMagic[10] = 0x44;
        Assert.NotNull(DeserializePacket(badMagic).InvalidReason);

        byte[] badVersion = BuildVanillaPacket(0xC8, 1, LocalTypeId, frame, trailing: []);
        badVersion[11] = 0x02;
        LanConnectNativeBusMessage decoded = DeserializePacket(badVersion);
        Assert.NotNull(decoded.InvalidReason);
        Assert.Null(decoded.Frame);
    }

    [Fact]
    public void Deserialize_rejects_packet_above_66000_bytes_without_throwing()
    {
        byte[] frame = new byte[LanConnectNativeBusMessage.MaxFrameBytes];
        byte[] packet = BuildVanillaPacket(0xC8, 1, LocalTypeId, frame, trailing: new byte[1100]);

        LanConnectNativeBusMessage decoded = DeserializePacket(packet);
        Assert.NotNull(decoded.InvalidReason);
        Assert.Null(decoded.Frame);
    }

    internal static byte[] FrameBytes(
        LanConnectSidecarMessageKind kind,
        uint sequence,
        int nonceSeed)
    {
        byte[] nonce = new byte[16];
        Random.Shared.NextBytes(nonce);
        nonce[0] = (byte)nonceSeed;
        LanConnectSidecarFrame frame = new(
            kind,
            nonce,
            sequence,
            ReadFixture());
        return LanConnectSidecarFrameCodec.Encode(frame);
    }

    private static byte[] ReadFixture()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllBytes(Path.Combine(
            directory?.FullName ?? throw new DirectoryNotFoundException(),
            "test-fixtures", "protocol", "v0.6", "tail-envelope-capabilities-v1.bin"));
    }

    internal static byte[] BuildVanillaPacket(
        byte typeId,
        ulong senderId,
        uint localTypeId,
        byte[] frame,
        byte[] trailing)
    {
        // 原版线头 [typeId:1][senderId:8 小端] 与 PacketWriter.WriteULong 的字节序一致。
        MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter writer = new() { WarnOnGrow = false };
        writer.WriteByte(typeId);
        writer.WriteULong(senderId);
        LanConnectNativeBusMessage message = new();
        message.Configure(localTypeId, frame);
        message.Serialize(writer);
        byte[] packet = new byte[writer.BytePosition + trailing.Length];
        writer.Buffer.AsSpan(0, writer.BytePosition).CopyTo(packet);
        trailing.AsSpan().CopyTo(packet.AsSpan(writer.BytePosition));
        return packet;
    }

    internal static LanConnectNativeBusMessage DeserializePacket(byte[] packet)
    {
        MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader reader = new();
        reader.Reset(packet);
        _ = reader.ReadByte();
        _ = reader.ReadULong();
        LanConnectNativeBusMessage message = new();
        message.Deserialize(reader);
        return message;
    }
}
