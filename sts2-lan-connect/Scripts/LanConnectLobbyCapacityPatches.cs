using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectLobbyCapacityPatches
{
    private static readonly ConditionalWeakTable<NetHostGameService, GuardedProtocolLease> GuardedLeases = new();
    private static readonly FieldInfo? MaxPlayersField =
        AccessTools.Field(typeof(StartRunLobby), "_maxPlayers")
        ?? AccessTools.Field(typeof(StartRunLobby), "<MaxPlayers>k__BackingField");

    public static void Apply(Harmony harmony)
    {
        int applied = 0;
        int skipped = 0;
        int failed = 0;

        MethodInfo? startENet = AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartENetHost));
        TrySafePatch(harmony, startENet, "StartENetHost",
            ref applied, ref skipped, ref failed,
            prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartENetHostPrefix)),
            postfix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartHostPostfix)));

        if (OperatingSystem.IsAndroid())
        {
            Log.Info("sts2_lan_connect gameplay: skipping StartSteamHost patch on Android.");
            skipped++;
        }
        else
        {
            MethodInfo? startSteam = AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartSteamHost));
            TrySafePatch(harmony, startSteam, "StartSteamHost",
                ref applied, ref skipped, ref failed,
                prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartSteamHostPrefix)),
                postfix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartHostPostfix)));
        }

        ConstructorInfo? lobbyCtor = AccessTools.Constructor(typeof(StartRunLobby),
            new[] { typeof(GameMode), typeof(INetGameService), typeof(IStartRunLobbyListener), typeof(int) });
        TrySafePatch(harmony, lobbyCtor, "StartRunLobby.ctor",
            ref applied, ref skipped, ref failed,
            postfix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartRunLobbyCtorPostfix)));

        MethodInfo? onConnected = AccessTools.Method(typeof(StartRunLobby), "OnConnectedToClientAsHost");
        TrySafePatch(harmony, onConnected, "OnConnectedToClientAsHost",
            ref applied, ref skipped, ref failed,
            prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(SyncMaxPlayersPrefix)));

        MethodInfo? handleJoin = AccessTools.Method(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage");
        TrySafePatch(harmony, handleJoin, "HandleClientLobbyJoinRequestMessage",
            ref applied, ref skipped, ref failed,
            prefix: new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(SyncMaxPlayersPrefix)));

        Log.Info($"sts2_lan_connect gameplay: lobby capacity patches applied={applied}, skipped={skipped}, failed={failed}.");
    }

    private static void TrySafePatch(
        Harmony harmony,
        MethodBase? target,
        string label,
        ref int applied,
        ref int skipped,
        ref int failed,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null)
    {
        if (target == null)
        {
            Log.Warn($"sts2_lan_connect gameplay: capacity patch target not found, skipping: {label}.");
            skipped++;
            return;
        }

        try
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            Log.Info($"sts2_lan_connect gameplay: capacity: patched {label}.");
            applied++;
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect gameplay: capacity patch failed for {label}: {ex.Message}");
            failed++;
        }
    }

    // ReSharper disable UnusedMember.Local

    private static void StartENetHostPrefix(
        NetHostGameService __instance,
        ref int maxClients,
        out LanConnectSessionProtocolLease? __state)
    {
        __state = FreezeHostGuardIfNeeded(__instance, maxClients);
        maxClients = ResolveRoomScopedMaxPlayers(maxClients);
    }

    private static void StartSteamHostPrefix(
        NetHostGameService __instance,
        ref int maxClients,
        out LanConnectSessionProtocolLease? __state)
    {
        __state = FreezeHostGuardIfNeeded(__instance, maxClients);
        maxClients = ResolveRoomScopedMaxPlayers(maxClients);
    }

    private static void StartHostPostfix(
        NetHostGameService __instance,
        NetErrorInfo? __result,
        LanConnectSessionProtocolLease? __state)
    {
        if (__state == null)
        {
            return;
        }

        if (__result.HasValue)
        {
            __state.Dispose();
            return;
        }

        GuardedLeases.Remove(__instance);
        GuardedLeases.Add(__instance, new GuardedProtocolLease(__instance, __state));
    }

    private static LanConnectSessionProtocolLease? FreezeHostGuardIfNeeded(
        NetHostGameService netService,
        int requestedMaxPlayers)
    {
        if (LanConnectSessionProtocolState.Shared.Current.Selection != null)
        {
            return null;
        }

        LanConnectProtocolSelection? savedSelection = TryResolveCurrentSavedRunSelection();
        LanConnectProtocolSelection selection = ResolveHostGuardSelection(
            savedSelection,
            requestedMaxPlayers,
            LanConnectBuildInfo.GetGameVersion(),
            LanConnectWireCacheDiagnostics.GetCurrentResult().Snapshot?.Signature);
        Log.Info(
            $"sts2_lan_connect gameplay: host protocol guard source={(savedSelection == null ? "compat_fallback" : "saved_run")}, profile={selection.Profile.ToCanonical()}, carrier={selection.Carrier.ToWireValue()}.");
        return LanConnectSessionProtocolState.Shared.FreezeHost(
            selection,
            $"host:{netService.GetHashCode():x8}");
    }

    internal static LanConnectProtocolSelection ResolveHostGuardSelection(
        LanConnectProtocolSelection? savedSelection,
        int requestedMaxPlayers,
        string gameVersion,
        string? wireCacheSignature) =>
        savedSelection
        ?? LanConnectProtocolSelection.CreateLocalCompat(
            Math.Clamp(
                requestedMaxPlayers,
                LanConnectConstants.ProtocolMinPlayers,
                LanConnectConstants.ProtocolMaxPlayers),
            gameVersion,
            wireCacheSignature);

    private static LanConnectProtocolSelection? TryResolveCurrentSavedRunSelection()
    {
        if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(
                out SerializableRun? run,
                out string failureReason)
            || run == null)
        {
            Log.Info(
                $"sts2_lan_connect gameplay: no saved protocol selection for host guard, reason={failureReason}.");
            return null;
        }

        LanConnectResolvedRoomBinding binding = LanConnectMultiplayerSaveRoomBinding.Resolve(run);
        if (binding.ProtocolFailure != null)
        {
            Log.Warn(
                $"sts2_lan_connect gameplay: saved protocol selection rejected for host guard, code={binding.ProtocolFailure.Code}.");
            return null;
        }

        return binding.ProtocolSelection;
    }

    private static void StartRunLobbyCtorPostfix(StartRunLobby __instance, INetGameService netService)
    {
        int currentMaxPlayers = GetMaxPlayers(__instance);
        int effective = ResolveRoomScopedMaxPlayers(currentMaxPlayers);
        if (netService.Type == NetGameType.Host
            && currentMaxPlayers != effective
            && MaxPlayersField != null)
        {
            MaxPlayersField.SetValue(__instance, effective);
        }
    }

    private static void SyncMaxPlayersPrefix(StartRunLobby __instance)
    {
        if (MaxPlayersField == null || __instance.NetService.Type != NetGameType.Host)
        {
            return;
        }

        int currentMaxPlayers = GetMaxPlayers(__instance);
        int effective = ResolveRoomScopedMaxPlayers(currentMaxPlayers);
        if (currentMaxPlayers != effective)
        {
            MaxPlayersField.SetValue(__instance, effective);
        }
    }

    private static int GetMaxPlayers(StartRunLobby lobby)
    {
        if (MaxPlayersField?.GetValue(lobby) is int value)
        {
            return value;
        }

        return LanConnectConstants.ProtocolMaxPlayers;
    }

    private static int ResolveRoomScopedMaxPlayers(int requestedMaxPlayers)
    {
        int active = LanConnectProtocolProfiles.GetActiveMaxPlayers();
        if (active > 0)
        {
            return Math.Clamp(
                active,
                LanConnectConstants.ProtocolMinPlayers,
                LanConnectConstants.ProtocolMaxPlayers);
        }

        return Math.Clamp(
            requestedMaxPlayers,
            LanConnectConstants.ProtocolMinPlayers,
            LanConnectConstants.ProtocolMaxPlayers);
    }

    private sealed class GuardedProtocolLease
    {
        private readonly NetHostGameService _netService;
        private readonly LanConnectSessionProtocolLease _lease;
        private readonly Action<NetErrorInfo> _disconnected;

        public GuardedProtocolLease(
            NetHostGameService netService,
            LanConnectSessionProtocolLease lease)
        {
            _netService = netService;
            _lease = lease;
            _disconnected = OnDisconnected;
            _netService.Disconnected += _disconnected;
        }

        private void OnDisconnected(NetErrorInfo _)
        {
            _netService.Disconnected -= _disconnected;
            _lease.Dispose();
            GuardedLeases.Remove(_netService);
        }
    }

    // ReSharper restore UnusedMember.Local
}
