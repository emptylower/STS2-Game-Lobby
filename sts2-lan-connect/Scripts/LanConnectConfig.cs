using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectConfigData
{
    public string LastEndpoint { get; set; } = string.Empty;

    public string LobbyServerBaseUrl { get; set; } = string.Empty;

    public string LastRoomName { get; set; } = string.Empty;

    public string PlayerDisplayName { get; set; } = string.Empty;

    public float? RoomChatOffsetX { get; set; }

    public float? RoomChatOffsetY { get; set; }

    public List<LanConnectSavedRoomBinding> SaveRoomBindings { get; set; } = new();
}

internal static class LanConnectConfig
{
    private const string ConfigFileName = "config.json";
    public const int MaxRoomNameLength = 32;
    public const int MaxPlayerDisplayNameLength = 10;
    public const int MaxRoomPasswordLength = 10;

    private static readonly object Sync = new();

    private static LanConnectConfigData _data = new();

    public static string LastEndpoint
    {
        get
        {
            lock (Sync)
            {
                return _data.LastEndpoint;
            }
        }
        set
        {
            lock (Sync)
            {
                if (_data.LastEndpoint == value)
                {
                    return;
                }

                _data.LastEndpoint = value;
                SaveUnsafe();
            }
        }
    }

    public static string LobbyServerBaseUrl
    {
        get
        {
            lock (Sync)
            {
                return string.IsNullOrWhiteSpace(_data.LobbyServerBaseUrl)
                    ? LanConnectLobbyEndpointDefaults.GetDefaultBaseUrl()
                    : _data.LobbyServerBaseUrl;
            }
        }
        set
        {
            SetString(
                static (data, next) => data.LobbyServerBaseUrl = next,
                static data => data.LobbyServerBaseUrl,
                NormalizeLobbyEndpointOverride(value));
        }
    }

    public static string LobbyServerBaseUrlOverride
    {
        get
        {
            lock (Sync)
            {
                return _data.LobbyServerBaseUrl;
            }
        }
    }

    public static bool HasLobbyServerOverrides
    {
        get
        {
            lock (Sync)
            {
                return !string.IsNullOrWhiteSpace(_data.LobbyServerBaseUrl);
            }
        }
    }

    public static string LastRoomName
    {
        get
        {
            lock (Sync)
            {
                return _data.LastRoomName;
            }
        }
        set
        {
            SetString(
                static (data, next) => data.LastRoomName = next,
                static data => data.LastRoomName,
                SanitizeRoomName(value));
        }
    }

    public static string PlayerDisplayName
    {
        get
        {
            lock (Sync)
            {
                return _data.PlayerDisplayName;
            }
        }
        set
        {
            SetString(
                static (data, next) => data.PlayerDisplayName = next,
                static data => data.PlayerDisplayName,
                SanitizePlayerDisplayName(value));
        }
    }

    public static Vector2? RoomChatOverlayOffset
    {
        get
        {
            lock (Sync)
            {
                if (!_data.RoomChatOffsetX.HasValue || !_data.RoomChatOffsetY.HasValue)
                {
                    return null;
                }

                return new Vector2(_data.RoomChatOffsetX.Value, _data.RoomChatOffsetY.Value);
            }
        }
        set
        {
            lock (Sync)
            {
                float? nextX = value?.X;
                float? nextY = value?.Y;
                if (_data.RoomChatOffsetX == nextX && _data.RoomChatOffsetY == nextY)
                {
                    return;
                }

                _data.RoomChatOffsetX = nextX;
                _data.RoomChatOffsetY = nextY;
                SaveUnsafe();
            }
        }
    }

    public static LanConnectSavedRoomBinding? TryGetSaveRoomBinding(string saveKey)
    {
        lock (Sync)
        {
            LanConnectSavedRoomBinding? binding = _data.SaveRoomBindings.FirstOrDefault(existing =>
                string.Equals(existing.SaveKey, saveKey, StringComparison.Ordinal));
            return binding == null ? null : CloneBinding(binding);
        }
    }

    public static void UpsertSaveRoomBinding(LanConnectSavedRoomBinding binding)
    {
        lock (Sync)
        {
            _data.SaveRoomBindings.RemoveAll(existing =>
                string.IsNullOrWhiteSpace(existing.SaveKey)
                || string.Equals(existing.SaveKey, binding.SaveKey, StringComparison.Ordinal));

            _data.SaveRoomBindings.Insert(0, CloneBinding(binding));
            if (_data.SaveRoomBindings.Count > 16)
            {
                _data.SaveRoomBindings.RemoveRange(16, _data.SaveRoomBindings.Count - 16);
            }

            SaveUnsafe();
        }
    }

