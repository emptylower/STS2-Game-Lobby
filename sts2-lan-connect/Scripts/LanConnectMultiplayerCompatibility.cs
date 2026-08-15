using System;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectMultiplayerCompatibility
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            LanConnectProtocolPatchDispatcher.Apply();

            Log.Info(
                $"sts2_lan_connect multiplayer compatibility ready. effectiveMaxPlayers={GetEffectiveMaxPlayers()} " +
                $"compatSlotBits={LanConnectConstants.ExtendedSlotIdBits} " +
                $"compatLobbyBits={LanConnectConstants.ExtendedLobbyListBits} " +
                $"matrix={LanConnectCompatibilityMatrix.DescribeCurrentPolicy()}");
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect failed to initialize multiplayer compatibility patches: {ex}");
            _initialized = false;
            throw;
        }
    }

    public static int GetEffectiveMaxPlayers()
    {
        int? configValue = LanConnectConfig.MaxPlayers;
        if (configValue is > 0)
        {
            return Math.Clamp(configValue.Value, LanConnectConstants.MinMaxPlayers, LanConnectConstants.MaxMaxPlayers);
        }

        int? externalValue = LanConnectExternalModDetection.TryReadExternalMaxPlayers();
        if (externalValue is > 0)
        {
            return Math.Clamp(externalValue.Value, LanConnectConstants.MinMaxPlayers, LanConnectConstants.MaxMaxPlayers);
        }

        return LanConnectConstants.DefaultMaxPlayers;
    }
}
