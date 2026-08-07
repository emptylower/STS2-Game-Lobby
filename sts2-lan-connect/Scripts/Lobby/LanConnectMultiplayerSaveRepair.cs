using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectSaveRepairResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;
}

internal static class LanConnectMultiplayerSaveRepair
{
    private static class BindingCoordinatorHolder
    {
        // The explicit cctor prevents beforefieldinit from resolving sts2 types until first use.
        static BindingCoordinatorHolder()
        {
        }

        internal static readonly LanConnectRunBindingCoordinator<SerializableRun> Instance = new(
            LoadRunForCoordinator,
            LanConnectMultiplayerSaveRoomBinding.BuildSaveKey,
            LanConnectConfig.TryGetSaveRoomBinding,
            PersistBindingFromCoordinator);
    }

    public static Task<LanConnectSaveRepairResult> RepairCurrentProfileAsync()
    {
        return Task.FromResult(RepairCurrentProfile());
    }

    private static LanConnectSaveRepairResult RepairCurrentProfile()
    {
        int profileId = SaveManager.Instance.CurrentProfileId;
        string userDataRoot = ProjectSettings.GlobalizePath("user://");
        string platformName = UserDataPathProvider.GetPlatformDirectoryName(PlatformUtil.PrimaryPlatform);
        ulong userId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform);
        string userDir = Path.Combine(userDataRoot, platformName, userId.ToString(CultureInfo.InvariantCulture));
        string vanillaProfileDir = Path.Combine(userDir, $"profile{profileId}");
        string moddedProfileDir = Path.Combine(userDir, "modded", $"profile{profileId}");
        string vanillaSaveDir = Path.Combine(vanillaProfileDir, "saves");
        string moddedSaveDir = Path.Combine(moddedProfileDir, "saves");
        string backupDir = Path.Combine(
            userDataRoot,
            "sts2_lan_connect_backups",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
            "repair",
            platformName,
            userId.ToString(CultureInfo.InvariantCulture),
            $"profile{profileId}");

        LanConnectSaveDiagnostics.LogNow(
            "save_repair:begin",
            $"profile={profileId}, vanillaSaveDir={vanillaSaveDir}, moddedSaveDir={moddedSaveDir}");

        if (!Directory.Exists(vanillaSaveDir))
        {
            return new LanConnectSaveRepairResult
            {
                Success = false,
                Message = $"修复失败：未找到原版存档目录 {vanillaSaveDir}"
            };
        }

        int filesCopied = 0;
        bool backupCreated = BackupProfileIfNeeded(moddedProfileDir, backupDir);
        Directory.CreateDirectory(moddedSaveDir);

        foreach (string sourceFile in Directory.GetFiles(vanillaSaveDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(vanillaSaveDir, sourceFile);
            string destinationFile = Path.Combine(moddedSaveDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            if (!File.Exists(destinationFile) || File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(destinationFile))
            {
                File.Copy(sourceFile, destinationFile, overwrite: true);
                filesCopied++;
            }
        }

        string bindingSummary;
        LanConnectRunBindingCoordinator<SerializableRun>.RepairBindingInspection bindingInspection =
            BindingCoordinatorHolder.Instance.InspectRepairBinding();
        if (bindingInspection.RunLoaded)
        {
            bindingSummary = bindingInspection.HasBinding
                ? $"已保留当前多人存档的房间绑定 {bindingInspection.SaveKey}"
                : $"当前多人存档没有已保存的房间绑定 {bindingInspection.SaveKey}";
        }
        else
        {
            bindingSummary = "当前多人存档无法立即解析，未更改房间绑定。";
        }

        string validation;
        bool validationSucceeded;
        if (!SaveManager.Instance.HasMultiplayerRunSave)
        {
            validation = "修复完成：当前没有多人续局存档，已完成备份与 vanilla -> modded 同步。";
            validationSucceeded = true;
        }
        else if (LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(out var repairedRun, out string repairedFailureReason) && repairedRun != null)
        {
            validation = $"修复完成：多人存档重检成功，saveKey={LanConnectMultiplayerSaveRoomBinding.BuildSaveKey(repairedRun)}";
            validationSucceeded = true;
        }
        else
        {
            validation = $"修复完成，但多人存档重检仍失败：{repairedFailureReason}";
            validationSucceeded = false;
        }

        LanConnectSaveDiagnostics.LogNow(
            "save_repair:finish",
            $"profile={profileId}, filesCopied={filesCopied}, backupCreated={backupCreated}, validation={(validationSucceeded ? "ok" : "failed")}");

        return new LanConnectSaveRepairResult
        {
            Success = validationSucceeded,
            Message = $"{validation}\n备份：{(backupCreated ? backupDir : "当前 modded profile 无旧文件，无需备份")}\n同步文件数：{filesCopied}\n{bindingSummary}"
        };
    }

    private static LanConnectRunBindingCoordinator<SerializableRun>.LoadResult LoadRunForCoordinator()
    {
        bool success = LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(
            out SerializableRun? run,
            out string failureReason);
        return new LanConnectRunBindingCoordinator<SerializableRun>.LoadResult(
            success,
            run,
            failureReason);
    }

    private static void PersistBindingFromCoordinator(
        SerializableRun run,
        LanConnectRunBindingCoordinator<SerializableRun>.BindingWrite write)
    {
        LanConnectMultiplayerSaveRoomBinding.PersistHostBinding(
            run,
            write.RoomName,
            write.Password,
            write.GameMode,
            write.HostChannel,
            write.Source);
    }

    private static bool BackupProfileIfNeeded(string sourceProfileDir, string backupProfileDir)
    {
        if (!Directory.Exists(sourceProfileDir))
        {
            return false;
        }

        string[] files = Directory.GetFiles(sourceProfileDir, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            return false;
        }

        foreach (string sourceFile in files)
        {
            string relativePath = Path.GetRelativePath(sourceProfileDir, sourceFile);
            string destinationFile = Path.Combine(backupProfileDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }

        return true;
    }
}
