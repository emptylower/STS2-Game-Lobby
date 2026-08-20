using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2LanConnect.Scripts;

[ModInitializer(nameof(Init))]
public static class Entry
{
    public static void Init()
    {
        using LanConnectStartupDiagnostics diagnostics = LanConnectStartupDiagnostics.CreateDefault();

        Log.Info(
            $"sts2_lan_connect init: platform={RuntimeInformation.OSDescription}, " +
            $"arch={RuntimeInformation.ProcessArchitecture}, " +
            $"isAndroid={OperatingSystem.IsAndroid()}, " +
            $"framework={RuntimeInformation.FrameworkDescription}");

        diagnostics.RunStage(LanConnectStartupStages.ConfigLoad, LanConnectConfig.Load);
        diagnostics.RunStage(LanConnectStartupStages.ExternalModDetection, LanConnectExternalModDetection.Detect);
        diagnostics.RunStage(
            LanConnectStartupStages.TailRuntimeConfigure,
            static () => LanConnectTailMessagePatches.ConfigureRuntime(LanConnectTailMessageRuntime.Shared));
        diagnostics.RunStage(LanConnectStartupStages.SentryCompatibility, LanConnectSentryCompatibilityPatches.Initialize);
        diagnostics.RunStage(LanConnectStartupStages.AccessibilityBridge, LanConnectAccessibilityBridge.Initialize);
        diagnostics.RunStage(LanConnectStartupStages.MultiplayerCompatibility, LanConnectMultiplayerCompatibility.Initialize);
        diagnostics.RunStage(LanConnectStartupStages.GameplayPatches, LanConnectGameplayPatches.Initialize);
        diagnostics.RunStage(LanConnectStartupStages.SceneReadyPatches, LanConnectSceneReadyPatches.Apply);
        diagnostics.RunStage(
            LanConnectStartupStages.LobbyRuntime,
            static () => LanConnectLobbyRuntime.Install(enableItemLinkCapture: true));
        diagnostics.RunStage(LanConnectStartupStages.RoomChatOverlay, LanConnectRoomChatOverlay.Install);
        diagnostics.Complete();
        Log.Info("sts2_lan_connect initialized with ready hooks.");
    }
}
