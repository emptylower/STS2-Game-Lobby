using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyPlayerNameSyncTests
{
    [Fact]
    public void Attach_with_a_preconnected_control_client_resends_player_name_sync()
    {
        Assert.True(LanConnectLobbyRuntime.ShouldResendPlayerNameSyncAfterAttach(new LobbyControlClient()));
    }

    [Fact]
    public void Attach_without_a_preconnected_control_client_keeps_the_legacy_connect_path()
    {
        Assert.False(LanConnectLobbyRuntime.ShouldResendPlayerNameSyncAfterAttach(null));
    }

    [Fact]
    public void Host_name_seed_is_built_from_the_join_response_under_the_enet_host_net_id()
    {
        LanConnectLobbyRuntime.JoinedRoomHostNameSeed? seed =
            LanConnectLobbyRuntime.BuildJoinedRoomHostNameSeed("房主小明");

        Assert.NotNull(seed);
        Assert.Equal(LanConnectConstants.EnetHostNetId, seed.Value.HostNetId);
        Assert.Equal("房主小明", seed.Value.HostPlayerName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Host_name_seed_is_skipped_when_the_join_response_has_no_host_name(string? hostPlayerName)
    {
        Assert.Null(LanConnectLobbyRuntime.BuildJoinedRoomHostNameSeed(hostPlayerName));
    }
}
