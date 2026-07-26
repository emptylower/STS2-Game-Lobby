using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// The base game's NJoinFriendScreen.OnSubmenuOpened auto-runs FastMpJoin() whenever Steam is not
/// initialized (always true on Android): a developer shortcut that immediately dials 127.0.0.1:33771
/// over ENet. With no local host, that connect blocks the screen behind a loading overlay until the
/// full ENet timeout pops an error. Suppress it unless the developer explicitly passed the "fastmp"
/// command-line argument, so LAN players get an interactive join screen right away.
/// </summary>
internal static class LanConnectJoinScreenAutoJoinPatches
{
    public static void Apply(Harmony harmony)
    {
        MethodInfo? fastMpJoin = AccessTools.Method(typeof(NJoinFriendScreen), "FastMpJoin");
        if (fastMpJoin == null)
        {
            Log.Warn("sts2_lan_connect join_screen: NJoinFriendScreen.FastMpJoin not found; auto-join suppression unavailable.");
            return;
        }

        harmony.Patch(
            fastMpJoin,
            prefix: new HarmonyMethod(typeof(LanConnectJoinScreenAutoJoinPatches), nameof(FastMpJoinPrefix)));
        Log.Info("sts2_lan_connect join_screen: patched NJoinFriendScreen.FastMpJoin to suppress the localhost auto-join.");
    }

    private static bool FastMpJoinPrefix(ref Task __result)
    {
        if (CommandLineHelper.HasArg("fastmp"))
        {
            return true;
        }

        Log.Info("sts2_lan_connect join_screen: suppressed vanilla FastMpJoin localhost auto-connect; use the LAN/IP join controls instead.");
        __result = Task.CompletedTask;
        return false;
    }
}
