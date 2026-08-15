using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectRitsuLibLobbyCompatibility
{
    private static bool _loggedPublicCarrier;

    internal static void Apply(Harmony harmony)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        if (_loggedPublicCarrier)
        {
            return;
        }

        _loggedPublicCarrier = true;
        Log.Info(
            "sts2_lan_connect ritsulib_compatibility: public typed sidecar carrier enabled; " +
            "registration is resolved lazily when RitsuLib is present.");
    }

    internal static void TrackLobbyNetService(INetGameService netService) =>
        ArgumentNullException.ThrowIfNull(netService);

    internal static void ReleaseLobbyNetService(INetGameService netService) =>
        ArgumentNullException.ThrowIfNull(netService);

    internal static void Tick(INetGameService? netService)
    {
    }
}
