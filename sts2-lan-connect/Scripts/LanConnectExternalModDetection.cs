using System;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectExternalModDetection
{
    public static bool IsRmpModLoaded { get; private set; }
    public static bool IsSts2UnlimitedLoaded { get; private set; }

    private static int? _rmpTargetPlayerLimit;
    private static int? _sts2UnlimitedMaxPlayers;

    public static void Detect()
    {
        DetectRmpMod();
        DetectSts2Unlimited();

        Log.Info(
            $"sts2_lan_connect external_mod_detection: rmpLoaded={IsRmpModLoaded} rmpLimit={_rmpTargetPlayerLimit?.ToString() ?? "n/a"}, " +
            $"sts2UnlimitedLoaded={IsSts2UnlimitedLoaded} unlimitedLimit={_sts2UnlimitedMaxPlayers?.ToString() ?? "n/a"}");
    }

    public static int? TryReadExternalMaxPlayers()
    {
        if (_rmpTargetPlayerLimit is > 0)
        {
            return _rmpTargetPlayerLimit;
        }

        if (_sts2UnlimitedMaxPlayers is > 0)
        {
            return _sts2UnlimitedMaxPlayers;
        }

        return null;
    }

    private static void DetectRmpMod()
    {
        try
        {
            Type? configType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(static a => a.GetType("RemoveMultiplayerPlayerLimit.Network.ProtocolConfig", throwOnError: false))
                .FirstOrDefault(static t => t != null);

            if (configType == null)
            {
                return;
            }

            IsRmpModLoaded = true;

            PropertyInfo? prop = configType.GetProperty("TargetPlayerLimit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            object? value = prop?.GetValue(null);
            if (value is int limit && limit > 0)
            {
                _rmpTargetPlayerLimit = limit;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect external_mod_detection: failed to read RMP mod: {ex.Message}");
        }
    }

    private static void DetectSts2Unlimited()
    {
        try
        {
            Type? unlimitedType = Type.GetType("Sts2Unlimited.Sts2Unlimited, sts2unlimited", throwOnError: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(static a => a.GetType("Sts2Unlimited.Sts2Unlimited", throwOnError: false))
                    .FirstOrDefault(static t => t != null);

            if (unlimitedType == null)
            {
                return;
            }

            IsSts2UnlimitedLoaded = true;

            PropertyInfo? prop = unlimitedType.GetProperty("MaxPlayersOverride", BindingFlags.Public | BindingFlags.Static);
            object? value = prop?.GetValue(null);
            if (value is int maxPlayers && maxPlayers > 0)
            {
                _sts2UnlimitedMaxPlayers = maxPlayers;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect external_mod_detection: failed to read Sts2Unlimited mod: {ex.Message}");
        }
    }
}
