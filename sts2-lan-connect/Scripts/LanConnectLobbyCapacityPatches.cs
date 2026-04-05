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
        MethodInfo? startENet = AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartENetHost));
        if (startENet != null)
        {
            harmony.Patch(startENet, prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartENetHostPrefix)));
        }

        MethodInfo? startSteam = AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartSteamHost));
        if (startSteam != null)
        {
            harmony.Patch(startSteam, prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartSteamHostPrefix)));
        }

        ConstructorInfo? lobbyCtor = AccessTools.Constructor(typeof(StartRunLobby),
            new[] { typeof(GameMode), typeof(INetGameService), typeof(IStartRunLobbyListener), typeof(int) });
        if (lobbyCtor != null)
        {
            harmony.Patch(lobbyCtor, postfix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartRunLobbyCtorPostfix)));
        }

        MethodInfo? onConnected = AccessTools.Method(typeof(StartRunLobby), "OnConnectedToClientAsHost");
        if (onConnected != null)
        {
            harmony.Patch(onConnected, prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(SyncMaxPlayersPrefix)));
        }

        MethodInfo? handleJoin = AccessTools.Method(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage");
        if (handleJoin != null)
        {
            harmony.Patch(handleJoin, prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(SyncMaxPlayersPrefix)));
        }

        Log.Info("sts2_lan_connect gameplay: lobby capacity patches applied.");
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
