using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectHostChannelBindingTests
{
    [Fact]
    public void SavedRoomBinding_host_channel_defaults_empty()
    {
        var b = new LanConnectSavedRoomBinding();
        Assert.Equal(string.Empty, b.HostChannel);
        Assert.Equal(LanConnectHostChannels.Lobby, LanConnectHostChannels.Resolve(b.HostChannel));
    }

    [Fact]
    public void ResolvedRoomBinding_effective_host_channel_defaults_to_lobby()
    {
        var resolved = new LanConnectResolvedRoomBinding
        {
            HostChannel = string.Empty
        };
        Assert.Equal(string.Empty, resolved.HostChannel);
        Assert.Equal(LanConnectHostChannels.Lobby, resolved.EffectiveHostChannel);
    }

    [Theory]
    [InlineData("lan", "lan")]
    [InlineData("lobby", "lobby")]
    [InlineData("", "lobby")]
    [InlineData("LAN", "lan")]
    public void ResolvedRoomBinding_effective_host_channel_resolves(string stored, string expected)
    {
        var resolved = new LanConnectResolvedRoomBinding
        {
            HostChannel = stored
        };
        Assert.Equal(expected, resolved.EffectiveHostChannel);
    }
}
