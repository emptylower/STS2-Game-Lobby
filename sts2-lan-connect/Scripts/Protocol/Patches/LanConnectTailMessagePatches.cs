using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

namespace Sts2LanConnect.Scripts;

internal interface ILanConnectTailMessageRuntime
{
    LanConnectPreparedTailMessage PrepareOutgoing(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        object message,
        LanConnectProtocolSelection selection);

    void SubmitSidecarBeforeVanilla(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        object message,
        byte[] container,
        LanConnectProtocolSelection selection);

    void ValidateStandaloneIncoming(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong transportSenderPeerId,
        INetMessage message,
        byte[] container,
        LanConnectProtocolSelection selection);

    void HandleIncomingFailure(
        NetMessageBus messageBus,
        ulong transportSenderPeerId,
        Exception exception,
        LanConnectProtocolSelection selection);

    bool TryPairSidecarIncoming(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        INetMessage message,
        LanConnectProtocolSelection selection);
}

internal interface ILanConnectAndroidTailMessageRuntime : ILanConnectTailMessageRuntime
{
    bool TryPrepareConcreteOutgoing(
        PacketWriter writer,
        LanConnectSidecarMessageKind messageKind,
        object message,
        out LanConnectAndroidPreparedMessage? prepared);

    void CompleteConcreteOutgoing(LanConnectAndroidPreparedMessage prepared);

    void ClearPendingOutgoing(PacketWriter writer);

    LanConnectAndroidTransportState? SubmitPendingSidecarBeforeVanilla(
        object transport,
        bool isHostTransport,
        ulong recipientPeerId,
        byte[] buffer,
        int length);

    void HandleVanillaTransportFailure(
        LanConnectAndroidTransportState state,
        Exception exception);
}

internal sealed record LanConnectPreparedTailMessage(object Message, byte[] Container);

internal sealed record LanConnectAndroidPreparedMessage(
    NetMessageBus MessageBus,
    PacketWriter Writer,
    LanConnectSidecarMessageKind MessageKind,
    ulong SenderPeerId,
    LanConnectProtocolSelection Selection,
    LanConnectPreparedTailMessage Prepared);

internal sealed record LanConnectAndroidTransportState(
    NetMessageBus MessageBus,
    long Sequence,
    ulong RecipientPeerId);

internal static partial class LanConnectTailMessagePatches
{
    private static readonly FieldInfo? NetMessageBusWriter =
        AccessTools.Field(typeof(NetMessageBus), "_writer");
    private static readonly FieldInfo? NetMessageBusReader =
        AccessTools.Field(typeof(NetMessageBus), "_reader");
    private static ILanConnectTailMessageRuntime? _runtime;

    internal static void ConfigureRuntime(ILanConnectTailMessageRuntime runtime) =>
        Volatile.Write(ref _runtime, runtime ?? throw new ArgumentNullException(nameof(runtime)));

    internal static void ResetRuntime() => Volatile.Write(ref _runtime, null);

    internal static void Apply(Harmony harmony)
        => ApplyResolvedPlan(harmony, ResolvePatchPlan(typeof(PacketWriter).Assembly, OperatingSystem.IsAndroid()));

    internal static void ApplyForTesting(Harmony harmony, bool isAndroid)
        => ApplyResolvedPlan(harmony, ResolvePatchPlan(typeof(PacketWriter).Assembly, isAndroid));

    internal static void ApplyPlanWithInjectedPatcherForTesting(
        Harmony harmony,
        LanConnectTailPatchPlan plan,
        Action<Harmony, LanConnectTailPatchStep> patcher)
        => ApplyResolvedPlan(harmony, plan, patcher, emitProductLog: false);

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
                    Log.Error(
                        $"sts2_lan_connect patch_diag: event=patch_failure profile={plan.Profile} " +
                        $"patch_id={step.Id} ordinal={ordinal}/{plan.Steps.Count} elapsed_ms={stopwatch.ElapsedMilliseconds} " +
                        $"exception={exception.GetType().FullName} hresult={exception.HResult}");
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

