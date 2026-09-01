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

    public void Serialize(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (Frame is not { } frame)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_native_frame_invalid",
                "Native bus message has no frame to serialize.");
        }

        if (frame.Length > MaxFrameBytes)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_native_frame_invalid",
                $"Native bus frame length {frame.Length} exceeds the {MaxFrameBytes}-byte send limit.");
        }

        writer.WriteByte(MagicFirst);
        writer.WriteByte(MagicSecond);
        writer.WriteByte(WireVersion);
        writer.WriteByte(checked((byte)(LocalTypeId >> 24)));
        writer.WriteByte(checked((byte)(LocalTypeId >> 16)));
        writer.WriteByte(checked((byte)(LocalTypeId >> 8)));
        writer.WriteByte(checked((byte)(LocalTypeId & 0xff)));
        writer.WriteByte(checked((byte)(frame.Length >> 24)));
        writer.WriteByte(checked((byte)(frame.Length >> 16)));
        writer.WriteByte(checked((byte)(frame.Length >> 8)));
        writer.WriteByte(checked((byte)(frame.Length & 0xff)));
        writer.WriteBytes(frame, frame.Length);
    }

    public void Deserialize(PacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        InvalidReason = null;
        Frame = null;
        LocalTypeId = 0;

        byte[] buffer = reader.Buffer;
        int remaining = buffer.Length - (reader.BitPosition + 7) / 8;
        if (remaining < OuterHeaderBytes)
        {
            InvalidReason = $"Outer header truncated: {remaining} bytes available, {OuterHeaderBytes} required.";
            return;
        }

        byte magicFirst = reader.ReadByte();
        byte magicSecond = reader.ReadByte();
        byte version = reader.ReadByte();
        uint localTypeId = (uint)(reader.ReadByte() << 24)
                           | (uint)(reader.ReadByte() << 16)
                           | (uint)(reader.ReadByte() << 8)
                           | reader.ReadByte();
        uint frameLength = (uint)(reader.ReadByte() << 24)
                           | (uint)(reader.ReadByte() << 16)
                           | (uint)(reader.ReadByte() << 8)
                           | reader.ReadByte();

        if (magicFirst != MagicFirst || magicSecond != MagicSecond)
        {
            InvalidReason = $"Outer magic mismatch: {magicFirst:X2} {magicSecond:X2}.";
            return;
        }

        if (version != WireVersion)
        {
            InvalidReason = $"Outer version {version} is unsupported.";
            return;
        }

        if (buffer.Length > MaxPacketBytes)
        {
            InvalidReason = $"Packet length {buffer.Length} exceeds the {MaxPacketBytes}-byte receive limit.";
            return;
        }

        // 此时 reader.BytePosition == 20（原版 9 字节线头 + 11 字节外层头）。
        // frameLen 必须落在界定内（packet 长度 − 20），尾随内容忽略。
        if (frameLength > buffer.Length - VanillaWireHeaderBytes - OuterHeaderBytes)
        {
            InvalidReason = $"Frame length {frameLength} exceeds the {buffer.Length - VanillaWireHeaderBytes - OuterHeaderBytes}-byte receive bound.";
            return;
        }

        byte[] frame = new byte[frameLength];
        if (frameLength > 0)
        {
            reader.ReadBytes(frame, checked((int)frameLength));
        }

        LocalTypeId = localTypeId;
        Frame = frame;
    }
}
