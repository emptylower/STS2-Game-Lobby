using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol.NativeBus;

/// <summary>
/// 0.6.2 按对端寻址（peer-addressed type id）契约：
///   offer 携带本机 native bus typeId（解析失败留 null）；
///   线头 packet[0] == frame.localTypeId == 接收方声明的 recipientTypeId；
///   外层帧 ver == 2（ver=1 旧帧判定非法，见 LanConnectNativeBusMessageCodecTests）；
///   对端 id 越界 ⇒ lan_type_id_mismatch（不得静默回退本机 id）。
/// wire 路径经 PacketWriter / LanConnectNativeBusMessage 执行，须在 Godot 进程内运行。
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectProtocolOfferNativeBusTests
{
    private const int RecipientTypeId = 200;

    private static byte[] FlowNonce() =>
        Enumerable.Range(1, LanConnectSidecarFrameCodec.FlowNonceBytes).Select(static value => (byte)value).ToArray();

    private static byte[] Container() => LanConnectTailCodec.Encode(
        1,
        [new LanConnectTailEntry("lan.probe", 1, isCritical: false, "x"u8)]);

    [TestCase]
    public void CreateCurrent_carries_the_resolved_native_bus_type_id()
    {
        LanConnectNativeBusSender.TypeIdResolverForTesting = () => 207;
        try
        {
            LanConnectProtocolOffer offer = LanConnectProtocolOffer.CreateCurrent();

            AssertThat(offer.NativeBusTypeId.HasValue).IsTrue();
            AssertInt(offer.NativeBusTypeId!.Value).IsEqual(207);
        }
        finally
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
        }
    }

    [TestCase]
    public void CreateCurrent_leaves_native_bus_type_id_null_when_resolution_fails()
    {
        // 注册表不可用（TypeToId 抛出）⇒ NativeBusTypeId 留 null，不得产出猜测值。
        // RegistryFingerprint 由独立的 try/catch 计算，不受 typeId 解析失败影响，故此处不断言它。
        LanConnectNativeBusSender.TypeIdResolverForTesting =
            () => throw new InvalidOperationException("message registry is unavailable.");
        try
        {
            LanConnectProtocolOffer offer = LanConnectProtocolOffer.CreateCurrent();

            AssertThat(offer.NativeBusTypeId.HasValue).IsFalse();
        }
        finally
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
        }
    }

    [TestCase]
    public void Addressed_wire_payload_wires_the_recipient_type_id_into_header_and_frame()
    {
        (byte[] buffer, int length) = LanConnectNativeBusSender.BuildAddressedWirePayload(
            RecipientTypeId,
            senderNetId: 7,
            LanConnectSidecarMessageKind.LobbyJoinRequest,
            FlowNonce(),
            messageSequence: 1,
            Container());
        byte[] payload = buffer.AsSpan(0, length).ToArray();

        // 原版线头：typeId 1 字节（按对端寻址）+ senderId 8 字节小端。
        AssertThat(payload[0]).IsEqual((byte)RecipientTypeId);
        AssertThat(System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(1, 8))).IsEqual(7UL);

        // 外层帧：ver == 2，帧内 localTypeId 与线头字节同为接收方 id。
        int offset = LanConnectNativeBusMessage.VanillaWireHeaderBytes;
        AssertThat(payload[offset]).IsEqual(LanConnectNativeBusMessage.MagicFirst);
        AssertThat(payload[offset + 1]).IsEqual(LanConnectNativeBusMessage.MagicSecond);
        AssertThat(payload[offset + 2]).IsEqual(LanConnectNativeBusMessage.WireVersion);
        int consumed = LanConnectNativeBusMessage.TryDecodeOuterFrame(
            payload.AsSpan(offset),
            out byte[]? frame,
            out uint localTypeId,
            out string? invalidReason);
        AssertThat(invalidReason).IsNull();
        AssertThat(localTypeId).IsEqual((uint)RecipientTypeId);
        AssertThat(consumed > LanConnectNativeBusMessage.OuterHeaderBytes).IsTrue();
        AssertThat(frame).IsNotNull();
    }

    [TestCase]
    public void Out_of_range_recipient_type_ids_fail_structured_before_any_transport_work()
    {
        foreach (int recipientTypeId in new[] { -1, 256 })
        {
            LanConnectProtocolException? sendException = null;
            try
            {
                LanConnectNativeBusSender.Send(
                    transport: null!,
                    isHostTransport: true,
                    recipientPeerId: 22,
                    senderNetId: 7,
                    recipientTypeId,
                    LanConnectSidecarMessageKind.LobbyJoinRequest,
                    FlowNonce(),
                    messageSequence: 1,
                    Container());
            }
            catch (LanConnectProtocolException caught)
            {
                sendException = caught;
            }

            AssertThat(sendException).IsNotNull();
            AssertThat(sendException!.Failure.Code).IsEqual("lan_type_id_mismatch");

            LanConnectProtocolException? payloadException = null;
            try
            {
                _ = LanConnectNativeBusSender.BuildAddressedWirePayload(
                    recipientTypeId,
                    senderNetId: 7,
                    LanConnectSidecarMessageKind.LobbyJoinRequest,
                    FlowNonce(),
                    messageSequence: 1,
                    Container());
            }
            catch (LanConnectProtocolException caught)
            {
                payloadException = caught;
            }

            AssertThat(payloadException).IsNotNull();
            AssertThat(payloadException!.Failure.Code).IsEqual("lan_type_id_mismatch");
        }
    }
}
