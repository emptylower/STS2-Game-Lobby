using Sts2LanConnect.Scripts;
using System.Text.Json;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectServerListBootstrapTests
{
    [Fact]
    public void Featured_test_server_is_added_when_discovery_is_empty()
    {
        List<ServerListEntry> entries = [];

        LanConnectServerListBootstrap.EnsureFeaturedServer(entries);

        ServerListEntry entry = Assert.Single(entries);
        Assert.Equal(LanConnectServerListBootstrap.FeaturedServerAddress, entry.Address);
        Assert.True(entry.IsPinned);
    }

    [Fact]
    public void Featured_test_server_is_inserted_once_and_pinned()
    {
        List<ServerListEntry> entries =
        [
            new()
            {
                Address = "http://101.35.217.99:8788/",
                DisplayName = "测试节点",
                Source = "cf"
            }
        ];

        LanConnectServerListBootstrap.EnsureFeaturedServer(entries);

        ServerListEntry entry = Assert.Single(entries);
        Assert.Equal(LanConnectServerListBootstrap.FeaturedServerAddress, entry.Address);
        Assert.Equal("测试节点", entry.DisplayName);
        Assert.True(entry.IsPinned);
    }

    [Fact]
    public void Featured_test_server_sorts_before_recent_and_low_latency_servers()
    {
        List<ServerListEntry> entries =
        [
            new()
            {
                Address = "https://recent.example",
                LastSuccessConnect = DateTime.UtcNow,
                Bucket = PingBucket.Low,
                PingMs = 1
            },
            new()
            {
                Address = LanConnectServerListBootstrap.FeaturedServerAddress,
                IsPinned = true,
                Bucket = PingBucket.High,
                PingMs = 2000
            }
        ];

        List<ServerListEntry> ordered = LanConnectServerListBootstrap.OrderForDisplay(entries).ToList();

        Assert.Equal(LanConnectServerListBootstrap.FeaturedServerAddress, ordered[0].Address);
    }

    [Fact]
    public void Live_metrics_enable_the_0_5_1_plus_badge_only_for_supported_servers()
    {
        ServerListEntry entry = new() { Address = "https://lobby.example" };

        LanConnectServerListBootstrap.ApplyMetrics(entry, new PeerMetricsResponse
        {
            ModSyncProtocolVersion = 1,
            ModSyncEnabled = true,
            ModSyncMinimumClientVersion = "0.5.1"
        });
        Assert.True(entry.SupportsModSyncV051Plus);

        LanConnectServerListBootstrap.ApplyMetrics(entry, new PeerMetricsResponse
        {
            ModSyncProtocolVersion = 1,
            ModSyncEnabled = false,
            ModSyncMinimumClientVersion = "0.5.1"
        });
        Assert.False(entry.SupportsModSyncV051Plus);
    }

    [Fact]
    public void Peer_metrics_deserializes_the_server_mod_sync_capability_contract()
    {
        PeerMetricsResponse? metrics = JsonSerializer.Deserialize<PeerMetricsResponse>(
            """{"modSyncProtocolVersion":1,"modSyncEnabled":true,"modSyncMinimumClientVersion":"0.5.1"}""",
            LanConnectJson.Options);

        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.ModSyncProtocolVersion);
        Assert.True(metrics.ModSyncEnabled);
        Assert.Equal("0.5.1", metrics.ModSyncMinimumClientVersion);
    }
}
