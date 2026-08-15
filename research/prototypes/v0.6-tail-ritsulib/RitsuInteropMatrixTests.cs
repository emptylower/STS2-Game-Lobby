using System.Threading.Tasks;
using Xunit;

namespace Sts2TailPrototype;

public sealed class RitsuInteropMatrixTests
{
    [Fact]
    public void Standalone_carrier_round_trips_the_frozen_container()
    {
        CarrierResult result = RitsuInteropHarness.RoundTripStandalone();
        Assert.Equal(InteropFixtures.ExpectedContainer, result.ContainerBytes);
        Assert.InRange(result.ContainerStartBit - result.VanillaBodyEndBit, 0, 7);
        Assert.Equal(0, result.ContainerStartBit % 8);
        Assert.Equal(288, result.ContainerEndBit - result.ContainerStartBit);
        Assert.True(result.AlignmentPaddingWasZero);
    }

    [Fact]
    public async Task Ritsu_sidecar_pairs_before_vanilla_handler()
    {
        SidecarCarrierResult result = await RitsuInteropHarness.RunRealTwoProcessSidecarAsync();
        Assert.Equal(InteropFixtures.ExpectedContainer, result.ContainerBytes);
        Assert.True(result.TrustedTicketHintBootstrappedReachability);
        Assert.True(result.SidecarReachableBeforeFirstLanFlow);
        Assert.True(result.HandlerBlockedUntilPairValidated);
        Assert.True(result.VanillaBytesMatchFixture);
        Assert.False(result.StandaloneTailPresent);
        Assert.True(result.HintClearedOnTeardown);
        Assert.True(result.ReusedPeerIdStartsUnknown);
    }
}
