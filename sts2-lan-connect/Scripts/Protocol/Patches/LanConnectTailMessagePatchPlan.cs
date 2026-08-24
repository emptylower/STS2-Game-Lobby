using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectTailPatchStep(
    string Id,
    string Category,
    Type? MessageType,
    MethodInfo Target,
    MethodInfo? Prefix = null,
    MethodInfo? Postfix = null,
    MethodInfo? Finalizer = null,
    int? PrefixPriority = null,
    int? PostfixPriority = null,
    int? FinalizerPriority = null)
{
    internal IEnumerable<MethodInfo> Hooks
    {
        get
        {
            if (Prefix != null)
            {
                yield return Prefix;
            }

            if (Postfix != null)
            {
                yield return Postfix;
            }

            if (Finalizer != null)
            {
                yield return Finalizer;
            }
        }
    }
}

internal sealed class LanConnectTailPatchPlan
{
    internal const string DesktopProfile = "desktop_generic_v1";
    internal const string DefaultProfile = "non_generic_v2";

    internal LanConnectTailPatchPlan(
        string profile,
        IReadOnlyList<(LanConnectSidecarMessageKind Kind, Type Type)> resolvedKinds,
        IReadOnlyList<Type> messageTypes,
        IReadOnlyList<LanConnectTailPatchStep> steps)
    {
        Profile = profile;
        ResolvedKinds = resolvedKinds;
        MessageTypes = messageTypes;
        Steps = steps;

        int expectedSteps = profile == DesktopProfile ? 30 : 15;
        if (ResolvedKinds.Count != 10 || MessageTypes.Count != 9 || Steps.Count != expectedSteps)
        {
            throw new InvalidDataException(
                $"Tail patch plan {profile} has an invalid shape: " +
                $"kinds={ResolvedKinds.Count}/10, types={MessageTypes.Count}/9, steps={Steps.Count}/{expectedSteps}.");
        }

        string? duplicateId = Steps
            .GroupBy(static step => step.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() != 1)?
            .Key;
        if (duplicateId != null)
        {
            throw new InvalidDataException($"Tail patch plan {profile} contains duplicate ID {duplicateId}.");
        }

        if (profile != DesktopProfile)
        {
            ValidateNonGenericMethodsAreConcrete();
            if (GenericTargetCount != 0)
            {
                throw new InvalidDataException(
                    $"Tail patch plan {profile} must not contain generic targets; found={GenericTargetCount}.");
            }
        }
    }

    internal string Profile { get; }
    internal IReadOnlyList<(LanConnectSidecarMessageKind Kind, Type Type)> ResolvedKinds { get; }
    internal IReadOnlyList<Type> MessageTypes { get; }
    internal IReadOnlyList<LanConnectTailPatchStep> Steps { get; }
    internal int GenericTargetCount => Steps.Count(static step => step.Target.IsGenericMethod);

    private void ValidateNonGenericMethodsAreConcrete()
    {
        foreach (LanConnectTailPatchStep step in Steps)
        {
            ValidateConcrete(step.Id, "target", step.Target);
            foreach (MethodInfo hook in step.Hooks)
            {
                ValidateConcrete(step.Id, "hook", hook);
            }
        }
    }

    private static void ValidateConcrete(string id, string role, MethodInfo method)
    {
        if (method.IsGenericMethod
            || method.ContainsGenericParameters
            || method.DeclaringType?.ContainsGenericParameters == true)
        {
            throw new InvalidDataException(
                $"Tail patch {id} {role} must be non-generic: {LanConnectTailMessagePatches.FormatMethod(method)}.");
        }
    }
}

internal static partial class LanConnectTailMessagePatches
{
    internal static LanConnectTailPatchPlan ResolvePatchPlan(Assembly sts2Assembly, bool isAndroid)
        => ResolvePatchPlan(sts2Assembly, isAndroid, preferLegacyDesktopGenericPlan: false);

