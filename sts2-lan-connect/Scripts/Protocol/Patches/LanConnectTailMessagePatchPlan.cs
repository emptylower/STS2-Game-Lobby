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
    int? FinalizerPriority = null,
    MethodInfo? FallbackTarget = null,
    MethodInfo? FallbackPrefix = null)
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

            if (FallbackPrefix != null)
            {
                yield return FallbackPrefix;
            }
        }
    }
}

/// <summary>
/// native_bus_v1 的唯一补丁计划（原 non_generic_v2 的非泛型形态，全平台统一）。
/// 步骤数 16：9 serialize（10 kinds 解析到 9 个具体类型）+ 1 writer_reset + 2 receive +
/// 1 deserialize + 1 dispatch barrier + 2 transport。
/// 桌面平台 9 个 serialize 步骤改挂 NetMessageBus.SerializeMessage&lt;T&gt; 的闭合实例化
/// （RitsuLib 补丁后优化编译体会内联小结构体 Serialize，绕过 T.Serialize 上的 detour）；
/// 安卓保持 T.Serialize 目标（gshared 无法为闭合泛型生成 wrapper）。
/// SetBufferMessages 目标被禁止（RitsuLib sync 补丁所有权）。
/// </summary>
internal sealed class LanConnectTailPatchPlan
{
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

        const int expectedSteps = 16;
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

        if (steps.Any(static step => step.Target.Name == nameof(NetMessageBus.SetBufferMessages)))
        {
            throw new InvalidDataException(
                $"Tail patch plan {profile} must not patch NetMessageBus.SetBufferMessages.");
        }

