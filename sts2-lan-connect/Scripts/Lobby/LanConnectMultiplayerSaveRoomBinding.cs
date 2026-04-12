using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectSavedRoomBinding
{
    public string SaveKey { get; set; } = string.Empty;

    public string RoomName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string GameMode { get; set; } = LanConnectConstants.DefaultGameMode;

    public long RunStartTime { get; set; }

    public int PlayerCount { get; set; }

    public string PlayerSignature { get; set; } = string.Empty;

    public string PlayerNames { get; set; } = string.Empty;

    public long UpdatedAtUnixSeconds { get; set; }
}

internal sealed class LanConnectResolvedRoomBinding
{
    public string SaveKey { get; init; } = string.Empty;

    public string RoomName { get; init; } = string.Empty;

    public string? Password { get; init; }

    public string GameMode { get; init; } = LanConnectConstants.DefaultGameMode;

    public bool HasStoredBinding { get; init; }
}

internal static class LanConnectMultiplayerSaveRoomBinding
{
    private static readonly FieldInfo? RunSaveManagerField = typeof(SaveManager).GetField("_runSaveManager", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? LoadMultiplayerRunSaveMethod = RunSaveManagerField?.FieldType.GetMethod("LoadMultiplayerRunSave", BindingFlags.Instance | BindingFlags.Public);

    public static bool TryLoadCurrentMultiplayerRun(out SerializableRun? run, out string failureReason)
    {
        run = null;
        if (!SaveManager.Instance.HasMultiplayerRunSave)
        {
            failureReason = "no_multiplayer_run_save";
            return false;
        }

        ReadSaveResult<SerializableRun> readResult = LoadRawCurrentMultiplayerRun();
        if (!readResult.Success || readResult.SaveData == null)
        {
            failureReason = $"load_failed:{readResult.Status}";
            return false;
        }

        ulong localPlayerId = ResolveCanonicalLocalPlayerId(readResult.SaveData);
        try
        {
            run = RunManager.CanonicalizeSave(readResult.SaveData, localPlayerId);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = $"canonicalize_failed:{ex.GetType().Name}";
            GD.Print(
                $"sts2_lan_connect save_binding: canonicalize failed localPlayerId={localPlayerId}, playerIds={string.Join(',', readResult.SaveData.Players.Select(static player => player.NetId))}, reason={ex.Message}");
            return false;
        }
    }

    public static LanConnectResolvedRoomBinding Resolve(SerializableRun run)
    {
        string saveKey = BuildSaveKey(run);
        LanConnectSavedRoomBinding? storedBinding = LanConnectConfig.TryGetSaveRoomBinding(saveKey);
        if (storedBinding != null)
        {
            return new LanConnectResolvedRoomBinding
            {
                SaveKey = saveKey,
                RoomName = storedBinding.RoomName,
                Password = string.IsNullOrWhiteSpace(storedBinding.Password) ? null : storedBinding.Password,
                GameMode = string.IsNullOrWhiteSpace(storedBinding.GameMode) ? GetLobbyGameMode(run) : storedBinding.GameMode,
                HasStoredBinding = true
            };
        }

        return new LanConnectResolvedRoomBinding
        {
            SaveKey = saveKey,
            RoomName = GetFallbackRoomName(run),
            Password = null,
            GameMode = GetLobbyGameMode(run),
            HasStoredBinding = false
        };
    }

    public static void PersistBinding(SerializableRun run, string roomName, string? password, string gameMode, string source)
    {
        string trimmedRoomName = LanConnectConfig.SanitizeRoomName(roomName);
        if (string.IsNullOrWhiteSpace(trimmedRoomName))
        {
            GD.Print($"sts2_lan_connect save_binding: skip persist because room name is empty. source={source}");
            return;
        }

        LanConnectSavedRoomBinding binding = new()
        {
            SaveKey = BuildSaveKey(run),
            RoomName = trimmedRoomName,
            Password = string.IsNullOrWhiteSpace(password) ? string.Empty : LanConnectConfig.SanitizeRoomPassword(password),
            GameMode = string.IsNullOrWhiteSpace(gameMode) ? GetLobbyGameMode(run) : gameMode.Trim(),
            RunStartTime = run.StartTime,
            PlayerCount = run.Players.Count,
            PlayerSignature = BuildPlayerSignature(run),
            PlayerNames = BuildPlayerNamesForPersist(run),
            UpdatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        LanConnectConfig.UpsertSaveRoomBinding(binding);
        GD.Print(
            $"sts2_lan_connect save_binding: persisted source={source}, saveKey={binding.SaveKey}, roomName='{binding.RoomName}', passwordSet={!string.IsNullOrWhiteSpace(binding.Password)}, playerCount={binding.PlayerCount}, signature={binding.PlayerSignature}");
    }

    public static string GetLobbyGameMode(GameMode gameMode)
    {
        return gameMode switch
        {
            GameMode.Standard => "standard",
            GameMode.Custom => "custom",
            GameMode.Daily => "daily",
            _ => LanConnectConstants.DefaultGameMode
        };
    }

    public static string GetLobbyGameModeLabel(GameMode gameMode)
    {
        return GetLobbyGameModeLabel(GetLobbyGameMode(gameMode));
    }

    public static string GetLobbyGameModeLabel(string? gameMode)
    {
        return gameMode?.Trim().ToLowerInvariant() switch
        {
            "daily" => "多人每日挑战",
            "custom" => "自定义模式",
            "standard" or "" or null => "标准模式",
            _ => gameMode.Trim()
        };
    }

    public static string GetLobbyGameMode(SerializableRun run)
    {
        if (run.Modifiers.Count == 0)
        {
            return "standard";
        }

        return run.DailyTime.HasValue ? "daily" : "custom";
    }

    public static LobbySavedRunInfo BuildSavedRunInfo(SerializableRun run, ulong hostNetId, Dictionary<ulong, string>? storedPlayerNames = null)
    {
        return new LobbySavedRunInfo
        {
            SaveKey = BuildSaveKey(run),
            ConnectedPlayerNetIds = new() { hostNetId.ToString(CultureInfo.InvariantCulture) },
            Slots = run.Players
                .OrderBy(player => player.NetId)
                .Select(player => BuildSavedRunSlot(player, hostNetId, storedPlayerNames))
                .ToList()
        };
    }

    public static string BuildSaveKey(SerializableRun run)
    {
        string descriptor = string.Join("|", new[]
        {
            $"mode={GetLobbyGameMode(run)}",
            $"start={run.StartTime}",
            $"daily={run.DailyTime?.ToUnixTimeSeconds() ?? 0}",
            $"asc={run.Ascension}",
            $"players={BuildPlayerSignature(run)}"
        });

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(descriptor));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string GetPlayerSignature(SerializableRun run)
    {
        return BuildPlayerSignature(run);
    }

    private static string BuildPlayerSignature(SerializableRun run)
    {
        return string.Join(",",
            run.Players
                .OrderBy(player => player.NetId)
                .Select(player => $"{player.NetId}:{player.CharacterId?.Entry ?? "unknown"}"));
    }

    private static string GetFallbackRoomName(SerializableRun run)
    {
        return GetLobbyGameMode(run) switch
        {
            "daily" => "每日续局房间",
            "custom" => "自定义续局房间",
            _ => "续局联机房间"
        };
    }

    private static LobbySavedRunSlot BuildSavedRunSlot(SerializablePlayer player, ulong hostNetId, Dictionary<ulong, string>? storedPlayerNames)
    {
        string? playerName = LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(player.NetId);
        if (string.IsNullOrWhiteSpace(playerName))
        {
            storedPlayerNames?.TryGetValue(player.NetId, out playerName);
        }

        return new LobbySavedRunSlot
        {
            NetId = player.NetId.ToString(CultureInfo.InvariantCulture),
            CharacterId = player.CharacterId?.Entry ?? string.Empty,
            CharacterName = ResolveCharacterName(player),
            PlayerName = playerName ?? string.Empty,
            IsHost = player.NetId == hostNetId,
            IsConnected = player.NetId == hostNetId
        };
    }

    private static string ResolveCharacterName(SerializablePlayer player)
    {
        if (player.CharacterId == null)
        {
            return "未知角色";
        }

        try
        {
            CharacterModel model = ModelDb.GetById<CharacterModel>(player.CharacterId);
            return model.Title.GetFormattedText();
        }
        catch
        {
            return player.CharacterId.Entry;
        }
    }

    private static ReadSaveResult<SerializableRun> LoadRawCurrentMultiplayerRun()
    {
        try
        {
            if (RunSaveManagerField?.GetValue(SaveManager.Instance) is not object runSaveManager || LoadMultiplayerRunSaveMethod == null)
            {
                return new ReadSaveResult<SerializableRun>(ReadSaveStatus.Unrecoverable, "RunSaveManager reflection unavailable.");
            }

            object? result = LoadMultiplayerRunSaveMethod.Invoke(runSaveManager, Array.Empty<object>());
            return result as ReadSaveResult<SerializableRun>
                ?? new ReadSaveResult<SerializableRun>(ReadSaveStatus.Unrecoverable, "LoadMultiplayerRunSave returned unexpected result.");
        }
        catch (Exception ex)
        {
            return new ReadSaveResult<SerializableRun>(ReadSaveStatus.Unrecoverable, ex.Message);
        }
    }

    private static ulong ResolveCanonicalLocalPlayerId(SerializableRun run)
    {
        INetGameService? netService = RunManager.Instance.NetService;
        if (RunManager.Instance.IsInProgress
            && netService != null
            && netService.Type.IsMultiplayer()
            && netService.Platform == PlatformType.None
            && netService.IsConnected)
        {
            return netService.NetId;
        }

        ulong platformLocalPlayerId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform);
        if (run.Players.Any(player => player.NetId == platformLocalPlayerId))
        {
            return platformLocalPlayerId;
        }

        if (PlatformUtil.PrimaryPlatform == PlatformType.None)
        {
            SerializablePlayer? hostPlayer = run.Players.FirstOrDefault(player => player.NetId == 1UL);
            if (hostPlayer != null)
            {
                return hostPlayer.NetId;
            }
        }

        return run.Players.First().NetId;
    }

    public static Dictionary<ulong, string> ParsePlayerNames(string? playerNames)
    {
        var result = new Dictionary<ulong, string>();
        if (string.IsNullOrWhiteSpace(playerNames))
        {
            return result;
        }

        foreach (string entry in playerNames.Split(','))
        {
            int sep = entry.IndexOf(':');
            if (sep <= 0 || sep >= entry.Length - 1)
            {
                continue;
            }

            if (ulong.TryParse(entry[..sep], NumberStyles.None, CultureInfo.InvariantCulture, out ulong netId))
            {
                string name = LanConnectConfig.SanitizePlayerDisplayName(entry[(sep + 1)..]);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result[netId] = name;
                }
            }
        }

        return result;
    }

    private static string BuildPlayerNamesForPersist(SerializableRun run)
    {
        string fromDirectory = BuildPlayerNamesFromDirectory(run);
        if (!string.IsNullOrWhiteSpace(fromDirectory))
        {
            return fromDirectory;
        }

        LanConnectSavedRoomBinding? existing = LanConnectConfig.TryGetSaveRoomBinding(BuildSaveKey(run));
        return existing?.PlayerNames ?? string.Empty;
    }

    private static string BuildPlayerNamesFromDirectory(SerializableRun run)
    {
        var entries = run.Players
            .OrderBy(player => player.NetId)
            .Select(player =>
            {
                string? name = LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(player.NetId);
                return string.IsNullOrWhiteSpace(name)
                    ? null
                    : $"{player.NetId}:{name}";
            })
            .Where(entry => entry != null);
        return string.Join(",", entries);
    }
}
