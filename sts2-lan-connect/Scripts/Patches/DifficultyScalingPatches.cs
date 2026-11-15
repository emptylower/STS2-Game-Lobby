using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;

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

        // 0.113 moved this model out of Models.Singleton and put RunState on its base class.
        // Resolve by name so the same DLL can still load and JIT on both game versions.
        Assembly gameAssembly = typeof(Creature).Assembly;
        Type? scalingModel = gameAssembly.GetType("MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel")
            ?? gameAssembly.GetType("MegaCrit.Sts2.Core.Models.MultiplayerScalingModel");
        if (scalingModel == null)
        {
            Log.Warn("sts2_lan_connect gameplay: multiplayer scaling model unavailable; only HP scaling patched.");
            return;
        }

        MethodInfo? modifyBlock = AccessTools.Method(scalingModel, "ModifyBlockMultiplicative");
        if (modifyBlock != null)
        {
            harmony.Patch(modifyBlock, transpiler: new HarmonyMethod(typeof(DifficultyScalingPatches), nameof(PatchPlayersCountTranspiler)));
        }

        MethodInfo? modifyPower = AccessTools.Method(scalingModel, "ModifyPowerAmountGiven");
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

    internal static IEnumerable<CodeInstruction> PatchPlayersCountTranspiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        MethodInfo helper = AccessTools.Method(typeof(DifficultyScalingPatches), nameof(GetEffectivePlayerCount));
        FieldInfo? runStateField = AccessTools.Field(__originalMethod.DeclaringType, "_runState");
        MethodInfo? runStateGetter = AccessTools.PropertyGetter(__originalMethod.DeclaringType, "RunState");
        int playerCountChain = 0;

        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;

            if ((runStateField != null && instruction.LoadsField(runStateField))
                || (runStateGetter != null && instruction.Calls(runStateGetter)))
            {
                playerCountChain = 1;
                continue;
            }

            if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call)
                && instruction.operand is MethodInfo method)
            {
                if (playerCountChain == 1 && method.Name == "get_Players")
                {
                    playerCountChain = 2;
                    continue;
                }

                if (playerCountChain == 2 && method.Name == "get_Count" && method.ReturnType == typeof(int))
                {
                    yield return new CodeInstruction(OpCodes.Call, helper);
                }
            }

            if (instruction.opcode != OpCodes.Nop)
            {
                playerCountChain = 0;
            }
        }
    }

    // ReSharper restore UnusedMember.Local
}
