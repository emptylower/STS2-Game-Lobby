using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Null;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectLobbyPlayerNameDirectory
{
    private static readonly object Sync = new();
    private static readonly FieldInfo? NullPlatformField = typeof(PlatformUtil).GetField("_null", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo? MultiplayerNamesField = typeof(NullPlatformUtilStrategy).GetField("_mpNames", BindingFlags.Instance | BindingFlags.NonPublic);

    private static string? _activeRoomId;
    private static readonly Dictionary<ulong, string> ActiveRoomNames = new();

    public static void BeginRoom(string roomId)
    {
        lock (Sync)
        {
            _activeRoomId = roomId;
            ActiveRoomNames.Clear();
            ApplyUnsafe();
        }

        LanConnectRemoteLobbyPlayerPatches.QueueRefreshAll();
    }

    public static void ClearRoom(string? roomId)
    {
        lock (Sync)
        {
            if (roomId != null && !string.Equals(_activeRoomId, roomId, StringComparison.Ordinal))
            {
                return;
            }

            _activeRoomId = null;
            ActiveRoomNames.Clear();
            ApplyUnsafe();
        }

        LanConnectRemoteLobbyPlayerPatches.QueueRefreshAll();
    }

    public static void Upsert(string roomId, ulong netId, string playerName)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        lock (Sync)
        {
            if (!string.Equals(_activeRoomId, roomId, StringComparison.Ordinal))
            {
                _activeRoomId = roomId;
                ActiveRoomNames.Clear();
            }

            ActiveRoomNames[netId] = LanConnectConfig.SanitizePlayerDisplayName(playerName);
            ApplyUnsafe();
        }

        LanConnectRemoteLobbyPlayerPatches.QueueRefreshAll();
    }

    public static void UpsertSnapshot(string roomId, IEnumerable<LobbyPlayerNameEntry>? entries)
    {
        if (entries == null)
        {
            return;
        }

        lock (Sync)
        {
            if (!string.Equals(_activeRoomId, roomId, StringComparison.Ordinal))
            {
                _activeRoomId = roomId;
                ActiveRoomNames.Clear();
            }

            foreach (LobbyPlayerNameEntry entry in entries)
            {
                if (!TryParseNetId(entry.PlayerNetId, out ulong netId) || string.IsNullOrWhiteSpace(entry.PlayerName))
                {
                    continue;
                }

                ActiveRoomNames[netId] = LanConnectConfig.SanitizePlayerDisplayName(entry.PlayerName);
            }

            ApplyUnsafe();
        }

        LanConnectRemoteLobbyPlayerPatches.QueueRefreshAll();
    }

    public static List<LobbyPlayerNameEntry> BuildSnapshot(string roomId)
    {
        lock (Sync)
        {
            if (!string.Equals(_activeRoomId, roomId, StringComparison.Ordinal))
            {
                return new List<LobbyPlayerNameEntry>();
            }

            return ActiveRoomNames
                .OrderBy(static pair => pair.Key)
                .Select(static pair => new LobbyPlayerNameEntry
                {
                    PlayerNetId = pair.Key.ToString(CultureInfo.InvariantCulture),
                    PlayerName = pair.Value
                })
                .ToList();
        }
    }

    public static string? TryGetPlayerName(ulong netId)
    {
        lock (Sync)
        {
            return ActiveRoomNames.TryGetValue(netId, out string? value)
                ? value
                : null;
        }
    }

    private static bool TryParseNetId(string? value, out ulong netId)
    {
        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out netId);
    }

    private static void ApplyUnsafe()
    {
        try
        {
            object? nullPlatform = NullPlatformField?.GetValue(null);
            if (nullPlatform == null || MultiplayerNamesField == null)
            {
                return;
            }

            List<NullMultiplayerName> names = ActiveRoomNames
                .OrderBy(static pair => pair.Key)
                .Select(static pair => new NullMultiplayerName
                {
                    netId = pair.Key,
                    name = pair.Value
                })
                .ToList();
            MultiplayerNamesField.SetValue(nullPlatform, names);
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect failed to update Null multiplayer names: {ex.Message}");
        }
    }
}
