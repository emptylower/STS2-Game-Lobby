using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectSerializationPatches
{
    private static readonly Harmony HarmonyInstance = new("sts2_lan_connect.serialization");
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
        AccessTools.Method(typeof(LanConnectProtocolProfiles), nameof(LanConnectProtocolProfiles.GetActiveSlotIdBitWidth));

    private static readonly MethodInfo? GetActiveLobbyListBitWidth =
        AccessTools.Method(typeof(LanConnectProtocolProfiles), nameof(LanConnectProtocolProfiles.GetActiveLobbyListBitWidth));

    public static void Apply()
    {
        if (_applied)
        {
            return;
        }

        _applied = true;

        if (LanConnectExternalModDetection.IsRmpModLoaded)
        {
            Log.Info("sts2_lan_connect serialization: RMP mod detected, skipping serialization patches.");
            return;
        }

        PatchLobbyPlayerSlotId();
        PatchClientLobbyJoinResponseList();
        PatchLobbyBeginRunList();

        Log.Info(
            $"sts2_lan_connect serialization: patches applied={_patchedCount}, failed={_failedCount}. " +
            $"activeProfile={LanConnectProtocolProfiles.GetActiveProfile()}, slotId=dynamic, lobbyList=dynamic");
    }

    private static void TrySafePatch(MethodInfo? target, string transpilerName, string label)
    {
        if (target == null)
        {
            Log.Warn($"sts2_lan_connect serialization: target method not found, skipping patch: {label}");
            _failedCount++;
            return;
        }

        try
        {
            HarmonyInstance.Patch(target, transpiler: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), transpilerName));
            _patchedCount++;
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect serialization: failed to patch {label}: {ex}");
            _failedCount++;
        }
    }

    private static void PatchLobbyPlayerSlotId()
    {
        TrySafePatch(
            AccessTools.Method(typeof(LobbyPlayer), nameof(LobbyPlayer.Serialize)),
            nameof(TranspileLobbyPlayerSerialize),
            "LobbyPlayer.Serialize");
        TrySafePatch(
            AccessTools.Method(typeof(LobbyPlayer), nameof(LobbyPlayer.Deserialize)),
            nameof(TranspileLobbyPlayerDeserialize),
            "LobbyPlayer.Deserialize");
    }

    private static void PatchClientLobbyJoinResponseList()
    {
        TrySafePatch(
            AccessTools.Method(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Serialize)),
            nameof(TranspileJoinResponseSerialize),
            "ClientLobbyJoinResponseMessage.Serialize");
        TrySafePatch(
            AccessTools.Method(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Deserialize)),
            nameof(TranspileJoinResponseDeserialize),
            "ClientLobbyJoinResponseMessage.Deserialize");
    }

    private static void PatchLobbyBeginRunList()
    {
        TrySafePatch(
            AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Serialize)),
            nameof(TranspileBeginRunSerialize),
            "LobbyBeginRunMessage.Serialize");
        TrySafePatch(
            AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Deserialize)),
            nameof(TranspileBeginRunDeserialize),
            "LobbyBeginRunMessage.Deserialize");
    }

    // ReSharper disable UnusedMember.Local — invoked by Harmony via reflection

    private static IEnumerable<CodeInstruction> TranspileLobbyPlayerSerialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCallWithProvider(instructions,
            WriteIntWithBits,
            LanConnectConstants.VanillaSlotIdBits,
            GetActiveSlotIdBitWidth,
            nameof(TranspileLobbyPlayerSerialize));

    private static IEnumerable<CodeInstruction> TranspileLobbyPlayerDeserialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCallWithProvider(instructions,
            ReadIntWithBits,
            LanConnectConstants.VanillaSlotIdBits,
            GetActiveSlotIdBitWidth,
            nameof(TranspileLobbyPlayerDeserialize));

    private static IEnumerable<CodeInstruction> TranspileJoinResponseSerialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCallWithProvider(instructions,
            WriteListWithBits,
            LanConnectConstants.VanillaLobbyListBits,
            GetActiveLobbyListBitWidth,
            nameof(TranspileJoinResponseSerialize));

    private static IEnumerable<CodeInstruction> TranspileJoinResponseDeserialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCallWithProvider(instructions,
            ReadListWithBits,
            LanConnectConstants.VanillaLobbyListBits,
            GetActiveLobbyListBitWidth,
            nameof(TranspileJoinResponseDeserialize));

    private static IEnumerable<CodeInstruction> TranspileBeginRunSerialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCallWithProvider(instructions,
            WriteListWithBits,
            LanConnectConstants.VanillaLobbyListBits,
            GetActiveLobbyListBitWidth,
            nameof(TranspileBeginRunSerialize));

    private static IEnumerable<CodeInstruction> TranspileBeginRunDeserialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCallWithProvider(instructions,
            ReadListWithBits,
            LanConnectConstants.VanillaLobbyListBits,
            GetActiveLobbyListBitWidth,
            nameof(TranspileBeginRunDeserialize));

    // ReSharper restore UnusedMember.Local
}
