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
            Log.Info("sts2_lan_connect serialization: RMP mod detected, skipping serialization patches.");
            return;
        }

        _patchedCount = 0;
        _failedCount = 0;

        bool includeBeginRunMessageBusBoundary = ShouldPatchBeginRunMessageBusBoundary(
            OperatingSystem.IsAndroid());
        WirePatchPlan patchPlan;
        try
        {
            patchPlan = ResolveRequiredPatchPlan(
                typeof(PacketWriter).Assembly,
                includeBeginRunMessageBusBoundary);
        }
        catch (Exception ex)
        {
            ResetAppliedAfterExternalRollback();
            _failedCount++;
            string message =
                $"sts2_lan_connect serialization: incompatible game wire schema; no patches were applied. " +
                $"{ex.GetType().Name}: {ex.Message}";
            Log.Error(message);
            throw new InvalidOperationException(message, ex);
        }

        foreach (WirePatchTarget target in patchPlan.Targets)
        {
            TrySafePatch(target);
        }

        if (patchPlan.BeginRunMessageBusSerialize != null)
        {
            TrySafeBeginRunPrefixPatch(
                patchPlan.BeginRunMessageBusSerialize,
                nameof(SerializeBeginRunAtMessageBusPrefix),
                $"NetMessageBus.SerializeMessage<{patchPlan.BeginRunMessageType.Name}>");
        }
        else
        {
            Log.Info(
                "sts2_lan_connect serialization: skipped the begin-run message-bus boundary patch " +
                "on Android because Harmony cannot compile closed generic wrappers under gshared.");
        }

        int requiredWirePatchCount = patchPlan.Targets.Count
                                     + (patchPlan.BeginRunMessageBusSerialize == null ? 0 : 1);
        if (_patchedCount != requiredWirePatchCount || _failedCount != 0)
        {
            int patchedCount = _patchedCount;
            int failedCount = _failedCount;
            RollBackIncompletePatches();
            string message =
                $"sts2_lan_connect serialization: required wire patches incomplete " +
                $"(applied={patchedCount}/{requiredWirePatchCount}, failed={failedCount}); " +
                "extended multiplayer is unsafe and compatibility initialization was aborted.";
            Log.Error(message);
            throw new InvalidOperationException(message);
        }

        _applied = true;

        Log.Info(
            $"sts2_lan_connect serialization: patches applied={_patchedCount}, failed={_failedCount}. " +
            $"runtimePlayerType={patchPlan.SlotIdCarrierType.FullName}, " +
            $"activeProfile={LanConnectProtocolProfiles.GetActiveProfile()}, slotId=dynamic, lobbyList=dynamic, " +
            $"beginRunMessageBusBoundary={(patchPlan.BeginRunMessageBusSerialize == null ? "android_gshared_skip" : "patched")}");
    }

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
            Log.Error($"sts2_lan_connect serialization: failed to patch {target.Label}: {ex}");
            _failedCount++;
        }
    }

    private static void TrySafeBeginRunPrefixPatch(MethodInfo method, string prefixName, string label)
    {
        try
        {
            HarmonyInstance.Patch(method, prefix: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), prefixName));
            _patchedCount++;
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect serialization: failed to patch {label}: {ex}");
            _failedCount++;
        }
    }

    internal static bool ShouldPatchBeginRunMessageBusBoundary(bool isAndroid) => !isAndroid;

    private static WirePatchPlan ResolveRequiredPatchPlan(
        Assembly sts2Assembly,
        bool includeBeginRunMessageBusBoundary)
    {
        Type joinResponseType = RequireType(sts2Assembly, ClientLobbyJoinResponseTypeName);
        Type beginRunType = RequireType(sts2Assembly, LobbyBeginRunTypeName);
        Type slotIdCarrierType = ResolveSlotIdCarrierType(joinResponseType, beginRunType);
        string slotIdCarrierName = slotIdCarrierType.FullName ?? slotIdCarrierType.Name;
        ValidateBeginRunWireSchema(beginRunType);
        _ = NetMessageBusWriter
            ?? throw new MissingFieldException(typeof(NetMessageBus).FullName, "_writer");
        MethodInfo? beginRunMessageBusSerialize = null;
        if (includeBeginRunMessageBusBoundary)
        {
            beginRunMessageBusSerialize = ResolveGenericSerializeMessageMethod(
                typeof(NetMessageBus),
                beginRunType);
        }

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
            targets);
    }

    internal static MethodInfo ResolveGenericSerializeMessageMethod(Type messageBusType, Type messageType)
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
            Log.Warn("sts2_lan_connect serialization: rolled back incomplete wire patch set.");
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect serialization: failed to roll back incomplete wire patches: {ex}");
        }

        ResetAppliedAfterExternalRollback();
    }

    internal static void ResetAppliedAfterExternalRollback()
    {
        _applied = false;
        _patchedCount = 0;
        _failedCount = 0;
    }

    internal static bool IsAppliedForTesting => _applied;

    internal static void SetAppliedForTesting(bool applied) => _applied = applied;

    // ReSharper disable UnusedMember.Local — invoked by Harmony via reflection

    [HarmonyPriority(Priority.First)]
    private static bool SerializeBeginRunAtMessageBusPrefix(
        NetMessageBus __instance,
        ulong senderId,
        LobbyBeginRunMessage message,
        ref int length,
        ref byte[] __result)
    {
        FieldInfo writerField = NetMessageBusWriter
            ?? throw new MissingFieldException(typeof(NetMessageBus).FullName, "_writer");
        PacketWriter writer = writerField.GetValue(__instance) as PacketWriter
            ?? throw new InvalidOperationException("NetMessageBus._writer is unavailable.");

        writer.Reset();
        writer.WriteByte(checked((byte)message.ToId()));
        writer.WriteULong(senderId);
        int listBitWidth = LanConnectProtocolProfiles.GetActiveLobbyListBitWidth();
        SerializeBeginRunBody(writer, message, listBitWidth);
        length = checked((int)(((long)writer.BitPosition + ByteBits - 1) / ByteBits));
        __result = writer.Buffer;
        Log.Info(
            $"sts2_lan_connect serialization: lobby begin-run forced at message-bus boundary " +
            $"players={message.playersInLobby?.Count ?? 0}, lobbyListBits={listBitWidth}, " +
            $"bodyBytes={length}");
        return false;
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
        IReadOnlyList<WirePatchTarget> Targets);
}
