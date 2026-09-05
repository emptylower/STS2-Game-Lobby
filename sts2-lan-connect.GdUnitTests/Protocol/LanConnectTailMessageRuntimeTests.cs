using System.Reflection;
using GdUnit4;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

/// <summary>
/// native_bus_v1 生产链与配对屏障测试（spec §6.1）。
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectTailMessageRuntimeTests
{
    private static readonly object InitializationSync = new();
    private static bool _initialized;
    private const uint TestNativeTypeId = 200;

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
        pair.Runtime.ValidateIncoming(
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
        pair.Runtime.ValidateIncoming(
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
        pair.Runtime.ValidateIncoming(
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
        pair.Runtime.ValidateIncoming(
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
        pair.Runtime.ValidateIncoming(
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
        pair.Runtime.ValidateIncoming(
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
        pair.Runtime.ValidateIncoming(
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
        pair.Runtime.ValidateIncoming(
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
        pair.Runtime.ValidateIncoming(
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

    // ---- native_bus_v1 生产链（spec §3.2 / §6.1） ----

    [TestCase]
    public void Host_broadcast_serializes_once_and_sends_a_paired_extension_to_each_peer()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        const ulong secondPeerId = 23;
        byte[] secondNonce = Enumerable.Repeat((byte)0x22, LanConnectSidecarFrameCodec.FlowNonceBytes).ToArray();
        pair.PrepareHostNativeFlow(secondPeerId, secondNonce);
        pair.HostTransport.AddConnectedPeer(secondPeerId);

        // 第一级 prefix + 原版序列化 + postfix（一次序列化，模拟宿主广播的序列化阶段）。
        pair.BootstrapHostRoster();
        PendingVanilla pending = pair.SerializeHostMatrixMessage(
            LanConnectSidecarMessageKind.PlayerJoined,
            new PlayerJoinedMessage { lobbyPlayer = StartRunPlayers([0]).Single() });

        // 广播循环：同一 pending 服务每个 peer 各一次。
        pair.DeliverPendingToPeer(pending, pair.ClientId);
        pair.DeliverPendingToPeer(pending, secondPeerId);

        CapturedPacket[] extensions = pair.HostTransport.SentToClients.ToArray();
        AssertThat(extensions.Length).IsEqual(2);
        foreach (CapturedPacket extension in extensions)
        {
            AssertThat(extension.Bytes[0]).IsEqual((byte)TestNativeTypeId);
        }

        AssertThat(extensions[0].PeerId).IsEqual(pair.ClientId);
        AssertThat(extensions[1].PeerId).IsEqual(secondPeerId);
        // 每个 peer 独立 flow：首条序号均为 1，nonce 各归各。
        AssertThat(FrameSequenceOf(extensions[0])).IsEqual(1u);
        AssertThat(FrameSequenceOf(extensions[1])).IsEqual(1u);
        AssertThat(FrameNonceOf(extensions[0])).IsEqual(pair.ProtocolFlowNonce);
        AssertThat(FrameNonceOf(extensions[1])).IsEqual(secondNonce);
    }

    [TestCase]
    public void Third_party_send_prefix_that_copies_and_extends_the_buffer_still_emits_the_extension_frame()
    {
        // 2026-09-05 本机双实例复现：RitsuLib 0.5.18 的 NativeTrailer 前缀挂在同一个
        // ENetHost.SendMessageToClient 上，会把 bytes 换成加长 36 字节的新数组。只按数组引用
        // + 精确长度匹配 pending 会静默失配，扩展帧永远不发，加入方扣住 InitialGameInfo 直到
        // 房主 10 秒 LobbyJoinTimeout。
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        pair.BootstrapHostRoster();
        PendingVanilla pending = pair.SerializeHostMatrixMessage(
            LanConnectSidecarMessageKind.PlayerJoined,
            new PlayerJoinedMessage { lobbyPlayer = StartRunPlayers([0]).Single() });

        byte[] extended = new byte[pending.Length + 36];
        pending.Buffer.AsSpan(0, pending.Length).CopyTo(extended);
        LanConnectNativeSendContext? context = pair.Runtime.BeginNativeTransport(
            pair.HostTransport,
            isHostTransport: true,
            pair.ClientId,
            extended,
            extended.Length);
        AssertThat(context != null).IsTrue();
        pair.Runtime.CompleteNativeTransport(context, vanillaPeerReachable: true);

        CapturedPacket[] extensions = pair.HostTransport.SentToClients.ToArray();
        AssertThat(extensions.Length).IsEqual(1);
        AssertThat(extensions[0].Bytes[0]).IsEqual((byte)TestNativeTypeId);
    }

    [TestCase]
    public void Writer_reset_clears_stale_pending_before_the_next_non_matrix_send()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        pair.BootstrapHostRoster();
        PendingVanilla pending = pair.SerializeHostMatrixMessage(
            LanConnectSidecarMessageKind.PlayerJoined,
            new PlayerJoinedMessage { lobbyPlayer = StartRunPlayers([0]).Single() });
        pair.DeliverPendingToPeer(pending, pair.ClientId);

        // PacketWriter.Reset prefix 职责：清除 writer 的 pending。
        pair.Runtime.ClearPendingOutgoing(pair.HostWriter);
        pair.HostWriter.Reset();

        LanConnectNativeSendContext? stale = pair.Runtime.BeginNativeTransport(
            pair.HostTransport,
            isHostTransport: true,
            pair.ClientId,
            pending.Buffer,
            pending.Length);
        AssertThat(stale == null).IsTrue();
    }

    [TestCase]
    public void Duplicate_peer_consumption_of_the_same_pending_terminates_the_binding()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        pair.BootstrapHostRoster();
        PendingVanilla pending = pair.SerializeHostMatrixMessage(
            LanConnectSidecarMessageKind.PlayerJoined,
            new PlayerJoinedMessage { lobbyPlayer = StartRunPlayers([0]).Single() });
        pair.DeliverPendingToPeer(pending, pair.ClientId);

        AssertThrown(() => pair.DeliverPendingToPeer(pending, pair.ClientId));
        AssertThat(pair.HostTransport.DisconnectedPeers.Contains(pair.ClientId)).IsTrue();
    }

    [TestCase]
    public void Native_send_reentry_is_structurally_ignored()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        pair.BootstrapHostRoster();
        PendingVanilla pending = pair.SerializeHostMatrixMessage(
            LanConnectSidecarMessageKind.PlayerJoined,
            new PlayerJoinedMessage { lobbyPlayer = StartRunPlayers([0]).Single() });

        // 模拟第三方在我们扩展帧的 transport 调用内再次触发发送：必须被深度守卫忽略。
        List<LanConnectNativeSendContext?> probes = [];
        pair.HostTransport.SendProbe = (peerId, bytes, length) =>
        {
            probes.Add(pair.Runtime.BeginNativeTransport(
                pair.HostTransport,
                isHostTransport: true,
                peerId,
                bytes,
                length));
            return null;
        };

        pair.DeliverPendingToPeer(pending, pair.ClientId);

        AssertThat(probes.Count).IsEqual(1);
        AssertThat(probes[0] == null).IsTrue();
        AssertThat(pair.HostTransport.SentToClients.Count).IsEqual(1);
    }

    [TestCase]
    public void Host_initial_game_info_extension_defers_until_control_binding_and_flushes()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new(bindFlows: false);
        using IDisposable session = pair.FreezeHostSession();
        PendingVanilla pending = pair.SerializeHostMatrixMessage(
            LanConnectSidecarMessageKind.InitialGameInfo,
            new InitialGameInfoMessage());

        // flow 未绑定：扩展帧延迟（原版照发，不静默丢帧语义在激活时兑现）。
        pair.DeliverPendingToPeer(pending, pair.ClientId);
        AssertThat(pair.HostTransport.SentToClients.Count).IsEqual(0);

        pair.Runtime.PrepareHostNativeFlow(pair.Host, pair.ClientId, pair.ProtocolFlowNonce);
        AssertThat(pair.HostTransport.SentToClients.Count).IsEqual(0);

        pair.Runtime.ActivateHostNativeFlow(pair.Host, pair.ClientId);
        AssertThat(pair.HostTransport.SentToClients.Count).IsEqual(1);
        AssertThat(pair.HostTransport.SentToClients[0].PeerId).IsEqual(pair.ClientId);
    }

    [TestCase]
    public void Client_first_send_binds_its_ticket_flow_and_reaches_the_host()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new(bindClientFlow: false);
        using IDisposable session = pair.FreezeClientSession();
        PendingVanilla pending = pair.SerializeClientMatrixMessage(
            LanConnectSidecarMessageKind.LobbyJoinRequest,
            new ClientLobbyJoinRequestMessage { unlockState = new SerializableUnlockState() });

        pair.DeliverPendingToHost(pending);

        AssertThat(pair.ClientTransport.SentToHost.Count).IsEqual(1);
        byte[] wire = pair.ClientTransport.SentToHost[0];
        AssertThat(wire[0]).IsEqual((byte)TestNativeTypeId);
        AssertThat(FrameSequenceOf(new CapturedPacket(0, wire))).IsEqual(1u);
    }

    [TestCase]
    public void Android_order_prepare_after_the_vanilla_header_still_produces_the_extension()
    {
        // 安卓与桌面回退路径的 seam：prefix 在 T.Serialize 边界触发（header 已写入）。
        // prepare 不再校验 header 边界后，该顺序必须与桌面顺序同样产出扩展帧。
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        pair.BootstrapHostRoster();
        PendingVanilla pending = pair.SerializeHostMatrixMessageAndroidOrder(
            LanConnectSidecarMessageKind.PlayerJoined,
            new PlayerJoinedMessage { lobbyPlayer = StartRunPlayers([0]).Single() });

        pair.DeliverPendingToPeer(pending, pair.ClientId);

        AssertThat(pair.HostTransport.SentToClients.Count).IsEqual(1);
        AssertThat(pair.HostTransport.SentToClients[0].Bytes[0]).IsEqual((byte)TestNativeTypeId);
        AssertThat(pair.HostTransport.SentToClients[0].PeerId).IsEqual(pair.ClientId);
    }

    // ---- 配对屏障（spec §3.3 / §6.1） ----

    [TestCase]
    public void Barrier_holds_matrix_until_extension_then_restores_and_dispatches_once()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeClientSession();
        List<(ClientLobbyJoinResponseMessage Message, ulong Sender)> dispatched = [];
        pair.ClientBus.RegisterMessageHandler<ClientLobbyJoinResponseMessage>(
            (message, senderId) => dispatched.Add((message, senderId)));

        int playerCount = 5;
        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            new ClientLobbyJoinResponseMessage { playersInLobby = StartRunPlayers(playerCount), modifiers = [] },
            pair.Selection);
        INetMessage projected = (INetMessage)prepared.Message;

        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
        {
            // overrideSenderId 伪造：配对身份只认传输层 sender。
            bool first = pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, projected, senderId: 999);
            AssertThat(first).IsFalse();
        }

        AssertThat(dispatched.Count).IsEqual(0);

        LanConnectNativeBusMessage extension = BuildExtension(
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.ProtocolFlowNonce,
            sequence: 1,
            prepared.Container);
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
        {
            bool second = pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, extension, pair.HostId);
            AssertThat(second).IsFalse();
        }

        AssertThat(dispatched.Count).IsEqual(1);
        AssertThat(dispatched[0].Sender).IsEqual(pair.HostId);
        AssertThat(dispatched[0].Message.playersInLobby!.Count).IsEqual(playerCount);
        AssertThat(dispatched[0].Message.playersInLobby.Select(static player => player.slotId).ToArray())
            .IsEqual(Enumerable.Range(0, playerCount).ToArray());
    }

    [TestCase]
    public void Barrier_shards_by_transport_sender_so_other_peers_may_proceed()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        List<ulong> dispatched = [];
        pair.HostBus.RegisterMessageHandler<ClientLobbyJoinRequestMessage>(
            (_, senderId) => dispatched.Add(senderId));

        const ulong peerA = 31;
        const ulong peerB = 32;
        ClientLobbyJoinRequestMessage messageA = new();
        ClientLobbyJoinRequestMessage messageB = new();

        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(peerA, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.HostBus, messageA, peerA)).IsFalse();
        }

        // peer B 的消息不被 peer A 的 hold 拦截（原版跨 peer 本就无序）。
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(peerB, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.HostBus, messageB, peerB)).IsFalse();
        }

        // 非 (sender, channel) 匹配的第二条矩阵消息即破坏背靠背不变量：立即 lan_extension_missing。
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(peerB, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.HostBus, new ClientLobbyJoinRequestMessage(), peerB))
                .IsFalse();
        }

        AssertThat(pair.HostTransport.DisconnectedPeers.Contains(peerB)).IsTrue();
        AssertThat(dispatched.Count).IsEqual(0);
    }

    [TestCase]
    public void Barrier_timeout_fires_lan_extension_missing_and_disconnects_the_peer()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        pair.HostBus.RegisterMessageHandler<ClientLobbyJoinRequestMessage>(static (_, _) => { });
        const ulong peerA = 41;
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(peerA, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(
                pair.HostBus, new ClientLobbyJoinRequestMessage(), peerA)).IsFalse();
        }

        pair.Runtime.SweepBarrierTimeouts(pair.Host, DateTimeOffset.UtcNow.AddSeconds(3));

        AssertThat(pair.HostTransport.DisconnectedPeers.Contains(peerA)).IsTrue();
    }

    [TestCase]
    public async Task Barrier_hold_times_out_on_a_timer_without_any_further_traffic()
    {
        // 2026-09-05 复现链路的另一半：扩展帧从未发出时，对端此后没有任何后续包，
        // “下一条消息到达时”的清扫永不触发。定时清扫必须自行兜底。
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        pair.HostBus.RegisterMessageHandler<ClientLobbyJoinRequestMessage>(static (_, _) => { });
        const ulong peerA = 61;
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(peerA, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(
                pair.HostBus, new ClientLobbyJoinRequestMessage(), peerA)).IsFalse();
        }

        // 不再送任何包：2 秒屏障超时 + 定时触发，以 lan_extension_missing 拒绝并断开。
        await Task.Delay(LanConnectTailMessageRuntime.BarrierHoldTimeout + TimeSpan.FromMilliseconds(500));

        AssertThat(pair.HostTransport.DisconnectedPeers.Contains(peerA)).IsTrue();
    }

    [TestCase]
    public void Extension_frame_on_nonzero_channel_is_a_structured_failure()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeClientSession();
        pair.ClientBus.RegisterMessageHandler<ClientLobbyJoinResponseMessage>(static (_, _) => { });
        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            new ClientLobbyJoinResponseMessage { playersInLobby = StartRunPlayers(2), modifiers = [] },
            pair.Selection);
        LanConnectNativeBusMessage extension = BuildExtension(
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.ProtocolFlowNonce,
            sequence: 1,
            prepared.Container);

        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 1))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, extension, pair.HostId)).IsFalse();
        }

        AssertThat(pair.ClientTransport.DisconnectReasons.Count).IsEqual(1);
    }

    [TestCase]
    public void Extension_kind_nonce_and_sequence_mismatches_are_structured_failures()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeClientSession();
        pair.ClientBus.RegisterMessageHandler<ClientLobbyJoinResponseMessage>(static (_, _) => { });
        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            new ClientLobbyJoinResponseMessage { playersInLobby = StartRunPlayers(2), modifiers = [] },
            pair.Selection);
        INetMessage projected = (INetMessage)prepared.Message;

        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, projected, pair.HostId)).IsFalse();
        }

        byte[] wrongNonce = Enumerable.Repeat((byte)0xEE, LanConnectSidecarFrameCodec.FlowNonceBytes).ToArray();
        LanConnectNativeBusMessage wrongNonceFrame = BuildExtension(
            LanConnectSidecarMessageKind.LobbyJoinResponse, wrongNonce, 1, prepared.Container);
        LanConnectNativeBusMessage wrongSequenceFrame = BuildExtension(
            LanConnectSidecarMessageKind.LobbyJoinResponse, pair.ProtocolFlowNonce, 7, prepared.Container);
        LanConnectNativeBusMessage wrongKindFrame = BuildExtension(
            LanConnectSidecarMessageKind.PlayerJoined, pair.ProtocolFlowNonce, 1, prepared.Container);

        foreach (LanConnectNativeBusMessage frame in new[] { wrongNonceFrame, wrongSequenceFrame, wrongKindFrame })
        {
            using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
            {
                AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, frame, pair.HostId)).IsFalse();
            }
        }

        AssertThat(pair.ClientTransport.DisconnectReasons.Count).IsEqual(3);
    }

    [TestCase]
    public void Buffered_release_restores_transport_context_via_the_sidetable()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeClientSession();
        List<(ClientLobbyJoinResponseMessage Message, ulong Sender)> dispatched = [];
        pair.ClientBus.RegisterMessageHandler<ClientLobbyJoinResponseMessage>(
            (message, senderId) => dispatched.Add((message, senderId)));

        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus,
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId,
            new ClientLobbyJoinResponseMessage { playersInLobby = StartRunPlayers(2), modifiers = [] },
            pair.Selection);
        INetMessage projected = (INetMessage)prepared.Message;

        // 缓冲期首次进入分发层：记录旁挂表并放行进原版缓冲。
        SetBusBuffering(pair.ClientBus, true);
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, projected, 999)).IsTrue();
        }

        // 缓冲释放：OnPacketReceived 调用栈已退出，只能靠旁挂表恢复传输上下文。
        SetBusBuffering(pair.ClientBus, false);
        AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, projected, 999)).IsFalse();

        LanConnectNativeBusMessage extension = BuildExtension(
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.ProtocolFlowNonce,
            sequence: 1,
            prepared.Container);
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, extension, pair.HostId)).IsFalse();
        }

        AssertThat(dispatched.Count).IsEqual(1);
        AssertThat(dispatched[0].Sender).IsEqual(pair.HostId);
    }

    [TestCase]
    public void Equal_valued_boxed_matrix_messages_do_not_crosslink_in_the_sidetable()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        const ulong peerA = 51;
        const ulong peerB = 52;
        // 两个内容完全相同的实例（装箱相等性会串键；旁挂表必须是引用身份）。
        // 注意各自固定一次装箱：结构体每次按接口传参都会产生新 box。
        INetMessage messageA = new ClientLobbyJoinRequestMessage();
        INetMessage messageB = new ClientLobbyJoinRequestMessage();

        SetBusBuffering(pair.HostBus, true);
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(peerA, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.HostBus, messageA, peerA)).IsTrue();
        }
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(peerB, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.HostBus, messageB, peerB)).IsTrue();
        }

        // 释放时各自恢复自己的传输 sender：A 的 hold 属于 A，B 到来的矩阵消息不会误配 A 的键。
        SetBusBuffering(pair.HostBus, false);
        AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.HostBus, messageA, peerB)).IsFalse();
        AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.HostBus, messageB, peerA)).IsFalse();

        // 两个 hold 各自独立存在：同 sender 第二条矩阵消息触发 lan_extension_missing。
        AssertThat(pair.Runtime.TryEnterNativeDispatch(
            pair.HostBus, new ClientLobbyJoinRequestMessage(), peerA)).IsFalse();
        AssertThat(pair.HostTransport.DisconnectedPeers.Contains(peerA)).IsTrue();
        AssertThat(pair.HostTransport.DisconnectedPeers.Contains(peerB)).IsFalse();
    }

    // ---- 辅助 ----

    private static void AssertThrown(Action action)
    {
        bool threw = false;
        try
        {
            action();
        }
        catch
        {
            threw = true;
        }

        AssertThat(threw).IsTrue();
    }

    private static uint FrameSequenceOf(CapturedPacket packet)
    {
        LanConnectNativeBusMessage message = DecodeExtension(packet.Bytes);
        return LanConnectSidecarFrameCodec.Decode(message.Frame!.ToArray()).MessageSequence;
    }

    private static byte[] FrameNonceOf(CapturedPacket packet)
    {
        LanConnectNativeBusMessage message = DecodeExtension(packet.Bytes);
        return LanConnectSidecarFrameCodec.Decode(message.Frame!.ToArray()).FlowNonce.ToArray();
    }

    private static LanConnectNativeBusMessage DecodeExtension(byte[] wire)
    {
        LanConnectNativeBusMessage message = new();
        message.Deserialize(new PacketReaderAdapter(wire));
        return message;
    }

    private static LanConnectNativeBusMessage BuildExtension(
        LanConnectSidecarMessageKind kind,
        byte[] flowNonce,
        uint sequence,
        byte[] container)
    {
        LanConnectSidecarFrame frame = new(kind, flowNonce, sequence, container);
        LanConnectNativeBusMessage message = new();
        message.Configure(TestNativeTypeId, LanConnectSidecarFrameCodec.Encode(frame));
        return message;
    }

    private static void SetBusBuffering(NetMessageBus bus, bool buffering) =>
        typeof(NetMessageBus)
            .GetField("_isBufferingMessages", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(bus, buffering);

    private static LanConnectRosterSnapshot DecodeRoster(
        LanConnectSidecarMessageKind kind,
        byte[] container,
        RuntimePair pair)
    {
        LanConnectTailMessagePayload payload = LanConnectTailMessagePatches.DecodeAndValidate(
            kind,
            container,
            pair.Selection,
            pair.HostId,
            pair.HostId);
        return payload.Roster ?? throw new InvalidOperationException("container carries no roster");
    }

    private static List<StartRunLobbyPlayer> StartRunPlayers(int count) =>
        StartRunPlayers(Enumerable.Range(0, count).ToArray());

    private static List<StartRunLobbyPlayer> StartRunPlayers(int[] slots)
    {
        CharacterModel character = ModelDb.Character<Ironclad>();
        return slots
            .Select(slot => new StartRunLobbyPlayer
            {
                id = 100UL + (ulong)slot,
                slotId = slot,
                character = character,
                unlockState = new SerializableUnlockState(),
                maxMultiplayerAscensionUnlocked = 20,
                isModded = false,
                isReady = true
            })
            .ToList();
    }

    private static SerializableRun RunWithPlayers(int count)
    {
        ModelId characterId = ModelDb.Character<Ironclad>().Id;
        return new SerializableRun
        {
            SchemaVersion = 1110,
            SerializableOdds = new SerializableRunOddsSet(),
            SerializableRng = new SerializableRunRngSet { Seed = "runtime-run-seed" },
            SerializableSharedRelicGrabBag = new SerializableRelicGrabBag(),
            Players = Enumerable.Range(0, count).Select(slot => new SerializablePlayer
            {
                NetId = 100UL + (ulong)slot,
                CharacterId = characterId,
                CurrentHp = 80,
                MaxHp = 80,
                MaxEnergy = 3,
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

    private sealed record PendingVanilla(PacketWriter Writer, byte[] Buffer, int Length);

    private sealed class RuntimePair : IDisposable
    {
        internal const ulong DefaultHostId = 1;
        internal const ulong DefaultClientId = 22;

        internal RuntimePair(bool bindFlows = true, bool bindClientFlow = true)
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = () => (int)TestNativeTypeId;
            HostTransport = new TestNetHost(Host, DefaultHostId);
            typeof(NetHostGameService).GetField("_netHost", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Host, HostTransport);
            ClientTransport = new TestNetClient(Client, DefaultClientId, DefaultHostId);
            Client.Initialize(ClientTransport, default);
            typeof(NetClientGameService).GetField("<IsConnected>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Client, true);
            Offer = new LanConnectProtocolOffer(1, 1, "0.6.1-alpha.1", false, false);
            Selection = CreateSelection();
            Runtime.BindHost(Host, Offer, Selection);
            Runtime.BindClient(Client, Offer, Selection, ProtocolFlowNonce);
            _ = bindClientFlow;
            if (bindFlows)
            {
                Runtime.PrepareHostNativeFlow(Host, ClientId, ProtocolFlowNonce);
                HostTransport.AddConnectedPeer(ClientId);
            }
        }

        private readonly bool _bindClientFlowUnused;


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
        internal PacketWriter HostWriter => GetWriter(HostBus);
        internal PacketWriter ClientWriter => GetWriter(ClientBus);

        internal IDisposable FreezeHostSession() =>
            LanConnectSessionProtocolState.Shared.FreezeHost(Selection, $"native-tests-{Guid.NewGuid():N}");

        internal IDisposable FreezeClientSession() =>
            LanConnectSessionProtocolState.Shared.FreezeClient(Selection, $"native-tests-{Guid.NewGuid():N}");

        /// <summary>先提交一次 join-response 快照，使 PlayerJoined 类消息具备权威 roster。</summary>
        internal void BootstrapHostRoster()
        {
            _ = Runtime.PrepareOutgoing(
                HostBus,
                LanConnectSidecarMessageKind.LobbyJoinResponse,
                HostId,
                new ClientLobbyJoinResponseMessage
                {
                    playersInLobby = StartRunPlayers(2),
                    modifiers = []
                },
                Selection);
        }

        internal void PrepareHostNativeFlow(ulong peerId, byte[] nonce)
        {
            Runtime.PrepareHostNativeFlow(Host, peerId, nonce);
            HostTransport.AddConnectedPeer(peerId);
        }

        /// <summary>镜像桌面第一级 seam：prefix（prepare）先于 header 写入，再投影序列化 + postfix。</summary>
        internal PendingVanilla SerializeHostMatrixMessage(
            LanConnectSidecarMessageKind kind,
            INetMessage message)
        {
            return SerializeMatrixMessage(HostWriter, HostBus, Runtime, kind, message, Selection, HostId, prepareBeforeHeader: true);
        }

        internal PendingVanilla SerializeClientMatrixMessage(
            LanConnectSidecarMessageKind kind,
            INetMessage message)
        {
            return SerializeMatrixMessage(ClientWriter, ClientBus, Runtime, kind, message, Selection, ClientId, prepareBeforeHeader: true);
        }

        /// <summary>镜像安卓/回退 seam：header 先写入，prepare 发生在 T.Serialize 边界。</summary>
        internal PendingVanilla SerializeHostMatrixMessageAndroidOrder(
            LanConnectSidecarMessageKind kind,
            INetMessage message)
        {
            return SerializeMatrixMessage(HostWriter, HostBus, Runtime, kind, message, Selection, HostId, prepareBeforeHeader: false);
        }

        private static PendingVanilla SerializeMatrixMessage(
            PacketWriter writer,
            NetMessageBus bus,
            LanConnectTailMessageRuntime runtime,
            LanConnectSidecarMessageKind kind,
            INetMessage message,
            LanConnectProtocolSelection selection,
            ulong senderNetId,
            bool prepareBeforeHeader)
        {
            writer.Reset();
            LanConnectNativePreparedMessage? prepared;
            if (prepareBeforeHeader)
            {
                if (!runtime.TryPrepareConcreteOutgoing(writer, kind, message, out prepared))
                {
                    throw new InvalidOperationException("matrix message was not prepared");
                }

                WriteVanillaHeader(writer, message, senderNetId);
            }
            else
            {
                WriteVanillaHeader(writer, message, senderNetId);
                if (!runtime.TryPrepareConcreteOutgoing(writer, kind, message, out prepared))
                {
                    throw new InvalidOperationException("matrix message was not prepared");
                }
            }

            INetMessage projected = (INetMessage)prepared!.Prepared.Message;
            projected.Serialize(writer);
            runtime.CompleteConcreteOutgoing(prepared);
            _ = bus;
            _ = selection;
            return new PendingVanilla(writer, writer.Buffer, writer.BytePosition);
        }

        private static void WriteVanillaHeader(PacketWriter writer, INetMessage message, ulong senderNetId)
        {
            writer.WriteByte(checked((byte)message.ToId()));
            writer.WriteULong(senderNetId);
        }

        internal void DeliverPendingToPeer(PendingVanilla pending, ulong peerId)
        {
            LanConnectNativeSendContext context = Runtime.BeginNativeTransport(
                HostTransport,
                isHostTransport: true,
                peerId,
                pending.Buffer,
                pending.Length) ?? throw new InvalidOperationException("pending did not resolve");
            Runtime.CompleteNativeTransport(context, vanillaPeerReachable: true);
        }

        internal void DeliverPendingToHost(PendingVanilla pending)
        {
            LanConnectNativeSendContext context = Runtime.BeginNativeTransport(
                ClientTransport,
                isHostTransport: false,
                recipientPeerId: 0,
                pending.Buffer,
                pending.Length) ?? throw new InvalidOperationException("pending did not resolve");
            Runtime.CompleteNativeTransport(context, vanillaPeerReachable: true);
        }

        public void Dispose()
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
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

        private static PacketWriter GetWriter(NetMessageBus bus) =>
            (PacketWriter)typeof(NetMessageBus)
                .GetField("_writer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(bus)!;
    }

    private static LanConnectProtocolSelection CreateSelection()
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            1,
            LanConnectProtocolCarrier.NativeBusV1,
            "0.6.1-alpha.1",
            8,
            "0.111.0",
            "aabb",
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }

    private sealed record CapturedPacket(ulong PeerId, byte[] Bytes);

    private sealed class TestNetHost(INetHostHandler handler, ulong netId) : ENetHost(handler)
    {
        private readonly List<ulong> _connectedPeerIds = [];
        internal List<CapturedPacket> SentToClients { get; } = [];
        internal List<ulong> DisconnectedPeers { get; } = [];
        internal Func<ulong, byte[], int, LanConnectNativeSendContext?>? SendProbe { get; set; }
        public override IEnumerable<ulong> ConnectedPeerIds => _connectedPeerIds;
        public override bool IsConnected => true;
        public override ulong NetId { get; } = netId;

        internal void AddConnectedPeer(ulong peerId)
        {
            if (!_connectedPeerIds.Contains(peerId))
            {
                _connectedPeerIds.Add(peerId);
            }
        }

        public override void Update() { }
        public override void SetHostIsClosed(bool isClosed) { }

        public override void SendMessageToClient(
            ulong peerId,
            byte[] bytes,
            int length,
            NetTransferMode mode,
            int channel = 0)
        {
            if (SendProbe != null)
            {
                _ = SendProbe(peerId, bytes, length);
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
        ulong hostNetId) : ENetClient(handler)
    {
        internal List<byte[]> SentToHost { get; } = [];
        internal List<NetError> DisconnectReasons { get; } = [];
        public override bool IsConnected => true;
        public override ulong NetId { get; } = netId;
        public override ulong HostNetId { get; } = hostNetId;
        public override void Update() { }

        public override void SendMessageToHost(
            byte[] bytes,
            int length,
            NetTransferMode mode,
            int channel = 0) =>
            SentToHost.Add(bytes.AsSpan(0, length).ToArray());

        public override void DisconnectFromHost(NetError reason, bool now = false) =>
            DisconnectReasons.Add(reason);

        public override string? GetRawLobbyIdentifier() => null;
    }

    /// <summary>将 wire 字节直接送入消息 Deserialize 的最小适配器（跳过 9 字节线头）。</summary>
    private sealed class PacketReaderAdapter : PacketReader
    {
        private readonly byte[] _wire;

        public PacketReaderAdapter(byte[] wire)
        {
            _wire = wire;
            Reset(wire);
            _ = ReadByte();
            _ = ReadULong();
        }
    }
}
