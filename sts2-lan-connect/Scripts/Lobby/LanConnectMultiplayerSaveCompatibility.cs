using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Daily;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectMultiplayerSaveCompatibility
{
    private static bool _cachedInterceptDecision;
    private static int _cachedProfileId = -1;
    private static bool _cachedHasRunSave;
    private static long _cachedSaveWriteTicks = long.MinValue;

    public static bool ShouldInterceptOfficialLoadButtons()
    {
        if (PlatformUtil.PrimaryPlatform == PlatformType.None)
        {
            return false;
        }

        bool hasRunSave = SaveManager.Instance.HasMultiplayerRunSave;
        int profileId = SaveManager.Instance.CurrentProfileId;
        string globalSavePath = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(Path.Combine("saves", "current_run_mp.save")));
        long saveWriteTicks = File.Exists(globalSavePath)
            ? File.GetLastWriteTimeUtc(globalSavePath).Ticks
            : long.MinValue;
        if (_cachedProfileId == profileId &&
            _cachedHasRunSave == hasRunSave &&
            _cachedSaveWriteTicks == saveWriteTicks)
        {
            return _cachedInterceptDecision;
        }

        _cachedProfileId = profileId;
        _cachedHasRunSave = hasRunSave;
        _cachedSaveWriteTicks = saveWriteTicks;
        if (!hasRunSave)
        {
            _cachedInterceptDecision = false;
            return false;
        }

        if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(out SerializableRun? run, out string failureReason) ||
            run == null)
        {
            GD.Print($"sts2_lan_connect save_compat: skip load interception because current save could not be loaded safely: {failureReason}");
            _cachedInterceptDecision = false;
            return false;
        }

        ulong steamLocalPlayerId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform);
        bool missingSteamPlayerId = run.Players.All(static player => player != null)
            && run.Players.All(player => player.NetId != steamLocalPlayerId);
        _cachedInterceptDecision = missingSteamPlayerId;
        return _cachedInterceptDecision;
    }

    public static Task StartLoadedRunAsLanHostAsync(Control loadingOverlay, NSubmenuStack stack)
    {
        if (!TryLoadSafeCurrentRun(out SerializableRun? run, out string failureReason) || run == null)
        {
            Log.Warn($"sts2_lan_connect save_compat: safe load failed before host start. reason={failureReason}");
            ShowInvalidSavePopup();
            return Task.CompletedTask;
        }

        loadingOverlay.Visible = true;
        try
        {
            NetHostGameService netService = new();
            int maxPlayers = LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();
            NetErrorInfo? error = netService.StartENetHost(LanConnectConstants.DefaultPort, maxPlayers);
            if (error.HasValue)
            {
                NErrorPopup? popup = NErrorPopup.Create(error.Value);
                if (popup != null)
                {
                    NModalContainer.Instance?.Add(popup);
                }

                return Task.CompletedTask;
            }

            GD.Print(
                $"sts2_lan_connect save_compat: starting loaded multiplayer run via ENet override. players=[{string.Join(",", run.Players.Select(static player => player.NetId))}]");
            PushLoadedRunScreen(stack, netService, run);
        }
        finally
        {
            loadingOverlay.Visible = false;
        }

        return Task.CompletedTask;
    }

    public static async Task AbandonCurrentRunAsync(NMultiplayerSubmenu submenu)
    {
        LocString header = new("main_menu_ui", "ABANDON_RUN_CONFIRMATION.header");
        LocString body = new("main_menu_ui", "ABANDON_RUN_CONFIRMATION.body");
        LocString yesButton = new("main_menu_ui", "GENERIC_POPUP.confirm");
        LocString noButton = new("main_menu_ui", "GENERIC_POPUP.cancel");
        NGenericPopup? popup = NGenericPopup.Create();
        if (popup == null)
        {
            return;
        }

        if (NModalContainer.Instance == null)
        {
            return;
        }

        NModalContainer.Instance.Add(popup);
        if (!await popup.WaitForConfirmation(body, header, noButton, yesButton))
        {
            return;
        }

        if (TryLoadSafeCurrentRun(out SerializableRun? run, out string failureReason) && run != null)
        {
            try
            {
                SaveManager.Instance.UpdateProgressWithRunData(run, victory: false);
                RunHistoryUtilities.CreateRunHistoryEntry(run, victory: false, isAbandoned: true, run.PlatformType);
                if (run.DailyTime.HasValue)
                {
                    int score = ScoreUtility.CalculateScore(run, won: false);
                    _ = TaskHelper.RunSafely(DailyRunUtility.UploadScore(run.DailyTime.Value, score, run.Players));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"ERROR: Failed to upload run history/metrics: {ex}");
            }
        }
        else
        {
            Log.Error($"ERROR: Failed to load multiplayer run save through LAN compatibility path: {failureReason}. Deleting current run...");
        }

        SaveManager.Instance.DeleteCurrentMultiplayerRun();
        submenu.Call(NMultiplayerSubmenu.MethodName.UpdateButtons);
    }

    private static bool TryLoadSafeCurrentRun(out SerializableRun? run, out string failureReason)
    {
        if (LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(out run, out failureReason) && run != null)
        {
            return true;
        }

        run = null;
        return false;
    }

    private static void PushLoadedRunScreen(NSubmenuStack stack, NetHostGameService netService, SerializableRun run)
    {
        if (run.Modifiers.Count > 0)
        {
            if (run.DailyTime.HasValue)
            {
                NDailyRunLoadScreen submenu = stack.GetSubmenuType<NDailyRunLoadScreen>();
                submenu.InitializeAsHost(netService, run);
                stack.Push(submenu);
                return;
            }

            NCustomRunLoadScreen submenuCustom = stack.GetSubmenuType<NCustomRunLoadScreen>();
            submenuCustom.InitializeAsHost(netService, run);
            stack.Push(submenuCustom);
            return;
        }

        NMultiplayerLoadGameScreen submenuStandard = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
        submenuStandard.InitializeAsHost(netService, run);
        stack.Push(submenuStandard);
    }

    private static void ShowInvalidSavePopup()
    {
        NErrorPopup? modalToCreate = NErrorPopup.Create(
            new LocString("main_menu_ui", "INVALID_SAVE_POPUP.title"),
            new LocString("main_menu_ui", "INVALID_SAVE_POPUP.description_run"),
            new LocString("main_menu_ui", "INVALID_SAVE_POPUP.dismiss"),
            showReportBugButton: true);
        if (modalToCreate != null)
        {
            NModalContainer.Instance?.Add(modalToCreate);
            NModalContainer.Instance?.ShowBackstop();
        }
    }
}
