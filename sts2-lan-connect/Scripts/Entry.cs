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
        // Record who patched before us: with a foreign owner present this early, the load
        // order was foreign-first — the single fact that decides whether the closed-generic
        // poisoning can trigger. Turns the next incident from log archaeology into one lookup.
        try
        {
            bool ritsuLibAssemblyLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(static assembly =>
                string.Equals(assembly.GetName().Name, "STS2-RitsuLib", StringComparison.OrdinalIgnoreCase));
            string[] externalPatchOwners = LanConnectProtocolPatchDispatcher.GetAllExternalPatchOwners();
            diagnostics.RecordInfo(
                "mod_load_order",
                new Dictionary<string, object?>
                {
                    ["ritsulib_assembly_loaded"] = ritsuLibAssemblyLoaded,
                    ["ritsulib_patched_before_us"] = externalPatchOwners.Any(static owner =>
                        owner.Contains("ritsu", StringComparison.OrdinalIgnoreCase)),
                    ["external_patch_owners"] = externalPatchOwners
                });
        }
        catch (Exception exception)
        {
            diagnostics.Warn("mod_load_order", exception);
        }
        diagnostics.RunStage(
            LanConnectStartupStages.TailRuntimeConfigure,
            static () => LanConnectTailMessagePatches.ConfigureRuntime(LanConnectTailMessageRuntime.Shared));
        diagnostics.RunStage(
            LanConnectStartupStages.NativeBusStartupCheck,
            static () =>
            {
                LanConnectNativeBusStartupCheck.Result check = LanConnectNativeBusStartupCheck.Run();
                LanConnectNativeBusStartupCheck.LogDiagnostics(check, patchStackOrder: "lan_connect_first_then_ritsulib");
                if (!check.Ok)
                {
                    LanConnectDegradedMode.Enter(
                        LanConnectDegradedMode.ProtocolPatchConflictCode,
                        $"native_bus_self_check:{check.Reason}");
                }
            });
        diagnostics.RunStage(LanConnectStartupStages.SentryCompatibility, LanConnectSentryCompatibilityPatches.Initialize);
        diagnostics.RunStage(LanConnectStartupStages.AccessibilityBridge, LanConnectAccessibilityBridge.Initialize);
        try
        {
            diagnostics.RunStage(LanConnectStartupStages.MultiplayerCompatibility, LanConnectMultiplayerCompatibility.Initialize);
        }
        catch (Exception exception)
        {
            // Fail-closed means refusing to host/join, not killing the whole mod: the lobby
            // UI (stage 9) must still install so players can see what happened.
            LanConnectDiagnosticException description = LanConnectDiagnosticRedactor.DescribeException(exception);
            LanConnectDegradedMode.Enter(LanConnectDegradedMode.ProtocolPatchConflictCode, description.Fingerprint);
            diagnostics.RecordInfo(
                "degraded_mode_entered",
                new Dictionary<string, object?>
                {
                    ["reason"] = LanConnectDegradedMode.ReasonCode,
                    ["exception_fingerprint"] = description.Fingerprint,
                    ["exception_type"] = description.Type
                });
        }
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
