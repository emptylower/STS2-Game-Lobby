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
        LanConnectSidecarMessageKind messageKind,
        ReadOnlySpan<byte> flowNonce,
        uint messageSequence,
        ReadOnlySpan<byte> container)
    {
        if (transport is not ENetHost and not ENetClient)
        {
            throw new InvalidOperationException(
                $"Native bus sender requires an ENet transport, got {transport?.GetType().FullName ?? "null"}.");
        }

        _reentry = true;
        try
        {
            LanConnectSidecarFrame frame = new(messageKind, flowNonce, messageSequence, container);
            byte[] encodedFrame = LanConnectSidecarFrameCodec.Encode(frame);

            int typeId = TypeIdResolverForTesting != null
                ? TypeIdResolverForTesting()
                : MessageTypes.TypeToId<LanConnectNativeBusMessage>();

            // 原版线头：typeId 1 字节 + senderId 8 字节小端（PacketWriter.WriteULong）。
            PacketWriter writer = new() { WarnOnGrow = false };
            writer.WriteByte(checked((byte)typeId));
            writer.WriteULong(senderNetId);
            LanConnectNativeBusMessage message = new();
            message.Configure((uint)typeId, encodedFrame);
            message.Serialize(writer);

            if (isHostTransport)
            {
                ((ENetHost)transport).SendMessageToClient(
                    recipientPeerId,
                    writer.Buffer,
                    writer.BytePosition,
                    NetTransferMode.Reliable,
                    0);
            }
            else
            {
                ((ENetClient)transport).SendMessageToHost(
                    writer.Buffer,
                    writer.BytePosition,
                    NetTransferMode.Reliable,
                    0);
            }
        }
        finally
        {
            _reentry = false;
        }
    }
}
