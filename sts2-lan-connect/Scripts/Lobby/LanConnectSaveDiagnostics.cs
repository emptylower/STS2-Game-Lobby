using System;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectSaveDiagnostics
{
    private static string? _lastSnapshot;

    public static string CaptureSnapshot()
    {
        return BuildSnapshot();
    }

    public static void Poll(string source)
    {
        LogSnapshot(source, force: false);
    }

    public static void LogNow(string source, string? extra = null, bool force = true)
    {
        LogSnapshot(source, force, extra);
    }

    private static void LogSnapshot(string source, bool force, string? extra = null)
    {
        string snapshot = BuildSnapshot();
        if (!force && string.Equals(snapshot, _lastSnapshot, StringComparison.Ordinal))
        {
            return;
        }

        _lastSnapshot = snapshot;
        string suffix = string.IsNullOrWhiteSpace(extra) ? string.Empty : $", {extra}";
        GD.Print($"sts2_lan_connect save_diag: source={source}, {snapshot}{suffix}");
    }

    private static string BuildSnapshot()
    {
        try
        {
            bool hasActiveHostedRoom = LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true;
            string activeRoomId = LanConnectLobbyRuntime.Instance?.ActiveRoomId ?? "<none>";
            string effectiveEndpoint = string.IsNullOrWhiteSpace(LanConnectConfig.LobbyServerBaseUrl)
                ? "<none>"
                : LanConnectConfig.LobbyServerBaseUrl;
            int profileId = SaveManager.Instance.CurrentProfileId;
            string multiplayerSavePath = SaveManager.Instance.GetProfileScopedPath(Path.Combine("saves", "current_run_mp.save"));
            // GetProfileScopedPath returns a Godot user:// path that System.IO cannot see.
            string globalizedSavePath = ProjectSettings.GlobalizePath(multiplayerSavePath);
            // Mirror the vanilla existence check instead of reading SaveManager.HasMultiplayerRunSave:
            // BaseLib patches that getter into a destructive load-and-validate pass, and diagnostics
            // must never be able to trigger it (it used to corrupt the save mid-SaveRun).
            bool hasRunSave = File.Exists(globalizedSavePath) || File.Exists(globalizedSavePath + ".backup");
            string multiplayerSaveTimestamp = File.Exists(globalizedSavePath)
                ? File.GetLastWriteTimeUtc(globalizedSavePath).ToString("O")
                : "<missing>";

            if (!hasRunSave)
            {
                return $"hasRunSave=false, load=no_multiplayer_run_save, profile={profileId}, mpSavePath={multiplayerSavePath}, mpSaveUpdatedAt={multiplayerSaveTimestamp}, activeHostedRoom={hasActiveHostedRoom}, activeRoomId={activeRoomId}, lobby={effectiveEndpoint}";
            }

            if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(out SerializableRun? run, out string failureReason) || run == null)
            {
                return $"hasRunSave=true, load={failureReason}, profile={profileId}, mpSavePath={multiplayerSavePath}, mpSaveUpdatedAt={multiplayerSaveTimestamp}, activeHostedRoom={hasActiveHostedRoom}, activeRoomId={activeRoomId}, lobby={effectiveEndpoint}";
            }

            string saveKey = LanConnectMultiplayerSaveRoomBinding.BuildSaveKey(run);
            LanConnectSavedRoomBinding? binding = LanConnectConfig.TryGetSaveRoomBinding(saveKey);
            string playerSignature = LanConnectMultiplayerSaveRoomBinding.GetPlayerSignature(run);
            string bindingSegment = binding == null
                ? "binding=missing, effectiveHostChannel=lobby"
                : $"binding=present, bindingHostChannel={LanConnectHostChannels.DescribePersisted(binding.HostChannel)}, effectiveHostChannel={LanConnectHostChannels.Resolve(binding.HostChannel)}";
            return
                $"hasRunSave=true, load=ok, profile={profileId}, mpSavePath={multiplayerSavePath}, mpSaveUpdatedAt={multiplayerSaveTimestamp}, saveKey={saveKey}, gameMode={LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(run)}, players={run.Players.Count}, playerSignature={playerSignature}, startTime={run.StartTime}, {bindingSegment}, activeHostedRoom={hasActiveHostedRoom}, activeRoomId={activeRoomId}, lobby={effectiveEndpoint}";
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect save_diag failed: {ex.Message}");
            return $"snapshot_failed={ex.GetType().Name}";
        }
    }
}