    internal static LanConnectTailPatchPlan ResolvePatchPlan(
        Assembly sts2Assembly,
        bool isAndroid,
        bool preferLegacyDesktopGenericPlan)
    {
        ArgumentNullException.ThrowIfNull(sts2Assembly);
        IReadOnlyList<(LanConnectSidecarMessageKind Kind, Type Type)> resolvedKinds =
            ResolveAllMessageKinds(sts2Assembly);
        Type[] messageTypes = resolvedKinds
            .Select(static resolved => resolved.Type)
            .Distinct()
            .ToArray();

        // The non-generic plan is the default on every platform: closed generic targets can be
        // poisoned by foreign patches declared on generic types (RitsuLib), and the non-generic
        // plan is byte-equivalent per the golden vector runtime tests. The legacy desktop generic
        // plan remains available as an explicit rollback branch.
        return isAndroid || !preferLegacyDesktopGenericPlan
            ? ResolveNonGenericPatchPlan(resolvedKinds, messageTypes)
            : ResolveDesktopPatchPlan(resolvedKinds, messageTypes);
    }

    private static IReadOnlyList<(LanConnectSidecarMessageKind Kind, Type Type)> ResolveAllMessageKinds(
        Assembly sts2Assembly)
    {
        LanConnectSidecarMessageKind[] kinds = Enum.GetValues<LanConnectSidecarMessageKind>();
        if (kinds.Length != 10)
        {
            throw new InvalidDataException($"Tail message kind matrix changed: found={kinds.Length}, expected=10.");
        }

        List<(LanConnectSidecarMessageKind Kind, Type Type)> resolved = new(kinds.Length);
        foreach (LanConnectSidecarMessageKind kind in kinds)
        {
            string typeName =
                $"MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.{LanConnectTailMessageTypeMatrix.GetTypeName(kind)}";
            Type type = sts2Assembly.GetType(typeName, throwOnError: false, ignoreCase: false)
                ?? throw new TypeLoadException(
                    $"Tail message kind {kind} requires missing concrete type {typeName}.");
            resolved.Add((kind, type));
        }

        int concreteTypeCount = resolved.Select(static item => item.Type).Distinct().Count();
        if (concreteTypeCount != 9)
        {
            throw new InvalidDataException(
                $"Tail message kind matrix must resolve 10 kinds to 9 concrete types; found={concreteTypeCount}.");
        }

        return resolved;
    }

    private static LanConnectTailPatchPlan ResolveDesktopPatchPlan(
        IReadOnlyList<(LanConnectSidecarMessageKind Kind, Type Type)> resolvedKinds,
        IReadOnlyList<Type> messageTypes)
    {
        List<LanConnectTailPatchStep> steps = [];
        MethodInfo serializePostfix = RequireHook(nameof(SerializePostfix));
        foreach (Type messageType in messageTypes)
        {
            MethodInfo serialize = LanConnectSerializationPatches.ResolveGenericSerializeMessageMethod(
                typeof(NetMessageBus),
                messageType);
            steps.Add(new LanConnectTailPatchStep(
                $"tail.serialize.{StableTypeId(messageType)}",
                "serialize",
                messageType,
                serialize,
                ResolveSerializePrefix(messageType),
                serializePostfix,
                PrefixPriority: Priority.First + 100));
        }

        AddSharedIncomingSteps(steps);

        MethodInfo hostBroadcastDefinition = typeof(NetHostGameService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == nameof(NetHostGameService.SendMessage)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters().Length == 1);
        MethodInfo hostSendInternalDefinition = typeof(NetHostGameService)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == "SendMessageToClientInternal"
                              && method.IsGenericMethodDefinition
                              && method.GetParameters().Length == 4);
        MethodInfo hostBroadcastPrefix = RequireHook(nameof(HostBroadcastPrefix));
        MethodInfo hostSendInternalPrefix = RequireHook(nameof(HostSendInternalPrefix));
        MethodInfo hostSendFinalizer = RequireHook(nameof(HostSendFinalizer));
        foreach (Type messageType in messageTypes)
        {
            string stableTypeId = StableTypeId(messageType);
            steps.Add(new LanConnectTailPatchStep(
                $"tail.host.broadcast.{stableTypeId}",
                "host_broadcast",
                messageType,
                hostBroadcastDefinition.MakeGenericMethod(messageType),
                hostBroadcastPrefix,
                Finalizer: hostSendFinalizer));
            steps.Add(new LanConnectTailPatchStep(
                $"tail.host.targeted.{stableTypeId}",
                "host_targeted",
                messageType,
                hostSendInternalDefinition.MakeGenericMethod(messageType),
                hostSendInternalPrefix,
                Finalizer: hostSendFinalizer));
        }

