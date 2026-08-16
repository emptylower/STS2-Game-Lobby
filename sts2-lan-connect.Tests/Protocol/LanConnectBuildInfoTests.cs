using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectBuildInfoTests
{
    [Fact]
    public void Assembly_fallback_is_a_supported_semantic_client_version()
    {
        string version = LanConnectBuildInfo.GetModVersion();

        Assert.True(LanConnectClientVersion.TryParseSupported(version, out LanConnectClientVersion? parsed));
        Assert.Equal("0.6.0-alpha.3", parsed!.Canonical);
    }
}
