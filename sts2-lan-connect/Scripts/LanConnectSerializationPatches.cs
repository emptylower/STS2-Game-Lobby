using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectSerializationPatches
{
    private const string ClientLobbyJoinResponseTypeName =
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage";
    private const string LobbyBeginRunTypeName =
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyBeginRunMessage";
    private const string PlayersInLobbyFieldName = "playersInLobby";
    private const int ByteBits = 8;

    private static readonly Harmony HarmonyInstance = new(LanConnectProtocolPatchDispatcher.HarmonyId);
    private static bool _applied;
    private static int _patchedCount;
    private static int _failedCount;
    private static MessageBusBoundaryState _beginRunBoundaryState;
    private static MessageBusBoundaryState _joinResponseBoundaryState;

    // Test seam: xUnit hosts cannot enter Godot's GD-based logging (native bootstrap is
    // absent outside the game process), so tests swap these sinks.
    internal static Action<string> LogInfoSink = static message => Log.Info(message);
    internal static Action<string> LogWarnSink = static message => Log.Warn(message);
    internal static Action<string> LogErrorSink = static message => Log.Error(message);

    internal static void ResetLogSinksForTesting()
    {
        LogInfoSink = static message => Log.Info(message);
        LogWarnSink = static message => Log.Warn(message);
        LogErrorSink = static message => Log.Error(message);
    }

    private static readonly MethodInfo? WriteIntWithBits =
        AccessTools.Method(typeof(PacketWriter), nameof(PacketWriter.WriteInt), new[] { typeof(int), typeof(int) });

    private static readonly MethodInfo? ReadIntWithBits =
        AccessTools.Method(typeof(PacketReader), nameof(PacketReader.ReadInt), new[] { typeof(int) });

    private static readonly MethodInfo? WriteListWithBits =
        typeof(PacketWriter).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(static m => m.Name == nameof(PacketWriter.WriteList)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType == typeof(int));

    private static readonly MethodInfo? ReadListWithBits =
        typeof(PacketReader).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(static m => m.Name == nameof(PacketReader.ReadList)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(int));

    private static readonly MethodInfo? GetActiveSlotIdBitWidth =
        AccessTools.Method(typeof(LanConnectCompatWirePatches), nameof(LanConnectCompatWirePatches.GetSlotIdBitWidth));

    private static readonly MethodInfo? GetActiveLobbyListBitWidth =
        AccessTools.Method(typeof(LanConnectCompatWirePatches), nameof(LanConnectCompatWirePatches.GetLobbyListBitWidth));

    private static readonly FieldInfo? NetMessageBusWriter =
        AccessTools.Field(typeof(NetMessageBus), "_writer");

    public static void Apply()
    {
        if (_applied)
        {
            return;
        }

        if (LanConnectExternalModDetection.IsRmpModLoaded)
        {
            _applied = true;
            LogInfoSink("sts2_lan_connect serialization: RMP mod detected, skipping serialization patches.");
            return;
        }

        _patchedCount = 0;
        _failedCount = 0;
        _beginRunBoundaryState = MessageBusBoundaryState.NotAttempted;
        _joinResponseBoundaryState = MessageBusBoundaryState.NotAttempted;

        // native_bus_v1 桌面 seam 会把 SerializeMessage<T> 闭合实例化替换为全优化
        // DynamicMethod，RyuJIT 按原始 IL 内联其中的小结构体 T.Serialize，绕过挂在
        // T.Serialize 上的 compat 位宽 transpiler（0.6.1 起 compat 房主的回归根因）。
        // 桌面因此在消息总线边界为 begin-run / join-response 重新挂强制 prefix；
        // Android gshared 无法编译闭合泛型包装，保持跳过（具体 T.Serialize 仍生效）。
        bool includeCompatMessageBusBoundary = ShouldPatchCompatMessageBusBoundary(
            OperatingSystem.IsAndroid());
        WirePatchPlan patchPlan;
        try
        {
            patchPlan = ResolveRequiredPatchPlan(
                typeof(PacketWriter).Assembly,
                includeCompatMessageBusBoundary);
        }
        catch (Exception ex)
        {
            ResetAppliedAfterExternalRollback();
            _failedCount++;
            string message =
                $"sts2_lan_connect serialization: incompatible game wire schema; no patches were applied. " +
                $"{ex.GetType().Name}: {ex.Message}";
            LogErrorSink(message);
            throw new InvalidOperationException(message, ex);
        }

        foreach (WirePatchTarget target in patchPlan.Targets)
        {
            TrySafePatch(target);
        }

        if (patchPlan.BeginRunMessageBusSerialize != null)
        {
            TrySafeBoundaryPrefixPatch(
                patchPlan.BeginRunMessageBusSerialize,
                nameof(SerializeBeginRunAtMessageBusPrefix),
                $"NetMessageBus.SerializeMessage<{patchPlan.BeginRunMessageType.Name}>",
                "begin_run_boundary_skipped",
                ref _beginRunBoundaryState);
        }
        else
        {
            _beginRunBoundaryState = MessageBusBoundaryState.SkippedAndroid;
            LogInfoSink(
                "sts2_lan_connect serialization: skipped the begin-run message-bus boundary patch " +
                "on Android because Harmony cannot compile closed generic wrappers under gshared.");
        }

        if (patchPlan.JoinResponseMessageBusSerialize != null)
        {
            TrySafeBoundaryPrefixPatch(
                patchPlan.JoinResponseMessageBusSerialize,
                nameof(SerializeJoinResponseAtMessageBusPrefix),
                $"NetMessageBus.SerializeMessage<{patchPlan.JoinResponseMessageType.Name}>",
                "join_response_boundary_skipped",
                ref _joinResponseBoundaryState);
        }
        else
        {
            _joinResponseBoundaryState = MessageBusBoundaryState.SkippedAndroid;
            LogInfoSink(
                "sts2_lan_connect serialization: skipped the join-response message-bus boundary patch " +
                "on Android because Harmony cannot compile closed generic wrappers under gshared.");
        }

        // 两个消息总线边界均为 best-effort：失败只 Warn 并如实上报诊断字段，不并入必需
        // 补丁数。失败仅发生在外部补丁（如 RitsuLib 的泛型声明 SerializePatch<T>）先占用
        // 同一闭合实例化时；compat 本就不支持与 RitsuLib 共存，故无需升级为硬失败。
        int requiredWirePatchCount = patchPlan.Targets.Count;
        if (_patchedCount != requiredWirePatchCount || _failedCount != 0)
        {
            int patchedCount = _patchedCount;
            int failedCount = _failedCount;
            RollBackIncompletePatches();
            string message =
                $"sts2_lan_connect serialization: required wire patches incomplete " +
                $"(applied={patchedCount}/{requiredWirePatchCount}, failed={failedCount}); " +
                "extended multiplayer is unsafe and compatibility initialization was aborted.";
            LogErrorSink(message);
            throw new InvalidOperationException(message);
        }

        _applied = true;

        LogInfoSink(
            $"sts2_lan_connect serialization: patches applied={_patchedCount}, failed={_failedCount}. " +
            $"runtimePlayerType={patchPlan.SlotIdCarrierType.FullName}, " +
            $"activeProfile={LanConnectProtocolProfiles.GetActiveProfile()}, slotId=dynamic, lobbyList=dynamic, " +
            $"beginRunMessageBusBoundary={FormatMessageBusBoundaryState(_beginRunBoundaryState)}, " +
            $"joinResponseMessageBusBoundary={FormatMessageBusBoundaryState(_joinResponseBoundaryState)}");
    }

    internal static bool ShouldPatchCompatMessageBusBoundary(bool isAndroid) => !isAndroid;

    private static void TrySafePatch(WirePatchTarget target)
    {
        try
        {
            HarmonyInstance.Patch(target.Method, transpiler: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), target.TranspilerName));
            _patchedCount++;
        }
        catch (Exception ex)
        {
            LogErrorSink($"sts2_lan_connect serialization: failed to patch {target.Label}: {ex}");
            _failedCount++;
        }
    }

    private static void TrySafeBoundaryPrefixPatch(
        MethodInfo method,
        string prefixName,
        string label,
        string diagnosticEventName,
        ref MessageBusBoundaryState state)
    {
        try
        {
            HarmonyInstance.Patch(method, prefix: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), prefixName));
            state = MessageBusBoundaryState.Patched;
        }
        catch (Exception ex)
        {
            string[] externalOwners = LanConnectProtocolPatchDispatcher.GetExternalPatchOwners(method);
            state = externalOwners.Length > 0
                ? MessageBusBoundaryState.SkippedForeignOwner
                : MessageBusBoundaryState.Failed;
            LanConnectDiagnosticException description = LanConnectDiagnosticRedactor.DescribeException(ex);
            LogWarnSink(
                $"sts2_lan_connect patch_diag: event={diagnosticEventName} " +
                $"target={label} state={FormatMessageBusBoundaryState(state)} " +
                $"exception={description.Type} hresult=0x{description.HResult:X8} " +
                $"fingerprint={description.Fingerprint} " +
                $"external_owners={(externalOwners.Length > 0 ? string.Join(",", externalOwners) : "none")}");
        }
    }

    private enum MessageBusBoundaryState
    {
        NotAttempted,
        Patched,
        SkippedAndroid,
        SkippedForeignOwner,
        Failed
    }

    private static string FormatMessageBusBoundaryState(MessageBusBoundaryState state) => state switch
    {
        MessageBusBoundaryState.Patched => "patched",
        MessageBusBoundaryState.SkippedAndroid => "skipped_android",
        MessageBusBoundaryState.SkippedForeignOwner => "skipped_foreign_owner",
        MessageBusBoundaryState.Failed => "failed",
        _ => "not_attempted"
    };

    internal static string BeginRunBoundaryStateForTesting =>
        FormatMessageBusBoundaryState(_beginRunBoundaryState);

    internal static string JoinResponseBoundaryStateForTesting =>
        FormatMessageBusBoundaryState(_joinResponseBoundaryState);

    private static WirePatchPlan ResolveRequiredPatchPlan(
        Assembly sts2Assembly,
        bool includeCompatMessageBusBoundary)
    {
        Type joinResponseType = RequireType(sts2Assembly, ClientLobbyJoinResponseTypeName);
        Type beginRunType = RequireType(sts2Assembly, LobbyBeginRunTypeName);
        Type slotIdCarrierType = ResolveSlotIdCarrierType(joinResponseType, beginRunType);
        string slotIdCarrierName = slotIdCarrierType.FullName ?? slotIdCarrierType.Name;
        ValidateBeginRunWireSchema(beginRunType);
        ValidateJoinResponseWireSchema(joinResponseType);
        _ = NetMessageBusWriter
            ?? throw new MissingFieldException(typeof(NetMessageBus).FullName, "_writer");
        // 桌面 seam 替换体会按原始 IL 内联 T.Serialize（见 Apply 注释）：两个携带
        // playersInLobby（compat 列表位宽）的消息必须由边界 prefix 显式产出字节；
        // Android gshared 下保持 null，具体 T.Serialize 上的 transpiler 仍然生效。
        MethodInfo? beginRunMessageBusSerialize = includeCompatMessageBusBoundary
            ? ResolveCompatBoundarySerializeTarget(typeof(NetMessageBus), beginRunType)
            : null;
        MethodInfo? joinResponseMessageBusSerialize = includeCompatMessageBusBoundary
            ? ResolveCompatBoundarySerializeTarget(typeof(NetMessageBus), joinResponseType)
            : null;

        WirePatchTarget[] targets =
        {
            new(
                RequireMethod(slotIdCarrierType, "Serialize", typeof(PacketWriter)),
                nameof(TranspileSlotIdCarrierSerialize),
                $"{slotIdCarrierName}.Serialize"),
            new(
                RequireMethod(slotIdCarrierType, "Deserialize", typeof(PacketReader)),
                nameof(TranspileSlotIdCarrierDeserialize),
                $"{slotIdCarrierName}.Deserialize"),
            new(
                RequireMethod(joinResponseType, "Serialize", typeof(PacketWriter)),
                nameof(TranspileJoinResponseSerialize),
                $"{ClientLobbyJoinResponseTypeName}.Serialize"),
            new(
                RequireMethod(joinResponseType, "Deserialize", typeof(PacketReader)),
                nameof(TranspileJoinResponseDeserialize),
                $"{ClientLobbyJoinResponseTypeName}.Deserialize"),
            new(
                RequireMethod(beginRunType, "Serialize", typeof(PacketWriter)),
                nameof(TranspileBeginRunSerialize),
                $"{LobbyBeginRunTypeName}.Serialize"),
            new(
                RequireMethod(beginRunType, "Deserialize", typeof(PacketReader)),
                nameof(TranspileBeginRunDeserialize),
                $"{LobbyBeginRunTypeName}.Deserialize")
        };

        return new WirePatchPlan(
            slotIdCarrierType,
            beginRunType,
            beginRunMessageBusSerialize,
            joinResponseType,
            joinResponseMessageBusSerialize,
            targets);
    }

    internal static MethodInfo ResolveCompatBoundarySerializeTarget(Type messageBusType, Type messageType)
    {
        MethodInfo[] matches = messageBusType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(static method => method.Name == nameof(NetMessageBus.SerializeMessage))
            .Where(static method => method.IsGenericMethodDefinition)
            .Where(static method => method.ReturnType == typeof(byte[]))
            .Where(static method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(ulong)
                    && parameters[1].ParameterType.IsGenericParameter
                    && parameters[2].ParameterType == typeof(int).MakeByRefType();
            })
            .ToArray();
        if (matches.Length != 1)
        {
            throw new MissingMethodException(
                messageBusType.FullName,
                $"SerializeMessage<T>(UInt64, T, out Int32) unique overload; found={matches.Length}");
        }

        return matches[0].MakeGenericMethod(messageType);
    }

    private static void ValidateBeginRunWireSchema(Type beginRunType)
    {
        RequireField(beginRunType, PlayersInLobbyFieldName, static type => IsList(type));
        RequireField(beginRunType, "seed", static type => type == typeof(string));
        RequireField(beginRunType, "modifiers", static type => IsList(type));
        RequireField(beginRunType, "act1", static type => type == typeof(string));
    }

    private static void ValidateJoinResponseWireSchema(Type joinResponseType)
    {
        RequireField(joinResponseType, PlayersInLobbyFieldName, static type => IsList(type));
        RequireField(joinResponseType, "dailyTime", static type => type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(Nullable<>));
        RequireField(joinResponseType, "seed", static type => type == typeof(string));
        RequireField(joinResponseType, "ascension", static type => type == typeof(int));
        RequireField(joinResponseType, "modifiers", static type => IsList(type));
    }

    private static void RequireField(Type declaringType, string fieldName, Func<Type, bool> isExpectedType)
    {
        FieldInfo? field = declaringType.GetField(
            fieldName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || !isExpectedType(field.FieldType))
        {
            throw new MissingFieldException(declaringType.FullName, fieldName);
        }
    }

    private static bool IsList(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    internal static Type ResolveSlotIdCarrierType(Type joinResponseType, Type beginRunType)
    {
        Type joinPlayerType = ResolveListElementType(joinResponseType, PlayersInLobbyFieldName);
        Type beginRunPlayerType = ResolveListElementType(beginRunType, PlayersInLobbyFieldName);
        if (joinPlayerType != beginRunPlayerType)
        {
            throw new InvalidOperationException(
                $"Lobby player wire types disagree: join={joinPlayerType.FullName}, beginRun={beginRunPlayerType.FullName}.");
        }

        FieldInfo? slotIdField = joinPlayerType.GetField(
            "slotId",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (slotIdField?.FieldType != typeof(int))
        {
            throw new MissingFieldException(
                joinPlayerType.FullName,
                "slotId (System.Int32)");
        }

        return joinPlayerType;
    }

    private static Type ResolveListElementType(Type declaringType, string fieldName)
    {
        FieldInfo? field = declaringType.GetField(
            fieldName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            throw new MissingFieldException(declaringType.FullName, fieldName);
        }

        Type fieldType = field.FieldType;
        if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(List<>))
        {
            throw new InvalidOperationException(
                $"{declaringType.FullName}.{fieldName} is not a player List<T>: {fieldType}.");
        }

        return fieldType.GetGenericArguments()[0];
    }

    private static Type RequireType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: false, ignoreCase: false)
            ?? throw new TypeLoadException($"Required type was not found: {typeName}.");
    }

    private static MethodInfo RequireMethod(Type declaringType, string methodName, Type parameterType)
    {
        return AccessTools.Method(declaringType, methodName, new[] { parameterType })
            ?? throw new MissingMethodException(declaringType.FullName, $"{methodName}({parameterType.FullName})");
    }

    private static void RollBackIncompletePatches()
    {
        try
        {
            HarmonyInstance.UnpatchAll(HarmonyInstance.Id);
            LogWarnSink("sts2_lan_connect serialization: rolled back incomplete wire patch set.");
        }
        catch (Exception ex)
        {
            LogErrorSink($"sts2_lan_connect serialization: failed to roll back incomplete wire patches: {ex}");
        }

        ResetAppliedAfterExternalRollback();
    }

    internal static void ResetAppliedAfterExternalRollback()
    {
        _applied = false;
        _patchedCount = 0;
        _failedCount = 0;
        _beginRunBoundaryState = MessageBusBoundaryState.NotAttempted;
        _joinResponseBoundaryState = MessageBusBoundaryState.NotAttempted;
    }

    internal static bool IsAppliedForTesting => _applied;

    internal static void SetAppliedForTesting(bool applied) => _applied = applied;

    // ReSharper disable UnusedMember.Local — invoked by Harmony via reflection

    // 两个边界 prefix 与同一方法上的 tail seam 补丁共存：tail prefix（Priority.First+100）
    // 先运行，compat 下其 __state 恒为 null（PrepareConcreteMessage 对非 tail_v1 profile
    // 直接原样返回），故此处跳过原方法体后 tail 的 postfix 不会补发扩展帧，也不会留下
    // pending 状态；tail_v1 下本 prefix 直接放行，完全走原路径。

    [HarmonyPriority(Priority.First)]
    private static bool SerializeBeginRunAtMessageBusPrefix(
        NetMessageBus __instance,
        ulong senderId,
        LobbyBeginRunMessage message,
        ref int length,
        ref byte[] __result)
    {
        if (!LanConnectCompatWirePatches.ShouldForceCompatWireAtMessageBusBoundary())
        {
            return true;
        }

        PacketWriter writer = RequireBusWriter(__instance);
        int listBitWidth = LanConnectProtocolProfiles.GetActiveLobbyListBitWidth();
        WriteCompatHeaderAndMeasure(
            writer,
            senderId,
            message,
            () => SerializeBeginRunBody(writer, message, listBitWidth),
            ref length,
            ref __result);
        LogInfoSink(
            $"sts2_lan_connect serialization: lobby begin-run forced at message-bus boundary " +
            $"players={message.playersInLobby?.Count ?? 0}, lobbyListBits={listBitWidth}, " +
            $"bodyBytes={length}");
        return false;
    }

    [HarmonyPriority(Priority.First)]
    private static bool SerializeJoinResponseAtMessageBusPrefix(
        NetMessageBus __instance,
        ulong senderId,
        ClientLobbyJoinResponseMessage message,
        ref int length,
        ref byte[] __result)
    {
        if (!LanConnectCompatWirePatches.ShouldForceCompatWireAtMessageBusBoundary())
        {
            return true;
        }

        PacketWriter writer = RequireBusWriter(__instance);
        int listBitWidth = LanConnectProtocolProfiles.GetActiveLobbyListBitWidth();
        WriteCompatHeaderAndMeasure(
            writer,
            senderId,
            message,
            () => SerializeJoinResponseBody(writer, message, listBitWidth),
            ref length,
            ref __result);
        LogInfoSink(
            $"sts2_lan_connect serialization: lobby join-response forced at message-bus boundary " +
            $"players={message.playersInLobby?.Count ?? 0}, lobbyListBits={listBitWidth}, " +
            $"bodyBytes={length}");
        return false;
    }

    private static PacketWriter RequireBusWriter(NetMessageBus messageBus)
    {
        FieldInfo writerField = NetMessageBusWriter
            ?? throw new MissingFieldException(typeof(NetMessageBus).FullName, "_writer");
        return writerField.GetValue(messageBus) as PacketWriter
            ?? throw new InvalidOperationException("NetMessageBus._writer is unavailable.");
    }

    // header（ToId() 字节 + senderId）与 length/__result 的产生方式和原版
    // SerializeMessage<T> 一致：Reset → WriteByte(ToId) → WriteULong(senderId) → body →
    // length = Ceil(BitPosition / 8)（即原版 BytePosition）、__result = Buffer。
    private static void WriteCompatHeaderAndMeasure(
        PacketWriter writer,
        ulong senderId,
        INetMessage message,
        Action writeBody,
        ref int length,
        ref byte[] result)
    {
        writer.Reset();
        writer.WriteByte(checked((byte)message.ToId()));
        writer.WriteULong(senderId);
        writeBody();
        length = checked((int)(((long)writer.BitPosition + ByteBits - 1) / ByteBits));
        result = writer.Buffer;
    }

    internal static void SerializeBeginRunBody(
        PacketWriter writer,
        LobbyBeginRunMessage message,
        int lobbyListBitWidth)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (message.playersInLobby == null)
        {
            throw new InvalidOperationException("Tried to serialize LobbyBeginRunMessage with null player list.");
        }

        writer.WriteList(message.playersInLobby, lobbyListBitWidth);
        writer.WriteString(message.seed);
        writer.WriteList(message.modifiers);
        writer.WriteString(message.act1);
    }

    // 镜像原版 ClientLobbyJoinResponseMessage.Serialize：仅 playersInLobby 的列表位宽
    // 换成 compat 宽度，其余字段（含 ascension 的 5 bit）逐项保持原版写法。
    internal static void SerializeJoinResponseBody(
        PacketWriter writer,
        ClientLobbyJoinResponseMessage message,
        int lobbyListBitWidth)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (message.playersInLobby == null)
        {
            throw new InvalidOperationException(
                "Tried to serialize ClientLobbyJoinResponseMessage with null player list.");
        }

        writer.WriteList(message.playersInLobby, lobbyListBitWidth);
        writer.WriteBool(message.dailyTime.HasValue);
        if (message.dailyTime.HasValue)
        {
            writer.Write(message.dailyTime.Value);
        }

        writer.WriteBool(message.seed != null);
        if (message.seed != null)
        {
            writer.WriteString(message.seed);
        }

        writer.WriteInt(message.ascension, JoinResponseAscensionBits);
        writer.WriteList(message.modifiers);
    }

    private const int JoinResponseAscensionBits = 5;

    private static IEnumerable<CodeInstruction> TranspileSlotIdCarrierSerialize(IEnumerable<CodeInstruction> instructions)
        => ReplaceRequiredBitWidth(instructions,
            WriteIntWithBits,
            LanConnectConstants.VanillaSlotIdBits,
            GetActiveSlotIdBitWidth,
            nameof(TranspileSlotIdCarrierSerialize));

    private static IEnumerable<CodeInstruction> TranspileSlotIdCarrierDeserialize(IEnumerable<CodeInstruction> instructions)
        => ReplaceRequiredBitWidth(instructions,
            ReadIntWithBits,
            LanConnectConstants.VanillaSlotIdBits,
            GetActiveSlotIdBitWidth,
            nameof(TranspileSlotIdCarrierDeserialize));

    private static IEnumerable<CodeInstruction> TranspileJoinResponseSerialize(IEnumerable<CodeInstruction> instructions)
        => ReplaceRequiredBitWidth(instructions,
            WriteListWithBits,
            LanConnectConstants.VanillaLobbyListBits,
            GetActiveLobbyListBitWidth,
            nameof(TranspileJoinResponseSerialize));

    private static IEnumerable<CodeInstruction> TranspileJoinResponseDeserialize(IEnumerable<CodeInstruction> instructions)
        => ReplaceRequiredBitWidth(instructions,
            ReadListWithBits,
            LanConnectConstants.VanillaLobbyListBits,
            GetActiveLobbyListBitWidth,
            nameof(TranspileJoinResponseDeserialize));

    private static IEnumerable<CodeInstruction> TranspileBeginRunSerialize(IEnumerable<CodeInstruction> instructions)
        => ReplaceRequiredBitWidth(instructions,
            WriteListWithBits,
            LanConnectConstants.VanillaLobbyListBits,
            GetActiveLobbyListBitWidth,
            nameof(TranspileBeginRunSerialize));

    private static IEnumerable<CodeInstruction> TranspileBeginRunDeserialize(IEnumerable<CodeInstruction> instructions)
        => ReplaceRequiredBitWidth(instructions,
            ReadListWithBits,
            LanConnectConstants.VanillaLobbyListBits,
            GetActiveLobbyListBitWidth,
            nameof(TranspileBeginRunDeserialize));

    private static IEnumerable<CodeInstruction> ReplaceRequiredBitWidth(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo? targetMethod,
        int expectedBitWidth,
        MethodInfo? providerMethod,
        string patchName)
    {
        MethodInfo resolvedProviderMethod = providerMethod
            ?? throw new InvalidOperationException($"{patchName}: provider method is null.");
        List<CodeInstruction> original = new(instructions);
        int providerCallsBefore = original.Count(instruction =>
            LanConnectTranspilerUtils.IsCallToMethod(instruction, resolvedProviderMethod));
        List<CodeInstruction> patched = LanConnectTranspilerUtils.ReplaceBitWidthBeforeCallWithProvider(
                original,
                targetMethod,
                expectedBitWidth,
                resolvedProviderMethod,
                patchName)
            .ToList();
        int replacements = patched.Count(instruction =>
                LanConnectTranspilerUtils.IsCallToMethod(instruction, resolvedProviderMethod))
            - providerCallsBefore;

        if (replacements != 1)
        {
            throw new InvalidOperationException(
                $"{patchName}: expected exactly one wire bit-width replacement, observed {replacements}.");
        }

        return patched;
    }

    // ReSharper restore UnusedMember.Local

    private readonly record struct WirePatchTarget(MethodInfo Method, string TranspilerName, string Label);

    private readonly record struct WirePatchPlan(
        Type SlotIdCarrierType,
        Type BeginRunMessageType,
        MethodInfo? BeginRunMessageBusSerialize,
        Type JoinResponseMessageType,
        MethodInfo? JoinResponseMessageBusSerialize,
        IReadOnlyList<WirePatchTarget> Targets);
}
