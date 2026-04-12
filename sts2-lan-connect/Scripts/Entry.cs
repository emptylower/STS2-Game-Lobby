using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2LanConnect.Scripts;

[ModInitializer(nameof(Init))]
public static class Entry
{
    public static void Init()
    {
        Log.Info(
            $"sts2_lan_connect init: platform={RuntimeInformation.OSDescription}, " +
            $"arch={RuntimeInformation.ProcessArchitecture}, " +
            $"isAndroid={OperatingSystem.IsAndroid()}, " +
            $"framework={RuntimeInformation.FrameworkDescription}");

        LanConnectConfig.Load();
        LanConnectExternalModDetection.Detect();
        LanConnectMultiplayerCompatibility.Initialize();
        LanConnectGameplayPatches.Initialize();
        LanConnectSceneReadyPatches.Apply();
        LanConnectLobbyRuntime.Install();
        LanConnectRoomChatOverlay.Install();
        Log.Info("sts2_lan_connect initialized with ready hooks.");
    }
}
