using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectTailCodecGoldenVectorTests
{
    [Fact]
    public void Writes_the_reviewed_single_entry_vector()
    {
        byte[] expected = Fixture.ReadBytes("tail-envelope-capabilities-v1.bin");
        byte[] actual = LanConnectTailCodec.Encode(1, [Fixtures.CapabilitiesSelectionEntry]);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Fixture_sidecar_reconstructs_the_reviewed_binary()
    {
        byte[] expected = Fixture.ReadBytes("tail-envelope-capabilities-v1.bin");
        using JsonDocument document = JsonDocument.Parse(Fixture.ReadText("tail-envelope-capabilities-v1.json"));
        JsonElement root = document.RootElement;

        byte[] documented = root.GetProperty("fields")
            .EnumerateArray()
            .SelectMany(field => Convert.FromHexString(
                field.GetProperty("hex").GetString()!.Replace(" ", string.Empty, StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(48, root.GetProperty("totalBytes").GetInt32());
        Assert.Equal(40, root.GetProperty("containerByteLength").GetInt32());
        Assert.Equal(expected, documented);
        Assert.Equal(
            root.GetProperty("sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant());
    }

    [Fact]
    public void Reads_the_reviewed_single_entry_vector()
    {
        LanConnectTailEnvelope envelope = LanConnectTailCodec.Decode(
            Fixture.ReadBytes("tail-envelope-capabilities-v1.bin"));

        Assert.Equal(1, envelope.SessionProtocolVersion);
        LanConnectTailEntry entry = Assert.Single(envelope.Entries);
        Assert.Equal("lan.capabilities", entry.Id);
        Assert.Equal(1, entry.Version);
        Assert.True(entry.IsCritical);
        Assert.Equal(Fixtures.CapabilitiesSelectionEntry.Payload.ToArray(), entry.Payload.ToArray());
    }

    [Fact]
    public void Canonicalizes_out_of_order_entries_by_raw_utf8_id_bytes()
    {
        LanConnectTailEntry capabilities = Fixtures.CapabilitiesSelectionEntry;
        LanConnectTailEntry rejection = Fixtures.Critical("lan.rejection", [0x02]);
        LanConnectTailEntry roster = Fixtures.Critical("lan.roster", [0x03]);

        byte[] reverse = LanConnectTailCodec.Encode(1, [roster, rejection, capabilities]);
        byte[] canonical = LanConnectTailCodec.Encode(1, [capabilities, rejection, roster]);
        LanConnectTailEnvelope decoded = LanConnectTailCodec.Decode(reverse);

        Assert.Equal(canonical, reverse);
        Assert.Equal(
            ["lan.capabilities", "lan.rejection", "lan.roster"],
            decoded.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public void Uses_raw_utf8_order_when_utf16_ordinal_order_differs()
    {
        const string supplementary = "vendor.\U00010000";
        const string privateUseBmp = "vendor.\ue000";
        Assert.True(StringComparer.Ordinal.Compare(supplementary, privateUseBmp) < 0);

        byte[] encoded = LanConnectTailCodec.Encode(
            1,
            [Fixtures.NonCritical(supplementary, []), Fixtures.NonCritical(privateUseBmp, [])]);
        int firstIdLength = encoded[18];
        string firstId = Encoding.UTF8.GetString(encoded, 19, firstIdLength);

        Assert.Equal(privateUseBmp, firstId);
    }

    [Fact]
    public void Rejects_duplicate_ids_on_encode_and_decode()
    {
        Assert.Throws<InvalidDataException>(() => LanConnectTailCodec.Encode(
            1,
            [Fixtures.CapabilitiesSelectionEntry, Fixtures.CapabilitiesSelectionEntry]));

        Assert.Throws<InvalidDataException>(() => LanConnectTailCodec.Decode(DuplicateFixtureEntry()));
    }

    [Fact]
    public void Rejects_bad_magic_container_version_and_container_flags()
    {
        AssertInvalidMutation(bytes => bytes[0] ^= 0xff);
        AssertInvalidMutation(bytes => bytes[8] = 2);
        AssertInvalidMutation(bytes => bytes[9] = 1);
    }

    [Fact]
    public void Rejects_bad_reserved_entry_version_and_flags()
    {
        AssertInvalidMutation(bytes => bytes[36] = 2);
        AssertInvalidMutation(bytes => bytes[37] = 0);
        AssertInvalidMutation(bytes => bytes[37] = 2);
    }

    [Fact]
    public void Rejects_truncation_at_every_byte_boundary()
    {
        byte[] valid = Fixture.ReadBytes("tail-envelope-capabilities-v1.bin");
        for (int length = 0; length < valid.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() => LanConnectTailCodec.Decode(valid.AsSpan(0, length)));
        }
    }

    [Fact]
    public void Rejects_declared_length_overflow_truncation_and_trailing_bytes()
    {
        AssertInvalidMutation(bytes => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(10, 4), uint.MaxValue));
        AssertInvalidMutation(bytes => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(10, 4), 39));
        AssertInvalidMutation(bytes => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(10, 4), 41));

        byte[] trailing = [.. Fixture.ReadBytes("tail-envelope-capabilities-v1.bin"), 0x00];
        Assert.Throws<InvalidDataException>(() => LanConnectTailCodec.Decode(trailing));
    }

    [Fact]
    public void Rejects_oversized_actual_container_count_id_and_payload()
    {
        Assert.Throws<InvalidDataException>(() =>
            LanConnectTailCodec.Decode(new byte[LanConnectTailCodec.MaxContainerBytes + 1]));
        AssertInvalidMutation(bytes => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16, 2), 33));
        AssertInvalidMutation(bytes => bytes[18] = 65);
        AssertInvalidMutation(bytes => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(38, 4), 65_537));
    }

    [Fact]
    public void Rejects_invalid_utf8_empty_and_oversized_ids()
    {
        Assert.Throws<InvalidDataException>(() =>
            LanConnectTailCodec.Encode(1, [Fixtures.NonCritical(string.Empty, [])]));
        Assert.Throws<InvalidDataException>(() =>
            LanConnectTailCodec.Encode(1, [Fixtures.NonCritical(new string('x', 65), [])]));
        Assert.Throws<InvalidDataException>(() =>
            LanConnectTailCodec.Encode(1, [Fixtures.NonCritical("\ud800", [])]));
        AssertInvalidMutation(bytes => bytes[19] = 0xff);
    }

    [Fact]
    public void Rejects_too_many_entries_and_oversized_payloads()
    {
        LanConnectTailEntry[] tooMany = Enumerable.Range(0, 33)
            .Select(index => Fixtures.NonCritical($"vendor.{index:D2}", []))
            .ToArray();
        Assert.Throws<InvalidDataException>(() => LanConnectTailCodec.Encode(1, tooMany));

        Assert.Throws<InvalidDataException>(() => LanConnectTailCodec.Encode(
            1,
            [Fixtures.NonCritical("vendor.large", new byte[LanConnectTailCodec.MaxEntryPayloadBytes + 1])]));
    }

    [Fact]
    public void Rejects_a_container_that_exceeds_the_total_limit()
    {
        byte[] maximumPayload = new byte[LanConnectTailCodec.MaxEntryPayloadBytes];
        LanConnectTailEntry[] entries =
        [
            Fixtures.Critical("lan.capabilities", maximumPayload),
            Fixtures.Critical("lan.rejection", maximumPayload),
            Fixtures.Critical("lan.roster", maximumPayload),
            Fixtures.NonCritical("vendor.future", maximumPayload)
        ];

        Assert.Throws<InvalidDataException>(() => LanConnectTailCodec.Encode(1, entries));
    }

    [Fact]
    public void Accepts_a_container_exactly_at_the_total_limit()
    {
        byte[] maximumPayload = new byte[LanConnectTailCodec.MaxEntryPayloadBytes];
        LanConnectTailEntry[] entries =
        [
            Fixtures.Critical("lan.capabilities", maximumPayload),
            Fixtures.Critical("lan.rejection", maximumPayload),
            Fixtures.Critical("lan.roster", maximumPayload),
            Fixtures.NonCritical("vendor.limit", new byte[65_435])
        ];

        byte[] encoded = LanConnectTailCodec.Encode(1, entries);

        Assert.Equal(LanConnectTailCodec.MaxContainerBytes, encoded.Length);
        Assert.Equal(3, LanConnectTailCodec.Decode(encoded).Entries.Count);
    }

    [Fact]
    public void Rejects_unknown_critical_entries_and_skips_unknown_noncritical_entries()
    {
        Assert.Throws<InvalidDataException>(() =>
            LanConnectTailCodec.Encode(1, [Fixtures.Critical("vendor.future", [0x01])]));

        AssertInvalidMutation(bytes => Encoding.ASCII.GetBytes("zzz.capabilities").CopyTo(bytes, 19));

        byte[] unknownNoncritical = Fixture.ReadBytes("tail-envelope-capabilities-v1.bin");
        Encoding.ASCII.GetBytes("zzz.capabilities").CopyTo(unknownNoncritical, 19);
        unknownNoncritical[37] = 0;
        LanConnectTailEnvelope decoded = LanConnectTailCodec.Decode(unknownNoncritical);
        Assert.Empty(decoded.Entries);

        byte[] encoded = LanConnectTailCodec.Encode(1, [Fixtures.NonCritical("vendor.future", [0x01])]);
        Assert.Empty(LanConnectTailCodec.Decode(encoded).Entries);
    }

    [Fact]
    public void Rejects_entry_bytes_not_declared_by_entry_count()
    {
        AssertInvalidMutation(bytes => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16, 2), 0));
    }

    [Fact]
    public void Entry_owns_a_copy_of_its_payload()
    {
        byte[] payload = [0x01];
        LanConnectTailEntry entry = Fixtures.NonCritical("vendor.future", payload);
        payload[0] = 0xff;

        Assert.Equal([0x01], entry.Payload.ToArray());
    }

    private static void AssertInvalidMutation(Action<byte[]> mutate)
    {
        byte[] bytes = Fixture.ReadBytes("tail-envelope-capabilities-v1.bin");
        mutate(bytes);
        Assert.Throws<InvalidDataException>(() => LanConnectTailCodec.Decode(bytes));
    }

    private static byte[] DuplicateFixtureEntry()
    {
        byte[] fixture = Fixture.ReadBytes("tail-envelope-capabilities-v1.bin");
        ReadOnlySpan<byte> entry = fixture.AsSpan(18);
        byte[] duplicate = new byte[18 + (entry.Length * 2)];
        fixture.AsSpan(0, 18).CopyTo(duplicate);
        BinaryPrimitives.WriteUInt32BigEndian(duplicate.AsSpan(10, 4), checked((uint)(duplicate.Length - 8)));
        BinaryPrimitives.WriteUInt16BigEndian(duplicate.AsSpan(16, 2), 2);
        entry.CopyTo(duplicate.AsSpan(18));
        entry.CopyTo(duplicate.AsSpan(18 + entry.Length));
        return duplicate;
    }

    private static class Fixtures
    {
        internal static readonly LanConnectTailEntry CapabilitiesSelectionEntry = new(
            "lan.capabilities",
            version: 1,
            isCritical: true,
            payload: [0x02, 0x00, 0x01, 0x01, 0x00, 0x00]);

        internal static LanConnectTailEntry Critical(string id, byte[] payload) =>
            new(id, version: 1, isCritical: true, payload);

        internal static LanConnectTailEntry NonCritical(string id, byte[] payload) =>
            new(id, version: 1, isCritical: false, payload);
    }

    private static class Fixture
    {
        internal static byte[] ReadBytes(string fileName) => File.ReadAllBytes(Path.Combine(
            FindRepositoryRoot(),
            "test-fixtures",
            "protocol",
            "v0.6",
            fileName));

        internal static string ReadText(string fileName) => File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "test-fixtures",
            "protocol",
            "v0.6",
            fileName));

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
    }
}
