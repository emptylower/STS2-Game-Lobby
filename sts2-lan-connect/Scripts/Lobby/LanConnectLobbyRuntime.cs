using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectLobbyRuntime : Node
{
    private const string RuntimeName = "Sts2LanConnectLobbyRuntime";
    private const int MaxChatMessages = 60;

    private HostedRoomSession? _activeSession;
    private JoinedClientSession? _activeClientSession;
    private bool _heartbeatInFlight;
    private double _timeUntilHeartbeat;
    private readonly List<LobbyRoomChatEntry> _chatMessages = new();
    private int _chatRevision;

    internal static LanConnectLobbyRuntime? Instance { get; private set; }

    internal bool HasActiveHostedRoom => _activeSession != null;

    internal string? ActiveRoomId => _activeSession?.RoomId ?? _activeClientSession?.RoomId;

    internal bool HasActiveRoomSession => _activeSession != null || _activeClientSession != null;

    internal int ChatRevision => _chatRevision;

    internal IReadOnlyList<LobbyRoomChatEntry> GetChatMessagesSnapshot()
    {
        return _chatMessages.ToArray();
    }

    internal bool IsManagingNetService(INetGameService netService)
    {
        return ReferenceEquals(_activeSession?.NetService, netService);
    }

    internal static void Install()
    {
        Callable.From(InstallDeferred).CallDeferred();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _timeUntilHeartbeat = 0d;
        Instance = this;
        SaveManager.Instance.Saved += OnRunSaved;
        LanConnectSaveDiagnostics.LogNow("runtime_ready");
        Log.Info("sts2_lan_connect lobby runtime ready.");
    }

    public override void _Process(double delta)
    {
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

    public override void _ExitTree()
    {
        SaveManager.Instance.Saved -= OnRunSaved;
        Instance = null;
        if (_activeSession != null)
        {
            TaskHelper.RunSafely(CloseHostedRoomAsync(_activeSession, suppressErrors: true));
        }

        if (_activeClientSession != null)
        {
            TaskHelper.RunSafely(CloseJoinedClientAsync(_activeClientSession));
        }

        LanConnectLobbyPlayerNameDirectory.ClearRoom(null);
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
        _activeSession = session;
        ResetChatState(registration.RoomId);
        _timeUntilHeartbeat = 0d;
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
            netService.NetId.ToString());
        session.SetEnvelopeHandler(envelope => OnJoinedClientControlEnvelope(session, envelope));
        _activeClientSession = session;
        ResetChatState(joinResponse.Room.RoomId);
        LanConnectLobbyPlayerNameDirectory.BeginRoom(joinResponse.Room.RoomId);
        LanConnectLobbyPlayerNameDirectory.Upsert(joinResponse.Room.RoomId, netService.NetId, LanConnectConfig.GetEffectivePlayerDisplayName());
        netService.Disconnected += session.OnDisconnected;
        TaskHelper.RunSafely(ConnectJoinedClientControlAsync(session));
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
            await session.ControlClient.ConnectHostAsync(uri, session.RoomId, session.ControlChannelId, LanConnectConfig.GetEffectivePlayerDisplayName(), CancellationToken.None);
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
                CancellationToken.None);
            await session.ControlClient.SendAsync(BuildPlayerNameSyncEnvelope(session.RoomId, session.PlayerNetId), CancellationToken.None);
            GD.Print($"sts2_lan_connect lobby runtime: client control channel connected roomId={session.RoomId}");
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect lobby client control channel failed to connect: {ex.Message}");
        }
    }

    private async Task CloseHostedRoomAsync(HostedRoomSession session, bool suppressErrors)
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
            });
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
            ClearChatIfInactive(session.RoomId);
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
        ClearChatIfInactive(session.RoomId);
        return Task.CompletedTask;
    }

    internal async Task SendRoomChatMessageAsync(string messageText)
    {
        string normalizedMessage = NormalizeChatMessage(messageText);
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return;
        }

        string senderName = LanConnectConfig.GetEffectivePlayerDisplayName();
        DateTimeOffset sentAt = DateTimeOffset.UtcNow;
        string messageId = Guid.NewGuid().ToString("N");

        if (_activeSession != null)
        {
            await _activeSession.ControlClient.SendAsync(new LobbyControlEnvelope
            {
                Type = "room_chat",
                RoomId = _activeSession.RoomId,
                ControlChannelId = _activeSession.ControlChannelId,
                Role = "host",
                PlayerName = senderName,
                PlayerNetId = _activeSession.NetService.NetId.ToString(),
                MessageId = messageId,
                MessageText = normalizedMessage,
                SentAtUnixMs = sentAt.ToUnixTimeMilliseconds()
            }, CancellationToken.None);
            AppendChatMessage(_activeSession.RoomId, messageId, senderName, _activeSession.NetService.NetId.ToString(), normalizedMessage, sentAt, isLocal: true);
            return;
        }

        if (_activeClientSession != null)
        {
            await _activeClientSession.ControlClient.SendAsync(new LobbyControlEnvelope
            {
                Type = "room_chat",
                RoomId = _activeClientSession.RoomId,
                ControlChannelId = _activeClientSession.ControlChannelId,
                Role = "client",
                TicketId = _activeClientSession.TicketId,
                PlayerName = senderName,
                PlayerNetId = _activeClientSession.PlayerNetId,
                MessageId = messageId,
                MessageText = normalizedMessage,
                SentAtUnixMs = sentAt.ToUnixTimeMilliseconds()
            }, CancellationToken.None);
            AppendChatMessage(_activeClientSession.RoomId, messageId, senderName, _activeClientSession.PlayerNetId, normalizedMessage, sentAt, isLocal: true);
            return;
        }

        throw new InvalidOperationException("No active lobby room session for chat.");
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
        }
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
        AppendChatMessage(roomId, messageId, senderName, envelope.PlayerNetId, normalizedMessage, sentAt, isLocal: false);
    }

    private void ResetChatState(string roomId)
    {
        _chatMessages.Clear();
        _chatRevision++;
        AppendChatMessage(roomId, $"system-{Guid.NewGuid():N}", "房间聊天", null, "已连接房间聊天。", DateTimeOffset.UtcNow, isLocal: false);
    }

    private void ClearChatIfInactive(string roomId)
    {
        if (string.Equals(_activeSession?.RoomId, roomId, StringComparison.Ordinal)
            || string.Equals(_activeClientSession?.RoomId, roomId, StringComparison.Ordinal))
        {
            return;
        }

        _chatMessages.Clear();
        _chatRevision++;
    }

    private void AppendChatMessage(string roomId, string messageId, string senderName, string? senderNetId, string messageText, DateTimeOffset sentAt, bool isLocal)
    {
        foreach (LobbyRoomChatEntry existing in _chatMessages)
        {
            if (string.Equals(existing.MessageId, messageId, StringComparison.Ordinal))
            {
                return;
            }
        }

        _chatMessages.Add(new LobbyRoomChatEntry
        {
            RoomId = roomId,
            MessageId = messageId,
            SenderName = senderName,
            SenderNetId = senderNetId,
            MessageText = messageText,
            SentAt = sentAt,
            IsLocal = isLocal
        });

        if (_chatMessages.Count > MaxChatMessages)
        {
            _chatMessages.RemoveRange(0, _chatMessages.Count - MaxChatMessages);
        }

        _chatRevision++;
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
        private readonly HashSet<ulong> _connectedPeerIds = new();

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
            string playerNetId)
        {
            NetService = netService;
            ApiClient = apiClient;
            RoomId = roomId;
            ControlChannelId = controlChannelId;
            TicketId = ticketId;
            PlayerNetId = playerNetId;
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
