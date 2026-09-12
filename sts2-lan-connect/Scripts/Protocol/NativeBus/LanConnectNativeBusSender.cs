using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// native_bus_v1 的专用发送出口（spec §3.2 第三级）。
///
/// 结构性递归免疫：入口置线程静态重入标志，finally 清除——本类发出的包再进入被补丁的
/// transport 方法时，postfix 直接跳过。手工拼装原版线头 [typeId:1][senderId:8 小端]
/// （与 NetMessageBus.SerializeMessage 线格式一致，不经过泛型 serializer），随后以
/// Reliable / ch0 直接调用 transport。
///
/// 按对端寻址（0.6.2 起）：typeId 是**接收方**本地表的下标，线头字节与帧内 localTypeId
/// 均写对端声明的 recipientTypeId，不再写本机 id。
/// </summary>
internal static class LanConnectNativeBusSender
{
    [ThreadStatic]
    private static bool _reentry;

    /// <summary>测试注入的 typeId 解析器；生产路径恒用 MessageTypes.TypeToId。</summary>
    internal static Func<int>? TypeIdResolverForTesting { get; set; }

    internal static bool ReentryForCurrentThread => _reentry;

    /// <summary>本机 LanConnectNativeBusMessage 的注册表 ID（测试可通过 TypeIdResolverForTesting 注入）。</summary>
    internal static int ResolveTypeId() =>
        TypeIdResolverForTesting != null
            ? TypeIdResolverForTesting()
            : MessageTypes.TypeToId<LanConnectNativeBusMessage>();

    internal static void Send(
        object transport,
        bool isHostTransport,
        ulong recipientPeerId,
        ulong senderNetId,
        int recipientTypeId,
        LanConnectSidecarMessageKind messageKind,
        ReadOnlySpan<byte> flowNonce,
        uint messageSequence,
        ReadOnlySpan<byte> container)
    {
        // 按对端寻址：缺失/非法的对端 id 一律结构化失败（不得静默回退到本机 id）。
        // 校验先于 transport 检查（纯值校验，xUnit 可直接测试）。
        if (recipientTypeId is < 0 or > 255)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_type_id_mismatch",
                $"Native bus recipient type id {recipientTypeId} is outside the 0..255 wire byte range.");
        }

        if (transport is not ENetHost and not ENetClient)
        {
            throw new InvalidOperationException(
                $"Native bus sender requires an ENet transport, got {transport?.GetType().FullName ?? "null"}.");
        }

        _reentry = true;
        try
        {
            (byte[] buffer, int length) = BuildAddressedWirePayload(
                recipientTypeId,
                senderNetId,
                messageKind,
                flowNonce,
                messageSequence,
                container);

            Log.Info(
                $"sts2_lan_connect tail: sending native extension {messageKind} seq={messageSequence} " +
                $"to peer {recipientPeerId} ({length} bytes, recipientTypeId={recipientTypeId}, host={isHostTransport}).");
            if (isHostTransport)
            {
                ((ENetHost)transport).SendMessageToClient(
                    recipientPeerId,
                    buffer,
                    length,
                    NetTransferMode.Reliable,
                    0);
            }
            else
            {
                ((ENetClient)transport).SendMessageToHost(
                    buffer,
                    length,
                    NetTransferMode.Reliable,
                    0);
            }
        }
        finally
        {
            _reentry = false;
        }
    }

    /// <summary>
    /// 按对端寻址拼装完整 wire 载荷：原版线头 [typeId:1][senderId:8 小端] + 外层帧。
    /// 纯字节路径（不触达 transport），供 xUnit 直接验证线头字节与帧内 localTypeId。
    /// </summary>
    internal static (byte[] Buffer, int Length) BuildAddressedWirePayload(
        int recipientTypeId,
        ulong senderNetId,
        LanConnectSidecarMessageKind messageKind,
        ReadOnlySpan<byte> flowNonce,
        uint messageSequence,
        ReadOnlySpan<byte> container)
    {
        if (recipientTypeId is < 0 or > 255)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_type_id_mismatch",
                $"Native bus recipient type id {recipientTypeId} is outside the 0..255 wire byte range.");
        }

        // 线头字节与帧内 localTypeId 必须同为接收方声明的 recipientTypeId。
        PacketWriter writer = new() { WarnOnGrow = false };
        writer.WriteByte(checked((byte)recipientTypeId));
        writer.WriteULong(senderNetId);
        LanConnectSidecarFrame frame = new(messageKind, flowNonce, messageSequence, container);
        LanConnectNativeBusMessage message = new();
        message.Configure((uint)recipientTypeId, LanConnectSidecarFrameCodec.Encode(frame));
        message.Serialize(writer);
        return (writer.Buffer, writer.BytePosition);
    }
}
