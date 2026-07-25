using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectClientIdentityTests
{
    [Fact]
    public void Reuses_valid_persisted_identity_without_generating()
    {
        int generated = 0;

        LanConnectClientIdentityResolution result = LanConnectClientIdentity.Resolve(
            " 11797420990750824289 ",
            () =>
            {
                generated++;
                return 42;
            });

        Assert.Equal(11797420990750824289UL, result.NetId);
        Assert.Equal("11797420990750824289", result.PersistedValue);
        Assert.False(result.Generated);
        Assert.Equal(0, generated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("not-a-number")]
    public void Generates_identity_for_missing_or_invalid_values(string? persisted)
    {
        LanConnectClientIdentityResolution result = LanConnectClientIdentity.Resolve(
            persisted,
            () => 4053194744260183570UL);

        Assert.Equal(4053194744260183570UL, result.NetId);
        Assert.Equal("4053194744260183570", result.PersistedValue);
        Assert.True(result.Generated);
    }

    [Fact]
    public void Rejects_generators_that_never_produce_a_valid_identity()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectClientIdentity.Resolve(string.Empty, () => 1UL));
    }
}
