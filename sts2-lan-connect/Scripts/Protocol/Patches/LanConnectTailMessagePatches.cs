using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectTailMessagePayload(
    LanConnectSidecarMessageKind MessageKind,
    LanConnectProtocolOffer? PeerOffer,
    LanConnectCapabilitiesSelection? SessionSelection,
    LanConnectRosterSnapshot? Roster,
    LanConnectProtocolFailure? Rejection);

internal interface ILanConnectTailMessageRuntime
{
    byte[] BuildOutgoingContainer(
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
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        INetMessage message,
        byte[] container,
        LanConnectProtocolSelection selection);

    bool TryPairSidecarIncoming(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        INetMessage message,
        LanConnectProtocolSelection selection);
}

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
            harmony.Patch(serialize, postfix: new HarmonyMethod(serializePostfix));
        }

        harmony.Patch(deserialize, postfix: new HarmonyMethod(deserializePostfix));
    }

    internal static byte[] EncodePeerOfferMessage(
        LanConnectSidecarMessageKind messageKind,
        LanConnectProtocolOffer offer)
    {
        if (!IsRequest(messageKind))
        {
            throw Invalid($"Message kind {messageKind} cannot carry a peer offer.");
        }

        LanConnectTailEntry capabilities = new(
            LanConnectTailEntry.CapabilitiesId,
            1,
            true,
            LanConnectCapabilitiesCodec.EncodePeerOffer(offer));
        return LanConnectTailCodec.Encode(0, [capabilities]);
    }

    internal static byte[] EncodeSessionMessage(
        LanConnectSidecarMessageKind messageKind,
        LanConnectProtocolSelection selection,
        LanConnectRosterSnapshot? roster = null,
        LanConnectProtocolFailure? rejection = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (IsRequest(messageKind))
        {
            throw Invalid($"Request kind {messageKind} must carry a peer offer, not a session selection.");
        }

        ValidateEntryMatrix(messageKind, roster, rejection);
        List<LanConnectTailEntry> entries =
        [
            new(
                LanConnectTailEntry.CapabilitiesId,
                1,
                true,
                LanConnectCapabilitiesCodec.EncodeSessionSelection(selection))
        ];
        if (roster != null)
        {
            entries.Add(new LanConnectTailEntry(
                LanConnectTailEntry.RosterId,
                1,
                true,
                LanConnectRosterCodec.Encode(roster)));
        }

        if (rejection != null)
        {
            entries.Add(new LanConnectTailEntry(
                LanConnectTailEntry.RejectionId,
                1,
                true,
                LanConnectRejectionCodec.Encode(rejection)));
        }

        return LanConnectTailCodec.Encode(checked((ushort)selection.SelectedLanProtocolVersion), entries);
    }

    internal static LanConnectTailMessagePayload DecodeAndValidate(
        LanConnectSidecarMessageKind messageKind,
        ReadOnlySpan<byte> container,
        LanConnectProtocolSelection? frozenSelection = null,
        ulong? transportSenderPeerId = null,
        ulong? currentHostPeerId = null)
    {
        LanConnectTailEnvelope envelope = LanConnectTailCodec.Decode(container);
        LanConnectTailEntry capabilities = RequireSingle(envelope, LanConnectTailEntry.CapabilitiesId);
        LanConnectTailEntry? rosterEntry = Find(envelope, LanConnectTailEntry.RosterId);
        LanConnectTailEntry? rejectionEntry = Find(envelope, LanConnectTailEntry.RejectionId);

        if (IsRequest(messageKind))
        {
            if (rosterEntry != null || rejectionEntry != null || envelope.SessionProtocolVersion != 0)
            {
                throw Invalid("Join requests permit only a session-version-zero peer offer.");
            }

            LanConnectProtocolOffer offer = LanConnectCapabilitiesCodec.DecodePeerOffer(
                capabilities.Payload.Span,
                envelope.SessionProtocolVersion);
            return new LanConnectTailMessagePayload(messageKind, offer, null, null, null);
        }

        if (frozenSelection == null)
        {
            throw Invalid("Session messages require a frozen protocol selection.");
        }

        LanConnectCapabilitiesSelection selection = LanConnectCapabilitiesCodec.DecodeSessionSelection(
            capabilities.Payload.Span,
            envelope.SessionProtocolVersion);
        LanConnectCapabilitiesCodec.ValidateMatches(selection, frozenSelection);
        LanConnectRosterSnapshot? roster = rosterEntry == null
            ? null
            : LanConnectRosterCodec.Decode(rosterEntry.Payload.Span);
        LanConnectProtocolFailure? rejection = rejectionEntry == null
            ? null
            : LanConnectRejectionCodec.Decode(rejectionEntry.Payload.Span);
        ValidateEntryMatrix(messageKind, roster, rejection);
        if (roster != null)
        {
            if (!transportSenderPeerId.HasValue || !currentHostPeerId.HasValue)
            {
                throw Invalid("Roster messages require transport-sender and current-host authority inputs.");
            }

            LanConnectRosterCodec.ValidateAuthority(roster, transportSenderPeerId.Value, currentHostPeerId.Value);
        }

        return new LanConnectTailMessagePayload(messageKind, null, selection, roster, rejection);
    }

    private static void ValidateEntryMatrix(
        LanConnectSidecarMessageKind messageKind,
        LanConnectRosterSnapshot? roster,
        LanConnectProtocolFailure? rejection)
    {
        bool requiresRoster = messageKind is
            LanConnectSidecarMessageKind.LobbyJoinResponse or
            LanConnectSidecarMessageKind.LoadJoinResponse or
            LanConnectSidecarMessageKind.RejoinResponse or
            LanConnectSidecarMessageKind.PlayerJoined or
            LanConnectSidecarMessageKind.LobbyBeginRun;
        bool requiresRejection = messageKind == LanConnectSidecarMessageKind.ConnectionFailed;
        if ((roster != null) != requiresRoster
            || (rejection != null) != requiresRejection
            || (roster != null && rejection != null))
        {
            throw Invalid($"Message kind {messageKind} has an invalid roster/rejection entry combination.");
        }
    }

    private static bool IsRequest(LanConnectSidecarMessageKind kind) => kind is
        LanConnectSidecarMessageKind.LobbyJoinRequest or
        LanConnectSidecarMessageKind.LoadJoinRequest or
        LanConnectSidecarMessageKind.RejoinRequest;

    private static LanConnectTailEntry RequireSingle(LanConnectTailEnvelope envelope, string id) =>
        Find(envelope, id) ?? throw Invalid($"Required entry '{id}' is missing.");

    private static LanConnectTailEntry? Find(LanConnectTailEnvelope envelope, string id) =>
        envelope.Entries.SingleOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));

    private static InvalidDataException Invalid(string message) => new(message);

    // ReSharper disable UnusedMember.Local -- invoked by Harmony.
    private static void SerializePostfix(
        NetMessageBus __instance,
        ulong senderId,
        object message,
        ref int length,
        ref byte[] __result)
    {
        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        LanConnectProtocolSelection? selection = snapshot.Selection;
        if (selection?.Profile != LanConnectProtocolProfile.TailV1 || !snapshot.IsActive)
        {
            return;
        }

        ILanConnectTailMessageRuntime runtime = Volatile.Read(ref _runtime)
            ?? throw new InvalidOperationException("Tail protocol message runtime is not configured.");
        LanConnectSidecarMessageKind kind = GetMessageKind(message.GetType());
        byte[] container = runtime.BuildOutgoingContainer(kind, senderId, message, selection);
        if (selection.Carrier == LanConnectProtocolCarrier.RitsuLibSidecarV1)
        {
            runtime.SubmitSidecarBeforeVanilla(kind, senderId, message, container, selection);
            return;
        }

        PacketWriter writer = NetMessageBusWriter?.GetValue(__instance) as PacketWriter
            ?? throw new InvalidOperationException("NetMessageBus writer is unavailable.");
        LanConnectStandaloneTailPlacement placement = LanConnectStandaloneTailCarrier.Write(
            writer,
            container,
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
            if (selection.Carrier == LanConnectProtocolCarrier.StandaloneTailV1)
            {
                PacketReader reader = NetMessageBusReader?.GetValue(__instance) as PacketReader
                    ?? throw new InvalidOperationException("NetMessageBus reader is unavailable.");
                LanConnectStandaloneTailPlacement placement = LanConnectStandaloneTailCarrier.Read(reader, selection);
                runtime.ValidateStandaloneIncoming(
                    kind,
                    overrideSenderId.Value,
                    message,
                    placement.ContainerBytes,
                    selection);
            }
            else if (!runtime.TryPairSidecarIncoming(
                         __instance,
                         kind,
                         overrideSenderId.Value,
                         message,
                         selection))
            {
                __result = false;
                message = null;
                overrideSenderId = null;
            }
        }
        catch
        {
            __result = false;
            message = null;
            overrideSenderId = null;
            throw;
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
