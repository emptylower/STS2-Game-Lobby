using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectHostChannelsTests
{
    [Theory]
    [InlineData("lan", "lan")]
    [InlineData("LAN", "lan")]
    [InlineData("lobby", "lobby")]
    [InlineData("Lobby", "lobby")]
    [InlineData(null, "lobby")]
    [InlineData("", "lobby")]
    [InlineData("   ", "lobby")]
    [InlineData("steam", "lobby")]
    public void Resolve_maps_values_to_effective_channel(string? input, string expected)
    {
        Assert.Equal(expected, LanConnectHostChannels.Resolve(input));
    }

    [Theory]
    [InlineData("lan", true)]
    [InlineData("lobby", true)]
    [InlineData("", false)]
    [InlineData("steam", false)]
    public void IsValid_only_allows_lan_and_lobby(string input, bool expected)
    {
        Assert.Equal(expected, LanConnectHostChannels.IsValid(input));
    }

    [Fact]
    public void DecideContinueRunPublish_skips_lan_only()
    {
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.SkipLanOrigin,
            LanConnectContinueRunPublishDecision.Decide(LanConnectHostChannels.Lan));
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Publish,
            LanConnectContinueRunPublishDecision.Decide(LanConnectHostChannels.Lobby));
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Publish,
            LanConnectContinueRunPublishDecision.Decide(LanConnectHostChannels.Resolve(null)));
    }
}
