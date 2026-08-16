using System.Reflection;
using GdUnit4;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectTailMessageRuntimeTests
{
    private static readonly object InitializationSync = new();
    private static bool _initialized;

    [TestCase(2)]
    [TestCase(5)]
    public void Normal_join_projects_four_and_restores_the_full_roster(int playerCount)
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        ClientLobbyJoinResponseMessage response = new()
        {
            playersInLobby = StartRunPlayers(playerCount),
            modifiers = []
        };

        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            response,
            pair.Selection);
        ClientLobbyJoinResponseMessage projected = (ClientLobbyJoinResponseMessage)prepared.Message;
        AssertThat(projected.playersInLobby!.Count).IsEqual(Math.Min(4, playerCount));
        AssertThat(projected.playersInLobby.Select(static player => player.slotId).ToArray())
            .IsEqual(Enumerable.Range(0, Math.Min(4, playerCount)).ToArray());

        INetMessage boxed = projected;
        pair.Runtime.ValidateStandaloneIncoming(
            pair.ClientBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            boxed,
            prepared.Container,
            pair.Selection);
        ClientLobbyJoinResponseMessage restored = (ClientLobbyJoinResponseMessage)boxed;
        AssertThat(restored.playersInLobby!.Count).IsEqual(playerCount);
        AssertThat(restored.playersInLobby.Select(static player => player.slotId).ToArray())
            .IsEqual(Enumerable.Range(0, playerCount).ToArray());
    }

    [TestCase]
    public void Normal_join_restores_sparse_high_real_slots()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        int[] realSlots = [0, 2, 5, 7];
        ClientLobbyJoinResponseMessage response = new()
        {
            playersInLobby = StartRunPlayers(realSlots),
            modifiers = []
        };

        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            response,
            pair.Selection);
        ClientLobbyJoinResponseMessage projected = (ClientLobbyJoinResponseMessage)prepared.Message;
        AssertThat(projected.playersInLobby!.Select(static player => player.slotId).ToArray())
            .IsEqual(new[] { 0, 1, 2, 3 });

        INetMessage boxed = projected;
        pair.Runtime.ValidateStandaloneIncoming(
            pair.ClientBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            boxed,
            prepared.Container,
            pair.Selection);
        ClientLobbyJoinResponseMessage restored = (ClientLobbyJoinResponseMessage)boxed;
        AssertThat(restored.playersInLobby!.Select(static player => player.slotId).ToArray())
            .IsEqual(realSlots);
    }

    [TestCase]
    public void Player_joined_mutates_once_and_begin_run_reuses_the_committed_snapshot()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        int[] firstSlots = [0, 5];
        int[] expandedSlots = [0, 5, 7];
        IReadOnlyList<StartRunLobbyPlayer> firstPlayers = StartRunPlayers(firstSlots);
        LanConnectPreparedTailMessage firstJoin = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            new ClientLobbyJoinResponseMessage { playersInLobby = firstPlayers.ToList(), modifiers = [] },
            pair.Selection);
        INetMessage firstJoinBox = (INetMessage)firstJoin.Message;
        pair.Runtime.ValidateStandaloneIncoming(
            pair.ClientBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            firstJoinBox,
            firstJoin.Container,
            pair.Selection);

        IReadOnlyList<StartRunLobbyPlayer> expandedBootstrap = StartRunPlayers(expandedSlots);
        LanConnectPreparedTailMessage newClientBootstrap = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            new ClientLobbyJoinResponseMessage { playersInLobby = expandedBootstrap.ToList(), modifiers = [] },
            pair.Selection);
        LanConnectRosterSnapshot revisionTwo = DecodeRoster(
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            newClientBootstrap.Container,
            pair);
        AssertThat(revisionTwo.RosterRevision).IsEqual(2u);

        LanConnectPreparedTailMessage joined = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.PlayerJoined,
            pair.HostId,
            new PlayerJoinedMessage { lobbyPlayer = StartRunPlayers([7]).Single() },
            pair.Selection);
        INetMessage joinedBox = (INetMessage)joined.Message;
        pair.Runtime.ValidateStandaloneIncoming(
            pair.ClientBus,
            LanConnectSidecarMessageKind.PlayerJoined,
            pair.HostId,
            joinedBox,
            joined.Container,
            pair.Selection);
        PlayerJoinedMessage restoredJoined = (PlayerJoinedMessage)joinedBox;
        AssertThat(restoredJoined.lobbyPlayer.slotId).IsEqual(7);

        LanConnectPreparedTailMessage begin = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId,
            new LobbyBeginRunMessage
            {
                playersInLobby = StartRunPlayers(expandedSlots),
                seed = "seed",
                modifiers = [],
                act1 = "act"
            },
            pair.Selection);
        LanConnectRosterSnapshot beginRoster = DecodeRoster(
            LanConnectSidecarMessageKind.LobbyBeginRun,
            begin.Container,
            pair);
        AssertThat(beginRoster.RosterRevision).IsEqual(2u);
        INetMessage beginBox = (INetMessage)begin.Message;
        pair.Runtime.ValidateStandaloneIncoming(
            pair.ClientBus,
            LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId,
            beginBox,
            begin.Container,
            pair.Selection);
        LobbyBeginRunMessage restoredBegin = (LobbyBeginRunMessage)beginBox;
        AssertThat(restoredBegin.playersInLobby!.Select(static player => player.slotId).ToArray())
            .IsEqual(expandedSlots);
    }

    [TestCase]
    public void Begin_run_accepts_the_next_revision_when_ready_state_changes()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        List<StartRunLobbyPlayer> waitingPlayers = StartRunPlayers([0, 1]);
        for (int index = 0; index < waitingPlayers.Count; index++)
        {
            StartRunLobbyPlayer player = waitingPlayers[index];
            player.isReady = false;
            waitingPlayers[index] = player;
        }

        LanConnectPreparedTailMessage joined = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            new ClientLobbyJoinResponseMessage { playersInLobby = waitingPlayers, modifiers = [] },
            pair.Selection);
        INetMessage joinedBox = (INetMessage)joined.Message;
        pair.Runtime.ValidateStandaloneIncoming(
            pair.ClientBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            joinedBox,
            joined.Container,
            pair.Selection);

        List<StartRunLobbyPlayer> readyPlayers = StartRunPlayers([0, 1]);
        LanConnectPreparedTailMessage begin = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId,
            new LobbyBeginRunMessage
            {
                playersInLobby = readyPlayers,
                seed = "seed",
                modifiers = [],
                act1 = "act"
            },
            pair.Selection);
        LanConnectRosterSnapshot beginRoster = DecodeRoster(
            LanConnectSidecarMessageKind.LobbyBeginRun,
            begin.Container,
            pair);
        AssertThat(beginRoster.RosterRevision).IsEqual(2u);

        INetMessage beginBox = (INetMessage)begin.Message;
        pair.Runtime.ValidateStandaloneIncoming(
            pair.ClientBus,
            LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId,
            beginBox,
            begin.Container,
            pair.Selection);
        LobbyBeginRunMessage restored = (LobbyBeginRunMessage)beginBox;
        AssertThat(restored.playersInLobby!.All(static player => player.isReady)).IsTrue();
    }

    [TestCase]
    public void Load_join_restores_the_current_roster_slots()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        SerializableRun run = RunWithPlayers(5);
        ClientLoadJoinResponseMessage response = new()
        {
            serializableRun = run,
            playersAlreadyConnected = run.Players
                .Select(static player => new LoadRunLobbyPlayer
                {
                    id = player.NetId,
                    isReady = true
                })
                .ToList()
        };
        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LoadJoinResponse,
            pair.HostId,
            response,
            pair.Selection);
        LanConnectRosterSnapshot roster = DecodeRoster(
            LanConnectSidecarMessageKind.LoadJoinResponse,
            prepared.Container,
            pair);
        AssertThat(roster.Players.Select(static player => (int)player.RealSlotId).ToArray())
            .IsEqual(Enumerable.Range(0, 5).ToArray());

        INetMessage boxed = (INetMessage)prepared.Message;
        pair.Runtime.ValidateStandaloneIncoming(
            pair.ClientBus,
            LanConnectSidecarMessageKind.LoadJoinResponse,
            pair.HostId,
            boxed,
            prepared.Container,
            pair.Selection);
        ClientLoadJoinResponseMessage restored = (ClientLoadJoinResponseMessage)boxed;
        AssertThat(restored.playersAlreadyConnected.Select(static player => player.id).ToArray())
            .IsEqual(run.Players.Select(static player => player.NetId).ToArray());
    }

    [TestCase]
    public void Rejoin_restores_serializable_players_in_real_slot_order()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        ClientRejoinResponseMessage response = new() { serializableRun = RunWithPlayers(5) };
        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.RejoinResponse,
            pair.HostId,
            response,
            pair.Selection);
        INetMessage boxed = (INetMessage)prepared.Message;
        pair.Runtime.ValidateStandaloneIncoming(
            pair.ClientBus,
            LanConnectSidecarMessageKind.RejoinResponse,
            pair.HostId,
            boxed,
            prepared.Container,
            pair.Selection);
        ClientRejoinResponseMessage restored = (ClientRejoinResponseMessage)boxed;
        AssertThat(restored.serializableRun.Players.Select(static player => player.NetId).ToArray())
            .IsEqual(Enumerable.Range(0, 5).Select(static slot => 100UL + (ulong)slot).ToArray());
    }

    [TestCase]
    public void Official_Ritsu_public_sidecar_contract_registers_and_hints_supported_then_unknown()
    {
        Assembly ritsuAssembly = LoadOfficialRitsuAssembly();
        LanConnectRitsuLibSidecarCarrier.Shared.ResetForTesting();

        LanConnectExternalCapabilitySnapshot snapshot = LanConnectExternalCapabilityCollector.Collect([ritsuAssembly]);
        AssertThat(snapshot.RitsuLibPresent).IsTrue();
        AssertThat(snapshot.RitsuLibSidecarAvailable).IsTrue();

        using RuntimePair pair = new(ritsu: true);
        LanConnectRitsuLibSidecarCarrier.Shared.ObserveNetService(pair.Host);
        LanConnectRitsuLibSidecarCarrier.Shared.SetPeerSupported(pair.ClientId);
        AssertThat(LanConnectRitsuLibSidecarCarrier.Shared.CanSendToPeer(pair.ClientId)).IsTrue();
        LanConnectRitsuLibSidecarCarrier.Shared.SetPeerUnknown(pair.ClientId);
        AssertThat(LanConnectRitsuLibSidecarCarrier.Shared.CanSendToPeer(pair.ClientId)).IsFalse();
    }

    [TestCase]
    public void Ritsu_initial_game_info_sidecar_waits_for_the_control_binding_then_flushes()
    {
        _ = LoadOfficialRitsuAssembly();
        InitializeSts2Serialization();
        LanConnectRitsuLibSidecarCarrier.Shared.ResetForTesting();
        using RuntimePair pair = new(ritsu: true, bindRitsuFlows: false);
        InitialGameInfoMessage message = new();
        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.InitialGameInfo,
            pair.HostId,
            message,
            pair.Selection);

        using (LanConnectTailMessageRuntime.PushOutgoingSidecarRecipientsForCurrentThread([pair.ClientId]))
        {
            pair.Runtime.SubmitSidecarBeforeVanilla(
                pair.HostBus,
                LanConnectSidecarMessageKind.InitialGameInfo,
                pair.HostId,
                message,
                prepared.Container,
                pair.Selection);
        }

        AssertThat(pair.HostTransport.SentToClients.Count).IsEqual(0);

        pair.Runtime.BindHostTrustedSidecarFlow(pair.Host, pair.ClientId, pair.ProtocolFlowNonce);

        AssertThat(pair.HostTransport.SentToClients.Count).IsEqual(1);
        AssertThat(pair.HostTransport.SentToClients[0].PeerId).IsEqual(pair.ClientId);
    }

    [TestCase]
    public void Ritsu_broadcast_preflight_rejects_three_peer_partial_delivery_before_any_send()
    {
        _ = LoadOfficialRitsuAssembly();
        InitializeSts2Serialization();
        LanConnectRitsuLibSidecarCarrier.Shared.ResetForTesting();
        using RuntimePair pair = new(ritsu: true);
        const ulong secondPeerId = 23;
        const ulong thirdPeerId = 24;
        byte[] secondNonce = Enumerable.Repeat((byte)0x22, LanConnectSidecarFrameCodec.FlowNonceBytes).ToArray();
        byte[] thirdNonce = Enumerable.Repeat((byte)0x33, LanConnectSidecarFrameCodec.FlowNonceBytes).ToArray();
        pair.Runtime.BindHostTrustedSidecarFlow(pair.Host, secondPeerId, secondNonce);
        pair.Runtime.BindHostTrustedSidecarFlow(pair.Host, thirdPeerId, thirdNonce);
        LanConnectRitsuLibSidecarCarrier.Shared.SetPeerUnknown(thirdPeerId);

        LanConnectProtocolException? failure = null;
        using (LanConnectTailMessageRuntime.PushOutgoingSidecarRecipientsForCurrentThread(
                   [pair.ClientId, secondPeerId, thirdPeerId]))
        {
            try
            {
                pair.Runtime.SubmitSidecarBeforeVanilla(
                    pair.HostBus,
                    LanConnectSidecarMessageKind.PlayerJoined,
                    pair.HostId,
                    new object(),
                    [0x01],
                    pair.Selection);
            }
            catch (LanConnectProtocolException exception)
            {
                failure = exception;
            }
        }

        AssertThat(failure != null).IsTrue();
        AssertThat(failure!.Failure.Code).IsEqual("ritsulib_sidecar_unavailable");
        AssertThat(pair.HostTransport.SentToClients.Count).IsEqual(0);
    }

    [TestCase]
    public void Ritsu_broadcast_disconnects_all_recipients_when_a_later_send_fails()
    {
        _ = LoadOfficialRitsuAssembly();
        InitializeSts2Serialization();
        LanConnectRitsuLibSidecarCarrier.Shared.ResetForTesting();
        using RuntimePair pair = new(ritsu: true);
        const ulong secondPeerId = 23;
        byte[] secondNonce = Enumerable.Repeat((byte)0x22, LanConnectSidecarFrameCodec.FlowNonceBytes).ToArray();
        pair.Runtime.BindHostTrustedSidecarFlow(pair.Host, secondPeerId, secondNonce);
        LanConnectRitsuLibSidecarCarrier.Shared.ObserveNetService(pair.Host);
        LanConnectRitsuLibSidecarCarrier.Shared.SetPeerSupported(pair.ClientId);
        LanConnectRitsuLibSidecarCarrier.Shared.SetPeerSupported(secondPeerId);
        pair.HostTransport.ThrowForPeerId = secondPeerId;
        byte[] container = LanConnectTailCodec.Encode(1, []);

        LanConnectProtocolException? failure = null;
        using (LanConnectTailMessageRuntime.PushOutgoingSidecarRecipientsForCurrentThread(
                   [pair.ClientId, secondPeerId]))
        {
            try
            {
                pair.Runtime.SubmitSidecarBeforeVanilla(
                    pair.HostBus,
                    LanConnectSidecarMessageKind.PlayerJoined,
                    pair.HostId,
                    new object(),
                    container,
                    pair.Selection);
            }
            catch (LanConnectProtocolException exception)
            {
                failure = exception;
            }
        }

        AssertThat(failure != null).IsTrue();
        AssertThat(failure!.Failure.Code).IsEqual("ritsulib_sidecar_unavailable");
        AssertThat(pair.HostTransport.SentToClients.Select(static packet => packet.PeerId).ToArray())
            .IsEqual(new[] { pair.ClientId });
        AssertThat(pair.HostTransport.DisconnectedPeers.OrderBy(static peerId => peerId).ToArray())
            .IsEqual(new[] { pair.ClientId, secondPeerId });
    }

    [TestCase]
    public void Ritsu_sidecar_frame_first_pairing_releases_vanilla_deserialization()
    {
        _ = LoadOfficialRitsuAssembly();
        InitializeSts2Serialization();
        using RuntimePair pair = new(ritsu: true);
        ClientLobbyJoinRequestMessage request = JoinRequest();
        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.ClientBus,
            LanConnectSidecarMessageKind.LobbyJoinRequest,
            pair.ClientId,
            request,
            pair.Selection);
        LanConnectSidecarFrame frame = new(
            LanConnectSidecarMessageKind.LobbyJoinRequest,
            pair.ProtocolFlowNonce,
            1,
            prepared.Container);

        InvokeSidecarFrame(pair.Runtime, pair.ClientId, LanConnectSidecarFrameCodec.Encode(frame));
        INetMessage boxed = request;
        bool paired = pair.Runtime.TryPairSidecarIncoming(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinRequest,
            pair.ClientId,
            boxed,
            pair.Selection);

        AssertThat(paired).IsTrue();
    }

    [TestCase]
    public async Task Ritsu_sidecar_vanilla_first_pairing_defers_handler_release_until_frame_arrives()
    {
        _ = LoadOfficialRitsuAssembly();
        InitializeSts2Serialization();
        using RuntimePair pair = new(ritsu: true);
        int handled = 0;
        pair.Host.RegisterMessageHandler<ClientLobbyJoinRequestMessage>((_, senderId) =>
        {
            if (senderId == pair.ClientId)
            {
                handled++;
            }
        });
        ClientLobbyJoinRequestMessage request = JoinRequest();
        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.ClientBus,
            LanConnectSidecarMessageKind.LobbyJoinRequest,
            pair.ClientId,
            request,
            pair.Selection);

        INetMessage boxed = request;
        bool pairedBeforeFrame = pair.Runtime.TryPairSidecarIncoming(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinRequest,
            pair.ClientId,
            boxed,
            pair.Selection);
        AssertThat(pairedBeforeFrame).IsFalse();
        AssertThat(handled).IsEqual(0);

        LanConnectSidecarFrame frame = new(
            LanConnectSidecarMessageKind.LobbyJoinRequest,
            pair.ProtocolFlowNonce,
            1,
            prepared.Container);
        InvokeSidecarFrame(pair.Runtime, pair.ClientId, LanConnectSidecarFrameCodec.Encode(frame));
        await Task.Delay(100);

        AssertThat(handled).IsEqual(1);
    }

    [TestCase]
    public void Malformed_request_returns_false_sends_structured_rejection_and_disconnects_peer()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        Harmony harmony = new($"sts2_lan_connect.tests.tail_runtime.{Guid.NewGuid():N}");
        using LanConnectSessionProtocolLease lease =
            LanConnectSessionProtocolState.Shared.FreezeHost(pair.Selection, harmony.Id);

        try
        {
            LanConnectTailMessagePatches.ConfigureRuntime(pair.Runtime);
            LanConnectTailMessagePatches.Apply(harmony);
            byte[] validRequest = pair.ClientBus.SerializeMessage(
                pair.ClientId,
                new ClientLobbyJoinRequestMessage
                {
                    maxAscensionUnlocked = 0,
                    unlockState = new SerializableUnlockState()
                },
                out int validLength).AsSpan(0, validLength).ToArray();
            byte[] truncatedRequest = validRequest[..^1];

            using (LanConnectTailMessagePatches.PushTransportSenderForTesting(pair.ClientId))
            {
                bool decoded = pair.HostBus.TryDeserializeMessage(
                    truncatedRequest,
                    out INetMessage? _,
                    out ulong? _);
                AssertThat(decoded).IsFalse();
            }

            AssertThat(pair.HostTransport.DisconnectedPeers).Contains(pair.ClientId);
            CapturedPacket rejection = pair.HostTransport.SentToClients.Single();
            pair.Client.OnPacketReceived(
                pair.HostId,
                rejection.Bytes,
                NetTransferMode.Reliable,
                0);
            bool hasFailure = pair.Runtime.TryTakeValidatedRejection(
                pair.Client,
                pair.HostId,
                out LanConnectProtocolFailure? receivedFailure);
            AssertThat(hasFailure).IsTrue();
            AssertThat(receivedFailure != null).IsTrue();
            AssertThat(receivedFailure!.Code).IsEqual("lan_protocol_version_mismatch");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            LanConnectTailMessagePatches.ConfigureRuntime(LanConnectTailMessageRuntime.Shared);
        }
    }

    [TestCase]
    public void Embedded_sender_mismatch_rejects_the_authenticated_transport_peer()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        Harmony harmony = new($"sts2_lan_connect.tests.tail_runtime.{Guid.NewGuid():N}");
        using LanConnectSessionProtocolLease lease =
            LanConnectSessionProtocolState.Shared.FreezeHost(pair.Selection, harmony.Id);

        try
        {
            LanConnectTailMessagePatches.ConfigureRuntime(pair.Runtime);
            LanConnectTailMessagePatches.Apply(harmony);
            byte[] requestFromClientId = pair.ClientBus.SerializeMessage(
                pair.ClientId,
                new ClientLobbyJoinRequestMessage
                {
                    maxAscensionUnlocked = 0,
                    unlockState = new SerializableUnlockState()
                },
                out int requestLength).AsSpan(0, requestLength).ToArray();
            ulong authenticatedPeerId = pair.ClientId + 1;

            using (LanConnectTailMessagePatches.PushTransportSenderForTesting(authenticatedPeerId))
            {
                bool decoded = pair.HostBus.TryDeserializeMessage(
                    requestFromClientId,
                    out INetMessage? _,
                    out ulong? _);
                AssertThat(decoded).IsFalse();
            }

            AssertThat(pair.HostTransport.DisconnectedPeers).Contains(authenticatedPeerId);
            CapturedPacket rejection = pair.HostTransport.SentToClients.Single();
            AssertThat(rejection.PeerId).IsEqual(authenticatedPeerId);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            LanConnectTailMessagePatches.ConfigureRuntime(LanConnectTailMessageRuntime.Shared);
        }
    }

    private static LanConnectRosterSnapshot DecodeRoster(
        LanConnectSidecarMessageKind kind,
        byte[] container,
        RuntimePair pair) =>
        LanConnectTailMessagePatches.DecodeAndValidate(
            kind,
            container,
            pair.Selection,
            pair.HostId,
            pair.HostId).Roster!;

    private static ClientLobbyJoinRequestMessage JoinRequest() => new()
    {
        maxAscensionUnlocked = 0,
        unlockState = new SerializableUnlockState()
    };

    private static void InvokeSidecarFrame(
        LanConnectTailMessageRuntime runtime,
        ulong senderPeerId,
        byte[] frame)
    {
        typeof(LanConnectTailMessageRuntime)
            .GetMethod("OnSidecarFrameReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(runtime, [senderPeerId, frame]);
    }

    private static Assembly LoadOfficialRitsuAssembly()
    {
        Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(static assembly =>
                string.Equals(assembly.GetName().Name, "STS2-RitsuLib", StringComparison.OrdinalIgnoreCase));
        if (loaded != null)
        {
            return loaded;
        }

        string localCopy = Path.Combine(AppContext.BaseDirectory, "STS2-RitsuLib.dll");
        if (File.Exists(localCopy))
        {
            Assembly sts2Assembly = typeof(INetGameService).Assembly;
            string assemblyDirectory = Path.GetDirectoryName(localCopy)!;
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                AssemblyName requested = new(args.Name);
                if (string.Equals(requested.Name, sts2Assembly.GetName().Name, StringComparison.Ordinal))
                {
                    return sts2Assembly;
                }

                Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly =>
                        string.Equals(assembly.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase));
                if (loaded != null)
                {
                    return loaded;
                }

                string dependencyPath = Path.Combine(assemblyDirectory, requested.Name + ".dll");
                return File.Exists(dependencyPath)
                    ? Assembly.LoadFrom(dependencyPath)
                    : null;
            };
            return Assembly.Load(File.ReadAllBytes(localCopy));
        }

        throw new FileNotFoundException("Official STS2-RitsuLib v0.5.12 assembly was not found.", localCopy);
    }

    private static List<StartRunLobbyPlayer> StartRunPlayers(int count)
    {
        return StartRunPlayers(Enumerable.Range(0, count));
    }

    private static List<StartRunLobbyPlayer> StartRunPlayers(IEnumerable<int> realSlots)
    {
        CharacterModel character = ModelDb.Character<Ironclad>();
        return realSlots.Select(slot => new StartRunLobbyPlayer
        {
            id = 100UL + (ulong)slot,
            slotId = slot,
            character = character,
            unlockState = new SerializableUnlockState(),
            maxMultiplayerAscensionUnlocked = 0,
            isReady = true
        }).ToList();
    }

    private static SerializableRun RunWithPlayers(int count)
    {
        ModelId characterId = ModelDb.Character<Ironclad>().Id;
        return new SerializableRun
        {
            Players = Enumerable.Range(0, count).Select(slot => new SerializablePlayer
            {
                NetId = 100UL + (ulong)slot,
                CharacterId = characterId,
                Rng = new SerializablePlayerRngSet(),
                Odds = new SerializablePlayerOddsSet(),
                RelicGrabBag = new SerializableRelicGrabBag(),
                ExtraFields = new SerializableExtraPlayerFields(),
                UnlockState = new SerializableUnlockState()
            }).ToList()
        };
    }

    private static PeerVersionInfo VersionInfo() => new()
    {
        version = "test",
        gameplayAffectingMods = [],
        otherMods = []
    };

    private static void InitializeSts2Serialization()
    {
        lock (InitializationSync)
        {
            if (_initialized)
            {
                return;
            }

            AssemblyInfo.Init();
            typeof(MessageTypes).GetField("_cache", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, new NetTypeCache<INetMessage>(INetMessageSubtypes.All.ToList()));
            if (!ModelDb.All.Any())
            {
                ModelDb.Init([typeof(Ironclad)]);
            }
            try
            {
                _ = ModelIdSerializationCache.GetNetIdForCategory(ModelId.none.Category);
            }
            catch (InvalidOperationException)
            {
                ModelIdSerializationCache.Init();
            }
            ModelDb.InitIds();
            _initialized = true;
        }
    }

    private sealed class RuntimePair : IDisposable
    {
        internal const ulong DefaultHostId = 1;
        internal const ulong DefaultClientId = 22;

        internal RuntimePair(bool ritsu = false, bool bindRitsuFlows = true)
        {
            HostTransport = new TestNetHost(Host, DefaultHostId);
            typeof(NetHostGameService).GetField("_netHost", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Host, HostTransport);
            ClientTransport = new TestNetClient(Client, DefaultClientId, DefaultHostId);
            Client.Initialize(ClientTransport, default);
            Offer = ritsu
                ? new LanConnectProtocolOffer(1, 1, "0.6.0-alpha.1", true, true)
                : new LanConnectProtocolOffer(1, 1, "0.6.0-alpha.1", false, false);
            Selection = CreateSelection(ritsu);
            Runtime.BindHost(Host, Offer, Selection);
            Runtime.BindClient(Client, Offer, Selection, ProtocolFlowNonce);
            if (ritsu && bindRitsuFlows)
            {
                Runtime.BindHostTrustedSidecarFlow(Host, ClientId, ProtocolFlowNonce);
                Runtime.BindClientHostSidecarFlow(Client);
            }
        }

        internal LanConnectTailMessageRuntime Runtime { get; } = new();
        internal NetHostGameService Host { get; } = new(PeerVersionInfo.LocalDefault());
        internal NetClientGameService Client { get; } = new(PeerVersionInfo.LocalDefault());
        internal TestNetHost HostTransport { get; }
        internal TestNetClient ClientTransport { get; }
        internal ulong HostId => DefaultHostId;
        internal ulong ClientId => DefaultClientId;
        internal byte[] ProtocolFlowNonce { get; } =
            Enumerable.Range(1, LanConnectSidecarFrameCodec.FlowNonceBytes).Select(static value => (byte)value).ToArray();
        internal LanConnectProtocolOffer Offer { get; }
        internal LanConnectProtocolSelection Selection { get; }
        internal NetMessageBus HostBus => GetBus(Host);
        internal NetMessageBus ClientBus => GetBus(Client);

        public void Dispose()
        {
            Runtime.Unbind(Host);
            Runtime.Unbind(Client);
        }

        private static NetMessageBus GetBus(INetGameService service)
        {
            FieldInfo field = service.GetType().GetField(
                "_messageBus",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (NetMessageBus)field.GetValue(service)!;
        }
    }

    private static LanConnectProtocolSelection CreateSelection(bool ritsu = false)
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            1,
            ritsu ? LanConnectProtocolCarrier.RitsuLibSidecarV1 : LanConnectProtocolCarrier.StandaloneTailV1,
            "0.6.0-alpha.1",
            8,
            "0.110.1",
            "aabb",
            ritsu,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }

    private sealed record CapturedPacket(ulong PeerId, byte[] Bytes);

    private sealed class TestNetHost(INetHostHandler handler, ulong netId) : NetHost(handler)
    {
        internal List<CapturedPacket> SentToClients { get; } = [];
        internal List<ulong> DisconnectedPeers { get; } = [];
        internal ulong? ThrowForPeerId { get; set; }
        public override IEnumerable<ulong> ConnectedPeerIds => [RuntimePair.DefaultClientId];
        public override bool IsConnected => true;
        public override ulong NetId { get; } = netId;
        public override void Update() { }
        public override void SetHostIsClosed(bool isClosed) { }
        public override void SendMessageToClient(
            ulong peerId,
            byte[] bytes,
            int length,
            NetTransferMode mode,
            int channel = 0)
        {
            if (peerId == ThrowForPeerId)
            {
                throw new IOException("Injected sidecar send failure.");
            }

            SentToClients.Add(new CapturedPacket(peerId, bytes.AsSpan(0, length).ToArray()));
        }
        public override void SendMessageToAll(
            byte[] bytes,
            int length,
            NetTransferMode mode,
            int channel = 0) { }
        public override void DisconnectClient(ulong peerId, NetError reason, bool now = false) =>
            DisconnectedPeers.Add(peerId);
        public override void StopHost(NetError reason, bool now = false) { }
        public override string? GetRawLobbyIdentifier() => null;
    }

    private sealed class TestNetClient(
        INetClientHandler handler,
        ulong netId,
        ulong hostNetId) : NetClient(handler)
    {
        internal List<byte[]> SentToHost { get; } = [];
        public override bool IsConnected { get; } = true;
        public override ulong NetId { get; } = netId;
        public override ulong HostNetId { get; } = hostNetId;
        public override void Update() { }
        public override void SendMessageToHost(
            byte[] bytes,
            int length,
            NetTransferMode mode,
            int channel = 0) =>
            SentToHost.Add(bytes.AsSpan(0, length).ToArray());
        public override void DisconnectFromHost(NetError reason, bool now = false) { }
        public override string? GetRawLobbyIdentifier() => null;
    }
}
