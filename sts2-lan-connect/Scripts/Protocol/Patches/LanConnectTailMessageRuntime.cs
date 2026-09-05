using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Sts2LanConnect.Scripts;

/// <summary>传输层接收上下文（OnPacketReceived prefix 捕获；配对身份只认传输层 sender）。</summary>
internal sealed record LanConnectTransportReceiveContext(
    ulong SenderPeerId,
    NetTransferMode Mode,
    int Channel);

internal interface ILanConnectTailMessageRuntime
{
    LanConnectPreparedTailMessage PrepareOutgoing(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        object message,
        LanConnectProtocolSelection selection);

    bool TryPrepareConcreteOutgoing(
        PacketWriter writer,
        LanConnectSidecarMessageKind messageKind,
        object message,
        out LanConnectNativePreparedMessage? prepared);

    void CompleteConcreteOutgoing(LanConnectNativePreparedMessage prepared);

    void ClearPendingOutgoing(PacketWriter writer);

    LanConnectNativeSendContext? BeginNativeTransport(
        object transport,
        bool isHostTransport,
        ulong recipientPeerId,
        byte[] buffer,
        int length);

    void CompleteNativeTransport(LanConnectNativeSendContext? state, bool vanillaPeerReachable);

    void HandleNativeTransportFailure(LanConnectNativeSendContext? state, Exception exception);

    bool TryEnterNativeDispatch(NetMessageBus messageBus, INetMessage message, ulong senderId);

    void HandleIncomingFailure(
        NetMessageBus messageBus,
        ulong transportSenderPeerId,
        Exception exception,
        LanConnectProtocolSelection selection);
}

internal sealed record LanConnectPreparedTailMessage(object Message, byte[] Container);

internal sealed record LanConnectNativePreparedMessage(
    NetMessageBus MessageBus,
    PacketWriter Writer,
    LanConnectSidecarMessageKind MessageKind,
    ulong SenderPeerId,
    LanConnectProtocolSelection Selection,
    LanConnectPreparedTailMessage Prepared);

internal sealed record LanConnectNativeSendContext(
    NetMessageBus MessageBus,
    LanConnectNativePendingOutgoing Pending,
    object Transport,
    bool IsHostTransport,
    ulong RecipientPeerId);

internal sealed class LanConnectTailMessageRuntime : ILanConnectTailMessageRuntime
{
    private static readonly FieldInfo HostMessageBus = RequireMessageBus(typeof(NetHostGameService));
    private static readonly FieldInfo ClientMessageBus = RequireMessageBus(typeof(NetClientGameService));
    private static readonly FieldInfo NetMessageBusWriter =
        typeof(NetMessageBus).GetField("_writer", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(NetMessageBus).FullName, "_writer");
    private static readonly FieldInfo NetMessageBusBuffering =
        typeof(NetMessageBus).GetField("_isBufferingMessages", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(NetMessageBus).FullName, "_isBufferingMessages");
    private readonly object _sync = new();
    private readonly Dictionary<NetMessageBus, Binding> _bindings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PacketWriter, Binding> _writerBindings = new(ReferenceEqualityComparer.Instance);

    /// <summary>配对屏障超时（spec §3.3：暂存后 2000ms 未配对 ⇒ lan_extension_missing）。</summary>
    internal static readonly TimeSpan BarrierHoldTimeout = TimeSpan.FromMilliseconds(2000);

    [ThreadStatic]
    private static Stack<LanConnectProtocolFailure>? _outgoingRejections;
    [ThreadStatic]
    private static Dictionary<PacketWriter, LanConnectNativePendingOutgoing>? _pendingOutgoing;
    [ThreadStatic]
    private static int _nativeSubmitDepth;
    [ThreadStatic]
    private static bool _dispatchBypass;

    internal static LanConnectTailMessageRuntime Shared { get; } = new();

    /// <summary>桌面 seam prefix 在 SerializeMessage 体内触发，writer 只能经 bus 实例反射获取。</summary>
    internal static PacketWriter GetBusWriter(NetMessageBus messageBus)
    {
        ArgumentNullException.ThrowIfNull(messageBus);
        return NetMessageBusWriter.GetValue(messageBus) as PacketWriter
            ?? throw new InvalidOperationException("NetMessageBus._writer is unavailable.");
    }

    private static readonly ConcurrentDictionary<Type, object> TailPlayerAccessorsCache = new();

    internal static bool HasPendingOutgoingRejectionForCurrentThread =>
        _outgoingRejections is { Count: > 0 };

    internal static bool DispatchBypassForCurrentThread => _dispatchBypass;

    internal void BindHost(
        NetHostGameService service,
        LanConnectProtocolOffer offer,
        LanConnectProtocolSelection selection)
    {
        ArgumentNullException.ThrowIfNull(service);
        Bind(service, GetMessageBus(service), offer, selection, isHost: true);
    }

    internal void BindClient(
        NetClientGameService service,
        LanConnectProtocolOffer offer,
        LanConnectProtocolSelection selection,
        ReadOnlySpan<byte> protocolFlowNonce)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (protocolFlowNonce.Length != LanConnectSidecarFrameCodec.FlowNonceBytes)
        {
            throw Protocol(
                "lan_protocol_version_mismatch",
                "Tail client binding requires the 16-byte protocol flow nonce from its ticket.");
        }

