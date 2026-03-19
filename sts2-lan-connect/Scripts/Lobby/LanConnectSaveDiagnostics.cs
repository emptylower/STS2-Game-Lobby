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
            bool hasRunSave = SaveManager.Instance.HasMultiplayerRunSave;
            bool hasActiveHostedRoom = LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true;
            string activeRoomId = LanConnectLobbyRuntime.Instance?.ActiveRoomId ?? "<none>";
            string effectiveEndpoint = string.IsNullOrWhiteSpace(LanConnectConfig.LobbyServerBaseUrl)
                ? "<none>"
                : LanConnectConfig.LobbyServerBaseUrl;
            string selectedServerId = string.IsNullOrWhiteSpace(LanConnectConfig.SelectedServerId)
                ? "<none>"
                : LanConnectConfig.SelectedServerId;
            int profileId = SaveManager.Instance.CurrentProfileId;
            string multiplayerSavePath = SaveManager.Instance.GetProfileScopedPath(Path.Combine("saves", "current_run_mp.save"));
            string multiplayerSaveTimestamp = File.Exists(multiplayerSavePath)
                ? File.GetLastWriteTimeUtc(multiplayerSavePath).ToString("O")
                : "<missing>";

            if (!hasRunSave)
            {
                return $"hasRunSave=false, load=no_multiplayer_run_save, profile={profileId}, mpSavePath={multiplayerSavePath}, mpSaveUpdatedAt={multiplayerSaveTimestamp}, activeHostedRoom={hasActiveHostedRoom}, activeRoomId={activeRoomId}, lobby={effectiveEndpoint}, selectedServerId={selectedServerId}";
            }

            if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(out SerializableRun? run, out string failureReason) || run == null)
            {
                return $"hasRunSave=true, load={failureReason}, profile={profileId}, mpSavePath={multiplayerSavePath}, mpSaveUpdatedAt={multiplayerSaveTimestamp}, activeHostedRoom={hasActiveHostedRoom}, activeRoomId={activeRoomId}, lobby={effectiveEndpoint}, selectedServerId={selectedServerId}";
            }

            string saveKey = LanConnectMultiplayerSaveRoomBinding.BuildSaveKey(run);
            LanConnectSavedRoomBinding? binding = LanConnectConfig.TryGetSaveRoomBinding(saveKey);
            string playerSignature = LanConnectMultiplayerSaveRoomBinding.GetPlayerSignature(run);
            return
                $"hasRunSave=true, load=ok, profile={profileId}, mpSavePath={multiplayerSavePath}, mpSaveUpdatedAt={multiplayerSaveTimestamp}, saveKey={saveKey}, gameMode={LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(run)}, players={run.Players.Count}, playerSignature={playerSignature}, startTime={run.StartTime}, binding={(binding == null ? "missing" : "present")}, activeHostedRoom={hasActiveHostedRoom}, activeRoomId={activeRoomId}, lobby={effectiveEndpoint}, selectedServerId={selectedServerId}";
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect save_diag failed: {ex.Message}");
            return $"snapshot_failed={ex.GetType().Name}";
        }
    }
}
