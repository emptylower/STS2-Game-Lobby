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

        try
        {
            NetErrorInfo? error = netService.StartENetHost(LanConnectConstants.DefaultPort, maxPlayers);
            if (error.HasValue)
            {
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

    public static async Task<bool> StartLobbyHostAsync(string roomName, string? password, Control loadingOverlay, NSubmenuStack stack)
    {
        loadingOverlay.Visible = true;
        NetHostGameService netService = new();
        int maxPlayers = LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();

        GD.Print(
            $"sts2_lan_connect host_flow: start lobby host roomName='{roomName}', passwordSet={!string.IsNullOrWhiteSpace(password)}, player='{LanConnectConfig.GetEffectivePlayerDisplayName()}', localAddressCount={LanConnectNetUtil.GetLanAddressStrings().Count}");

        try
        {
            NetErrorInfo? error = netService.StartENetHost(LanConnectConstants.DefaultPort, maxPlayers);
            if (error.HasValue)
            {
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
                GameMode.Standard,
                publishSource: "overlay_create",
                boundSaveKey: null,
                savedRunInfo: null,
                maxPlayers,
                notifyOnFailure: true);
            if (!published)
            {
                netService.Disconnect(NetError.InternalError, now: true);
                return false;
            }

            PushHostScreen(GameMode.Standard, stack, netService, maxPlayers);
            await Task.Yield();

            string primaryAddress = LanConnectNetUtil.GetPrimaryLanAddress();
            string lockStatus = string.IsNullOrWhiteSpace(password) ? "无密码" : "已加锁";
            LanConnectPopupUtil.ShowInfo(
                $"大厅房间已发布。\n房间名：{roomName}\n状态：{lockStatus}\n本地 ENet：{primaryAddress}:{LanConnectConstants.DefaultPort}\n好友现在可以从“游戏大厅”直接加入。");
            return true;
        }
        catch (LobbyServiceException ex)
        {
            netService.Disconnect(NetError.InternalError, now: true);
            GD.Print($"sts2_lan_connect host_flow: lobby create failed code={ex.Code}, status={ex.StatusCode}, message={ex.Message}");
            LanConnectPopupUtil.ShowInfo($"大厅服务创建房间失败：{ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            netService.Disconnect(NetError.InternalError, now: true);
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
        bool notifyOnFailure)
    {
        string trimmedRoomName = roomName.Trim();
        string? trimmedPassword = string.IsNullOrWhiteSpace(password) ? null : password.Trim();
        LobbyApiClient? apiClient = null;
        string playerName = LanConnectConfig.GetEffectivePlayerDisplayName();
        string lobbyGameMode = LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode);
        int localAddressCount = LanConnectNetUtil.GetLanAddressStrings().Count;

        GD.Print(
            $"sts2_lan_connect host_flow: publish existing host source={publishSource}, roomName='{trimmedRoomName}', passwordSet={!string.IsNullOrWhiteSpace(trimmedPassword)}, gameMode={lobbyGameMode}, player='{playerName}', platform={netService.Platform}, localAddressCount={localAddressCount}, saveKey={(boundSaveKey ?? "<none>")}");

        try
        {
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
                    SavedRun = savedRunInfo
                });
            apiClient = null;
            GD.Print($"sts2_lan_connect host_flow: attached hosted room session roomId={registration.RoomId}, source={publishSource}");
            return true;
        }
        catch (LobbyServiceException ex)
        {
            apiClient?.Dispose();
            GD.Print(
                $"sts2_lan_connect host_flow: publish existing host failed source={publishSource}, code={ex.Code}, status={ex.StatusCode}, message={ex.Message}");
            if (notifyOnFailure)
            {
                LanConnectPopupUtil.ShowInfo($"大厅服务创建房间失败：{ex.Message}");
            }

            return false;
        }
        catch (Exception ex)
        {
            apiClient?.Dispose();
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
