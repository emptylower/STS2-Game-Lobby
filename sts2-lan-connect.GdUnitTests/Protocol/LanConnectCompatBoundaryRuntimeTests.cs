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
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

/// <summary>
/// compat_4_5_v1 桌面回归（0.6.1 起）：native_bus_v1 桌面 seam 把 SerializeMessage&lt;T&gt;
/// 闭合实例化替换为全优化 DynamicMethod，其内联的原始 IL 会绕过 T.Serialize 上的 compat
/// 位宽 transpiler。这里验证消息总线边界强制路径：compat 下 begin-run / join-response
/// 恒为 compat 位宽（不依赖 JIT 内联决策）；tail_v1 完全走原路径且扩展帧不受影响。
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectCompatBoundaryRuntimeTests
{
    private static readonly object InitializationSync = new();
    private static bool _initialized;

    [TestCase]
    public void Compat_begin_run_is_written_at_compat_widths_at_the_message_bus_boundary()
    {
        using BoundaryScope scope = BoundaryScope.Create(captureLogs: true);
        using LanConnectSessionProtocolLease lease = LanConnectSessionProtocolState.Shared.FreezeHost(
            CompatSelection(), "compat-boundary-begin-run");

        LobbyBeginRunMessage message = new()
        {
            playersInLobby = StartRunPlayers([0, 1, 2, 3, 4, 5, 6, 7]),
            seed = "seed-8",
            modifiers = [],
            act1 = "Act1"
        };
        byte[] buffer = SerializeMessage(scope.Pair.HostBus, scope.Pair.HostId, message, out int length);
        byte[] generated = buffer.AsSpan(0, length).ToArray();

        // 强制路径自身留下的证据：诊断状态 + 边界日志。
        AssertThat(LanConnectSerializationPatches.BeginRunBoundaryStateForTesting).IsEqual("patched");
        AssertThat(LanConnectSerializationPatches.JoinResponseBoundaryStateForTesting).IsEqual("patched");
        AssertThat(scope.CapturedLogs.Any(static line => line.Contains(
            "lobby begin-run forced at message-bus boundary players=8, lobbyListBits=5, bodyBytes=",
            StringComparison.Ordinal))).IsTrue();

        // 用 compat 位宽（5 bit 列表 / 4 bit slotId）读回全部字段。
        PacketReader reader = new();
        reader.Reset(generated);
        AssertThat(reader.ReadByte()).IsEqual(checked((byte)message.ToId()));
        AssertThat(reader.ReadULong()).IsEqual(scope.Pair.HostId);
        List<StartRunLobbyPlayer> players = reader.ReadList<StartRunLobbyPlayer>(
            LanConnectConstants.ExtendedLobbyListBits);
        AssertThat(players.Count).IsEqual(8);
        AssertThat(players.Select(static player => player.slotId).ToArray())
            .IsEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        AssertThat(players.Select(static player => player.id).ToArray())
            .IsEqual(Enumerable.Range(0, 8).Select(static slot => 100UL + (ulong)slot).ToArray());
        AssertThat(reader.ReadString()).IsEqual("seed-8");
        AssertThat(reader.ReadList<SerializableModifier>().Count).IsEqual(0);
        AssertThat(reader.ReadString()).IsEqual("Act1");
        AssertThat(reader.BitPosition).IsLessEqual(length * 8);

        // 原版位宽（3 bit）读出的玩家数必然不是 8：证明线上字节不是原版布局。
        PacketReader vanillaReader = new();
        vanillaReader.Reset(generated);
        _ = vanillaReader.ReadByte();
        _ = vanillaReader.ReadULong();
        AssertThat(vanillaReader.ReadInt(LanConnectConstants.VanillaLobbyListBits)).IsNotEqual(8);

        // compat 下不产生扩展帧，也不得给 writer 留下 pending（tail transport postfix 拿不到上下文）。
        AssertThat(IndexOf(generated, "STSLAN01"u8.ToArray()) < 0).IsTrue();
        AssertThat(scope.Pair.Runtime.BeginNativeTransport(
            scope.Pair.HostTransport,
            isHostTransport: true,
            scope.Pair.ClientId,
            generated,
            length)).IsNull();
    }

    [TestCase]
    public void Compat_join_response_is_written_at_compat_widths_at_the_message_bus_boundary()
    {
        using BoundaryScope scope = BoundaryScope.Create(captureLogs: true);
        using LanConnectSessionProtocolLease lease = LanConnectSessionProtocolState.Shared.FreezeHost(
            CompatSelection(), "compat-boundary-join-response");

        ClientLobbyJoinResponseMessage message = new()
        {
            playersInLobby = StartRunPlayers([4, 5, 6, 7]),
            dailyTime = null,
            ascension = 8,
            seed = null,
            modifiers = []
        };
        byte[] buffer = SerializeMessage(scope.Pair.HostBus, scope.Pair.HostId, message, out int length);
        byte[] generated = buffer.AsSpan(0, length).ToArray();

        AssertThat(scope.CapturedLogs.Any(static line => line.Contains(
            "lobby join-response forced at message-bus boundary players=4, lobbyListBits=5, bodyBytes=",
            StringComparison.Ordinal))).IsTrue();

        PacketReader reader = new();
        reader.Reset(generated);
        AssertThat(reader.ReadByte()).IsEqual(checked((byte)message.ToId()));
        AssertThat(reader.ReadULong()).IsEqual(scope.Pair.HostId);
        List<StartRunLobbyPlayer> players = reader.ReadList<StartRunLobbyPlayer>(
            LanConnectConstants.ExtendedLobbyListBits);
        AssertThat(players.Count).IsEqual(4);
        AssertThat(players.Select(static player => player.slotId).ToArray()).IsEqual(new[] { 4, 5, 6, 7 });
        AssertThat(reader.ReadBool()).IsFalse();
        AssertThat(reader.ReadBool()).IsFalse();
        AssertThat(reader.ReadInt(5)).IsEqual(8);
        AssertThat(reader.ReadList<SerializableModifier>().Count).IsEqual(0);
        AssertThat(reader.BitPosition).IsLessEqual(length * 8);

        PacketReader vanillaReader = new();
        vanillaReader.Reset(generated);
        _ = vanillaReader.ReadByte();
        _ = vanillaReader.ReadULong();
        AssertThat(vanillaReader.ReadInt(LanConnectConstants.VanillaLobbyListBits)).IsNotEqual(4);

        AssertThat(IndexOf(generated, "STSLAN01"u8.ToArray()) < 0).IsTrue();
        AssertThat(scope.Pair.Runtime.BeginNativeTransport(
            scope.Pair.HostTransport,
            isHostTransport: true,
            scope.Pair.ClientId,
            generated,
            length)).IsNull();
    }

    [TestCase]
    public void Tail_profile_walks_the_original_path_and_extension_frames_still_flow()
    {
        using BoundaryScope scope = BoundaryScope.Create(captureLogs: false);

        // 先在 compat 租约下走一次强制路径（不得给 writer 留下 pending 或脏状态）。
        using (LanConnectSessionProtocolLease compatLease = LanConnectSessionProtocolState.Shared.FreezeHost(
                   CompatSelection(), "tail-path-after-compat"))
        {
            LobbyBeginRunMessage compatMessage = new()
            {
                playersInLobby = StartRunPlayers([0, 1, 2, 3, 4, 5, 6, 7]),
                seed = "seed-compat",
                modifiers = [],
                act1 = "Act1"
            };
            _ = SerializeMessage(scope.Pair.HostBus, scope.Pair.HostId, compatMessage, out _);
        }

        // 切回 tail_v1：prefix 必须走原路径，原版位宽可读，扩展帧仍成对发出。
        using (LanConnectSessionProtocolLease tailLease = LanConnectSessionProtocolState.Shared.FreezeHost(
                   scope.Pair.Selection, "tail-path-original"))
        {
            LobbyBeginRunMessage tailMessage = new()
            {
                playersInLobby = StartRunPlayers([0, 1, 2]),
                seed = "seed-3",
                modifiers = [],
                act1 = "Act1"
            };
            byte[] buffer = SerializeMessage(scope.Pair.HostBus, scope.Pair.HostId, tailMessage, out int length);
            byte[] generated = buffer.AsSpan(0, length).ToArray();

            PacketReader reader = new();
            reader.Reset(generated);
            _ = reader.ReadByte();
            _ = reader.ReadULong();
            List<StartRunLobbyPlayer> players = reader.ReadList<StartRunLobbyPlayer>(
                LanConnectConstants.VanillaLobbyListBits);
            AssertThat(players.Count).IsEqual(3);
            AssertThat(reader.ReadString()).IsEqual("seed-3");

            LanConnectSidecarFrame extensionFrame = DeliverExtensionFrame(scope.Pair, buffer, length);
            AssertThat(extensionFrame.MessageKind).IsEqual(LanConnectSidecarMessageKind.LobbyBeginRun);
        }
    }

    private static LanConnectProtocolSelection CompatSelection() =>
        LanConnectProtocolSelection.CreateLocalCompat(8, "0.111.0", wireCacheSignature: null);

    private static byte[] SerializeMessage(NetMessageBus bus, ulong sender, object message, out int length)
    {
        MethodInfo method = typeof(NetMessageBus).GetMethods()
            .Where(static candidate => candidate.Name == nameof(NetMessageBus.SerializeMessage)
                && candidate.IsGenericMethodDefinition)
            .Single(static candidate => candidate.GetParameters().Length == 3)
            .MakeGenericMethod(message.GetType());
        object?[] args = [sender, message, 0];
        byte[] buffer = (byte[])method.Invoke(bus, args)!;
        length = (int)args[2]!;
        return buffer;
    }

    /// <summary>镜像第三级 transport postfix：取 pending → 专用发送出口 → 解出 sidecar 帧。</summary>
    private static LanConnectSidecarFrame DeliverExtensionFrame(
        BoundaryRuntimePair pair,
        byte[] buffer,
        int length)
    {
        LanConnectNativeSendContext context = pair.Runtime.BeginNativeTransport(
            pair.HostTransport,
            isHostTransport: true,
            pair.ClientId,
            buffer,
            length) ?? throw new InvalidOperationException("tail pending did not resolve");
        pair.Runtime.CompleteNativeTransport(context, vanillaPeerReachable: true);

        byte[] wire = pair.HostTransport.SentToClients[^1].Bytes;
        LanConnectNativeBusMessage message = new();
        PacketReader reader = new();
        reader.Reset(wire);
        _ = reader.ReadByte();
        _ = reader.ReadULong();
        message.Deserialize(reader);
        AssertThat(message.InvalidReason).IsNull();
        return LanConnectSidecarFrameCodec.Decode(message.Frame!.ToArray());
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
            maxMultiplayerAscensionUnlocked = 20,
            isModded = false,
            isReady = true
        }).ToList();
    }

    private static int IndexOf(byte[] source, byte[] needle)
    {
        for (int index = 0; index <= source.Length - needle.Length; index++)
        {
            if (source.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return index;
            }
        }

        return -1;
    }

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

    /// <summary>生产补丁组合（serialization + 默认 tail 计划）+ 测试宿主/传输桩的生命周期。</summary>
    private sealed class BoundaryScope : IDisposable
    {
        private readonly Harmony _harmony;
        private readonly Harmony _productionCleanup;
        private readonly NativeTypeIdScope _typeId = new();
        private readonly bool _capturedLogs;

        private BoundaryScope(Harmony harmony, Harmony productionCleanup, bool capturedLogs)
        {
            _harmony = harmony;
            _productionCleanup = productionCleanup;
            _capturedLogs = capturedLogs;
        }

        internal BoundaryRuntimePair Pair { get; } = new();
        internal List<string> CapturedLogs { get; } = [];

        internal static BoundaryScope Create(bool captureLogs)
        {
            InitializeSts2Serialization();
            BoundaryScope scope = new(
                new Harmony($"sts2_lan_connect.tests.compat_boundary.{Guid.NewGuid():N}"),
                new Harmony(LanConnectProtocolPatchDispatcher.HarmonyId),
                captureLogs);
            LanConnectTailMessagePatches.ConfigureRuntime(scope.Pair.Runtime);
            if (captureLogs)
            {
                LanConnectSerializationPatches.LogInfoSink = scope.CapturedLogs.Add;
            }

            LanConnectSerializationPatches.Apply();
            LanConnectTailMessagePatches.Apply(scope._harmony);
            return scope;
        }

        public void Dispose()
        {
            _harmony.UnpatchAll(_harmony.Id);
            _productionCleanup.UnpatchAll(LanConnectProtocolPatchDispatcher.HarmonyId);
            LanConnectSerializationPatches.ResetAppliedAfterExternalRollback();
            LanConnectTailMessagePatches.ConfigureRuntime(LanConnectTailMessageRuntime.Shared);
            if (_capturedLogs)
            {
                LanConnectSerializationPatches.ResetLogSinksForTesting();
            }

            _typeId.Dispose();
            Pair.Dispose();
        }
    }

    private sealed class NativeTypeIdScope : IDisposable
    {
        private const uint TestNativeTypeId = 200;

        public NativeTypeIdScope()
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = () => (int)TestNativeTypeId;
        }

        public void Dispose() => LanConnectNativeBusSender.TypeIdResolverForTesting = null;
    }

    private sealed class BoundaryRuntimePair : IDisposable
    {
        internal const ulong DefaultHostId = 1;
        internal const ulong DefaultClientId = 22;

        // 与 NativeTypeIdScope.TestNativeTypeId 同值：两端对称（同表）时对端寻址 id == 本机 id。
        private const uint TestNativeTypeId = 200;

        internal BoundaryRuntimePair()
        {
            HostTransport = new TestNetHost(Host, DefaultHostId);
            typeof(NetHostGameService).GetField("_netHost", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Host, HostTransport);
            ClientTransport = new TestNetClient(Client, DefaultClientId, DefaultHostId);
            Client.Initialize(ClientTransport, default);
            Runtime.BindHost(Host, Offer, Selection);
            Runtime.BindClient(Client, Offer, Selection, Convert.FromHexString("00112233445566778899aabbccddeeff"), (int)TestNativeTypeId);
            Runtime.PrepareHostNativeFlow(Host, ClientId, Convert.FromHexString("00112233445566778899aabbccddeeff"), (int)TestNativeTypeId);
        }

        internal LanConnectTailMessageRuntime Runtime { get; } = new();
        internal NetHostGameService Host { get; } = new(PeerVersionInfo.LocalDefault());
        internal NetClientGameService Client { get; } = new(PeerVersionInfo.LocalDefault());
        internal TestNetHost HostTransport { get; }
        internal TestNetClient ClientTransport { get; }
        internal ulong HostId => DefaultHostId;
        internal ulong ClientId => DefaultClientId;
        internal LanConnectProtocolOffer Offer { get; } = new(1, 1, "0.6.3-alpha.1", false, false);
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

        private static LanConnectProtocolSelection CreateSelection()
        {
            LanConnectProtocolSelection selection = new(
                LanConnectProtocolProfile.TailV1,
                1,
                LanConnectProtocolCarrier.NativeBusV1,
                "0.6.3-alpha.1",
                8,
                "0.111.0",
                "aabbccdd",
                false,
                string.Empty);
            return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
        }
    }

    private sealed record CapturedPacket(ulong PeerId, byte[] Bytes);

    private sealed class TestNetHost(INetHostHandler handler, ulong netId) : ENetHost(handler)
    {
        internal List<CapturedPacket> SentToClients { get; } = [];

        public override IEnumerable<ulong> ConnectedPeerIds => [BoundaryRuntimePair.DefaultClientId];
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

        public override void SendMessageToAll(byte[] bytes, int length, NetTransferMode mode, int channel = 0) { }
        public override void DisconnectClient(ulong peerId, NetError reason, bool now = false) { }
        public override void StopHost(NetError reason, bool now = false) { }
        public override string? GetRawLobbyIdentifier() => null;
    }

    private sealed class TestNetClient(INetClientHandler handler, ulong netId, ulong hostNetId) : ENetClient(handler)
    {
        internal List<byte[]> SentToHost { get; } = [];

        public override bool IsConnected => true;
        public override ulong NetId { get; } = netId;
        public override ulong HostNetId { get; } = hostNetId;
        public override void Update() { }

        public override void SendMessageToHost(byte[] bytes, int length, NetTransferMode mode, int channel = 0) =>
            SentToHost.Add(bytes.AsSpan(0, length).ToArray());

        public override void DisconnectFromHost(NetError reason, bool now = false) { }
        public override string? GetRawLobbyIdentifier() => null;
    }
}
