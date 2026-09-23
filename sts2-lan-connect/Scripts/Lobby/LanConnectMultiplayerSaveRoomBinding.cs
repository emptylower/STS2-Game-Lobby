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
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; }

    public string SaveKey { get; set; } = string.Empty;

    public string RoomName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string GameMode { get; set; } = LanConnectConstants.DefaultGameMode;

    public long RunStartTime { get; set; }

    public int PlayerCount { get; set; }

    public string PlayerSignature { get; set; } = string.Empty;

    public string PlayerNames { get; set; } = string.Empty;

    public long UpdatedAtUnixSeconds { get; set; }

    /// <summary>
    /// Raw persisted host channel ("lan" / "lobby"). Empty means legacy/missing; resolve via LanConnectHostChannels.Resolve.
    /// </summary>
    public string HostChannel { get; set; } = string.Empty;

    public string ProtocolProfileV2 { get; set; } = string.Empty;

    public int SelectedLanProtocolVersion { get; set; }

    public string ProtocolCarrier { get; set; } = string.Empty;

    public int ProtocolMaxPlayers { get; set; }

    public string MinimumClientVersion { get; set; } = string.Empty;

    public string ProtocolGameVersion { get; set; } = string.Empty;

    public string? WireCacheSignatureV1 { get; set; }

    public bool RitsuLibPresent { get; set; }

    public string CapabilityDigest { get; set; } = string.Empty;
}

internal sealed class LanConnectResolvedRoomBinding
{
    public int SchemaVersion { get; init; }

    public string SaveKey { get; init; } = string.Empty;

    public string RoomName { get; init; } = string.Empty;

    public string? Password { get; init; }

    public string GameMode { get; init; } = LanConnectConstants.DefaultGameMode;

    public bool HasStoredBinding { get; init; }

    /// <summary>
    /// Raw stored host channel (empty when missing/legacy or no stored binding).
    /// </summary>
    public string HostChannel { get; init; } = string.Empty;

    public string EffectiveHostChannel => LanConnectHostChannels.Resolve(HostChannel);

    public LanConnectProtocolSelection? ProtocolSelection { get; init; }

    public LanConnectProtocolFailure? ProtocolFailure { get; init; }
}

