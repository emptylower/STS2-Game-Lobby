using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectHostFlow
{
    private static bool _useLanHostOnce;

    public static void QueueLanHost()
    {
        _useLanHostOnce = true;
    }

    public static bool ConsumeQueuedLanHost()
    {
        if (!_useLanHostOnce)
        {
            return false;
        }

        _useLanHostOnce = false;
        return true;
    }

    public static async Task StartLanHostAsync(GameMode gameMode, Control loadingOverlay, NSubmenuStack stack)
    {
        loadingOverlay.Visible = true;
        NetHostGameService netService = new();
        int maxPlayers = LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();
        string protocolProfile = LanConnectProtocolProfiles.DetermineProfileForMaxPlayers(maxPlayers);

        try
        {
            LanConnectProtocolProfiles.SetActiveProfile(protocolProfile, "start_lan_host");
            NetErrorInfo? error = netService.StartENetHost(LanConnectConstants.DefaultPort, maxPlayers);
            if (error.HasValue)
            {
                LanConnectProtocolProfiles.ResetActiveProfile("start_lan_host_failed");
                NErrorPopup? popup = NErrorPopup.Create(error.Value);
                if (popup != null)
                {
                    NModalContainer.Instance?.Add(popup);
                }

                return;
            }

            PushHostScreen(gameMode, stack, netService, maxPlayers);

            await Task.Yield();
            string ip = LanConnectNetUtil.GetPrimaryLanAddress();
            LanConnectPopupUtil.ShowInfo($"LAN 主机已启动。\n把这个地址发给好友：{ip}:{LanConnectConstants.DefaultPort}");
        }
        catch
        {
            LanConnectProtocolProfiles.ResetActiveProfile("start_lan_host_exception");
            NErrorPopup? popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            if (popup != null)
            {
                NModalContainer.Instance?.Add(popup);
            }

            throw;
        }
        finally
        {
            loadingOverlay.Visible = false;
        }
    }

    public static async Task<bool> StartLobbyHostAsync(string roomName, string? password, GameMode gameMode, Control loadingOverlay, NSubmenuStack stack, int? maxPlayersOverride = null)
    {
        loadingOverlay.Visible = true;
        NetHostGameService netService = new();
        int maxPlayers = maxPlayersOverride ?? LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();
        string protocolProfile = LanConnectProtocolProfiles.DetermineProfileForMaxPlayers(maxPlayers);
        string lobbyGameMode = LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode);
        string gameModeLabel = LanConnectMultiplayerSaveRoomBinding.GetLobbyGameModeLabel(gameMode);

        GD.Print(
            $"sts2_lan_connect host_flow: start lobby host roomName='{roomName}', passwordSet={!string.IsNullOrWhiteSpace(password)}, gameMode={lobbyGameMode}, player='{LanConnectConfig.GetEffectivePlayerDisplayName()}', localAddressCount={LanConnectNetUtil.GetLanAddressStrings().Count}");

        try
        {
            LanConnectProtocolProfiles.SetActiveProfile(protocolProfile, "start_lobby_host");
            NetErrorInfo? error = netService.StartENetHost(LanConnectConstants.DefaultPort, maxPlayers);
            if (error.HasValue)
            {
                LanConnectProtocolProfiles.ResetActiveProfile("start_lobby_host_failed");
                GD.Print($"sts2_lan_connect host_flow: ENet host failed with {error.Value}");
                NErrorPopup? popup = NErrorPopup.Create(error.Value);
                if (popup != null)
                {
                    NModalContainer.Instance?.Add(popup);
                }

                return false;
            }

            GD.Print("sts2_lan_connect host_flow: ENet host started, registering room with lobby service.");
            bool published = await PublishExistingHostToLobbyAsync(
                netService,
                roomName,
                password,
                gameMode,
                publishSource: "overlay_create",
                boundSaveKey: null,
                savedRunInfo: null,
                maxPlayers,
                notifyOnFailure: true,
                throwOnCreateGuardRejection: true);
            if (!published)
            {
                netService.Disconnect(NetError.InternalError, now: true);
                LanConnectProtocolProfiles.ResetActiveProfile("publish_existing_host_failed");
                return false;
            }

            PushHostScreen(gameMode, stack, netService, maxPlayers);
            await Task.Yield();

            string primaryAddress = LanConnectNetUtil.GetPrimaryLanAddress();
            string lockStatus = string.IsNullOrWhiteSpace(password) ? "无密码" : "已加锁";
            LanConnectPopupUtil.ShowInfo(
                $"大厅房间已发布。\n房间名：{roomName}\n模式：{gameModeLabel}\n状态：{lockStatus}\n本地 ENet：{primaryAddress}:{LanConnectConstants.DefaultPort}\n好友现在可以从“游戏大厅”直接加入。");
            return true;
        }
        catch (LobbyServiceException ex)
        {
            netService.Disconnect(NetError.InternalError, now: true);
            LanConnectProtocolProfiles.ResetActiveProfile("start_lobby_host_service_exception");
            GD.Print($"sts2_lan_connect host_flow: lobby create failed code={ex.Code}, status={ex.StatusCode}, message={ex.Message}");
            if (string.Equals(ex.Code, "server_bandwidth_near_capacity", StringComparison.Ordinal))
            {
                throw;
            }
            LanConnectPopupUtil.ShowInfo($"大厅服务创建房间失败：{ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            netService.Disconnect(NetError.InternalError, now: true);
            LanConnectProtocolProfiles.ResetActiveProfile("start_lobby_host_exception");
            GD.Print($"sts2_lan_connect host_flow: unexpected exception during host create -> {ex}");
            NErrorPopup? popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            if (popup != null)
            {
                NModalContainer.Instance?.Add(popup);
            }

            throw;
        }
        finally
        {
            GD.Print("sts2_lan_connect host_flow: create flow finished.");
            loadingOverlay.Visible = false;
        }
    }

    public static async Task<bool> PublishExistingHostToLobbyAsync(
        NetHostGameService netService,
        string roomName,
        string? password,
        GameMode gameMode,
        string publishSource,
        string? boundSaveKey,
        LobbySavedRunInfo? savedRunInfo,
        int maxPlayers,
        bool notifyOnFailure,
        bool throwOnCreateGuardRejection = false)
    {
        string trimmedRoomName = LanConnectConfig.SanitizeRoomName(roomName);
        string? trimmedPassword = string.IsNullOrWhiteSpace(password) ? null : LanConnectConfig.SanitizeRoomPassword(password);
        LobbyApiClient? apiClient = null;
        string playerName = LanConnectConfig.GetEffectivePlayerDisplayName();
        string lobbyGameMode = LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode);
        int localAddressCount = LanConnectNetUtil.GetLanAddressStrings().Count;
        string protocolProfile = LanConnectProtocolProfiles.DetermineProfileForMaxPlayers(maxPlayers);

        GD.Print(
            $"sts2_lan_connect host_flow: publish existing host source={publishSource}, roomName='{trimmedRoomName}', passwordSet={!string.IsNullOrWhiteSpace(trimmedPassword)}, gameMode={lobbyGameMode}, player='{playerName}', platform={netService.Platform}, localAddressCount={localAddressCount}, saveKey={(boundSaveKey ?? "<none>")}");

        try
        {
            LanConnectProtocolProfiles.SetActiveProfile(protocolProfile, $"publish_existing_host:{publishSource}");
            apiClient = LobbyApiClient.CreateConfigured();
            LobbyCreateRoomResponse registration = await apiClient.CreateRoomAsync(new LobbyCreateRoomRequest
            {
                RoomName = trimmedRoomName,
                Password = trimmedPassword,
                HostPlayerName = playerName,
                GameMode = lobbyGameMode,
                Version = LanConnectBuildInfo.GetGameVersion(),
                ModVersion = LanConnectBuildInfo.GetModVersion(),
                ModList = LanConnectBuildInfo.GetModList(),
                ProtocolProfile = protocolProfile,
                MaxPlayers = maxPlayers,
                HostConnectionInfo = new LobbyHostConnectionInfo
                {
                    EnetPort = LanConnectConstants.DefaultPort,
                    LocalAddresses = LanConnectNetUtil.GetLanAddressStrings().ToList()
                },
                SavedRun = savedRunInfo
            });

            GD.Print(
                $"sts2_lan_connect host_flow: lobby room registered roomId={registration.RoomId}, controlChannelId={registration.ControlChannelId}, heartbeat={registration.HeartbeatIntervalSeconds}s, source={publishSource}");

            LanConnectConfig.LastRoomName = trimmedRoomName;
            if (LanConnectLobbyRuntime.Instance == null)
            {
                apiClient.Dispose();
                apiClient = null;
                GD.Print("sts2_lan_connect host_flow: runtime missing after room registration, host session cannot attach.");
                if (notifyOnFailure)
                {
                    LanConnectPopupUtil.ShowInfo("大厅后台运行时未安装，无法托管房主会话。请重启游戏后重试。");
                }

                return false;
            }

            LanConnectLobbyRuntime.Instance.AttachHostedRoom(
                netService,
                apiClient,
                registration,
                new LanConnectHostedRoomMetadata
                {
                    RoomName = trimmedRoomName,
                    Password = trimmedPassword,
                    GameMode = lobbyGameMode,
                    PublishSource = publishSource,
                    SaveKey = boundSaveKey,
                    SavedRun = savedRunInfo,
                    ProtocolProfile = protocolProfile
                });
            apiClient = null;
            GD.Print($"sts2_lan_connect host_flow: attached hosted room session roomId={registration.RoomId}, source={publishSource}");
            return true;
        }
        catch (LobbyServiceException ex)
        {
            apiClient?.Dispose();
            LanConnectProtocolProfiles.ResetActiveProfile($"publish_existing_host_failed:{publishSource}");
            GD.Print(
                $"sts2_lan_connect host_flow: publish existing host failed source={publishSource}, code={ex.Code}, status={ex.StatusCode}, message={ex.Message}");
            if (throwOnCreateGuardRejection && string.Equals(ex.Code, "server_bandwidth_near_capacity", StringComparison.Ordinal))
            {
                throw;
            }
            if (notifyOnFailure)
            {
                LanConnectPopupUtil.ShowInfo($"大厅服务创建房间失败：{ex.Message}");
            }

            return false;
        }
        catch (Exception ex)
        {
            apiClient?.Dispose();
            LanConnectProtocolProfiles.ResetActiveProfile($"publish_existing_host_exception:{publishSource}");
            GD.Print($"sts2_lan_connect host_flow: unexpected exception during publish source={publishSource} -> {ex}");
            if (notifyOnFailure)
            {
                NErrorPopup? popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
                if (popup != null)
                {
                    NModalContainer.Instance?.Add(popup);
                }
            }

            return false;
        }
    }

    private static void PushHostScreen(GameMode gameMode, NSubmenuStack stack, NetHostGameService netService, int maxPlayers)
    {
        switch (gameMode)
        {
            case GameMode.Standard:
            {
                NCharacterSelectScreen submenu = stack.GetSubmenuType<NCharacterSelectScreen>();
                submenu.InitializeMultiplayerAsHost(netService, maxPlayers);
                stack.Push(submenu);
                break;
            }
            case GameMode.Daily:
            {
                NDailyRunScreen submenu = stack.GetSubmenuType<NDailyRunScreen>();
                submenu.InitializeMultiplayerAsHost(netService);
                stack.Push(submenu);
                break;
            }
            default:
            {
                NCustomRunScreen submenu = stack.GetSubmenuType<NCustomRunScreen>();
                submenu.InitializeMultiplayerAsHost(netService, maxPlayers);
                stack.Push(submenu);
                break;
            }
        }
    }
}
