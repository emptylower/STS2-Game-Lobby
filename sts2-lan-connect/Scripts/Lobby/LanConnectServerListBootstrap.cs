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

internal sealed class ServerListMergeResult
{
    public bool Changed { get; set; }
    public List<ServerListEntry> AddedEntries { get; } = new();
}

internal static class LanConnectServerListBootstrap
{
    public static List<ServerListEntry> GatherInitialCandidates()
    {
        var cache = LanConnectKnownPeersCache.Load();
        var cleaned = LanConnectKnownPeersCache.Cleanup(cache, DateTime.UtcNow);
        LanConnectKnownPeersCache.Save(cleaned);

        var byAddr = new Dictionary<string, ServerListEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var cachedPeer in cleaned)
        {
            if (string.IsNullOrWhiteSpace(cachedPeer.Address))
            {
                continue;
            }

            byAddr[cachedPeer.Address] = new ServerListEntry
            {
                Address = cachedPeer.Address,
                DisplayName = cachedPeer.DisplayName,
                Source = "cache",
                IsFavorite = cachedPeer.IsFavorite,
                LastSuccessConnect = DateTime.TryParse(cachedPeer.LastSuccessConnect ?? "", out var connectedAt) ? connectedAt : null,
            };
        }

        foreach (var seedAddress in LanConnectLobbyEndpointDefaults.GetSeedPeers())
        {
            if (string.IsNullOrWhiteSpace(seedAddress) || byAddr.ContainsKey(seedAddress))
            {
                continue;
            }

            byAddr[seedAddress] = new ServerListEntry
            {
                Address = seedAddress,
                Source = "seed",
            };
        }

        return byAddr.Values.ToList();
    }

    public static async Task<List<ServerListEntry>> GatherCloudflareCandidatesAsync(CancellationToken ct = default)
    {
        var cfList = await LanConnectCfDiscoveryClient.GetServersAsync(
            LanConnectLobbyEndpointDefaults.GetCfDiscoveryBaseUrl(),
            ct);

        var byAddr = new Dictionary<string, ServerListEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var cfEntry in cfList)
        {
            if (string.IsNullOrWhiteSpace(cfEntry.Address) || byAddr.ContainsKey(cfEntry.Address))
            {
                continue;
            }

            byAddr[cfEntry.Address] = new ServerListEntry
            {
                Address = cfEntry.Address,
                DisplayName = cfEntry.DisplayName,
                Source = "cf",
            };
        }

        return byAddr.Values.ToList();
    }

    public static ServerListMergeResult MergeDiscoveredEntries(List<ServerListEntry> currentEntries, IEnumerable<ServerListEntry> discoveredEntries)
    {
        var result = new ServerListMergeResult();
        var byAddr = new Dictionary<string, ServerListEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var currentEntry in currentEntries)
        {
            if (!string.IsNullOrWhiteSpace(currentEntry.Address))
            {
                byAddr[currentEntry.Address] = currentEntry;
            }
        }

        foreach (var incomingEntry in discoveredEntries)
        {
            if (string.IsNullOrWhiteSpace(incomingEntry.Address))
            {
                continue;
            }

            if (!byAddr.TryGetValue(incomingEntry.Address, out var existingEntry))
            {
                currentEntries.Add(incomingEntry);
                byAddr[incomingEntry.Address] = incomingEntry;
                result.AddedEntries.Add(incomingEntry);
                result.Changed = true;
                continue;
            }

            if (MergeEntry(existingEntry, incomingEntry))
            {
                result.Changed = true;
            }
        }

        return result;
    }

    public static async Task PingAllAsync(IEnumerable<ServerListEntry> entries, CancellationToken ct = default)
    {
        var entryList = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Address))
            .ToList();

        var tasks = entryList.Select(async entry =>
        {
            // Probe first for the latency bucket + signed-challenge liveness.
            // Metrics fetched in parallel, since they're a different endpoint
            // that doesn't share state with the probe.
            var probeTask = LanConnectPeerPing.ProbeAsync(entry.Address, ct);
            var metricsTask = LanConnectPeerMetricsClient.FetchAsync(entry.Address, ct);
            await Task.WhenAll(probeTask, metricsTask);

            PeerProbeResult result = probeTask.Result;
            entry.PingMs = result.Ms >= 0 ? result.Ms : null;
            entry.Bucket = result.Bucket;
            // Probe is the freshest source — it's a live round-trip to the
            // server itself. Prefer it over the CF/cache value when present so
            // operator name changes propagate immediately in the picker
            // without waiting for the next CF aggregation cycle.
            if (!string.IsNullOrWhiteSpace(result.DisplayName))
            {
                entry.DisplayName = result.DisplayName;
            }

            PeerMetricsResponse? metrics = metricsTask.Result;
            if (metrics != null)
            {
                entry.Rooms = metrics.Rooms;
                entry.CurrentBandwidthMbps = metrics.CurrentBandwidthMbps;
                entry.BandwidthCapacityMbps = metrics.BandwidthCapacityMbps;
                entry.ResolvedCapacityMbps = metrics.ResolvedCapacityMbps;
                entry.BandwidthUtilizationRatio = metrics.BandwidthUtilizationRatio;
                entry.CapacitySource = metrics.CapacitySource;
                entry.CreateRoomGuardApplies = metrics.CreateRoomGuardApplies;
                entry.CreateRoomGuardStatus = metrics.CreateRoomGuardStatus;
                if (!string.IsNullOrWhiteSpace(metrics.DisplayName))
                {
                    entry.DisplayName = metrics.DisplayName;
                }
            }
        }).ToList();

        await Task.WhenAll(tasks);
    }

    private static bool MergeEntry(ServerListEntry target, ServerListEntry incoming)
    {
        bool changed = false;

        if (string.Equals(target.Source, "seed", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(incoming.Source, "cf", StringComparison.OrdinalIgnoreCase))
        {
            target.Source = incoming.Source;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(target.DisplayName) && !string.IsNullOrWhiteSpace(incoming.DisplayName))
        {
            target.DisplayName = incoming.DisplayName;
            changed = true;
        }

        return changed;
    }
}
