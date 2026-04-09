using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectSceneReadyPatches
{
    private static readonly Harmony HarmonyInstance = new("sts2_lan_connect.scene_ready");
    private static bool _applied;

    public static void Apply()
    {
        if (_applied)
        {
            return;
        }

        _applied = true;

        TryPatchReady(typeof(NMultiplayerHostSubmenu), nameof(OnHostSubmenuReady), "NMultiplayerHostSubmenu._Ready");
        TryPatchReady(typeof(NJoinFriendScreen), nameof(OnJoinFriendScreenReady), "NJoinFriendScreen._Ready");
        TryPatchReady(typeof(NMultiplayerSubmenu), nameof(OnMultiplayerSubmenuReady), "NMultiplayerSubmenu._Ready");
        TryPatchReady(typeof(NMultiplayerLoadGameScreen), nameof(OnMultiplayerLoadScreenReady), "NMultiplayerLoadGameScreen._Ready");
        TryPatchReady(typeof(NCustomRunLoadScreen), nameof(OnCustomRunLoadScreenReady), "NCustomRunLoadScreen._Ready");
        TryPatchReady(typeof(NDailyRunLoadScreen), nameof(OnDailyRunLoadScreenReady), "NDailyRunLoadScreen._Ready");
        TryPatchReady(typeof(NCharacterSelectScreen), nameof(OnCharacterSelectReady), "NCharacterSelectScreen._Ready");
        TryPatchReady(typeof(NPauseMenu), nameof(OnPauseMenuReady), "NPauseMenu._Ready");
        TryPatchReady(typeof(NRemoteLobbyPlayer), nameof(OnRemoteLobbyPlayerReady), "NRemoteLobbyPlayer._Ready");
    }

    private static void TryPatchReady(Type nodeType, string postfixName, string label)
    {
        MethodInfo? target = AccessTools.DeclaredMethod(nodeType, "_Ready");
        if (target == null)
        {
            Log.Warn($"sts2_lan_connect scene_ready: target method not found, skipping patch {label}");
            return;
        }

        try
        {
            HarmonyInstance.Patch(target, postfix: new HarmonyMethod(typeof(LanConnectSceneReadyPatches), postfixName));
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect scene_ready: failed to patch {label}: {ex}");
        }
    }

    private static void OnHostSubmenuReady(NMultiplayerHostSubmenu __instance)
    {
        HostSubmenuPatches.ScheduleEnsureLanHostButton(__instance, "ready_postfix");
    }

    private static void OnJoinFriendScreenReady(NJoinFriendScreen __instance)
    {
        JoinFriendScreenPatches.ScheduleEnsureLanJoinControls(__instance, "ready_postfix");
    }

    private static void OnMultiplayerSubmenuReady(NMultiplayerSubmenu __instance)
    {
        MultiplayerSubmenuPatches.ScheduleEnsureLobbyEntry(__instance, "ready_postfix");
    }

    private static void OnMultiplayerLoadScreenReady(NMultiplayerLoadGameScreen __instance)
    {
        LanConnectContinueRunLobbyAutoPublisher.ScheduleEnsureAutoPublish(__instance, "ready_postfix");
    }

    private static void OnCustomRunLoadScreenReady(NCustomRunLoadScreen __instance)
    {
        LanConnectContinueRunLobbyAutoPublisher.ScheduleEnsureAutoPublish(__instance, "ready_postfix");
    }

    private static void OnDailyRunLoadScreenReady(NDailyRunLoadScreen __instance)
    {
        LanConnectContinueRunLobbyAutoPublisher.ScheduleEnsureAutoPublish(__instance, "ready_postfix");
    }

    private static void OnCharacterSelectReady(NCharacterSelectScreen __instance)
    {
        LanConnectInviteButtonPatch.EnsureInviteButton(__instance);
    }

    private static void OnPauseMenuReady(NPauseMenu __instance)
    {
        PauseMenuPatches.ScheduleEnsureRoomManagementButton(__instance, "ready_postfix");
    }

    private static void OnRemoteLobbyPlayerReady(NRemoteLobbyPlayer __instance)
    {
        LanConnectRemoteLobbyPlayerPatches.RegisterAndRefresh(__instance, "ready_postfix");
    }
}
