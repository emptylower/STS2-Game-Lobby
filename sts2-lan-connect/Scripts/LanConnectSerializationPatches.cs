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

        try
        {
            PatchLobbyPlayerSlotId();
            PatchClientLobbyJoinResponseList();
            PatchLobbyBeginRunList();

            Log.Info(
                $"sts2_lan_connect serialization: all patches applied. " +
                $"slotId={LanConnectConstants.VanillaSlotIdBits}->{LanConnectConstants.ExtendedSlotIdBits}, " +
                $"lobbyList={LanConnectConstants.VanillaLobbyListBits}->{LanConnectConstants.ExtendedLobbyListBits}");
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect serialization: failed to apply patches: {ex}");
        }
    }

    private static void PatchLobbyPlayerSlotId()
    {
        MethodInfo? serialize = AccessTools.Method(typeof(LobbyPlayer), nameof(LobbyPlayer.Serialize));
        MethodInfo? deserialize = AccessTools.Method(typeof(LobbyPlayer), nameof(LobbyPlayer.Deserialize));

        if (serialize != null)
        {
            HarmonyInstance.Patch(serialize, transpiler: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), nameof(TranspileLobbyPlayerSerialize)));
        }

        if (deserialize != null)
        {
            HarmonyInstance.Patch(deserialize, transpiler: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), nameof(TranspileLobbyPlayerDeserialize)));
        }
    }

    private static void PatchClientLobbyJoinResponseList()
    {
        MethodInfo? serialize = AccessTools.Method(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Serialize));
        MethodInfo? deserialize = AccessTools.Method(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Deserialize));

        if (serialize != null)
        {
            HarmonyInstance.Patch(serialize, transpiler: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), nameof(TranspileJoinResponseSerialize)));
        }

        if (deserialize != null)
        {
            HarmonyInstance.Patch(deserialize, transpiler: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), nameof(TranspileJoinResponseDeserialize)));
        }
    }

    private static void PatchLobbyBeginRunList()
    {
        MethodInfo? serialize = AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Serialize));
        MethodInfo? deserialize = AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Deserialize));

        if (serialize != null)
        {
            HarmonyInstance.Patch(serialize, transpiler: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), nameof(TranspileBeginRunSerialize)));
        }

        if (deserialize != null)
        {
            HarmonyInstance.Patch(deserialize, transpiler: new HarmonyMethod(
                typeof(LanConnectSerializationPatches), nameof(TranspileBeginRunDeserialize)));
        }
    }

    // ReSharper disable UnusedMember.Local — invoked by Harmony via reflection

    private static IEnumerable<CodeInstruction> TranspileLobbyPlayerSerialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
            WriteIntWithBits,
            LanConnectConstants.VanillaSlotIdBits, LanConnectConstants.ExtendedSlotIdBits,
            nameof(TranspileLobbyPlayerSerialize));

    private static IEnumerable<CodeInstruction> TranspileLobbyPlayerDeserialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
            ReadIntWithBits,
            LanConnectConstants.VanillaSlotIdBits, LanConnectConstants.ExtendedSlotIdBits,
            nameof(TranspileLobbyPlayerDeserialize));

    private static IEnumerable<CodeInstruction> TranspileJoinResponseSerialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
            WriteListWithBits,
            LanConnectConstants.VanillaLobbyListBits, LanConnectConstants.ExtendedLobbyListBits,
            nameof(TranspileJoinResponseSerialize));

    private static IEnumerable<CodeInstruction> TranspileJoinResponseDeserialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
            ReadListWithBits,
            LanConnectConstants.VanillaLobbyListBits, LanConnectConstants.ExtendedLobbyListBits,
            nameof(TranspileJoinResponseDeserialize));

    private static IEnumerable<CodeInstruction> TranspileBeginRunSerialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
            WriteListWithBits,
            LanConnectConstants.VanillaLobbyListBits, LanConnectConstants.ExtendedLobbyListBits,
            nameof(TranspileBeginRunSerialize));

    private static IEnumerable<CodeInstruction> TranspileBeginRunDeserialize(IEnumerable<CodeInstruction> instructions)
        => LanConnectTranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
            ReadListWithBits,
            LanConnectConstants.VanillaLobbyListBits, LanConnectConstants.ExtendedLobbyListBits,
            nameof(TranspileBeginRunDeserialize));

    // ReSharper restore UnusedMember.Local
}
