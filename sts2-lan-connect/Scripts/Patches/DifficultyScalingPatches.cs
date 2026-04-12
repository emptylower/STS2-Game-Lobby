using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Singleton;

namespace Sts2LanConnect.Scripts;

internal static class DifficultyScalingPatches
{
    public static void Apply(Harmony harmony)
    {
        MethodInfo? scaleHp = AccessTools.Method(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer));
        if (scaleHp != null)
        {
            harmony.Patch(scaleHp, prefix: new HarmonyMethod(typeof(DifficultyScalingPatches), nameof(ScaleMonsterHpPrefix)));
        }

        MethodInfo? modifyBlock = AccessTools.Method(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.ModifyBlockMultiplicative));
        if (modifyBlock != null)
        {
            harmony.Patch(modifyBlock, transpiler: new HarmonyMethod(typeof(DifficultyScalingPatches), nameof(PatchPlayersCountTranspiler)));
        }

        MethodInfo? modifyPower = AccessTools.Method(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.ModifyPowerAmountGiven));
        if (modifyPower != null)
        {
            harmony.Patch(modifyPower, transpiler: new HarmonyMethod(typeof(DifficultyScalingPatches), nameof(PatchPlayersCountTranspiler)));
        }

        Log.Info("sts2_lan_connect gameplay: difficulty scaling patches applied.");
    }

    internal static int GetEffectivePlayerCount(int rawCount)
    {
        return LanConnectConfig.DifficultyScalingEnabled ? rawCount : Math.Min(rawCount, 4);
    }

    // ReSharper disable UnusedMember.Local

    private static void ScaleMonsterHpPrefix(ref int playerCount)
    {
        playerCount = GetEffectivePlayerCount(playerCount);
    }

    private static IEnumerable<CodeInstruction> PatchPlayersCountTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo helper = AccessTools.Method(typeof(DifficultyScalingPatches), nameof(GetEffectivePlayerCount));
        FieldInfo? runStateField = AccessTools.Field(typeof(MultiplayerScalingModel), "_runState");

        bool foundRunStateLoad = false;

        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;

            if (!foundRunStateLoad && runStateField != null && instruction.LoadsField(runStateField))
            {
                foundRunStateLoad = true;
                continue;
            }

            if (foundRunStateLoad
                && (instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call)
                && instruction.operand is MethodInfo mi
                && mi.Name == "get_Count"
                && mi.ReturnType == typeof(int))
            {
                yield return new CodeInstruction(OpCodes.Call, helper);
                foundRunStateLoad = false;
            }
        }
    }

    // ReSharper restore UnusedMember.Local
}
