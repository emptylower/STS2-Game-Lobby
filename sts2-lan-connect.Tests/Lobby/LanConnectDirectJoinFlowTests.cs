using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectDirectJoinFlowTests
{
    [Fact]
    public async Task Retries_timeout_once_with_the_same_identity()
    {
        List<ulong> identities = [];

        string result = await LanConnectDirectJoinFlow.ExecuteAttemptsAsync(
            11797420990750824289UL,
            (attempt, netId) =>
            {
                identities.Add(netId);
                return attempt == 1
                    ? Task.FromException<string>(new RetryableTestException())
                    : Task.FromResult("joined");
            },
            exception => exception is RetryableTestException,
            (_, _) => Task.CompletedTask);

        Assert.Equal("joined", result);
        Assert.Equal(
            [11797420990750824289UL, 11797420990750824289UL],
            identities);
    }

    [Fact]
    public async Task Does_not_retry_non_retryable_failure()
    {
        int attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LanConnectDirectJoinFlow.ExecuteAttemptsAsync<string>(
                42UL,
                (_, _) =>
                {
                    attempts++;
                    return Task.FromException<string>(new InvalidOperationException("NotInSaveGame"));
                },
                _ => false,
                (_, _) => Task.CompletedTask));

        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData("Timeout", true)]
    [InlineData("HandshakeTimeout", true)]
    [InlineData("UnknownNetworkError", true)]
    [InlineData("NotInSaveGame", false)]
    [InlineData("RunInProgress", false)]
    [InlineData("ModMismatch", false)]
    [InlineData("VersionMismatch", false)]
    public void Retry_policy_only_retries_transport_failures(string reason, bool expected)
    {
        Assert.Equal(expected, LanConnectDirectJoinFlow.IsRetryableReason(reason));
    }

    [Fact]
    public void Local_ritsulib_is_rejected_before_direct_transport()
    {
        LanConnectProtocolFailure? failure =
            LanConnectDirectJoinFlow.ValidateCompatOnlyPreTransport(ritsuLibPresent: true);

        Assert.NotNull(failure);
        Assert.Equal("ritsulib_not_allowed_in_compat_mode", failure.Code);
    }

    [Fact]
    public void No_ritsulib_passes_direct_pre_transport_guard()
    {
        Assert.Null(LanConnectDirectJoinFlow.ValidateCompatOnlyPreTransport(ritsuLibPresent: false));
    }

    private sealed class RetryableTestException : Exception;
}
