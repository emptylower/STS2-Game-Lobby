using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectFullMessageGoldenVectorTests
{
    private static readonly byte[] TailMagic = "STSLAN01"u8.ToArray();
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly string[] RejectionCodes =
    [
        "client_update_required",
        "protocol_profile_unsupported",
        "ritsulib_not_allowed_in_compat_mode",
        "ritsulib_presence_mismatch",
        "game_version_mismatch",
        "wire_cache_mismatch",
        "lan_tail_required",
        "lan_tail_malformed",
        "lan_protocol_version_mismatch",
        "ritsulib_sidecar_unavailable"
    ];

    [Fact]
    public void Reviewed_full_message_vectors_cover_current_v01110_tail_contract()
    {
        string fixtureRoot = Path.Combine(FindRepositoryRoot(), "test-fixtures", "protocol", "v0.6");
        string[] jsonFiles = Directory.GetFiles(fixtureRoot, "tail-full-*-v1.json")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(15, jsonFiles.Length);
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-initial-game-info-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-lobby-join-request-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-lobby-join-response-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-load-join-request-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-load-join-response-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-rejoin-request-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-rejoin-response-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-initial-game-info-rejection-lobby-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-initial-game-info-rejection-load-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-initial-game-info-rejection-rejoin-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-player-joined-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-begin-run-2p-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-begin-run-4p-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-begin-run-5p-v1.json", StringComparison.Ordinal));
        Assert.Contains(jsonFiles, static path => path.EndsWith("tail-full-begin-run-8p-v1.json", StringComparison.Ordinal));

        foreach (string jsonFile in jsonFiles)
        {
            VerifyFixture(jsonFile, fixtureRoot);
        }
    }

    private static void VerifyFixture(string jsonFile, string fixtureRoot)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonFile));
        JsonElement root = document.RootElement;
        Assert.Equal("sts2-v0.111.0-netmessagebus-native-full-v2", root.GetProperty("schema").GetString());
        string file = root.GetProperty("file").GetString()!;
        byte[] vanilla = File.ReadAllBytes(Path.Combine(fixtureRoot, file));

        VerifyFixtureProvenance(root.GetProperty("fixtureProvenance"));
        Assert.Equal(root.GetProperty("totalBytes").GetInt32(), vanilla.Length);
        Assert.Equal(root.GetProperty("sha256").GetString(), Sha256(vanilla));
        Assert.Equal(9, root.GetProperty("headerBytes").GetInt32());

        int vanillaBodyEndBit = root.GetProperty("vanillaBodyEndBit").GetInt32();
        int paddingBits = root.GetProperty("paddingBits").GetInt32();
        int containerStartBit = root.GetProperty("containerStartBit").GetInt32();
        Assert.InRange(vanillaBodyEndBit, 0, containerStartBit);
        Assert.InRange(paddingBits, 0, 7);
        Assert.Equal(0, containerStartBit % 8);
        Assert.Equal(paddingBits, containerStartBit - vanillaBodyEndBit);
        VerifyByteMapReview(root.GetProperty("byteMapReview"), root);

        // native_bus_v1：原版包不再追加尾部容器；containerStartByte 即原版包终点，
        // 容器哈希记录扩展帧承载的容器内容（其逐字节验证在 GdUnit 生产链测试中执行）。
        int containerStartByte = containerStartBit / 8;
        Assert.Equal(root.GetProperty("containerStartByte").GetInt32(), containerStartByte);
        Assert.Equal(containerStartByte, vanilla.Length);
        Assert.Equal(root.GetProperty("messageTypeId").GetInt32(), vanilla[0]);
        ulong senderPeerId = root.GetProperty("senderPeerId").GetUInt64();
        Assert.Equal(senderPeerId, BinaryPrimitives.ReadUInt64LittleEndian(vanilla.AsSpan(1, 8)));
        string containerSha256 = root.GetProperty("containerSha256").GetString();
        Assert.Matches("^[0-9a-f]{64}$", containerSha256);
        Assert.True(root.GetProperty("containerBytes").GetInt32() > 0);
        Assert.Equal(-1, IndexOf(vanilla, TailMagic));
        AssertPaddingBitsAreZero(vanilla, vanillaBodyEndBit, paddingBits);

        int kind = root.GetProperty("messageKindValue").GetInt32();
        Assert.Equal(root.GetProperty("messageKind").GetString(), MessageKindName(kind));
        Assert.NotNull(root.GetProperty("expected").GetProperty("payload").GetString());
    }

    private static void VerifyFixtureProvenance(JsonElement provenance)
    {
        Assert.True(provenance.GetProperty("frozen").GetBoolean());
        Assert.Equal(
            "captured-from-production-serialization-manually-byte-map-reviewed",
            provenance.GetProperty("authoringMethod").GetString());
        Assert.Equal(
            "not-independent-authoring",
            provenance.GetProperty("vanillaBodyAuthoring").GetString());
        Assert.Equal(
            "independent-test-parser-no-production-lan-codec-or-runtime",
            provenance.GetProperty("lanContainerReview").GetString());
        Assert.Equal(
            "fixture updates require independent construction or manual byte-level review; tests are read-only",
            provenance.GetProperty("updatePolicy").GetString());
    }

    private static void VerifyByteMapReview(JsonElement byteMap, JsonElement root)
    {
        JsonElement header = byteMap.GetProperty("packetHeader");
        Assert.Equal(0, header.GetProperty("messageTypeIdOffset").GetInt32());
        Assert.Equal("1..8", header.GetProperty("senderPeerIdLittleEndianByteRange").GetString());
        Assert.Equal(root.GetProperty("headerBytes").GetInt32(), header.GetProperty("headerBytes").GetInt32());

        JsonElement body = byteMap.GetProperty("vanillaBody");
        Assert.Equal(72, body.GetProperty("startBit").GetInt32());
        Assert.Equal(root.GetProperty("vanillaBodyEndBit").GetInt32(), body.GetProperty("endBit").GetInt32());
        Assert.Equal(
            "reviewed-by-offset-map-not-independently-constructed",
            body.GetProperty("reviewStatus").GetString());

        JsonElement tail = byteMap.GetProperty("standaloneTail");
        Assert.Equal(root.GetProperty("paddingBits").GetInt32(), tail.GetProperty("paddingBits").GetInt32());
        Assert.Equal(root.GetProperty("containerStartBit").GetInt32(), tail.GetProperty("containerStartBit").GetInt32());
        Assert.Equal(root.GetProperty("containerStartByte").GetInt32(), tail.GetProperty("containerStartByte").GetInt32());
        Assert.Equal(root.GetProperty("containerBytes").GetInt32(), tail.GetProperty("containerBytes").GetInt32());
        Assert.Equal(root.GetProperty("containerSha256").GetString(), tail.GetProperty("containerSha256").GetString());
    }

    private static void VerifyExpectedPayload(
        JsonElement expected,
        string payloadKind,
        int messageKind,
        ulong senderPeerId,
        IndependentTailEnvelope envelope)
    {
        IndependentTailEntry capabilities = RequireEntry(envelope, "lan.capabilities");
        IndependentTailEntry? roster = FindEntry(envelope, "lan.roster");
        IndependentTailEntry? rejection = FindEntry(envelope, "lan.rejection");

        switch (payloadKind)
        {
            case "peerOffer":
                Assert.True(IsRequest(messageKind));
                Assert.Equal(0, envelope.SessionProtocolVersion);
                Assert.Null(roster);
                Assert.Null(rejection);
                VerifyPeerOfferPayload(expected, capabilities.Payload);
                break;
            case "selection":
                Assert.False(IsRequest(messageKind));
                Assert.Null(roster);
                Assert.Null(rejection);
                VerifySessionSelectionPayload(envelope, capabilities.Payload);
                break;
            case "roster":
                Assert.False(IsRequest(messageKind));
                Assert.NotNull(roster);
                Assert.Null(rejection);
                VerifySessionSelectionPayload(envelope, capabilities.Payload);
                VerifyRosterPayload(expected, senderPeerId, roster!.Payload);
                break;
            case "rejection":
                Assert.False(IsRequest(messageKind));
                Assert.Null(roster);
                Assert.NotNull(rejection);
                VerifySessionSelectionPayload(envelope, capabilities.Payload);
                VerifyRejectionPayload(expected, rejection!.Payload);
                break;
            default:
                throw new InvalidDataException($"Unknown expected payload kind '{payloadKind}'.");
        }
    }

    private static IndependentTailEnvelope ParseTailContainer(byte[] container)
    {
        Assert.True(container.AsSpan(0, TailMagic.Length).SequenceEqual(TailMagic));
        Assert.Equal(1, container[8]);
        Assert.Equal(0, container[9]);
        uint declaredBodyLength = BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(10, 4));
        Assert.Equal(container.Length, checked((int)declaredBodyLength + TailMagic.Length));
        ushort sessionProtocolVersion = BinaryPrimitives.ReadUInt16BigEndian(container.AsSpan(14, 2));
        ushort entryCount = BinaryPrimitives.ReadUInt16BigEndian(container.AsSpan(16, 2));
        Assert.InRange(entryCount, 1, 32);

        int offset = 18;
        List<IndependentTailEntry> entries = new(entryCount);
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int index = 0; index < entryCount; index++)
        {
            int idLength = container[offset++];
            Assert.InRange(idLength, 1, 64);
            string id = StrictUtf8.GetString(container.AsSpan(offset, idLength));
            offset += idLength;
            Assert.True(ids.Add(id));
            Assert.Contains(id, new[] { "lan.capabilities", "lan.rejection", "lan.roster" });
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(container.AsSpan(offset, 2));
            offset += 2;
            byte flags = container[offset++];
            Assert.Equal(1, version);
            Assert.Equal(1, flags);
            uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(offset, 4));
            offset += 4;
            Assert.InRange(payloadLength, 0u, 64u * 1024u);
            byte[] payload = container.AsSpan(offset, checked((int)payloadLength)).ToArray();
            offset += payload.Length;
            entries.Add(new IndependentTailEntry(id, version, flags, payload));
        }

        Assert.Equal(container.Length, offset);
        Assert.Equal(
            entries.OrderBy(static entry => StrictUtf8.GetBytes(entry.Id), ByteArrayComparer.Instance).Select(static entry => entry.Id),
            entries.Select(static entry => entry.Id));
        return new IndependentTailEnvelope(sessionProtocolVersion, entries);
    }

    private static void VerifyPeerOfferPayload(JsonElement expected, byte[] payload)
    {
        Assert.InRange(payload.Length, 8, 40);
        Assert.Equal(1, payload[0]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(3, 2)));
        int versionLength = payload[5];
        Assert.Equal(payload.Length, 8 + versionLength);
        string clientVersion = StrictUtf8.GetString(payload.AsSpan(6, versionLength));
        Assert.Equal(expected.GetProperty("clientVersion").GetString(), clientVersion);
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(6 + versionLength, 2));
        Assert.Equal(expected.GetProperty("ritsuLibPresent").GetBoolean(), (flags & 1) != 0);
        Assert.Equal(expected.GetProperty("ritsuLibSidecarAvailable").GetBoolean(), (flags & 2) != 0);
        Assert.Equal(0, flags & ~3);
    }

    private static void VerifySessionSelectionPayload(IndependentTailEnvelope envelope, byte[] payload)
    {
        Assert.Equal(1, envelope.SessionProtocolVersion);
        Assert.Equal(6, payload.Length);
        Assert.Equal(2, payload[0]);
        Assert.Equal(envelope.SessionProtocolVersion, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1, 2)));
        Assert.Equal(1, payload[3]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(4, 2)));
    }

    private static void VerifyRosterPayload(JsonElement expected, ulong senderPeerId, byte[] payload)
    {
        Assert.True(payload.Length >= 15);
        Assert.Equal(1, payload[0]);
        Assert.Equal(1, payload[1]);
        Assert.Equal(senderPeerId, BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(2, 8)));
        uint revision = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(10, 4));
        Assert.True(revision > 0);
        if (expected.TryGetProperty("rosterRevision", out JsonElement revisionJson))
        {
            Assert.Equal(uint.Parse(revisionJson.GetRawText(), CultureInfo.InvariantCulture), revision);
        }

        int playerCount = payload[14];
        int[] expectedSlots = expected.GetProperty("slots").EnumerateArray().Select(static value => value.GetInt32()).ToArray();
        ulong[] expectedPlayerIds = expected.GetProperty("playerIds").EnumerateArray().Select(static value => value.GetUInt64()).ToArray();
        Assert.Equal(expectedSlots.Length, playerCount);
        Assert.Equal(expectedPlayerIds.Length, playerCount);

        int offset = 15;
        List<int> slots = [];
        List<ulong> playerIds = [];
        for (int index = 0; index < playerCount; index++)
        {
            ulong playerId = BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(offset, 8));
            offset += 8;
            int slot = payload[offset++];
            uint bitLength = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset, 4));
            offset += 4;
            int byteLength = checked((int)((bitLength + 7u) / 8u));
            Assert.True(bitLength > 0);
            Assert.InRange(byteLength, 1, 16 * 1024);
            byte[] vanillaPlayerCarrier = payload.AsSpan(offset, byteLength).ToArray();
            offset += byteLength;
            int usedBits = checked((int)(bitLength % 8u));
            if (usedBits != 0)
            {
                byte unusedMask = unchecked((byte)(0xff << usedBits));
                Assert.Equal(0, vanillaPlayerCarrier[^1] & unusedMask);
            }

            playerIds.Add(playerId);
            slots.Add(slot);
        }

        Assert.Equal(payload.Length, offset);
        Assert.Equal(expectedSlots, slots.ToArray());
        Assert.Equal(expectedPlayerIds, playerIds.ToArray());
        Assert.Equal(slots.Order().ToArray(), slots.ToArray());
        Assert.Equal(slots.Count, slots.Distinct().Count());
        Assert.Equal(playerIds.Count, playerIds.Distinct().Count());
    }

    private static void VerifyRejectionPayload(JsonElement expected, byte[] payload)
    {
        Assert.True(payload.Length >= 7);
        Assert.Equal(1, payload[0]);
        ushort reason = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1, 2));
        Assert.InRange(reason, (ushort)1, (ushort)RejectionCodes.Length);
        Assert.Equal(expected.GetProperty("code").GetString(), RejectionCodes[reason - 1]);

        int versionLength = payload[3];
        Assert.InRange(versionLength, 0, 32);
        string? requiredVersion = versionLength == 0 ? null : StrictUtf8.GetString(payload.AsSpan(4, versionLength));
        if (expected.TryGetProperty("requiredClientVersion", out JsonElement requiredVersionJson))
        {
            Assert.Equal(requiredVersionJson.GetString(), requiredVersion);
        }
        else
        {
            Assert.Null(requiredVersion);
        }

        byte presenceValue = payload[4 + versionLength];
        bool? presence = presenceValue switch
        {
            0 => null,
            1 => false,
            2 => true,
            _ => throw new InvalidDataException($"Unknown presence value {presenceValue}.")
        };
        if (expected.TryGetProperty("requiredRitsuLibPresent", out JsonElement presenceJson))
        {
            Assert.Equal(presenceJson.GetBoolean(), presence);
        }
        else
        {
            Assert.Null(presence);
        }

        int detailLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(5 + versionLength, 2));
        Assert.InRange(detailLength, 0, 512);
        Assert.Equal(payload.Length, 7 + versionLength + detailLength);
        string? detail = detailLength == 0 ? null : StrictUtf8.GetString(payload.AsSpan(7 + versionLength, detailLength));
        if (expected.TryGetProperty("detail", out JsonElement detailJson))
        {
            Assert.Equal(detailJson.GetString(), detail);
        }
        else
        {
            Assert.Null(detail);
        }
    }

    private static bool IsRequest(int messageKind) => messageKind is 2 or 4 or 6;

    private static string MessageKindName(int messageKind) => messageKind switch
    {
        1 => "InitialGameInfo",
        2 => "LobbyJoinRequest",
        3 => "LobbyJoinResponse",
        4 => "LoadJoinRequest",
        5 => "LoadJoinResponse",
        6 => "RejoinRequest",
        7 => "RejoinResponse",
        8 => "ConnectionFailed",
        9 => "PlayerJoined",
        10 => "LobbyBeginRun",
        _ => throw new InvalidDataException($"Unknown message kind {messageKind}.")
    };

    private static IndependentTailEntry RequireEntry(IndependentTailEnvelope envelope, string id) =>
        FindEntry(envelope, id) ?? throw new InvalidDataException($"Missing entry {id}.");

    private static IndependentTailEntry? FindEntry(IndependentTailEnvelope envelope, string id) =>
        envelope.Entries.SingleOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));

    private static void AssertPaddingBitsAreZero(byte[] fullMessage, int vanillaBodyEndBit, int paddingBits)
    {
        for (int index = 0; index < paddingBits; index++)
        {
            int bit = vanillaBodyEndBit + index;
            int value = (fullMessage[bit / 8] >> (bit % 8)) & 1;
            Assert.Equal(0, value);
        }
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

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record IndependentTailEnvelope(
        ushort SessionProtocolVersion,
        IReadOnlyList<IndependentTailEntry> Entries);

    private sealed record IndependentTailEntry(
        string Id,
        ushort Version,
        byte Flags,
        byte[] Payload);

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        internal static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            int length = Math.Min(x.Length, y.Length);
            for (int index = 0; index < length; index++)
            {
                int diff = x[index].CompareTo(y[index]);
                if (diff != 0)
                {
                    return diff;
                }
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
