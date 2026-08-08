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

internal sealed record LanConnectSaveRepairContext(
    int ProfileId,
    string VanillaSaveDir,
    string ModdedProfileDir,
    string ModdedSaveDir,
    string BackupDir,
    Func<LanConnectSaveRepairBindingInspection> InspectBinding,
    Func<LanConnectSaveRepairValidation> Validate,
    Action<string, string> Log);

internal sealed record LanConnectSaveRepairBindingInspection(
    bool RunLoaded,
    string SaveKey,
    bool HasBinding);

internal sealed record LanConnectSaveRepairValidation(
    bool Success,
    string Message);

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
            static (_, _) => throw new InvalidOperationException(
                "Save-repair binding coordinator must not persist save bindings."));
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

        return RepairCurrentProfile(
            new LanConnectSaveRepairContext(
                profileId,
                vanillaSaveDir,
                moddedProfileDir,
                moddedSaveDir,
                backupDir,
                InspectCurrentBinding,
                ValidateCurrentSave,
                (source, extra) => LanConnectSaveDiagnostics.LogNow(source, extra)));
    }

    internal static LanConnectSaveRepairResult RepairCurrentProfile(LanConnectSaveRepairContext context)
    {
        context.Log(
            "save_repair:begin",
            $"profile={context.ProfileId}, vanillaSaveDir={context.VanillaSaveDir}, moddedSaveDir={context.ModdedSaveDir}");

        if (!Directory.Exists(context.VanillaSaveDir))
        {
            return new LanConnectSaveRepairResult
            {
                Success = false,
                Message = $"修复失败：未找到原版存档目录 {context.VanillaSaveDir}"
            };
        }

        int filesCopied = 0;
        bool backupCreated = BackupProfileIfNeeded(context.ModdedProfileDir, context.BackupDir);
        Directory.CreateDirectory(context.ModdedSaveDir);

        foreach (string sourceFile in Directory.GetFiles(context.VanillaSaveDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(context.VanillaSaveDir, sourceFile);
            string destinationFile = Path.Combine(context.ModdedSaveDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            if (!File.Exists(destinationFile) || File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(destinationFile))
            {
                File.Copy(sourceFile, destinationFile, overwrite: true);
                filesCopied++;
            }
        }

        string bindingSummary;
        LanConnectSaveRepairBindingInspection bindingInspection = context.InspectBinding();
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

        LanConnectSaveRepairValidation validation = context.Validate();

        context.Log(
            "save_repair:finish",
            $"profile={context.ProfileId}, filesCopied={filesCopied}, backupCreated={backupCreated}, validation={(validation.Success ? "ok" : "failed")}");

        return new LanConnectSaveRepairResult
        {
            Success = validation.Success,
            Message = $"{validation.Message}\n备份：{(backupCreated ? context.BackupDir : "当前 modded profile 无旧文件，无需备份")}\n同步文件数：{filesCopied}\n{bindingSummary}"
        };
    }

    private static LanConnectSaveRepairBindingInspection InspectCurrentBinding()
    {
        LanConnectRunBindingCoordinator<SerializableRun>.RepairBindingInspection inspection =
            BindingCoordinatorHolder.Instance.InspectRepairBinding();
        return new LanConnectSaveRepairBindingInspection(
            inspection.RunLoaded,
            inspection.SaveKey,
            inspection.HasBinding);
    }

    private static LanConnectSaveRepairValidation ValidateCurrentSave()
    {
        if (!SaveManager.Instance.HasMultiplayerRunSave)
        {
            return new LanConnectSaveRepairValidation(
                true,
                "修复完成：当前没有多人续局存档，已完成备份与 vanilla -> modded 同步。");
        }

        if (LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(
                out SerializableRun? repairedRun,
                out string repairedFailureReason)
            && repairedRun != null)
        {
            return new LanConnectSaveRepairValidation(
                true,
                $"修复完成：多人存档重检成功，saveKey={LanConnectMultiplayerSaveRoomBinding.BuildSaveKey(repairedRun)}");
        }

        return new LanConnectSaveRepairValidation(
            false,
            $"修复完成，但多人存档重检仍失败：{repairedFailureReason}");
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