    private static MethodInfo ResolveSerializePrefix(Type messageType)
    {
        string methodName = messageType == typeof(InitialGameInfoMessage)
            ? nameof(SerializeInitialGameInfoPrefix)
            : messageType == typeof(ClientLobbyJoinRequestMessage)
                ? nameof(SerializeLobbyJoinRequestPrefix)
                : messageType == typeof(ClientLobbyJoinResponseMessage)
                    ? nameof(SerializeLobbyJoinResponsePrefix)
                    : messageType == typeof(ClientLoadJoinRequestMessage)
                        ? nameof(SerializeLoadJoinRequestPrefix)
                        : messageType == typeof(ClientLoadJoinResponseMessage)
                            ? nameof(SerializeLoadJoinResponsePrefix)
                            : messageType == typeof(ClientRejoinRequestMessage)
                                ? nameof(SerializeRejoinRequestPrefix)
                                : messageType == typeof(ClientRejoinResponseMessage)
                                    ? nameof(SerializeRejoinResponsePrefix)
                                    : messageType == typeof(PlayerJoinedMessage)
                                        ? nameof(SerializePlayerJoinedPrefix)
                                        : messageType == typeof(LobbyBeginRunMessage)
                                            ? nameof(SerializeLobbyBeginRunPrefix)
                                            : throw new InvalidDataException(
                                                $"Message type {messageType.FullName} has no concrete tail serializer prefix.");
        return AccessTools.Method(typeof(LanConnectTailMessagePatches), methodName)
            ?? throw new MissingMethodException(typeof(LanConnectTailMessagePatches).FullName, methodName);
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

    [ThreadStatic]
    private static Stack<ulong>? _transportSenders;

    // ReSharper disable UnusedMember.Local -- invoked by Harmony.
    private static void SerializeInitialGameInfoPrefix(
        NetMessageBus __instance,
        ulong senderId,
        ref InitialGameInfoMessage message,
        out LanConnectPreparedTailMessage? __state)
    {
        message = (InitialGameInfoMessage)PrepareSerializeMessage(
            __instance,
            senderId,
            message,
            typeof(InitialGameInfoMessage),
            out __state);
    }

    private static void SerializeLobbyJoinRequestPrefix(
        NetMessageBus __instance,
        ulong senderId,
        ref ClientLobbyJoinRequestMessage message,
        out LanConnectPreparedTailMessage? __state)
    {
        message = (ClientLobbyJoinRequestMessage)PrepareSerializeMessage(
            __instance,
            senderId,
            message,
            typeof(ClientLobbyJoinRequestMessage),
            out __state);
    }

    private static void SerializeLobbyJoinResponsePrefix(
        NetMessageBus __instance,
        ulong senderId,
        ref ClientLobbyJoinResponseMessage message,
        out LanConnectPreparedTailMessage? __state)
    {
        message = (ClientLobbyJoinResponseMessage)PrepareSerializeMessage(
            __instance,
            senderId,
            message,
            typeof(ClientLobbyJoinResponseMessage),
            out __state);
    }

    private static void SerializeLoadJoinRequestPrefix(
        NetMessageBus __instance,
        ulong senderId,
        ref ClientLoadJoinRequestMessage message,
        out LanConnectPreparedTailMessage? __state)
    {
        message = (ClientLoadJoinRequestMessage)PrepareSerializeMessage(
            __instance,
            senderId,
            message,
            typeof(ClientLoadJoinRequestMessage),
            out __state);
    }

    private static void SerializeLoadJoinResponsePrefix(
        NetMessageBus __instance,
        ulong senderId,
        ref ClientLoadJoinResponseMessage message,
        out LanConnectPreparedTailMessage? __state)
    {
        message = (ClientLoadJoinResponseMessage)PrepareSerializeMessage(
            __instance,
            senderId,
            message,
            typeof(ClientLoadJoinResponseMessage),
            out __state);
    }

    private static void SerializeRejoinRequestPrefix(
        NetMessageBus __instance,
        ulong senderId,
        ref ClientRejoinRequestMessage message,
        out LanConnectPreparedTailMessage? __state)
    {
        message = (ClientRejoinRequestMessage)PrepareSerializeMessage(
            __instance,
            senderId,
            message,
            typeof(ClientRejoinRequestMessage),
            out __state);
    }

    private static void SerializeRejoinResponsePrefix(
        NetMessageBus __instance,
        ulong senderId,
        ref ClientRejoinResponseMessage message,
        out LanConnectPreparedTailMessage? __state)
    {
        message = (ClientRejoinResponseMessage)PrepareSerializeMessage(
            __instance,
            senderId,
            message,
            typeof(ClientRejoinResponseMessage),
            out __state);
    }

    private static void SerializePlayerJoinedPrefix(
        NetMessageBus __instance,
        ulong senderId,
        ref PlayerJoinedMessage message,
        out LanConnectPreparedTailMessage? __state)
    {
        message = (PlayerJoinedMessage)PrepareSerializeMessage(
            __instance,
            senderId,
            message,
            typeof(PlayerJoinedMessage),
            out __state);
    }

    private static void SerializeLobbyBeginRunPrefix(
        NetMessageBus __instance,
        ulong senderId,
        ref LobbyBeginRunMessage message,
        out LanConnectPreparedTailMessage? __state)
    {
        message = (LobbyBeginRunMessage)PrepareSerializeMessage(
            __instance,
            senderId,
            message,
            typeof(LobbyBeginRunMessage),
            out __state);
    }

    private static void AndroidSerializeInitialGameInfoPrefix(
        ref InitialGameInfoMessage __instance,
        PacketWriter __0,
        out LanConnectAndroidPreparedMessage? __state) =>
        __instance = PrepareAndroidConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLobbyJoinRequestPrefix(
        ref ClientLobbyJoinRequestMessage __instance,
        PacketWriter __0,
        out LanConnectAndroidPreparedMessage? __state) =>
        __instance = PrepareAndroidConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLobbyJoinResponsePrefix(
        ref ClientLobbyJoinResponseMessage __instance,
        PacketWriter __0,
        out LanConnectAndroidPreparedMessage? __state) =>
        __instance = PrepareAndroidConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLoadJoinRequestPrefix(
        ref ClientLoadJoinRequestMessage __instance,
        PacketWriter __0,
        out LanConnectAndroidPreparedMessage? __state) =>
        __instance = PrepareAndroidConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLoadJoinResponsePrefix(
        ref ClientLoadJoinResponseMessage __instance,
        PacketWriter __0,
        out LanConnectAndroidPreparedMessage? __state) =>
        __instance = PrepareAndroidConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeRejoinRequestPrefix(
        ref ClientRejoinRequestMessage __instance,
        PacketWriter __0,
        out LanConnectAndroidPreparedMessage? __state) =>
        __instance = PrepareAndroidConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeRejoinResponsePrefix(
        ref ClientRejoinResponseMessage __instance,
        PacketWriter __0,
        out LanConnectAndroidPreparedMessage? __state) =>
        __instance = PrepareAndroidConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializePlayerJoinedPrefix(
        ref PlayerJoinedMessage __instance,
        PacketWriter __0,
        out LanConnectAndroidPreparedMessage? __state) =>
        __instance = PrepareAndroidConcreteMessage(__0, __instance, out __state);

    private static void AndroidSerializeLobbyBeginRunPrefix(
        ref LobbyBeginRunMessage __instance,
        PacketWriter __0,
        out LanConnectAndroidPreparedMessage? __state) =>
        __instance = PrepareAndroidConcreteMessage(__0, __instance, out __state);

    private static T PrepareAndroidConcreteMessage<T>(
        PacketWriter writer,
        T message,
        out LanConnectAndroidPreparedMessage? state)
        where T : struct, INetMessage
    {
        state = null;
        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        if (!snapshot.IsActive || snapshot.Selection?.Profile != LanConnectProtocolProfile.TailV1)
        {
            return message;
        }

        ILanConnectAndroidTailMessageRuntime runtime = Volatile.Read(ref _runtime)
            as ILanConnectAndroidTailMessageRuntime
            ?? throw new InvalidOperationException("Android Tail protocol message runtime is not configured.");
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

    private static void AndroidConcreteSerializePostfix(LanConnectAndroidPreparedMessage? __state)
    {
        if (__state == null)
        {
            return;
        }

        ILanConnectAndroidTailMessageRuntime runtime = Volatile.Read(ref _runtime)
            as ILanConnectAndroidTailMessageRuntime
            ?? throw new InvalidOperationException("Android Tail protocol message runtime is not configured.");
        runtime.CompleteConcreteOutgoing(__state);
    }

    private static void AndroidWriterResetPrefix(PacketWriter __instance)
    {
        if (Volatile.Read(ref _runtime) is ILanConnectAndroidTailMessageRuntime runtime)
        {
            runtime.ClearPendingOutgoing(__instance);
        }
    }

    private static void AndroidHostTransportPrefix(
        ENetHost __instance,
        ulong peerId,
        byte[] bytes,
        int length,
        out LanConnectAndroidTransportState? __state)
    {
        __state = PrepareAndroidTransport(
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
        out LanConnectAndroidTransportState? __state)
    {
        __state = PrepareAndroidTransport(
            __instance,
            isHostTransport: false,
            recipientPeerId: 0,
            bytes,
            length);
    }

    private static LanConnectAndroidTransportState? PrepareAndroidTransport(
        object transport,
        bool isHostTransport,
        ulong recipientPeerId,
        byte[] bytes,
        int length)
    {
        if (Volatile.Read(ref _runtime) is not ILanConnectAndroidTailMessageRuntime runtime)
        {
            return null;
        }

        return runtime.SubmitPendingSidecarBeforeVanilla(
            transport,
            isHostTransport,
            recipientPeerId,
            bytes,
            length);
    }

    private static Exception? AndroidHostTransportFinalizer(
        Exception? __exception,
        LanConnectAndroidTransportState? __state) =>
        CompleteAndroidTransport(__exception, __state);

    private static Exception? AndroidClientTransportFinalizer(
        Exception? __exception,
        LanConnectAndroidTransportState? __state) =>
        CompleteAndroidTransport(__exception, __state);

    private static Exception? CompleteAndroidTransport(
        Exception? exception,
        LanConnectAndroidTransportState? state)
    {
        if (exception == null || state == null)
        {
            return exception;
        }

        try
        {
            if (Volatile.Read(ref _runtime) is ILanConnectAndroidTailMessageRuntime runtime)
            {
                runtime.HandleVanillaTransportFailure(state, exception);
            }
        }
        catch (Exception abortException)
        {
            Log.Error(
                "sts2_lan_connect tail: failed to terminate Android Tail binding after vanilla transport failure: " +
                $"{abortException.GetType().Name}: {abortException.Message}");
        }

        return exception;
    }

    private static object PrepareSerializeMessage(
        NetMessageBus messageBus,
        ulong senderId,
        object message,
        Type messageType,
        out LanConnectPreparedTailMessage? state)
    {
        state = null;
        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        LanConnectProtocolSelection? selection = snapshot.Selection;
        if (selection?.Profile != LanConnectProtocolProfile.TailV1 || !snapshot.IsActive)
        {
            return message;
        }

        ILanConnectTailMessageRuntime runtime = Volatile.Read(ref _runtime)
            ?? throw new InvalidOperationException("Tail protocol message runtime is not configured.");
        LanConnectSidecarMessageKind kind = GetMessageKind(messageType);
        if (kind == LanConnectSidecarMessageKind.InitialGameInfo
            && LanConnectTailMessageRuntime.HasPendingOutgoingRejectionForCurrentThread)
        {
            kind = LanConnectSidecarMessageKind.ConnectionFailed;
        }
        LanConnectPreparedTailMessage prepared = runtime.PrepareOutgoing(
            messageBus,
            kind,
            senderId,
            message,
            selection);
        if (!messageType.IsInstanceOfType(prepared.Message))
        {
            throw new InvalidOperationException(
                $"Tail runtime returned {prepared.Message.GetType().FullName} for {messageType.FullName}.");
        }

        object projected = prepared.Message;
        state = prepared;
        if (selection.Carrier == LanConnectProtocolCarrier.RitsuLibSidecarV1)
        {
            runtime.SubmitSidecarBeforeVanilla(messageBus, kind, senderId, projected, prepared.Container, selection);
        }

        return projected;
    }

    private static void SerializePostfix(
        NetMessageBus __instance,
        ulong senderId,
        object message,
        ref int length,
        ref byte[] __result,
        LanConnectPreparedTailMessage? __state)
    {
        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        LanConnectProtocolSelection? selection = snapshot.Selection;
        if (selection?.Profile != LanConnectProtocolProfile.TailV1 || !snapshot.IsActive)
        {
            return;
        }

        if (__state == null)
        {
            throw new InvalidOperationException("Tail outgoing message was not prepared before serialization.");
        }

        if (selection.Carrier == LanConnectProtocolCarrier.RitsuLibSidecarV1)
        {
            return;
        }

        PacketWriter writer = NetMessageBusWriter?.GetValue(__instance) as PacketWriter
            ?? throw new InvalidOperationException("NetMessageBus writer is unavailable.");
        LanConnectStandaloneTailPlacement placement = LanConnectStandaloneTailCarrier.Write(
            writer,
            __state.Container,
            selection);
        length = checked((placement.ContainerEndBit + 7) / 8);
        __result = writer.Buffer;
    }

    private static void DeserializePostfix(
        NetMessageBus __instance,
        ref bool __result,
        ref INetMessage? message,
        ref ulong? overrideSenderId)
    {
        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        LanConnectProtocolSelection? selection = snapshot.Selection;
        if (selection?.Profile != LanConnectProtocolProfile.TailV1 || !snapshot.IsActive)
        {
            return;
        }

        ILanConnectTailMessageRuntime? runtime = Volatile.Read(ref _runtime);
        if (runtime == null)
        {
            __result = false;
            message = null;
            overrideSenderId = null;
            return;
        }

        if (!__result || message == null || !overrideSenderId.HasValue)
        {
            if (!__result && TryPeekTransportSender(out ulong failedSenderPeerId))
            {
                try
                {
                    runtime.HandleIncomingFailure(
                        __instance,
                        failedSenderPeerId,
                        new InvalidDataException("Tail message deserialization failed before vanilla dispatch."),
                        selection);
                }
                catch
                {
                    // Rejection transport can fail too; the malformed message must still stay blocked.
                }
            }

            return;
        }

        try
        {
            if (!TryGetIncomingMessageKind(message, out LanConnectSidecarMessageKind kind))
            {
                return;
            }

            if (!TryPeekTransportSender(out ulong transportSenderPeerId))
            {
                throw new InvalidDataException(
                    "Tail message deserialization has no authenticated transport-sender context.");
            }

            if (overrideSenderId.Value != transportSenderPeerId)
            {
                throw new InvalidDataException(
                    "Embedded message sender differs from the authenticated transport sender.");
            }

            if (selection.Carrier == LanConnectProtocolCarrier.StandaloneTailV1)
            {
                PacketReader reader = NetMessageBusReader?.GetValue(__instance) as PacketReader
                    ?? throw new InvalidOperationException("NetMessageBus reader is unavailable.");
                LanConnectStandaloneTailPlacement placement = LanConnectStandaloneTailCarrier.Read(reader, selection);
                runtime.ValidateStandaloneIncoming(
                    __instance,
                    kind,
                    transportSenderPeerId,
                    message,
                    placement.ContainerBytes,
                    selection);
            }
            else if (!runtime.TryPairSidecarIncoming(
                         __instance,
                         kind,
                         transportSenderPeerId,
                         message,
                         selection))
            {
                __result = false;
                message = null;
                overrideSenderId = null;
            }
        }
        catch (Exception exception)
        {
            Log.Warn(
                $"sts2_lan_connect tail: blocked {message?.GetType().Name ?? "unknown"} " +
                $"from transport peer {overrideSenderId?.ToString() ?? "unknown"}: " +
                $"{exception.GetType().Name}: {exception.Message}");
            __result = false;
            message = null;
            overrideSenderId = null;
            if (!TryPeekTransportSender(out ulong transportSenderPeerId))
            {
                return;
            }

            try
            {
                runtime.HandleIncomingFailure(
                    __instance,
                    transportSenderPeerId,
                    exception,
                    selection);
            }
            catch
            {
                // Protocol rejection transport can fail too; the original message must still stay blocked.
            }
        }
    }

    private static void ReceivePrefix(INetGameService __instance, ulong senderId)
    {
        _ = __instance;
        (_transportSenders ??= new Stack<ulong>()).Push(senderId);
    }

    private static Exception? ReceiveFinalizer(Exception? __exception)
    {
        if (_transportSenders is { Count: > 0 } senders)
        {
            senders.Pop();
        }

        return __exception;
    }

    internal static IDisposable PushTransportSenderForTesting(ulong senderPeerId)
    {
        (_transportSenders ??= new Stack<ulong>()).Push(senderPeerId);
        return new TransportSenderScope();
    }

    private static ulong RequireTransportSender()
    {
        if (!TryPeekTransportSender(out ulong senderPeerId))
        {
            throw new InvalidOperationException(
                "Tail message deserialization has no authenticated transport-sender context.");
        }

        return senderPeerId;
    }

    private static bool TryPeekTransportSender(out ulong senderPeerId)
    {
        if (_transportSenders is { Count: > 0 } senders)
        {
            senderPeerId = senders.Peek();
            return true;
        }

        senderPeerId = 0;
        return false;
    }

    private static void HostBroadcastPrefix(
        NetHostGameService __instance,
        out IDisposable? __state)
    {
        __state = null;
        if (!IsActiveRitsuTail())
        {
            return;
        }

        ulong[] recipients = __instance.ConnectedPeers
            .Where(static peer => peer.readyForBroadcasting)
            .Select(static peer => peer.peerId)
            .ToArray();
        __state = LanConnectTailMessageRuntime.PushOutgoingSidecarRecipientsForCurrentThread(recipients);
    }

    private static void HostSendInternalPrefix(
        ulong peerId,
        out IDisposable? __state)
    {
        __state = null;
        if (!IsActiveRitsuTail())
        {
            return;
        }

        __state = LanConnectTailMessageRuntime.PushOutgoingSidecarRecipientsForCurrentThread([peerId]);
    }

    private static Exception? HostSendFinalizer(Exception? __exception, IDisposable? __state)
    {
        __state?.Dispose();
        return __exception;
    }

    private static bool IsActiveRitsuTail()
    {
        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        LanConnectProtocolSelection? selection = snapshot.Selection;
        return selection is
            {
                Profile: LanConnectProtocolProfile.TailV1,
                Carrier: LanConnectProtocolCarrier.RitsuLibSidecarV1
            } && snapshot.IsActive;
    }

    private sealed class TransportSenderScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && _transportSenders is { Count: > 0 } senders)
            {
                senders.Pop();
            }
        }
    }
    // ReSharper restore UnusedMember.Local

    internal static bool TryGetMessageKind(Type type, out LanConnectSidecarMessageKind kind)
        => LanConnectTailMessageTypeMatrix.TryGetKind(type.Name, out kind);

    private static LanConnectSidecarMessageKind GetMessageKind(Type type) =>
        TryGetMessageKind(type, out LanConnectSidecarMessageKind kind)
            ? kind
            : throw Invalid($"Message type {type.FullName} is not part of LAN protocol v1.");

    private static bool TryGetIncomingMessageKind(
        INetMessage message,
        out LanConnectSidecarMessageKind kind)
    {
        if (!TryGetMessageKind(message.GetType(), out kind))
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

}
