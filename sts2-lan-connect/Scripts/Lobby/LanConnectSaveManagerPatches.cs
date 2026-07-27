using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectSaveManagerPatches
{
    private static readonly FieldInfo? RunSaveManagerField = typeof(SaveManager).GetField("_runSaveManager", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? LoadMultiplayerRunSaveMethod = RunSaveManagerField?.FieldType.GetMethod("LoadMultiplayerRunSave", BindingFlags.Instance | BindingFlags.Public);

    private static readonly object BaseLibPatchLock = new();
    private static Harmony? _harmony;
    private static bool _baseLibGuardApplied;
    private static bool _assemblyLoadHookRegistered;

    public static void Apply(Harmony harmony)
    {
        _harmony = harmony;
        PatchRenameBrokenMultiplayerRunSaveGuard(harmony);
        PatchBaseLibUnknownCharacterGuard(harmony, "mod_init");
    }

    /// <summary>
    /// Game-side defense in depth: the base game renames current_run_mp.save to *.corrupt whenever
    /// CanonicalizeSave fails, including when a probe merely used a platform (Steam) identity against a
    /// LAN-identity save. Refuse the rename when the save file itself is intact and canonicalizes with a
    /// LAN-aware identity, so misdirected probes can never destroy a resumable multiplayer save.
    /// </summary>
    private static void PatchRenameBrokenMultiplayerRunSaveGuard(Harmony harmony)
    {
        if (RunSaveManagerField == null)
        {
            Log.Warn("sts2_lan_connect save_manager: RunSaveManager field not found; corrupt-save rename guard unavailable.");
            return;
        }

        MethodInfo? renameMethod = AccessTools.Method(RunSaveManagerField.FieldType, "RenameBrokenMultiplayerRunSave");
        if (renameMethod == null)
        {
            Log.Warn("sts2_lan_connect save_manager: RenameBrokenMultiplayerRunSave not found; corrupt-save rename guard unavailable.");
            return;
        }

        harmony.Patch(
            renameMethod,
            prefix: new HarmonyMethod(typeof(LanConnectSaveManagerPatches), nameof(RenameBrokenMultiplayerRunSavePrefix)));
        Log.Info("sts2_lan_connect save_manager: patched RunSaveManager.RenameBrokenMultiplayerRunSave with LAN-identity guard.");
    }

    [HarmonyPriority(Priority.First)]
    private static bool RenameBrokenMultiplayerRunSavePrefix(object __instance, ReadSaveStatus status)
    {
        try
        {
            if (!TryCanonicalizeWithAnyLanIdentity(__instance, out string detail))
            {
                GD.Print($"sts2_lan_connect save_manager: allowing corrupt-save rename status={status}, probe={detail}");
                return true;
            }

            GD.Print(
                $"sts2_lan_connect save_manager: blocked corrupt-save rename status={status}, probe={detail}. " +
                "The multiplayer save canonicalizes with a LAN-aware identity, so it is not corrupt; keeping it on disk.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect save_manager: rename guard probe failed ({ex.GetType().Name}: {ex.Message}); allowing vanilla rename.");
            return true;
        }
    }

    /// <summary>
    /// BaseLib's UnknownCharacterPatches.IgnoreUnknownCoopRun turns SaveManager.HasMultiplayerRunSave into a
    /// full load + CanonicalizeSave using the platform (Steam) local player id. LAN saves store LAN identities,
    /// so that validation always fails and the base game renames the save to *.corrupt. Our prefix answers the
    /// probe with a LAN-aware identity first and skips BaseLib's destructive body whenever the save is valid.
    /// The mod loader may initialize us before BaseLib, so retry when the BaseLib assembly loads later.
    /// </summary>
    private static void PatchBaseLibUnknownCharacterGuard(Harmony harmony, string source)
    {
        lock (BaseLibPatchLock)
        {
            if (_baseLibGuardApplied)
            {
                return;
            }

            Type? guardType = AccessTools.TypeByName("BaseLib.Patches.Compatibility.UnknownCharacterPatches+IgnoreUnknownCoopRun")
                ?? AccessTools.TypeByName("BaseLib.Patches.Compatibility.IgnoreUnknownCoopRun")
                ?? AccessTools.TypeByName("BaseLib.Patches.Compatibility.UnknownCharacterPatches.IgnoreUnknownCoopRun");
            if (guardType == null)
            {
                RegisterBaseLibAssemblyLoadRetry();
                return;
            }

            try
            {
                MethodInfo? guardMethod = AccessTools.Method(guardType, "SkipUnknownCharacter");
                if (guardMethod == null)
                {
                    Log.Warn($"sts2_lan_connect save_manager: BaseLib unknown-character save guard method not found on type={guardType.FullName}.");
                    return;
                }

                harmony.Patch(
                    guardMethod,
                    prefix: new HarmonyMethod(typeof(LanConnectSaveManagerPatches), nameof(BaseLibSkipUnknownCharacterPrefix)));
                _baseLibGuardApplied = true;
                Log.Info($"sts2_lan_connect save_manager: patched BaseLib unknown-character save guard type={guardType.FullName}, source={source}.");
            }
            catch (Exception ex)
            {
                Log.Error($"sts2_lan_connect save_manager: failed to patch BaseLib unknown-character save guard: {ex}");
            }
        }
    }

    private static void RegisterBaseLibAssemblyLoadRetry()
    {
        if (_assemblyLoadHookRegistered)
        {
            return;
        }

        _assemblyLoadHookRegistered = true;
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        Log.Info("sts2_lan_connect save_manager: BaseLib not loaded yet; deferring unknown-character save guard patch until the BaseLib assembly loads.");
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (_baseLibGuardApplied || _harmony == null)
        {
            return;
        }

        string? assemblyName = args.LoadedAssembly.GetName().Name;
        if (!string.Equals(assemblyName, "BaseLib", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            PatchBaseLibUnknownCharacterGuard(_harmony, "assembly_load");
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect save_manager: deferred BaseLib guard patch failed: {ex}");
        }
        finally
        {
            if (_baseLibGuardApplied)
            {
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            }
        }
    }

    [HarmonyPriority(Priority.First)]
    private static bool BaseLibSkipUnknownCharacterPrefix([HarmonyArgument(1)] ref bool saveManagerGetterResult)
    {
        try
        {
            if (RunSaveManagerField?.GetValue(SaveManager.Instance) is not object runSaveManager)
            {
                return true;
            }

            if (!TryCanonicalizeWithAnyLanIdentity(runSaveManager, out string detail))
            {
                GD.Print($"sts2_lan_connect save_manager: LAN multiplayer save guard declined, probe={detail}");
                return true;
            }

            saveManagerGetterResult = true;
            return false;
        }
        catch (Exception ex)
        {
            // Never let a probe failure escape through SaveManager.HasMultiplayerRunSave to its callers.
            Log.Warn($"sts2_lan_connect save_manager: BaseLib guard probe failed ({ex.GetType().Name}: {ex.Message}); deferring to BaseLib.");
            return true;
        }
    }

    /// <summary>
    /// Probes whether the current multiplayer save canonicalizes with any LAN-plausible identity:
    /// the active net session id, the platform id, the LAN host id, then every player id in the save.
    /// Purely in-memory; never renames or deletes anything.
    /// </summary>
    private static bool TryCanonicalizeWithAnyLanIdentity(object runSaveManager, out string detail)
    {
        ReadSaveResult<SerializableRun> readResult = LoadRawMultiplayerRun(runSaveManager);
        if (!readResult.Success || readResult.SaveData == null)
        {
            detail = $"raw_load_failed:{readResult.Status}";
            return false;
        }

        List<ulong> candidates = BuildCandidateLocalPlayerIds(readResult.SaveData);
        List<string> failures = new();
        SerializableRun? save = readResult.SaveData;
        var firstAttempt = true;
        foreach (ulong candidate in candidates)
        {
            if (!firstAttempt)
            {
                // CanonicalizeSave may mutate the run, so probe each candidate against a fresh load.
                ReadSaveResult<SerializableRun> reload = LoadRawMultiplayerRun(runSaveManager);
                if (!reload.Success || reload.SaveData == null)
                {
                    failures.Add($"reload_failed:{reload.Status}");
                    break;
                }

                save = reload.SaveData;
            }

            firstAttempt = false;
            try
            {
                _ = RunManager.CanonicalizeSave(save, candidate);
                detail = $"canonicalized_with:{candidate}";
                return true;
            }
            catch (Exception ex)
            {
                failures.Add($"{candidate}:{ex.GetType().Name}");
            }
        }

        detail = $"canonicalize_failed:[{string.Join(";", failures)}], playerIds={string.Join(",", GetPlayerNetIds(readResult.SaveData))}";
        return false;
    }

    private static List<ulong> GetPlayerNetIds(SerializableRun run)
    {
        // Repaired/hand-edited saves can deserialize with a null Players list or null entries.
        return run.Players?.Where(static player => player != null).Select(static player => player.NetId).ToList()
            ?? new List<ulong>();
    }

    private static ReadSaveResult<SerializableRun> LoadRawMultiplayerRun(object runSaveManager)
    {
        try
        {
            if (LoadMultiplayerRunSaveMethod == null)
            {
                return new ReadSaveResult<SerializableRun>(ReadSaveStatus.Unrecoverable, "RunSaveManager reflection unavailable.");
            }

            object? result = LoadMultiplayerRunSaveMethod.Invoke(runSaveManager, Array.Empty<object>());
            return result as ReadSaveResult<SerializableRun>
                ?? new ReadSaveResult<SerializableRun>(ReadSaveStatus.Unrecoverable, "LoadMultiplayerRunSave returned unexpected result.");
        }
        catch (Exception ex)
        {
            return new ReadSaveResult<SerializableRun>(ReadSaveStatus.Unrecoverable, ex.Message);
        }
    }

    private static List<ulong> BuildCandidateLocalPlayerIds(SerializableRun run)
    {
        List<ulong> candidates = new();
        List<ulong> playerIds = GetPlayerNetIds(run);

        INetGameService? netService = RunManager.Instance.NetService;
        if (RunManager.Instance.IsInProgress
            && netService != null
            && netService.Type.IsMultiplayer()
            && netService.Platform == PlatformType.None
            && netService.IsConnected)
        {
            candidates.Add(netService.NetId);
        }

        ulong platformLocalPlayerId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform);
        if (playerIds.Contains(platformLocalPlayerId))
        {
            candidates.Add(platformLocalPlayerId);
        }

        candidates.AddRange(playerIds);
        return candidates.Distinct().ToList();
    }
}
