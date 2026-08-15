using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectProtocolProfileTests
{
    [Fact]
    public void Unknown_profile_never_falls_back()
    {
        LanConnectProtocolException exception = Assert.Throws<LanConnectProtocolException>(
            () => LanConnectProtocolProfileExtensions.ParseCanonical("unknown"));

        Assert.Equal("protocol_profile_unsupported", exception.Failure.Code);
    }

    [Fact]
    public void Extended_legacy_projection_maps_only_to_compat()
    {
        Assert.Equal(
            LanConnectProtocolProfile.Compat4x5V1,
            LanConnectProtocolProfileExtensions.ParseApiProjection(
                canonicalProfile: null,
                LanConnectProtocolProfiles.Extended8p,
                LanConnectClientApiGeneration.Compat0305));
    }

    [Fact]
    public void Legacy_4p_is_not_a_runtime_profile()
    {
        Assert.Throws<LanConnectProtocolException>(() =>
            LanConnectProtocolProfileExtensions.ParseApiProjection(
                canonicalProfile: null,
                LanConnectProtocolProfiles.Legacy4p,
                LanConnectClientApiGeneration.Compat0305));
    }

    [Fact]
    public void Canonical_generation_does_not_accept_legacy_profile_as_fallback()
    {
        Assert.Throws<LanConnectProtocolException>(() =>
            LanConnectProtocolProfileExtensions.ParseApiProjection(
                canonicalProfile: null,
                LanConnectProtocolProfiles.Extended8p,
                LanConnectClientApiGeneration.Canonical06Plus));
    }

    [Fact]
    public void Compat_rejects_local_ritsulib_before_selection()
    {
        LanConnectCreateRoomIntent intent = new(
            LanConnectProtocolProfile.Compat4x5V1,
            8,
            new LanConnectProtocolOffer(1, 1, "0.6.0-alpha.1", true, true));

        LanConnectProtocolException exception = Assert.Throws<LanConnectProtocolException>(intent.Validate);

        Assert.Equal("ritsulib_not_allowed_in_compat_mode", exception.Failure.Code);
    }
}
