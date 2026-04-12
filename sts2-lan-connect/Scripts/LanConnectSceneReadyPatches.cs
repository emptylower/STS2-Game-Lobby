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
    private static bool _dailyRunReadyPatchPending;

    public static void Apply()
    {
        if (_applied)
        {
            return;
        }

        _applied = true;
        LanConnectInviteButtonPatch.ApplyNativeInvitePatches();
        bool isAndroid = OperatingSystem.IsAndroid();

        TryPatchReady(typeof(NMultiplayerHostSubmenu), nameof(OnHostSubmenuReady), "NMultiplayerHostSubmenu._Ready");
        TryPatchReady(typeof(NJoinFriendScreen), nameof(OnJoinFriendScreenReady), "NJoinFriendScreen._Ready");
        TryPatchReady(typeof(NMultiplayerLoadGameScreen), nameof(OnMultiplayerLoadScreenReady), "NMultiplayerLoadGameScreen._Ready");
        TryPatchReady(typeof(NCustomRunLoadScreen), nameof(OnCustomRunLoadScreenReady), "NCustomRunLoadScreen._Ready");
        TryPatchReady(typeof(NPauseMenu), nameof(OnPauseMenuReady), "NPauseMenu._Ready");
        TryPatchReady(typeof(NMainMenu), nameof(OnMainMenuReady), "NMainMenu._Ready");
        TryPatchReady(typeof(NRemoteLobbyPlayer), nameof(OnRemoteLobbyPlayerReady), "NRemoteLobbyPlayer._Ready");
        TryPatchMainMenuSubmenuLookup();

        if (isAndroid)
        {
            // On Android, patching this screen during mod initialization triggers the game's static ctor too
            // early and poisons later startup. Install the hook lazily once the menu UI is already alive.
            _dailyRunReadyPatchPending = true;
            Log.Info("sts2_lan_connect scene_ready: deferring NDailyRunLoadScreen._Ready patch on Android.");
            Log.Info("sts2_lan_connect scene_ready: skipping NMultiplayerSubmenu._Ready patch on Android.");
            Log.Info("sts2_lan_connect scene_ready: skipping NCharacterSelectScreen._Ready patch on Android.");
            return;
        }

        TryPatchReady(typeof(NMultiplayerSubmenu), nameof(OnMultiplayerSubmenuReady), "NMultiplayerSubmenu._Ready");
        TryPatchReady(typeof(NCharacterSelectScreen), nameof(OnCharacterSelectReady), "NCharacterSelectScreen._Ready");
        TryPatchReady(typeof(NDailyRunLoadScreen), nameof(OnDailyRunLoadScreenReady), "NDailyRunLoadScreen._Ready");
    }

    private static bool TryPatchReady(Type nodeType, string postfixName, string label)
    {
        try
        {
            MethodInfo? target = AccessTools.DeclaredMethod(nodeType, "_Ready");
            if (target == null)
            {
                Log.Warn($"sts2_lan_connect scene_ready: target method not found, skipping patch {label}");
                return false;
            }

            HarmonyInstance.Patch(target, postfix: new HarmonyMethod(typeof(LanConnectSceneReadyPatches), postfixName));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect scene_ready: failed to patch {label}: {ex}");
            return false;
        }
    }

    private static void TryPatchMainMenuSubmenuLookup()
    {
        try
        {
            MethodInfo? target = AccessTools.DeclaredMethod(
                typeof(NMainMenuSubmenuStack),
                nameof(NMainMenuSubmenuStack.GetSubmenuType),
                new[] { typeof(Type) });
            if (target == null)
            {
                Log.Warn("sts2_lan_connect scene_ready: target method not found, skipping patch NMainMenuSubmenuStack.GetSubmenuType(Type)");
                return;
            }

            HarmonyInstance.Patch(target, postfix: new HarmonyMethod(typeof(LanConnectSceneReadyPatches), nameof(OnMainMenuSubmenuResolved)));
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect scene_ready: failed to patch NMainMenuSubmenuStack.GetSubmenuType(Type): {ex}");
        }
    }

    private static void EnsureDeferredAndroidPatches()
    {
        if (!_dailyRunReadyPatchPending)
        {
            return;
        }

        if (!TryPatchReady(typeof(NDailyRunLoadScreen), nameof(OnDailyRunLoadScreenReady), "NDailyRunLoadScreen._Ready"))
        {
            return;
        }

        _dailyRunReadyPatchPending = false;
        Log.Info("sts2_lan_connect scene_ready: applied deferred NDailyRunLoadScreen._Ready patch.");
    }

    private static void OnMainMenuSubmenuResolved(Type __0, NSubmenu __result)
    {
        EnsureDeferredAndroidPatches();
        if (__0 != typeof(NMultiplayerSubmenu) || __result is not NMultiplayerSubmenu submenu)
        {
            return;
        }

        MultiplayerSubmenuPatches.ScheduleEnsureLobbyEntry(submenu, "main_menu_stack_get_submenu_type");
        LanConnectLobbyRuntime.Instance?.OnMultiplayerSubmenuReady(submenu);
    }

    private static void OnHostSubmenuReady(NMultiplayerHostSubmenu __instance)
    {
        EnsureDeferredAndroidPatches();
        HostSubmenuPatches.ScheduleEnsureLanHostButton(__instance, "ready_postfix");
    }

    private static void OnJoinFriendScreenReady(NJoinFriendScreen __instance)
    {
        EnsureDeferredAndroidPatches();
        JoinFriendScreenPatches.ScheduleEnsureLanJoinControls(__instance, "ready_postfix");
    }

    private static void OnMultiplayerSubmenuReady(NMultiplayerSubmenu __instance)
    {
        EnsureDeferredAndroidPatches();
        MultiplayerSubmenuPatches.ScheduleEnsureLobbyEntry(__instance, "ready_postfix");
        LanConnectLobbyRuntime.Instance?.OnMultiplayerSubmenuReady(__instance);
    }

    private static void OnMultiplayerLoadScreenReady(NMultiplayerLoadGameScreen __instance)
    {
        EnsureDeferredAndroidPatches();
        LanConnectContinueRunLobbyAutoPublisher.ScheduleEnsureAutoPublish(__instance, "ready_postfix");
    }

    private static void OnCustomRunLoadScreenReady(NCustomRunLoadScreen __instance)
    {
        EnsureDeferredAndroidPatches();
        LanConnectContinueRunLobbyAutoPublisher.ScheduleEnsureAutoPublish(__instance, "ready_postfix");
    }

    private static void OnDailyRunLoadScreenReady(NDailyRunLoadScreen __instance)
    {
        LanConnectContinueRunLobbyAutoPublisher.ScheduleEnsureAutoPublish(__instance, "ready_postfix");
    }

    private static void OnCharacterSelectReady(NCharacterSelectScreen __instance)
    {
        EnsureDeferredAndroidPatches();
        LanConnectInviteButtonPatch.ScheduleEnsureInviteButton(__instance, "ready_postfix");
    }

    private static void OnPauseMenuReady(NPauseMenu __instance)
    {
        EnsureDeferredAndroidPatches();
        PauseMenuPatches.ScheduleEnsureRoomManagementButton(__instance, "ready_postfix");
    }

    private static void OnMainMenuReady(NMainMenu __instance)
    {
        EnsureDeferredAndroidPatches();
        LanConnectLobbyRuntime.Instance?.OnMainMenuReady(__instance);
    }

    private static void OnRemoteLobbyPlayerReady(NRemoteLobbyPlayer __instance)
    {
        EnsureDeferredAndroidPatches();
        LanConnectRemoteLobbyPlayerPatches.RegisterAndRefresh(__instance, "ready_postfix");
    }
}
