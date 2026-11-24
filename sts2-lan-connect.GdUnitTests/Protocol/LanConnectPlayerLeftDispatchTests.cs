using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GdUnit4;
using HarmonyLib;
using static GdUnit4.Assertions;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectPlayerLeftDispatchTests
{
    private const ulong HostId = 100;
    private const ulong ClientId = 22;
    private const ulong DepartedId = 33;

    [TestCase]
    public void Installed_native_dispatch_hook_observes_host_leave_with_actual_ENet_receive_mode()
    {
        using DispatchFixture fixture = new();
        LanConnectRosterSnapshot initial = fixture.Bootstrap();

        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });

        AssertThat(fixture.Delivered).IsEqual(1);
        AssertThat(fixture.ClientTransport.DisconnectReasons).IsEmpty();
        fixture.Authority.Accept(HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition);
        AssertThat(fixture.Authority.Current!.Players.Count).IsEqual(2);
    }

    [TestCase]
    public void Native_buffer_replay_retains_transport_context_captured_before_bootstrap()
    {
        using DispatchFixture fixture = new();
        fixture.Bus.SetBufferMessages(true);
        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });
        AssertThat(fixture.Delivered).IsEqual(0);
        LanConnectRosterSnapshot initial = fixture.Bootstrap();

        // There is deliberately no receive-thread context during native replay.
        fixture.Bus.SetBufferMessages(false);

        AssertThat(fixture.Delivered).IsEqual(1);
        fixture.Authority.Accept(HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition);
    }

    [TestCase(999UL, HostId, 0, DepartedId)]
    [TestCase(HostId, 999UL, 0, DepartedId)]
    [TestCase(HostId, HostId, 1, DepartedId)]
    [TestCase(HostId, HostId, 0, HostId)]
    public void Untrusted_native_leave_is_suppressed_and_cannot_authorize_removal(
        ulong transportSender, ulong headerSender, int channel, ulong departed)
    {
        using DispatchFixture fixture = new();
        LanConnectRosterSnapshot initial = fixture.Bootstrap();

        fixture.Deliver(new PlayerLeftMessage { playerId = departed }, transportSender, headerSender, channel);

        AssertThat(fixture.Delivered).IsEqual(0);
        AssertInvalidData(() => fixture.Authority.Accept(
            HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition));
    }

    [TestCase]
    public void Unknown_and_duplicate_native_leaves_do_not_authorize_additional_removals()
    {
        using DispatchFixture fixture = new();
        LanConnectRosterSnapshot initial = fixture.Bootstrap();
        fixture.Deliver(new PlayerLeftMessage { playerId = 999 });
        AssertInvalidData(() => fixture.Authority.Accept(
            HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition));
        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });
        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });
        fixture.Authority.Accept(HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition);

        AssertInvalidData(() => fixture.Authority.Accept(HostId, initial with
        {
            RosterRevision = 3,
            Players = [initial.Players[0], initial.Players[2]]
        }, LanConnectRosterSnapshotUse.StateTransition));
    }

    [TestCase]
    public void Running_or_loaded_lobby_leave_reaches_native_handler_without_changing_roster_authority()
    {
        using DispatchFixture fixture = new();
        LanConnectRosterSnapshot initial = fixture.Bootstrap();
        fixture.Binding.EndStartLobby();

        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });

        AssertThat(fixture.Delivered).IsEqual(1);
        AssertInvalidData(() => fixture.Authority.Accept(
            HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Invalid_projection_or_carrier_does_not_consume_verified_departure(bool invalidCarrier)
    {
        using DispatchFixture fixture = new();
        LanConnectRosterSnapshot initial = fixture.Bootstrap();
        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });
        LanConnectRosterSnapshot next = Reduced(initial);
        TestPlayer[] projection = Players()[..2];
        LanConnectRosterSnapshot invalid = next;
        if (invalidCarrier)
        {
            LanConnectRosterPlayerCarrier first = next.Players[0];
            invalid = next with
            {
                Players = [new(first.PlayerId, first.RealSlotId,
                    first.VanillaPlayerBitLength + 1, first.VanillaPlayerBytes.Span), next.Players[1]]
            };
        }
        else
        {
            projection[0].id = 999;
        }

        AssertInvalidData(() => LanConnectTailMessageRuntime.RestoreAndAcceptStartPlayers(
            invalid, projection, fixture.Authority, LanConnectRosterSnapshotUse.StateTransition));
        AssertThat(fixture.Authority.Current!.RosterRevision).IsEqual(1u);
        AssertThat(fixture.Binding.IsStartLobbyActive).IsTrue();

        List<TestPlayer> restored = LanConnectTailMessageRuntime.RestoreAndAcceptStartPlayers(
            next, Players()[..2], fixture.Authority, LanConnectRosterSnapshotUse.StateTransition);
        AssertThat(restored.Select(player => player.id).ToArray()).IsEqual(new[] { HostId, ClientId });
    }

    [TestCase]
    public void Failed_load_response_keeps_start_phase_and_departure_evidence()
    {
        using DispatchFixture fixture = new();
        LanConnectRosterSnapshot initial = fixture.Bootstrap();
        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });
        ClientLoadJoinResponseMessage message = LoadMessage([ClientId, HostId, DepartedId]);
        LanConnectPreparedTailMessage prepared = LanConnectTailMessageRuntime.PrepareLoadJoin(
            message, fixture.Selection, new(HostId));
        ClientLoadJoinResponseMessage malformed = (ClientLoadJoinResponseMessage)prepared.Message;
        malformed.playersAlreadyConnected = malformed.playersAlreadyConnected.AsEnumerable().Reverse().ToList();

        AssertInvalidData(() => fixture.Runtime.ValidateIncoming(
            fixture.Bus, LanConnectSidecarMessageKind.LoadJoinResponse, HostId,
            malformed, prepared.Container, fixture.Selection));

        AssertThat(fixture.Binding.IsStartLobbyActive).IsTrue();
        fixture.Authority.Accept(HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition);
    }

    [TestCase]
    public void Successful_real_load_response_closes_start_phase_and_clears_departure_evidence()
    {
        using DispatchFixture fixture = new();
        ClientLoadJoinResponseMessage message = LoadMessage([DepartedId, HostId, ClientId]);
        LanConnectPreparedTailMessage prepared = LanConnectTailMessageRuntime.PrepareLoadJoin(
            message, fixture.Selection, new(HostId));
        fixture.Runtime.ValidateIncoming(fixture.Bus, LanConnectSidecarMessageKind.LoadJoinResponse,
            HostId, (INetMessage)prepared.Message, prepared.Container, fixture.Selection);
        LanConnectRosterSnapshot initial = fixture.Authority.Current!;
        fixture.Binding.BeginStartLobby();
        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });

        fixture.Runtime.ValidateIncoming(fixture.Bus, LanConnectSidecarMessageKind.LoadJoinResponse,
            HostId, (INetMessage)prepared.Message, prepared.Container, fixture.Selection);

        AssertThat(fixture.Binding.IsStartLobbyActive).IsFalse();
        AssertThat(fixture.Authority.Current).IsEqual(initial);
        AssertInvalidData(() => fixture.Authority.Accept(
            HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition));
        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });
        AssertInvalidData(() => fixture.Authority.Accept(
            HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition));
    }

    [TestCase]
    public void Dedicated_host_observer_marks_only_active_lobby_departure_and_generic_cleanup_does_not()
    {
        using DispatchFixture fixture = new(isHost: true);
        LanConnectRosterSnapshot initial = fixture.Bootstrap();
        fixture.Runtime.ClearNativePeer(fixture.Host!, DepartedId);
        AssertThat(fixture.Authority.CommitHostSnapshot(initial.Players).RosterRevision).IsEqual(1u);

        fixture.Runtime.ObserveHostLobbyDeparture(fixture.Host!, DepartedId);
        AssertThat(fixture.Authority.CommitHostSnapshot(initial.Players).RosterRevision).IsEqual(2u);
        fixture.Binding.EndStartLobby();
        fixture.Runtime.ObserveHostLobbyDeparture(fixture.Host!, DepartedId);
        AssertThat(fixture.Authority.CommitHostSnapshot(initial.Players).RosterRevision).IsEqual(2u);
        fixture.Binding.MarkTerminated();
        fixture.Binding.BeginStartLobby();
        AssertThat(fixture.Binding.IsStartLobbyActive).IsFalse();
    }

    [TestCase]
    public void Binding_termination_clears_pending_departure_proof()
    {
        using DispatchFixture fixture = new();
        LanConnectRosterSnapshot initial = fixture.Bootstrap();
        fixture.Deliver(new PlayerLeftMessage { playerId = DepartedId });

        fixture.Runtime.Unbind(fixture.Client!);

        AssertThat(fixture.Binding.IsStartLobbyActive).IsFalse();
        AssertInvalidData(() => fixture.Authority.Accept(
            HostId, Reduced(initial), LanConnectRosterSnapshotUse.StateTransition));
    }

    private static void AssertInvalidData(Action action)
    {
        bool rejected = false;
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        AssertThat(rejected).IsTrue();
    }

    private static LanConnectRosterSnapshot Reduced(LanConnectRosterSnapshot initial) =>
        initial with { RosterRevision = initial.RosterRevision + 1, Players = initial.Players.Take(2).ToArray() };

    private static TestPlayer[] Players() =>
    [
        new() { id = HostId, slotId = 0 }, new() { id = ClientId, slotId = 1 },
        new() { id = DepartedId, slotId = 2 }
    ];

    private static LanConnectRosterPlayerCarrier Carrier(TestPlayer player)
    {
        PacketWriter writer = new() { WarnOnGrow = false };
        player.Serialize(writer);
        return new(player.id, (byte)player.slotId, (uint)writer.BitPosition,
            writer.Buffer.AsSpan(0, (writer.BitPosition + 7) / 8));
    }

    private static ClientLoadJoinResponseMessage LoadMessage(ulong[] order)
    {
        SerializableRun run = (SerializableRun)RuntimeHelpers.GetUninitializedObject(typeof(SerializableRun));
        run.Players = new[] { HostId, ClientId, DepartedId }.Select(id =>
        {
            SerializablePlayer player = (SerializablePlayer)RuntimeHelpers.GetUninitializedObject(typeof(SerializablePlayer));
            FieldInfo? field = typeof(SerializablePlayer).GetField(nameof(SerializablePlayer.NetId));
            if (field != null)
            {
                field.SetValue(player, id);
            }
            else
            {
                typeof(SerializablePlayer).GetProperty(nameof(SerializablePlayer.NetId))!.SetValue(player, id);
            }
            return player;
        }).ToList();
        return new() { serializableRun = run, playersAlreadyConnected = order.Select(id => new LoadRunLobbyPlayer { id = id }).ToList() };
    }

    public struct TestPlayer : IPacketSerializable
    {
        public ulong id;
        public int slotId;
        public bool isReady;
        public void Serialize(PacketWriter writer)
        {
            writer.WriteULong(id);
            writer.WriteInt(slotId, 2);
            writer.WriteBool(isReady);
        }
        public void Deserialize(PacketReader reader)
        {
            id = reader.ReadULong();
            slotId = reader.ReadInt(2);
            isReady = reader.ReadBool();
        }
    }

    private sealed class DispatchFixture : IDisposable
    {
        private readonly Harmony _harmony = new("sts2_lan_connect.tests.player-left." + Guid.NewGuid().ToString("N"));
        private readonly LanConnectTailPatchStep _dispatch;
        private readonly IDisposable _session;
        internal DispatchFixture(bool isHost = false)
        {
            LanConnectProtocolSelection selection = new(LanConnectProtocolProfile.TailV1, 1,
                LanConnectProtocolCarrier.NativeBusV1, "0.6.2-alpha.1", 8, "0.113.1", "aabb", false, string.Empty);
            Selection = selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
            PeerVersionInfo version = (PeerVersionInfo)RuntimeHelpers.GetUninitializedObject(typeof(PeerVersionInfo));
            INetGameService service;
            if (isHost)
            {
                Host = new(version);
                typeof(NetHostGameService).GetField("_netHost", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(Host, new TestNetHost(Host));
                service = Host;
            }
            else
            {
                Client = new(version);
                ClientTransport = new(Client);
                Client.Initialize(ClientTransport, default);
                typeof(NetClientGameService).GetField("<IsConnected>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(Client, true);
                service = Client;
            }

            // Isolate the unrelated ModelDb/type-registry startup check; actual
            // binding, native bus, dispatch hook, sender and session checks run.
            Binding = new(Runtime, service, new(1, 1, "0.6.2-alpha.1", false, false), Selection,
                isHost, new byte[LanConnectSidecarFrameCodec.FlowNonceBytes], 200);
            Bus = Binding.MessageBus;
            ((IDictionary)typeof(LanConnectTailMessageRuntime).GetField("_bindings", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(Runtime)!).Add(Bus, Binding);
            ((IDictionary)typeof(LanConnectTailMessageRuntime).GetField("_writerBindings", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(Runtime)!).Add(Binding.Writer, Binding);
            Bus.RegisterMessageHandler<PlayerLeftMessage>((_, _) => Delivered++);
            _session = isHost
                ? LanConnectSessionProtocolState.Shared.FreezeHost(Selection, "left-tests-" + Guid.NewGuid().ToString("N"))
                : LanConnectSessionProtocolState.Shared.FreezeClient(Selection, "left-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                LanConnectTailMessagePatches.ConfigureRuntime(Runtime);
                _dispatch = LanConnectTailMessagePatches.ResolvePatchPlan(typeof(PacketWriter).Assembly).Steps
                    .Single(step => step.Category == "dispatch_barrier");
                _harmony.Patch(_dispatch.Target, prefix: new HarmonyMethod(_dispatch.Prefix!));
                AssertThat(Harmony.GetPatchInfo(_dispatch.Target)!.Prefixes.Any(patch => patch.owner == _harmony.Id)).IsTrue();
            }
            catch
            {
                _harmony.UnpatchAll(_harmony.Id);
                LanConnectTailMessagePatches.ResetRuntime();
                _session.Dispose();
                Runtime.Unbind(service);
                throw;
            }
        }

        internal LanConnectTailMessageRuntime Runtime { get; } = new();
        internal LanConnectTailMessageRuntime.Binding Binding { get; }
        internal LanConnectRosterAuthorityState Authority => Binding.RequireRoster();
        internal LanConnectProtocolSelection Selection { get; }
        internal NetMessageBus Bus { get; }
        internal NetClientGameService? Client { get; }
        internal NetHostGameService? Host { get; }
        internal TestNetClient ClientTransport { get; } = null!;
        internal int Delivered { get; private set; }

        internal LanConnectRosterSnapshot Bootstrap()
        {
            LanConnectRosterSnapshot snapshot = new(HostId, 1, Players().Select(Carrier).ToArray());
            Authority.Accept(HostId, snapshot, LanConnectRosterSnapshotUse.Bootstrap);
            Binding.BeginStartLobby();
            return snapshot;
        }

        internal void Deliver(PlayerLeftMessage message, ulong transportSender = HostId, ulong headerSender = HostId, int channel = 0)
        {
            using IDisposable context = LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(
                transportSender, channel, NetTransferMode.None);
            _dispatch.Target.Invoke(Bus, [message, headerSender]);
        }

        public void Dispose()
        {
            _harmony.Unpatch(_dispatch.Target, HarmonyPatchType.Prefix, _harmony.Id);
            LanConnectTailMessagePatches.ResetRuntime();
            _session.Dispose();
            Runtime.Unbind((INetGameService?)Client ?? Host!);
        }
    }

    private sealed class TestNetHost(INetHostHandler handler) : ENetHost(handler)
    {
        public override IEnumerable<ulong> ConnectedPeerIds => [ClientId, DepartedId];
        public override bool IsConnected => true;
        public override ulong NetId => HostId;
        public override void Update() { }
        public override void SetHostIsClosed(bool isClosed) { }
        public override void SendMessageToClient(ulong peerId, byte[] bytes, int length, NetTransferMode mode, int channel = 0) { }
        public override void SendMessageToAll(byte[] bytes, int length, NetTransferMode mode, int channel = 0) { }
        public override void StopHost(NetError reason, bool now = false) { }
        public override string? GetRawLobbyIdentifier() => null;
    }

    private sealed class TestNetClient(INetClientHandler handler) : ENetClient(handler)
    {
        internal List<NetError> DisconnectReasons { get; } = [];
        public override bool IsConnected => true;
        public override ulong NetId => ClientId;
        public override ulong HostNetId => HostId;
        public override void Update() { }
        public override void SendMessageToHost(byte[] bytes, int length, NetTransferMode mode, int channel = 0) { }
        public override void DisconnectFromHost(NetError reason, bool now = false) => DisconnectReasons.Add(reason);
        public override string? GetRawLobbyIdentifier() => null;
    }
}
