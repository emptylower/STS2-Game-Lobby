using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

namespace Sts2LanConnect.Scripts;

internal static partial class LanConnectTailMessagePatches
{
    private static ILanConnectTailMessageRuntime? _runtime;

    internal static void ConfigureRuntime(ILanConnectTailMessageRuntime runtime) =>
        Volatile.Write(ref _runtime, runtime ?? throw new ArgumentNullException(nameof(runtime)));

    internal static void ResetRuntime() => Volatile.Write(ref _runtime, null);

    internal static void Apply(Harmony harmony)
        => ApplyResolvedPlan(harmony, ResolvePatchPlan(typeof(PacketWriter).Assembly));

    // Tests resolve the plan explicitly via ResolvePatchPlan(...) and pass it in, so there is
    // no boolean whose meaning drifts as the production default changes.
    internal static void ApplyPlanForTesting(Harmony harmony, LanConnectTailPatchPlan plan)
        => ApplyResolvedPlan(harmony, plan);

    internal static void ApplyPlanWithInjectedPatcherForTesting(
        Harmony harmony,
        LanConnectTailPatchPlan plan,
        Action<Harmony, LanConnectTailPatchStep> patcher)
        => ApplyResolvedPlan(harmony, plan, patcher, emitProductLog: false);

    // Real patching against the real plan, without product logging: xUnit hosts cannot
    // enter Godot's GD-based logging.
    internal static void ApplyPlanQuietlyForTesting(Harmony harmony, LanConnectTailPatchPlan plan)
        => ApplyResolvedPlan(harmony, plan, patcher: null, emitProductLog: false);

    private static void ApplyResolvedPlan(
        Harmony harmony,
        LanConnectTailPatchPlan plan,
        Action<Harmony, LanConnectTailPatchStep>? patcher = null,
        bool emitProductLog = true)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(plan);
        int applied = 0;
        if (emitProductLog)
        {
            Log.Info(
                $"sts2_lan_connect patch_diag: event=plan_begin profile={plan.Profile} " +
                $"total={plan.Steps.Count} generic_target_count={plan.GenericTargetCount}");
        }
        foreach (LanConnectTailPatchStep step in plan.Steps)
        {
            int ordinal = applied + 1;
            Stopwatch stopwatch = Stopwatch.StartNew();
            LanConnectPatchDiagnosticDescriptor diagnosticDescriptor =
                CreateDiagnosticDescriptor(harmony, plan, step, ordinal);
            LanConnectStartupDiagnostics? diagnostics = LanConnectStartupDiagnostics.Current;
            long diagnosticStarted = diagnostics?.RecordPatchBegin(diagnosticDescriptor)
                                     ?? Stopwatch.GetTimestamp();
            if (emitProductLog)
            {
                Log.Info(
                    $"sts2_lan_connect patch_diag: event=patch_begin profile={plan.Profile} " +
                    $"patch_id={step.Id} ordinal={ordinal}/{plan.Steps.Count} category={step.Category} " +
                    $"target={FormatMethod(step.Target)}");
            }
            try
            {
                if (patcher == null)
                {
                    harmony.Patch(
                        step.Target,
                        prefix: CreateHarmonyMethod(step.Prefix, step.PrefixPriority),
                        postfix: CreateHarmonyMethod(step.Postfix, step.PostfixPriority),
                        finalizer: CreateHarmonyMethod(step.Finalizer, step.FinalizerPriority));
                }
                else
                {
                    patcher(harmony, step);
                }
                applied++;
                diagnostics?.RecordPatchSuccess(diagnosticDescriptor, diagnosticStarted);
                if (emitProductLog)
                {
                    Log.Info(
                        $"sts2_lan_connect patch_diag: event=patch_success profile={plan.Profile} " +
                        $"patch_id={step.Id} ordinal={ordinal}/{plan.Steps.Count} elapsed_ms={stopwatch.ElapsedMilliseconds}");
                }
            }
            catch (Exception exception)
            {
                diagnostics?.RecordPatchFailure(diagnosticDescriptor, diagnosticStarted, exception);
                if (emitProductLog)
                {
                    string externalOwners = DescribeExternalOwnersBestEffort(step.Target);
                    Log.Error(
                        $"sts2_lan_connect patch_diag: event=patch_failure profile={plan.Profile} " +
                        $"patch_id={step.Id} ordinal={ordinal}/{plan.Steps.Count} elapsed_ms={stopwatch.ElapsedMilliseconds} " +
                        $"exception={exception.GetType().FullName} hresult={exception.HResult} " +
                        $"external_owners={externalOwners}");
                }
                throw;
            }
        }

