using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal sealed class ServerListEntry
{
    public string Address { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Source { get; set; } = "unknown"; // cache | cf | seed
    public bool IsFavorite { get; set; }
    public DateTime? LastSuccessConnect { get; set; }
    public PingBucket Bucket { get; set; } = PingBucket.Unreachable;
    public int? PingMs { get; set; }

    // Live metrics from `/peers/metrics`, populated by PingAllAsync after the
    // initial pre-warmed gather. All nullable — older v0.2/v0.3 nodes don't
    // expose this endpoint and the picker must render gracefully without it.
    public int? Rooms { get; set; }
    public double? CurrentBandwidthMbps { get; set; }
    public double? BandwidthCapacityMbps { get; set; }
    public double? ResolvedCapacityMbps { get; set; }
    public double? BandwidthUtilizationRatio { get; set; }
    public string CapacitySource { get; set; } = "unknown";
    public bool CreateRoomGuardApplies { get; set; }
    public string CreateRoomGuardStatus { get; set; } = "allow";
}

internal static class LanConnectServerListBootstrap
{
    public static async Task<List<ServerListEntry>> GatherAsync(CancellationToken ct = default)
    {
        var cache = LanConnectKnownPeersCache.Load();
        var cleaned = LanConnectKnownPeersCache.Cleanup(cache, DateTime.UtcNow);
        LanConnectKnownPeersCache.Save(cleaned);

        var cfTask = LanConnectCfDiscoveryClient.GetServersAsync(LanConnectLobbyEndpointDefaults.GetCfDiscoveryBaseUrl(), ct);
        var seeds = LanConnectLobbyEndpointDefaults.GetSeedPeers();

        List<CfServerEntry> cfList;
        try { cfList = await cfTask.WaitAsync(TimeSpan.FromSeconds(5), ct); }
        catch { cfList = new List<CfServerEntry>(); }

        var byAddr = new Dictionary<string, ServerListEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cleaned)
        {
            byAddr[c.Address] = new ServerListEntry
            {
                Address = c.Address, DisplayName = c.DisplayName, Source = "cache",
                IsFavorite = c.IsFavorite,
                LastSuccessConnect = DateTime.TryParse(c.LastSuccessConnect ?? "", out var t) ? t : null,
            };
        }
        foreach (var c in cfList)
        {
            if (!byAddr.TryGetValue(c.Address, out var e))
            {
                byAddr[c.Address] = new ServerListEntry { Address = c.Address, DisplayName = c.DisplayName, Source = "cf" };
            }
        }
        foreach (var s in seeds)
        {
            if (!byAddr.ContainsKey(s))
            {
                byAddr[s] = new ServerListEntry { Address = s, Source = "seed" };
            }
        }
        return byAddr.Values.ToList();
    }

    public static async Task PingAllAsync(List<ServerListEntry> entries, CancellationToken ct = default)
    {
        var tasks = entries.Select(async e =>
        {
            // Probe first for the latency bucket + signed-challenge liveness.
            // Metrics fetched in parallel, since they're a different endpoint
            // that doesn't share state with the probe.
            var probeTask = LanConnectPeerPing.ProbeAsync(e.Address, ct);
            var metricsTask = LanConnectPeerMetricsClient.FetchAsync(e.Address, ct);
            await Task.WhenAll(probeTask, metricsTask);

            PeerProbeResult result = probeTask.Result;
            e.PingMs = result.Ms >= 0 ? result.Ms : null;
            e.Bucket = result.Bucket;
            // Probe is the freshest source — it's a live round-trip to the
            // server itself. Prefer it over the CF/cache value when present so
            // operator name changes propagate immediately in the picker
            // without waiting for the next CF aggregation cycle.
            if (!string.IsNullOrWhiteSpace(result.DisplayName))
            {
                e.DisplayName = result.DisplayName;
            }

            PeerMetricsResponse? metrics = metricsTask.Result;
            if (metrics != null)
            {
                e.Rooms = metrics.Rooms;
                e.CurrentBandwidthMbps = metrics.CurrentBandwidthMbps;
                e.BandwidthCapacityMbps = metrics.BandwidthCapacityMbps;
                e.ResolvedCapacityMbps = metrics.ResolvedCapacityMbps;
                e.BandwidthUtilizationRatio = metrics.BandwidthUtilizationRatio;
                e.CapacitySource = metrics.CapacitySource;
                e.CreateRoomGuardApplies = metrics.CreateRoomGuardApplies;
                e.CreateRoomGuardStatus = metrics.CreateRoomGuardStatus;
                if (!string.IsNullOrWhiteSpace(metrics.DisplayName))
                {
                    e.DisplayName = metrics.DisplayName;
                }
            }
        }).ToList();
        await Task.WhenAll(tasks);
    }
}
