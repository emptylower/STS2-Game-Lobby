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
                ProbeState = ServerProbeState.Reachable,
                Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 6, "0.6.x（推断）"),
                Bucket = PingBucket.Low,
                PingMs = 1
            },
            new()
            {
                Address = LanConnectServerListBootstrap.FeaturedServerAddress,
                IsPinned = true,
                ProbeState = ServerProbeState.Unreachable,
                Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 4, "0.4.x 或更早（推断）"),
                Bucket = PingBucket.High,
                PingMs = 2000
            }
        ];

        List<ServerListEntry> ordered = LanConnectServerListBootstrap.OrderForDisplay(entries).ToList();

        Assert.Equal(LanConnectServerListBootstrap.FeaturedServerAddress, ordered[0].Address);
    }

    [Fact]
    public void OrderForDisplay_ranks_reachable_before_unreachable_and_version_tiers_descending()
    {
        List<ServerListEntry> entries =
        [
            new() { Address = "https://unknown.example", ProbeState = ServerProbeState.Reachable },
            new() { Address = "https://old-04.example", ProbeState = ServerProbeState.Reachable, Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 4, "0.4.x 或更早（推断）"), PingMs = 10 },
            new() { Address = "https://mid-05.example", ProbeState = ServerProbeState.Reachable, Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 5, "0.5.x（推断）"), PingMs = 10 },
            new() { Address = "https://new-06.example", ProbeState = ServerProbeState.Reachable, Version = new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.1"), PingMs = 10 },
            new() { Address = "https://dead-07.example", ProbeState = ServerProbeState.Unreachable, Version = new ServerVersionInfo(ServerVersionSource.Reported, 0, 7, "0.7.0") },
            new() { Address = "https://dead-04.example", ProbeState = ServerProbeState.Unreachable, Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 4, "0.4.x 或更早（推断）") },
            new() { Address = "https://pending.example", ProbeState = ServerProbeState.Pending },
        ];

        List<string> ordered = LanConnectServerListBootstrap.OrderForDisplay(entries)
            .Select(entry => entry.Address)
            .ToList();

        Assert.Equal(
        [
            "https://new-06.example",
            "https://mid-05.example",
            "https://old-04.example",
            "https://unknown.example",
            "https://dead-07.example",
            "https://dead-04.example",
            "https://pending.example",
        ], ordered);
    }

    [Fact]
    public void OrderForDisplay_breaks_version_tier_ties_by_exact_milliseconds_then_address()
    {
        List<ServerListEntry> entries =
        [
            new() { Address = "https://slow.example", ProbeState = ServerProbeState.Reachable, Version = new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.1"), PingMs = 42 },
            new() { Address = "https://alpha.example", ProbeState = ServerProbeState.Reachable, Version = new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.2-alpha.1"), PingMs = 41 },
            new() { Address = "https://fast-a.example", ProbeState = ServerProbeState.Reachable, Version = new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.2-alpha.1"), PingMs = 41 },
            new() { Address = "https://fast-b.example", ProbeState = ServerProbeState.Reachable, Version = new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.2-alpha.1"), PingMs = 41 },
        ];

        List<string> ordered = LanConnectServerListBootstrap.OrderForDisplay(entries)
            .Select(entry => entry.Address)
            .ToList();

        Assert.Equal(
        [
            "https://alpha.example",
            "https://fast-a.example",
            "https://fast-b.example",
            "https://slow.example",
        ], ordered);
    }

    [Fact]
    public void OrderForDisplay_groups_pending_with_unreachable_and_sorts_by_version_then_address()
    {
        List<ServerListEntry> entries =
        [
            new() { Address = "https://pending-b.example", ProbeState = ServerProbeState.Pending, Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 5, "0.5.x（推断）") },
            new() { Address = "https://pending-a.example", ProbeState = ServerProbeState.Pending },
            new() { Address = "https://dead-b.example", ProbeState = ServerProbeState.Unreachable, Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 5, "0.5.x（推断）") },
            new() { Address = "https://dead-a.example", ProbeState = ServerProbeState.Unreachable, Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 5, "0.5.x（推断）") },
            new() { Address = "https://dead-06.example", ProbeState = ServerProbeState.Unreachable, Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 6, "0.6.x（推断）") },
        ];

        List<string> ordered = LanConnectServerListBootstrap.OrderForDisplay(entries)
            .Select(entry => entry.Address)
            .ToList();

        Assert.Equal(
        [
            "https://dead-06.example",
            "https://dead-a.example",
            "https://dead-b.example",
            "https://pending-b.example",
            "https://pending-a.example",
        ], ordered);
    }

    [Fact]
    public void OrderForDisplay_ignores_ping_within_the_unreachable_group()
    {
        List<ServerListEntry> entries =
        [
            new() { Address = "https://z.example", ProbeState = ServerProbeState.Unreachable, Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 5, "0.5.x（推断）"), PingMs = 7 },
            new() { Address = "https://a.example", ProbeState = ServerProbeState.Unreachable, Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 5, "0.5.x（推断）"), PingMs = 42 },
        ];

        List<string> ordered = LanConnectServerListBootstrap.OrderForDisplay(entries)
            .Select(entry => entry.Address)
            .ToList();

        // Same tier, both unreachable, different PingMs — the latency key is
        // reachable-group-only, so the address tiebreak decides.
        Assert.Equal(["https://a.example", "https://z.example"], ordered);
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
    public void Protocol_one_metrics_without_minimum_client_version_keep_the_0_5_1_plus_badge()
    {
        ServerListEntry entry = new() { Address = LanConnectServerListBootstrap.FeaturedServerAddress };

        LanConnectServerListBootstrap.ApplyMetrics(entry, new PeerMetricsResponse
        {
            ModSyncProtocolVersion = 1,
            ModSyncEnabled = true
        });

        Assert.True(entry.SupportsModSyncV051Plus);
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
