using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectMultiplayerCompatibility
{
    private const int ExpandedLobbyPlayerSlotBits = 8;
    private static readonly Harmony HarmonyInstance = new("sts2_lan_connect.multiplayer_compat");
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            MethodInfo? serialize = typeof(LobbyPlayer).GetMethod(nameof(LobbyPlayer.Serialize), BindingFlags.Public | BindingFlags.Instance);
            MethodInfo? deserialize = typeof(LobbyPlayer).GetMethod(nameof(LobbyPlayer.Deserialize), BindingFlags.Public | BindingFlags.Instance);
            MethodInfo? transpiler = typeof(LanConnectMultiplayerCompatibility).GetMethod(
                nameof(TranspileLobbyPlayerSlotBits),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (serialize != null && transpiler != null)
            {
                HarmonyInstance.Patch(serialize, transpiler: new HarmonyMethod(transpiler));
            }

            if (deserialize != null && transpiler != null)
            {
                HarmonyInstance.Patch(deserialize, transpiler: new HarmonyMethod(transpiler));
            }

            Log.Info(
                $"sts2_lan_connect multiplayer compatibility ready. effectiveMaxPlayers={GetEffectiveMaxPlayers()} lobbyPlayerSlotBits={ExpandedLobbyPlayerSlotBits}");
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect failed to initialize multiplayer compatibility patches: {ex}");
        }
    }

    public static int GetEffectiveMaxPlayers()
    {
        int? configuredValue = TryReadUnlimitedMaxPlayers();
        if (!configuredValue.HasValue)
        {
            return LanConnectConstants.DefaultMaxPlayers;
        }

        return Math.Clamp(configuredValue.Value, LanConnectConstants.DefaultMaxPlayers, 256);
    }

    private static int? TryReadUnlimitedMaxPlayers()
    {
        try
        {
            Type? unlimitedType = Type.GetType("Sts2Unlimited.Sts2Unlimited, sts2unlimited", throwOnError: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(static assembly => assembly.GetType("Sts2Unlimited.Sts2Unlimited", throwOnError: false))
                    .FirstOrDefault(static type => type != null);
            PropertyInfo? property = unlimitedType?.GetProperty("MaxPlayersOverride", BindingFlags.Public | BindingFlags.Static);
            object? value = property?.GetValue(null);
            if (value is int maxPlayers && maxPlayers > 0)
            {
                return maxPlayers;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect could not read Sts2Unlimited max player setting: {ex.Message}");
        }

        return null;
    }

    private static IEnumerable<CodeInstruction> TranspileLobbyPlayerSlotBits(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_2)
            {
                yield return new CodeInstruction(OpCodes.Ldc_I4, ExpandedLobbyPlayerSlotBits);
                continue;
            }

            yield return instruction;
        }
    }
}
