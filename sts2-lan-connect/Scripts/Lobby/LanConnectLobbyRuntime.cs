using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectRoomChatSendDecision(
    bool Enabled,
    bool UseV2,
    string LegacyText,
    string DisabledReason);

internal sealed partial class LanConnectLobbyRuntime : Node, ILanConnectRoomLifecycle
{
    private const string RuntimeName = "Sts2LanConnectLobbyRuntime";
    private const double RestartSubmenuRetryIntervalSeconds = 0.6d;
    private const int RestartContextRetryDelayMs = 250;
    private const int RestartRoomPollDelayMs = 2000;
    private const int RestartOpenSubmenuDebounceMs = 1500;

    private HostedRoomSession? _activeSession;
    private JoinedClientSession? _activeClientSession;
    private bool _heartbeatInFlight;
    private double _timeUntilHeartbeat;
    private LanConnectRotatingServerChatPort? _chatOwner;
    private LanConnectServerSwitchCoordinator? _serverSwitchCoordinator;
    private string? _serverChatPlayerNetId;
    private bool _chatEnabled = true;
    private int _chatEnabledRevision;
    private PendingHostRestart? _pendingHostRestart;
    private PendingClientReconnect? _pendingClientReconnect;
    private string? _hostRestartInFlightToken;
    private string? _clientReconnectInFlightToken;
    private double _timeUntilRestartSubmenuAttempt;
    private long _lastRestartSubmenuOpenAtUnixMs;
    private LanConnectItemLinkCapture? _itemLinkCapture;
    private static bool _enableItemLinkCaptureOnInstall;
    private bool _itemLinkCaptureRouteOnlyForTests;

    internal static LanConnectLobbyRuntime? Instance { get; private set; }

    internal bool HasActiveHostedRoom => _activeSession != null;

    internal string? ActiveRoomId => _activeSession?.RoomId ?? _activeClientSession?.RoomId;

    internal bool HasActiveRoomSession => _activeSession != null || _activeClientSession != null;

    internal int ChatRevision => unchecked((int)Chat.Room.Revision);

    internal LanConnectDualChatState Chat => _chatOwner?.Current.State ??
        throw new InvalidOperationException("Chat is unavailable before the lobby runtime is ready.");

    internal bool ChatEnabled => _chatEnabled;

    internal int ChatEnabledRevision => _chatEnabledRevision;

    internal event Action? ChatStateChanged;

    private sealed record PendingHostRestart(
        string RestartToken,
        string SaveKey,
        string RoomName,
        string? RoomPassword,
        string HostPlayerName,
        long ExpiresAtUnixMs);

    private sealed record PendingClientReconnect(
        string RestartToken,
        string SaveKey,
        string DesiredSavePlayerNetId,
        string? RoomPassword,
        string? HostPlayerName,
        string? RoomName,
        long ExpiresAtUnixMs);

    internal IReadOnlyList<LobbyRoomChatEntry> GetChatMessagesSnapshot()
    {
        string roomId = Chat.ActiveRoomId ?? string.Empty;
        return Chat.Room.Messages.Select(message => new LobbyRoomChatEntry
        {
            RoomId = roomId,
            MessageId = message.MessageId ?? message.ClientMessageId ?? string.Empty,
            SenderName = message.SenderName,
            SenderNetId = message.SenderNetId,
            MessageText = message.Text,
            SentAt = message.SentAt,
            IsLocal = message.IsLocal
        }).ToArray();
    }

    internal string? GetHostedRoomPassword()
    {
        return _activeSession?.Metadata.Password;
    }

    internal NetHostGameService? GetHostNetService()
    {
        return _activeSession?.NetService;
    }

    internal bool IsManagingNetService(INetGameService netService)
    {
        return ReferenceEquals(_activeSession?.NetService, netService);
    }

    internal static void Install(bool enableItemLinkCapture = false)
    {
        _enableItemLinkCaptureOnInstall |= enableItemLinkCapture;
        Callable.From(InstallDeferred).CallDeferred();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _timeUntilHeartbeat = 0d;
        _timeUntilRestartSubmenuAttempt = 0d;
        _lastRestartSubmenuOpenAtUnixMs = 0;
        Instance = this;
        if (_itemLinkCaptureRouteOnlyForTests)
        {
            return;
        }
        if (_enableItemLinkCaptureOnInstall)
        {
            _itemLinkCapture = new LanConnectItemLinkCapture(
                new LanConnectGodotItemLinkCapturePorts(this));
        }
        _chatOwner = new LanConnectRotatingServerChatPort(
            new LanConnectServerChatClient(),
            static () => new LanConnectServerChatClient());
        _chatOwner.StateChanged += OnChatStateChanged;
        _serverSwitchCoordinator = new LanConnectServerSwitchCoordinator(
            this,
            _chatOwner,
            new ConfigServerAddressStore());
        LanConnectProtocolProfiles.ResetActiveProfile("runtime_ready");
        SaveManager.Instance.Saved += OnRunSaved;
        LanConnectSaveDiagnostics.LogNow("runtime_ready");
        Log.Info("sts2_lan_connect lobby runtime ready.");
        // The picker is no longer triggered at runtime startup. It opens only
        // when the user clicks the "游戏大厅" entry on the multiplayer submenu —
        // see Patches.MultiplayerSubmenu.OnLobbyPressed. Verification-phase
        // policy is "always show picker on entry", so there is no auto-connect
        // shortcut here.
    }

