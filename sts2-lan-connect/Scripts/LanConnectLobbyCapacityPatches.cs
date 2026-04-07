using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectLobbyCapacityPatches
{
    private static readonly FieldInfo? MaxPlayersField =
        AccessTools.Field(typeof(StartRunLobby), "<MaxPlayers>k__BackingField");

    public static void Apply(Harmony harmony)
    {
        int applied = 0;
        int skipped = 0;
        int failed = 0;

        MethodInfo? startENet = AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartENetHost));
        TrySafePatch(harmony, startENet, "StartENetHost",
            ref applied, ref skipped, ref failed,
            prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartENetHostPrefix)));

        if (OperatingSystem.IsAndroid())
        {
            Log.Info("sts2_lan_connect gameplay: skipping StartSteamHost patch on Android.");
            skipped++;
        }
        else
        {
            MethodInfo? startSteam = AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartSteamHost));
            TrySafePatch(harmony, startSteam, "StartSteamHost",
                ref applied, ref skipped, ref failed,
                prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartSteamHostPrefix)));
        }

        ConstructorInfo? lobbyCtor = AccessTools.Constructor(typeof(StartRunLobby),
            new[] { typeof(GameMode), typeof(INetGameService), typeof(IStartRunLobbyListener), typeof(int) });
        TrySafePatch(harmony, lobbyCtor, "StartRunLobby.ctor",
            ref applied, ref skipped, ref failed,
            postfix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartRunLobbyCtorPostfix)));

        MethodInfo? onConnected = AccessTools.Method(typeof(StartRunLobby), "OnConnectedToClientAsHost");
        TrySafePatch(harmony, onConnected, "OnConnectedToClientAsHost",
            ref applied, ref skipped, ref failed,
            prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(SyncMaxPlayersPrefix)));

        MethodInfo? handleJoin = AccessTools.Method(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage");
        TrySafePatch(harmony, handleJoin, "HandleClientLobbyJoinRequestMessage",
            ref applied, ref skipped, ref failed,
            prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(SyncMaxPlayersPrefix)));

        Log.Info($"sts2_lan_connect gameplay: lobby capacity patches applied={applied}, skipped={skipped}, failed={failed}.");
    }

    private static void TrySafePatch(
        Harmony harmony,
        MethodBase? target,
        string label,
        ref int applied,
        ref int skipped,
        ref int failed,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null)
    {
        if (target == null)
        {
            Log.Warn($"sts2_lan_connect gameplay: capacity patch target not found, skipping: {label}.");
            skipped++;
            return;
        }

        try
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            Log.Info($"sts2_lan_connect gameplay: capacity: patched {label}.");
            applied++;
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect gameplay: capacity patch failed for {label}: {ex.Message}");
            failed++;
        }
    }

    // ReSharper disable UnusedMember.Local

    private static void StartENetHostPrefix(ref int maxClients)
    {
        int effective = LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();
        maxClients = Math.Max(maxClients, effective);
    }

    private static void StartSteamHostPrefix(ref int maxClients)
    {
        int effective = LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();
        maxClients = Math.Max(maxClients, effective);
    }

    private static void StartRunLobbyCtorPostfix(StartRunLobby __instance, INetGameService netService)
    {
        int effective = LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();
        if (netService.Type == NetGameType.Host
            && __instance.MaxPlayers < effective
            && MaxPlayersField != null)
        {
            MaxPlayersField.SetValue(__instance, effective);
        }
    }

    private static void SyncMaxPlayersPrefix(StartRunLobby __instance)
    {
        if (MaxPlayersField == null || __instance.NetService.Type != NetGameType.Host)
        {
            return;
        }

        int effective = LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();
        if (__instance.MaxPlayers != effective)
        {
            MaxPlayersField.SetValue(__instance, effective);
        }
    }

    // ReSharper restore UnusedMember.Local
}
