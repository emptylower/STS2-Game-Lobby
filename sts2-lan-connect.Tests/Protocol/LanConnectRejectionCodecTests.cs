using System.Buffers.Binary;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectRejectionCodecTests
{
    [Theory]
    [InlineData("client_update_required", 1)]
    [InlineData("protocol_profile_unsupported", 2)]
    [InlineData("ritsulib_not_allowed_in_compat_mode", 3)]
    [InlineData("ritsulib_presence_mismatch", 4)]
    [InlineData("game_version_mismatch", 5)]
    [InlineData("wire_cache_mismatch", 6)]
    [InlineData("lan_tail_required", 7)]
    [InlineData("lan_tail_malformed", 8)]
    [InlineData("lan_protocol_version_mismatch", 9)]
    [InlineData("ritsulib_sidecar_unavailable", 10)]
    public void Round_trips_all_fixed_reason_codes(string code, int reasonCode)
    {
        LanConnectProtocolFailure failure = new(code, "0.6.0-alpha.1", true, "detail");
        byte[] payload = LanConnectRejectionCodec.Encode(failure);

        Assert.Equal(reasonCode, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1, 2)));
        Assert.Equal(failure, LanConnectRejectionCodec.Decode(payload));
    }

    [Fact]
    public void Unknown_reason_is_preserved_as_terminal_generic_protocol_failure()
    {
        byte[] payload = LanConnectRejectionCodec.Encode(
            new LanConnectProtocolFailure("lan_tail_malformed"));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1, 2), 11);

        Assert.Equal("unknown_tail_rejection_11", LanConnectRejectionCodec.Decode(payload).Code);
    }

    [Fact]
    public void Rejects_unknown_presence_invalid_utf8_bounds_and_trailing_bytes()
    {
        byte[] payload = LanConnectRejectionCodec.Encode(
            new LanConnectProtocolFailure("lan_tail_malformed", null, null, "x"));
        payload[4] = 3;
        Assert.Throws<InvalidDataException>(() => LanConnectRejectionCodec.Decode(payload));

        byte[] invalidUtf8 = LanConnectRejectionCodec.Encode(
            new LanConnectProtocolFailure("lan_tail_malformed", null, null, "x"));
        invalidUtf8[^1] = 0xff;
        Assert.Throws<InvalidDataException>(() => LanConnectRejectionCodec.Decode(invalidUtf8));
        Assert.Throws<ArgumentException>(() => LanConnectRejectionCodec.Encode(
            new LanConnectProtocolFailure("lan_tail_malformed", Detail: new string('x', 513))));
        Assert.Throws<InvalidDataException>(() => LanConnectRejectionCodec.Decode([.. invalidUtf8, 0]));
    }
}
