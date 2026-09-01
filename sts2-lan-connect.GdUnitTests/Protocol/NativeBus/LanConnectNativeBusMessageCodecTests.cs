using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol.NativeBus;

/// <summary>
/// 外层帧纯字节编解码 golden vector（spec §3.1 边界：65000/65001、尾随 0/1/30/36、
/// 截断、magic/ver 拒绝、66000 上限）。类型实现游戏接口，须在 Godot 进程内运行。
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectNativeBusMessageCodecTests
{
    private const uint LocalTypeId = 0x000000C8; // 200

    [TestCase]
    public void Encode_emits_exact_big_endian_outer_header_and_frame()
    {
        byte[] frame = FrameBytes(LanConnectSidecarMessageKind.LobbyJoinRequest, 3);
        byte[] payload = LanConnectNativeBusMessage.EncodeOuterFrame(LocalTypeId, frame);

        AssertThat(payload.AsSpan(0, LanConnectNativeBusMessage.OuterHeaderBytes).ToArray()).IsEqual(new byte[]
        {
            0x4C, 0x42,
            0x01,
            0x00, 0x00, 0x00, 0xC8,
            (byte)(frame.Length >> 24),
            (byte)(frame.Length >> 16),
            (byte)(frame.Length >> 8),
            (byte)(frame.Length & 0xff),
        });
        AssertThat(payload.AsSpan(LanConnectNativeBusMessage.OuterHeaderBytes).ToArray()).IsEqual(frame);
        AssertThat(payload.Length).IsEqual(LanConnectNativeBusMessage.OuterHeaderBytes + frame.Length);
    }

    [TestCase]
    public void Round_trip_returns_the_exact_frame_and_local_type_id()
    {
        byte[] frame = FrameBytes(LanConnectSidecarMessageKind.InitialGameInfo, 1);
        byte[] payload = LanConnectNativeBusMessage.EncodeOuterFrame(LocalTypeId, frame);

        int consumed = LanConnectNativeBusMessage.TryDecodeOuterFrame(
            payload, out byte[]? decoded, out uint localTypeId, out string? invalidReason);

        AssertThat(invalidReason).IsNull();
        AssertThat(localTypeId).IsEqual(LocalTypeId);
        AssertThat(decoded).IsEqual(frame);
        AssertThat(consumed).IsEqual(LanConnectNativeBusMessage.OuterHeaderBytes + frame.Length);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(30)]
    [TestCase(36)]
    public void Decode_ignores_trailing_bytes_after_frame(int trailingCount)
    {
        byte[] frame = FrameBytes(LanConnectSidecarMessageKind.LobbyJoinResponse, 9);
        byte[] payload = LanConnectNativeBusMessage.EncodeOuterFrame(LocalTypeId, frame);
        byte[] withTrailing = new byte[payload.Length + trailingCount];
        payload.CopyTo(withTrailing, 0);

        int consumed = LanConnectNativeBusMessage.TryDecodeOuterFrame(
            withTrailing, out byte[]? decoded, out _, out string? invalidReason);

        AssertThat(invalidReason).IsNull();
        AssertThat(decoded).IsEqual(frame);
        AssertThat(consumed).IsEqual(payload.Length);
    }

    [TestCase]
    public void Decode_accepts_frame_at_exactly_65000_bytes()
    {
        byte[] frame = new byte[LanConnectNativeBusMessage.MaxFrameBytes];
        byte[] payload = LanConnectNativeBusMessage.EncodeOuterFrame(LocalTypeId, frame);

        LanConnectNativeBusMessage.TryDecodeOuterFrame(
            payload, out byte[]? decoded, out _, out string? invalidReason);

        AssertThat(invalidReason).IsNull();
        AssertThat(decoded).IsEqual(frame);
    }

    [TestCase]
    public void Encode_rejects_frame_above_65000_bytes_before_encoding()
    {
        LanConnectProtocolException? exception = null;
        try
        {
            _ = LanConnectNativeBusMessage.EncodeOuterFrame(
                LocalTypeId,
                new byte[LanConnectNativeBusMessage.MaxFrameBytes + 1]);
        }
        catch (LanConnectProtocolException caught)
        {
            exception = caught;
        }

        AssertThat(exception).IsNotNull();
        AssertThat(exception!.Failure.Code).IsEqual("lan_native_frame_invalid");
    }

    [TestCase]
    public void Decode_rejects_frame_length_above_receive_bound_without_throwing()
    {
        byte[] payload = LanConnectNativeBusMessage.EncodeOuterFrame(LocalTypeId, new byte[64]);
        uint oversize = LanConnectNativeBusMessage.MaxFrameBytes + 1;
        payload[7] = (byte)(oversize >> 24);
        payload[8] = (byte)(oversize >> 16);
        payload[9] = (byte)(oversize >> 8);
        payload[10] = (byte)(oversize & 0xff);

        LanConnectNativeBusMessage.TryDecodeOuterFrame(
            payload, out byte[]? decoded, out _, out string? invalidReason);

        AssertThat(invalidReason).IsNotNull();
        AssertThat(decoded).IsNull();
    }

    [TestCase]
    public void Decode_rejects_truncated_header_without_throwing()
    {
        byte[] frame = FrameBytes(LanConnectSidecarMessageKind.InitialGameInfo, 1);
        byte[] payload = LanConnectNativeBusMessage.EncodeOuterFrame(LocalTypeId, frame);

        LanConnectNativeBusMessage.TryDecodeOuterFrame(
            payload.AsSpan(0, 4).ToArray(), out byte[]? decoded, out _, out string? invalidReason);

        AssertThat(invalidReason).IsNotNull();
        AssertThat(decoded).IsNull();
    }

    [TestCase]
    public void Decode_rejects_bad_magic_and_bad_version_without_throwing()
    {
        byte[] frame = FrameBytes(LanConnectSidecarMessageKind.InitialGameInfo, 1);

        byte[] badMagic = LanConnectNativeBusMessage.EncodeOuterFrame(LocalTypeId, frame);
        badMagic[1] = 0x44;
        LanConnectNativeBusMessage.TryDecodeOuterFrame(
            badMagic, out byte[]? magicDecoded, out _, out string? magicReason);
        AssertThat(magicReason).IsNotNull();
        AssertThat(magicDecoded).IsNull();

        byte[] badVersion = LanConnectNativeBusMessage.EncodeOuterFrame(LocalTypeId, frame);
        badVersion[2] = 0x02;
        LanConnectNativeBusMessage.TryDecodeOuterFrame(
            badVersion, out byte[]? versionDecoded, out _, out string? versionReason);
        AssertThat(versionReason).IsNotNull();
        AssertThat(versionDecoded).IsNull();
    }

    [TestCase]
    public void Decode_rejects_packet_above_66000_bytes_without_throwing()
    {
        byte[] frame = new byte[LanConnectNativeBusMessage.MaxFrameBytes];
        byte[] payload = LanConnectNativeBusMessage.EncodeOuterFrame(LocalTypeId, frame);
        byte[] withTrailing = new byte[payload.Length + 1100];
        payload.CopyTo(withTrailing, 0);

        LanConnectNativeBusMessage.TryDecodeOuterFrame(
            withTrailing, out byte[]? decoded, out _, out string? invalidReason);

        AssertThat(invalidReason).IsNotNull();
        AssertThat(decoded).IsNull();
    }

    private static byte[] FrameBytes(LanConnectSidecarMessageKind kind, uint sequence)
    {
        byte[] container = LanConnectTailCodec.Encode(
            1,
            [new LanConnectTailEntry(
                LanConnectTailEntry.CapabilitiesId,
                1,
                true,
                LanConnectCapabilitiesCodec.EncodeSessionSelection(Selection()))]);
        LanConnectSidecarFrame frame = new(
            kind,
            new byte[LanConnectSidecarFrameCodec.FlowNonceBytes],
            sequence,
            container);
        return LanConnectSidecarFrameCodec.Encode(frame);
    }

    private static LanConnectProtocolSelection Selection()
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            1,
            LanConnectProtocolCarrier.NativeBusV1,
            "0.6.1-alpha.1",
            8,
            "0.111.0",
            "aabb",
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }
}
