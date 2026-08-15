using System.Buffers.Binary;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectCapabilitiesCodecTests
{
    [Fact]
    public void Peer_offer_round_trips_only_at_session_version_zero()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.1", false, false);
        byte[] payload = LanConnectCapabilitiesCodec.EncodePeerOffer(offer);

        Assert.Equal(offer, LanConnectCapabilitiesCodec.DecodePeerOffer(payload, 0));
        Assert.Throws<InvalidDataException>(() => LanConnectCapabilitiesCodec.DecodePeerOffer(payload, 1));
    }

    [Fact]
    public void Ritsu_offer_requires_presence_and_public_sidecar_readiness_together()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.1", true, true);
        byte[] payload = LanConnectCapabilitiesCodec.EncodePeerOffer(offer);
        Assert.Equal(offer, LanConnectCapabilitiesCodec.DecodePeerOffer(payload, 0));

        payload[^1] = 1;
        Assert.Throws<InvalidDataException>(() => LanConnectCapabilitiesCodec.DecodePeerOffer(payload, 0));
    }

    [Fact]
    public void Session_selection_round_trips_and_matches_the_frozen_http_selection()
    {
        LanConnectProtocolSelection selection = TailSelection(false);
        byte[] payload = LanConnectCapabilitiesCodec.EncodeSessionSelection(selection);
        LanConnectCapabilitiesSelection decoded = LanConnectCapabilitiesCodec.DecodeSessionSelection(payload, 1);

        LanConnectCapabilitiesCodec.ValidateMatches(decoded, selection);
        Assert.Equal(LanConnectProtocolCarrier.StandaloneTailV1, decoded.Carrier);
        Assert.False(decoded.RitsuLibPresent);
    }

    [Fact]
    public void Selection_rejects_version_carrier_presence_and_unknown_flag_drift()
    {
        byte[] payload = LanConnectCapabilitiesCodec.EncodeSessionSelection(TailSelection(false));
        Assert.Throws<InvalidDataException>(() => LanConnectCapabilitiesCodec.DecodeSessionSelection(payload, 0));
        Assert.Throws<InvalidDataException>(() => LanConnectCapabilitiesCodec.DecodeSessionSelection(payload, 2));

        byte[] badCarrier = payload.ToArray();
        badCarrier[3] = 2;
        Assert.Throws<InvalidDataException>(() => LanConnectCapabilitiesCodec.DecodeSessionSelection(badCarrier, 1));

        byte[] badFlags = payload.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(badFlags.AsSpan(4, 2), 2);
        Assert.Throws<InvalidDataException>(() => LanConnectCapabilitiesCodec.DecodeSessionSelection(badFlags, 1));
    }

    [Fact]
    public void Capabilities_payloads_reject_trailing_bytes_wrong_kind_and_noncanonical_versions()
    {
        byte[] offer = LanConnectCapabilitiesCodec.EncodePeerOffer(new(1, 1, "0.6.0", false, false));
        Assert.Throws<InvalidDataException>(() =>
            LanConnectCapabilitiesCodec.DecodePeerOffer([.. offer, 0], 0));
        offer[0] = 2;
        Assert.Throws<InvalidDataException>(() => LanConnectCapabilitiesCodec.DecodePeerOffer(offer, 0));
        Assert.Throws<LanConnectProtocolException>(() => LanConnectCapabilitiesCodec.EncodePeerOffer(
            new LanConnectProtocolOffer(1, 1, "00.6.0", false, false)));
    }

    private static LanConnectProtocolSelection TailSelection(bool ritsu)
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            1,
            ritsu ? LanConnectProtocolCarrier.RitsuLibSidecarV1 : LanConnectProtocolCarrier.StandaloneTailV1,
            "0.6.0-alpha.1",
            8,
            "0.110.1",
            null,
            ritsu,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }
}
