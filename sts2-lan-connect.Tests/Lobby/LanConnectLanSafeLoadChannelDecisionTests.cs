using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLanSafeLoadChannelDecisionTests
{
    [Theory]
    [InlineData("lobby")]
    [InlineData("Lobby")]
    [InlineData(" LOBBY ")]
    public void Lobby_bound_saves_keep_their_binding(string persistedChannel)
    {
        Assert.Equal(
            LanConnectLanSafeLoadChannelActionKind.KeepBinding,
            LanConnectLanSafeLoadChannelDecision.Decide(persistedChannel));
    }

    [Theory]
    [InlineData("lan")]
    [InlineData("LAN")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("steam")]
    public void Non_lobby_channels_migrate_to_lan(string? persistedChannel)
    {
        Assert.Equal(
            LanConnectLanSafeLoadChannelActionKind.MigrateToLan,
            LanConnectLanSafeLoadChannelDecision.Decide(persistedChannel));
    }
}