internal static class LanConnectMultiplayerSaveRoomBinding
{
    private static readonly FieldInfo? RunSaveManagerField = typeof(SaveManager).GetField("_runSaveManager", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? LoadMultiplayerRunSaveMethod = RunSaveManagerField?.FieldType.GetMethod("LoadMultiplayerRunSave", BindingFlags.Instance | BindingFlags.Public);

    public static bool TryLoadCurrentMultiplayerRun(out SerializableRun? run, out string failureReason)
    {
        run = null;
        // Do not gate on SaveManager.HasMultiplayerRunSave: BaseLib patches that getter into a
        // destructive load-and-validate pass (renames the save to *.corrupt on identity mismatch).
        // The raw load below answers "no save" via FileNotFound without touching the getter.
        ReadSaveResult<SerializableRun> readResult = LoadRawCurrentMultiplayerRun();
        if (!readResult.Success || readResult.SaveData == null)
        {
            failureReason = readResult.Status == ReadSaveStatus.FileNotFound
                ? "no_multiplayer_run_save"
                : $"load_failed:{readResult.Status}";
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
            LanConnectProtocolSelection? protocolSelection = TryRestoreProtocolSelection(
                storedBinding,
                out LanConnectProtocolFailure? protocolFailure);
            return new LanConnectResolvedRoomBinding
            {
                SaveKey = saveKey,
                RoomName = storedBinding.RoomName,
                Password = string.IsNullOrWhiteSpace(storedBinding.Password) ? null : storedBinding.Password,
                GameMode = string.IsNullOrWhiteSpace(storedBinding.GameMode) ? GetLobbyGameMode(run) : storedBinding.GameMode,
                HasStoredBinding = true,
                HostChannel = storedBinding.HostChannel ?? string.Empty,
                SchemaVersion = storedBinding.SchemaVersion,
                ProtocolSelection = protocolSelection,
                ProtocolFailure = protocolFailure
            };
        }

        return new LanConnectResolvedRoomBinding
        {
            SaveKey = saveKey,
            RoomName = GetFallbackRoomName(run),
            Password = null,
            GameMode = GetLobbyGameMode(run),
            HasStoredBinding = false,
            HostChannel = string.Empty,
            SchemaVersion = 0,
            ProtocolFailure = MissingProtocolSelectionFailure("No saved room binding exists for this multiplayer run.")
        };
    }

    internal static LanConnectProtocolFailure MissingProtocolSelectionFailure(string detail) =>
        LanConnectProtocolFailureMapper.FromLocal("saved_protocol_selection_missing", detail);

    internal static void PresentContinueRunProtocolFailure(LanConnectProtocolFailure failure)
    {
        if (failure.Code is "saved_protocol_selection_missing"
            or LanConnectDegradedMode.ConfigRecoveryRequiredCode
            or LanConnectDegradedMode.ProtocolPatchConflictCode)
        {
            LanConnectProtocolUiMessages.Present(failure);
            return;
        }

        LanConnectPopupUtil.ShowInfo(
            $"无法验证当前多人存档的原房间协议（{failure.Code}），已阻止续局建房。请从备份恢复包含此存档 saveRoomBindings 的 config.json 并重启游戏；若无备份，请导出诊断并联系 MOD 作者核实原协议。不要改用旧协议继续保存。");
    }

    public static bool PersistHostBinding(
        SerializableRun run,
        string roomName,
        string? password,
        string gameMode,
        string hostChannel,
        string source,
        LanConnectProtocolSelection? frozenSelection = null)
    {
        string trimmedRoomName = LanConnectConfig.SanitizeRoomName(roomName);
        if (string.IsNullOrWhiteSpace(trimmedRoomName))
        {
            GD.Print($"sts2_lan_connect save_binding: skip persist because room name is empty. source={source}");
            return false;
        }

        if (!LanConnectHostChannels.IsValid(hostChannel))
        {
            GD.Print(
                $"sts2_lan_connect save_binding: skip persist because hostChannel is invalid. source={source}, hostChannel={LanConnectHostChannels.DescribePersisted(hostChannel)}");
            return false;
        }

        string normalizedHostChannel = hostChannel.Trim().ToLowerInvariant();
        LanConnectSessionProtocolSnapshot snapshot = LanConnectSessionProtocolState.Shared.Current;
        LanConnectProtocolSelection? selection = ResolvePersistableHostSelection(
            snapshot,
            frozenSelection,
            LanConnectProtocolOffer.CreateCurrent());
        if (selection == null)
        {
            GD.Print(
                $"sts2_lan_connect save_binding: skip persist without validated frozen host selection. source={source}, saveKey={BuildSaveKey(run)}, phase={snapshot.Phase}, role={snapshot.Role}, explicitSelection={frozenSelection != null}");
            return false;
        }

        LanConnectSavedRoomBinding binding = new()
        {
            SchemaVersion = LanConnectSavedRoomBinding.CurrentSchemaVersion,
            SaveKey = BuildSaveKey(run),
            RoomName = trimmedRoomName,
            Password = string.IsNullOrWhiteSpace(password) ? string.Empty : LanConnectConfig.SanitizeRoomPassword(password),
            GameMode = string.IsNullOrWhiteSpace(gameMode) ? GetLobbyGameMode(run) : gameMode.Trim(),
            RunStartTime = run.StartTime,
            PlayerCount = run.Players.Count,
            PlayerSignature = BuildPlayerSignature(run),
            PlayerNames = BuildPlayerNamesForPersist(run),
            UpdatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            HostChannel = normalizedHostChannel
        };
        binding.ProtocolProfileV2 = selection.Profile.ToCanonical();
        binding.SelectedLanProtocolVersion = selection.SelectedLanProtocolVersion;
        binding.ProtocolCarrier = selection.Carrier.ToWireValue();
        binding.ProtocolMaxPlayers = selection.MaxPlayers;
        binding.MinimumClientVersion = selection.MinimumClientVersion;
        binding.ProtocolGameVersion = selection.GameVersion;
        binding.WireCacheSignatureV1 = selection.WireCacheSignature;
        binding.RitsuLibPresent = selection.RitsuLibPresent;
        binding.CapabilityDigest = selection.CapabilityDigest;

        if (!LanConnectConfig.UpsertSaveRoomBinding(binding))
        {
            GD.Print(
                $"sts2_lan_connect save_binding: persist refused by config store source={source}, saveKey={binding.SaveKey}");
            return false;
        }
        GD.Print(
            $"sts2_lan_connect save_binding: persisted source={source}, saveKey={binding.SaveKey}, protocolSource={(frozenSelection == null ? "active_host" : "captured_host")}, profile={binding.ProtocolProfileV2}, carrier={binding.ProtocolCarrier}, roomName='{binding.RoomName}', hostChannel={binding.HostChannel}, passwordSet={!string.IsNullOrWhiteSpace(binding.Password)}, playerCount={binding.PlayerCount}, signature={binding.PlayerSignature}");
        return true;
    }

    internal static LanConnectProtocolSelection? ResolvePersistableHostSelection(
        LanConnectSessionProtocolSnapshot snapshot,
        LanConnectProtocolSelection? frozenSelection,
        LanConnectProtocolOffer offer)
    {
        if (snapshot.Phase == LanConnectSessionProtocolPhase.Tentative
            || snapshot.Role == LanConnectSessionProtocolRole.Client
            || (snapshot.Selection != null && frozenSelection != null && snapshot.Selection != frozenSelection))
        {
            return null;
        }

        // A delayed save can finish after the host lease is released. In that case the
        // caller must pass the selection captured from the host session or binding intent.
        LanConnectProtocolSelection? selection = frozenSelection
            ?? (snapshot.Phase == LanConnectSessionProtocolPhase.Frozen
                && snapshot.Role == LanConnectSessionProtocolRole.Host
                    ? snapshot.Selection
                    : null);
        if (selection == null)
        {
            return null;
        }

        try
        {
            return selection.Validate(offer);
        }
        catch (LanConnectProtocolException)
        {
            return null;
        }
    }

    internal static LanConnectProtocolSelection? TryRestoreProtocolSelection(
        LanConnectSavedRoomBinding? binding,
        out LanConnectProtocolFailure? failure,
        LanConnectProtocolOffer? offer = null,
        int? legacyMaxPlayers = null,
        string? legacyGameVersion = null,
        string? legacyWireCacheSignature = null)
    {
        failure = null;
        if (binding == null)
        {
            failure = MissingProtocolSelectionFailure("No saved room binding exists for this multiplayer run.");
            return null;
        }

        if (binding.SchemaVersion is 0 or 1)
        {
            // Schema 2 introduced tail_v1 together with protocol persistence. An
            // unmodified schema 0/1 record therefore identifies an old compat room.
            if (binding.SelectedLanProtocolVersion != 0
                || binding.ProtocolMaxPlayers != 0
                || binding.RitsuLibPresent
                || !string.IsNullOrEmpty(binding.ProtocolProfileV2)
                || !string.IsNullOrEmpty(binding.ProtocolCarrier)
                || !string.IsNullOrEmpty(binding.MinimumClientVersion)
                || !string.IsNullOrEmpty(binding.ProtocolGameVersion)
                || !string.IsNullOrEmpty(binding.WireCacheSignatureV1)
                || !string.IsNullOrEmpty(binding.CapabilityDigest))
            {
                failure = MissingProtocolSelectionFailure("Legacy binding contains inconsistent protocol fields.");
                return null;
            }

            try
            {
                int maxPlayers = Math.Clamp(
                    Math.Max(binding.PlayerCount, legacyMaxPlayers ?? LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers()),
                    LanConnectConstants.ProtocolMinPlayers,
                    LanConnectConstants.ProtocolMaxPlayers);
                LanConnectProtocolSelection compat = LanConnectProtocolSelection.CreateLocalCompat(
                    maxPlayers,
                    legacyGameVersion ?? LanConnectBuildInfo.GetGameVersion(),
                    legacyWireCacheSignature ?? LanConnectWireCacheDiagnostics.GetCurrentResult().Snapshot?.Signature);
                return compat.Validate(offer ?? LanConnectProtocolOffer.CreateCurrent());
            }
            catch (LanConnectProtocolException exception)
            {
                failure = exception.Failure;
                return null;
            }
        }

        if (binding.SchemaVersion < 2
            || string.IsNullOrWhiteSpace(binding.ProtocolProfileV2)
            || string.IsNullOrWhiteSpace(binding.ProtocolCarrier)
            || string.IsNullOrWhiteSpace(binding.MinimumClientVersion)
            || string.IsNullOrWhiteSpace(binding.ProtocolGameVersion)
            || binding.ProtocolMaxPlayers == 0
            || string.IsNullOrWhiteSpace(binding.CapabilityDigest))
        {
            failure = MissingProtocolSelectionFailure("Saved room protocol fields are missing or incomplete.");
            return null;
        }

        try
        {
            // registry fingerprint 不持久化：重建 selection 只恢复结构字段；重发布房间时
            // 经 CreateCurrent() 以本机注册表即时重算并随 offer 上传（spec §5 字段级合同）。
            // 旧载体绑定在此被 Validate 以 lan_legacy_carrier_unsupported 拒绝 → 回退为重新发布。
            LanConnectProtocolSelection selection = new(
                LanConnectProtocolProfileExtensions.ParseCanonical(binding.ProtocolProfileV2),
                binding.SelectedLanProtocolVersion,
                LanConnectProtocolProfileExtensions.ParseCarrier(binding.ProtocolCarrier),
                binding.MinimumClientVersion,
                binding.ProtocolMaxPlayers,
                binding.ProtocolGameVersion,
                binding.WireCacheSignatureV1,
                binding.RitsuLibPresent,
                binding.CapabilityDigest);
            return selection.Validate(offer ?? LanConnectProtocolOffer.CreateCurrent());
        }
        catch (LanConnectProtocolException exception)
        {
            failure = exception.Failure;
            return null;
        }
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

    internal static bool IsUnboundNativeSteamRun(SerializableRun run) =>
        IsUnboundNativeSteamRun(
            run.PlatformType == PlatformType.Steam,
            LanConnectConfig.TryGetSaveRoomBinding(BuildSaveKey(run)) != null);

    internal static bool IsUnboundNativeSteamRun(bool isSteamPlatform, bool hasStoredBinding) =>
        isSteamPlatform && !hasStoredBinding;

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