        ValidateTargets();
        ValidateNonGenericHooksAreConcrete();
    }

    internal string Profile { get; }
    internal IReadOnlyList<(LanConnectSidecarMessageKind Kind, Type Type)> ResolvedKinds { get; }
    internal IReadOnlyList<Type> MessageTypes { get; }
    internal IReadOnlyList<LanConnectTailPatchStep> Steps { get; }
    internal int GenericTargetCount => Steps.Count(static step => step.Target.IsGenericMethod);

    private void ValidateTargets()
    {
        // 安卓：Mono/gshared 无法为闭合泛型目标生成 wrapper，全部目标必须非泛型。
        // 桌面：仅 serialize 类别的 9 步允许（且必须是）SerializeMessage 闭合实例化，
        // 并携带非泛型的 T.Serialize 回退目标；其余类别目标必须非泛型。
        if (OperatingSystem.IsAndroid())
        {
            foreach (LanConnectTailPatchStep step in Steps)
            {
                ValidateConcrete(step.Id, "target", step.Target);
            }

            if (GenericTargetCount != 0)
            {
                throw new InvalidDataException(
                    $"Tail patch plan {Profile} must not contain generic targets on Android; found={GenericTargetCount}.");
            }

            return;
        }

        int serializeGenericTargets = 0;
        foreach (LanConnectTailPatchStep step in Steps)
        {
            if (step.Category != "serialize")
            {
                ValidateConcrete(step.Id, "target", step.Target);
                continue;
            }

            if (!step.Target.IsGenericMethod
                || step.Target.IsGenericMethodDefinition
                || step.Target.ContainsGenericParameters)
            {
                throw new InvalidDataException(
                    $"Tail patch {step.Id} target must be a closed instantiation of the bus serializer on desktop: " +
                    $"{LanConnectTailMessagePatches.FormatMethod(step.Target)}.");
            }

            serializeGenericTargets++;
            if (step.FallbackTarget == null || step.FallbackPrefix == null)
            {
                throw new InvalidDataException(
                    $"Tail patch {step.Id} must carry a T.Serialize fallback target on desktop.");
            }

            ValidateConcrete(step.Id, "fallback target", step.FallbackTarget!);
        }

        if (serializeGenericTargets != 9 || GenericTargetCount != 9)
        {
            throw new InvalidDataException(
                $"Tail patch plan {Profile} must expose exactly 9 generic serialize targets on desktop; " +
                $"serializeGeneric={serializeGenericTargets}, genericTotal={GenericTargetCount}.");
        }
    }

    private void ValidateNonGenericHooksAreConcrete()
    {
        foreach (LanConnectTailPatchStep step in Steps)
        {
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
    internal static LanConnectTailPatchPlan ResolvePatchPlan(Assembly sts2Assembly)
    {
        ArgumentNullException.ThrowIfNull(sts2Assembly);
        IReadOnlyList<(LanConnectSidecarMessageKind Kind, Type Type)> resolvedKinds =
            ResolveAllMessageKinds(sts2Assembly);
        Type[] messageTypes = resolvedKinds
            .Select(static resolved => resolved.Type)
            .Distinct()
            .ToArray();

        List<LanConnectTailPatchStep> steps = [];

        // 第一级：10 个具体消息 Serialize prefix/postfix（容器生产 seam，不改写原版字节）。
        // 桌面挂 SerializeMessage<T> 闭合实例化（RitsuLib 补丁会内联小结构体 Serialize，
        // 绕过 T.Serialize detour）；安卓 gshared 保持 T.Serialize 目标。
        MethodInfo serializerPostfix = RequireHook(nameof(AndroidConcreteSerializePostfix));
        MethodInfo serializeMessageDefinition = AccessTools.DeclaredMethod(
            typeof(NetMessageBus),
            nameof(NetMessageBus.SerializeMessage))
            ?? throw new MissingMethodException(
                typeof(NetMessageBus).FullName,
                nameof(NetMessageBus.SerializeMessage));
        bool useBusSerializeSeam = !OperatingSystem.IsAndroid();
        foreach (Type messageType in messageTypes)
        {
            MethodInfo serialize = AccessTools.Method(messageType, "Serialize", [typeof(PacketWriter)])
                ?? throw new MissingMethodException(messageType.FullName, "Serialize(PacketWriter)");
            steps.Add(new LanConnectTailPatchStep(
                $"tail.serialize.{StableTypeId(messageType)}",
                "serialize",
                messageType,
                useBusSerializeSeam ? serializeMessageDefinition.MakeGenericMethod(messageType) : serialize,
                useBusSerializeSeam
                    ? ResolveBusSerializePrefix(messageType)
                    : ResolveSerializePrefix(messageType),
                serializerPostfix,
                PrefixPriority: Priority.First + 100,
                FallbackTarget: useBusSerializeSeam ? serialize : null,
                FallbackPrefix: useBusSerializeSeam ? ResolveSerializePrefix(messageType) : null));
        }

        // PacketWriter.Reset prefix：清除该 writer 的 pending（广播批次结束后防误命中残留）。
        MethodInfo reset = AccessTools.Method(typeof(PacketWriter), nameof(PacketWriter.Reset), Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(PacketWriter).FullName, nameof(PacketWriter.Reset));
        steps.Add(new LanConnectTailPatchStep(
            "tail.writer_reset",
            "writer_reset",
            null,
            reset,
            RequireHook(nameof(AndroidWriterResetPrefix)),
            PrefixPriority: Priority.First + 100));

        // 接收上下文捕获：两个 OnPacketReceived prefix。
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

        // TryDeserializeMessage：prefix（<9 字节已知 ID 拦截）+ postfix（未知 ID offset-9 捕获）+ finalizer。
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
            RequireHook(nameof(TryDeserializePrefix)),
            RequireHook(nameof(TryDeserializePostfix)),
            RequireHook(nameof(TryDeserializeFinalizer))));

        // 配对屏障：SendMessageToAllHandlers prefix（hold 一帧，零自有队列）。
        MethodInfo dispatch = AccessTools.Method(
            typeof(NetMessageBus),
            nameof(NetMessageBus.SendMessageToAllHandlers),
            [typeof(INetMessage), typeof(ulong)])
            ?? throw new MissingMethodException(typeof(NetMessageBus).FullName, nameof(NetMessageBus.SendMessageToAllHandlers));
        steps.Add(new LanConnectTailPatchStep(
            "tail.dispatch_barrier",
            "dispatch_barrier",
            null,
            dispatch,
            RequireHook(nameof(DispatchBarrierPrefix))));

        // 第二/第三级：两个 transport send 点 prefix/postfix/finalizer（prefix 先于第三方发送补丁）。
        MethodInfo hostSend = AccessTools.Method(
            typeof(ENetHost),
            nameof(ENetHost.SendMessageToClient),
            [typeof(ulong), typeof(byte[]), typeof(int), typeof(NetTransferMode), typeof(int)])
            ?? throw new MissingMethodException(typeof(ENetHost).FullName, nameof(ENetHost.SendMessageToClient));
        steps.Add(new LanConnectTailPatchStep(
            "tail.transport.host",
            "transport",
            null,
            hostSend,
            RequireHook(nameof(AndroidHostTransportPrefix)),
            RequireHook(nameof(AndroidHostTransportPostfix)),
            RequireHook(nameof(AndroidHostTransportFinalizer)),
            PrefixPriority: Priority.First + 100));

        MethodInfo clientSend = AccessTools.Method(
            typeof(ENetClient),
            nameof(ENetClient.SendMessageToHost),
            [typeof(byte[]), typeof(int), typeof(NetTransferMode), typeof(int)])
            ?? throw new MissingMethodException(typeof(ENetClient).FullName, nameof(ENetClient.SendMessageToHost));
        steps.Add(new LanConnectTailPatchStep(
            "tail.transport.client",
            "transport",
            null,
            clientSend,
            RequireHook(nameof(AndroidClientTransportPrefix)),
            RequireHook(nameof(AndroidClientTransportPostfix)),
            RequireHook(nameof(AndroidClientTransportFinalizer)),
            PrefixPriority: Priority.First + 100));

        return new LanConnectTailPatchPlan(
            "native_bus_v1",
            resolvedKinds,
            messageTypes,
            steps);
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

    private static MethodInfo RequireHook(string name) =>
        AccessTools.Method(typeof(LanConnectTailMessagePatches), name)
        ?? throw new MissingMethodException(typeof(LanConnectTailMessagePatches).FullName, name);

    private static MethodInfo ResolveSerializePrefix(Type messageType)
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
                $"Message type {messageType.FullName} has no concrete serializer prefix.")
        };
        return RequireHook(methodName);
    }

    private static MethodInfo ResolveBusSerializePrefix(Type messageType)
    {
        string methodName = messageType.Name switch
        {
            "InitialGameInfoMessage" => nameof(BusSerializeInitialGameInfoPrefix),
            "ClientLobbyJoinRequestMessage" => nameof(BusSerializeLobbyJoinRequestPrefix),
            "ClientLobbyJoinResponseMessage" => nameof(BusSerializeLobbyJoinResponsePrefix),
            "ClientLoadJoinRequestMessage" => nameof(BusSerializeLoadJoinRequestPrefix),
            "ClientLoadJoinResponseMessage" => nameof(BusSerializeLoadJoinResponsePrefix),
            "ClientRejoinRequestMessage" => nameof(BusSerializeRejoinRequestPrefix),
            "ClientRejoinResponseMessage" => nameof(BusSerializeRejoinResponsePrefix),
            "PlayerJoinedMessage" => nameof(BusSerializePlayerJoinedPrefix),
            "LobbyBeginRunMessage" => nameof(BusSerializeLobbyBeginRunPrefix),
            _ => throw new InvalidDataException(
                $"Message type {messageType.FullName} has no bus serializer prefix.")
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
