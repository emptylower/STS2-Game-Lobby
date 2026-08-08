using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectPeerVersionInfoPatches
{
    internal static void Apply(Harmony harmony)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        harmony.Patch(
            LocalDefaultPatchHolder.LocalDefaultMethod,
            postfix: new HarmonyMethod(
                typeof(LocalDefaultPatchHolder),
                nameof(LocalDefaultPatchHolder.LocalDefaultPostfix)));

        try
        {
            harmony.Patch(
                HostDiagnosticsPatchHolder.StartRunJoinMethod,
                prefix: new HarmonyMethod(
                    typeof(HostDiagnosticsPatchHolder),
                    nameof(HostDiagnosticsPatchHolder.StartRunJoinPrefix)));
            harmony.Patch(
                HostDiagnosticsPatchHolder.LoadRunJoinMethod,
                prefix: new HarmonyMethod(
                    typeof(HostDiagnosticsPatchHolder),
                    nameof(HostDiagnosticsPatchHolder.LoadRunJoinPrefix)));
            harmony.Patch(
                HostDiagnosticsPatchHolder.RunRejoinMethod,
                prefix: new HarmonyMethod(
                    typeof(HostDiagnosticsPatchHolder),
                    nameof(HostDiagnosticsPatchHolder.RunRejoinPrefix)));
        }
        catch (Exception ex)
        {
            SafeLog(() => Log.Warn(
                $"sts2_lan_connect wire_handshake host gating unavailable: " +
                $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    private static class LocalDefaultPatchHolder
    {
        // Keep every game-typed static behind an explicit cctor so merely loading the
        // outer patch coordinator cannot resolve sts2.dll types.
        static LocalDefaultPatchHolder()
        {
        }

        internal static readonly MethodInfo LocalDefaultMethod =
            AccessTools.DeclaredMethod(typeof(PeerVersionInfo), nameof(PeerVersionInfo.LocalDefault))
            ?? throw new MissingMethodException(typeof(PeerVersionInfo).FullName, nameof(PeerVersionInfo.LocalDefault));
        internal static void LocalDefaultPostfix(ref PeerVersionInfo __result)
        {
            LanConnectWireCacheHandshakeToken? token = null;
            try
            {
                LanConnectWireCacheCaptureResult capture =
                    LanConnectWireCacheDiagnostics.GetCurrentResult();
                if (capture.IsAvailable)
                {
                    token = LanConnectWireCacheHandshakeToken.FromSnapshot(capture.Snapshot!);
                    SafeLog(() => Log.Info(
                        $"sts2_lan_connect wire_handshake advertise: signature={token.Signature}, " +
                        $"widths={token.FormatWidths()}, decision=advertised"));
                }
                else
                {
                    SafeLog(() => Log.Warn(
                        $"sts2_lan_connect wire_handshake advertise: signature=unavailable, " +
                        $"decision=local-unavailable, reason={capture.FailureReason}"));
                }
            }
            catch (Exception ex)
            {
                SafeLog(() => Log.Warn(
                    $"sts2_lan_connect wire_handshake advertise: signature=unavailable, " +
                    $"decision=local-unavailable, reason={ex.GetType().Name}: {ex.Message}"));
            }

            __result.otherMods = LanConnectWireCacheHandshakeToken.ReplaceSentinels(
                __result.otherMods,
                token);
        }
    }

    private static class HostDiagnosticsPatchHolder
    {
        // These optional diagnostics must not initialize until after the required
        // LocalDefault patch is installed.
        static HostDiagnosticsPatchHolder()
        {
        }

        internal static readonly MethodInfo StartRunJoinMethod = RequireMethod(
            typeof(StartRunLobby),
            "HandleClientLobbyJoinRequestMessage");
        internal static readonly MethodInfo LoadRunJoinMethod = RequireMethod(
            typeof(LoadRunLobby),
            "HandleClientLoadJoinRequestMessage");
        internal static readonly MethodInfo RunRejoinMethod = RequireMethod(
            typeof(RunLobby),
            "HandleClientRejoinRequestMessage");

        internal static bool StartRunJoinPrefix(
            StartRunLobby __instance,
            ClientLobbyJoinRequestMessage message,
            ulong senderId) =>
            ShouldRunOriginalHandler(
                message.versionInfo,
                senderId,
                "lobby-join",
                __instance.NetService);

        internal static bool LoadRunJoinPrefix(
            LoadRunLobby __instance,
            ClientLoadJoinRequestMessage message,
            ulong senderId) =>
            ShouldRunOriginalHandler(
                message.versionInfo,
                senderId,
                "load-join",
                __instance.NetService);

        internal static bool RunRejoinPrefix(
            ClientRejoinRequestMessage message,
            ulong senderId,
            INetGameService ____netService) =>
            ShouldRunOriginalHandler(
                message.versionInfo,
                senderId,
                "rejoin",
                ____netService);

        private static bool ShouldRunOriginalHandler(
            PeerVersionInfo remoteInfo,
            ulong senderId,
            string path,
            INetGameService netService)
        {
            LanConnectWireCacheHandshakeTokenParseResult? remoteParse = null;
            return LanConnectWireCacheHandshakeGate.ShouldRunHostHandler(
                () =>
                {
                    LanConnectWireCacheCaptureResult localCapture =
                        LanConnectWireCacheDiagnostics.GetCurrentResult();
                    remoteParse = LanConnectWireCacheHandshakeToken.Parse(remoteInfo.otherMods);
                    return LanConnectWireCacheHandshakeDecision.Evaluate(
                        localCapture,
                        remoteParse,
                        relaxedCompatibility: false);
                },
                decision => LogRemoteHandshakeDecision(
                    decision,
                    remoteParse!,
                    senderId,
                    path),
                decision =>
                {
                    ((INetHostGameService)netService).DisconnectClient(
                        senderId,
                        NetError.ModMismatch,
                        now: true);
                    SafeLog(() => Log.Warn(
                        $"sts2_lan_connect wire_handshake host rejected: path={path}, " +
                        $"senderId={senderId}, reason={decision.Detail}"));
                },
                ex => Log.Warn(
                    $"sts2_lan_connect wire_handshake host: path={path}, senderId={senderId}, " +
                    $"localSignature=unavailable, remoteSignature=unavailable, " +
                    $"decision=local-unavailable, reason={ex.GetType().Name}: {ex.Message}"));
        }

        private static void LogRemoteHandshakeDecision(
            LanConnectWireCacheHandshakeDecision decision,
            LanConnectWireCacheHandshakeTokenParseResult remoteParse,
            ulong senderId,
            string path)
        {
            string decisionName = decision.Kind switch
            {
                LanConnectWireCacheHandshakeDecisionKind.Match => "match",
                LanConnectWireCacheHandshakeDecisionKind.Mismatch => "mismatch",
                LanConnectWireCacheHandshakeDecisionKind.LocalUnavailable => "local-unavailable",
                LanConnectWireCacheHandshakeDecisionKind.RemoteAbsent => "remote-absent",
                _ => decision.Kind.ToString()
            };
            string diagnostic =
                $"sts2_lan_connect wire_handshake host: path={path}, senderId={senderId}, " +
                $"localSignature={decision.LocalToken?.Signature ?? "unavailable"}, " +
                $"remoteSignature={decision.RemoteToken?.Signature ?? "absent"}, " +
                $"decision={decisionName}, " +
                $"localWidths={decision.LocalToken?.FormatWidths() ?? "unavailable"}, " +
                $"remoteWidths={decision.RemoteToken?.FormatWidths() ?? "unavailable"}, " +
                $"remoteSentinelStatus={remoteParse.Status}";
            if (decision.Kind == LanConnectWireCacheHandshakeDecisionKind.Match)
            {
                Log.Info(diagnostic);
            }
            else
            {
                Log.Warn(diagnostic);
            }
        }

        private static MethodInfo RequireMethod(Type type, string name) =>
            AccessTools.DeclaredMethod(type, name)
            ?? throw new MissingMethodException(type.FullName, name);
    }

    private static void SafeLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Handshake diagnostics must never become a connection failure path.
        }
    }
}