    public override void _Process(double delta)
    {
        DrivePendingRestartNavigation(delta);
        if (_activeSession == null)
        {
            return;
        }

        _timeUntilHeartbeat -= delta;
        if (_timeUntilHeartbeat > 0d || _heartbeatInFlight)
        {
            return;
        }

        _heartbeatInFlight = true;
        _timeUntilHeartbeat = Math.Max(3d, _activeSession.HeartbeatIntervalSeconds);
        TaskHelper.RunSafely(SendHeartbeatAsync(_activeSession));
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_itemLinkCapture == null)
        {
            return;
        }
        LanConnectItemLinkCaptureInputRoute.TryRoute(
            inputEvent,
            _itemLinkCapture,
            () => GetViewport().SetInputAsHandled());
    }

    internal void ConfigureItemLinkCaptureRouteForTests(ILanConnectItemLinkCapturePorts ports)
    {
        _itemLinkCaptureRouteOnlyForTests = true;
        _itemLinkCapture = new LanConnectItemLinkCapture(ports);
    }

    public override void _ExitTree()
    {
        if (_itemLinkCaptureRouteOnlyForTests)
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
            return;
        }
        SaveManager.Instance.Saved -= OnRunSaved;
        Instance = null;
        LanConnectServerSwitchCoordinator? serverSwitchCoordinator = _serverSwitchCoordinator;
        LanConnectRotatingServerChatPort? chatOwner = _chatOwner;
        _serverSwitchCoordinator = null;
        TaskHelper.RunSafely(ShutdownAsync(
            serverSwitchCoordinator,
            chatOwner,
            _activeSession,
            _activeClientSession));

        LanConnectLobbyPlayerNameDirectory.ClearRoom(null);
    }

    private async Task ShutdownAsync(
        LanConnectServerSwitchCoordinator? serverSwitchCoordinator,
        LanConnectRotatingServerChatPort? chatOwner,
        HostedRoomSession? hostedSession,
        JoinedClientSession? clientSession)
    {
        if (serverSwitchCoordinator != null)
        {
            await serverSwitchCoordinator.DisposeAsync();
        }
        if (hostedSession != null)
        {
            await CloseHostedRoomAsync(hostedSession, suppressErrors: true);
        }
        if (clientSession != null)
        {
            await CloseJoinedClientAsync(clientSession);
        }

        if (ReferenceEquals(_chatOwner, chatOwner))
        {
            _chatOwner = null;
        }
        if (chatOwner != null)
        {
            chatOwner.StateChanged -= OnChatStateChanged;
            await chatOwner.DisposeAsync();
        }
    }

    public void AttachHostedRoom(NetHostGameService netService, LobbyApiClient apiClient, LobbyCreateRoomResponse registration, LanConnectHostedRoomMetadata metadata)
    {
        if (_activeSession != null)
        {
            GD.Print($"sts2_lan_connect lobby runtime: replacing existing hosted room {_activeSession.RoomId} with {registration.RoomId}");
            TaskHelper.RunSafely(CloseHostedRoomAsync(_activeSession, suppressErrors: true));
        }

        if (_activeClientSession != null)
        {
            TaskHelper.RunSafely(CloseJoinedClientAsync(_activeClientSession));
        }

        HostedRoomSession session = new(netService, apiClient, registration, metadata);
        session.SetEnvelopeHandler(envelope => OnHostedControlEnvelope(session, envelope));
        session.ControlClient.RoomChatReadyReceived += envelope =>
        {
            if (ReferenceEquals(_activeSession, session))
            {
                GetChatCoordinator().SetRoomRichFeatures(envelope.EnabledFeatures);
            }
        };
        session.ControlClient.RoomChatAckReceived += envelope =>
        {
            if (ReferenceEquals(_activeSession, session))
            {
                GetChatCoordinator().ApplyRoomAck(envelope, session.NetService.NetId.ToString());
            }
        };
        session.ControlClient.RoomChatMessageReceived += envelope =>
        {
            if (ReferenceEquals(_activeSession, session))
            {
                GetChatCoordinator().ApplyRoomMessage(envelope, session.NetService.NetId.ToString());
            }
        };
        session.ControlClient.RoomChatErrorReceived += envelope =>
        {
            if (ReferenceEquals(_activeSession, session))
            {
                GetChatCoordinator().ApplyRoomError(envelope);
            }
        };
        BindRoomChatDisconnect(
            session.ControlClient,
            GetChatCoordinator(),
            () => ReferenceEquals(_activeSession, session) && !session.IsClosing);
        _activeSession = session;
        EnterChatRoom(registration.RoomId);
        _timeUntilHeartbeat = 0d;
        LanConnectProtocolProfiles.SetActiveProfile(registration.Room.ProtocolProfile, registration.Room.MaxPlayers, "attach_hosted_room");
        GD.Print(
            $"sts2_lan_connect lobby runtime: attached hosted room roomId={registration.RoomId}, roomName='{metadata.RoomName}', source={metadata.PublishSource}, saveKey={(metadata.SaveKey ?? "<none>")}");
        LanConnectSaveDiagnostics.LogNow("attach_hosted_room", $"roomId={registration.RoomId}, publishSource={metadata.PublishSource}, saveKey={(metadata.SaveKey ?? "<none>")}");
        LanConnectLobbyPlayerNameDirectory.BeginRoom(registration.RoomId);
        LanConnectLobbyPlayerNameDirectory.Upsert(registration.RoomId, netService.NetId, LanConnectConfig.GetEffectivePlayerDisplayName());
        netService.Disconnected += session.OnDisconnected;
        netService.ClientConnected += session.OnClientCountChanged;
        netService.ClientDisconnected += session.OnClientDisconnected;
        TaskHelper.RunSafely(ConnectHostedControlAsync(session));
        PersistBindingForCurrentSave("attach");
        _pendingHostRestart = null;
    }

    public void AttachJoinedClient(NetClientGameService netService, LobbyJoinRoomResponse joinResponse)
    {
        string? controlChannelId = joinResponse.ConnectionPlan.ControlChannelId;
        if (string.IsNullOrWhiteSpace(controlChannelId))
        {
            GD.Print($"sts2_lan_connect lobby runtime: skip joined client control attach because controlChannelId is missing for roomId={joinResponse.Room.RoomId}");
            return;
        }

        if (_activeClientSession != null)
        {
            TaskHelper.RunSafely(CloseJoinedClientAsync(_activeClientSession));
        }

        JoinedClientSession session = new(
            netService,
            LobbyApiClient.CreateConfigured(),
            joinResponse.Room.RoomId,
            controlChannelId,
            joinResponse.TicketId,
            netService.NetId.ToString(),
            joinResponse.RoomSessionId);
        session.SetEnvelopeHandler(envelope => OnJoinedClientControlEnvelope(session, envelope));
        session.ControlClient.RoomChatReadyReceived += envelope =>
        {
            if (ReferenceEquals(_activeClientSession, session))
            {
                GetChatCoordinator().SetRoomRichFeatures(envelope.EnabledFeatures);
            }
        };
        session.ControlClient.RoomChatAckReceived += envelope =>
        {
            if (ReferenceEquals(_activeClientSession, session))
            {
                GetChatCoordinator().ApplyRoomAck(envelope, session.PlayerNetId);
            }
        };
        session.ControlClient.RoomChatMessageReceived += envelope =>
        {
            if (ReferenceEquals(_activeClientSession, session))
            {
                GetChatCoordinator().ApplyRoomMessage(envelope, session.PlayerNetId);
            }
        };
        session.ControlClient.RoomChatErrorReceived += envelope =>
        {
            if (ReferenceEquals(_activeClientSession, session))
            {
                GetChatCoordinator().ApplyRoomError(envelope);
            }
        };
        BindRoomChatDisconnect(
            session.ControlClient,
            GetChatCoordinator(),
            () => ReferenceEquals(_activeClientSession, session) && !session.IsClosing);
        _activeClientSession = session;
        EnterChatRoom(joinResponse.Room.RoomId);
        LanConnectProtocolProfiles.SetActiveProfile(joinResponse.Room.ProtocolProfile, joinResponse.Room.MaxPlayers, "attach_joined_client");
        LanConnectLobbyPlayerNameDirectory.BeginRoom(joinResponse.Room.RoomId);
        LanConnectLobbyPlayerNameDirectory.Upsert(joinResponse.Room.RoomId, netService.NetId, LanConnectConfig.GetEffectivePlayerDisplayName());
        netService.Disconnected += session.OnDisconnected;
        TaskHelper.RunSafely(ConnectJoinedClientControlAsync(session));
        _pendingClientReconnect = null;
    }

    public Task CloseActiveHostedRoomAsync(bool suppressErrors = false)
    {
        HostedRoomSession? session = _activeSession;
        if (session == null)
        {
            return Task.CompletedTask;
        }

        GD.Print($"sts2_lan_connect lobby runtime: closing active hosted room roomId={session.RoomId}");
        LanConnectSaveDiagnostics.LogNow("close_active_hosted_room", $"roomId={session.RoomId}, suppressErrors={suppressErrors}");
        session.NetService.Disconnect(NetError.Quit, now: true);
        return CloseHostedRoomAsync(session, suppressErrors);
    }

    internal async Task CloseActiveRoomAsync(CancellationToken cancellationToken = default)
    {
        HostedRoomSession? hostedSession = _activeSession;
        JoinedClientSession? clientSession = _activeClientSession;
        if (hostedSession != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hostedSession.NetService.Disconnect(NetError.Quit, now: true);
            await CloseHostedRoomAsync(hostedSession, suppressErrors: false, cancellationToken);
        }

        if (clientSession != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            clientSession.NetService.Disconnect(NetError.Quit, now: true);
            await CloseJoinedClientAsync(clientSession);
        }
    }

    bool ILanConnectRoomLifecycle.HasActiveRoom => HasActiveRoomSession;

    Task ILanConnectRoomLifecycle.LeaveActiveRoomAsync(CancellationToken cancellationToken) =>
        CloseActiveRoomAsync(cancellationToken);

    private static void InstallDeferred()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            Callable.From(InstallDeferred).CallDeferred();
            return;
        }

        if (tree.Root.GetNodeOrNull<Node>(RuntimeName) != null)
        {
            return;
        }

        LanConnectLobbyRuntime runtime = new()
        {
            Name = RuntimeName
        };
        tree.Root.AddChild(runtime);
    }

    private async Task SendHeartbeatAsync(HostedRoomSession session)
    {
        try
        {
            await session.ApiClient.SendHeartbeatAsync(session.RoomId, new LobbyHeartbeatRequest
            {
                HostToken = session.HostToken,
                CurrentPlayers = session.GetCurrentPlayers(),
                Status = !session.NetService.IsConnected
                    ? "closed"
                    : RunManager.Instance.IsInProgress
                        ? "starting"
                        : LanConnectConstants.DefaultRoomStatus,
                ConnectedPlayerNetIds = session.Metadata.SavedRun != null
                    ? session.GetConnectedPlayerNetIds()
                    : null
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect lobby heartbeat failed for room {session.RoomId}: {ex.Message}");
        }
        finally
        {
            _heartbeatInFlight = false;
        }
    }

    private async Task ConnectHostedControlAsync(HostedRoomSession session)
    {
        try
        {
            Uri uri = session.ApiClient.BuildHostControlUri(session.ControlChannelId, session.RoomId, session.HostToken);
            GD.Print($"sts2_lan_connect lobby runtime: connecting host control channel roomId={session.RoomId}");
            await session.ControlClient.ConnectHostAsync(
                uri,
                session.RoomId,
                session.ControlChannelId,
                LanConnectConfig.GetEffectivePlayerDisplayName(),
                session.NetService.NetId.ToString(),
                session.RoomSessionId,
                CancellationToken.None);
            GD.Print($"sts2_lan_connect lobby runtime: host control channel connected roomId={session.RoomId}");
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect lobby control channel failed to connect: {ex.Message}");
        }
    }

    private async Task ConnectJoinedClientControlAsync(JoinedClientSession session)
    {
        try
        {
            Uri uri = session.ApiClient.BuildClientControlUri(session.ControlChannelId, session.RoomId, session.TicketId);
            GD.Print($"sts2_lan_connect lobby runtime: connecting client control channel roomId={session.RoomId}");
            await session.ControlClient.ConnectClientAsync(
                uri,
                session.RoomId,
                session.ControlChannelId,
                session.TicketId,
                LanConnectConfig.GetEffectivePlayerDisplayName(),
                session.PlayerNetId,
                session.RoomSessionId,
                CancellationToken.None);
            await session.ControlClient.SendAsync(BuildPlayerNameSyncEnvelope(session.RoomId, session.PlayerNetId), CancellationToken.None);
            GD.Print($"sts2_lan_connect lobby runtime: client control channel connected roomId={session.RoomId}");
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect lobby client control channel failed to connect: {ex.Message}");
        }
    }

    private async Task CloseHostedRoomAsync(
        HostedRoomSession session,
        bool suppressErrors,
        CancellationToken cancellationToken = default)
    {
        if (session.IsClosing)
        {
            return;
        }

        session.IsClosing = true;
        GD.Print($"sts2_lan_connect lobby runtime: deleting hosted room roomId={session.RoomId}");
        try
        {
            await session.ApiClient.DeleteRoomAsync(session.RoomId, new LobbyDeleteRoomRequest
            {
                HostToken = session.HostToken
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!suppressErrors)
            {
                Log.Warn($"sts2_lan_connect failed to delete hosted room {session.RoomId}: {ex.Message}");
            }
        }
        finally
        {
            session.Dispose();
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
                GD.Print($"sts2_lan_connect lobby runtime: hosted room cleared roomId={session.RoomId}");
            }

            LanConnectLobbyPlayerNameDirectory.ClearRoom(session.RoomId);
            LeaveChatRoomIfIdle();
            ResetProtocolProfileIfIdle($"close_hosted_room:{session.RoomId}");
        }
    }

    private Task CloseJoinedClientAsync(JoinedClientSession session)
    {
        if (session.IsClosing)
        {
            return Task.CompletedTask;
        }

        session.IsClosing = true;
        session.Dispose();
        if (ReferenceEquals(_activeClientSession, session))
        {
            _activeClientSession = null;
        }

        LanConnectLobbyPlayerNameDirectory.ClearRoom(session.RoomId);
        LeaveChatRoomIfIdle();
        ResetProtocolProfileIfIdle($"close_joined_client:{session.RoomId}");
        return Task.CompletedTask;
    }

    private void ResetProtocolProfileIfIdle(string source)
    {
        if (_activeSession == null && _activeClientSession == null)
        {
            LanConnectProtocolProfiles.ResetActiveProfile(source);
        }
    }

    internal Task SendRoomChatMessageAsync(string messageText) =>
        SendChatTextAsync(LanConnectChatChannel.Room, messageText);

    internal Task SendChatTextAsync(
        LanConnectChatChannel channel,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        LanConnectLobbyRuntimeChatCoordinator coordinator = GetChatCoordinator();
        if (channel == LanConnectChatChannel.Server)
        {
            return coordinator.SendServerAsync(messageText, cancellationToken);
        }

        if (!_chatEnabled || !coordinator.State.Room.ChatEnabled)
        {
            throw new InvalidOperationException("Room chat is disabled.");
        }

        return SendRoomChatTextCoreAsync(coordinator, messageText, cancellationToken);
    }

    internal Task SendChatAsync(
        LanConnectChatChannel channel,
        LanConnectChatContent content,
        CancellationToken cancellationToken = default) =>
        SendChatAsync(channel, content, Guid.NewGuid().ToString("D"), cancellationToken);

    internal Task SendChatAsync(
        LanConnectChatChannel channel,
        LanConnectChatContent content,
        string clientMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(clientMessageId);
        LanConnectLobbyRuntimeChatCoordinator coordinator = GetChatCoordinator();
        if (channel == LanConnectChatChannel.Server)
        {
            return coordinator.SendServerAsync(content, clientMessageId, cancellationToken);
        }
        if (!_chatEnabled || !coordinator.State.Room.ChatEnabled)
        {
            throw new InvalidOperationException("Room chat is disabled.");
        }
        return SendRoomChatContentCoreAsync(coordinator, content, clientMessageId, cancellationToken);
    }

    internal static LanConnectRoomChatSendDecision DecideRoomChatSend(
        LanConnectChatContent content,
        LanConnectRoomChatReadyEnvelope? ready,
        string roomId,
        string roomSessionId)
    {
        ArgumentNullException.ThrowIfNull(content);
        LanConnectChatContent canonical;
        try
        {
            canonical = LanConnectServerChatProtocol.Canonicalize(
                content,
                new LanConnectChatFeatureVersions(1, 1, 1, 0));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return new LanConnectRoomChatSendDecision(false, false, string.Empty, exception.Message);
        }
        bool pureText = canonical.Segments.Count == 1 &&
                        canonical.Segments[0] is LanConnectTextSegment;
        if (ready == null)
        {
            return pureText
                ? new LanConnectRoomChatSendDecision(
                    true,
                    false,
                    ((LanConnectTextSegment)canonical.Segments[0]).Text,
                    string.Empty)
                : new LanConnectRoomChatSendDecision(
                    false,
                    false,
                    string.Empty,
                    "Room rich chat ready has not been received.");
        }
        if (!string.Equals(ready.RoomId, roomId, StringComparison.Ordinal) ||
            !string.Equals(ready.RoomSessionId, roomSessionId, StringComparison.Ordinal))
        {
            return new LanConnectRoomChatSendDecision(
                false,
                false,
                string.Empty,
                "Room rich chat ready does not match the active room session.");
        }
        try
        {
            canonical = LanConnectServerChatProtocol.Canonicalize(content, ready.EnabledFeatures);
            LanConnectServerChatProtocol.AssertInboundBudget(
                canonical,
                LanConnectConfig.GetEffectivePlayerDisplayName());
            return new LanConnectRoomChatSendDecision(true, true, string.Empty, string.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return new LanConnectRoomChatSendDecision(false, false, string.Empty, exception.Message);
        }
    }

    private async Task SendRoomChatTextCoreAsync(
        LanConnectLobbyRuntimeChatCoordinator coordinator,
        string messageText,
        CancellationToken cancellationToken,
        string? clientMessageId = null)
    {
        string normalizedMessage = NormalizeChatMessage(messageText);
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return;
        }

        string senderName = LanConnectConfig.GetEffectivePlayerDisplayName();
        string? senderNetId = _activeSession?.NetService.NetId.ToString() ?? _activeClientSession?.PlayerNetId;
        DateTimeOffset sentAt = DateTimeOffset.UtcNow;
        string messageId = clientMessageId ?? Guid.NewGuid().ToString("N");
        coordinator.BeginRoomPending(messageId, senderName, senderNetId, normalizedMessage, sentAt);
        try
        {
            await SendLegacyRoomChatEnvelopeAsync(
                messageId,
                senderName,
                normalizedMessage,
                sentAt,
                cancellationToken);
            coordinator.ConfirmRoomSend(messageId);
        }
        catch (Exception ex)
        {
            coordinator.FailRoomSend(messageId, "send_failed", ex.Message);
            throw;
        }
    }

    private async Task SendRoomChatContentCoreAsync(
        LanConnectLobbyRuntimeChatCoordinator coordinator,
        LanConnectChatContent content,
        string clientMessageId,
        CancellationToken cancellationToken)
    {
        LobbyControlClient controlClient = _activeSession?.ControlClient ??
            _activeClientSession?.ControlClient ??
            throw new InvalidOperationException("No active room control channel is available.");
        string roomId = _activeSession?.RoomId ?? _activeClientSession?.RoomId ?? string.Empty;
        string roomSessionId = _activeSession?.RoomSessionId ?? _activeClientSession?.RoomSessionId ?? string.Empty;
        if (string.IsNullOrEmpty(roomSessionId))
        {
            roomSessionId = controlClient.LatestRoomChatReady?.RoomSessionId ?? string.Empty;
        }
        LanConnectRoomChatSendDecision decision = DecideRoomChatSend(
            content,
            controlClient.LatestRoomChatReady,
            roomId,
            roomSessionId);
        if (!decision.Enabled)
        {
            throw new InvalidOperationException(decision.DisabledReason);
        }
        if (!decision.UseV2)
        {
            await SendRoomChatTextCoreAsync(
                coordinator,
                decision.LegacyText,
                cancellationToken,
                clientMessageId);
            return;
        }

        LanConnectChatContent canonical = LanConnectServerChatProtocol.Canonicalize(
            content,
            controlClient.LatestRoomChatReady!.EnabledFeatures);
        string senderName = LanConnectConfig.GetEffectivePlayerDisplayName();
        string? senderNetId = _activeSession?.NetService.NetId.ToString() ?? _activeClientSession?.PlayerNetId;
        DateTimeOffset sentAt = DateTimeOffset.UtcNow;
        coordinator.BeginRoomPending(clientMessageId, senderName, senderNetId, canonical, sentAt);
        try
        {
            await controlClient.SendRoomChatV2Async(new LanConnectRoomChatV2Envelope
            {
                ClientMessageId = clientMessageId,
                RoomId = roomId,
                RoomSessionId = roomSessionId,
                Content = canonical
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            coordinator.FailRoomSend(clientMessageId, "send_failed", exception.Message);
            throw;
        }
    }

    internal Task ConnectServerChatAsync(
        Uri lobbyBaseUri,
        string playerNetId,
        string playerName,
        CancellationToken cancellationToken = default) =>
        GetChatCoordinator().ConnectServerAsync(lobbyBaseUri, playerNetId, playerName, cancellationToken);

    internal Task RetryServerChatAsync(
        string clientMessageId,
        CancellationToken cancellationToken = default) =>
        GetChatCoordinator().RetryServerAsync(clientMessageId, cancellationToken);

    internal Task RetryRoomChatAsync(
        string clientMessageId,
        CancellationToken cancellationToken = default)
    {
        LanConnectLobbyRuntimeChatCoordinator coordinator = GetChatCoordinator();
        LobbyControlClient controlClient = _activeSession?.ControlClient ??
            _activeClientSession?.ControlClient ??
            throw new InvalidOperationException("No active room control channel is available.");
        string roomId = _activeSession?.RoomId ?? _activeClientSession?.RoomId ?? string.Empty;
        string roomSessionId = _activeSession?.RoomSessionId ?? _activeClientSession?.RoomSessionId ?? string.Empty;
        if (string.IsNullOrEmpty(roomSessionId))
        {
            roomSessionId = controlClient.LatestRoomChatReady?.RoomSessionId ?? string.Empty;
        }
        return RetryRoomChatAsync(
            coordinator,
            controlClient,
            roomId,
            roomSessionId,
            clientMessageId,
            cancellationToken);
    }

    internal static Task RetryRoomChatAsync(
        LanConnectLobbyRuntimeChatCoordinator coordinator,
        LobbyControlClient controlClient,
        string roomId,
        string roomSessionId,
        string clientMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(controlClient);
        LanConnectRoomChatReadyEnvelope ready = controlClient.LatestRoomChatReady ??
            throw new InvalidOperationException("Room rich chat ready has not been received.");
        if (!string.Equals(ready.RoomId, roomId, StringComparison.Ordinal) ||
            !string.Equals(ready.RoomSessionId, roomSessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Room rich chat ready does not match the active room session.");
        }

        return coordinator.RetryRoomAsync(
            clientMessageId,
            (content, retryClientMessageId, token) => controlClient.SendRoomChatV2Async(
                new LanConnectRoomChatV2Envelope
                {
                    ClientMessageId = retryClientMessageId,
                    RoomId = roomId,
                    RoomSessionId = roomSessionId,
                    Content = content
                },
                token),
            cancellationToken);
    }

    internal static void BindRoomChatDisconnect(
        LobbyControlClient controlClient,
        LanConnectLobbyRuntimeChatCoordinator coordinator,
        Func<bool> shouldMarkDisconnected)
    {
        ArgumentNullException.ThrowIfNull(controlClient);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(shouldMarkDisconnected);
        controlClient.Disconnected += () =>
        {
            if (shouldMarkDisconnected())
            {
                coordinator.MarkRoomDisconnected();
            }
        };
    }

    internal Task StopServerChatAsync(CancellationToken cancellationToken = default) =>
        GetChatCoordinator().StopServerAsync(cancellationToken);

    internal Task SwitchLobbyServerAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        LanConnectServerSwitchCoordinator coordinator = _serverSwitchCoordinator ??
            throw new InvalidOperationException("Server switching is unavailable before the lobby runtime is ready.");
        return coordinator.SwitchAsync(
            baseUrl,
            ResolveCurrentPlayerNetId(),
            LanConnectConfig.GetEffectivePlayerDisplayName(),
            cancellationToken);
    }

    internal Task<LanConnectServerContextLease> SwitchLobbyServerWithContextAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        LanConnectServerSwitchCoordinator coordinator = _serverSwitchCoordinator ??
            throw new InvalidOperationException("Server switching is unavailable before the lobby runtime is ready.");
        return coordinator.SwitchWithContextAsync(
            baseUrl,
            ResolveCurrentPlayerNetId(),
            LanConnectConfig.GetEffectivePlayerDisplayName(),
            cancellationToken);
    }

    internal LanConnectServerContextLease AcquireCurrentServerContext() =>
        (_serverSwitchCoordinator ??
            throw new InvalidOperationException("Server switching is unavailable before the lobby runtime is ready."))
        .AcquireCurrentServerContext();

    internal bool IsLobbyServerSwitchInProgress => _serverSwitchCoordinator?.IsSwitchInProgress == true;

    private async Task SendLegacyRoomChatEnvelopeAsync(
        string messageId,
        string senderName,
        string messageText,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken)
    {
        if (_activeSession != null)
        {
            LobbyControlEnvelope envelope = CreateHostedRoomChatEnvelope(
                _activeSession.RoomId,
                _activeSession.ControlChannelId,
                senderName,
                _activeSession.NetService.NetId.ToString(),
                messageId,
                messageText,
                sentAt);
            await _activeSession.ControlClient.SendAsync(envelope, cancellationToken);
            return;
        }

        if (_activeClientSession != null)
        {
            LobbyControlEnvelope envelope = CreateJoinedRoomChatEnvelope(
                _activeClientSession.RoomId,
                _activeClientSession.ControlChannelId,
                _activeClientSession.TicketId,
                senderName,
                _activeClientSession.PlayerNetId,
                messageId,
                messageText,
                sentAt);
            await _activeClientSession.ControlClient.SendAsync(envelope, cancellationToken);
            return;
        }

        throw new InvalidOperationException("No active lobby room session for chat.");
    }

    internal async Task SendKickPlayerAsync(string targetPlayerNetId, string targetPlayerName)
    {
        if (_activeSession == null)
        {
            return;
        }

        await _activeSession.ControlClient.SendAsync(new LobbyControlEnvelope
        {
            Type = "kick_player",
            RoomId = _activeSession.RoomId,
            TargetPlayerNetId = targetPlayerNetId,
            TargetPlayerName = targetPlayerName,
        }, CancellationToken.None);
    }

    internal async Task SendRoomSettingsAsync(bool chatEnabled)
    {
        if (_activeSession == null)
        {
            return;
        }

        ApplyRoomChatEnabled(chatEnabled);
        await _activeSession.ControlClient.SendAsync(new LobbyControlEnvelope
        {
            Type = "room_settings",
            RoomId = _activeSession.RoomId,
            ChatEnabled = chatEnabled,
        }, CancellationToken.None);
    }

    internal async Task<bool> StartHostedRunRestartAsync()
    {
        HostedRoomSession? session = _activeSession;
        if (session == null)
        {
            LanConnectPopupUtil.ShowInfo("当前没有可重开的托管房间。");
            return false;
        }

        if (_hostRestartInFlightToken != null)
        {
            LanConnectPopupUtil.ShowInfo("已经在执行重开流程，请稍候。");
            return false;
        }

        if (!SaveManager.Instance.HasMultiplayerRunSave)
        {
            LanConnectPopupUtil.ShowInfo("当前没有多人续局存档，无法执行重开。");
            return false;
        }

        if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(out SerializableRun? run, out string failureReason) || run == null)
        {
            LanConnectPopupUtil.ShowInfo($"读取多人续局存档失败：{failureReason}");
            return false;
        }

        string restartToken = Guid.NewGuid().ToString("N");
        PendingHostRestart pending = new(
            restartToken,
            LanConnectMultiplayerSaveRoomBinding.BuildSaveKey(run),
            session.Metadata.RoomName,
            session.Metadata.Password,
            LanConnectConfig.GetEffectivePlayerDisplayName(),
            DateTimeOffset.UtcNow.AddMinutes(3).ToUnixTimeMilliseconds());
        _pendingHostRestart = pending;
        _hostRestartInFlightToken = restartToken;

        try
        {
            await session.ControlClient.SendAsync(new LobbyControlEnvelope
            {
                Type = "restart_prepare",
                RoomId = session.RoomId,
                ControlChannelId = session.ControlChannelId,
                Role = "host",
                SaveKey = pending.SaveKey,
                RestartToken = pending.RestartToken,
                ExpiresAtUnixMs = pending.ExpiresAtUnixMs,
                HostPlayerName = pending.HostPlayerName,
                RoomName = pending.RoomName,
                RoomPassword = pending.RoomPassword
            }, CancellationToken.None);

            LanConnectPopupUtil.ShowInfo("已通知队友准备自动重连。\n正在返回主菜单并重开当前多人续局...");
            _timeUntilRestartSubmenuAttempt = 0d;
            await Task.Delay(200);
            if (NGame.Instance == null)
            {
                throw new InvalidOperationException("NGame instance is unavailable.");
            }

            await NGame.Instance.ReturnToMainMenu();
            return true;
        }
        catch (Exception ex)
        {
            _pendingHostRestart = null;
            LanConnectPopupUtil.ShowInfo($"重开流程启动失败：{ex.Message}");
            return false;
        }
        finally
        {
            if (_hostRestartInFlightToken == restartToken)
            {
                _hostRestartInFlightToken = null;
            }
        }
    }

    internal void OnMainMenuReady(NMainMenu mainMenu)
    {
        if (_pendingHostRestart == null && _pendingClientReconnect == null)
        {
            return;
        }

        Callable.From(() =>
        {
            TryOpenMultiplayerSubmenu(mainMenu, "main_menu_ready");
        }).CallDeferred();
    }

    internal void OnMultiplayerSubmenuReady(NMultiplayerSubmenu submenu)
    {
        if (_pendingHostRestart != null)
        {
            TaskHelper.RunSafely(TryStartPendingHostRestartAsync(submenu, _pendingHostRestart));
            return;
        }

        if (_pendingClientReconnect != null)
        {
            TaskHelper.RunSafely(TryStartPendingClientReconnectAsync(submenu, _pendingClientReconnect));
        }
    }

    internal IReadOnlyCollection<ulong> GetHostedRoomPeerIds()
    {
        return _activeSession?.ConnectedPeerIds ?? (IReadOnlyCollection<ulong>)Array.Empty<ulong>();
    }

    private void OnRunSaved()
    {
        LanConnectSaveDiagnostics.LogNow("save_event:before_persist");
        PersistBindingForCurrentSave("save_event");
        LanConnectSaveDiagnostics.LogNow("save_event:after_persist");
    }

    private void PersistBindingForCurrentSave(string source)
    {
        HostedRoomSession? session = _activeSession;
        if (session == null)
        {
            return;
        }

        if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(out SerializableRun? run, out string failureReason) || run == null)
        {
            GD.Print($"sts2_lan_connect lobby runtime: skip save binding persist source={source}, reason={failureReason}");
            return;
        }

        LanConnectMultiplayerSaveRoomBinding.PersistBinding(run, session.Metadata.RoomName, session.Metadata.Password, session.Metadata.GameMode, source);
    }

    private async void OnHostedControlEnvelope(HostedRoomSession session, LobbyControlEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case "player_name_sync":
                if (!ulong.TryParse(envelope.PlayerNetId, out ulong playerNetId) || string.IsNullOrWhiteSpace(envelope.PlayerName))
                {
                    return;
                }

                LanConnectLobbyPlayerNameDirectory.Upsert(session.RoomId, playerNetId, envelope.PlayerName);
                try
                {
                    await session.ControlClient.SendAsync(BuildPlayerNameSnapshotEnvelope(session.RoomId), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Warn($"sts2_lan_connect failed to broadcast player name snapshot for room {session.RoomId}: {ex.Message}");
                }

                break;
            case "room_chat":
                TryAppendRemoteChatMessage(session.RoomId, envelope);
                break;
            case "room_settings":
                if (envelope.ChatEnabled.HasValue)
                {
                    ApplyRoomChatEnabled(envelope.ChatEnabled.Value);
                }

                break;
            case "player_kicked":
                AppendChatMessage(session.RoomId, $"system-kick-{Guid.NewGuid():N}",
                    "系统", null,
                    $"玩家 {envelope.TargetPlayerName ?? envelope.TargetPlayerNetId} 已被移出房间。",
                    DateTimeOffset.UtcNow, isLocal: false);
                break;
        }
    }

    private void OnJoinedClientControlEnvelope(JoinedClientSession session, LobbyControlEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case "player_name_snapshot":
                LanConnectLobbyPlayerNameDirectory.UpsertSnapshot(session.RoomId, envelope.PlayerNames);
                break;
            case "player_name_sync":
                if (ulong.TryParse(envelope.PlayerNetId, out ulong playerNetId) && !string.IsNullOrWhiteSpace(envelope.PlayerName))
                {
                    LanConnectLobbyPlayerNameDirectory.Upsert(session.RoomId, playerNetId, envelope.PlayerName);
                }

                break;
            case "room_chat":
                TryAppendRemoteChatMessage(session.RoomId, envelope);
                break;
            case "kicked":
                Log.Info($"sts2_lan_connect: kicked from room {session.RoomId}, reason={envelope.Reason}");
                // Disconnect ENet FIRST so the game doesn't show "主机离开了游戏"
                try
                {
                    session.NetService.Disconnect(MegaCrit.Sts2.Core.Entities.Multiplayer.NetError.Quit, now: true);
                }
                catch (Exception ex)
                {
                    Log.Warn($"sts2_lan_connect: kicked ENet disconnect failed: {ex.Message}");
                }

                TaskHelper.RunSafely(CloseJoinedClientAsync(session));
                LanConnectPopupUtil.ShowInfo(envelope.Message ?? "你已被房主移出房间。");
                break;
            case "room_settings":
                if (envelope.ChatEnabled.HasValue)
                {
                    ApplyRoomChatEnabled(envelope.ChatEnabled.Value);
                }

                break;
            case "player_kicked":
                AppendChatMessage(session.RoomId, $"system-kick-{Guid.NewGuid():N}",
                    "系统", null,
                    $"玩家 {envelope.TargetPlayerName ?? envelope.TargetPlayerNetId} 已被移出房间。",
                    DateTimeOffset.UtcNow, isLocal: false);
                break;
            case "restart_prepare":
                TaskHelper.RunSafely(HandleRestartPrepareAsync(session, envelope));
                break;
        }
    }

    private async Task TryStartPendingHostRestartAsync(NMultiplayerSubmenu submenu, PendingHostRestart pending)
    {
        if (_pendingHostRestart?.RestartToken != pending.RestartToken)
        {
            return;
        }

        if (_hostRestartInFlightToken == pending.RestartToken)
        {
            return;
        }

        _hostRestartInFlightToken = pending.RestartToken;
        try
        {
            if (!SaveManager.Instance.HasMultiplayerRunSave)
            {
                _pendingHostRestart = null;
                LanConnectPopupUtil.ShowInfo("多人续局存档不存在，无法自动重开。");
                return;
            }

            while (_pendingHostRestart?.RestartToken == pending.RestartToken
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <= pending.ExpiresAtUnixMs)
            {
                if (LanConnectMultiplayerSaveCompatibility.TryStartLoadedRunAsLanHostFromSubmenu(submenu))
                {
                    _pendingHostRestart = null;
                    LanConnectPopupUtil.ShowInfo("正在自动重开多人续局，房间会在载入页自动恢复。");
                    return;
                }

                await Task.Delay(RestartContextRetryDelayMs);
            }

            if (_pendingHostRestart?.RestartToken == pending.RestartToken)
            {
                _pendingHostRestart = null;
                LanConnectPopupUtil.ShowInfo("自动重开等待超时，请手动在多人页面点击“载入”。");
            }
        }
        finally
        {
            if (_hostRestartInFlightToken == pending.RestartToken)
            {
                _hostRestartInFlightToken = null;
            }
        }
    }

    private async Task HandleRestartPrepareAsync(JoinedClientSession session, LobbyControlEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.RestartToken) ||
            string.IsNullOrWhiteSpace(envelope.SaveKey) ||
            !envelope.ExpiresAtUnixMs.HasValue ||
            string.IsNullOrWhiteSpace(session.PlayerNetId))
        {
            return;
        }

        PendingClientReconnect pending = new(
            envelope.RestartToken.Trim(),
            envelope.SaveKey.Trim(),
            session.PlayerNetId.Trim(),
            string.IsNullOrWhiteSpace(envelope.RoomPassword) ? null : envelope.RoomPassword,
            string.IsNullOrWhiteSpace(envelope.HostPlayerName) ? null : envelope.HostPlayerName.Trim(),
            string.IsNullOrWhiteSpace(envelope.RoomName) ? null : envelope.RoomName.Trim(),
            envelope.ExpiresAtUnixMs.Value);
        if (_pendingClientReconnect?.RestartToken == pending.RestartToken)
        {
            return;
        }

        _pendingClientReconnect = pending;
        _timeUntilRestartSubmenuAttempt = 0d;
        GD.Print($"sts2_lan_connect restart_prepare: queued auto reconnect saveKey={pending.SaveKey}, desiredNetId={pending.DesiredSavePlayerNetId}");
        LanConnectPopupUtil.ShowInfo(
            $"房主正在重开一局。\n正在准备自动重连：{pending.RoomName ?? "续局房间"}");

        try
        {
            session.NetService.Disconnect(NetError.Quit, now: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect restart_prepare: client disconnect failed: {ex.Message}");
        }

        await CloseJoinedClientAsync(session);
        GD.Print("sts2_lan_connect restart_prepare: waiting for game to return to main menu after disconnect.");
    }

    private async Task TryStartPendingClientReconnectAsync(NMultiplayerSubmenu submenu, PendingClientReconnect pending)
    {
        if (_pendingClientReconnect?.RestartToken != pending.RestartToken)
        {
            return;
        }

        if (_clientReconnectInFlightToken == pending.RestartToken)
        {
            return;
        }

        _clientReconnectInFlightToken = pending.RestartToken;
        try
        {
            while (_pendingClientReconnect?.RestartToken == pending.RestartToken
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <= pending.ExpiresAtUnixMs)
            {
                if (!LanConnectMultiplayerSaveCompatibility.TryResolveMultiplayerSubmenuContext(submenu, out Control? loadingOverlay, out NSubmenuStack? stack)
                    || loadingOverlay == null
                    || stack == null)
                {
                    await Task.Delay(RestartContextRetryDelayMs);
                    continue;
                }

                try
                {
                    using LobbyApiClient apiClient = LobbyApiClient.CreateConfigured();
                    IReadOnlyList<LobbyRoomSummary> rooms = await apiClient.GetRoomsAsync();
                    LobbyRoomSummary? room = FindRestartTargetRoom(rooms, pending);
                    if (room == null)
                    {
                        await Task.Delay(RestartRoomPollDelayMs);
                        continue;
                    }

                    LobbyJoinRoomResponse joinResponse = await apiClient.JoinRoomAsync(room.RoomId, new LobbyJoinRoomRequest
                    {
                        PlayerName = LanConnectConfig.GetEffectivePlayerDisplayName(),
                        Password = string.IsNullOrWhiteSpace(pending.RoomPassword) ? null : pending.RoomPassword,
                        Version = LanConnectBuildInfo.GetGameVersion(),
                        ModVersion = LanConnectBuildInfo.GetModVersion(),
                        ModList = LanConnectBuildInfo.GetModList(),
                        DesiredSavePlayerNetId = pending.DesiredSavePlayerNetId,
                        PlayerNetId = pending.DesiredSavePlayerNetId
                    });
                    LobbyJoinAttemptResult joinResult = await LanConnectLobbyJoinFlow.JoinAsync(
                        stack,
                        loadingOverlay,
                        joinResponse,
                        pending.DesiredSavePlayerNetId,
                        CancellationToken.None);
                    if (joinResult.Joined)
                    {
                        _pendingClientReconnect = null;
                        LanConnectPopupUtil.ShowInfo("已自动重新加入房间。");
                        return;
                    }

                    Log.Warn(
                        $"sts2_lan_connect restart_rejoin: join attempt failed roomId={room.RoomId}, reason={(string.IsNullOrWhiteSpace(joinResult.FailureMessage) ? "<none>" : joinResult.FailureMessage)}");
                    await Task.Delay(RestartRoomPollDelayMs);
                }
                catch (LobbyServiceException ex) when (string.Equals(ex.Code, "room_not_found", StringComparison.Ordinal))
                {
                    await Task.Delay(RestartRoomPollDelayMs);
                }
                catch (Exception ex)
                {
                    Log.Warn($"sts2_lan_connect restart_rejoin: attempt failed: {ex.Message}");
                    await Task.Delay(RestartRoomPollDelayMs);
                }
            }

            if (_pendingClientReconnect?.RestartToken == pending.RestartToken)
            {
                _pendingClientReconnect = null;
                LanConnectPopupUtil.ShowInfo("等待房主重开超时。请稍后手动从“游戏大厅”重新加入。");
            }
        }
        finally
        {
            if (_clientReconnectInFlightToken == pending.RestartToken)
            {
                _clientReconnectInFlightToken = null;
            }
        }
    }

    private void DrivePendingRestartNavigation(double delta)
    {
        if (_pendingHostRestart == null && _pendingClientReconnect == null)
        {
            return;
        }

        if (_activeSession != null || _activeClientSession != null)
        {
            return;
        }

        if (_hostRestartInFlightToken != null || _clientReconnectInFlightToken != null)
        {
            return;
        }

        _timeUntilRestartSubmenuAttempt -= delta;
        if (_timeUntilRestartSubmenuAttempt > 0d)
        {
            return;
        }

        _timeUntilRestartSubmenuAttempt = RestartSubmenuRetryIntervalSeconds;
        NMainMenu? mainMenu = FindMainMenuNode();
        if (mainMenu == null)
        {
            return;
        }

        TryOpenMultiplayerSubmenu(mainMenu, "runtime_drive");
    }

    private void TryOpenMultiplayerSubmenu(NMainMenu mainMenu, string source)
    {
        if (!GodotObject.IsInstanceValid(mainMenu) || !mainMenu.IsInsideTree())
        {
            return;
        }

        if (_pendingHostRestart == null && _pendingClientReconnect == null)
        {
            return;
        }

        if (_hostRestartInFlightToken != null || _clientReconnectInFlightToken != null)
        {
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastRestartSubmenuOpenAtUnixMs < RestartOpenSubmenuDebounceMs)
        {
            return;
        }

        try
        {
            _lastRestartSubmenuOpenAtUnixMs = now;
            mainMenu.OpenMultiplayerSubmenu();
            GD.Print($"sts2_lan_connect restart_nav: open multiplayer submenu source={source}");
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect restart_nav: failed to open multiplayer submenu source={source}: {ex.Message}");
        }
    }

    private NMainMenu? FindMainMenuNode()
    {
        SceneTree? tree = GetTree();
        if (tree?.Root == null)
        {
            return null;
        }

        return FindNodeByType<NMainMenu>(tree.Root);
    }

    private static T? FindNodeByType<T>(Node node) where T : class
    {
        if (node is T target)
        {
            return target;
        }

        foreach (Node child in node.GetChildren())
        {
            T? found = FindNodeByType<T>(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static LobbyRoomSummary? FindRestartTargetRoom(IReadOnlyList<LobbyRoomSummary> rooms, PendingClientReconnect pending)
    {
        IEnumerable<LobbyRoomSummary> matches = rooms.Where(room =>
            string.Equals(room.SavedRun?.SaveKey, pending.SaveKey, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(pending.HostPlayerName))
        {
            matches = matches.Where(room =>
                string.Equals(room.HostPlayerName, pending.HostPlayerName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(pending.RoomName))
        {
            matches = matches.Where(room =>
                string.Equals(room.RoomName, pending.RoomName, StringComparison.Ordinal));
        }

        return matches
            .OrderByDescending(room => room.LastHeartbeatAt)
            .FirstOrDefault();
    }

    private void TryAppendRemoteChatMessage(string roomId, LobbyControlEnvelope envelope)
    {
        string normalizedMessage = NormalizeChatMessage(envelope.MessageText);
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return;
        }

        string senderName = string.IsNullOrWhiteSpace(envelope.PlayerName) ? "Unknown" : envelope.PlayerName.Trim();
        string messageId = string.IsNullOrWhiteSpace(envelope.MessageId) ? Guid.NewGuid().ToString("N") : envelope.MessageId.Trim();
        DateTimeOffset sentAt = envelope.SentAtUnixMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(envelope.SentAtUnixMs.Value)
            : DateTimeOffset.UtcNow;
        GetChatCoordinator().AppendRoomConfirmed(
            roomId,
            messageId,
            senderName,
            envelope.PlayerNetId,
            normalizedMessage,
            sentAt,
            isLocal: false);
    }

    private void EnterChatRoom(string roomId)
    {
        GetChatCoordinator().EnterRoom(roomId);
        ApplyRoomChatEnabled(true);
        GetChatCoordinator().AppendRoomConfirmed(
            roomId,
            $"system-{Guid.NewGuid():N}",
            "房间聊天",
            null,
            "已连接房间聊天。",
            DateTimeOffset.UtcNow,
            isLocal: false);
    }

    private void ApplyRoomChatEnabled(bool enabled)
    {
        _chatEnabled = enabled;
        _chatEnabledRevision++;
        _chatOwner?.Current.SetRoomChatEnabled(enabled);
    }

    private void LeaveChatRoomIfIdle()
    {
        if (_activeSession != null || _activeClientSession != null || _chatOwner == null)
        {
            return;
        }

        _chatOwner.Current.LeaveRoom();
    }

    private void AppendChatMessage(string roomId, string messageId, string senderName, string? senderNetId, string messageText, DateTimeOffset sentAt, bool isLocal)
    {
        GetChatCoordinator().AppendRoomConfirmed(
            roomId,
            messageId,
            senderName,
            senderNetId,
            messageText,
            sentAt,
            isLocal);
    }

    internal static LobbyControlEnvelope CreateHostedRoomChatEnvelope(
        string roomId,
        string controlChannelId,
        string playerName,
        string playerNetId,
        string messageId,
        string messageText,
        DateTimeOffset sentAt) => new()
    {
        Type = "room_chat",
        RoomId = roomId,
        ControlChannelId = controlChannelId,
        Role = "host",
        PlayerName = playerName,
        PlayerNetId = playerNetId,
        MessageId = messageId,
        MessageText = messageText,
        SentAtUnixMs = sentAt.ToUnixTimeMilliseconds()
    };

    internal static LobbyControlEnvelope CreateJoinedRoomChatEnvelope(
        string roomId,
        string controlChannelId,
        string ticketId,
        string playerName,
        string playerNetId,
        string messageId,
        string messageText,
        DateTimeOffset sentAt) => new()
    {
        Type = "room_chat",
        RoomId = roomId,
        ControlChannelId = controlChannelId,
        Role = "client",
        TicketId = ticketId,
        PlayerName = playerName,
        PlayerNetId = playerNetId,
        MessageId = messageId,
        MessageText = messageText,
        SentAtUnixMs = sentAt.ToUnixTimeMilliseconds()
    };

    private LanConnectLobbyRuntimeChatCoordinator GetChatCoordinator() =>
        _chatOwner?.Current ?? throw new InvalidOperationException(
            "Chat is unavailable before the lobby runtime is ready.");

    private string ResolveCurrentPlayerNetId()
    {
        string? activePlayerNetId = _activeSession?.NetService.NetId.ToString() ?? _activeClientSession?.PlayerNetId;
        if (!string.IsNullOrWhiteSpace(activePlayerNetId))
        {
            _serverChatPlayerNetId = activePlayerNetId;
            return activePlayerNetId;
        }

        if (!string.IsNullOrWhiteSpace(_serverChatPlayerNetId))
        {
            return _serverChatPlayerNetId;
        }

        try
        {
            ulong platformPlayerId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform);
            if (platformPlayerId > 1)
            {
                _serverChatPlayerNetId = platformPlayerId.ToString();
                return _serverChatPlayerNetId;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect failed to resolve platform player id for server chat: {ex.Message}");
        }

        _serverChatPlayerNetId = LanConnectNetUtil.GenerateClientNetId().ToString();
        return _serverChatPlayerNetId;
    }

    private void OnChatStateChanged() => ChatStateChanged?.Invoke();

    private sealed class ConfigServerAddressStore : ILanConnectServerAddressStore
    {
        public void Persist(string baseUrl, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LanConnectConfig.PersistLobbyServerAddress(baseUrl);
        }
    }

    private static string NormalizeChatMessage(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return string.Empty;
        }

        string normalized = messageText.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length > 60)
        {
            normalized = normalized[..60];
        }

        return normalized;
    }

    private static LobbyControlEnvelope BuildPlayerNameSyncEnvelope(string roomId, string playerNetId)
    {
        return new LobbyControlEnvelope
        {
            Type = "player_name_sync",
            RoomId = roomId,
            Role = "client",
            PlayerNetId = playerNetId,
            PlayerName = LanConnectConfig.GetEffectivePlayerDisplayName()
        };
    }

    private static LobbyControlEnvelope BuildPlayerNameSnapshotEnvelope(string roomId)
    {
        return new LobbyControlEnvelope
        {
            Type = "player_name_snapshot",
            RoomId = roomId,
            Role = "host",
            PlayerNames = LanConnectLobbyPlayerNameDirectory.BuildSnapshot(roomId)
        };
    }

    private sealed class HostedRoomSession
    {
        private readonly Action<NetErrorInfo> _disconnectedHandler;
        private readonly Action<ulong> _clientConnectedHandler;
        private readonly Action<ulong, NetErrorInfo> _clientDisconnectedHandler;
        private Action<LobbyControlEnvelope>? _controlEnvelopeHandler;
        internal readonly HashSet<ulong> _connectedPeerIds = new();

        public IReadOnlyCollection<ulong> ConnectedPeerIds => _connectedPeerIds;

        public HostedRoomSession(
            NetHostGameService netService,
            LobbyApiClient apiClient,
            LobbyCreateRoomResponse registration,
            LanConnectHostedRoomMetadata metadata)
        {
            NetService = netService;
            ApiClient = apiClient;
            Registration = registration;
            Metadata = metadata;
            ControlClient = new LobbyControlClient();
            RelayTunnel = registration.RelayEndpoint != null
                ? new LanConnectLobbyRelayHostTunnel(registration.RoomId, registration.RelayEndpoint, registration.HostToken)
                : null;
            _disconnectedHandler = OnDisconnected;
            _clientConnectedHandler = OnClientCountChanged;
            _clientDisconnectedHandler = OnClientDisconnected;
        }

        public NetHostGameService NetService { get; }

        public LobbyApiClient ApiClient { get; }

        public LobbyCreateRoomResponse Registration { get; }

        public LanConnectHostedRoomMetadata Metadata { get; }

        public LobbyControlClient ControlClient { get; }

        public LanConnectLobbyRelayHostTunnel? RelayTunnel { get; }

        public string RoomId => Registration.RoomId;

        public string HostToken => Registration.HostToken;

        public string ControlChannelId => Registration.ControlChannelId;

        public string RoomSessionId => Registration.RoomSessionId ?? string.Empty;

        public bool IsClosing { get; set; }

        public int HeartbeatIntervalSeconds => Registration.HeartbeatIntervalSeconds > 0
            ? Registration.HeartbeatIntervalSeconds
            : (int)LanConnectConstants.LobbyHeartbeatIntervalSeconds;

        public int GetCurrentPlayers()
        {
            return 1 + _connectedPeerIds.Count;
        }

        public List<string> GetConnectedPlayerNetIds()
        {
            List<string> connected = new()
            {
                NetService.NetId.ToString()
            };
            foreach (ulong peerId in _connectedPeerIds)
            {
                connected.Add(peerId.ToString());
            }

            return connected;
        }

        public void OnDisconnected(NetErrorInfo _)
        {
            if (IsClosing)
            {
                return;
            }

            LanConnectSaveDiagnostics.LogNow("host_net_disconnected", $"roomId={RoomId}");
            if (LanConnectLobbyRuntime.Instance != null)
            {
                TaskHelper.RunSafely(LanConnectLobbyRuntime.Instance.CloseHostedRoomAsync(this, suppressErrors: true));
            }
        }

        public void OnClientCountChanged(ulong _)
        {
            _connectedPeerIds.Add(_);
            if (LanConnectLobbyRuntime.Instance != null)
            {
                LanConnectLobbyRuntime.Instance._timeUntilHeartbeat = 0d;
            }
        }

        public void OnClientDisconnected(ulong _, NetErrorInfo __)
        {
            _connectedPeerIds.Remove(_);
            if (LanConnectLobbyRuntime.Instance != null)
            {
                LanConnectLobbyRuntime.Instance._timeUntilHeartbeat = 0d;
            }
        }

        public void SetEnvelopeHandler(Action<LobbyControlEnvelope> controlEnvelopeHandler)
        {
            _controlEnvelopeHandler = controlEnvelopeHandler;
            ControlClient.EnvelopeReceived += _controlEnvelopeHandler;
        }

        public void Dispose()
        {
            NetService.Disconnected -= _disconnectedHandler;
            NetService.ClientConnected -= _clientConnectedHandler;
            NetService.ClientDisconnected -= _clientDisconnectedHandler;
            if (_controlEnvelopeHandler != null)
            {
                ControlClient.EnvelopeReceived -= _controlEnvelopeHandler;
            }
            Task.Run(async () =>
            {
                if (RelayTunnel != null)
                {
                    await RelayTunnel.DisposeAsync();
                }

                await ControlClient.DisposeAsync();
                ApiClient.Dispose();
            });
        }
    }

    private sealed class JoinedClientSession
    {
        private readonly Action<NetErrorInfo> _disconnectedHandler;
        private Action<LobbyControlEnvelope>? _controlEnvelopeHandler;

        public JoinedClientSession(
            NetClientGameService netService,
            LobbyApiClient apiClient,
            string roomId,
            string controlChannelId,
            string ticketId,
            string playerNetId,
            string? roomSessionId)
        {
            NetService = netService;
            ApiClient = apiClient;
            RoomId = roomId;
            ControlChannelId = controlChannelId;
            TicketId = ticketId;
            PlayerNetId = playerNetId;
            RoomSessionId = roomSessionId ?? string.Empty;
            ControlClient = new LobbyControlClient();
            _disconnectedHandler = OnDisconnected;
        }

        public NetClientGameService NetService { get; }

        public LobbyApiClient ApiClient { get; }

        public LobbyControlClient ControlClient { get; }

        public string RoomId { get; }

        public string ControlChannelId { get; }

        public string TicketId { get; }

        public string PlayerNetId { get; }

        public string RoomSessionId { get; }

        public bool IsClosing { get; set; }

        public void SetEnvelopeHandler(Action<LobbyControlEnvelope> controlEnvelopeHandler)
        {
            _controlEnvelopeHandler = controlEnvelopeHandler;
            ControlClient.EnvelopeReceived += _controlEnvelopeHandler;
        }

        public void OnDisconnected(NetErrorInfo _)
        {
            if (IsClosing)
            {
                return;
            }

            if (LanConnectLobbyRuntime.Instance != null)
            {
                TaskHelper.RunSafely(LanConnectLobbyRuntime.Instance.CloseJoinedClientAsync(this));
            }
        }

        public void Dispose()
        {
            NetService.Disconnected -= _disconnectedHandler;
            if (_controlEnvelopeHandler != null)
            {
                ControlClient.EnvelopeReceived -= _controlEnvelopeHandler;
            }
            Task.Run(async () =>
            {
                await ControlClient.DisposeAsync();
                ApiClient.Dispose();
            });
        }
    }
}
