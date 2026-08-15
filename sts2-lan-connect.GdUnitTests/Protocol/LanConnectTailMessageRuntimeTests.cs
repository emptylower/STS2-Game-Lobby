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

        internal RuntimePair()
        {
            HostTransport = new TestNetHost(Host, DefaultHostId);
            typeof(NetHostGameService).GetField("_netHost", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Host, HostTransport);
            ClientTransport = new TestNetClient(Client, DefaultClientId, DefaultHostId);
            Client.Initialize(ClientTransport, default);
            Runtime.BindHost(Host, Offer, Selection);
            Runtime.BindClient(Client, Offer, Selection, new byte[16]);
        }

        internal LanConnectTailMessageRuntime Runtime { get; } = new();
        internal NetHostGameService Host { get; } = new(PeerVersionInfo.LocalDefault());
        internal NetClientGameService Client { get; } = new(PeerVersionInfo.LocalDefault());
        internal TestNetHost HostTransport { get; }
        internal TestNetClient ClientTransport { get; }
        internal ulong HostId => DefaultHostId;
        internal ulong ClientId => DefaultClientId;
        internal LanConnectProtocolOffer Offer { get; } = new(1, 1, "0.6.0-alpha.1", false, false);
        internal LanConnectProtocolSelection Selection { get; } = CreateSelection();
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

    private static LanConnectProtocolSelection CreateSelection()
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            1,
            LanConnectProtocolCarrier.StandaloneTailV1,
            "0.6.0-alpha.1",
            8,
            "0.110.1",
            "aabb",
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }

    private sealed record CapturedPacket(ulong PeerId, byte[] Bytes);

    private sealed class TestNetHost(INetHostHandler handler, ulong netId) : NetHost(handler)
    {
        internal List<CapturedPacket> SentToClients { get; } = [];
        internal List<ulong> DisconnectedPeers { get; } = [];
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
            int channel = 0) =>
            SentToClients.Add(new CapturedPacket(peerId, bytes.AsSpan(0, length).ToArray()));
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
