using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Sts2LanConnect.Scripts;

// The game's ENet continue-run path ignores StartENetHost's error result. Guard
// both the load button (before canonicalization) and StartHost (also used by fastmp).
internal static class LanConnectOfficialContinueRunPatches
{
    public static void ApplyAndroidDeferredEntry(Harmony harmony)
    {
        // Android cannot patch NMultiplayerSubmenu before its Godot scene has
        // initialized. This method returns the created submenu before either the
        // normal load button or fastmp=load can proceed.
        MethodInfo? target = AccessTools.Method(
            typeof(NMainMenu),
            nameof(NMainMenu.OpenMultiplayerSubmenu),
            Type.EmptyTypes);
        if (target == null)
        {
            throw new MissingMethodException("NMainMenu.OpenMultiplayerSubmenu() not found.");
        }

        harmony.Patch(
            target,
            postfix: new HarmonyMethod(
                typeof(LanConnectOfficialContinueRunPatches),
                nameof(InstallAfterSubmenuOpened)));
    }

    private static void InstallAfterSubmenuOpened() =>
        LanConnectGameplayPatches.EnsureAndroidOfficialContinueRunGuards();

    public static void Apply(Harmony harmony)
    {
        List<string> failures = new();
        PatchRequired(
            harmony,
            AccessTools.Method(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu.StartHost),
                new[] { typeof(SerializableRun) }),
            "NMultiplayerSubmenu.StartHost",
            nameof(StartHostPrefix),
            failures);
        PatchRequired(
            harmony,
            AccessTools.Method(typeof(NMultiplayerSubmenu), "StartLoad", new[] { typeof(NButton) }),
            "NMultiplayerSubmenu.StartLoad",
            nameof(StartLoadPrefix),
            failures);

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Required official continue-run guards failed: {string.Join(", ", failures)}.");
        }
    }

    private static void PatchRequired(
        Harmony harmony,
        MethodBase? target,
        string label,
        string prefixName,
        List<string> failures)
    {
        if (target == null)
        {
            failures.Add($"{label}:missing_target");
            return;
        }

        try
        {
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(LanConnectOfficialContinueRunPatches), prefixName));
            Log.Info($"sts2_lan_connect continue_run: guarded {label}.");
        }
        catch (Exception exception)
        {
            failures.Add($"{label}:{exception.GetType().Name}");
        }
    }

    [HarmonyPriority(Priority.First)]
    private static bool StartLoadPrefix()
    {
        if (PresentDegradedFailure())
        {
            return false;
        }

        if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(
                out SerializableRun? run,
                out string failureReason) || run == null)
        {
            if (failureReason == "no_multiplayer_run_save")
            {
                return true;
            }

            LanConnectMultiplayerSaveRoomBinding.PresentContinueRunProtocolFailure(
                LanConnectMultiplayerSaveRoomBinding.MissingProtocolSelectionFailure(
                    $"The multiplayer save could not be safely loaded: {failureReason}."));
            return false;
        }

        return ValidateLoadedRun(run);
    }

    [HarmonyPriority(Priority.First)]
    private static bool StartHostPrefix([HarmonyArgument(0)] SerializableRun run) =>
        !PresentDegradedFailure() && ValidateLoadedRun(run);

    private static bool PresentDegradedFailure()
    {
        if (LanConnectDegradedMode.CreateBlockingFailure() is not { } failure)
        {
            return false;
        }

        LanConnectProtocolUiMessages.Present(failure);
        return true;
    }

    private static bool ValidateLoadedRun(SerializableRun run)
    {
        try
        {
            // Steam's own multiplayer saves have no LAN binding and keep their
            // original load path. LAN-origin saves must carry a frozen selection.
            if (LanConnectMultiplayerSaveRoomBinding.IsUnboundNativeSteamRun(run))
            {
                if (CanUseNativeSteamTransport(
                        SteamInitializer.Initialized,
                        CommandLineHelper.HasArg("fastmp")))
                {
                    return true;
                }

                LanConnectMultiplayerSaveRoomBinding.PresentContinueRunProtocolFailure(
                    LanConnectMultiplayerSaveRoomBinding.MissingProtocolSelectionFailure(
                        "An unbound native Steam save cannot be resumed over ENet."));
                return false;
            }

            LanConnectResolvedRoomBinding binding = LanConnectMultiplayerSaveRoomBinding.Resolve(run);
            LanConnectProtocolFailure? failure = binding.ProtocolFailure;
            if (failure == null && binding.ProtocolSelection == null)
            {
                failure = LanConnectMultiplayerSaveRoomBinding.MissingProtocolSelectionFailure(
                    "The saved multiplayer run has no validated protocol selection.");
            }

            if (failure == null)
            {
                return true;
            }

            LanConnectMultiplayerSaveRoomBinding.PresentContinueRunProtocolFailure(failure);
            return false;
        }
        catch (Exception exception)
        {
            Log.Error($"sts2_lan_connect continue_run: validation failed: {exception.GetType().Name}.");
            LanConnectMultiplayerSaveRoomBinding.PresentContinueRunProtocolFailure(
                LanConnectMultiplayerSaveRoomBinding.MissingProtocolSelectionFailure(
                    "The saved multiplayer run could not be validated."));
            return false;
        }
    }

    internal static bool CanUseNativeSteamTransport(bool steamInitialized, bool fastMpRequested) =>
        steamInitialized && !fastMpRequested;
}
