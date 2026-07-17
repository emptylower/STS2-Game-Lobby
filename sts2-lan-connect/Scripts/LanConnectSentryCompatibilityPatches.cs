using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectSentryCompatibilityPatches
{
    private static readonly Harmony HarmonyInstance = new("sts2_lan_connect.sentry_compatibility");
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        TryPatch(
            AccessTools.Method(typeof(SentryService), "DisableGdExtensionIfModded"),
            nameof(DisableGdExtensionIfModdedPrefix),
            "SentryService.DisableGdExtensionIfModded");

        TryPatch(
            AccessTools.Method(typeof(SentryService), nameof(SentryService.AfterGameInit)),
            nameof(AfterGameInitPrefix),
            "SentryService.AfterGameInit");
    }

    private static void TryPatch(MethodInfo? target, string prefixName, string label)
    {
        if (target == null)
        {
            Log.Warn($"sts2_lan_connect sentry_compatibility: target missing: {label}");
            return;
        }

        try
        {
            HarmonyInstance.Patch(
                target,
                prefix: new HarmonyMethod(typeof(LanConnectSentryCompatibilityPatches), prefixName));
            Log.Info($"sts2_lan_connect sentry_compatibility: patched {label}.");
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect sentry_compatibility: failed to patch {label}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool DisableGdExtensionIfModdedPrefix()
    {
        if (!ShouldBypassModdedSentry("disable_gd_extension"))
        {
            return true;
        }

        Log.Info("sts2_lan_connect sentry_compatibility: skipped macOS modded Sentry GDExtension sampling hook.");
        return false;
    }

    private static bool AfterGameInitPrefix()
    {
        if (!ShouldBypassModdedSentry("after_game_init"))
        {
            return true;
        }

        Log.Info("sts2_lan_connect sentry_compatibility: skipped macOS modded Sentry post-init shutdown.");
        return false;
    }

    private static bool ShouldBypassModdedSentry(string context)
    {
        try
        {
            return OperatingSystem.IsMacOS() && ModManager.IsRunningModded();
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect sentry_compatibility: failed modded check during {context}: {ex.Message}");
            return false;
        }
    }
}
