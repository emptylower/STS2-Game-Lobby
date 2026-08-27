using System.Diagnostics;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectProtocolPatchDispatcher
{
    internal const string HarmonyId = "sts2_lan_connect.protocol.v1";
    private static readonly object Sync = new();
    private static bool _applied;

    internal static void Apply()
    {
        lock (Sync)
        {
            if (_applied)
            {
                return;
            }

            if (LanConnectExternalModDetection.IsRmpModLoaded)
            {
                _applied = true;
                Log.Info("sts2_lan_connect protocol dispatcher: RMP detected, skipping all LAN protocol patches.");
                return;
            }

            Harmony harmony = new(HarmonyId);
            try
            {
                LanConnectSerializationPatches.Apply();
                if (LanConnectTailRuntimeSupport.IsAvailable)
                {
                    LanConnectTailMessagePatches.Apply(harmony);
                }
                else
                {
                    Log.Info(
                        "sts2_lan_connect protocol dispatcher: tail runtime unavailable, "
                        + $"skipping tail patches: {LanConnectTailRuntimeSupport.Current.UnavailableReason}");
                }

                _applied = true;
            }
            catch (Exception exception)
            {
                RollBackAndRethrowOriginal(harmony, HarmonyId, exception);
            }
        }
    }

    internal static void ApplyAtomicForTesting(
        Harmony harmony,
        IReadOnlyList<Action<Harmony>> patchSteps,
        Action<Harmony, string>? rollback = null,
        bool emitRollbackDiagnostics = true)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(patchSteps);
        try
        {
            foreach (Action<Harmony>? step in patchSteps)
            {
                (step ?? throw new InvalidOperationException("Atomic patch plan contains null."))(harmony);
            }
        }
        catch (Exception exception)
        {
            RollBackAndRethrowOriginal(
                harmony,
                harmony.Id,
                exception,
                rollback,
                emitRollbackDiagnostics);
        }
    }

    internal static bool IsAppliedForTesting => _applied;

    internal static void SetAppliedForTesting(bool applied) => _applied = applied;

    private static void TryResetSerializationPatchesAfterRollback()
    {
        try
        {
            LanConnectSerializationPatches.ResetAppliedAfterExternalRollback();
        }
        catch (FileNotFoundException)
        {
            // Some non-Godot xUnit runs do not copy sts2.dll. Rollback must still clear dispatcher state.
        }
    }

    private static void RollBackAndRethrowOriginal(
        Harmony harmony,
        string owner,
        Exception originalException,
        Action<Harmony, string>? rollback = null,
        bool emitDiagnostics = true)
    {
        ExceptionDispatchInfo original = ExceptionDispatchInfo.Capture(originalException);
        if (emitDiagnostics)
        {
            Log.Error(
                "sts2_lan_connect patch_diag: event=rollback_begin " +
                $"owner={owner} original_exception={originalException.GetType().FullName} " +
                $"hresult={originalException.HResult}");
        }

        try
        {
            if (rollback == null)
            {
                harmony.UnpatchAll(owner);
            }
            else
            {
                rollback(harmony, owner);
            }
        }
        catch (Exception rollbackException)
        {
            if (emitDiagnostics)
            {
                Log.Error(
                    "sts2_lan_connect patch_diag: event=rollback_failure " +
                    $"owner={owner} exception={rollbackException.GetType().FullName} " +
                    $"hresult={rollbackException.HResult}");
            }
        }

        try
        {
            TryResetSerializationPatchesAfterRollback();
        }
        catch (Exception resetException)
        {
            if (emitDiagnostics)
            {
                Log.Error(
                    "sts2_lan_connect patch_diag: event=rollback_state_reset_failure " +
                    $"owner={owner} exception={resetException.GetType().FullName} " +
                    $"hresult={resetException.HResult}");
            }
        }

        _applied = false;
        if (emitDiagnostics)
        {
            try
            {
                (int remainingOwnPatches, string[] externalOwners) = InspectPatchOwners(owner);
                Log.Info(
                    "sts2_lan_connect patch_diag: event=rollback_complete " +
                    $"owner={owner} remaining_owner_patch_count={remainingOwnPatches} " +
                    $"external_owners_preserved={string.Join(",", externalOwners)}");
            }
            catch (Exception inspectionException)
            {
                Log.Warn(
                    "sts2_lan_connect patch_diag: event=rollback_inspection_failure " +
                    $"owner={owner} exception={inspectionException.GetType().FullName}");
            }
        }

        original.Throw();
        throw new UnreachableException();
    }

    internal static string[] GetAllExternalPatchOwners() => InspectPatchOwners(HarmonyId).ExternalOwners;

    internal static string[] GetExternalPatchOwners(System.Reflection.MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);
        Patches? patches = Harmony.GetPatchInfo(method);
        if (patches == null)
        {
            return [];
        }

        return patches.Prefixes
            .Concat(patches.Postfixes)
            .Concat(patches.Transpilers)
            .Concat(patches.Finalizers)
            .Select(static patch => patch.owner)
            .Where(owner => !string.IsNullOrWhiteSpace(owner)
                            && !string.Equals(owner, HarmonyId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static owner => owner, StringComparer.Ordinal)
            .ToArray();
    }

    private static (int RemainingOwnPatches, string[] ExternalOwners) InspectPatchOwners(string owner)
    {
        int remainingOwnPatches = 0;
        HashSet<string> externalOwners = new(StringComparer.Ordinal);
        foreach (System.Reflection.MethodBase method in Harmony.GetAllPatchedMethods())
        {
            Patches? patches = Harmony.GetPatchInfo(method);
            if (patches == null)
            {
                continue;
            }

            foreach (Patch patch in patches.Prefixes
                         .Concat(patches.Postfixes)
                         .Concat(patches.Transpilers)
                         .Concat(patches.Finalizers))
            {
                if (string.Equals(patch.owner, owner, StringComparison.Ordinal))
                {
                    remainingOwnPatches++;
                }
                else if (!string.IsNullOrWhiteSpace(patch.owner))
                {
                    externalOwners.Add(patch.owner);
                }
            }
        }

        return (remainingOwnPatches, externalOwners.OrderBy(static value => value, StringComparer.Ordinal).ToArray());
    }
}
