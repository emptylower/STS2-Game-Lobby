using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

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

internal sealed record LanConnectPreparedTailMessage(object Message, byte[] Container);

internal static class LanConnectTailMessagePatches
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
    {
        ArgumentNullException.ThrowIfNull(harmony);
        Assembly assembly = typeof(PacketWriter).Assembly;
        Type[] messageTypes = Enum.GetValues<LanConnectSidecarMessageKind>()
            .Select(kind => assembly.GetType(
                $"MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.{GetMessageTypeName(kind)}",
                throwOnError: false,
                ignoreCase: false)
                ?? throw new TypeLoadException($"Required Tail message type for {kind} was not found."))
            .ToArray();
        MethodInfo serializePostfix = AccessTools.Method(typeof(LanConnectTailMessagePatches), nameof(SerializePostfix))
            ?? throw new MissingMethodException(nameof(SerializePostfix));
        MethodInfo serializePrefixDefinition = AccessTools.Method(
            typeof(LanConnectTailMessagePatches),
            nameof(SerializePrefix))
            ?? throw new MissingMethodException(nameof(SerializePrefix));
        MethodInfo deserializePostfix = AccessTools.Method(typeof(LanConnectTailMessagePatches), nameof(DeserializePostfix))
            ?? throw new MissingMethodException(nameof(DeserializePostfix));
        MethodInfo deserialize = AccessTools.Method(
            typeof(NetMessageBus),
            nameof(NetMessageBus.TryDeserializeMessage),
            [typeof(byte[]), typeof(INetMessage).MakeByRefType(), typeof(ulong?).MakeByRefType()])
            ?? throw new MissingMethodException(typeof(NetMessageBus).FullName, nameof(NetMessageBus.TryDeserializeMessage));

        foreach (Type messageType in messageTypes)
        {
            MethodInfo serialize = LanConnectSerializationPatches.ResolveGenericSerializeMessageMethod(
                typeof(NetMessageBus),
                messageType);
            MethodInfo serializePrefix = serializePrefixDefinition.MakeGenericMethod(messageType);
            harmony.Patch(
                serialize,
                prefix: new HarmonyMethod(serializePrefix) { priority = Priority.First + 100 },
                postfix: new HarmonyMethod(serializePostfix));
        }

        harmony.Patch(deserialize, postfix: new HarmonyMethod(deserializePostfix));

        MethodInfo receivePrefix = AccessTools.Method(
            typeof(LanConnectTailMessagePatches),
            nameof(ReceivePrefix))
            ?? throw new MissingMethodException(nameof(ReceivePrefix));
        MethodInfo receiveFinalizer = AccessTools.Method(
            typeof(LanConnectTailMessagePatches),
            nameof(ReceiveFinalizer))
            ?? throw new MissingMethodException(nameof(ReceiveFinalizer));
        foreach (Type serviceType in new[] { typeof(NetHostGameService), typeof(NetClientGameService) })
        {
            MethodInfo receive = AccessTools.Method(
                serviceType,
                "OnPacketReceived",
                [typeof(ulong), typeof(byte[]),
                    typeof(MegaCrit.Sts2.Core.Multiplayer.Transport.NetTransferMode), typeof(int)])
                ?? throw new MissingMethodException(serviceType.FullName, "OnPacketReceived");
            harmony.Patch(
                receive,
                prefix: new HarmonyMethod(receivePrefix),
                finalizer: new HarmonyMethod(receiveFinalizer));
        }
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
    private static void SerializePrefix<T>(
        NetMessageBus __instance,
        ulong senderId,
        ref T message,
        out LanConnectPreparedTailMessage? __state)
        where T : struct, INetMessage
    {
        __state = null;
        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        LanConnectProtocolSelection? selection = snapshot.Selection;
        if (selection?.Profile != LanConnectProtocolProfile.TailV1 || !snapshot.IsActive)
        {
            return;
        }

        ILanConnectTailMessageRuntime runtime = Volatile.Read(ref _runtime)
            ?? throw new InvalidOperationException("Tail protocol message runtime is not configured.");
        LanConnectSidecarMessageKind kind = GetMessageKind(typeof(T));
        LanConnectPreparedTailMessage prepared = runtime.PrepareOutgoing(
            __instance,
            kind,
            senderId,
            message,
            selection);
        if (prepared.Message is not T projected)
        {
            throw new InvalidOperationException(
                $"Tail runtime returned {prepared.Message.GetType().FullName} for {typeof(T).FullName}.");
        }

        message = projected;
        __state = prepared;
        if (selection.Carrier == LanConnectProtocolCarrier.RitsuLibSidecarV1)
        {
            runtime.SubmitSidecarBeforeVanilla(kind, senderId, message, prepared.Container, selection);
        }
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
        if (!__result || message == null || !overrideSenderId.HasValue)
        {
            return;
        }

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

        try
        {
            LanConnectSidecarMessageKind kind = GetMessageKind(message.GetType());
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

    private static LanConnectSidecarMessageKind GetMessageKind(Type type)
    {
        foreach (LanConnectSidecarMessageKind kind in Enum.GetValues<LanConnectSidecarMessageKind>())
        {
            if (string.Equals(type.Name, GetMessageTypeName(kind), StringComparison.Ordinal))
            {
                return kind;
            }
        }

        throw Invalid($"Message type {type.FullName} is not part of LAN protocol v1.");
    }

    private static string GetMessageTypeName(LanConnectSidecarMessageKind kind) => kind switch
    {
        LanConnectSidecarMessageKind.InitialGameInfo => "InitialGameInfoMessage",
        LanConnectSidecarMessageKind.LobbyJoinRequest => "ClientLobbyJoinRequestMessage",
        LanConnectSidecarMessageKind.LobbyJoinResponse => "ClientLobbyJoinResponseMessage",
        LanConnectSidecarMessageKind.LoadJoinRequest => "ClientLoadJoinRequestMessage",
        LanConnectSidecarMessageKind.LoadJoinResponse => "ClientLoadJoinResponseMessage",
        LanConnectSidecarMessageKind.RejoinRequest => "ClientRejoinRequestMessage",
        LanConnectSidecarMessageKind.RejoinResponse => "ClientRejoinResponseMessage",
        LanConnectSidecarMessageKind.ConnectionFailed => "ClientConnectionFailedMessage",
        LanConnectSidecarMessageKind.PlayerJoined => "PlayerJoinedMessage",
        LanConnectSidecarMessageKind.LobbyBeginRun => "LobbyBeginRunMessage",
        _ => throw Invalid($"Unknown LAN message kind {kind}.")
    };
}
