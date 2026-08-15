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
                LanConnectTailMessagePatches.Apply(harmony);
                _applied = true;
            }
            catch
            {
                harmony.UnpatchAll(HarmonyId);
                throw;
            }
        }
    }

    internal static void ApplyAtomicForTesting(Harmony harmony, IReadOnlyList<Action<Harmony>> patchSteps)
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
        catch
        {
            harmony.UnpatchAll(harmony.Id);
            throw;
        }
    }
}