    public static bool RemoveSaveRoomBinding(string saveKey)
    {
        lock (Sync)
        {
            int removed = _data.SaveRoomBindings.RemoveAll(existing =>
                string.Equals(existing.SaveKey, saveKey, StringComparison.Ordinal));
            if (removed <= 0)
            {
                return false;
            }

            SaveUnsafe();
            return true;
        }
    }

    public static string GetEffectivePlayerDisplayName()
    {
        string configured = PlayerDisplayName;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return SanitizePlayerDisplayName(System.Environment.UserName);
    }

    public static void Load()
    {
        lock (Sync)
        {
            string path = GetConfigPath();
            if (!File.Exists(path))
            {
                NormalizeDefaultsUnsafe();
                SaveUnsafe();
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                _data = JsonSerializer.Deserialize<LanConnectConfigData>(json) ?? new LanConnectConfigData();
                NormalizeDefaultsUnsafe();
            }
            catch (Exception ex)
            {
                Log.Warn($"sts2_lan_connect failed to read config: {ex.Message}");
                _data = new LanConnectConfigData();
                NormalizeDefaultsUnsafe();
                SaveUnsafe();
            }
        }
    }

    private static void SaveUnsafe()
    {
        string path = GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    private static string GetConfigPath()
    {
        string writableDirectory = LanConnectPaths.ResolveWritableDataDirectory();
        return Path.Combine(writableDirectory, ConfigFileName);
    }

    private static void SetString(Action<LanConnectConfigData, string> setter, Func<LanConnectConfigData, string> getter, string value)
    {
        lock (Sync)
        {
            if (getter(_data) == value)
            {
                return;
            }

            setter(_data, value);
            SaveUnsafe();
        }
    }

    private static void NormalizeDefaultsUnsafe()
    {
        if (LanConnectLobbyEndpointDefaults.IsLegacyLocalhostBaseUrl(_data.LobbyServerBaseUrl))
        {
            _data.LobbyServerBaseUrl = string.Empty;
        }
        else if (LanConnectLobbyEndpointDefaults.MatchesBundledDefaultBaseUrl(_data.LobbyServerBaseUrl))
        {
            _data.LobbyServerBaseUrl = string.Empty;
        }

        _data.LastRoomName = SanitizeRoomName(_data.LastRoomName);
        _data.PlayerDisplayName = SanitizePlayerDisplayName(_data.PlayerDisplayName);
        if (_data.RoomChatOffsetX.HasValue && !float.IsFinite(_data.RoomChatOffsetX.Value))
        {
            _data.RoomChatOffsetX = null;
        }
        if (_data.RoomChatOffsetY.HasValue && !float.IsFinite(_data.RoomChatOffsetY.Value))
        {
            _data.RoomChatOffsetY = null;
        }

        _data.SaveRoomBindings = _data.SaveRoomBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.SaveKey) && !string.IsNullOrWhiteSpace(binding.RoomName))
            .Select(CloneBinding)
            .Take(16)
            .ToList();
    }

    private static string NormalizeLobbyEndpointOverride(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static LanConnectSavedRoomBinding CloneBinding(LanConnectSavedRoomBinding binding)
    {
        return new LanConnectSavedRoomBinding
        {
            SaveKey = binding.SaveKey,
            RoomName = SanitizeRoomName(binding.RoomName),
            Password = binding.Password,
            GameMode = binding.GameMode,
            RunStartTime = binding.RunStartTime,
            PlayerCount = binding.PlayerCount,
            PlayerSignature = binding.PlayerSignature,
            UpdatedAtUnixSeconds = binding.UpdatedAtUnixSeconds
        };
    }

    public static string SanitizeRoomName(string? value)
    {
        return SanitizeText(value, MaxRoomNameLength);
    }

    public static string SanitizePlayerDisplayName(string? value)
    {
        return SanitizeText(value, MaxPlayerDisplayNameLength);
    }

    public static string SanitizeRoomPassword(string? value)
    {
        return SanitizeText(value, MaxRoomPasswordLength);
    }

    private static string SanitizeText(string? value, int maxLength)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }
}
