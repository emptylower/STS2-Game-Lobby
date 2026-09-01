using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectCompatFixtureTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Current_compat_rooms_always_use_four_and_five_bit_widths(int maxPlayers)
    {
        LanConnectSessionProtocolState state = new();
        LanConnectProtocolSelection selection = LanConnectProtocolSelection.CreateLocalCompat(
            maxPlayers,
            "0.110.1",
            wireCacheSignature: null);

        using LanConnectSessionProtocolLease lease = state.FreezeHost(selection, $"compat-{maxPlayers}");
        using LanConnectSessionProtocolLease sharedLease =
            LanConnectSessionProtocolState.Shared.FreezeHost(selection, $"shared-compat-{maxPlayers}");

        Assert.Equal(LanConnectConstants.ExtendedSlotIdBits, LanConnectProtocolProfiles.GetActiveSlotIdBitWidth());
        Assert.Equal(LanConnectConstants.ExtendedLobbyListBits, LanConnectProtocolProfiles.GetActiveLobbyListBitWidth());
        Assert.Equal(4, LanConnectConstants.ExtendedSlotIdBits);
        Assert.Equal(5, LanConnectConstants.ExtendedLobbyListBits);
    }

    [Fact]
    public void Tail_rooms_keep_the_original_two_and_three_bit_projection()
    {
        LanConnectProtocolSelection selection = TailSelection();
        using LanConnectSessionProtocolLease lease =
            LanConnectSessionProtocolState.Shared.FreezeHost(selection, "shared-tail");

        Assert.Equal(LanConnectConstants.VanillaSlotIdBits, LanConnectProtocolProfiles.GetActiveSlotIdBitWidth());
        Assert.Equal(LanConnectConstants.VanillaLobbyListBits, LanConnectProtocolProfiles.GetActiveLobbyListBitWidth());
        Assert.Equal(2, LanConnectConstants.VanillaSlotIdBits);
        Assert.Equal(3, LanConnectConstants.VanillaLobbyListBits);
    }

    [Fact]
    public void Legacy_api_projection_maps_only_extended_8p_to_compat()
    {
        Assert.Equal(
            LanConnectProtocolProfile.Compat4x5V1,
            LanConnectProtocolProfileExtensions.ParseApiProjection(
                canonicalProfile: null,
                legacyProfile: LanConnectProtocolProfiles.Extended8p,
                LanConnectClientApiGeneration.Compat0305));

        Assert.Throws<LanConnectProtocolException>(() =>
            LanConnectProtocolProfileExtensions.ParseApiProjection(
                canonicalProfile: null,
                legacyProfile: LanConnectProtocolProfiles.Legacy4p,
                LanConnectClientApiGeneration.Compat0305));
    }

    private static LanConnectProtocolSelection TailSelection()
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            LanConnectConstants.TailLanProtocolVersion,
            LanConnectProtocolCarrier.LegacyTailV1,
            "0.6.0-alpha.1",
            8,
            "0.110.1",
            null,
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }
}