        return new LanConnectTailPatchPlan(
            LanConnectTailPatchPlan.DesktopProfile,
            resolvedKinds,
            messageTypes,
            steps);
    }

    private static LanConnectTailPatchPlan ResolveNonGenericPatchPlan(
        IReadOnlyList<(LanConnectSidecarMessageKind Kind, Type Type)> resolvedKinds,
        IReadOnlyList<Type> messageTypes)
    {
        List<LanConnectTailPatchStep> steps = [];
        MethodInfo serializerPostfix = RequireHook(nameof(AndroidConcreteSerializePostfix));
        foreach (Type messageType in messageTypes)
        {
            MethodInfo serialize = AccessTools.Method(messageType, "Serialize", [typeof(PacketWriter)])
                ?? throw new MissingMethodException(messageType.FullName, "Serialize(PacketWriter)");
            steps.Add(new LanConnectTailPatchStep(
                $"tail.android.serialize.{StableTypeId(messageType)}",
                "android_concrete_serialize",
                messageType,
                serialize,
                ResolveAndroidSerializePrefix(messageType),
                serializerPostfix,
                PrefixPriority: Priority.First + 100));
        }

        MethodInfo reset = AccessTools.Method(typeof(PacketWriter), nameof(PacketWriter.Reset), Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(PacketWriter).FullName, nameof(PacketWriter.Reset));
        steps.Add(new LanConnectTailPatchStep(
            "tail.android.writer_reset",
            "android_writer_reset",
            null,
            reset,
            RequireHook(nameof(AndroidWriterResetPrefix)),
            PrefixPriority: Priority.First + 100));

        AddSharedIncomingSteps(steps);

        MethodInfo hostSend = AccessTools.Method(
            typeof(ENetHost),
            nameof(ENetHost.SendMessageToClient),
            [typeof(ulong), typeof(byte[]), typeof(int), typeof(NetTransferMode), typeof(int)])
            ?? throw new MissingMethodException(typeof(ENetHost).FullName, nameof(ENetHost.SendMessageToClient));
        steps.Add(new LanConnectTailPatchStep(
            "tail.android.transport.host",
            "android_transport",
            null,
            hostSend,
            RequireHook(nameof(AndroidHostTransportPrefix)),
            Finalizer: RequireHook(nameof(AndroidHostTransportFinalizer)),
            PrefixPriority: Priority.First + 100));

        MethodInfo clientSend = AccessTools.Method(
            typeof(ENetClient),
            nameof(ENetClient.SendMessageToHost),
            [typeof(byte[]), typeof(int), typeof(NetTransferMode), typeof(int)])
            ?? throw new MissingMethodException(typeof(ENetClient).FullName, nameof(ENetClient.SendMessageToHost));
        steps.Add(new LanConnectTailPatchStep(
            "tail.android.transport.client",
            "android_transport",
            null,
            clientSend,
            RequireHook(nameof(AndroidClientTransportPrefix)),
            Finalizer: RequireHook(nameof(AndroidClientTransportFinalizer)),
            PrefixPriority: Priority.First + 100));

        return new LanConnectTailPatchPlan(
            LanConnectTailPatchPlan.DefaultProfile,
            resolvedKinds,
            messageTypes,
            steps);
    }

    private static void AddSharedIncomingSteps(List<LanConnectTailPatchStep> steps)
    {
        MethodInfo deserialize = AccessTools.Method(
            typeof(NetMessageBus),
            nameof(NetMessageBus.TryDeserializeMessage),
            [typeof(byte[]), typeof(INetMessage).MakeByRefType(), typeof(ulong?).MakeByRefType()])
            ?? throw new MissingMethodException(typeof(NetMessageBus).FullName, nameof(NetMessageBus.TryDeserializeMessage));
        steps.Add(new LanConnectTailPatchStep(
            "tail.deserialize",
            "deserialize",
            null,
            deserialize,
            Postfix: RequireHook(nameof(DeserializePostfix))));

        MethodInfo receivePrefix = RequireHook(nameof(ReceivePrefix));
        MethodInfo receiveFinalizer = RequireHook(nameof(ReceiveFinalizer));
        foreach ((Type serviceType, string id) in new[]
                 {
                     (typeof(NetHostGameService), "host"),
                     (typeof(NetClientGameService), "client")
                 })
        {
            MethodInfo receive = AccessTools.Method(
                serviceType,
                "OnPacketReceived",
                [typeof(ulong), typeof(byte[]), typeof(NetTransferMode), typeof(int)])
                ?? throw new MissingMethodException(serviceType.FullName, "OnPacketReceived");
            steps.Add(new LanConnectTailPatchStep(
                $"tail.receive.{id}",
                "receive",
                null,
                receive,
                receivePrefix,
                Finalizer: receiveFinalizer));
        }
    }

    private static MethodInfo RequireHook(string name) =>
        AccessTools.Method(typeof(LanConnectTailMessagePatches), name)
        ?? throw new MissingMethodException(typeof(LanConnectTailMessagePatches).FullName, name);

    private static MethodInfo ResolveAndroidSerializePrefix(Type messageType)
    {
        string methodName = messageType.Name switch
        {
            "InitialGameInfoMessage" => nameof(AndroidSerializeInitialGameInfoPrefix),
            "ClientLobbyJoinRequestMessage" => nameof(AndroidSerializeLobbyJoinRequestPrefix),
            "ClientLobbyJoinResponseMessage" => nameof(AndroidSerializeLobbyJoinResponsePrefix),
            "ClientLoadJoinRequestMessage" => nameof(AndroidSerializeLoadJoinRequestPrefix),
            "ClientLoadJoinResponseMessage" => nameof(AndroidSerializeLoadJoinResponsePrefix),
            "ClientRejoinRequestMessage" => nameof(AndroidSerializeRejoinRequestPrefix),
            "ClientRejoinResponseMessage" => nameof(AndroidSerializeRejoinResponsePrefix),
            "PlayerJoinedMessage" => nameof(AndroidSerializePlayerJoinedPrefix),
            "LobbyBeginRunMessage" => nameof(AndroidSerializeLobbyBeginRunPrefix),
            _ => throw new InvalidDataException(
                $"Message type {messageType.FullName} has no Android concrete serializer prefix.")
        };
        return RequireHook(methodName);
    }

    private static string StableTypeId(Type type) => type.Name switch
    {
        "InitialGameInfoMessage" => "initial_game_info",
        "ClientLobbyJoinRequestMessage" => "lobby_join_request",
        "ClientLobbyJoinResponseMessage" => "lobby_join_response",
        "ClientLoadJoinRequestMessage" => "load_join_request",
        "ClientLoadJoinResponseMessage" => "load_join_response",
        "ClientRejoinRequestMessage" => "rejoin_request",
        "ClientRejoinResponseMessage" => "rejoin_response",
        "PlayerJoinedMessage" => "player_joined",
        "LobbyBeginRunMessage" => "lobby_begin_run",
        _ => throw new InvalidDataException($"Unknown Tail concrete message type {type.FullName}.")
    };
}

internal static class LanConnectTailPlanOverride
{
    // Emergency rollback only: launch the desktop game with STS2_LAN_CONNECT_TAIL_PLAN=desktop_generic_v1
    // to restore the pre-alpha.9 generic plan. Android ignores this because Mono gshared cannot
    // compile closed generic wrappers there.
    private const string PlanEnvironmentVariable = "STS2_LAN_CONNECT_TAIL_PLAN";

    private static bool? _preferLegacyDesktopGenericPlanForTesting;

    internal static bool PreferLegacyDesktopGenericPlan =>
        _preferLegacyDesktopGenericPlanForTesting
        ?? string.Equals(
            Environment.GetEnvironmentVariable(PlanEnvironmentVariable),
            LanConnectTailPatchPlan.DesktopProfile,
            StringComparison.Ordinal);

    internal static void SetPreferLegacyDesktopGenericPlanForTesting(bool? value) =>
        _preferLegacyDesktopGenericPlanForTesting = value;
}
