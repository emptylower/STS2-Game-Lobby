using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectContinueRunProtocolSelectionTests
{
    [Fact]
    public void Persisted_tail_selection_is_reused_exactly()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.4", false, false);
        LanConnectProtocolSelection selection = TailSelection(ritsuLibPresent: false);

        LanConnectCreateRoomIntent intent = LanConnectHostFlow.ResolveExistingHostPublishIntent(
            offer,
            selection,
            fallbackMaxPlayers: 4);

        Assert.Equal(LanConnectProtocolProfile.TailV1, intent.Profile);
        Assert.Equal(8, intent.MaxPlayers);
        Assert.Same(offer, intent.Offer);
    }

    [Fact]
    public void Missing_legacy_selection_with_ritsulib_renegotiates_tail()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.4", true, true);

        LanConnectCreateRoomIntent intent = LanConnectHostFlow.ResolveExistingHostPublishIntent(
            offer,
            requiredSelection: null,
            fallbackMaxPlayers: 6);

        Assert.Equal(LanConnectProtocolProfile.TailV1, intent.Profile);
        Assert.Equal(6, intent.MaxPlayers);
    }

    [Fact]
    public void Missing_legacy_selection_without_ritsulib_renegotiates_compat()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.4", false, false);

        LanConnectCreateRoomIntent intent = LanConnectHostFlow.ResolveExistingHostPublishIntent(
            offer,
            requiredSelection: null,
            fallbackMaxPlayers: 4);

        Assert.Equal(LanConnectProtocolProfile.Compat4x5V1, intent.Profile);
        Assert.Equal(4, intent.MaxPlayers);
    }

    [Fact]
    public void Service_lowercasing_of_wire_cache_signature_is_tolerated_for_resume()
    {
        LanConnectProtocolSelection required = TailSelection(ritsuLibPresent: false) with
        {
            WireCacheSignature = "wcv1:AbC_-",
            CapabilityDigest = new string('a', 64)
        };
        LanConnectProtocolSelection server = required with
        {
            WireCacheSignature = "wcv1:abc_-",
            CapabilityDigest = new string('b', 64)
        };

        Assert.True(LanConnectHostFlow.ArePublishSelectionsEquivalent(required, server));
    }

    [Fact]
    public void Resume_still_rejects_material_protocol_changes()
    {
        LanConnectProtocolSelection required = TailSelection(ritsuLibPresent: false);

        Assert.False(LanConnectHostFlow.ArePublishSelectionsEquivalent(
            required,
            required with { MaxPlayers = 4 }));
        Assert.False(LanConnectHostFlow.ArePublishSelectionsEquivalent(
            required,
            required with { Carrier = LanConnectProtocolCarrier.RitsuLibSidecarV1 }));
    }

    private static LanConnectProtocolSelection TailSelection(bool ritsuLibPresent)
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            LanConnectConstants.TailLanProtocolVersion,
            ritsuLibPresent
                ? LanConnectProtocolCarrier.RitsuLibSidecarV1
                : LanConnectProtocolCarrier.StandaloneTailV1,
            "0.6.0-alpha.1",
            8,
            "0.111.0",
            null,
            ritsuLibPresent,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }
}
