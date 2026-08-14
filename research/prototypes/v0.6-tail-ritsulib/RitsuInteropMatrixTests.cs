using Xunit;

namespace Sts2TailPrototype;

public sealed class RitsuInteropMatrixTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Lan_tail_bytes_and_cursor_are_stable(bool senderHasRitsu, bool receiverHasRitsu)
    {
        InteropResult result = RitsuInteropHarness.RoundTrip(senderHasRitsu, receiverHasRitsu);
        Assert.Equal(InteropFixtures.ExpectedLanTail, result.LanTailBytes);
        Assert.Equal(result.LanTailEndBit, result.RitsuReadStartBit);
        Assert.Equal(InteropFixtures.ExpectedMessage, result.Message);
    }
}
