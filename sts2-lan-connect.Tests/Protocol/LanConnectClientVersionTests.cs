using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectClientVersionTests
{
    [Theory]
    [InlineData("0.2.2", false)]
    [InlineData("0.3.0", true)]
    [InlineData("0.5.5", true)]
    [InlineData("0.6.0-alpha.1", true)]
    [InlineData("1.0.0", true)]
    [InlineData("0.06.0", false)]
    [InlineData("v0.6.0", false)]
    [InlineData("0.6.0+build", false)]
    public void Parses_supported_client_generations(string value, bool supported)
    {
        Assert.Equal(supported, LanConnectClientVersion.TryParseSupported(value, out _));
    }

    [Fact]
    public void Preserves_canonical_prerelease()
    {
        LanConnectClientVersion version = LanConnectClientVersion.ParseSupported("0.6.0-alpha.1");

        Assert.Equal("0.6.0-alpha.1", version.Canonical);
        Assert.Equal(LanConnectClientApiGeneration.Canonical06Plus, version.Generation);
    }
}
