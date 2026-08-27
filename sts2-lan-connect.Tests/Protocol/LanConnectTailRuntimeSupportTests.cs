using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectTailRuntimeSupportTests : IDisposable
{
    public LanConnectTailRuntimeSupportTests()
    {
        LanConnectTailRuntimeSupport.ResetForTesting();
    }

    public void Dispose()
    {
        LanConnectTailRuntimeSupport.ResetForTesting();
    }

    [Fact]
    public void Probe_reports_unavailable_for_assembly_without_tail_message_kinds()
    {
        LanConnectTailRuntimeSupportResult result =
            LanConnectTailRuntimeSupport.Probe(typeof(object).Assembly);

        Assert.False(result.Available);
        Assert.NotNull(result.UnavailableReason);
        Assert.Contains("missing concrete type", result.UnavailableReason, StringComparison.Ordinal);
        Assert.Contains(
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.",
            result.UnavailableReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Current_honors_the_forced_test_override()
    {
        LanConnectTailRuntimeSupport.SetForTesting(new(false, "forced"));

        Assert.False(LanConnectTailRuntimeSupport.IsAvailable);
        Assert.Equal("forced", LanConnectTailRuntimeSupport.Current.UnavailableReason);
    }

    [Fact]
    public void Protocol_offer_degrades_to_zero_range_when_tail_runtime_is_unavailable()
    {
        LanConnectTailRuntimeSupport.SetForTesting(new(false, "probe_failed"));

        LanConnectProtocolOffer offer = LanConnectProtocolOffer.CreateCurrent();

        Assert.Equal(0, offer.LanProtocolMin);
        Assert.Equal(0, offer.LanProtocolMax);
        Assert.False(offer.Supports(LanConnectConstants.TailLanProtocolVersion));
        Assert.Same(offer, offer.Validate());
    }

    [Fact]
    public void Protocol_offer_keeps_tail_range_when_tail_runtime_is_available()
    {
        LanConnectTailRuntimeSupport.SetForTesting(LanConnectTailRuntimeSupportResult.Supported);

        LanConnectProtocolOffer offer = LanConnectProtocolOffer.CreateCurrent();

        Assert.Equal(LanConnectConstants.TailLanProtocolVersion, offer.LanProtocolMin);
        Assert.Equal(LanConnectConstants.TailLanProtocolVersion, offer.LanProtocolMax);
        Assert.True(offer.Supports(LanConnectConstants.TailLanProtocolVersion));
    }

    [Fact]
    public void Tail_create_option_is_unselectable_with_zeroed_offer_and_selectable_with_tail_offer()
    {
        LanConnectProtocolOffer zeroed = new(0, 0, "0.6.0", false, false);
        LanConnectProtocolOffer tailOffer = new(1, 1, "0.6.0", false, false);

        Assert.False(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(301, zeroed, true));
        Assert.True(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(300, zeroed, true));
        Assert.Equal(300, LanConnectLobbyOverlay.GetDefaultCreateProtocolIdForTests(zeroed, true));

        Assert.True(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(301, tailOffer, true));
        Assert.Equal(
            300,
            LanConnectLobbyOverlay.GetDefaultCreateProtocolIdForTests(tailOffer, true));
    }

    [Fact]
    public void Tail_room_intent_validation_fails_closed_when_offer_lacks_tail_protocol()
    {
        LanConnectProtocolOffer zeroed = new(0, 0, "0.6.0", false, false);

        LanConnectProtocolException exception = Assert.Throws<LanConnectProtocolException>(() =>
            new LanConnectCreateRoomIntent(
                LanConnectProtocolProfile.TailV1,
                4,
                zeroed).Validate());

        Assert.Equal("lan_protocol_version_mismatch", exception.Failure.Code);
        Assert.Contains(
            "Tail runtime is unavailable on this game version.",
            exception.Failure.Detail ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Compat_room_intent_still_validates_with_zeroed_offer()
    {
        LanConnectProtocolOffer zeroed = new(0, 0, "0.6.0", false, false);

        LanConnectCreateRoomIntent validated = new LanConnectCreateRoomIntent(
            LanConnectProtocolProfile.Compat4x5V1,
            4,
            zeroed).Validate();

        Assert.Equal(LanConnectProtocolProfile.Compat4x5V1, validated.Profile);
    }
}