        if (emitProductLog)
        {
            Log.Info(
                $"sts2_lan_connect patch_diag: event=plan_success profile={plan.Profile} " +
                $"applied={applied}/{plan.Steps.Count} generic_target_count={plan.GenericTargetCount}");
        }
    }

    private static string DescribeExternalOwnersBestEffort(MethodInfo target)
    {
        try
        {
            string[] owners = LanConnectProtocolPatchDispatcher.GetExternalPatchOwners(target);
            return owners.Length > 0 ? string.Join(",", owners) : "none";
        }
        catch
        {
            return "unknown";
        }
    }

    private static LanConnectPatchDiagnosticDescriptor CreateDiagnosticDescriptor(
        Harmony harmony,
        LanConnectTailPatchPlan plan,
        LanConnectTailPatchStep step,
        int ordinal)
    {
        (MethodInfo Hook, int Priority)[] hooks = GetHooksWithPriorities(step).ToArray();
        if (hooks.Length == 0)
        {
            throw new InvalidDataException($"Tail patch {step.Id} has no Harmony hook.");
        }

        return new LanConnectPatchDiagnosticDescriptor(
            step.Id,
            ordinal,
            plan.Steps.Count,
            step.Category,
            step.MessageType?.FullName,
            step.Target,
            hooks[0].Hook,
            harmony.Id,
            hooks[0].Priority)
        {
            PlanProfile = plan.Profile,
            AdditionalHooks = hooks.Skip(1).Select(static item => item.Hook).ToArray(),
            AdditionalHookPriorities = hooks.Skip(1).Select(static item => item.Priority).ToArray()
        };
    }

    private static IEnumerable<(MethodInfo Hook, int Priority)> GetHooksWithPriorities(
        LanConnectTailPatchStep step)
    {
        if (step.Prefix != null)
        {
            yield return (step.Prefix, step.PrefixPriority ?? Priority.Normal);
        }

        if (step.Postfix != null)
        {
            yield return (step.Postfix, step.PostfixPriority ?? Priority.Normal);
        }

        if (step.Finalizer != null)
        {
            yield return (step.Finalizer, step.FinalizerPriority ?? Priority.Normal);
        }
    }

    private static HarmonyMethod? CreateHarmonyMethod(MethodInfo? method, int? priority)
    {
        if (method == null)
        {
            return null;
        }

        HarmonyMethod harmonyMethod = new(method);
        if (priority.HasValue)
        {
            harmonyMethod.priority = priority.Value;
        }

        return harmonyMethod;
    }

    internal static string FormatMethod(MethodInfo method)
    {
        string genericArguments = method.IsGenericMethod
            ? $"<{string.Join(",", method.GetGenericArguments().Select(static type => type.FullName ?? type.Name))}>"
            : string.Empty;
        string parameters = string.Join(
            ",",
            method.GetParameters().Select(static parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name));
        return $"{method.DeclaringType?.FullName}.{method.Name}{genericArguments}({parameters})";
    }

    internal static byte[] EncodePeerOfferMessage(
        LanConnectSidecarMessageKind messageKind,
        LanConnectProtocolOffer offer) =>
        LanConnectTailMessageProtocol.EncodePeerOffer(messageKind, offer);

    internal static byte[] EncodeSessionMessage(
        LanConnectSidecarMessageKind messageKind,
        LanConnectProtocolSelection selection,
        LanConnectRosterSnapshot? roster = null,
        LanConnectProtocolFailure? rejection = null) =>
        LanConnectTailMessageProtocol.EncodeSession(messageKind, selection, roster, rejection);

    internal static LanConnectTailMessagePayload DecodeAndValidate(
        LanConnectSidecarMessageKind messageKind,
        ReadOnlySpan<byte> container,
        LanConnectProtocolSelection? frozenSelection = null,
        ulong? transportSenderPeerId = null,
        ulong? currentHostPeerId = null) =>
        LanConnectTailMessageProtocol.DecodeAndValidate(
            messageKind,
            container,
            frozenSelection,
            transportSenderPeerId,
            currentHostPeerId);

    private static InvalidDataException Invalid(string message) => new(message);

    // ---- 接收上下文（OnPacketReceived prefix：捕获传输层 sender/mode/channel） ----

    [ThreadStatic]
    private static Stack<LanConnectTransportReceiveContext>? _transportReceiveContexts;

    private static void ReceivePrefix(
        INetGameService __instance,
        ulong senderId,
        NetTransferMode mode,
        int channel)
    {
        _ = __instance;
        (_transportReceiveContexts ??= new Stack<LanConnectTransportReceiveContext>())
            .Push(new LanConnectTransportReceiveContext(senderId, mode, channel));
    }

    private static Exception? ReceiveFinalizer(Exception? __exception)
    {
        if (_transportReceiveContexts is { Count: > 0 } contexts)
        {
            contexts.Pop();
        }

        return __exception;
    }

    internal static IDisposable PushTransportReceiveContextForTesting(
        ulong senderPeerId,
        int channel = 0,
        NetTransferMode mode = NetTransferMode.None)
    {
        (_transportReceiveContexts ??= new Stack<LanConnectTransportReceiveContext>())
            .Push(new LanConnectTransportReceiveContext(senderPeerId, mode, channel));
        return new TransportReceiveContextScope();
    }

    internal static bool TryPeekTransportReceiveContext(
        out LanConnectTransportReceiveContext context)
    {
        if (_transportReceiveContexts is { Count: > 0 } contexts)
        {
            context = contexts.Peek();
            return true;
        }

        context = null!;
        return false;
    }

    private sealed class TransportReceiveContextScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0
                && _transportReceiveContexts is { Count: > 0 } contexts)
            {
                contexts.Pop();
            }
        }
    }

    // ---- 第一级：10 个具体消息 Serialize prefix（容器生产 seam，不改写原版字节） ----

    // ReSharper disable UnusedMember.Local -- invoked by Harmony.
    private static void AndroidSerializeInitialGameInfoPrefix(
        ref InitialGameInfoMessage __instance,
        PacketWriter __0,
        out LanConnectNativePreparedMessage? __state) =>
        __instance = PrepareConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLobbyJoinRequestPrefix(
        ref ClientLobbyJoinRequestMessage __instance,
        PacketWriter __0,
        out LanConnectNativePreparedMessage? __state) =>
        __instance = PrepareConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLobbyJoinResponsePrefix(
        ref ClientLobbyJoinResponseMessage __instance,
        PacketWriter __0,
        out LanConnectNativePreparedMessage? __state) =>
        __instance = PrepareConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLoadJoinRequestPrefix(
        ref ClientLoadJoinRequestMessage __instance,
        PacketWriter __0,
        out LanConnectNativePreparedMessage? __state) =>
        __instance = PrepareConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLoadJoinResponsePrefix(
        ref ClientLoadJoinResponseMessage __instance,
        PacketWriter __0,
        out LanConnectNativePreparedMessage? __state) =>
        __instance = PrepareConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeRejoinRequestPrefix(
        ref ClientRejoinRequestMessage __instance,
        PacketWriter __0,
        out LanConnectNativePreparedMessage? __state) =>
        __instance = PrepareConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeRejoinResponsePrefix(
        ref ClientRejoinResponseMessage __instance,
        PacketWriter __0,
        out LanConnectNativePreparedMessage? __state) =>
        __instance = PrepareConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializePlayerJoinedPrefix(
        ref PlayerJoinedMessage __instance,
        PacketWriter __0,
        out LanConnectNativePreparedMessage? __state) =>
        __instance = PrepareConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLobbyBeginRunPrefix(
        ref LobbyBeginRunMessage __instance,
        PacketWriter __0,
        out LanConnectNativePreparedMessage? __state) =>
        __instance = PrepareConcreteMessage(__0, __instance, out __state);

    private static T PrepareConcreteMessage<T>(
        PacketWriter writer,
        T message,
        out LanConnectNativePreparedMessage? state)
        where T : struct, INetMessage
    {
        state = null;
        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        if (!snapshot.IsActive || snapshot.Selection?.Profile != LanConnectProtocolProfile.TailV1)
        {
            return message;
        }

        ILanConnectTailMessageRuntime runtime = Volatile.Read(ref _runtime)
            ?? throw new InvalidOperationException("Tail protocol message runtime is not configured.");
        LanConnectSidecarMessageKind kind = GetMessageKind(typeof(T));
        if (kind == LanConnectSidecarMessageKind.InitialGameInfo
            && LanConnectTailMessageRuntime.HasPendingOutgoingRejectionForCurrentThread)
        {
            kind = LanConnectSidecarMessageKind.ConnectionFailed;
        }

        if (!runtime.TryPrepareConcreteOutgoing(writer, kind, message, out state))
        {
            return message;
        }

        if (state?.Prepared.Message is not T projected)
        {
            throw new InvalidOperationException(
                $"Tail runtime returned {state?.Prepared.Message.GetType().FullName ?? "null"} for {typeof(T).FullName}.");
        }

        return projected;
    }

    private static void AndroidConcreteSerializePostfix(LanConnectNativePreparedMessage? __state)
    {
        if (__state == null)
        {
            return;
        }

        ILanConnectTailMessageRuntime runtime = Volatile.Read(ref _runtime)
            ?? throw new InvalidOperationException("Tail protocol message runtime is not configured.");
        runtime.CompleteConcreteOutgoing(__state);
    }

    private static void AndroidWriterResetPrefix(PacketWriter __instance)
    {
        if (Volatile.Read(ref _runtime) is ILanConnectTailMessageRuntime runtime)
        {
            runtime.ClearPendingOutgoing(__instance);
        }
    }

    // ---- 第二/第三级：transport prefix/postfix/finalizer（非泛型、单点） ----

    private static void AndroidHostTransportPrefix(
        ENetHost __instance,
        ulong peerId,
        byte[] bytes,
        int length,
        out LanConnectNativeSendContext? __state)
    {
        __state = PrepareNativeTransport(
            __instance,
            isHostTransport: true,
            peerId,
            bytes,
            length);
    }

    private static void AndroidClientTransportPrefix(
        ENetClient __instance,
        byte[] bytes,
        int length,
        out LanConnectNativeSendContext? __state)
    {
        __state = PrepareNativeTransport(
            __instance,
            isHostTransport: false,
            recipientPeerId: 0,
            bytes,
            length);
    }

    private static LanConnectNativeSendContext? PrepareNativeTransport(
        object transport,
        bool isHostTransport,
        ulong recipientPeerId,
        byte[] bytes,
        int length)
    {
        if (Volatile.Read(ref _runtime) is not ILanConnectTailMessageRuntime runtime)
        {
            return null;
        }

        return runtime.BeginNativeTransport(
            transport,
            isHostTransport,
            recipientPeerId,
            bytes,
            length);
    }

    private static void AndroidHostTransportPostfix(
        ENetHost __instance,
        ulong peerId,
        LanConnectNativeSendContext? __state)
    {
        // 结构性递归免疫：native 发送出口触发的重入直接跳过。
        if (LanConnectNativeBusSender.ReentryForCurrentThread)
        {
            return;
        }

        bool peerReachable = __instance.ConnectedPeerIds.Contains(peerId);
        CompleteNativeTransport(__state, peerReachable);
    }

    private static void AndroidClientTransportPostfix(
        ENetClient __instance,
        LanConnectNativeSendContext? __state)
    {
        if (LanConnectNativeBusSender.ReentryForCurrentThread)
        {
            return;
        }

        CompleteNativeTransport(__state, __instance.IsConnected);
    }

    private static void CompleteNativeTransport(LanConnectNativeSendContext? state, bool vanillaPeerReachable)
    {
        if (Volatile.Read(ref _runtime) is not ILanConnectTailMessageRuntime runtime)
        {
            return;
        }

        runtime.CompleteNativeTransport(state, vanillaPeerReachable);
    }

    private static Exception? AndroidHostTransportFinalizer(
        Exception? __exception,
        LanConnectNativeSendContext? __state) =>
        CompleteNativeTransportFailure(__exception, __state);

    private static Exception? AndroidClientTransportFinalizer(
        Exception? __exception,
        LanConnectNativeSendContext? __state) =>
        CompleteNativeTransportFailure(__exception, __state);

    private static Exception? CompleteNativeTransportFailure(
        Exception? exception,
        LanConnectNativeSendContext? state)
    {
        if (exception == null || state == null)
        {
            return exception;
        }

        try
        {
            if (Volatile.Read(ref _runtime) is ILanConnectTailMessageRuntime runtime)
            {
                runtime.HandleNativeTransportFailure(state, exception);
            }
        }
        catch (Exception abortException)
        {
            Log.Error(
                "sts2_lan_connect tail: failed to terminate Native Tail binding after vanilla transport failure: " +
                $"{abortException.GetType().Name}: {abortException.Message}");
        }

        return exception;
    }

    // ---- 配对屏障（NetMessageBus.SendMessageToAllHandlers prefix） ----

    private static bool DispatchBarrierPrefix(
        NetMessageBus __instance,
        INetMessage message,
        ulong senderId)
    {
        if (Volatile.Read(ref _runtime) is not ILanConnectTailMessageRuntime runtime)
        {
            return true;
        }

        try
        {
            return runtime.TryEnterNativeDispatch(__instance, message, senderId);
        }
        catch (Exception exception)
        {
            Log.Error(
                $"sts2_lan_connect tail: native dispatch barrier crashed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    // ---- TryDeserializeMessage 拆分（prefix：<9 字节已知 ID 前置拦截；postfix：未知 ID 捕获） ----

    private static bool TryDeserializePrefix(NetMessageBus __instance, byte[] __0)
    {
        // 原版会在读取 senderId 时越界抛出：先拦截并转 lan_native_frame_invalid。
        if (__0.Length >= LanConnectNativeBusMessage.VanillaWireHeaderBytes || __0.Length == 0)
        {
            return true;
        }

        if (__0[0] != (byte)LanConnectNativeBusSender.ResolveTypeId())
        {
            return true;
        }

        if (Volatile.Read(ref _runtime) is ILanConnectTailMessageRuntime runtime
            && TryPeekTransportReceiveContext(out LanConnectTransportReceiveContext context))
        {
            try
            {
                runtime.HandleIncomingFailure(
                    __instance,
                    context.SenderPeerId,
                    LanConnectProtocolFailureMapper.FromLocalException(
                        "lan_native_frame_invalid",
                        $"Native bus packet of {__0.Length} bytes is shorter than the vanilla wire header."),
                    RequireActiveSelection());
            }
            catch
            {
                // 拒绝通道失败时仍须跳过原版反序列化（前缀返回 false），避免越界读取。
            }
        }

        return false;
    }

    private static void TryDeserializePostfix(
        NetMessageBus __instance,
        byte[] __0,
        bool __result)
    {
        if (__result
            || __0.Length < LanConnectNativeBusMessage.VanillaWireHeaderBytes + 3)
        {
            return;
        }

        int offset = LanConnectNativeBusMessage.VanillaWireHeaderBytes;
        if (__0[offset] != LanConnectNativeBusMessage.MagicFirst
            || __0[offset + 1] != LanConnectNativeBusMessage.MagicSecond
            || __0[offset + 2] != LanConnectNativeBusMessage.WireVersion)
        {
            return;
        }

        if (!LooksLikeCompleteOuterFrame(__0))
        {
            // 仅前缀相似：维持原版"警告一次后丢弃"，不误伤断开第三方消息。
            return;
        }

        if (Volatile.Read(ref _runtime) is ILanConnectTailMessageRuntime runtime
            && TryPeekTransportReceiveContext(out LanConnectTransportReceiveContext context))
        {
            try
            {
                runtime.HandleIncomingFailure(
                    __instance,
                    context.SenderPeerId,
                    LanConnectProtocolFailureMapper.FromLocalException(
                        "lan_type_id_mismatch",
                        $"Packet carries a native bus outer frame under unknown type id {__0[0]}."),
                    RequireActiveSelection());
            }
            catch
            {
                // 结构化拒绝通道也可能失败；原版"警告后丢弃"语义保持不变。
            }
        }
    }

    private static Exception? TryDeserializeFinalizer(
        NetMessageBus __instance,
        Exception? __exception)
    {
        if (__exception != null
            && Volatile.Read(ref _runtime) is ILanConnectTailMessageRuntime runtime
            && TryPeekTransportReceiveContext(out LanConnectTransportReceiveContext context))
        {
            try
            {
                runtime.HandleIncomingFailure(
                    __instance,
                    context.SenderPeerId,
                    __exception,
                    RequireActiveSelection());
            }
            catch
            {
                // 异常清理失败不得替代原始异常。
            }
        }

        return __exception;
    }

    /// <summary>
    /// offset-9 完整性检查：外层帧 magic/ver/frameLen 边界/localTypeId 处于 byte 范围内
    /// 全部合法时才视为"我方帧落在未知 ID 上"（v11 M-MAGIC：第三方前缀相似不误伤）。
    /// </summary>
    private static bool LooksLikeCompleteOuterFrame(byte[] packet)
    {
        const int outerHeader = LanConnectNativeBusMessage.OuterHeaderBytes;
        int offset = LanConnectNativeBusMessage.VanillaWireHeaderBytes;
        if (packet.Length < offset + outerHeader)
        {
            return false;
        }

        if (packet[offset] != LanConnectNativeBusMessage.MagicFirst
            || packet[offset + 1] != LanConnectNativeBusMessage.MagicSecond
            || packet[offset + 2] != LanConnectNativeBusMessage.WireVersion)
        {
            return false;
        }

        uint frameLength = BinaryPrimitivesReadUInt32BE(packet, offset + 3 + 4);
        uint localTypeId = BinaryPrimitivesReadUInt32BE(packet, offset + 3);
        if (localTypeId > 255)
        {
            return false;
        }

        long declaredEnd = offset + outerHeader + (long)frameLength;
        return declaredEnd <= packet.Length && packet.Length <= LanConnectNativeBusMessage.MaxPacketBytes;
    }

    private static uint BinaryPrimitivesReadUInt32BE(byte[] source, int offset) =>
        (uint)(source[offset] << 24
               | source[offset + 1] << 16
               | source[offset + 2] << 8
               | source[offset + 3]);

    private static LanConnectProtocolSelection RequireActiveSelection()
        => LanConnectSessionProtocolState.Shared.Current.Selection
           ?? throw new InvalidOperationException("No active Tail protocol selection.");

    // ---- 工具 ----

    internal static bool TryGetMessageKind(Type type, out LanConnectSidecarMessageKind kind)
        => LanConnectTailMessageTypeMatrix.TryGetKind(type.Name, out kind);

    private static LanConnectSidecarMessageKind GetMessageKind(Type type) =>
        TryGetMessageKind(type, out LanConnectSidecarMessageKind kind)
            ? kind
            : throw Invalid($"Message type {type.FullName} is not part of LAN protocol v1.");
    // ReSharper restore UnusedMember.Local
}
