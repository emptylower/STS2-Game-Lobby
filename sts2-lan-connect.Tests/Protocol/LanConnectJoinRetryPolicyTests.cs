using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectJoinRetryPolicyTests
{
    [Theory]
    [InlineData("Timeout", true)]
    [InlineData("HandshakeTimeout", true)]
    [InlineData("UnknownNetworkError", true)]
    [InlineData("ModMismatch", false)]
    [InlineData("VersionMismatch", false)]
    [InlineData("RunInProgress", false)]
    [InlineData("NotInSaveGame", false)]
    [InlineData("InternalError", false)]
    public void Only_transport_timeouts_and_unknown_network_are_retryable(string reason, bool expected)
    {
        Assert.Equal(expected, LanConnectJoinRetryPolicy.IsRetryableReason(reason));
    }

    [Fact]
    public void Protocol_exception_is_never_retryable()
    {
        Assert.False(LanConnectJoinRetryPolicy.IsRetryable(
            new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibPresenceMismatch(true))));
    }
}
