using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private static readonly FieldInfo? MultiplayerSubmenuLoadingOverlayField = typeof(NMultiplayerSubmenu).GetField("_loadingOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? MultiplayerSubmenuStackField = typeof(NSubmenu).GetField("_stack", BindingFlags.Instance | BindingFlags.NonPublic);

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

            LanConnectResolvedRoomBinding binding = LanConnectMultiplayerSaveRoomBinding.Resolve(run);
            GD.Print(
                $"sts2_lan_connect save_compat: preserving host binding during ENet safe load. saveKey={binding.SaveKey}, hostChannel={LanConnectHostChannels.DescribePersisted(binding.HostChannel)}");

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

    public static bool TryResolveMultiplayerSubmenuContext(
        NMultiplayerSubmenu submenu,
        out Control? loadingOverlay,
        out NSubmenuStack? stack)
    {
        loadingOverlay = MultiplayerSubmenuLoadingOverlayField?.GetValue(submenu) as Control;
        stack = MultiplayerSubmenuStackField?.GetValue(submenu) as NSubmenuStack;
        return loadingOverlay != null && stack != null;
    }

    public static bool TryStartLoadedRunAsLanHostFromSubmenu(NMultiplayerSubmenu submenu)
    {
        if (!TryResolveMultiplayerSubmenuContext(submenu, out Control? loadingOverlay, out NSubmenuStack? stack)
            || loadingOverlay == null
            || stack == null)
        {
            return false;
        }

        TaskHelper.RunSafely(StartLoadedRunAsLanHostAsync(loadingOverlay, stack));
        return true;
    }

    public static async Task AbandonCurrentRunAsync(NMultiplayerSubmenu submenu)
    {
        if (!await ConfirmPermanentAbandonAsync(submenu))
        {
            return;
        }

        if (!TryLoadSafeCurrentRun(out SerializableRun? run, out string failureReason) || run == null)
        {
            Log.Error($"ERROR: Refusing to delete unreadable multiplayer save: {failureReason}");
            GD.Print($"sts2_lan_connect save_compat: abandon blocked because save load failed reason={failureReason}");
            LanConnectPopupUtil.ShowInfo("无法安全读取当前多人存档，因此没有执行删除。请先复制调试报告并联系开发者。");
            return;
        }

        string sourceSavePath = ProjectSettings.GlobalizePath(
            SaveManager.Instance.GetProfileScopedPath(Path.Combine("saves", "current_run_mp.save")));
        string backupRoot = Path.Combine(LanConnectPaths.ResolveWritableDataDirectory(), "save-backups");
        if (!LanConnectSaveBackup.TryCreate(
                sourceSavePath,
                backupRoot,
                SaveManager.Instance.CurrentProfileId,
                DateTimeOffset.UtcNow,
                out string backupPath,
                out string backupError))
        {
            Log.Error($"ERROR: Refusing to delete multiplayer save because backup failed: {backupError}");
            GD.Print($"sts2_lan_connect save_compat: abandon blocked because backup failed source={sourceSavePath}, reason={backupError}");
            LanConnectPopupUtil.ShowInfo($"删除前备份失败，因此没有删除存档。\n原因：{backupError}");
            return;
        }

        GD.Print($"sts2_lan_connect save_compat: abandon backup created path={backupPath}");
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

        try
        {
            SaveManager.Instance.DeleteCurrentMultiplayerRun();
        }
        catch (Exception ex)
        {
            Log.Error($"ERROR: Failed to delete multiplayer save after backup: {ex}");
            LanConnectPopupUtil.ShowInfo($"存档删除失败；备份仍保留在：\n{backupPath}");
            return;
        }

        string saveKey = LanConnectMultiplayerSaveRoomBinding.BuildSaveKey(run);
        bool removedBinding = LanConnectConfig.RemoveSaveRoomBinding(saveKey);
        GD.Print(
            $"sts2_lan_connect save_compat: abandon completed removedBinding={removedBinding}, saveKey={saveKey}, backupPath={backupPath}");
        submenu.Call(NMultiplayerSubmenu.MethodName.UpdateButtons);
        LanConnectPopupUtil.ShowInfo($"多人存档已放弃。删除前备份保存在：\n{backupPath}");
    }

    private static async Task<bool> ConfirmPermanentAbandonAsync(NMultiplayerSubmenu submenu)
    {
        ConfirmationDialog confirmation = new()
        {
            Name = "LanConnectSafeAbandonConfirmation",
            Title = "确认永久放弃多人存档",
            DialogText =
                "此操作会结束当前多人进度，并删除游戏使用的 current_run_mp.save。\n\n" +
                "LAN Connect 会先在 user://sts2_lan_connect/save-backups/ 创建可恢复备份；如果读取或备份失败，删除会自动取消。",
            OkButtonText = "备份并永久放弃",
            CancelButtonText = "保留存档",
            Exclusive = true,
            Unresizable = false,
            MinSize = new Vector2I(560, 300)
        };
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        confirmation.Confirmed += () => completion.TrySetResult(true);
        confirmation.Canceled += () => completion.TrySetResult(false);
        confirmation.CloseRequested += () => completion.TrySetResult(false);
        submenu.AddChild(confirmation);
        try
        {
            confirmation.PopupCenteredClamped(new Vector2I(680, 380), 0.9f);
            confirmation.GetCancelButton().GrabFocus();
            return await completion.Task;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(confirmation))
            {
                confirmation.QueueFree();
            }
        }
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
