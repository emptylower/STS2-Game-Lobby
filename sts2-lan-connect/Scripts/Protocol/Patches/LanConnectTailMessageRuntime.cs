using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectTailMessageRuntime : ILanConnectTailMessageRuntime
{
    private static readonly FieldInfo HostMessageBus = RequireMessageBus(typeof(NetHostGameService));
    private static readonly FieldInfo ClientMessageBus = RequireMessageBus(typeof(NetClientGameService));
    private readonly object _sync = new();
    private readonly Dictionary<NetMessageBus, Binding> _bindings = new(ReferenceEqualityComparer.Instance);

    [ThreadStatic]
    private static Stack<LanConnectProtocolFailure>? _outgoingRejections;
    [ThreadStatic]
    private static Stack<IReadOnlyList<ulong>>? _outgoingSidecarRecipients;

    internal static LanConnectTailMessageRuntime Shared { get; } = new();

    internal static bool HasPendingOutgoingRejectionForCurrentThread =>
        _outgoingRejections is { Count: > 0 };

    internal LanConnectTailMessageRuntime()
    {
        LanConnectRitsuLibSidecarCarrier.Shared.FrameReceived += OnSidecarFrameReceived;
    }

    internal void BindHost(
        NetHostGameService service,
        LanConnectProtocolOffer offer,
        LanConnectProtocolSelection selection)
    {
        ArgumentNullException.ThrowIfNull(service);
        Bind(service, GetMessageBus(service), offer, selection, isHost: true);
    }

    internal void BindClient(
        NetClientGameService service,
        LanConnectProtocolOffer offer,
        LanConnectProtocolSelection selection,
        ReadOnlySpan<byte> protocolFlowNonce)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (protocolFlowNonce.Length != 16)
        {
            throw Protocol(
                "lan_protocol_version_mismatch",
                "Tail client binding requires the 16-byte protocol flow nonce from its ticket.");
        }
        Bind(
            service,
            GetMessageBus(service),
            offer,
            selection,
            isHost: false,
            protocolFlowNonce.ToArray());
    }

    internal void Unbind(INetGameService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        NetMessageBus bus = GetMessageBus(service);
        lock (_sync)
        {
            if (_bindings.Remove(bus, out Binding? binding)
                && binding.Selection.Carrier == LanConnectProtocolCarrier.RitsuLibSidecarV1)
            {
                ulong[] peers = binding.BoundPeerIds.ToArray();
                binding.ClearSidecar();
                foreach (ulong peerId in peers)
                {
                    LanConnectRitsuLibSidecarCarrier.Shared.SetPeerUnknown(peerId);
                }
                if (_bindings.Values.All(static value =>
                        value.Selection.Carrier != LanConnectProtocolCarrier.RitsuLibSidecarV1))
                {
                    LanConnectRitsuLibSidecarCarrier.Shared.ObserveNetService(null);
                }
            }
        }
    }

    internal void BindHostTrustedSidecarFlow(
        NetHostGameService service,
        ulong peerNetId,
        ReadOnlySpan<byte> protocolFlowNonce)
    {
        Binding binding = RequireBinding(GetMessageBus(service));
        if (binding.Selection.Carrier != LanConnectProtocolCarrier.RitsuLibSidecarV1)
        {
            return;
        }

        binding.BindBidirectionalSidecarFlow(service.NetId, peerNetId, protocolFlowNonce);
        LanConnectRitsuLibSidecarCarrier.Shared.SetPeerSupported(peerNetId);
        PendingOutgoingSidecar? pending = binding.TakePendingOutgoingSidecar(peerNetId);
        if (pending != null)
        {
            SendDeferredHostSidecar(binding, peerNetId, pending);
        }
    }

    internal void BindClientHostSidecarFlow(NetClientGameService service)
    {
        BindClientHostSidecarFlow(service, service.NetId, service.HostNetId);
    }

    internal void BindClientHostSidecarFlow(NetClientGameService service, ulong localNetId, ulong hostNetId)
    {
        Binding binding = RequireBinding(GetMessageBus(service));
        if (binding.Selection.Carrier != LanConnectProtocolCarrier.RitsuLibSidecarV1)
        {
            return;
        }

        byte[] nonce = binding.ProtocolFlowNonce
            ?? throw new InvalidOperationException("Tail client sidecar binding has no protocol flow nonce.");
        binding.BindBidirectionalSidecarFlow(localNetId, hostNetId, nonce);
        LanConnectRitsuLibSidecarCarrier.Shared.ObserveNetService(service);
        LanConnectRitsuLibSidecarCarrier.Shared.SetPeerSupported(hostNetId);
    }

    internal void ClearSidecarPeer(INetGameService service, ulong peerNetId)
    {
        Binding binding = RequireBinding(GetMessageBus(service));
        binding.ClearSidecarPeer(peerNetId);
        LanConnectRitsuLibSidecarCarrier.Shared.SetPeerUnknown(peerNetId);
    }

    internal static IDisposable PushOutgoingSidecarRecipientsForCurrentThread(IReadOnlyList<ulong> recipientPeerIds)
    {
        (_outgoingSidecarRecipients ??= new Stack<IReadOnlyList<ulong>>()).Push(recipientPeerIds.ToArray());
        return new OutgoingSidecarRecipientScope();
    }

    internal bool TryTakeValidatedRejection(
        INetGameService service,
        ulong senderPeerId,
        out LanConnectProtocolFailure? failure)
    {
        Binding binding = RequireBinding(GetMessageBus(service));
        lock (binding.Sync)
        {
            if (binding.ValidatedRejections.Remove(senderPeerId, out LanConnectProtocolFailure? value))
            {
                failure = value;
                return true;
            }
        }

        failure = null;
        return false;
    }

    public LanConnectPreparedTailMessage PrepareOutgoing(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        object message,
        LanConnectProtocolSelection selection)
    {
        Binding binding = RequireBinding(messageBus);
        ValidateBinding(binding, senderPeerId, selection);
        return messageKind switch
        {
            LanConnectSidecarMessageKind.LobbyJoinRequest or
            LanConnectSidecarMessageKind.LoadJoinRequest or
            LanConnectSidecarMessageKind.RejoinRequest => new(
                message,
                LanConnectTailMessagePatches.EncodePeerOfferMessage(messageKind, binding.Offer)),
            LanConnectSidecarMessageKind.InitialGameInfo => HasPendingOutgoingRejectionForCurrentThread
                ? PrepareRejection(message, selection)
                : SessionOnly(messageKind, message, selection),
            LanConnectSidecarMessageKind.LobbyJoinResponse => PrepareStartRunRoster(
                messageKind,
                RequireMessage<ClientLobbyJoinResponseMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.PlayerJoined => PreparePlayerJoined(
                RequireMessage<PlayerJoinedMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.LobbyBeginRun => PrepareBeginRun(
                RequireMessage<LobbyBeginRunMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.LoadJoinResponse => PrepareLoadJoin(
                RequireMessage<ClientLoadJoinResponseMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.RejoinResponse => PrepareRejoin(
                RequireMessage<ClientRejoinResponseMessage>(message),
                selection,
                binding),
            LanConnectSidecarMessageKind.ConnectionFailed => PrepareRejection(message, selection),
            _ => throw Protocol("lan_protocol_version_mismatch", $"Unsupported Tail message kind {messageKind}.")
        };
    }

    public void SubmitSidecarBeforeVanilla(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        object message,
        byte[] container,
        LanConnectProtocolSelection selection)
    {
        _ = message;
        Binding binding = RequireBinding(messageBus);
        ValidateBinding(binding, senderPeerId, selection);
        EnsureSidecarReady(binding);

        bool mayDeferUntilControlBinding = binding.IsHost
                                           && messageKind is LanConnectSidecarMessageKind.InitialGameInfo
                                               or LanConnectSidecarMessageKind.ConnectionFailed;
        IReadOnlyList<ulong> recipients = ResolveOutgoingRecipients(binding, mayDeferUntilControlBinding);
        List<(ulong RecipientPeerId, SidecarFlow Flow, byte[] Encoded)> submissions = [];
        foreach (ulong recipientPeerId in recipients)
        {
            if (mayDeferUntilControlBinding
                && !binding.CanSendSidecarTo(senderPeerId, recipientPeerId))
            {
                binding.RememberPendingOutgoingSidecar(
                    recipientPeerId,
                    new PendingOutgoingSidecar(messageKind, senderPeerId, container.ToArray()));
                continue;
            }

            SidecarFlow flow = binding.RequireOutgoingSidecarFlow(senderPeerId, recipientPeerId);
            LanConnectSidecarFrame frame = new(
                messageKind,
                flow.FlowNonce,
                flow.NextOutgoingSequence,
                container);
            submissions.Add((recipientPeerId, flow, LanConnectSidecarFrameCodec.Encode(frame)));
        }

        List<SidecarFlow> deliveredFlows = [];
        foreach ((ulong recipientPeerId, SidecarFlow flow, byte[] encoded) in submissions)
        {
            bool sent;
            try
            {
                sent = binding.IsHost
                    ? LanConnectRitsuLibSidecarCarrier.Shared.SendToPeer(binding.Service, recipientPeerId, encoded)
                    : LanConnectRitsuLibSidecarCarrier.Shared.SendToHost(binding.Service, encoded);
            }
            catch
            {
                AbortPartialSidecarBroadcast(binding, submissions, deliveredFlows);
                throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibSidecarUnavailable());
            }

            if (!sent)
            {
                AbortPartialSidecarBroadcast(binding, submissions, deliveredFlows);
                throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibSidecarUnavailable());
            }

            deliveredFlows.Add(flow);
        }

        foreach ((_, SidecarFlow flow, _) in submissions)
        {
            flow.AdvanceOutgoing();
        }
    }

    private static void AbortPartialSidecarBroadcast(
        Binding binding,
        IReadOnlyList<(ulong RecipientPeerId, SidecarFlow Flow, byte[] Encoded)> submissions,
        IReadOnlyList<SidecarFlow> deliveredFlows)
    {
        if (deliveredFlows.Count == 0 || binding.Service is not NetHostGameService host)
        {
            return;
        }

        foreach (SidecarFlow flow in deliveredFlows)
        {
            flow.AdvanceOutgoing();
        }

        foreach (ulong recipientPeerId in submissions.Select(static submission => submission.RecipientPeerId))
        {
            host.DisconnectClient(recipientPeerId, NetError.ModMismatch);
        }
    }

    public void ValidateStandaloneIncoming(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong transportSenderPeerId,
        INetMessage message,
        byte[] container,
        LanConnectProtocolSelection selection)
    {
        Binding binding = RequireBinding(messageBus);
        ValidateBinding(binding, binding.Service.NetId, selection);
        ValidateIncomingCore(binding, messageKind, transportSenderPeerId, message, container, selection);
    }

    public void HandleIncomingFailure(
        NetMessageBus messageBus,
        ulong transportSenderPeerId,
        Exception exception,
        LanConnectProtocolSelection selection)
    {
        Binding binding = RequireBinding(messageBus);
        ValidateBinding(binding, binding.Service.NetId, selection);
        LanConnectProtocolFailure failure = exception is LanConnectProtocolException protocolException
            ? protocolException.Failure
            : Protocol("lan_protocol_version_mismatch", exception.Message).Failure;
        RejectAndDisconnect(binding, transportSenderPeerId, failure);
    }

    public bool TryPairSidecarIncoming(
        NetMessageBus messageBus,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        INetMessage message,
        LanConnectProtocolSelection selection)
    {
        Binding binding = RequireBinding(messageBus);
        ValidateBinding(binding, binding.Service.NetId, selection);
        EnsureSidecarReady(binding);

        ulong recipientPeerId = binding.Service.NetId;
        SidecarFlow flow = binding.RequireIncomingSidecarFlow(senderPeerId, recipientPeerId);
        LanConnectPairedSidecarMessage? paired = binding.SidecarPairing.SubmitVanilla(
            senderPeerId,
            recipientPeerId,
            flow.FlowNonce,
            messageKind,
            message,
            DateTimeOffset.UtcNow);
        if (paired == null)
        {
            return false;
        }

        ValidateIncomingCore(
            binding,
            paired.MessageKind,
            paired.SenderPeerId,
            message,
            paired.Frame.Container.ToArray(),
            selection);
        return true;
    }

    private void OnSidecarFrameReceived(ulong senderPeerId, byte[] payload)
    {
        LanConnectSidecarFrame frame;
        try
        {
            frame = LanConnectSidecarFrameCodec.Decode(payload);
        }
        catch (InvalidDataException)
        {
            return;
        }

        Binding[] bindings;
        lock (_sync)
        {
            bindings = _bindings.Values.ToArray();
        }

        foreach (Binding binding in bindings)
        {
            if (binding.Selection.Carrier != LanConnectProtocolCarrier.RitsuLibSidecarV1)
            {
                continue;
            }

            ulong recipientPeerId;
            try
            {
                recipientPeerId = binding.Service.NetId;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            try
            {
                if (!binding.HasIncomingSidecarFlow(senderPeerId, recipientPeerId, frame.FlowNonce.Span))
                {
                    continue;
                }

                LanConnectPairedSidecarMessage? paired = binding.SidecarPairing.SubmitFrame(
                    senderPeerId,
                    recipientPeerId,
                    frame,
                    DateTimeOffset.UtcNow);
                if (paired != null)
                {
                    ReleasePairedIncoming(binding, paired);
                }
                return;
            }
            catch (Exception exception) when (exception is InvalidDataException or LanConnectProtocolException)
            {
                RejectAndDisconnect(
                    binding,
                    senderPeerId,
                    Protocol("lan_protocol_version_mismatch", exception.Message).Failure);
                return;
            }
        }
    }

    private void ReleasePairedIncoming(Binding binding, LanConnectPairedSidecarMessage paired)
    {
        INetMessage message = (INetMessage)paired.VanillaMessage;
        ValidateIncomingCore(
            binding,
            paired.MessageKind,
            paired.SenderPeerId,
            message,
            paired.Frame.Container.ToArray(),
            binding.Selection);
        void Release() => binding.MessageBus.SendMessageToAllHandlers(message, paired.SenderPeerId);
        if (Engine.GetMainLoop() is SceneTree)
        {
            Callable.From(Release).CallDeferred();
        }
        else
        {
            Task.Run(Release);
        }
    }

    private void ValidateIncomingCore(
        Binding binding,
        LanConnectSidecarMessageKind messageKind,
        ulong senderPeerId,
        INetMessage message,
        byte[] container,
        LanConnectProtocolSelection selection)
    {
        ulong hostPeerId = binding.IsHost ? binding.Service.NetId : GetHostPeerId(binding.Service);
        LanConnectTailMessagePayload payload;
        try
        {
            payload = LanConnectTailMessagePatches.DecodeAndValidate(
                messageKind,
                container,
                selection,
                senderPeerId,
                hostPeerId);
        }
        catch (InvalidDataException) when (messageKind == LanConnectSidecarMessageKind.InitialGameInfo)
        {
            payload = LanConnectTailMessagePatches.DecodeAndValidate(
                LanConnectSidecarMessageKind.ConnectionFailed,
                container,
                selection,
                senderPeerId,
                hostPeerId);
            messageKind = LanConnectSidecarMessageKind.ConnectionFailed;
        }

        if (IsRequest(messageKind))
        {
            if (!binding.IsHost || payload.PeerOffer == null)
            {
                throw new InvalidDataException("Only a host may accept a Tail join request.");
            }

            ValidatePeerOffer(payload.PeerOffer, selection);
            return;
        }

        if (messageKind == LanConnectSidecarMessageKind.ConnectionFailed)
        {
            if (payload.Rejection == null || senderPeerId != hostPeerId || binding.IsHost)
            {
                throw new InvalidDataException("Tail rejection must be sent by the current host to a joining client.");
            }

            lock (binding.Sync)
            {
                binding.ValidatedRejections[senderPeerId] = payload.Rejection;
            }
            return;
        }

        if (senderPeerId != hostPeerId || binding.IsHost)
        {
            throw new InvalidDataException("Session-selection messages must be received from the current host.");
        }

        switch (messageKind)
        {
            case LanConnectSidecarMessageKind.InitialGameInfo:
                break;
            case LanConnectSidecarMessageKind.LobbyJoinResponse:
                RestoreStartRunResponse(
                    message,
                    (ClientLobbyJoinResponseMessage)(object)message,
                    RequireRoster(payload),
                    binding);
                break;
            case LanConnectSidecarMessageKind.PlayerJoined:
                RestorePlayerJoined(message, (PlayerJoinedMessage)(object)message, RequireRoster(payload), binding);
                break;
            case LanConnectSidecarMessageKind.LobbyBeginRun:
                RestoreBeginRun(message, (LobbyBeginRunMessage)(object)message, RequireRoster(payload), binding);
                break;
            case LanConnectSidecarMessageKind.LoadJoinResponse:
                RestoreLoadJoin(message, (ClientLoadJoinResponseMessage)(object)message, RequireRoster(payload), binding);
                break;
            case LanConnectSidecarMessageKind.RejoinResponse:
                RestoreRejoin((ClientRejoinResponseMessage)(object)message, RequireRoster(payload), binding);
                break;
            default:
                throw new InvalidDataException($"Unexpected incoming Tail message kind {messageKind}.");
        }
    }

    private static LanConnectPreparedTailMessage SessionOnly(
        LanConnectSidecarMessageKind kind,
        object message,
        LanConnectProtocolSelection selection) =>
        new(message, LanConnectTailMessagePatches.EncodeSessionMessage(kind, selection));

    private static LanConnectPreparedTailMessage PrepareStartRunRoster(
        LanConnectSidecarMessageKind kind,
        ClientLobbyJoinResponseMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        List<StartRunLobbyPlayer> players = message.playersInLobby
            ?? throw new InvalidDataException("Join response has no lobby roster.");
        (List<StartRunLobbyPlayer> projection, IReadOnlyList<LanConnectRosterPlayerCarrier> carriers) =
            ProjectStartRunPlayers(players);
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().CommitHostSnapshot(carriers);
        message.playersInLobby = projection;
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(kind, selection, snapshot));
    }

    private static LanConnectPreparedTailMessage PrepareBeginRun(
        LobbyBeginRunMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        List<StartRunLobbyPlayer> players = message.playersInLobby
            ?? throw new InvalidDataException("Begin-run message has no lobby roster.");
        (List<StartRunLobbyPlayer> projection, IReadOnlyList<LanConnectRosterPlayerCarrier> carriers) =
            ProjectStartRunPlayers(players);
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().CommitHostSnapshot(carriers);
        message.playersInLobby = projection;
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.LobbyBeginRun,
                selection,
                snapshot));
    }

    private static LanConnectPreparedTailMessage PreparePlayerJoined(
        PlayerJoinedMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().Current
            ?? throw new InvalidDataException("PlayerJoined cannot precede the authoritative join response snapshot.");
        int canonicalIndex = snapshot.Players
            .OrderBy(static player => player.RealSlotId)
            .ThenBy(static player => player.PlayerId)
            .Select((player, index) => (player, index))
            .Single(value => value.player.PlayerId == message.lobbyPlayer.id)
            .index;
        message.lobbyPlayer.slotId = canonicalIndex % 4;
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.PlayerJoined,
                selection,
                snapshot));
    }

    private static LanConnectPreparedTailMessage PrepareLoadJoin(
        ClientLoadJoinResponseMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        IReadOnlyList<LanConnectRosterPlayerCarrier> carriers = message.playersAlreadyConnected
            .Select(player =>
            {
                int realSlot = message.serializableRun.Players.FindIndex(saved => saved.NetId == player.id);
                if (realSlot < 0)
                {
                    throw new InvalidDataException("Loaded-lobby player is absent from SerializableRun.Players.");
                }
                return SerializeCarrier(player.id, realSlot, player);
            })
            .ToArray();
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().CommitHostSnapshot(carriers);
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.LoadJoinResponse,
                selection,
                snapshot));
    }

    private static LanConnectPreparedTailMessage PrepareRejoin(
        ClientRejoinResponseMessage message,
        LanConnectProtocolSelection selection,
        Binding binding)
    {
        IReadOnlyList<LanConnectRosterPlayerCarrier> carriers = message.serializableRun.Players
            .Select((player, index) => SerializeCarrier(player.NetId, index, player))
            .ToArray();
        LanConnectRosterSnapshot snapshot = binding.RequireRoster().CommitHostSnapshot(carriers);
        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.RejoinResponse,
                selection,
                snapshot));
    }

    private static LanConnectPreparedTailMessage PrepareRejection(
        object message,
        LanConnectProtocolSelection selection)
    {
        if (_outgoingRejections is not { Count: > 0 } rejections)
        {
            throw new InvalidOperationException("Tail rejection was serialized without a call-scoped protocol failure.");
        }

        return new LanConnectPreparedTailMessage(
            message,
            LanConnectTailMessagePatches.EncodeSessionMessage(
                LanConnectSidecarMessageKind.ConnectionFailed,
                selection,
                rejection: rejections.Peek()));
    }

    private static void RestoreStartRunResponse(
        object boxedMessage,
        ClientLobbyJoinResponseMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        List<StartRunLobbyPlayer> projection = message.playersInLobby
            ?? throw new InvalidDataException("Join response has no vanilla roster projection.");
        AcceptRoster(binding, snapshot, LanConnectRosterSnapshotUse.Bootstrap);
        message.playersInLobby = RestoreStartRunPlayers(snapshot, projection);
        SetBoxedField(boxedMessage, nameof(ClientLobbyJoinResponseMessage.playersInLobby), message.playersInLobby);
    }

    private static void RestoreBeginRun(
        object boxedMessage,
        LobbyBeginRunMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        List<StartRunLobbyPlayer> projection = message.playersInLobby
            ?? throw new InvalidDataException("Begin-run has no vanilla roster projection.");
        LanConnectRosterAuthorityState authority = binding.RequireRoster();
        authority.Accept(
            snapshot.AuthorityPeerId,
            snapshot,
            LanConnectRosterSnapshotUse.StateTransition);
        message.playersInLobby = RestoreStartRunPlayers(snapshot, projection);
        SetBoxedField(boxedMessage, nameof(LobbyBeginRunMessage.playersInLobby), message.playersInLobby);
    }

    private static void RestorePlayerJoined(
        object boxedMessage,
        PlayerJoinedMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        LanConnectRosterAuthorityState authority = binding.RequireRoster();
        HashSet<ulong> membership = authority.Current?.Players
            .Select(static player => player.PlayerId)
            .ToHashSet() ?? [];
        membership.Add(message.lobbyPlayer.id);
        authority.Accept(
            snapshot.AuthorityPeerId,
            snapshot,
            LanConnectRosterSnapshotUse.MembershipMutation,
            membership,
            message.lobbyPlayer.id);
        LanConnectRosterPlayerCarrier carrier = snapshot.Players.Single(
            player => player.PlayerId == message.lobbyPlayer.id);
        StartRunLobbyPlayer restored = DeserializeCarrier<StartRunLobbyPlayer>(carrier, out uint consumed);
        if (consumed != carrier.VanillaPlayerBitLength || restored.id != message.lobbyPlayer.id
            || restored.slotId != snapshot.Players
                .OrderBy(static player => player.RealSlotId)
                .ThenBy(static player => player.PlayerId)
                .Select((player, index) => (player, index))
                .Single(value => value.player.PlayerId == restored.id).index % 4)
        {
            throw new InvalidDataException("PlayerJoined body and Tail roster disagree.");
        }
        restored.slotId = carrier.RealSlotId;
        SetBoxedField(boxedMessage, nameof(PlayerJoinedMessage.lobbyPlayer), restored);
    }

    private static void RestoreLoadJoin(
        object boxedMessage,
        ClientLoadJoinResponseMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        AcceptRoster(binding, snapshot, LanConnectRosterSnapshotUse.Bootstrap);
        List<LoadRunLobbyPlayer> restored = snapshot.Players.Select(carrier =>
        {
            LoadRunLobbyPlayer player = DeserializeCarrier<LoadRunLobbyPlayer>(carrier, out uint consumed);
            if (consumed != carrier.VanillaPlayerBitLength || player.id != carrier.PlayerId
                || message.serializableRun.Players.FindIndex(saved => saved.NetId == player.id) != carrier.RealSlotId)
            {
                throw new InvalidDataException("Loaded-lobby roster carrier disagrees with the run/player binding.");
            }
            return player;
        }).ToList();
        if (!message.playersAlreadyConnected.Select(static player => player.id)
            .SequenceEqual(restored.Select(static player => player.id)))
        {
            throw new InvalidDataException("Loaded-lobby vanilla membership disagrees with the Tail roster.");
        }
        SetBoxedField(boxedMessage, nameof(ClientLoadJoinResponseMessage.playersAlreadyConnected), restored);
    }

    private static void RestoreRejoin(
        ClientRejoinResponseMessage message,
        LanConnectRosterSnapshot snapshot,
        Binding binding)
    {
        AcceptRoster(binding, snapshot, LanConnectRosterSnapshotUse.Bootstrap);
        List<SerializablePlayer> restored = snapshot.Players.Select(carrier =>
        {
            SerializablePlayer player = DeserializeCarrier<SerializablePlayer>(carrier, out uint consumed);
            if (consumed != carrier.VanillaPlayerBitLength || player.NetId != carrier.PlayerId)
            {
                throw new InvalidDataException("Rejoin roster carrier disagrees with SerializableRun.Players.");
            }
            return player;
        }).ToList();
        if (!message.serializableRun.Players.Select(static player => player.NetId)
            .SequenceEqual(restored.Select(static player => player.NetId)))
        {
            throw new InvalidDataException("Rejoin vanilla membership disagrees with the Tail roster.");
        }
        message.serializableRun.Players = restored;
    }

    private static List<StartRunLobbyPlayer> RestoreStartRunPlayers(
        LanConnectRosterSnapshot snapshot,
        IReadOnlyList<StartRunLobbyPlayer> projection)
    {
        LanConnectRosterProjection.Validate(
            snapshot,
            projection,
            static player => player.id,
            static player => player.slotId);
        return LanConnectRosterProjection.Restore(
            snapshot,
            carrier =>
            {
                StartRunLobbyPlayer player = DeserializeCarrier<StartRunLobbyPlayer>(carrier, out uint bits);
                return (player, bits);
            },
            static player => player.id,
            static player => player.slotId,
            static (player, slot) =>
            {
                player.slotId = slot;
                return player;
            }).ToList();
    }

    private static (List<StartRunLobbyPlayer> Projection, IReadOnlyList<LanConnectRosterPlayerCarrier> Carriers)
        ProjectStartRunPlayers(IReadOnlyList<StartRunLobbyPlayer> players)
    {
        IReadOnlyList<LanConnectRosterProjectionItem<StartRunLobbyPlayer>> projected =
            LanConnectRosterProjection.Create(
                players,
                static player => player.id,
                static player => player.slotId,
                static (player, slot) =>
                {
                    player.slotId = slot;
                    return player;
                });
        Dictionary<ulong, int> embeddedSlots = projected.ToDictionary(
            static item => item.PlayerId,
            static item => item.CanonicalIndex % 4);
        IReadOnlyList<LanConnectRosterPlayerCarrier> carriers = players
            .OrderBy(static player => player.slotId)
            .ThenBy(static player => player.id)
            .Select(player =>
            {
                int realSlot = player.slotId;
                int canonicalIndex = embeddedSlots.TryGetValue(player.id, out int firstFourIndex)
                    ? firstFourIndex
                    : players.OrderBy(static value => value.slotId).ThenBy(static value => value.id)
                        .Select((value, index) => (value, index))
                        .Single(value => value.value.id == player.id).index % 4;
                player.slotId = canonicalIndex;
                return SerializeCarrier(player.id, realSlot, player);
            })
            .ToArray();
        return (projected.Select(static item => item.VanillaPlayer).ToList(), carriers);
    }

    private static LanConnectRosterPlayerCarrier SerializeCarrier<T>(ulong playerId, int realSlot, T player)
        where T : IPacketSerializable
    {
        PacketWriter writer = new() { WarnOnGrow = false };
        player.Serialize(writer);
        int byteLength = checked((writer.BitPosition + 7) / 8);
        return new LanConnectRosterPlayerCarrier(
            playerId,
            checked((byte)realSlot),
            checked((uint)writer.BitPosition),
            writer.Buffer.AsSpan(0, byteLength));
    }

    private static T DeserializeCarrier<T>(LanConnectRosterPlayerCarrier carrier, out uint consumedBits)
        where T : IPacketSerializable, new()
    {
        PacketReader reader = new();
        reader.Reset(carrier.VanillaPlayerBytes.ToArray());
        T value = reader.Read<T>();
        consumedBits = checked((uint)reader.BitPosition);
        return value;
    }

    private static void AcceptRoster(
        Binding binding,
        LanConnectRosterSnapshot snapshot,
        LanConnectRosterSnapshotUse firstUse)
    {
        LanConnectRosterAuthorityState authority = binding.RequireRoster();
        authority.Accept(
            snapshot.AuthorityPeerId,
            snapshot,
            authority.Current == null ? firstUse : LanConnectRosterSnapshotUse.CurrentState);
    }

    private static void ValidatePeerOffer(
        LanConnectProtocolOffer offer,
        LanConnectProtocolSelection selection)
    {
        offer.Validate();
        if (!offer.Supports(selection.SelectedLanProtocolVersion)
            || offer.RitsuLibPresent != selection.RitsuLibPresent)
        {
            throw new InvalidDataException("Peer offer is incompatible with the frozen Tail selection.");
        }

        if (selection.Carrier == LanConnectProtocolCarrier.RitsuLibSidecarV1)
        {
            if (!offer.RitsuLibSidecarAvailable)
            {
                throw new InvalidDataException("Peer offer has no usable public RitsuLib sidecar.");
            }
        }
        else if (offer.RitsuLibSidecarAvailable)
        {
            throw new InvalidDataException("Standalone Tail peer offer unexpectedly advertises RitsuLib sidecar.");
        }
    }

    private static void EnsureSidecarReady(Binding binding)
    {
        if (binding.Selection.Carrier != LanConnectProtocolCarrier.RitsuLibSidecarV1
            || !binding.Offer.RitsuLibPresent
            || !binding.Offer.RitsuLibSidecarAvailable
            || !LanConnectRitsuLibSidecarCarrier.Shared.TryEnsureRegistered()
            || !LanConnectRitsuLibSidecarCarrier.Shared.IsReady)
        {
            throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibSidecarUnavailable());
        }
    }

    private static IReadOnlyList<ulong> ResolveOutgoingRecipients(
        Binding binding,
        bool permitUnboundRecipients = false)
    {
        if (!binding.IsHost)
        {
            return [GetHostPeerId(binding.Service)];
        }

        if (_outgoingSidecarRecipients is { Count: > 0 } stack)
        {
            IReadOnlyList<ulong> requested = stack.Peek();
            ulong[] recipients = requested.Distinct().ToArray();
            if (recipients.Length != requested.Count
                || (!permitUnboundRecipients && recipients.Any(peerId => !binding.HasSidecarPeer(peerId))))
            {
                throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibSidecarUnavailable());
            }

            return recipients;
        }

        ulong[] fallback = binding.BoundPeerIds.ToArray();
        if (fallback.Length > 0)
        {
            return fallback;
        }

        throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibSidecarUnavailable());
    }

    private static void SendDeferredHostSidecar(
        Binding binding,
        ulong recipientPeerId,
        PendingOutgoingSidecar pending)
    {
        SidecarFlow flow = binding.RequireOutgoingSidecarFlow(pending.SenderPeerId, recipientPeerId);
        LanConnectSidecarFrame frame = new(
            pending.MessageKind,
            flow.FlowNonce,
            flow.NextOutgoingSequence,
            pending.Container);
        bool sent;
        try
        {
            sent = LanConnectRitsuLibSidecarCarrier.Shared.SendToPeer(
                binding.Service,
                recipientPeerId,
                LanConnectSidecarFrameCodec.Encode(frame));
        }
        catch
        {
            sent = false;
        }

        if (!sent)
        {
            if (binding.Service is NetHostGameService host)
            {
                host.DisconnectClient(recipientPeerId, NetError.ModMismatch);
            }
            throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibSidecarUnavailable());
        }

        flow.AdvanceOutgoing();
    }

    private static LanConnectRosterSnapshot RequireRoster(LanConnectTailMessagePayload payload) =>
        payload.Roster ?? throw new InvalidDataException("Tail message is missing its authoritative roster.");

    private void RejectAndDisconnect(
        Binding binding,
        ulong senderPeerId,
        LanConnectProtocolFailure failure)
    {
        if (binding.Service is NetHostGameService host)
        {
            Stack<LanConnectProtocolFailure> rejections =
                _outgoingRejections ??= new Stack<LanConnectProtocolFailure>();
            rejections.Push(failure);
            try
            {
                host.SendMessage(new InitialGameInfoMessage
                {
                    connectionFailureReason = ConnectionFailureReason.ModMismatch
                }, senderPeerId);
            }
            finally
            {
                LanConnectProtocolFailure popped = rejections.Pop();
                if (!ReferenceEquals(popped, failure))
                {
                    throw new InvalidOperationException("Tail rejection call context was corrupted.");
                }
                host.DisconnectClient(senderPeerId, NetError.ModMismatch);
            }
        }
        else
        {
            binding.Service.Disconnect(NetError.ModMismatch);
        }
    }

    private void Bind(
        INetGameService service,
        NetMessageBus bus,
        LanConnectProtocolOffer offer,
        LanConnectProtocolSelection selection,
        bool isHost,
        byte[]? protocolFlowNonce = null)
    {
        offer.Validate();
        selection.Validate(offer);
        Binding binding = new(service, offer, selection, isHost, protocolFlowNonce);
        lock (_sync)
        {
            _bindings[bus] = binding;
        }
        if (selection.Carrier == LanConnectProtocolCarrier.RitsuLibSidecarV1 && isHost)
        {
            LanConnectRitsuLibSidecarCarrier.Shared.ObserveNetService(service);
        }
    }

    private Binding RequireBinding(NetMessageBus messageBus)
    {
        lock (_sync)
        {
            return _bindings.TryGetValue(messageBus, out Binding? binding)
                ? binding
                : throw new InvalidOperationException("Tail message bus has no ticket/session binding.");
        }
    }

    private static void ValidateBinding(
        Binding binding,
        ulong localSenderPeerId,
        LanConnectProtocolSelection selection)
    {
        if (binding.Selection != selection || binding.Service.NetId != localSenderPeerId)
        {
            throw Protocol(
                "protocol_selection_conflict",
                "Tail message service/selection differs from the frozen session binding.");
        }
    }

    private static ulong GetHostPeerId(INetGameService service) => service switch
    {
        NetHostGameService host => host.NetId,
        NetClientGameService client => client.HostNetId,
        _ => throw new InvalidOperationException("Unsupported Tail net service type.")
    };

    private static NetMessageBus GetMessageBus(INetGameService service) => service switch
    {
        NetHostGameService host => HostMessageBus.GetValue(host) as NetMessageBus
            ?? throw new InvalidOperationException("NetHostGameService._messageBus is unavailable."),
        NetClientGameService client => ClientMessageBus.GetValue(client) as NetMessageBus
            ?? throw new InvalidOperationException("NetClientGameService._messageBus is unavailable."),
        _ => throw new InvalidOperationException("Unsupported Tail net service type.")
    };

    private static FieldInfo RequireMessageBus(Type serviceType) =>
        serviceType.GetField("_messageBus", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(serviceType.FullName, "_messageBus");

    private static T RequireMessage<T>(object message) where T : struct, INetMessage =>
        message is T typed
            ? typed
            : throw new InvalidOperationException(
                $"Expected {typeof(T).FullName}, got {message.GetType().FullName}.");

    private static void SetBoxedField(object boxedMessage, string fieldName, object value)
    {
        FieldInfo field = boxedMessage.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingFieldException(boxedMessage.GetType().FullName, fieldName);
        field.SetValue(boxedMessage, value);
    }

    private static bool IsRequest(LanConnectSidecarMessageKind kind) => kind is
        LanConnectSidecarMessageKind.LobbyJoinRequest or
        LanConnectSidecarMessageKind.LoadJoinRequest or
        LanConnectSidecarMessageKind.RejoinRequest;

    private static LanConnectProtocolException Protocol(string code, string detail) =>
        LanConnectProtocolFailureMapper.FromLocalException(code, detail);

    private sealed class Binding
    {
        internal Binding(
            INetGameService service,
            LanConnectProtocolOffer offer,
            LanConnectProtocolSelection selection,
            bool isHost,
            byte[]? protocolFlowNonce)
        {
            Service = service;
            Offer = offer;
            Selection = selection;
            IsHost = isHost;
            ProtocolFlowNonce = protocolFlowNonce?.ToArray();
            MessageBus = GetMessageBus(service);
            _roster = isHost ? new LanConnectRosterAuthorityState(service.NetId) : null;
        }

        internal object Sync { get; } = new();
        internal INetGameService Service { get; }
        internal LanConnectProtocolOffer Offer { get; }
        internal LanConnectProtocolSelection Selection { get; }
        internal bool IsHost { get; }
        internal byte[]? ProtocolFlowNonce { get; }
        internal NetMessageBus MessageBus { get; }
        internal LanConnectSidecarPairingCache SidecarPairing { get; } = new();
        private readonly Dictionary<SidecarFlowKey, SidecarFlow> _sidecarFlows = [];
        private readonly Dictionary<ulong, PendingOutgoingSidecar> _pendingOutgoingSidecars = [];
        private LanConnectRosterAuthorityState? _roster;
        internal LanConnectRosterAuthorityState RequireRoster()
        {
            lock (Sync)
            {
                return _roster ??= new LanConnectRosterAuthorityState(GetHostPeerId(Service));
            }
        }
        internal Dictionary<ulong, LanConnectProtocolFailure> ValidatedRejections { get; } = [];

        internal IReadOnlyCollection<ulong> BoundPeerIds
        {
            get
            {
                lock (Sync)
                {
                    ulong localPeerId = Service.NetId;
                    return _sidecarFlows.Keys
                        .Where(key => key.SenderPeerId == localPeerId || key.RecipientPeerId == localPeerId)
                        .Select(key => key.SenderPeerId == localPeerId ? key.RecipientPeerId : key.SenderPeerId)
                        .Where(peerId => peerId != localPeerId)
                        .Distinct()
                        .ToArray();
                }
            }
        }

        internal bool HasSidecarPeer(ulong peerNetId)
        {
            lock (Sync)
            {
                return _sidecarFlows.Keys.Any(key =>
                    key.SenderPeerId == peerNetId || key.RecipientPeerId == peerNetId);
            }
        }

        internal bool CanSendSidecarTo(ulong senderPeerId, ulong recipientPeerId)
        {
            lock (Sync)
            {
                return _sidecarFlows.ContainsKey(new SidecarFlowKey(senderPeerId, recipientPeerId))
                       && LanConnectRitsuLibSidecarCarrier.Shared.CanSendToPeer(recipientPeerId);
            }
        }

        internal void RememberPendingOutgoingSidecar(
            ulong recipientPeerId,
            PendingOutgoingSidecar pending)
        {
            lock (Sync)
            {
                if (!_pendingOutgoingSidecars.ContainsKey(recipientPeerId)
                    && _pendingOutgoingSidecars.Count >= LanConnectConstants.ProtocolMaxPlayers)
                {
                    throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibSidecarUnavailable());
                }

                _pendingOutgoingSidecars[recipientPeerId] = pending;
            }
        }

        internal PendingOutgoingSidecar? TakePendingOutgoingSidecar(ulong recipientPeerId)
        {
            lock (Sync)
            {
                return _pendingOutgoingSidecars.Remove(recipientPeerId, out PendingOutgoingSidecar? pending)
                    ? pending
                    : null;
            }
        }

        internal void BindBidirectionalSidecarFlow(
            ulong localPeerId,
            ulong remotePeerId,
            ReadOnlySpan<byte> protocolFlowNonce)
        {
            if (protocolFlowNonce.Length != LanConnectSidecarFrameCodec.FlowNonceBytes)
            {
                throw Protocol(
                    "lan_protocol_version_mismatch",
                    "Ritsu sidecar binding requires a 16-byte protocol flow nonce.");
            }

            lock (Sync)
            {
                BindSidecarFlowLocked(localPeerId, remotePeerId, protocolFlowNonce);
                BindSidecarFlowLocked(remotePeerId, localPeerId, protocolFlowNonce);
            }
        }

        internal SidecarFlow RequireOutgoingSidecarFlow(ulong senderPeerId, ulong recipientPeerId)
        {
            lock (Sync)
            {
                SidecarFlowKey key = new(senderPeerId, recipientPeerId);
                if (_sidecarFlows.TryGetValue(key, out SidecarFlow? flow)
                    && LanConnectRitsuLibSidecarCarrier.Shared.CanSendToPeer(recipientPeerId))
                {
                    return flow;
                }
            }

            throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibSidecarUnavailable());
        }

        internal SidecarFlow RequireIncomingSidecarFlow(ulong senderPeerId, ulong recipientPeerId)
        {
            lock (Sync)
            {
                SidecarFlowKey key = new(senderPeerId, recipientPeerId);
                return _sidecarFlows.TryGetValue(key, out SidecarFlow? flow)
                    ? flow
                    : throw new InvalidDataException("Incoming Ritsu sidecar frame has no trusted flow binding.");
            }
        }

        internal bool HasIncomingSidecarFlow(
            ulong senderPeerId,
            ulong recipientPeerId,
            ReadOnlySpan<byte> protocolFlowNonce)
        {
            lock (Sync)
            {
                SidecarFlowKey key = new(senderPeerId, recipientPeerId);
                return _sidecarFlows.TryGetValue(key, out SidecarFlow? flow)
                       && flow.FlowNonce.SequenceEqual(protocolFlowNonce);
            }
        }

        internal void ClearSidecarPeer(ulong peerNetId)
        {
            lock (Sync)
            {
                foreach (SidecarFlowKey key in _sidecarFlows.Keys
                             .Where(key => key.SenderPeerId == peerNetId || key.RecipientPeerId == peerNetId)
                             .ToArray())
                {
                    _sidecarFlows.Remove(key);
                }
                SidecarPairing.ClearPeer(peerNetId);
                _pendingOutgoingSidecars.Remove(peerNetId);
            }
        }

        internal void ClearSidecar()
        {
            lock (Sync)
            {
                _sidecarFlows.Clear();
                _pendingOutgoingSidecars.Clear();
                SidecarPairing.Clear();
            }
        }

        private void BindSidecarFlowLocked(
            ulong senderPeerId,
            ulong recipientPeerId,
            ReadOnlySpan<byte> protocolFlowNonce)
        {
            SidecarFlowKey key = new(senderPeerId, recipientPeerId);
            if (_sidecarFlows.ContainsKey(key))
            {
                return;
            }

            SidecarPairing.BindFlow(senderPeerId, recipientPeerId, protocolFlowNonce);
            _sidecarFlows.Add(key, new SidecarFlow(protocolFlowNonce));
        }
    }

    private sealed class OutgoingSidecarRecipientScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0
                && _outgoingSidecarRecipients is { Count: > 0 } recipients)
            {
                recipients.Pop();
            }
        }
    }

    private readonly record struct SidecarFlowKey(ulong SenderPeerId, ulong RecipientPeerId);

    private sealed record PendingOutgoingSidecar(
        LanConnectSidecarMessageKind MessageKind,
        ulong SenderPeerId,
        byte[] Container);

    private sealed class SidecarFlow
    {
        private readonly byte[] _flowNonce;

        internal SidecarFlow(ReadOnlySpan<byte> flowNonce)
        {
            _flowNonce = flowNonce.ToArray();
        }

        internal ReadOnlySpan<byte> FlowNonce => _flowNonce;
        internal uint NextOutgoingSequence { get; private set; } = 1;

        internal void AdvanceOutgoing()
        {
            if (NextOutgoingSequence == uint.MaxValue)
            {
                throw new InvalidDataException("Ritsu sidecar outgoing message sequence is exhausted.");
            }

            NextOutgoingSequence++;
        }
    }
}
