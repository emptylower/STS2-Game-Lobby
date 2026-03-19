using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Platform;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectDebugOverlayState(
    string LastStatusMessage,
    double LastLobbyRttMs,
    int RoomCount,
    string? SelectedRoomId,
    int ConsecutiveRefreshFailures,
    LobbyRoomSummary? SelectedRoom);

internal static class LanConnectDebugReport
{
    private const int MaxLogLines = 120;
    private const int LogTailWindow = 2500;
    private const int MaxLineLength = 600;

    public static string Build(LanConnectDebugOverlayState overlayState)
    {
        StringBuilder builder = new();
        string writableDataDirectory = LanConnectPaths.ResolveWritableDataDirectory();
        string configPath = Path.Combine(writableDataDirectory, "config.json");
        string? logPath = ResolveClientLogPath();
        string configuredPlayerName = LanConnectConfig.PlayerDisplayName;
        string fallbackUserName = System.Environment.UserName;
        string effectivePlayerName = LanConnectConfig.GetEffectivePlayerDisplayName();
        string effectiveBaseUrl = LanConnectConfig.LobbyServerBaseUrl;
        string effectiveWsUrl = LanConnectConfig.LobbyServerWsUrl;
        IReadOnlyList<string> logLines = ReadRelevantLogLines(logPath);
        Dictionary<string, List<string>> identifiers = ExtractIdentifiers(logLines);

        builder.AppendLine("STS2 LAN Connect Client Debug Report");
        builder.AppendLine($"generated_at_utc: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"generated_at_local: {DateTimeOffset.Now:O}");
        builder.AppendLine($"game_version: {LanConnectBuildInfo.GetGameVersion()}");
        builder.AppendLine($"mod_version: {LanConnectBuildInfo.GetModVersion()}");
        builder.AppendLine($"gameplay_relevant_mods: {FormatList(LanConnectBuildInfo.GetModList())}");
        builder.AppendLine($"player_name: {effectivePlayerName}");
        builder.AppendLine($"player_name_source: {DescribePlayerNameSource(configuredPlayerName)}");
        builder.AppendLine($"configured_player_name: {FormatValue(configuredPlayerName)}");
        builder.AppendLine($"fallback_system_user_name: {FormatValue(fallbackUserName)}");
        builder.AppendLine($"fallback_user_name_looks_like_android_uid: {LooksLikeAndroidUidUserName(fallbackUserName)}");
        builder.AppendLine($"primary_platform: {PlatformUtil.PrimaryPlatform}");
        builder.AppendLine($"local_platform_player_id: {TryGetLocalPlayerId()}");
        builder.AppendLine($"platform: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"architecture: {RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"machine_name: {System.Environment.MachineName}");
        builder.AppendLine($"mod_directory: {LanConnectPaths.ResolveModDirectory()}");
        builder.AppendLine($"writable_data_directory: {writableDataDirectory}");
        builder.AppendLine($"config_path: {configPath}");
        builder.AppendLine($"config_exists: {File.Exists(configPath)}");
        builder.AppendLine($"config_updated_at_utc: {FormatFileTimestamp(configPath)}");
        builder.AppendLine($"lobby_base_url_effective: {effectiveBaseUrl}");
        builder.AppendLine($"lobby_ws_url_effective: {effectiveWsUrl}");
        builder.AppendLine($"registry_base_url: {LanConnectConstants.DefaultLobbyRegistryBaseUrl}");
        builder.AppendLine($"selected_server_id: {FormatValue(LanConnectConfig.SelectedServerId)}");
        builder.AppendLine($"has_lobby_overrides: {LanConnectConfig.HasLobbyServerOverrides}");
        builder.AppendLine($"has_bundled_defaults: {LanConnectLobbyEndpointDefaults.HasBundledDefaults()}");
        builder.AppendLine($"compatibility_profile: {LanConnectLobbyEndpointDefaults.GetCompatibilityProfile()}");
        builder.AppendLine($"connection_strategy: {LanConnectLobbyEndpointDefaults.GetConnectionStrategy()}");
        builder.AppendLine($"last_manual_endpoint: {FormatValue(LanConnectConfig.LastEndpoint)}");
        builder.AppendLine($"last_room_name: {FormatValue(LanConnectConfig.LastRoomName)}");
        builder.AppendLine($"active_hosted_room: {LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true}");
        builder.AppendLine($"active_room_id: {FormatValue(LanConnectLobbyRuntime.Instance?.ActiveRoomId)}");
        builder.AppendLine($"save_snapshot: {LanConnectSaveDiagnostics.CaptureSnapshot()}");
        builder.AppendLine($"overlay_last_status: {FormatValue(overlayState.LastStatusMessage)}");
        builder.AppendLine($"overlay_last_lobby_rtt_ms: {(overlayState.LastLobbyRttMs >= 0d ? $"{overlayState.LastLobbyRttMs:0}" : "<unknown>")}");
        builder.AppendLine($"overlay_room_count: {overlayState.RoomCount}");
        builder.AppendLine($"overlay_selected_room_id: {FormatValue(overlayState.SelectedRoomId)}");
        builder.AppendLine($"overlay_consecutive_refresh_failures: {overlayState.ConsecutiveRefreshFailures}");
        builder.AppendLine($"selected_room_name: {FormatValue(overlayState.SelectedRoom?.RoomName)}");
        builder.AppendLine($"selected_room_host_player: {FormatValue(overlayState.SelectedRoom?.HostPlayerName)}");
        builder.AppendLine($"selected_room_game_mode: {FormatValue(overlayState.SelectedRoom?.GameMode)}");
        builder.AppendLine($"selected_room_status: {FormatValue(overlayState.SelectedRoom?.Status)}");
        builder.AppendLine($"selected_room_relay_state: {FormatValue(overlayState.SelectedRoom?.RelayState)}");
        builder.AppendLine($"selected_room_version: {FormatValue(overlayState.SelectedRoom?.Version)}");
        builder.AppendLine($"selected_room_mod_version: {FormatValue(overlayState.SelectedRoom?.ModVersion)}");
        builder.AppendLine($"selected_room_players: {FormatPlayerCounts(overlayState.SelectedRoom)}");
        builder.AppendLine($"selected_room_save_key: {FormatValue(overlayState.SelectedRoom?.SavedRun?.SaveKey)}");
        builder.AppendLine($"selected_room_connected_player_net_ids: {FormatList(overlayState.SelectedRoom?.SavedRun?.ConnectedPlayerNetIds)}");
        builder.AppendLine($"selected_room_saved_run_slots: {FormatSavedRunSlots(overlayState.SelectedRoom?.SavedRun?.Slots)}");
        builder.AppendLine($"client_log_path: {FormatValue(logPath)}");
        builder.AppendLine($"client_log_exists: {(!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath))}");
        builder.AppendLine($"client_log_updated_at_utc: {FormatFileTimestamp(logPath)}");
        builder.AppendLine($"recent_room_ids: {FormatList(GetIdentifierValues(identifiers, "roomId"))}");
        builder.AppendLine($"recent_ticket_ids: {FormatList(GetIdentifierValues(identifiers, "ticketId"))}");
        builder.AppendLine($"recent_player_net_ids: {FormatList(GetIdentifierValues(identifiers, "playerNetId"))}");
        builder.AppendLine($"recent_net_ids: {FormatList(GetIdentifierValues(identifiers, "netId"))}");
        builder.AppendLine($"recent_save_keys: {FormatList(GetIdentifierValues(identifiers, "saveKey"))}");
        builder.AppendLine();
        builder.AppendLine("Recent Relevant Client Log Lines");
        builder.AppendLine("--------------------------------");

        if (logLines.Count == 0)
        {
            builder.AppendLine("<no relevant local log lines found>");
        }
        else
        {
            foreach (string line in logLines)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ReadRelevantLogLines(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            List<string> tailLines = File.ReadLines(logPath)
                .TakeLast(LogTailWindow)
                .Select(SanitizeLogLine)
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            List<string> selected = tailLines
                .Where(IsRelevantLogLine)
                .TakeLast(MaxLogLines)
                .ToList();

            return selected;
        }
        catch (Exception ex)
        {
            return new[]
            {
                $"<failed to read client log: {ex.GetType().Name}: {ex.Message}>"
            };
        }
    }

    private static bool IsRelevantLogLine(string line)
    {
        string lower = line.ToLowerInvariant();
        return lower.Contains("sts2_lan_connect")
               || lower.Contains("clientconnectionfailedexception")
               || lower.Contains("connectionfailurereason")
               || lower.Contains("lobbyserviceexception")
               || lower.Contains("unexpectedly disconnected from host")
               || lower.Contains("mod mismatch")
               || lower.Contains("version mismatch")
               || lower.Contains("timeout")
               || lower.Contains("handshaketimeout")
               || lower.Contains("unknownnetworkerror")
               || lower.Contains("runtime error")
               || lower.Contains("exception")
               || lower.Contains("error")
               || lower.Contains("disconnect")
               || lower.Contains("relay_failure")
               || lower.Contains("direct_failure");
    }

    private static string SanitizeLogLine(string line)
    {
        string sanitized = line
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return sanitized.Length <= MaxLineLength
            ? sanitized
            : sanitized[..MaxLineLength] + "...";
    }

    private static string? ResolveClientLogPath()
    {
        foreach (string candidate in EnumerateCandidateLogPaths())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return EnumerateCandidateLogPaths().FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path));
    }

    private static string TryGetLocalPlayerId()
    {
        try
        {
            return PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform).ToString();
        }
        catch (Exception ex)
        {
            return $"<failed:{ex.GetType().Name}>";
        }
    }