        Bind(
            service,
            GetMessageBus(service),
            offer,
            selection,
            isHost: false,
            protocolFlowNonce.ToArray());
    }

    internal void Unbind(INetGameService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        NetMessageBus bus = GetMessageBus(service);
        lock (_sync)
        {
            if (!_bindings.Remove(bus, out Binding? binding))
            {
                return;
            }

            binding.MarkTerminated();
            _writerBindings.Remove(binding.Writer);
            ClearPendingOutgoing(binding.Writer);
        }
    }

    /// <summary>宿主侧绑定某 peer 的 native flow（nonce 来自该 peer 加入工单，经控制通道下发）。</summary>
    internal void PrepareHostNativeFlow(
        NetHostGameService service,
        ulong peerNetId,
        ReadOnlySpan<byte> protocolFlowNonce)
    {
        Binding binding = RequireBinding(GetMessageBus(service));
        binding.BindBidirectionalNativeFlow(service.NetId, peerNetId, protocolFlowNonce);
        Log.Info($"sts2_lan_connect tail: native flow bound for peer {peerNetId} (host side).");
    }

    /// <summary>激活宿主侧 native flow：flush 该 peer 的延迟扩展帧（InitialGameInfo/ConnectionFailed）。</summary>
    internal void ActivateHostNativeFlow(NetHostGameService service, ulong peerNetId)
    {
        Binding binding = RequireBinding(GetMessageBus(service));
        if (!binding.HasNativePeer(peerNetId))
        {
            Log.Info($"sts2_lan_connect tail: native flow activation skipped for peer {peerNetId} (no flow bound).");
            return;
        }

        PendingOutgoingNative? pending = binding.TakePendingOutgoingNative(peerNetId);
        Log.Info(
            $"sts2_lan_connect tail: native flow activated for peer {peerNetId}, " +
            $"deferred extension={(pending == null ? "none" : pending.MessageKind.ToString())}.");
        if (pending != null)
        {
            SendDeferredHostNative(binding, peerNetId, pending);
        }
    }

    internal void ClearNativePeer(INetGameService service, ulong peerNetId)
    {
        Binding binding = RequireBinding(GetMessageBus(service));
        binding.ClearNativePeer(peerNetId);
    }

    /// <summary>屏障超时巡检（由大厅 runtime tick 调用；入口路径亦会自巡检）。</summary>
    internal void SweepBarrierTimeouts(INetGameService service, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(service);
        Binding? binding;
        lock (_sync)
        {
            _bindings.TryGetValue(GetMessageBus(service), out binding);
        }

        binding?.SweepExpiredBarrierHolds(now);
    }

    public bool TryPrepareConcreteOutgoing(
        PacketWriter writer,
        LanConnectSidecarMessageKind messageKind,
        object message,
        out LanConnectNativePreparedMessage? prepared)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(message);
        prepared = null;

        Binding? binding;
        lock (_sync)
        {
            _writerBindings.TryGetValue(writer, out binding);
        }

        if (binding == null)
        {
            int writerBindingCount;
            lock (_sync)
            {
                writerBindingCount = _writerBindings.Count;
            }

            Log.Info($"sts2_lan_connect tail: prepare skipped for {messageKind}: writer has no binding (writerBindings={writerBindingCount}).");
            return false;
        }

        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        if (!snapshot.IsActive || snapshot.Selection?.Profile != LanConnectProtocolProfile.TailV1)
        {
            Log.Info($"sts2_lan_connect tail: prepare skipped for {messageKind}: session snapshot inactive or not tail (active={snapshot.IsActive}).");
            return false;
        }

        try
        {
            binding.ThrowIfTerminated();
            if (snapshot.Selection != binding.Selection)
            {
                throw new InvalidDataException(
                    "Bound PacketWriter selection differs from the active Tail session.");
            }

            // 桌面 seam 在 SerializeMessage 体内（Reset/header 写入之前）触发，writer 仍是
            // 上一条消息的残留：prepare 只校验已绑定 writer 与会话快照，header 校验统一
            // 延后到 CompleteConcreteOutgoing（android 顺序 header 先写同样满足）。
            // 体内 Reset() 的 detour 可能被优化编译内联绕过（Harmony 补挂闭合泛型会触发
            // 优化编译，与 RitsuLib 同机制）：新一轮序列化开始即视为旧 pending 失效，
            // 与 AndroidWriterResetPrefix 同语义补位。
            ClearStalePendingForWriter(writer);
            LanConnectPreparedTailMessage runtimePrepared = PrepareOutgoing(
                binding.MessageBus,
                messageKind,
                binding.Service.NetId,
                message,
                binding.Selection);
            prepared = new LanConnectNativePreparedMessage(
                binding.MessageBus,
                writer,
                messageKind,
                binding.Service.NetId,
                binding.Selection,
                runtimePrepared);
            return true;
        }
        catch (Exception exception)
        {
            AbortActiveBinding(binding, "native_concrete_prepare_failure", exception);
            throw;
        }
    }

    public void CompleteConcreteOutgoing(LanConnectNativePreparedMessage prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        Binding binding = RequireWriterBinding(prepared.Writer, prepared.MessageBus);
        try
        {
            binding.ThrowIfTerminated();
            if (prepared.Selection != binding.Selection)
            {
                throw new InvalidDataException(
                    "Native Tail serializer completed under a different Tail selection.");
            }

            ValidateNativeWriterHeader(
                prepared.Writer,
                binding,
                prepared.Prepared.Message,
                requireSerializeBoundary: false);
            Dictionary<PacketWriter, LanConnectNativePendingOutgoing> pending =
                _pendingOutgoing ??= new Dictionary<PacketWriter, LanConnectNativePendingOutgoing>(
                    ReferenceEqualityComparer.Instance);
            if (pending.ContainsKey(prepared.Writer))
            {
                throw new InvalidDataException(
                    "Native Tail writer published a second context before PacketWriter.Reset.");
            }

            byte[] buffer = prepared.Writer.Buffer;
            int length = prepared.Writer.BytePosition;
            pending.Add(
                prepared.Writer,
                new LanConnectNativePendingOutgoing(
                    this,
                    binding,
                    prepared.MessageKind,
                    prepared.SenderPeerId,
                    prepared.Prepared.Message,
                    prepared.Prepared.Container.ToArray(),
                    buffer,
                    length,
                    ComputeHeaderFingerprint(buffer, length)));
            Log.Info($"sts2_lan_connect tail: pending extension registered for {prepared.MessageKind} (vanilla bytes={length}).");
        }
        catch (Exception exception)
        {
            AbortActiveBinding(binding, "native_concrete_complete_failure", exception);
            throw;
        }
    }

    public void ClearPendingOutgoing(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (_pendingOutgoing is not { } pending
            || !pending.TryGetValue(writer, out LanConnectNativePendingOutgoing? context)
            || !ReferenceEquals(context.Owner, this))
        {
            return;
        }

        pending.Remove(writer);
        Log.Info($"sts2_lan_connect tail: pending extension for {context.MessageKind} cleared by writer reset before transport.");
    }

    /// <summary>
    /// 新一轮矩阵序列化开始：该 writer 的既有 pending 即为残留（体内 Reset 的 detour 被
    /// 内联绕过时由 prepare 补位清除；detour 正常触发时此处无操作）。
    /// </summary>
    private void ClearStalePendingForWriter(PacketWriter writer)
    {
        if (_pendingOutgoing is not { } pending
            || !pending.TryGetValue(writer, out LanConnectNativePendingOutgoing? stale)
            || !ReferenceEquals(stale.Owner, this))
        {
            return;
        }

        pending.Remove(writer);
        Log.Info(
            $"sts2_lan_connect tail: pending extension for {stale.MessageKind} superseded by the next serialization on the same writer.");
    }

    public LanConnectNativeSendContext? BeginNativeTransport(
        object transport,
        bool isHostTransport,
        ulong recipientPeerId,
        byte[] buffer,
        int length)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(buffer);
        if (_nativeSubmitDepth > 0 || LanConnectNativeBusSender.ReentryForCurrentThread)
        {
            return null;
        }

        LanConnectNativePendingOutgoing? context = ResolvePendingTransportContext(
            buffer,
            length,
            out IReadOnlyList<LanConnectNativePendingOutgoing> ambiguousMatches);
        if (ambiguousMatches.Count > 0)
        {
            // 内容匹配出现多候选：无法判定归属，终止全部候选绑定（幂等）。
            InvalidDataException ambiguous = new(
                "Native Tail transport buffer content-matched multiple pending contexts.");
            foreach (LanConnectNativePendingOutgoing candidate in ambiguousMatches)
            {
                AbortActiveBinding(candidate.Binding, "native_transport_begin_failure", ambiguous);
            }

            throw ambiguous;
        }

        if (context == null)
        {
            if (_pendingOutgoing is { Count: > 0 } unmatched)
            {
                string summary = string.Join(
                    ";",
                    unmatched.Values.Select(p => $"{p.MessageKind}:len={p.Length}:first={p.Buffer[0]}"));
                Log.Info(
                    $"sts2_lan_connect tail: transport buffer matched no pending (length={length}, first={(length > 0 ? buffer[0] : -1)}, pendings={summary}).");
            }

            return null;
        }

        Binding binding = context.Binding;
        try
        {
            binding.ThrowIfTerminated();
            ValidateTransportMatch(context, transport, isHostTransport, buffer, length);
            ulong resolvedRecipient = isHostTransport
                ? recipientPeerId
                : GetHostPeerId(binding.Service);
            if (resolvedRecipient == 0)
            {
                throw new InvalidDataException("Native Tail transport has no authenticated recipient.");
            }

            return new LanConnectNativeSendContext(
                binding.MessageBus,
                context,
                transport,
                isHostTransport,
                resolvedRecipient);
        }
        catch (Exception exception)
        {
            AbortActiveBinding(binding, "native_transport_begin_failure", exception);
            throw;
        }
    }

    public void CompleteNativeTransport(LanConnectNativeSendContext? state, bool vanillaPeerReachable)
    {
        if (state == null)
        {
            return;
        }

        Binding binding = state.Pending.Binding;
        try
        {
            binding.ThrowIfTerminated();

            // "未抛异常"不等于"发送成功"：host 目标 peer 不存在 / client 已断连 ⇒ 该 peer 结构化失败。
            if (!vanillaPeerReachable)
            {
                throw new InvalidDataException(
                    "Vanilla transport did not deliver to the intended peer.");
            }

            ulong recipient = state.RecipientPeerId;
            if (!state.Pending.ProcessedPeerIds.Add(recipient))
            {
                throw new InvalidDataException(
                    "Native Tail context was consumed twice for the same transport peer.");
            }

            // 宿主的 InitialGameInfo/ConnectionFailed 可能在控制通道绑定 nonce 之前发出：
            // 扩展帧延迟到 flow 绑定激活时补发（v0.6 deferred 注入语义复用）。
            if (binding.IsHost
                && state.Pending.MessageKind is LanConnectSidecarMessageKind.InitialGameInfo
                    or LanConnectSidecarMessageKind.ConnectionFailed
                && !binding.HasNativeFlow(state.Pending.SenderPeerId, recipient))
            {
                binding.RememberPendingOutgoingNative(
                    recipient,
                    new PendingOutgoingNative(
                        state.Pending.MessageKind,
                        state.Pending.SenderPeerId,
                        state.Pending.Container));
                Log.Info(
                    $"sts2_lan_connect tail: extension for {state.Pending.MessageKind} to peer {recipient} deferred " +
                    "(native flow not bound yet; flushed on control-channel binding).");
                return;
            }

            NativeFlow flow = RequireOutgoingFlow(binding, state.Pending.SenderPeerId, recipient);
            object? transport = ResolveTransport(binding);
            if (transport == null)
            {
                throw new InvalidDataException("Native Tail binding has no ENet transport.");
            }

            _nativeSubmitDepth++;
            try
            {
                LanConnectNativeBusSender.Send(
                    transport,
                    binding.IsHost,
                    recipient,
                    binding.Service.NetId,
                    state.Pending.MessageKind,
                    flow.FlowNonce,
                    flow.NextOutgoingSequence,
                    state.Pending.Container);
            }
            finally
            {
                _nativeSubmitDepth--;
            }

            flow.AdvanceOutgoing();
        }
        catch (Exception exception)
        {
            AbortActiveBinding(binding, "native_transport_failure", exception);
            throw;
        }
    }

    public void HandleNativeTransportFailure(LanConnectNativeSendContext? state, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (state == null)
        {
            return;
        }

        Binding? binding;
        lock (_sync)
        {
            _bindings.TryGetValue(state.MessageBus, out binding);
        }

        if (binding != null)
        {
            AbortActiveBinding(binding, "native_vanilla_transport_failure", exception);
        }
    }

    internal bool TryTakeValidatedRejection(
        INetGameService service,
        ulong senderPeerId,
        out LanConnectProtocolFailure? failure)
    {
        Binding binding = RequireBinding(GetMessageBus(service));
        lock (binding.Sync)
        {
            if (binding.ValidatedRejections.Remove(senderPeerId, out LanConnectProtocolFailure? value))
            {
                failure = value;
                return true;
            }
        }

        failure = null;
        return false;
    }

    public LanConnectPreparedTailMessage PrepareOutgoing(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        object message,
        LanConnectProtocolSelection selection)
    {
        Binding binding = RequireBinding(messageBus);
        ValidateBinding(binding, senderPeerId, selection);
        return messageKind switch
        {
            LanConnectSidecarMessageKind.LobbyJoinRequest or
            LanConnectSidecarMessageKind.LoadJoinRequest or
            LanConnectSidecarMessageKind.RejoinRequest => new(
                message,
                LanConnectTailMessagePatches.EncodePeerOfferMessage(messageKind, binding.Offer)),
            LanConnectSidecarMessageKind.InitialGameInfo => HasPendingOutgoingRejectionForCurrentThread
                ? PrepareRejection(message, selection)
                : SessionOnly(messageKind, message, selection),
            LanConnectSidecarMessageKind.LobbyJoinResponse => PrepareStartRunRoster(
                messageKind,
                RequireMessage<ClientLobbyJoinResponseMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.PlayerJoined => PreparePlayerJoined(
                RequireMessage<PlayerJoinedMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.LobbyBeginRun => PrepareBeginRun(
                RequireMessage<LobbyBeginRunMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.LoadJoinResponse => PrepareLoadJoin(
                RequireMessage<ClientLoadJoinResponseMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.RejoinResponse => PrepareRejoin(
                RequireMessage<ClientRejoinResponseMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.ConnectionFailed => PrepareRejection(message, selection),
            _ => throw Protocol("lan_protocol_version_mismatch", $"Unsupported Tail message kind {messageKind}.")
        };
    }

    /// <summary>直接入站校验入口（roster 恢复语义测试与屏障内部共用）。</summary>
    internal void ValidateIncoming(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong transportSenderPeerId,
        INetMessage message,
        byte[] container,
        LanConnectProtocolSelection selection)
    {
        Binding binding = RequireBinding(messageBus);
        ValidateBinding(binding, binding.Service.NetId, selection);
        ValidateIncomingCore(binding, messageKind, transportSenderPeerId, message, container, selection);
    }

    public void HandleIncomingFailure(
        NetMessageBus messageBus,
        ulong transportSenderPeerId,
        Exception exception,
        LanConnectProtocolSelection selection)
    {
        Binding binding = RequireBinding(messageBus);
        ValidateBinding(binding, binding.Service.NetId, selection);
        LanConnectProtocolFailure failure = exception is LanConnectProtocolException protocolException
            ? protocolException.Failure
            : Protocol("lan_protocol_version_mismatch", exception.Message).Failure;
        RejectAndDisconnect(binding, transportSenderPeerId, failure);
    }

    // ---- 配对屏障（spec §3.3：纯分发层，hold 一帧，零自有队列/零缓冲补丁） ----

    public bool TryEnterNativeDispatch(NetMessageBus messageBus, INetMessage message, ulong senderId)
    {
        if (_dispatchBypass)
        {
            return true;
        }

        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        if (!snapshot.IsActive || snapshot.Selection?.Profile != LanConnectProtocolProfile.TailV1)
        {
            return true;
        }

        Binding? binding;
        lock (_sync)
        {
            _bindings.TryGetValue(messageBus, out binding);
        }

        if (binding == null)
        {
            return true;
        }

        bool isNative = message is LanConnectNativeBusMessage;
        bool isMatrix = TryGetIncomingMessageKind(message, out LanConnectSidecarMessageKind kind);
        if (!isNative && !isMatrix)
        {
            return true;
        }

        // 传输上下文解析：旁挂表（缓冲释放路径）优先、消费即删；否则取 OnPacketReceived 线程上下文。
        if (!binding.TryTakeRecordedTransportContext(message, out LanConnectTransportReceiveContext context))
        {
            if (LanConnectTailMessagePatches.TryPeekTransportReceiveContext(
                    out LanConnectTransportReceiveContext threadContext))
            {
                context = threadContext;
                binding.RecordTransportContext(message, context);
            }
            else
            {
                FailPeer(
                    binding,
                    senderId,
                    "lan_protocol_version_mismatch",
                    "Tail message dispatch has no authenticated transport-sender context.");
                return false;
            }
        }

        binding.SweepExpiredBarrierHolds(DateTimeOffset.UtcNow);

        // 缓冲期：矩阵与扩展帧同入原版 _bufferedMessages，由原版按到达序统一释放（不进入 hold）。
        if (IsBusBuffering(messageBus))
        {
            return true;
        }

        if (isNative)
        {
            return HandleExtensionFrame(binding, messageBus, (LanConnectNativeBusMessage)message, context);
        }

        return HoldMatrixMessage(binding, message, kind, context);
    }

    private bool HandleExtensionFrame(
        Binding binding,
        NetMessageBus messageBus,
        LanConnectNativeBusMessage extension,
        LanConnectTransportReceiveContext context)
    {
        BarrierKey key = new(context.SenderPeerId, context.Channel);
        FailPeerCallback fail = (code, detail) => FailPeer(binding, context.SenderPeerId, code, detail);
        Log.Info(
            $"sts2_lan_connect tail: extension frame received from peer {context.SenderPeerId} on channel {context.Channel}, " +
            $"held={(binding.BarrierHolds.ContainsKey(key) ? "yes" : "no")}, invalid={extension.InvalidReason ?? "none"}.");

        // 扩展帧仅接受 channel == 0（ENet 入站 mode 恒为 None，不参与判定）。
        if (context.Channel != 0)
        {
            fail("lan_native_frame_invalid", $"Native extension frame arrived on channel {context.Channel}.");
            return false;
        }

        if (!binding.BarrierHolds.Remove(key, out HeldMessage? held))
        {
            fail("lan_native_frame_invalid", "Native extension frame arrived without its paired matrix message.");
            return false;
        }

        if (extension.InvalidReason != null)
        {
            fail("lan_native_frame_invalid", extension.InvalidReason);
            return false;
        }

        if (extension.Frame == null)
        {
            fail("lan_native_frame_invalid", "Native extension frame carries no frame payload.");
            return false;
        }

        if (extension.LocalTypeId != (uint)LanConnectNativeBusSender.ResolveTypeId())
        {
            fail(
                "lan_type_id_mismatch",
                $"Native extension frame localTypeId {extension.LocalTypeId} differs from local {LanConnectNativeBusSender.ResolveTypeId()}.");
            return false;
        }

        LanConnectSidecarFrame frame;
        try
        {
            frame = LanConnectSidecarFrameCodec.Decode(extension.Frame);
        }
        catch (InvalidDataException exception)
        {
            fail("lan_native_frame_invalid", exception.Message);
            return false;
        }

        if (frame.MessageKind != held.Kind)
        {
            fail(
                "lan_protocol_version_mismatch",
                $"Native extension kind {frame.MessageKind} does not pair with held {held.Kind}.");
            return false;
        }

        try
        {
            binding.ConsumeIncomingNativeFlow(context.SenderPeerId, frame.FlowNonce.Span, frame.MessageSequence);
            ValidateIncomingCore(
                binding,
                held.Kind,
                context.SenderPeerId,
                held.Message,
                frame.Container.ToArray(),
                binding.Selection);
        }
        catch (Exception exception) when (exception is InvalidDataException or LanConnectProtocolException)
        {
            fail(
                exception is LanConnectProtocolException protocol ? protocol.Failure.Code : "lan_protocol_version_mismatch",
                exception.Message);
            return false;
        }

        // 先应用扩展语义（projection 已恢复），再经 bypass 分发原版 handler。
        _dispatchBypass = true;
        try
        {
            messageBus.SendMessageToAllHandlers(held.Message, context.SenderPeerId);
        }
        finally
        {
            _dispatchBypass = false;
        }

        return false;
    }

    private bool HoldMatrixMessage(
        Binding binding,
        INetMessage message,
        LanConnectSidecarMessageKind kind,
        LanConnectTransportReceiveContext context)
    {
        BarrierKey key = new(context.SenderPeerId, context.Channel);
        if (binding.BarrierHolds.TryGetValue(key, out HeldMessage? existing))
        {
            // 同 (sender, channel) 的下一帧必须是扩展帧——出现第二条矩阵消息即背靠背不变量被破坏。
            binding.BarrierHolds.Remove(key);
            FailPeer(
                binding,
                context.SenderPeerId,
                "lan_extension_missing",
                $"Matrix message {kind} arrived while {existing.Kind} was still awaiting its extension frame.");
            return false;
        }

        binding.BarrierHolds[key] = new HeldMessage(message, kind, context, DateTimeOffset.UtcNow);
        Log.Info($"sts2_lan_connect tail: holding {kind} from peer {context.SenderPeerId} on channel {context.Channel}, awaiting extension frame.");
        // 屏障超时不再依赖“下一条消息到达时”自巡检：hold 存在期间必有定时清扫兜底。
        binding.EnsureBarrierSweepScheduled();
        return false;
    }

    private void FailPeer(
        Binding binding,
        ulong transportSenderPeerId,
        string code,
        string detail)
    {
        try
        {
            HandleIncomingFailure(
                binding.MessageBus,
                transportSenderPeerId,
                Protocol(code, detail),
                binding.Selection);
        }
        catch (Exception exception)
        {
            Log.Error(
                $"sts2_lan_connect tail: native dispatch failure handling aborted: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsBusBuffering(NetMessageBus messageBus) =>
        NetMessageBusBuffering.GetValue(messageBus) is true;

    private delegate void FailPeerCallback(string code, string detail);

    private void SendDeferredHostNative(
        Binding binding,
        ulong recipientPeerId,
        PendingOutgoingNative pending)
    {
        NativeFlow flow = RequireOutgoingFlow(binding, pending.SenderPeerId, recipientPeerId);
        object? transport = ResolveTransport(binding);
        if (transport == null)
        {
            throw new InvalidDataException("Native Tail binding has no ENet transport.");
        }

        _nativeSubmitDepth++;
        try
        {
            LanConnectNativeBusSender.Send(
                transport,
                isHostTransport: true,
                recipientPeerId,
                binding.Service.NetId,
                pending.MessageKind,
                flow.FlowNonce,
                flow.NextOutgoingSequence,
                pending.Container);
        }
        finally
        {
            _nativeSubmitDepth--;
        }

        flow.AdvanceOutgoing();
    }

    private void ValidateIncomingCore(
        Binding binding,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        INetMessage message,
        byte[] container,
        LanConnectProtocolSelection selection)
    {
        ulong hostPeerId = binding.IsHost ? binding.Service.NetId : GetHostPeerId(binding.Service);
        LanConnectTailMessagePayload payload;
        try
        {
            payload = LanConnectTailMessagePatches.DecodeAndValidate(
                messageKind,
                container,
                selection,
                senderPeerId,
                hostPeerId);
        }
        catch (InvalidDataException) when (messageKind == LanConnectSidecarMessageKind.InitialGameInfo)
        {
            payload = LanConnectTailMessagePatches.DecodeAndValidate(
                LanConnectSidecarMessageKind.ConnectionFailed,
                container,
                selection,
                senderPeerId,
                hostPeerId);
            messageKind = LanConnectSidecarMessageKind.ConnectionFailed;
        }

        if (IsRequest(messageKind))
        {
            if (!binding.IsHost || payload.PeerOffer == null)
            {
                throw new InvalidDataException("Only a host may accept a Tail join request.");
            }

            ValidatePeerOffer(payload.PeerOffer, selection);
            return;
        }

        if (messageKind == LanConnectSidecarMessageKind.ConnectionFailed)
        {
            if (payload.Rejection == null || senderPeerId != hostPeerId || binding.IsHost)
            {
                throw new InvalidDataException("Tail rejection must be sent by the current host to a joining client.");
            }

            lock (binding.Sync)
            {
                binding.ValidatedRejections[senderPeerId] = payload.Rejection;
            }
            return;
        }

        if (senderPeerId != hostPeerId || binding.IsHost)
        {
            throw new InvalidDataException("Session-selection messages must be received from the current host.");
        }

        switch (messageKind)
        {
            case LanConnectSidecarMessageKind.InitialGameInfo:
                break;
            case LanConnectSidecarMessageKind.LobbyJoinResponse:
                RestoreStartRunResponse(
                    message,
                    (ClientLobbyJoinResponseMessage)(object)message,
                    RequireRoster(payload),
                    binding);
                break;
            case LanConnectSidecarMessageKind.PlayerJoined:
                RestorePlayerJoined(message, (PlayerJoinedMessage)(object)message, RequireRoster(payload), binding);
                break;
            case LanConnectSidecarMessageKind.LobbyBeginRun:
                RestoreBeginRun(message, (LobbyBeginRunMessage)(object)message, RequireRoster(payload), binding);
                break;
            case LanConnectSidecarMessageKind.LoadJoinResponse:
                RestoreLoadJoin(message, (ClientLoadJoinResponseMessage)(object)message, RequireRoster(payload), binding);
                break;
            case LanConnectSidecarMessageKind.RejoinResponse:
                RestoreRejoin((ClientRejoinResponseMessage)(object)message, RequireRoster(payload), binding);
                break;
            default:
                throw new InvalidDataException($"Unexpected incoming Tail message kind {messageKind}.");
        }
    }

    private static LanConnectPreparedTailMessage SessionOnly(
        LanConnectSidecarMessageKind kind,
        object message,
        LanConnectProtocolSelection selection) =>
        new(message, LanConnectTailMessagePatches.EncodeSessionMessage(kind, selection));

    private static LanConnectPreparedTailMessage PrepareStartRunRoster(
        LanConnectSidecarMessageKind kind,
        ClientLobbyJoinResponseMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        List<StartRunLobbyPlayer> players = message.playersInLobby
            ?? throw new InvalidDataException("Join response has no lobby roster.");
        (List<StartRunLobbyPlayer> projection, IReadOnlyList<LanConnectRosterPlayerCarrier> carriers) =
            ProjectPlayers(players);
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().CommitHostSnapshot(carriers);
        message.playersInLobby = projection;
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(kind, selection, snapshot));
    }

    private static LanConnectPreparedTailMessage PrepareBeginRun(
        LobbyBeginRunMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        List<StartRunLobbyPlayer> players = message.playersInLobby
            ?? throw new InvalidDataException("Begin-run message has no lobby roster.");
        (List<StartRunLobbyPlayer> projection, IReadOnlyList<LanConnectRosterPlayerCarrier> carriers) =
            ProjectPlayers(players);
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().CommitHostSnapshot(carriers);
        message.playersInLobby = projection;
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.LobbyBeginRun,
                selection,
                snapshot));
    }

    private static LanConnectPreparedTailMessage PreparePlayerJoined(
        PlayerJoinedMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().Current
            ?? throw new InvalidDataException("PlayerJoined cannot precede the authoritative join response snapshot.");
        int canonicalIndex = snapshot.Players
            .OrderBy(static player => player.RealSlotId)
            .ThenBy(static player => player.PlayerId)
            .Select((player, index) => (player, index))
            .Single(value => value.player.PlayerId == message.lobbyPlayer.id)
            .index;
        message.lobbyPlayer.slotId = canonicalIndex % 4;
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.PlayerJoined,
                selection,
                snapshot));
    }

    private static LanConnectPreparedTailMessage PrepareLoadJoin(
        ClientLoadJoinResponseMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        IReadOnlyList<LanConnectRosterPlayerCarrier> carriers = BuildLoadJoinCarriers(
            message.serializableRun.Players,
            message.playersAlreadyConnected);
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().CommitHostSnapshot(carriers);
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.LoadJoinResponse,
                selection,
                snapshot));
    }

    private static LanConnectPreparedTailMessage PrepareRejoin(
        ClientRejoinResponseMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        IReadOnlyList<LanConnectRosterPlayerCarrier> carriers = message.serializableRun.Players
            .Select((player, index) => SerializeCarrier(player.NetId, index, player))
            .ToArray();
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().CommitHostSnapshot(carriers);
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.RejoinResponse,
                selection,
                snapshot));
    }

    private static LanConnectPreparedTailMessage PrepareRejection(
        object message,
        LanConnectProtocolSelection selection)
    {
        if (_outgoingRejections is not { Count: > 0 } rejections)
        {
            throw new InvalidOperationException("Tail rejection was serialized without a call-scoped protocol failure.");
        }

        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.ConnectionFailed,
                selection,
                rejection: rejections.Peek()));
    }

    private static void RestoreStartRunResponse(
        object boxedMessage,
        ClientLobbyJoinResponseMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        List<StartRunLobbyPlayer> projection = message.playersInLobby
            ?? throw new InvalidDataException("Join response has no vanilla roster projection.");
        AcceptRoster(binding, snapshot, LanConnectRosterSnapshotUse.Bootstrap);
        List<StartRunLobbyPlayer> restored = RestorePlayers(snapshot, projection);
        message.playersInLobby = restored;
        SetBoxedField(boxedMessage, nameof(ClientLobbyJoinResponseMessage.playersInLobby), message.playersInLobby);
    }

    private static void RestoreBeginRun(
        object boxedMessage,
        LobbyBeginRunMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        List<StartRunLobbyPlayer> projection = message.playersInLobby
            ?? throw new InvalidDataException("Begin-run has no vanilla roster projection.");
        LanConnectRosterAuthorityState authority = binding.RequireRoster();
        authority.Accept(
            snapshot.AuthorityPeerId,
            snapshot,
            LanConnectRosterSnapshotUse.StateTransition);
        List<StartRunLobbyPlayer> restored = RestorePlayers(snapshot, projection);
        message.playersInLobby = restored;
        SetBoxedField(boxedMessage, nameof(LobbyBeginRunMessage.playersInLobby), message.playersInLobby);
    }

    private static void RestorePlayerJoined(
        object boxedMessage,
        PlayerJoinedMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        LanConnectRosterAuthorityState authority = binding.RequireRoster();
        HashSet<ulong> membership = authority.Current?.Players
            .Select(static player => player.PlayerId)
            .ToHashSet() ?? [];
        membership.Add(message.lobbyPlayer.id);
        authority.Accept(
            snapshot.AuthorityPeerId,
            snapshot,
            LanConnectRosterSnapshotUse.MembershipMutation,
            membership,
            message.lobbyPlayer.id);
        StartRunLobbyPlayer restored = RestoreJoinedPlayer<StartRunLobbyPlayer>(
            snapshot.Players.Single(player => player.PlayerId == message.lobbyPlayer.id),
            snapshot,
            message.lobbyPlayer.id);
        SetBoxedField(boxedMessage, nameof(PlayerJoinedMessage.lobbyPlayer), restored);
    }

    private static void RestoreLoadJoin(
        object boxedMessage,
        ClientLoadJoinResponseMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        AcceptRoster(binding, snapshot, LanConnectRosterSnapshotUse.Bootstrap);
        List<LoadRunLobbyPlayer> restored = RestoreLoadJoinPlayers(
            snapshot,
            message.serializableRun.Players,
            message.playersAlreadyConnected);
        SetBoxedField(boxedMessage, nameof(ClientLoadJoinResponseMessage.playersAlreadyConnected), restored);
    }

    private static void RestoreRejoin(
        ClientRejoinResponseMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        AcceptRoster(binding, snapshot, LanConnectRosterSnapshotUse.Bootstrap);
        List<SerializablePlayer> restored = snapshot.Players.Select(carrier =>
        {
            SerializablePlayer player = DeserializeCarrier<SerializablePlayer>(carrier, out uint consumed);
            if (consumed != carrier.VanillaPlayerBitLength || player.NetId != carrier.PlayerId)
            {
                throw new InvalidDataException("Rejoin roster carrier disagrees with SerializableRun.Players.");
            }
            return player;
        }).ToList();
        if (!message.serializableRun.Players.Select(static player => player.NetId)
            .SequenceEqual(restored.Select(static player => player.NetId)))
        {
            throw new InvalidDataException("Rejoin vanilla membership disagrees with the Tail roster.");
        }
        message.serializableRun.Players = restored;
    }

    private static List<TPlayer> RestorePlayers<TPlayer>(
        LanConnectRosterSnapshot snapshot,
        IReadOnlyList<TPlayer> projection)
        where TPlayer : IPacketSerializable, new()
    {
        LanConnectTailPlayerAccessors<TPlayer> accessors = ResolvePlayerAccessors<TPlayer>();
        Func<TPlayer, int> getEmbeddedSlotId = accessors.GetSlotId
            ?? throw new NotSupportedException($"{typeof(TPlayer).FullName} has no readable slotId member.");
        LanConnectRosterProjection.Validate(snapshot, projection, accessors.GetId, getEmbeddedSlotId);
        return LanConnectRosterProjection.Restore(
            snapshot,
            carrier =>
            {
                TPlayer player = DeserializeCarrier<TPlayer>(carrier, out uint bits);
                return (player, bits);
            },
            accessors.GetId,
            getEmbeddedSlotId,
            (player, realSlot) =>
            {
                accessors.SetSlotId(ref player, realSlot);
                return player;
            }).ToList();
    }

    private static TPlayer RestoreJoinedPlayer<TPlayer>(
        LanConnectRosterPlayerCarrier carrier,
        LanConnectRosterSnapshot snapshot,
        ulong expectedId)
        where TPlayer : IPacketSerializable, new()
    {
        LanConnectTailPlayerAccessors<TPlayer> accessors = ResolvePlayerAccessors<TPlayer>();
        Func<TPlayer, int> getSlotId = accessors.GetSlotId
            ?? throw new NotSupportedException($"{typeof(TPlayer).FullName} has no readable slotId member.");
        TPlayer restored = DeserializeCarrier<TPlayer>(carrier, out uint consumed);
        if (consumed != carrier.VanillaPlayerBitLength || accessors.GetId(restored) != expectedId
            || getSlotId(restored) != snapshot.Players
                .OrderBy(static player => player.RealSlotId)
                .ThenBy(static player => player.PlayerId)
                .Select((player, index) => (player, index))
                .Single(value => value.player.PlayerId == accessors.GetId(restored)).index % 4)
        {
            throw new InvalidDataException("PlayerJoined body and Tail roster disagree.");
        }
        accessors.SetSlotId(ref restored, carrier.RealSlotId);
        return restored;
    }

    private static List<TPlayer> RestoreLoadJoinPlayers<TPlayer>(
        LanConnectRosterSnapshot snapshot,
        List<SerializablePlayer> savedPlayers,
        IReadOnlyList<TPlayer> alreadyConnected)
        where TPlayer : IPacketSerializable, new()
    {
        LanConnectTailPlayerAccessors<TPlayer> accessors = ResolvePlayerAccessors<TPlayer>();
        List<TPlayer> restored = snapshot.Players.Select(carrier =>
        {
            TPlayer player = DeserializeCarrier<TPlayer>(carrier, out uint consumed);
            if (consumed != carrier.VanillaPlayerBitLength
                || accessors.GetId(player) != carrier.PlayerId
                || savedPlayers.FindIndex(saved => saved.NetId == accessors.GetId(player)) != carrier.RealSlotId)
            {
                throw new InvalidDataException("Loaded-lobby roster carrier disagrees with the run/player binding.");
            }
            return player;
        }).ToList();
        if (!alreadyConnected.Select(accessors.GetId)
            .SequenceEqual(restored.Select(accessors.GetId)))
        {
            throw new InvalidDataException("Loaded-lobby vanilla membership disagrees with the Tail roster.");
        }

        return restored;
    }

    private static IReadOnlyList<LanConnectRosterPlayerCarrier> BuildLoadJoinCarriers<TPlayer>(
        List<SerializablePlayer> savedPlayers,
        IReadOnlyList<TPlayer> connectedPlayers)
        where TPlayer : IPacketSerializable, new()
    {
        LanConnectTailPlayerAccessors<TPlayer> accessors = ResolvePlayerAccessors<TPlayer>();
        return connectedPlayers
            .Select(player =>
            {
                int realSlot = savedPlayers.FindIndex(saved => saved.NetId == accessors.GetId(player));
                if (realSlot < 0)
                {
                    throw new InvalidDataException("Loaded-lobby player is absent from SerializableRun.Players.");
                }

                return SerializeCarrier(accessors.GetId(player), realSlot, player);
            })
            .ToArray();
    }

    private static (List<TPlayer> Projection, IReadOnlyList<LanConnectRosterPlayerCarrier> Carriers)
        ProjectPlayers<TPlayer>(IReadOnlyList<TPlayer> players)
        where TPlayer : IPacketSerializable, new()
    {
        LanConnectTailPlayerAccessors<TPlayer> accessors = ResolvePlayerAccessors<TPlayer>();
        Func<TPlayer, int> getSlotId = accessors.GetSlotId
            ?? throw new NotSupportedException($"{typeof(TPlayer).FullName} has no readable slotId member.");
        IReadOnlyList<LanConnectRosterProjectionItem<TPlayer>> projected =
            LanConnectRosterProjection.Create(
                players,
                accessors.GetId,
                getSlotId,
                (player, slot) =>
                {
                    accessors.SetSlotId(ref player, slot);
                    return player;
                });
        Dictionary<ulong, int> embeddedSlots = projected.ToDictionary(
            static item => item.PlayerId,
            static item => item.CanonicalIndex % 4);
        IReadOnlyList<LanConnectRosterPlayerCarrier> carriers = players
            .OrderBy(getSlotId)
            .ThenBy(accessors.GetId)
            .Select(player =>
            {
                int realSlot = getSlotId(player);
                ulong playerId = accessors.GetId(player);
                int canonicalIndex = embeddedSlots.TryGetValue(playerId, out int firstFourIndex)
                    ? firstFourIndex
                    : players.OrderBy(getSlotId).ThenBy(accessors.GetId)
                        .Select((value, index) => (value, index))
                        .Single(value => accessors.GetId(value.value) == playerId).index % 4;
                accessors.SetSlotId(ref player, canonicalIndex);
                return SerializeCarrier(playerId, realSlot, player);
            })
            .ToArray();
        return (projected.Select(static item => item.VanillaPlayer).ToList(), carriers);
    }

    private static LanConnectTailPlayerAccessors<TPlayer> ResolvePlayerAccessors<TPlayer>()
        where TPlayer : IPacketSerializable, new()
    {
        return (LanConnectTailPlayerAccessors<TPlayer>)TailPlayerAccessorsCache.GetOrAdd(
            typeof(TPlayer),
            static _ => LanConnectTailPlayerAccessors<TPlayer>.FromMembers("id", "slotId"));
    }

    private static LanConnectRosterPlayerCarrier SerializeCarrier<T>(ulong playerId, int realSlot, T player)
        where T : IPacketSerializable
    {
        PacketWriter writer = new() { WarnOnGrow = false };
        player.Serialize(writer);
        int byteLength = checked((writer.BitPosition + 7) / 8);
        return new LanConnectRosterPlayerCarrier(
            playerId,
            checked((byte)realSlot),
            checked((uint)writer.BitPosition),
            writer.Buffer.AsSpan(0, byteLength));
    }

    private static T DeserializeCarrier<T>(LanConnectRosterPlayerCarrier carrier, out uint consumedBits)
        where T : IPacketSerializable, new()
    {
        PacketReader reader = new();
        reader.Reset(carrier.VanillaPlayerBytes.ToArray());
        T value = reader.Read<T>();
        consumedBits = checked((uint)reader.BitPosition);
        return value;
    }

    private static void AcceptRoster(
        Binding binding,
        LanConnectRosterSnapshot snapshot,
        LanConnectRosterSnapshotUse firstUse)
    {
        LanConnectRosterAuthorityState authority = binding.RequireRoster();
        authority.Accept(
            snapshot.AuthorityPeerId,
            snapshot,
            authority.Current == null ? firstUse : LanConnectRosterSnapshotUse.CurrentState);
    }

    private static void ValidatePeerOffer(
        LanConnectProtocolOffer offer,
        LanConnectProtocolSelection selection)
    {
        offer.Validate();
        if (!offer.Supports(selection.SelectedLanProtocolVersion)
            || offer.RitsuLibPresent != selection.RitsuLibPresent)
        {
            throw new InvalidDataException("Peer offer is incompatible with the frozen Tail selection.");
        }
    }

    private NativeFlow RequireOutgoingFlow(Binding binding, ulong senderPeerId, ulong recipientPeerId)
    {
        if (!binding.IsHost)
        {
            // 客户端唯一的 flow 在首次发送时以工单 nonce 即时绑定（此时 NetId 已可用）。
            binding.EnsureClientNativeFlowBound();
        }

        return binding.RequireOutgoingNativeFlow(senderPeerId, recipientPeerId);
    }

    private static object? ResolveTransport(Binding binding) => binding.Service switch
    {
        NetHostGameService host => (object?)host.NetHost,
        NetClientGameService client => client.NetClient,
        _ => null
    };

    private void RejectAndDisconnect(
        Binding binding,
        ulong senderPeerId,
        LanConnectProtocolFailure failure)
    {
        if (binding.Service is NetHostGameService host)
        {
            Stack<LanConnectProtocolFailure> rejections =
                _outgoingRejections ??= new Stack<LanConnectProtocolFailure>();
            rejections.Push(failure);
            try
            {
                host.SendMessage(new InitialGameInfoMessage
                {
                    connectionFailureReason = ConnectionFailureReason.ModMismatch
                }, senderPeerId);
            }
            finally
            {
                LanConnectProtocolFailure popped = rejections.Pop();
                if (!ReferenceEquals(popped, failure))
                {
                    throw new InvalidOperationException("Tail rejection call context was corrupted.");
                }
                host.DisconnectClient(senderPeerId, NetError.ModMismatch);
            }
        }
        else
        {
            binding.Service.Disconnect(NetError.ModMismatch);
        }
    }

    private void Bind(
        INetGameService service,
        NetMessageBus bus,
        LanConnectProtocolOffer offer,
        LanConnectProtocolSelection selection,
        bool isHost,
        byte[]? protocolFlowNonce = null)
    {
        // 自检强制执行点：用户已发起 tail 会话，主菜单前的注册表初始化必然已完成。
        LanConnectNativeBusStartupCheck.EnsureReadyOrThrow();
        offer.Validate();
        selection.Validate(offer);
        Binding binding = new(this, service, offer, selection, isHost, protocolFlowNonce);
        lock (_sync)
        {
            if (_bindings.Remove(bus, out Binding? previous))
            {
                previous.MarkTerminated();
                _writerBindings.Remove(previous.Writer);
                ClearPendingOutgoing(previous.Writer);
            }

            _bindings[bus] = binding;
            _writerBindings[binding.Writer] = binding;
        }
    }

    private Binding RequireWriterBinding(PacketWriter writer, NetMessageBus expectedBus)
    {
        lock (_sync)
        {
            if (_writerBindings.TryGetValue(writer, out Binding? binding)
                && ReferenceEquals(binding.MessageBus, expectedBus))
            {
                return binding;
            }
        }

        throw new InvalidOperationException("Tail PacketWriter has no active ticket/session binding.");
    }

    private static void ValidateNativeWriterHeader(
        PacketWriter writer,
        Binding binding,
        object message,
        bool requireSerializeBoundary = true)
    {
        const int HeaderBits = 72;
        const int HeaderBytes = HeaderBits / 8;
        if (requireSerializeBoundary && writer.BitPosition != HeaderBits)
        {
            throw new InvalidDataException(
                $"Bound Native Tail writer expected BitPosition={HeaderBits}, actual={writer.BitPosition}.");
        }

        if (writer.BitPosition < HeaderBits || writer.Buffer.Length < HeaderBytes)
        {
            throw new InvalidDataException("Bound Native Tail writer has a truncated message header.");
        }

        if (message is not INetMessage netMessage)
        {
            throw new InvalidDataException("Native Tail concrete serializer received a non-network message.");
        }

        byte expectedMessageId = (byte)netMessage.ToId();
        if (writer.Buffer[0] != expectedMessageId)
        {
            throw new InvalidDataException("Bound Native Tail writer message ID does not match the concrete serializer.");
        }

        ulong headerSender = BinaryPrimitives.ReadUInt64LittleEndian(writer.Buffer.AsSpan(1, sizeof(ulong)));
        if (headerSender != binding.Service.NetId)
        {
            throw new InvalidDataException(
                "Bound Native Tail writer sender header differs from the authenticated local service.");
        }
    }

    /// <summary>
    /// 按“引用相等快路径 + 内容前缀匹配慢路径”解析传输层 buffer 对应的 pending：
    /// 第三方发送前缀（如 RitsuLib 0.5.18 NativeTrailer）会把原版包复制进加长的新数组，
    /// 因此慢路径只要求待发内容是传输 buffer 的前缀（后面可挂第三方 trailer）。
    /// 多于一个候选时通过 <paramref name="ambiguousMatches"/> 上报（走 AbortActiveBinding）。
    /// </summary>
    private LanConnectNativePendingOutgoing? ResolvePendingTransportContext(
        byte[] buffer,
        int length,
        out IReadOnlyList<LanConnectNativePendingOutgoing> ambiguousMatches)
    {
        ambiguousMatches = Array.Empty<LanConnectNativePendingOutgoing>();
        if (_pendingOutgoing is not { Count: > 0 } pending)
        {
            return null;
        }

        List<LanConnectNativePendingOutgoing> owned = pending.Values
            .Where(context => ReferenceEquals(context.Owner, this))
            .ToList();

        LanConnectNativePendingOutgoing? byReference = owned
            .FirstOrDefault(context => ReferenceEquals(context.Buffer, buffer));
        if (byReference != null)
        {
            return byReference;
        }

        if (length < 0)
        {
            return null;
        }

        List<LanConnectNativePendingOutgoing> candidates = owned
            .Where(context => length >= context.Length
                && buffer.AsSpan(0, context.Length).SequenceEqual(context.Buffer.AsSpan(0, context.Length)))
            .ToList();
        switch (candidates.Count)
        {
            case 0:
                return null;
            case 1:
                return candidates[0];
            default:
                ambiguousMatches = candidates;
                return null;
        }
    }

    private static void ValidateTransportMatch(
        LanConnectNativePendingOutgoing context,
        object transport,
        bool isHostTransport,
        byte[] buffer,
        int length)
    {
        Binding binding = context.Binding;
        bool transportMatches = isHostTransport
            ? binding.Service is NetHostGameService host
              && transport is ENetHost
              && ReferenceEquals(host.NetHost, transport)
            : binding.Service is NetClientGameService client
              && transport is ENetClient
              && ReferenceEquals(client.NetClient, transport);
        if (binding.IsHost != isHostTransport || !transportMatches)
        {
            throw new InvalidDataException(
                "Native Tail pending context reached a different ENet transport binding.");
        }

        ValidateTransportBuffer(context, buffer, length);
    }

    private static void ValidateTransportBuffer(
        LanConnectNativePendingOutgoing context,
        byte[] buffer,
        int length)
    {
        // 只要求待发内容是传输 buffer 的前缀：第三方发送前缀可在原包之后追加 trailer。
        if (length < context.Length || length > buffer.Length
            || !buffer.AsSpan(0, context.Length).SequenceEqual(context.Buffer.AsSpan(0, context.Length)))
        {
            throw new InvalidDataException(
                "Native Tail pending context buffer content does not prefix-match the vanilla transport.");
        }

        // 指纹按 pending 自身长度计算（trailer 不参与）。
        byte[] fingerprint = ComputeHeaderFingerprint(context.Buffer, context.Length);
        if (!CryptographicOperations.FixedTimeEquals(context.HeaderFingerprint, fingerprint))
        {
            throw new InvalidDataException(
                "Native Tail pending context header fingerprint does not match the vanilla transport.");
        }
    }

    private static byte[] ComputeHeaderFingerprint(byte[] buffer, int length)
    {
        const int HeaderBytes = 9;
        if (length < HeaderBytes || length > buffer.Length)
        {
            throw new InvalidDataException("Native Tail transport length cannot contain the authenticated header.");
        }

        return SHA256.HashData(buffer.AsSpan(0, HeaderBytes));
    }

    private void AbortActiveBinding(Binding binding, string reason, Exception exception)
    {
        ulong[] peerIds;
        lock (_sync)
        {
            if (!binding.TryMarkTerminated())
            {
                return;
            }

            if (_bindings.TryGetValue(binding.MessageBus, out Binding? current)
                && ReferenceEquals(current, binding))
            {
                _bindings.Remove(binding.MessageBus);
            }
            if (_writerBindings.TryGetValue(binding.Writer, out current)
                && ReferenceEquals(current, binding))
            {
                _writerBindings.Remove(binding.Writer);
            }
            ClearPendingOutgoing(binding.Writer);

            IEnumerable<ulong> peers = binding.BoundPeerIds;
            if (binding.Service is NetHostGameService host)
            {
                peers = peers.Concat(host.ConnectedPeers.Select(static peer => peer.peerId));
            }
            peerIds = peers.Distinct().ToArray();
            binding.ClearNative();
        }

        Log.Error(
            $"sts2_lan_connect tail: terminating active Native Tail binding reason={reason} " +
            $"exception={exception.GetType().FullName} hresult={exception.HResult}.");
        if (binding.Service is NetHostGameService netHost)
        {
            foreach (ulong peerId in peerIds)
            {
                try
                {
                    netHost.DisconnectClient(peerId, NetError.ModMismatch);
                }
                catch (Exception disconnectException)
                {
                    Log.Warn(
                        "sts2_lan_connect tail: host peer disconnect failed during binding termination: " +
                        disconnectException.GetType().Name);
                }
            }
        }
        else
        {
            try
            {
                binding.Service.Disconnect(NetError.ModMismatch);
            }
            catch (Exception disconnectException)
            {
                Log.Warn(
                    "sts2_lan_connect tail: client disconnect failed during binding termination: " +
                    disconnectException.GetType().Name);
            }
        }
    }

    private Binding RequireBinding(NetMessageBus messageBus)
    {
        lock (_sync)
        {
            return _bindings.TryGetValue(messageBus, out Binding? binding)
                ? binding
                : throw new InvalidOperationException("Tail message bus has no ticket/session binding.");
        }
    }

    private static void ValidateBinding(
        Binding binding,
        ulong localSenderPeerId,
        LanConnectProtocolSelection selection)
    {
        if (binding.Selection != selection || binding.Service.NetId != localSenderPeerId)
        {
            throw Protocol(
                "protocol_selection_conflict",
                "Tail message service/selection differs from the frozen session binding.");
        }
    }

    private static ulong GetHostPeerId(INetGameService service) => service switch
    {
        NetHostGameService host => host.NetId,
        NetClientGameService client => client.HostNetId,
        _ => throw new InvalidOperationException("Unsupported Tail net service type.")
    };

    private static NetMessageBus GetMessageBus(INetGameService service) => service switch
    {
        NetHostGameService host => HostMessageBus.GetValue(host) as NetMessageBus
            ?? throw new InvalidOperationException("NetHostGameService._messageBus is unavailable."),
        NetClientGameService client => ClientMessageBus.GetValue(client) as NetMessageBus
            ?? throw new InvalidOperationException("NetClientGameService._messageBus is unavailable."),
        _ => throw new InvalidOperationException("Unsupported Tail net service type.")
    };

    private static FieldInfo RequireMessageBus(Type serviceType) =>
        serviceType.GetField("_messageBus", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(serviceType.FullName, "_messageBus");

    private static T RequireMessage<T>(object message) where T : struct, INetMessage =>
        message is T typed
            ? typed
            : throw new InvalidOperationException(
                $"Expected {typeof(T).FullName}, got {message.GetType().FullName}.");

    private static void SetBoxedField(object boxedMessage, string fieldName, object value)
    {
        FieldInfo field = boxedMessage.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingFieldException(boxedMessage.GetType().FullName, fieldName);
        field.SetValue(boxedMessage, value);
    }

    internal static bool TryGetIncomingMessageKind(
        INetMessage message,
        out LanConnectSidecarMessageKind kind)
    {
        if (!LanConnectTailMessagePatches.TryGetMessageKind(message.GetType(), out kind))
        {
            return false;
        }

        if (kind == LanConnectSidecarMessageKind.InitialGameInfo
            && message is InitialGameInfoMessage { connectionFailureReason: not null })
        {
            kind = LanConnectSidecarMessageKind.ConnectionFailed;
        }

        return true;
    }

    private static LanConnectRosterSnapshot RequireRoster(LanConnectTailMessagePayload payload) =>
        payload.Roster ?? throw new InvalidDataException("Tail message is missing its authoritative roster.");

    private static bool IsRequest(LanConnectSidecarMessageKind kind) => kind is
        LanConnectSidecarMessageKind.LobbyJoinRequest or
        LanConnectSidecarMessageKind.LoadJoinRequest or
        LanConnectSidecarMessageKind.RejoinRequest;

    private static LanConnectProtocolException Protocol(string code, string detail) =>
        LanConnectProtocolFailureMapper.FromLocalException(code, detail);

    internal sealed class Binding
    {
        internal Binding(
            LanConnectTailMessageRuntime owner,
            INetGameService service,
            LanConnectProtocolOffer offer,
            LanConnectProtocolSelection selection,
            bool isHost,
            byte[]? protocolFlowNonce)
        {
            Owner = owner;
            Service = service;
            Offer = offer;
            Selection = selection;
            IsHost = isHost;
            ProtocolFlowNonce = protocolFlowNonce?.ToArray();
            MessageBus = GetMessageBus(service);
            Writer = NetMessageBusWriter.GetValue(MessageBus) as PacketWriter
                ?? throw new InvalidOperationException("NetMessageBus._writer is unavailable.");
            _roster = isHost ? new LanConnectRosterAuthorityState(service.NetId) : null;
        }

        internal LanConnectTailMessageRuntime Owner { get; }
        internal object Sync { get; } = new();
        internal INetGameService Service { get; }
        internal LanConnectProtocolOffer Offer { get; }
        internal LanConnectProtocolSelection Selection { get; }
        internal bool IsHost { get; }
        internal byte[]? ProtocolFlowNonce { get; }
        internal NetMessageBus MessageBus { get; }
        internal PacketWriter Writer { get; }
        private readonly Dictionary<NativeFlowKey, NativeFlow> _nativeFlows = [];
        private readonly Dictionary<ulong, PendingOutgoingNative> _pendingOutgoingNatives = [];
        private readonly Dictionary<BarrierKey, HeldMessage> _barrierHolds = [];
        private readonly ConditionalWeakTable<INetMessage, LanConnectTransportReceiveContext> _transportContexts = new();
        private LanConnectRosterAuthorityState? _roster;
        private int _terminated;
        private int _barrierSweepScheduled;

        internal void MarkTerminated() => Interlocked.Exchange(ref _terminated, 1);

        internal bool TryMarkTerminated() => Interlocked.Exchange(ref _terminated, 1) == 0;

        internal void ThrowIfTerminated()
        {
            if (Volatile.Read(ref _terminated) != 0)
            {
                throw new InvalidOperationException("Tail ticket/session binding has already terminated.");
            }
        }

        internal LanConnectRosterAuthorityState RequireRoster()
        {
            lock (Sync)
            {
                return _roster ??= new LanConnectRosterAuthorityState(GetHostPeerId(Service));
            }
        }

        internal Dictionary<ulong, LanConnectProtocolFailure> ValidatedRejections { get; } = [];

        internal Dictionary<BarrierKey, HeldMessage> BarrierHolds => _barrierHolds;

        internal IReadOnlyCollection<ulong> BoundPeerIds
        {
            get
            {
                lock (Sync)
                {
                    ulong localPeerId = Service.NetId;
                    return _nativeFlows.Keys
                        .Where(key => key.SenderPeerId == localPeerId || key.RecipientPeerId == localPeerId)
                        .Select(key => key.SenderPeerId == localPeerId ? key.RecipientPeerId : key.SenderPeerId)
                        .Where(peerId => peerId != localPeerId)
                        .Distinct()
                        .ToArray();
                }
            }
        }

        internal bool HasNativePeer(ulong peerNetId)
        {
            lock (Sync)
            {
                return _nativeFlows.Keys.Any(key =>
                    key.SenderPeerId == peerNetId || key.RecipientPeerId == peerNetId);
            }
        }

        internal bool HasNativeFlow(ulong senderPeerId, ulong recipientPeerId)
        {
            lock (Sync)
            {
                return _nativeFlows.ContainsKey(new NativeFlowKey(senderPeerId, recipientPeerId));
            }
        }

        internal void RememberPendingOutgoingNative(
            ulong recipientPeerId,
            PendingOutgoingNative pending)
        {
            lock (Sync)
            {
                if (!_pendingOutgoingNatives.ContainsKey(recipientPeerId)
                    && _pendingOutgoingNatives.Count >= LanConnectConstants.ProtocolMaxPlayers)
                {
                    throw new LanConnectProtocolException(
                        LanConnectProtocolFailureMapper.FromLocal(
                            "lan_protocol_version_mismatch",
                            "Deferred native extension queue exceeded the protocol player bound."));
                }

                _pendingOutgoingNatives[recipientPeerId] = pending;
            }
        }

        internal PendingOutgoingNative? TakePendingOutgoingNative(ulong recipientPeerId)
        {
            lock (Sync)
            {
                return _pendingOutgoingNatives.Remove(recipientPeerId, out PendingOutgoingNative? pending)
                    ? pending
                    : null;
            }
        }

        internal void BindBidirectionalNativeFlow(
            ulong localPeerId,
            ulong remotePeerId,
            ReadOnlySpan<byte> protocolFlowNonce)
        {
            if (protocolFlowNonce.Length != LanConnectSidecarFrameCodec.FlowNonceBytes)
            {
                throw Protocol(
                    "lan_protocol_version_mismatch",
                    "Native flow binding requires a 16-byte protocol flow nonce.");
            }

            lock (Sync)
            {
                BindNativeFlowLocked(localPeerId, remotePeerId, protocolFlowNonce);
                BindNativeFlowLocked(remotePeerId, localPeerId, protocolFlowNonce);
            }
        }

        internal void EnsureClientNativeFlowBound()
        {
            lock (Sync)
            {
                if (IsHost || _nativeFlows.Count > 0)
                {
                    return;
                }

                byte[] nonce = ProtocolFlowNonce
                    ?? throw new InvalidOperationException("Tail client binding has no protocol flow nonce.");
                BindBidirectionalNativeFlow(Service.NetId, GetHostPeerId(Service), nonce);
            }
        }

        internal NativeFlow RequireOutgoingNativeFlow(ulong senderPeerId, ulong recipientPeerId)
        {
            lock (Sync)
            {
                NativeFlowKey key = new(senderPeerId, recipientPeerId);
                if (_nativeFlows.TryGetValue(key, out NativeFlow? flow))
                {
                    return flow;
                }
            }

            throw new LanConnectProtocolException(
                LanConnectProtocolFailureMapper.FromLocal(
                    "lan_protocol_version_mismatch",
                    "Native outgoing flow has no trusted binding for the recipient."));
        }

        internal void ConsumeIncomingNativeFlow(
            ulong senderPeerId,
            ReadOnlySpan<byte> flowNonce,
            uint messageSequence)
        {
            lock (Sync)
            {
                if (!IsHost && _nativeFlows.Count == 0)
                {
                    // 客户端可能在首发（LobbyJoinRequest）之前先收到宿主的 InitialGameInfo
                    // 扩展帧——以工单 nonce 即时绑定（此时 NetId 已可用）。
                    EnsureClientNativeFlowBound();
                }

                NativeFlowKey key = new(senderPeerId, Service.NetId);
                if (!_nativeFlows.TryGetValue(key, out NativeFlow? flow))
                {
                    throw new InvalidDataException("Incoming native frame has no trusted flow binding.");
                }

                if (!flow.FlowNonce.SequenceEqual(flowNonce))
                {
                    throw new InvalidDataException("Incoming native frame flow nonce does not match the binding.");
                }

                if (messageSequence != flow.ExpectedIncomingSequence)
                {
                    throw new InvalidDataException(
                        $"Incoming native frame sequence {messageSequence} != expected {flow.ExpectedIncomingSequence}.");
                }

                flow.AdvanceIncoming();
            }
        }

        internal void RecordTransportContext(INetMessage message, LanConnectTransportReceiveContext context)
        {
            lock (Sync)
            {
                _transportContexts.AddOrUpdate(message, context);
            }
        }

        internal bool TryTakeRecordedTransportContext(
            INetMessage message,
            out LanConnectTransportReceiveContext context)
        {
            lock (Sync)
            {
                if (_transportContexts.TryGetValue(message, out LanConnectTransportReceiveContext? recorded))
                {
                    _transportContexts.Remove(message);
                    context = recorded;
                    return true;
                }
            }

            context = null!;
            return false;
        }

        /// <summary>
        /// 保证 hold 存在期间恰有一次待触发的定时清扫：BarrierHoldTimeout + 50ms 后在
        /// 消息分发所在的同步上下文（Godot 主线程经 GodotSynchronizationContext.Post；
        /// 无同步上下文时直接在线程池续体执行）调用 SweepExpiredBarrierHolds。
        /// 清扫后若仍有未到期 hold 则链式续排；绑定终止或 hold 表清空后链条自然终结。
        /// </summary>
        internal void EnsureBarrierSweepScheduled()
        {
            if (Volatile.Read(ref _terminated) != 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _barrierSweepScheduled, 1, 0) != 0)
            {
                return;
            }

            SynchronizationContext? dispatchContext = SynchronizationContext.Current;
            _ = Task.Delay(BarrierHoldTimeout + TimeSpan.FromMilliseconds(50))
                .ContinueWith(
                    _ =>
                    {
                        if (dispatchContext != null)
                        {
                            dispatchContext.Post(
                                static state => ((Binding)state!).RunScheduledBarrierSweep(),
                                this);
                        }
                        else
                        {
                            RunScheduledBarrierSweep();
                        }
                    },
                    TaskScheduler.Default);
        }

        private void RunScheduledBarrierSweep()
        {
            Interlocked.Exchange(ref _barrierSweepScheduled, 0);
            if (Volatile.Read(ref _terminated) != 0)
            {
                return;
            }

            SweepExpiredBarrierHolds(DateTimeOffset.UtcNow);

            bool holdsRemain;
            lock (Sync)
            {
                holdsRemain = _barrierHolds.Count > 0;
            }

            if (holdsRemain)
            {
                EnsureBarrierSweepScheduled();
            }
        }

        internal void SweepExpiredBarrierHolds(DateTimeOffset now)
        {
            List<KeyValuePair<BarrierKey, HeldMessage>> expired;
            lock (Sync)
            {
                expired = _barrierHolds
                    .Where(pair => now - pair.Value.HeldAt >= BarrierHoldTimeout)
                    .ToList();
                foreach (KeyValuePair<BarrierKey, HeldMessage> pair in expired)
                {
                    _barrierHolds.Remove(pair.Key);
                    _transportContexts.Remove(pair.Value.Message);
                }
            }

            foreach (KeyValuePair<BarrierKey, HeldMessage> pair in expired)
            {
                Log.Warn(
                    $"sts2_lan_connect tail: extension frame for {pair.Value.Kind} from peer " +
                    $"{pair.Key.SenderPeerId} did not arrive within {BarrierHoldTimeout.TotalMilliseconds:0}ms.");
                FailPeerForExpiredHold(pair.Value, pair.Key.SenderPeerId);
            }
        }

        private void FailPeerForExpiredHold(HeldMessage held, ulong senderPeerId)
        {
            try
            {
                Owner.HandleIncomingFailure(
                    MessageBus,
                    senderPeerId,
                    Protocol(
                        "lan_extension_missing",
                        $"Extension frame for held {held.Kind} did not arrive within the barrier timeout."),
                    Selection);
            }
            catch (Exception exception)
            {
                Log.Error(
                    $"sts2_lan_connect tail: barrier timeout rejection failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        internal void ClearNativePeer(ulong peerNetId)
        {
            lock (Sync)
            {
                foreach (NativeFlowKey key in _nativeFlows.Keys
                             .Where(key => key.SenderPeerId == peerNetId || key.RecipientPeerId == peerNetId)
                             .ToArray())
                {
                    _nativeFlows.Remove(key);
                }
                _pendingOutgoingNatives.Remove(peerNetId);
                foreach (BarrierKey key in _barrierHolds.Keys
                             .Where(key => key.SenderPeerId == peerNetId)
                             .ToArray())
                {
                    HeldMessage? held = _barrierHolds.GetValueOrDefault(key);
                    _barrierHolds.Remove(key);
                    if (held != null)
                    {
                        _transportContexts.Remove(held.Message);
                    }
                }
            }
        }

        internal void ClearNative()
        {
            lock (Sync)
            {
                _nativeFlows.Clear();
                _pendingOutgoingNatives.Clear();
                _barrierHolds.Clear();
            }
        }

        private void BindNativeFlowLocked(
            ulong senderPeerId,
            ulong recipientPeerId,
            ReadOnlySpan<byte> protocolFlowNonce)
        {
            NativeFlowKey key = new(senderPeerId, recipientPeerId);
            if (_nativeFlows.ContainsKey(key))
            {
                return;
            }

            _nativeFlows.Add(key, new NativeFlow(protocolFlowNonce));
        }
    }

    internal readonly record struct NativeFlowKey(ulong SenderPeerId, ulong RecipientPeerId);

    internal readonly record struct BarrierKey(ulong SenderPeerId, int Channel);

    internal sealed record PendingOutgoingNative(
        LanConnectSidecarMessageKind MessageKind,
        ulong SenderPeerId,
        byte[] Container);

    internal sealed class HeldMessage
    {
        internal HeldMessage(
            INetMessage message,
            LanConnectSidecarMessageKind kind,
            LanConnectTransportReceiveContext context,
            DateTimeOffset heldAt)
        {
            Message = message;
            Kind = kind;
            Context = context;
            HeldAt = heldAt;
        }

        internal INetMessage Message { get; }
        internal LanConnectSidecarMessageKind Kind { get; }
        internal LanConnectTransportReceiveContext Context { get; }
        internal DateTimeOffset HeldAt { get; }
    }

    internal sealed class NativeFlow
    {
        private readonly byte[] _flowNonce;

        internal NativeFlow(ReadOnlySpan<byte> flowNonce)
        {
            _flowNonce = flowNonce.ToArray();
        }

        internal ReadOnlySpan<byte> FlowNonce => _flowNonce;
        internal uint NextOutgoingSequence { get; private set; } = 1;
        internal uint ExpectedIncomingSequence { get; private set; } = 1;

        internal void AdvanceOutgoing()
        {
            if (NextOutgoingSequence == uint.MaxValue)
            {
                throw new InvalidDataException("Native flow outgoing message sequence is exhausted.");
            }

            NextOutgoingSequence++;
        }

        internal void AdvanceIncoming()
        {
            if (ExpectedIncomingSequence == uint.MaxValue)
            {
                throw new InvalidDataException("Native flow incoming message sequence is exhausted.");
            }

            ExpectedIncomingSequence++;
        }
    }
}

/// <summary>
/// writer 键控的待发上下文（spec §3.2 第二级）：一次矩阵序列化产物，服务宿主广播循环中的
/// 每个 peer 各一次（按 (pending, peer) 消费集去重），生命周期由 PacketWriter.Reset prefix
/// 与下一次矩阵序列化共同封口。
/// </summary>
internal sealed class LanConnectNativePendingOutgoing
{
    internal LanConnectNativePendingOutgoing(
        LanConnectTailMessageRuntime owner,
        LanConnectTailMessageRuntime.Binding binding,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        object message,
        byte[] container,
        byte[] buffer,
        int length,
        byte[] headerFingerprint)
    {
        Owner = owner;
        Binding = binding;
        MessageKind = messageKind;
        SenderPeerId = senderPeerId;
        Message = message;
        Container = container;
        Buffer = buffer;
        Length = length;
        HeaderFingerprint = headerFingerprint;
    }

    internal LanConnectTailMessageRuntime Owner { get; }
    internal LanConnectTailMessageRuntime.Binding Binding { get; }
    internal LanConnectSidecarMessageKind MessageKind { get; }
    internal ulong SenderPeerId { get; }
    internal object Message { get; }
    internal byte[] Container { get; }
    internal byte[] Buffer { get; }
    internal int Length { get; }
    internal byte[] HeaderFingerprint { get; }
    internal HashSet<ulong> ProcessedPeerIds { get; } = [];
}
