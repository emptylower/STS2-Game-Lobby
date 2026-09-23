using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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

    public static void ApplyHostProtocolGuards(Harmony harmony)
    {
        // These guards protect the game's own continue-run flow as well as our lobby UI.
        // A missing target or failed Harmony patch enters LAN Connect's degraded mode.
        PatchRequiredHostGuard(
            harmony,
            AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartENetHost)),
            "StartENetHost",
            new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartENetHostPrefix)),
            new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartHostPostfix)));

        if (OperatingSystem.IsAndroid())
        {
            Log.Info("sts2_lan_connect gameplay: skipping StartSteamHost guard on Android.");
            return;
        }

        PatchRequiredHostGuard(
            harmony,
            AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartSteamHost)),
            "StartSteamHost",
            new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartSteamHostPrefix)),
            new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), nameof(StartSteamHostPostfix)));
    }

    private static void PatchRequiredHostGuard(
        Harmony harmony,
        MethodBase? target,
        string label,
        HarmonyMethod prefix,
        HarmonyMethod postfix)
    {
        if (target == null)
        {
            throw new MissingMethodException($"Required host protocol guard target not found: {label}.");
        }

        harmony.Patch(target, prefix: prefix, postfix: postfix);
        Log.Info($"sts2_lan_connect gameplay: host protocol guard patched {label}.");
    }

    public static void Apply(Harmony harmony)
    {
        int applied = 0;
        int skipped = 0;
        int failed = 0;

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

    private static bool StartENetHostPrefix(
        NetHostGameService __instance,
        ref int maxClients,
        ref NetErrorInfo? __result,
        out LanConnectSessionProtocolLease? __state)
    {
        return TryStartHostWithProtocolGuard(__instance, ref maxClients, ref __result, out __state,
            isSteamHost: false);
    }

    private static bool StartSteamHostPrefix(
        NetHostGameService __instance,
        ref int maxClients,
        ref Task<NetErrorInfo?> __result,
        out LanConnectSessionProtocolLease? __state)
    {
        NetErrorInfo? error = null;
        if (TryStartHostWithProtocolGuard(__instance, ref maxClients, ref error, out __state,
                isSteamHost: true))
        {
            return true;
        }

        __result = Task.FromResult(error);
        return false;
    }

    private static bool TryStartHostWithProtocolGuard(
        NetHostGameService netService,
        ref int maxClients,
        ref NetErrorInfo? result,
        out LanConnectSessionProtocolLease? lease,
        bool isSteamHost)
    {
        lease = null;
        try
        {
            if (LanConnectDegradedMode.CreateBlockingFailure() is { } degradedFailure)
            {
                throw new LanConnectProtocolException(degradedFailure);
            }

            lease = FreezeHostGuardIfNeeded(netService, maxClients, isSteamHost);
        }
        catch (LanConnectProtocolException exception)
        {
            Log.Warn(
                $"sts2_lan_connect gameplay: blocked host start because saved protocol could not be confirmed, code={exception.Failure.Code}.");
            LanConnectMultiplayerSaveRoomBinding.PresentContinueRunProtocolFailure(exception.Failure);
            result = new NetErrorInfo(NetError.InternalError, selfInitiated: false);
            return false;
        }

        maxClients = ResolveRoomScopedMaxPlayers(maxClients);
        return true;
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

    private static void StartSteamHostPostfix(
        NetHostGameService __instance,
        ref Task<NetErrorInfo?> __result,
        LanConnectSessionProtocolLease? __state)
    {
        if (__state != null)
        {
            __result = TrackSteamHostStartAsync(__instance, __result, __state);
        }
    }

    private static async Task<NetErrorInfo?> TrackSteamHostStartAsync(
        NetHostGameService netService,
        Task<NetErrorInfo?> startTask,
        LanConnectSessionProtocolLease lease)
    {
        try
        {
            NetErrorInfo? result = await startTask;
            if (result.HasValue)
            {
                lease.Dispose();
                return result;
            }

            GuardedLeases.Remove(netService);
            GuardedLeases.Add(netService, new GuardedProtocolLease(netService, lease));
            return null;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static LanConnectSessionProtocolLease? FreezeHostGuardIfNeeded(
        NetHostGameService netService,
        int requestedMaxPlayers,
        bool isSteamHost)
    {
        LanConnectSessionProtocolSnapshot activeSnapshot = LanConnectSessionProtocolState.Shared.Current;
        if (activeSnapshot.Selection != null)
        {
            string ownerId = $"host:{netService.GetHashCode():x8}";
            if (CanReuseFrozenHostSelection(activeSnapshot, ownerId))
            {
                return null;
            }

            throw LanConnectProtocolFailureMapper.FromLocalException(
                "protocol_selection_conflict",
                "Another protocol session is frozen for a different host.");
        }

        LanConnectResolvedRoomBinding? savedBinding = TryResolveCurrentSavedRunBinding(
            out bool isUnboundNativeSteamRun);
        if (isUnboundNativeSteamRun && !isSteamHost)
        {
            throw new LanConnectProtocolException(
                LanConnectMultiplayerSaveRoomBinding.MissingProtocolSelectionFailure(
                    "An unbound native Steam save cannot be resumed over ENet."));
        }

        if (CanBypassLanProtocolGuardForNativeSteam(
                isSteamHost,
                isUnboundNativeSteamRun,
                savedBinding == null))
        {
            Log.Info("sts2_lan_connect gameplay: leaving native Steam host without a LAN protocol selection.");
            return null;
        }

        LanConnectProtocolSelection selection = ResolveHostGuardSelection(
            savedBinding,
            requestedMaxPlayers,
            LanConnectBuildInfo.GetGameVersion(),
            LanConnectWireCacheDiagnostics.GetCurrentResult().Snapshot?.Signature);
        Log.Info(
            $"sts2_lan_connect gameplay: host protocol guard source={(savedBinding == null ? "new_host" : "saved_run")}, saveKey={savedBinding?.SaveKey ?? "<none>"}, profile={selection.Profile.ToCanonical()}, carrier={selection.Carrier.ToWireValue()}.");
        return LanConnectSessionProtocolState.Shared.FreezeHost(
            selection,
            $"host:{netService.GetHashCode():x8}");
    }

    internal static bool CanReuseFrozenHostSelection(
        LanConnectSessionProtocolSnapshot snapshot,
        string ownerId) =>
        snapshot.Phase == LanConnectSessionProtocolPhase.Frozen
        && snapshot.Role == LanConnectSessionProtocolRole.Host
        && snapshot.Selection != null
        && string.Equals(snapshot.OwnerId, ownerId, StringComparison.Ordinal);

    internal static bool CanBypassLanProtocolGuardForNativeSteam(
        bool isSteamHost,
        bool isUnboundNativeSteamRun,
        bool hasNoSavedRun) =>
        isSteamHost && (isUnboundNativeSteamRun || hasNoSavedRun);

    internal static LanConnectProtocolSelection ResolveHostGuardSelection(
        LanConnectResolvedRoomBinding? savedBinding,
        int requestedMaxPlayers,
        string gameVersion,
        string? wireCacheSignature)
    {
        if (savedBinding?.ProtocolFailure != null)
        {
            throw new LanConnectProtocolException(savedBinding.ProtocolFailure);
        }

        if (savedBinding != null)
        {
            return savedBinding.ProtocolSelection
                ?? throw new LanConnectProtocolException(
                    LanConnectMultiplayerSaveRoomBinding.MissingProtocolSelectionFailure(
                        "The saved multiplayer run has no validated protocol selection."));
        }

        return LanConnectProtocolSelection.CreateLocalCompat(
            Math.Clamp(
                requestedMaxPlayers,
                LanConnectConstants.ProtocolMinPlayers,
                LanConnectConstants.ProtocolMaxPlayers),
            gameVersion,
            wireCacheSignature);
    }

    private static LanConnectResolvedRoomBinding? TryResolveCurrentSavedRunBinding(
        out bool isUnboundNativeSteamRun)
    {
        isUnboundNativeSteamRun = false;
        if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(
                out SerializableRun? run,
                out string failureReason)
            || run == null)
        {
            if (failureReason == "no_multiplayer_run_save")
            {
                return null;
            }

            return new LanConnectResolvedRoomBinding
            {
                ProtocolFailure = LanConnectMultiplayerSaveRoomBinding.MissingProtocolSelectionFailure(
                    $"The multiplayer save could not be safely loaded: {failureReason}.")
            };
        }

        isUnboundNativeSteamRun = LanConnectMultiplayerSaveRoomBinding.IsUnboundNativeSteamRun(run);
        return isUnboundNativeSteamRun ? null : LanConnectMultiplayerSaveRoomBinding.Resolve(run);
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
