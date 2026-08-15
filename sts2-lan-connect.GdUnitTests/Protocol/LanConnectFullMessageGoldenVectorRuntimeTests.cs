using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
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
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectFullMessageGoldenVectorRuntimeTests
{
    private static readonly object InitializationSync = new();
    private static bool _initialized;

    [TestCase]
    public void Full_message_golden_vectors_match_real_v01110_netmessagebus_packets()
    {
        InitializeSts2Serialization();
        string fixtureRoot = Path.Combine(FindRepositoryRoot(), "test-fixtures", "protocol", "v0.6");

        using RuntimePair pair = new();
        Harmony harmony = new($"sts2_lan_connect.tests.full_message_golden.{Guid.NewGuid():N}");
        LanConnectTailMessagePatches.ConfigureRuntime(pair.Runtime);
        LanConnectTailMessagePatches.Apply(harmony);
        try
        {
            foreach (MessageSpec spec in Specs())
            {
                using LanConnectSessionProtocolLease lease = spec.Direction == Direction.HostToClient
                    ? LanConnectSessionProtocolState.Shared.FreezeHost(pair.Selection, spec.Name)
                    : LanConnectSessionProtocolState.Shared.FreezeClient(pair.Selection, spec.Name);
                IDisposable? rejectionScope = spec.Rejection == null ? null : PushOutgoingRejection(spec.Rejection);
                try
                {
                    object message = spec.CreateMessage();
                    NetMessageBus senderBus = spec.Direction == Direction.HostToClient ? pair.HostBus : pair.ClientBus;
                    ulong sender = spec.Direction == Direction.HostToClient ? pair.HostId : pair.ClientId;
                    byte[] generated = SerializeMessage(senderBus, sender, message, out int length)
                        .AsSpan(0, length)
                        .ToArray();
                    Fixture fixture = BuildFixture(spec, generated, sender, message.GetType());

                    string binPath = Path.Combine(fixtureRoot, $"{spec.Name}.bin");
                    string jsonPath = Path.Combine(fixtureRoot, $"{spec.Name}.json");

                    byte[] expected = File.ReadAllBytes(binPath);
                    AssertThat(Convert.ToHexString(generated)).IsEqual(Convert.ToHexString(expected));
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
                    JsonElement root = document.RootElement;
                    AssertThat(root.GetProperty("schema").GetString()).IsEqual("sts2-v0.111.0-netmessagebus-tail-full-v1");
                    AssertThat(root.GetProperty("messageTypeId").GetInt32()).IsEqual(fixture.MessageTypeId);
                    AssertThat(root.GetProperty("senderPeerId").GetUInt64()).IsEqual(sender);
                    AssertThat(root.GetProperty("vanillaBodyEndBit").GetInt32()).IsEqual(fixture.VanillaBodyEndBit);
                    AssertThat(root.GetProperty("containerStartBit").GetInt32()).IsEqual(fixture.ContainerStartBit);
                    AssertThat(root.GetProperty("sha256").GetString()).IsEqual(fixture.Sha256);
                    AssertThat(root.GetProperty("containerSha256").GetString()).IsEqual(fixture.ContainerSha256);

                    NetMessageBus receiverBus = new(new PacketReader(), new PacketWriter());
                    RecordingRuntime recordingRuntime = new(pair.Selection, sender, pair.HostId);
                    LanConnectTailMessagePatches.ConfigureRuntime(recordingRuntime);
                    using (LanConnectTailMessagePatches.PushTransportSenderForTesting(sender))
                    {
                        bool decoded = receiverBus.TryDeserializeMessage(
                            expected,
                            out INetMessage? decodedMessage,
                            out ulong? decodedSender);
                        AssertThat(decoded).IsTrue();
                        AssertThat(decodedSender.HasValue).IsTrue();
                        AssertThat(decodedSender!.Value).IsEqual(sender);
                        AssertBodySemantics(spec, decodedMessage!);
                        AssertTailSemantics(spec, recordingRuntime.Payload!);
                    }
                    LanConnectTailMessagePatches.ConfigureRuntime(pair.Runtime);
                }
                finally
                {
                    LanConnectTailMessagePatches.ConfigureRuntime(pair.Runtime);
                    rejectionScope?.Dispose();
                }
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            LanConnectTailMessagePatches.ConfigureRuntime(LanConnectTailMessageRuntime.Shared);
        }
    }

    private static IEnumerable<MessageSpec> Specs()
    {
        yield return Host(
            "tail-full-initial-game-info-v1",
            LanConnectSidecarMessageKind.InitialGameInfo,
            () => new InitialGameInfoMessage
            {
                sessionState = RunSessionState.InLobby,
                gameMode = GameMode.Standard,
                connectionFailureReason = null
            },
            new Expected("selection") { SessionState = "InLobby", GameMode = "Standard" });
        yield return Client(
            "tail-full-lobby-join-request-v1",
            LanConnectSidecarMessageKind.LobbyJoinRequest,
            () => new ClientLobbyJoinRequestMessage
            {
                maxAscensionUnlocked = 12,
                unlockState = new SerializableUnlockState()
            },
            new Expected("peerOffer")
            {
                MaxAscensionUnlocked = 12,
                ClientVersion = "0.6.0-alpha.1",
                RitsuLibPresent = false,
                RitsuLibSidecarAvailable = false
            });
        yield return Client(
            "tail-full-load-join-request-v1",
            LanConnectSidecarMessageKind.LoadJoinRequest,
            () => new ClientLoadJoinRequestMessage(),
            new Expected("peerOffer")
            {
                ClientVersion = "0.6.0-alpha.1",
                RitsuLibPresent = false,
                RitsuLibSidecarAvailable = false
            });
        yield return Client(
            "tail-full-rejoin-request-v1",
            LanConnectSidecarMessageKind.RejoinRequest,
            () => new ClientRejoinRequestMessage(),
            new Expected("peerOffer")
            {
                ClientVersion = "0.6.0-alpha.1",
                RitsuLibPresent = false,
                RitsuLibSidecarAvailable = false
            });
        yield return Host(
            "tail-full-lobby-join-response-v1",
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            () => new ClientLobbyJoinResponseMessage
            {
                playersInLobby = StartRunPlayers([0, 1, 2, 5, 7]),
                dailyTime = null,
                ascension = 8,
                seed = "qa-seed",
                modifiers = []
            },
            new Expected("roster")
            {
                Ascension = 8,
                Seed = "qa-seed",
                Slots = [0, 1, 2, 5, 7],
                PlayerIds = [100, 101, 102, 105, 107]
            });
        yield return Host(
            "tail-full-player-joined-v1",
            LanConnectSidecarMessageKind.PlayerJoined,
            () => new PlayerJoinedMessage { lobbyPlayer = StartRunPlayers([7]).Single() },
            new Expected("roster")
            {
                Slots = [0, 1, 2, 5, 7],
                PlayerIds = [100, 101, 102, 105, 107],
                JoinedPlayerId = 107
            });
        yield return Host(
            "tail-full-load-join-response-v1",
            LanConnectSidecarMessageKind.LoadJoinResponse,
            () => new ClientLoadJoinResponseMessage
            {
                serializableRun = RunWithPlayers(5),
                playersAlreadyConnected = Enumerable.Range(0, 5)
                    .Select(static slot => new LoadRunLobbyPlayer
                    {
                        id = 100UL + (ulong)slot,
                        isModded = false,
                        isReady = true
                    })
                    .ToList()
            },
            new Expected("roster") { Slots = [0, 1, 2, 3, 4], PlayerIds = [100, 101, 102, 103, 104] });
        yield return Host(
            "tail-full-rejoin-response-v1",
            LanConnectSidecarMessageKind.RejoinResponse,
            () => new ClientRejoinResponseMessage { serializableRun = RunWithPlayers(5), combatState = null },
            new Expected("roster") { Slots = [0, 1, 2, 3, 4], PlayerIds = [100, 101, 102, 103, 104] });

        foreach ((string name, int[] slots) in new[]
        {
            ("tail-full-begin-run-2p-v1", new[] { 0, 1 }),
            ("tail-full-begin-run-4p-v1", new[] { 0, 1, 2, 3 }),
            ("tail-full-begin-run-5p-v1", new[] { 0, 1, 2, 3, 7 }),
            ("tail-full-begin-run-8p-v1", new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
        })
        {
            yield return Host(
                name,
                LanConnectSidecarMessageKind.LobbyBeginRun,
                () => new LobbyBeginRunMessage
                {
                    playersInLobby = StartRunPlayers(slots),
                    seed = $"seed-{slots.Length}",
                    modifiers = [],
                    act1 = "Act1"
                },
                new Expected("roster")
                {
                    Slots = slots,
                    PlayerIds = slots.Select(static slot => 100UL + (ulong)slot).ToArray(),
                    Seed = $"seed-{slots.Length}",
                    Act1 = "Act1"
                });
        }

        foreach ((string name, string stage, string code, string detail) in new[]
        {
            ("tail-full-initial-game-info-rejection-lobby-v1", "lobby", "lan_protocol_version_mismatch", "rejected during lobby join request"),
            ("tail-full-initial-game-info-rejection-load-v1", "load", "game_version_mismatch", "rejected during loaded-run join request"),
            ("tail-full-initial-game-info-rejection-rejoin-v1", "rejoin", "wire_cache_mismatch", "rejected during rejoin request")
        })
        {
            LanConnectProtocolFailure failure = new LanConnectProtocolFailure(code, null, null, detail).Validate();
            yield return Host(
                name,
                LanConnectSidecarMessageKind.ConnectionFailed,
                () => new InitialGameInfoMessage
                {
                    sessionState = RunSessionState.InLobby,
                    gameMode = GameMode.Standard,
                    connectionFailureReason = ConnectionFailureReason.ModMismatch
                },
                new Expected("rejection")
                {
                    RejectionStage = stage,
                    Code = failure.Code,
                    Detail = failure.Detail,
                    ConnectionFailureReason = "ModMismatch",
                    SessionState = "InLobby",
                    GameMode = "Standard"
                },
                failure);
        }
    }

    private static MessageSpec Host(
        string name,
        LanConnectSidecarMessageKind kind,
        Func<object> createMessage,
        Expected expected,
        LanConnectProtocolFailure? rejection = null) =>
        new(name, Direction.HostToClient, kind, createMessage, expected, rejection);

    private static MessageSpec Client(
        string name,
        LanConnectSidecarMessageKind kind,
        Func<object> createMessage,
        Expected expected) =>
        new(name, Direction.ClientToHost, kind, createMessage, expected, null);

    private static Fixture BuildFixture(MessageSpec spec, byte[] packet, ulong sender, Type messageType)
    {
        int containerStartByte = IndexOf(packet, "STSLAN01"u8.ToArray());
        if (containerStartByte < 0)
        {
            throw new InvalidDataException($"{spec.Name}: standalone Tail container magic not found.");
        }

        PacketReader reader = new();
        reader.Reset(packet);
        int messageTypeId = reader.ReadByte();
        ulong embeddedSender = reader.ReadULong();
        INetMessage parsed = (INetMessage)Activator.CreateInstance(messageType)!;
        parsed.Deserialize(reader);
        int vanillaBodyEndBit = reader.BitPosition;
        int containerStartBit = containerStartByte * 8;
        int paddingBits = containerStartBit - vanillaBodyEndBit;
        if (embeddedSender != sender || paddingBits is < 0 or > 7)
        {
            throw new InvalidDataException($"{spec.Name}: invalid sender or standalone padding.");
        }

        byte[] container = packet.AsSpan(containerStartByte).ToArray();
        return new Fixture(
            spec.Name,
            "sts2-v0.111.0-netmessagebus-tail-full-v1",
            $"{spec.Name}.bin",
            messageType.Name,
            messageTypeId,
            sender,
            9,
            spec.Kind.ToString(),
            (byte)spec.Kind,
            vanillaBodyEndBit,
            paddingBits,
            containerStartBit,
            containerStartByte,
            container.Length,
            packet.Length,
            Sha256(packet),
            Sha256(container),
            spec.Expected);
    }

    private static void AssertBodySemantics(MessageSpec spec, INetMessage message)
    {
        switch (message)
        {
            case InitialGameInfoMessage initial:
                AssertThat(initial.sessionState.ToString()).IsEqual(spec.Expected.SessionState);
                AssertThat(initial.gameMode.ToString()).IsEqual(spec.Expected.GameMode);
                AssertThat(initial.connectionFailureReason?.ToString()).IsEqual(spec.Expected.ConnectionFailureReason);
                break;
            case ClientLobbyJoinRequestMessage request:
                AssertThat(request.maxAscensionUnlocked).IsEqual(spec.Expected.MaxAscensionUnlocked!.Value);
                break;
            case ClientLobbyJoinResponseMessage response:
                AssertThat(response.ascension).IsEqual(spec.Expected.Ascension!.Value);
                AssertThat(response.seed).IsEqual(spec.Expected.Seed);
                AssertThat(response.playersInLobby!.Count).IsLessEqual(4);
                break;
            case PlayerJoinedMessage joined:
                AssertThat(joined.lobbyPlayer.id).IsEqual(spec.Expected.JoinedPlayerId!.Value);
                break;
            case ClientLoadJoinResponseMessage load:
                AssertThat(load.playersAlreadyConnected.Select(static player => player.id).ToArray())
                    .IsEqual(spec.Expected.PlayerIds!);
                break;
            case ClientRejoinResponseMessage rejoin:
                AssertThat(rejoin.serializableRun.Players.Select(static player => player.NetId).ToArray())
                    .IsEqual(spec.Expected.PlayerIds!);
                break;
            case LobbyBeginRunMessage begin:
                AssertThat(begin.seed).IsEqual(spec.Expected.Seed);
                AssertThat(begin.act1).IsEqual(spec.Expected.Act1);
                AssertThat(begin.playersInLobby!.Count).IsLessEqual(4);
                break;
        }
    }

    private static void AssertTailSemantics(MessageSpec spec, LanConnectTailMessagePayload payload)
    {
        AssertThat(payload.MessageKind).IsEqual(spec.Kind);
        switch (spec.Expected.Payload)
        {
            case "peerOffer":
                AssertThat(payload.PeerOffer!.ClientVersion).IsEqual(spec.Expected.ClientVersion);
                AssertThat(payload.PeerOffer.RitsuLibPresent).IsEqual(spec.Expected.RitsuLibPresent!.Value);
                AssertThat(payload.PeerOffer.RitsuLibSidecarAvailable)
                    .IsEqual(spec.Expected.RitsuLibSidecarAvailable!.Value);
                break;
            case "selection":
                AssertThat(payload.SessionSelection!.Carrier).IsEqual(LanConnectProtocolCarrier.StandaloneTailV1);
                AssertThat(payload.Roster).IsNull();
                AssertThat(payload.Rejection).IsNull();
                break;
            case "roster":
                AssertThat(payload.Roster!.Players.Select(static player => (int)player.RealSlotId).ToArray())
                    .IsEqual(spec.Expected.Slots!);
                AssertThat(payload.Roster.Players.Select(static player => player.PlayerId).ToArray())
                    .IsEqual(spec.Expected.PlayerIds!);
                break;
            case "rejection":
                AssertThat(payload.Rejection!.Code).IsEqual(spec.Expected.Code);
                AssertThat(payload.Rejection.Detail).IsEqual(spec.Expected.Detail);
                break;
        }
    }

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

    private static IDisposable PushOutgoingRejection(LanConnectProtocolFailure failure)
    {
        FieldInfo field = typeof(LanConnectTailMessageRuntime).GetField(
            "_outgoingRejections",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Stack<LanConnectProtocolFailure> stack = new();
        stack.Push(failure);
        field.SetValue(null, stack);
        return new RejectionScope(field);
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

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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

    private static SerializableRun RunWithPlayers(int count)
    {
        ModelId characterId = ModelDb.Character<Ironclad>().Id;
        return new SerializableRun
        {
            SchemaVersion = 1110,
            SerializableOdds = new SerializableRunOddsSet(),
            SerializableRng = new SerializableRunRngSet { Seed = "golden-run-seed" },
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

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private enum Direction
    {
        HostToClient,
        ClientToHost
    }

    private sealed record MessageSpec(
        string Name,
        Direction Direction,
        LanConnectSidecarMessageKind Kind,
        Func<object> CreateMessage,
        Expected Expected,
        LanConnectProtocolFailure? Rejection);

    private sealed record Fixture(
        string Name,
        string Schema,
        string File,
        string MessageType,
        int MessageTypeId,
        ulong SenderPeerId,
        int HeaderBytes,
        string MessageKind,
        int MessageKindValue,
        int VanillaBodyEndBit,
        int PaddingBits,
        int ContainerStartBit,
        int ContainerStartByte,
        int ContainerBytes,
        int TotalBytes,
        string Sha256,
        string ContainerSha256,
        Expected Expected);

    private sealed record Expected(string Payload)
    {
        public string? SessionState { get; init; }
        public string? GameMode { get; init; }
        public string? ConnectionFailureReason { get; init; }
        public int? MaxAscensionUnlocked { get; init; }
        public int? Ascension { get; init; }
        public string? Seed { get; init; }
        public string? Act1 { get; init; }
        public string? ClientVersion { get; init; }
        public bool? RitsuLibPresent { get; init; }
        public bool? RitsuLibSidecarAvailable { get; init; }
        public int[]? Slots { get; init; }
        public ulong[]? PlayerIds { get; init; }
        public ulong? JoinedPlayerId { get; init; }
        public string? RejectionStage { get; init; }
        public string? Code { get; init; }
        public string? Detail { get; init; }
    }

    private sealed class RejectionScope(FieldInfo field) : IDisposable
    {
        public void Dispose() => field.SetValue(null, null);
    }

    private sealed class RecordingRuntime(
        LanConnectProtocolSelection selection,
        ulong expectedSender,
        ulong hostPeerId) : ILanConnectTailMessageRuntime
    {
        internal LanConnectTailMessagePayload? Payload { get; private set; }

        public LanConnectPreparedTailMessage PrepareOutgoing(
            NetMessageBus messageBus,
            LanConnectSidecarMessageKind messageKind,
            ulong senderPeerId,
            object message,
            LanConnectProtocolSelection activeSelection) =>
            throw new NotSupportedException();

        public void SubmitSidecarBeforeVanilla(
            NetMessageBus messageBus,
            LanConnectSidecarMessageKind messageKind,
            ulong senderPeerId,
            object message,
            byte[] container,
            LanConnectProtocolSelection activeSelection) =>
            throw new NotSupportedException();

        public void ValidateStandaloneIncoming(
            NetMessageBus messageBus,
            LanConnectSidecarMessageKind messageKind,
            ulong transportSenderPeerId,
            INetMessage message,
            byte[] container,
            LanConnectProtocolSelection activeSelection)
        {
            AssertThat(transportSenderPeerId).IsEqual(expectedSender);
            Payload = messageKind is
                LanConnectSidecarMessageKind.LobbyJoinRequest or
                LanConnectSidecarMessageKind.LoadJoinRequest or
                LanConnectSidecarMessageKind.RejoinRequest
                    ? LanConnectTailMessagePatches.DecodeAndValidate(messageKind, container)
                    : LanConnectTailMessagePatches.DecodeAndValidate(
                        messageKind,
                        container,
                        selection,
                        transportSenderPeerId,
                        hostPeerId);
        }

        public void HandleIncomingFailure(
            NetMessageBus messageBus,
            ulong transportSenderPeerId,
            Exception exception,
            LanConnectProtocolSelection activeSelection) =>
            throw exception;

        public bool TryPairSidecarIncoming(
            NetMessageBus messageBus,
            LanConnectSidecarMessageKind messageKind,
            ulong senderPeerId,
            INetMessage message,
            LanConnectProtocolSelection activeSelection) =>
            false;
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
            Runtime.BindClient(Client, Offer, Selection, Convert.FromHexString("00112233445566778899aabbccddeeff"));
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

        private static LanConnectProtocolSelection CreateSelection()
        {
            LanConnectProtocolSelection selection = new(
                LanConnectProtocolProfile.TailV1,
                1,
                LanConnectProtocolCarrier.StandaloneTailV1,
                "0.6.0-alpha.1",
                8,
                "0.111.0",
                "aabbccdd",
                false,
                string.Empty);
            return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
        }
    }

    private sealed record CapturedPacket(ulong PeerId, byte[] Bytes);

    private sealed class TestNetHost(INetHostHandler handler, ulong netId) : NetHost(handler)
    {
        internal List<CapturedPacket> SentToClients { get; } = [];
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
        public override void SendMessageToAll(byte[] bytes, int length, NetTransferMode mode, int channel = 0) { }
        public override void DisconnectClient(ulong peerId, NetError reason, bool now = false) { }
        public override void StopHost(NetError reason, bool now = false) { }
        public override string? GetRawLobbyIdentifier() => null;
    }

    private sealed class TestNetClient(INetClientHandler handler, ulong netId, ulong hostNetId) : NetClient(handler)
    {
        public override bool IsConnected { get; } = true;
        public override ulong NetId { get; } = netId;
        public override ulong HostNetId { get; } = hostNetId;
        public override void Update() { }
        public override void SendMessageToHost(byte[] bytes, int length, NetTransferMode mode, int channel = 0) { }
        public override void DisconnectFromHost(NetError reason, bool now = false) { }
        public override string? GetRawLobbyIdentifier() => null;
    }
}
