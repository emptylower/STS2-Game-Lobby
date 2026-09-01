using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// native_bus_v1 载体的自定义总线消息类型（v0.7 起）。
///
/// ⚠️ 类名与命名空间冻结声明：`NetTypeCache` 经 `ContentSorter` 多级排序分配消息 ID（排序键含
/// Type.FullName / assembly），本类型一经发布**永不改名、永不移动命名空间**——改名即换 ID，
/// 与所有已发布版本断连。
///
/// 外层帧格式（v0.7 spec §3.1；除原版 9 字节线头 senderId 为小端外，本消息字段一律大端）：
///   [magic:2 = 0x4C 0x42][ver:1 = 1][localTypeId:4 BE][frameLen:4 BE][frame:frameLen][尾随字节:忽略]
/// frame 为现有 LanConnectSidecarFrame 编码；尾随内容（如 RitsuLib native trailer，0.5.12 布局
/// 36 字节）由 frameLen 长度边界忽略。
///
/// Deserialize 契约：**非抛出**。任何读取失败只记录 InvalidReason 并返回，坏帧绝不炸穿原版
/// TryDeserializeMessage 接收循环；由配对屏障读取 InvalidReason 后统一转 lan_native_frame_invalid
/// 结构化失败并断开。
/// </summary>
public sealed class LanConnectNativeBusMessage : INetMessage
{
    internal const byte MagicFirst = 0x4C;
    internal const byte MagicSecond = 0x42;
    internal const byte WireVersion = 1;

    /// <summary>外层帧头字节数：magic(2) + ver(1) + localTypeId(4) + frameLen(4)。</summary>
    internal const int OuterHeaderBytes = 11;

    /// <summary>原版线头字节数：typeId(1) + senderId(8，小端)。</summary>
    internal const int VanillaWireHeaderBytes = 9;

    /// <summary>发送侧 frame 硬上限（编码前拒绝）。</summary>
    internal const int MaxFrameBytes = 65000;

    /// <summary>接收侧整包上限（为 RitsuLib trailer 等尾随内容预留余量）。</summary>
    internal const int MaxPacketBytes = 66000;

    // 目标地址由我们显式控制（单发或逐 peer 发送），不经宿主自动回广播。
    public bool ShouldBroadcast => false;

    // Reliable = ch0，与原版大厅消息同通道，保证成对有序。
    public NetTransferMode Mode => NetTransferMode.Reliable;

    public bool ShouldBuffer => true;

    public LogLevel LogLevel => LogLevel.VeryDebug;

    /// <summary>反序列化失败原因（成功时为 null），供配对屏障转结构化失败。</summary>
    public string? InvalidReason { get; private set; }

    /// <summary>仅 [frameLen] 界定内的字节；尾随内容忽略。</summary>
    public byte[]? Frame { get; private set; }

    /// <summary>发送端本机 TypeToId（大端字段）；接收端与本地 TypeToId 比对。</summary>
    public uint LocalTypeId { get; private set; }

    internal void Configure(uint localTypeId, ReadOnlySpan<byte> frame)
    {
        LocalTypeId = localTypeId;
        Frame = frame.ToArray();
        InvalidReason = null;
    }

    /// <summary>纯字节外层帧编码（xUnit golden vector 直接测试；发送侧超限在此拒绝）。</summary>
    internal static byte[] EncodeOuterFrame(uint localTypeId, ReadOnlySpan<byte> frame)
    {
        if (frame.Length > MaxFrameBytes)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_native_frame_invalid",
                $"Native bus frame length {frame.Length} exceeds the {MaxFrameBytes}-byte send limit.");
        }

        byte[] payload = new byte[OuterHeaderBytes + frame.Length];
        payload[0] = MagicFirst;
        payload[1] = MagicSecond;
        payload[2] = WireVersion;
        payload[3] = (byte)(localTypeId >> 24);
        payload[4] = (byte)(localTypeId >> 16);
        payload[5] = (byte)(localTypeId >> 8);
        payload[6] = (byte)(localTypeId & 0xff);
        payload[7] = (byte)(frame.Length >> 24);
        payload[8] = (byte)(frame.Length >> 16);
        payload[9] = (byte)(frame.Length >> 8);
        payload[10] = (byte)(frame.Length & 0xff);
        frame.CopyTo(payload.AsSpan(OuterHeaderBytes));
        return payload;
    }

    /// <summary>
    /// 纯字节外层帧解码（非抛出）。输入为原版 9 字节线头之后、从当前字节边界起的剩余载荷；
    /// 返回消费的字节数（成功 = 11 + frameLen），尾随内容忽略。
    /// </summary>
    internal static int TryDecodeOuterFrame(
        ReadOnlySpan<byte> payload,
        out byte[]? frame,
        out uint localTypeId,
        out string? invalidReason)
    {
        frame = null;
        localTypeId = 0;
        invalidReason = null;
        if (payload.Length < OuterHeaderBytes)
        {
            invalidReason = $"Outer header truncated: {payload.Length} bytes available, {OuterHeaderBytes} required.";
            return 0;
        }

        if (payload[0] != MagicFirst || payload[1] != MagicSecond)
        {
            invalidReason = $"Outer magic mismatch: {payload[0]:X2} {payload[1]:X2}.";
            return 0;
        }

        if (payload[2] != WireVersion)
        {
            invalidReason = $"Outer version {payload[2]} is unsupported.";
            return 0;
        }

        localTypeId = (uint)(payload[3] << 24)
                      | (uint)(payload[4] << 16)
                      | (uint)(payload[5] << 8)
                      | payload[6];
        uint frameLength = (uint)(payload[7] << 24)
                           | (uint)(payload[8] << 16)
                           | (uint)(payload[9] << 8)
                           | payload[10];

        // 接收边界：整包（含原版 9 字节线头）≤ 66000，且 frameLen ≤ 剩余界定内字节。
        int packetLength = VanillaWireHeaderBytes + payload.Length;
        if (packetLength > MaxPacketBytes)
        {
            invalidReason = $"Packet length {packetLength} exceeds the {MaxPacketBytes}-byte receive limit.";
            return 0;
        }

        if (frameLength > payload.Length - OuterHeaderBytes)
        {
            invalidReason =
                $"Frame length {frameLength} exceeds the {payload.Length - OuterHeaderBytes}-byte receive bound.";
            return 0;
        }

        frame = payload.Slice(OuterHeaderBytes, checked((int)frameLength)).ToArray();
        return OuterHeaderBytes + checked((int)frameLength);
    }

    public void Serialize(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (Frame is not { } frame)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_native_frame_invalid",
                "Native bus message has no frame to serialize.");
        }

        byte[] payload = EncodeOuterFrame(LocalTypeId, frame);
        writer.WriteBytes(payload, payload.Length);
    }

    public void Deserialize(PacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        InvalidReason = null;
        Frame = null;
        LocalTypeId = 0;

        // 进入时恒为字节边界（原版 TryDeserializeMessage 已读 typeId:1 + senderId:8）。
        int consumed = TryDecodeOuterFrame(
            reader.Buffer.AsSpan((reader.BitPosition + 7) / 8),
            out byte[]? frame,
            out uint localTypeId,
            out string? invalidReason);
        InvalidReason = invalidReason;
        if (frame == null)
        {
            return;
        }

        Frame = frame;
        LocalTypeId = localTypeId;
        // 推进读取位置到 frameLen 界定边界（尾随内容留给后续读取方）。
        reader.ReadBytes(new byte[consumed], consumed);
    }
}
