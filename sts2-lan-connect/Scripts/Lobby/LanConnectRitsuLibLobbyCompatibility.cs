using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectRitsuLibLobbyCompatibility
{
    private static bool _loggedDisabled;

    internal static void Apply(Harmony harmony)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        if (_loggedDisabled)
        {
            return;
        }

        _loggedDisabled = true;
        Log.Info(
            "sts2_lan_connect ritsulib_compatibility: disabled in v0.6 alpha; " +
            "Ritsu Tail rooms remain fail-closed until the public sidecar carrier gate passes.");
    }

    internal static void TrackLobbyNetService(INetGameService netService) =>
        ArgumentNullException.ThrowIfNull(netService);

    internal static void ReleaseLobbyNetService(INetGameService netService) =>
        ArgumentNullException.ThrowIfNull(netService);

    internal static void Tick(INetGameService? netService)
    {
    }
}
