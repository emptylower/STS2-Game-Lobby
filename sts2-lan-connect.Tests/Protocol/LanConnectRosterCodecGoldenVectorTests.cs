using System.Buffers.Binary;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectRosterCodecGoldenVectorTests
{
    [Fact]
    public void Writes_and_reads_the_reviewed_two_player_vector()
    {
        byte[] expected = ReadFixture("tail-roster-2p-v1.bin");
        LanConnectRosterSnapshot snapshot = Snapshot();

        Assert.Equal(expected, LanConnectRosterCodec.Encode(snapshot));
        LanConnectRosterSnapshot decoded = LanConnectRosterCodec.Decode(expected);
        Assert.Equal(0x0102030405060708UL, decoded.AuthorityPeerId);
        Assert.Equal(1u, decoded.RosterRevision);
        Assert.Equal([0, 7], decoded.Players.Select(player => (int)player.RealSlotId));
    }

    [Fact]
    public void Encoder_canonicalizes_slot_order_but_decoder_rejects_noncanonical_wire_order()
    {
        LanConnectRosterSnapshot source = Snapshot();
        LanConnectRosterSnapshot reverse = source with { Players = source.Players.Reverse().ToArray() };
        Assert.Equal(LanConnectRosterCodec.Encode(source), LanConnectRosterCodec.Encode(reverse));

        byte[] malformed = ReadFixture("tail-roster-2p-v1.bin");
        byte[] first = malformed.AsSpan(15, 14).ToArray();
        byte[] second = malformed.AsSpan(29, 14).ToArray();
        second.CopyTo(malformed, 15);
        first.CopyTo(malformed, 29);
        Assert.Throws<InvalidDataException>(() => LanConnectRosterCodec.Decode(malformed));
    }

    [Fact]
    public void Rejects_duplicate_identity_slot_invalid_revision_and_bad_unused_bits()
    {
        LanConnectRosterSnapshot source = Snapshot();
        LanConnectRosterPlayerCarrier first = source.Players[0];
        Assert.Throws<InvalidDataException>(() => LanConnectRosterCodec.Encode(
            source with { Players = [first, new(first.PlayerId, 1, 8, [0])] }));
        Assert.Throws<InvalidDataException>(() => LanConnectRosterCodec.Encode(
            source with { Players = [first, new(99, first.RealSlotId, 8, [0])] }));
        Assert.Throws<InvalidDataException>(() => LanConnectRosterCodec.Encode(
            source with { RosterRevision = 0 }));
        Assert.Throws<InvalidDataException>(() => LanConnectRosterCodec.Encode(
            source with { Players = [new(11, 0, 3, [0xfd]), source.Players[1]] }));
    }

    [Fact]
    public void Rejects_truncation_oversized_carrier_and_trailing_bytes()
    {
        byte[] fixture = ReadFixture("tail-roster-2p-v1.bin");
        for (int length = 0; length < fixture.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() => LanConnectRosterCodec.Decode(fixture.AsSpan(0, length)));
        }

        Assert.Throws<InvalidDataException>(() => LanConnectRosterCodec.Decode([.. fixture, 0]));
        Assert.Throws<InvalidDataException>(() => LanConnectRosterCodec.Encode(new LanConnectRosterSnapshot(
            1,
            1,
            [new(1, 0, 1, [0]), new(2, 1, 131_073, new byte[16_385])])));
    }

    [Fact]
    public void Authority_requires_transport_sender_and_current_host_to_match()
    {
        LanConnectRosterSnapshot snapshot = Snapshot();
        LanConnectRosterCodec.ValidateAuthority(snapshot, snapshot.AuthorityPeerId, snapshot.AuthorityPeerId);
        Assert.Throws<InvalidDataException>(() =>
            LanConnectRosterCodec.ValidateAuthority(snapshot, 9, snapshot.AuthorityPeerId));
    }

    private static LanConnectRosterSnapshot Snapshot() => new(
        0x0102030405060708UL,
        1,
        [new(11, 0, 3, [0x05]), new(22, 7, 8, [0xaa])]);

    private static byte[] ReadFixture(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllBytes(Path.Combine(
            directory?.FullName ?? throw new DirectoryNotFoundException(),
            "test-fixtures", "protocol", "v0.6", name));
    }
}
