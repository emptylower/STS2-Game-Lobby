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
        int maxPlayers = Math.Clamp(
            LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers(),
            LanConnectConstants.ProtocolMinPlayers,
            LanConnectConstants.ProtocolMaxPlayers);
        string lobbyGameMode = LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode);
        LanConnectSessionProtocolLease? protocolLease = null;
        bool leaseTransferred = false;

        GD.Print(
            $"sts2_lan_connect host_flow: start LAN host gameMode={lobbyGameMode}, port={LanConnectConstants.DefaultPort}, maxPlayers={maxPlayers}");

        try
        {
            LanConnectProtocolSelection selection = LanConnectProtocolSelection.CreateLocalCompat(
                maxPlayers,
                LanConnectBuildInfo.GetGameVersion(),
                LanConnectWireCacheDiagnostics.GetCurrentResult().Snapshot?.Signature);
            protocolLease = LanConnectSessionProtocolState.Shared.FreezeHost(
                selection,
                BuildHostLeaseOwner(netService));
            NetErrorInfo? error = netService.StartENetHost(LanConnectConstants.DefaultPort, maxPlayers);
            if (error.HasValue)
            {
                protocolLease.Dispose();
                GD.Print(
                    $"sts2_lan_connect host_flow: LAN host failed gameMode={lobbyGameMode}, port={LanConnectConstants.DefaultPort}, reason={error.Value}");
                NErrorPopup? popup = NErrorPopup.Create(error.Value);
                if (popup != null)
                {
                    NModalContainer.Instance?.Add(popup);
                }

                return;
            }

            GD.Print(
                $"sts2_lan_connect host_flow: LAN ENet host started gameMode={lobbyGameMode}, port={LanConnectConstants.DefaultPort}");

            if (LanConnectLobbyRuntime.Instance != null)
            {
                LanConnectLobbyRuntime.Instance.RegisterHostOrigin(
                    netService,
                    LanConnectHostChannels.Lan,
                    "LAN 联机房间",
                    password: null,
                    gameMode: LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode),
                    protocolLease);
                leaseTransferred = true;
            }
            else
            {
                GD.Print("sts2_lan_connect host_flow: runtime missing, cannot register LAN host origin");
            }

            PushHostScreen(gameMode, stack, netService, maxPlayers);

            await Task.Yield();
            string ip = LanConnectNetUtil.GetPrimaryLanAddress();
            LanConnectPopupUtil.ShowInfo($"LAN 主机已启动。\n把这个地址发给好友：{ip}:{LanConnectConstants.DefaultPort}");
        }
        catch (Exception ex)
        {
            protocolLease?.Dispose();
            GD.Print(
                $"sts2_lan_connect host_flow: LAN host failed gameMode={lobbyGameMode}, port={LanConnectConstants.DefaultPort}, reason={ex}");
            NErrorPopup? popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            if (popup != null)
            {
                NModalContainer.Instance?.Add(popup);
            }

            throw;
        }
        finally
        {
            if (!leaseTransferred)
            {
                protocolLease?.Dispose();
            }
            loadingOverlay.Visible = false;
        }
    }

    public static async Task<LanConnectHostAttemptResult> StartLobbyHostAsync(
        string roomName,
        string? password,
        GameMode gameMode,
        Control loadingOverlay,
        NSubmenuStack stack,
        LanConnectCreateRoomIntent intent)
    {
        loadingOverlay.Visible = true;
        NetHostGameService netService = new();
        LobbyApiClient? apiClient = null;
        LobbyCreateRoomResponse? registration = null;
        LanConnectSessionProtocolLease? protocolLease = null;
        bool leaseTransferred = false;
        int maxPlayers = intent.MaxPlayers;
        string lobbyGameMode = LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode);
        string gameModeLabel = LanConnectMultiplayerSaveRoomBinding.GetLobbyGameModeLabel(gameMode);

        GD.Print(
            $"sts2_lan_connect host_flow: start lobby host roomName='{roomName}', passwordSet={!string.IsNullOrWhiteSpace(password)}, gameMode={lobbyGameMode}, player='{LanConnectConfig.GetEffectivePlayerDisplayName()}', localAddressCount={LanConnectNetUtil.GetLanAddressStrings().Count}, matrix={LanConnectCompatibilityMatrix.DescribeCurrentPolicy()}");

        try
        {
            intent.Validate();
            string trimmedRoomName = LanConnectConfig.SanitizeRoomName(roomName);
            string? trimmedPassword = string.IsNullOrWhiteSpace(password)
                ? null
                : LanConnectConfig.SanitizeRoomPassword(password);
            apiClient = LobbyApiClient.CreateConfigured();
            registration = await apiClient.CreateRoomAsync(BuildCreateRoomRequest(
                trimmedRoomName,
                trimmedPassword,
                lobbyGameMode,
                intent,
                savedRunInfo: null));

            LobbyProtocolSelectionDto selectionDto = registration.ProtocolSelection
                ?? registration.Room.ProtocolSelection
                ?? throw LanConnectProtocolFailureMapper.FromLocalException(
                    "lan_protocol_version_mismatch",
                    "The create response did not include a frozen protocol selection.");
            LanConnectProtocolSelection selection = selectionDto.ToValidatedValue(intent.Offer);
            if (selection.Profile != intent.Profile)
            {
                throw LanConnectProtocolFailureMapper.FromLocalException(
                    "protocol_profile_unsupported",
                    "The service selected a different profile than the create intent.");
            }

            protocolLease = LanConnectSessionProtocolState.Shared.FreezeHost(selection, registration.RoomId);
            NetErrorInfo? error = netService.StartENetHost(LanConnectConstants.DefaultPort, maxPlayers);
            if (error.HasValue)
            {
                await DeleteRegisteredRoomSafeAsync(apiClient, registration);
                GD.Print($"sts2_lan_connect host_flow: ENet host failed with {error.Value}");
                NErrorPopup? popup = NErrorPopup.Create(error.Value);
                if (popup != null)
                {
                    NModalContainer.Instance?.Add(popup);
                }

                return LanConnectHostAttemptResult.Failed(error.Value.ToString());
            }

            if (LanConnectLobbyRuntime.Instance == null)
            {
                netService.Disconnect(NetError.InternalError, now: true);
                await DeleteRegisteredRoomSafeAsync(apiClient, registration);
                return LanConnectHostAttemptResult.Failed("大厅后台运行时未安装，无法托管房主会话。");
            }

            LanConnectConfig.LastRoomName = trimmedRoomName;
            LanConnectLobbyRuntime.Instance.AttachHostedRoom(
                netService,
                apiClient,
                registration,
                new LanConnectHostedRoomMetadata
                {
                    RoomName = trimmedRoomName,
                    Password = trimmedPassword,
                    GameMode = lobbyGameMode,
                    PublishSource = "overlay_create",
                    ProtocolProfile = selection.Profile.ToCanonical()
                },
                protocolLease,
                selection);
            apiClient = null;
            leaseTransferred = true;

            PushHostScreen(gameMode, stack, netService, maxPlayers);
            await Task.Yield();

            string primaryAddress = LanConnectNetUtil.GetPrimaryLanAddress();
            string lockStatus = string.IsNullOrWhiteSpace(password) ? "无密码" : "已加锁";
            LanConnectPopupUtil.ShowInfo(
                $"大厅房间已发布。\n房间名：{roomName}\n模式：{gameModeLabel}\n状态：{lockStatus}\n本地 ENet：{primaryAddress}:{LanConnectConstants.DefaultPort}\n好友现在可以从“游戏大厅”直接加入。");
            return LanConnectHostAttemptResult.Success();
        }
        catch (LanConnectProtocolException ex)
        {
            netService.Disconnect(NetError.InternalError, now: true);
            if (apiClient != null && registration != null)
            {
                await DeleteRegisteredRoomSafeAsync(apiClient, registration);
            }
            return LanConnectHostAttemptResult.Failed(ex.Failure);
        }
        catch (LobbyServiceException ex)
        {
            netService.Disconnect(NetError.InternalError, now: true);
            GD.Print($"sts2_lan_connect host_flow: lobby create failed code={ex.Code}, status={ex.StatusCode}, message={ex.Message}");
            if (string.Equals(ex.Code, "server_bandwidth_near_capacity", StringComparison.Ordinal))
            {
                throw;
            }
            if (LanConnectProtocolFailureMapper.IsKnownProtocolServiceCode(ex.Code))
            {
                return LanConnectHostAttemptResult.Failed(LanConnectProtocolFailureMapper.FromService(ex));
            }
            string message = LanConnectModerationUiMessages.DescribeCreateRoomFailure(ex);
            LanConnectPopupUtil.ShowInfo(message);
            return LanConnectHostAttemptResult.Failed(message);
        }
        catch (Exception ex)
        {
            netService.Disconnect(NetError.InternalError, now: true);
            if (apiClient != null && registration != null)
            {
                await DeleteRegisteredRoomSafeAsync(apiClient, registration);
            }
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
            apiClient?.Dispose();
            if (!leaseTransferred)
            {
                protocolLease?.Dispose();
            }
            GD.Print("sts2_lan_connect host_flow: create flow finished.");
            loadingOverlay.Visible = false;
        }
    }

    public static async Task<LanConnectHostAttemptResult> PublishExistingHostToLobbyAsync(
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
        LobbyCreateRoomResponse? registration = null;
        LanConnectSessionProtocolLease? protocolLease = null;
        bool leaseTransferred = false;
        string playerName = LanConnectConfig.GetEffectivePlayerDisplayName();
        string lobbyGameMode = LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode);
        int localAddressCount = LanConnectNetUtil.GetLanAddressStrings().Count;

        GD.Print(
            $"sts2_lan_connect host_flow: publish existing host source={publishSource}, roomName='{trimmedRoomName}', passwordSet={!string.IsNullOrWhiteSpace(trimmedPassword)}, gameMode={lobbyGameMode}, player='{playerName}', platform={netService.Platform}, localAddressCount={localAddressCount}, saveKey={(boundSaveKey ?? "<none>")}, matrix={LanConnectCompatibilityMatrix.DescribeCurrentPolicy()}");

        try
        {
            LanConnectProtocolOffer offer = LanConnectProtocolOffer.CreateCurrent();
            LanConnectSessionProtocolSnapshot activeSnapshot = LanConnectSessionProtocolState.Shared.Current;
            LanConnectProtocolSelection requestedSelection =
                activeSnapshot.Role == LanConnectSessionProtocolRole.Host && activeSnapshot.Selection != null
                    ? activeSnapshot.Selection
                    : LanConnectProtocolSelection.CreateLocalCompat(
                        maxPlayers,
                        LanConnectBuildInfo.GetGameVersion(),
                        LanConnectWireCacheDiagnostics.GetCurrentResult().Snapshot?.Signature);
            LanConnectCreateRoomIntent intent = new(
                requestedSelection.Profile,
                requestedSelection.MaxPlayers,
                offer);
            intent.Validate();
            protocolLease = LanConnectSessionProtocolState.Shared.FreezeHost(
                requestedSelection,
                activeSnapshot.Role == LanConnectSessionProtocolRole.Host
                    ? activeSnapshot.OwnerId!
                    : BuildHostLeaseOwner(netService));
            apiClient = LobbyApiClient.CreateConfigured();
            registration = await apiClient.CreateRoomAsync(
                BuildCreateRoomRequest(
                    trimmedRoomName,
                    trimmedPassword,
                    lobbyGameMode,
                    intent,
                    savedRunInfo));
            LobbyProtocolSelectionDto selectionDto = registration.ProtocolSelection
                ?? registration.Room.ProtocolSelection
                ?? throw LanConnectProtocolFailureMapper.FromLocalException(
                    "lan_protocol_version_mismatch",
                    "The create response did not include a frozen protocol selection.");
            LanConnectProtocolSelection serverSelection = selectionDto.ToValidatedValue(offer);
            if (serverSelection != requestedSelection)
            {
                throw LanConnectProtocolFailureMapper.FromLocalException(
                    "capability_digest_mismatch",
                    "Continue-run publication cannot replace the already frozen selection.");
            }

            GD.Print(
                $"sts2_lan_connect host_flow: lobby room registered roomId={registration.RoomId}, controlChannelId={registration.ControlChannelId}, heartbeat={registration.HeartbeatIntervalSeconds}s, source={publishSource}");

            LanConnectConfig.LastRoomName = trimmedRoomName;
            if (LanConnectLobbyRuntime.Instance == null)
            {
                await DeleteRegisteredRoomSafeAsync(apiClient, registration);
                apiClient.Dispose();
                apiClient = null;
                GD.Print("sts2_lan_connect host_flow: runtime missing after room registration, host session cannot attach.");
                if (notifyOnFailure)
                {
                    LanConnectPopupUtil.ShowInfo("大厅后台运行时未安装，无法托管房主会话。请重启游戏后重试。");
                }

                return LanConnectHostAttemptResult.Failed("大厅后台运行时未安装，无法托管房主会话。");
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
                    ProtocolProfile = serverSelection.Profile.ToCanonical()
                },
                protocolLease,
                serverSelection);
            apiClient = null;
            leaseTransferred = true;
            GD.Print($"sts2_lan_connect host_flow: attached hosted room session roomId={registration.RoomId}, source={publishSource}");
            return LanConnectHostAttemptResult.Success();
        }
        catch (LanConnectProtocolException ex)
        {
            if (apiClient != null && registration != null)
            {
                await DeleteRegisteredRoomSafeAsync(apiClient, registration);
            }
            apiClient?.Dispose();
            return LanConnectHostAttemptResult.Failed(ex.Failure);
        }
        catch (LobbyServiceException ex)
        {
            if (apiClient != null && registration != null)
            {
                await DeleteRegisteredRoomSafeAsync(apiClient, registration);
            }
            apiClient?.Dispose();
            GD.Print(
                $"sts2_lan_connect host_flow: publish existing host failed source={publishSource}, code={ex.Code}, status={ex.StatusCode}, message={ex.Message}");
            if (throwOnCreateGuardRejection && string.Equals(ex.Code, "server_bandwidth_near_capacity", StringComparison.Ordinal))
            {
                throw;
            }
            if (LanConnectProtocolFailureMapper.IsKnownProtocolServiceCode(ex.Code))
            {
                return LanConnectHostAttemptResult.Failed(LanConnectProtocolFailureMapper.FromService(ex));
            }
            if (notifyOnFailure)
            {
                LanConnectPopupUtil.ShowInfo(LanConnectModerationUiMessages.DescribeCreateRoomFailure(ex));
            }

            return LanConnectHostAttemptResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            if (apiClient != null && registration != null)
            {
                await DeleteRegisteredRoomSafeAsync(apiClient, registration);
            }
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

            return LanConnectHostAttemptResult.Failed(ex.Message);
        }
        finally
        {
            if (!leaseTransferred)
            {
                protocolLease?.Dispose();
            }
        }
    }

    private static LobbyCreateRoomRequest BuildCreateRoomRequest(
        string roomName,
        string? password,
        string lobbyGameMode,
        LanConnectCreateRoomIntent intent,
        LobbySavedRunInfo? savedRunInfo) => new LobbyCreateRoomRequest
    {
        RoomName = roomName,
        Password = password,
        HostPlayerName = LanConnectConfig.GetEffectivePlayerDisplayName(),
        ClientInstallationId = LanConnectConfig.GetOrCreateClientInstallationId(),
        GameMode = lobbyGameMode,
        Version = LanConnectBuildInfo.GetGameVersion(),
        ModVersion = intent.Offer.ClientVersion,
        ClientVersion = intent.Offer.ClientVersion,
        ModList = LanConnectBuildInfo.GetModList(),
        WireCacheSignatureV1 = LanConnectWireCacheDiagnostics.GetCurrentResult().Snapshot?.Signature,
        HostModInventory = LanConnectBuildInfo.GetModInventory(),
        ProtocolProfile = LanConnectProtocolProfiles.Extended8p,
        ProtocolProfileV2 = intent.Profile.ToCanonical(),
        ProtocolOffer = LobbyProtocolOfferDto.FromValue(intent.Offer),
        MaxPlayers = intent.MaxPlayers,
        HostConnectionInfo = new LobbyHostConnectionInfo
        {
            EnetPort = LanConnectConstants.DefaultPort,
            LocalAddresses = LanConnectNetUtil.GetLanAddressStrings().ToList()
        },
        SavedRun = savedRunInfo
    };

    private static async Task DeleteRegisteredRoomSafeAsync(
        LobbyApiClient apiClient,
        LobbyCreateRoomResponse registration)
    {
        try
        {
            await apiClient.DeleteRoomAsync(
                registration.RoomId,
                new LobbyDeleteRoomRequest { HostToken = registration.HostToken });
        }
        catch (Exception exception)
        {
            GD.Print(
                $"sts2_lan_connect host_flow: failed to roll back room {registration.RoomId}: {exception.Message}");
        }
    }

    private static string BuildHostLeaseOwner(NetHostGameService netService) =>
        $"host:{netService.GetHashCode():x8}";

    private static void PushHostScreen(GameMode gameMode, NSubmenuStack stack, NetHostGameService netService, int maxPlayers)
    {
        switch (gameMode)
        {
            case GameMode.Standard:
            {
                NCharacterSelectScreen submenu = stack.GetSubmenuType<NCharacterSelectScreen>();
                submenu.InitializeMultiplayerAsHost(netService, maxPlayers);
                stack.Push(submenu);
                LanConnectInviteButtonPatch.ScheduleEnsureInviteButton(submenu, "push_host_screen");
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
