using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyControlUriTests
{
    [Fact]
    public void Tail_host_control_uri_carries_the_frozen_protocol_identity()
    {
        using LobbyApiClient api = new("https://lobby.example/", diagnosticSink: static _ => { });

        Uri uri = api.BuildHostControlUri(
            "channel id",
            "room/id",
            "host+token",
            "0.6.0-alpha.6",
            "sha256:AbC+/=");

        Assert.Equal(
            "wss://lobby.example/control?controlChannelId=channel%20id&role=host&roomId=room%2Fid" +
            "&token=host%2Btoken&clientVersion=0.6.0-alpha.6&capabilityDigest=sha256%3AAbC%2B%2F%3D",
            uri.AbsoluteUri);
    }

    [Fact]
    public void Compat_host_control_uri_omits_optional_protocol_identity()
    {
        using LobbyApiClient api = new("http://127.0.0.1:8788/", diagnosticSink: static _ => { });

        Uri uri = api.BuildHostControlUri("channel", "room", "token");

        Assert.Equal(
            "ws://127.0.0.1:8788/control?controlChannelId=channel&role=host&roomId=room&token=token",
            uri.AbsoluteUri);
        Assert.DoesNotContain("clientVersion", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("capabilityDigest", uri.Query, StringComparison.Ordinal);
    }
}
