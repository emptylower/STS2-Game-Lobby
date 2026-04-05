using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectGameplayPatches
{
    private static readonly Harmony HarmonyInstance = new("sts2_lan_connect.gameplay");
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        if (LanConnectExternalModDetection.IsRmpModLoaded)
        {
            Log.Info("sts2_lan_connect gameplay: RMP mod detected, skipping gameplay patches.");
            return;
        }

        try
        {
            DifficultyScalingPatches.Apply(HarmonyInstance);
            RestSitePatches.Apply(HarmonyInstance);
            MerchantPatches.Apply(HarmonyInstance);
            TreasurePatches.Apply(HarmonyInstance);
            LanConnectLobbyCapacityPatches.Apply(HarmonyInstance);

            Log.Info("sts2_lan_connect gameplay: all gameplay patches applied.");
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect gameplay: failed to apply patches: {ex}");
        }
    }
}