    private static string DescribePlayerNameSource(string configuredPlayerName)
    {
        return string.IsNullOrWhiteSpace(configuredPlayerName)
            ? "environment_user_name"
            : "configured";
    }

    private static bool LooksLikeAndroidUidUserName(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && Regex.IsMatch(value, @"^u\d+_a\d+$", RegexOptions.CultureInvariant);
    }

    private static string FormatPlayerCounts(LobbyRoomSummary? room)
    {
        return room == null
            ? "<none>"
            : $"{room.CurrentPlayers}/{room.MaxPlayers}";
    }

    private static string FormatSavedRunSlots(IReadOnlyCollection<LobbySavedRunSlot>? slots)
    {
        if (slots == null || slots.Count == 0)
        {
            return "<none>";
        }

        return string.Join(
            ", ",
            slots.Select(static slot => $"{slot.NetId}:{slot.CharacterId}:{(slot.IsHost ? "host" : "guest")}:{(slot.IsConnected ? "online" : "offline")}"));
    }

    private static Dictionary<string, List<string>> ExtractIdentifiers(IReadOnlyList<string> logLines)
    {
        Dictionary<string, List<string>> output = new(StringComparer.Ordinal);
        foreach (string key in new[] { "roomId", "ticketId", "playerNetId", "netId", "saveKey" })
        {
            output[key] = new List<string>();
        }

        foreach (string line in logLines)
        {
            foreach (string key in output.Keys)
            {
                foreach (Match match in Regex.Matches(line, $@"\b{Regex.Escape(key)}=([^,\s;]+)"))
                {
                    string value = match.Groups[1].Value.Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    List<string> values = output[key];
                    if (values.Contains(value, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    values.Add(value);
                    if (values.Count > 12)
                    {
                        values.RemoveAt(0);
                    }
                }
            }
        }

        return output;
    }

    private static IReadOnlyCollection<string> GetIdentifierValues(IReadOnlyDictionary<string, List<string>> values, string key)
    {
        return values.TryGetValue(key, out List<string>? result)
            ? result
            : Array.Empty<string>();
    }

    private static IEnumerable<string> EnumerateCandidateLogPaths()
    {
        string userHome = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        string roamingAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

        yield return ProjectSettings.GlobalizePath("user://logs/godot.log");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return Path.Combine(userHome, "Library", "Application Support", "SlayTheSpire2", "logs", "godot.log");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return Path.Combine(roamingAppData, "SlayTheSpire2", "logs", "godot.log");
            yield return Path.Combine(localAppData, "SlayTheSpire2", "logs", "godot.log");
        }
        else
        {
            string xdgStateHome = System.Environment.GetEnvironmentVariable("XDG_STATE_HOME") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(xdgStateHome))
            {
                yield return Path.Combine(xdgStateHome, "SlayTheSpire2", "logs", "godot.log");
            }

            yield return Path.Combine(userHome, ".local", "share", "SlayTheSpire2", "logs", "godot.log");
        }
    }

    private static string FormatFileTimestamp(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? File.GetLastWriteTimeUtc(path).ToString("O")
                : "<missing>";
        }
        catch (Exception ex)
        {
            return $"<failed:{ex.GetType().Name}>";
        }
    }

    private static string FormatList(IReadOnlyCollection<string>? values)
    {
        return values == null || values.Count == 0
            ? "<none>"
            : string.Join(", ", values);
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value.Trim();
    }
}
