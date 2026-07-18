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
    public bool IsPinned { get; set; }
    public bool SupportsModSyncV051Plus { get; set; }

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
    public const string FeaturedServerAddress = "http://101.35.217.99:8788";

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

        List<ServerListEntry> entries = byAddr.Values.ToList();
        EnsureFeaturedServer(entries);
        return entries;
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
                byAddr[NormalizeAddress(currentEntry.Address)] = currentEntry;
            }
        }

        foreach (var incomingEntry in discoveredEntries)
        {
            if (string.IsNullOrWhiteSpace(incomingEntry.Address))
            {
                continue;
            }

            string addressKey = NormalizeAddress(incomingEntry.Address);
            if (!byAddr.TryGetValue(addressKey, out var existingEntry))
            {
                currentEntries.Add(incomingEntry);
                byAddr[addressKey] = incomingEntry;
                result.AddedEntries.Add(incomingEntry);
                result.Changed = true;
                continue;
            }

            if (MergeEntry(existingEntry, incomingEntry))
            {
                result.Changed = true;
            }
        }

        EnsureFeaturedServer(currentEntries);

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
            if (metrics != null) ApplyMetrics(entry, metrics);
        }).ToList();

        await Task.WhenAll(tasks);
    }

    internal static void EnsureFeaturedServer(List<ServerListEntry> entries)
    {
        List<ServerListEntry> matches = entries
            .Where(entry => IsFeaturedAddress(entry.Address))
            .ToList();
        ServerListEntry featured;
        if (matches.Count == 0)
        {
            featured = new ServerListEntry
            {
                Address = FeaturedServerAddress,
                Source = "featured",
            };
            entries.Add(featured);
        }
        else
        {
            featured = matches[0];
            foreach (ServerListEntry duplicate in matches.Skip(1)) entries.Remove(duplicate);
        }

        featured.Address = FeaturedServerAddress;
        featured.IsPinned = true;
    }

    internal static IOrderedEnumerable<ServerListEntry> OrderForDisplay(IEnumerable<ServerListEntry> entries) =>
        entries
            .OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.LastSuccessConnect ?? DateTime.MinValue)
            .ThenBy(entry => entry.Bucket)
            .ThenBy(entry => entry.Address, StringComparer.OrdinalIgnoreCase);

    internal static void ApplyMetrics(ServerListEntry entry, PeerMetricsResponse metrics)
    {
        entry.Rooms = metrics.Rooms;
        entry.CurrentBandwidthMbps = metrics.CurrentBandwidthMbps;
        entry.BandwidthCapacityMbps = metrics.BandwidthCapacityMbps;
        entry.ResolvedCapacityMbps = metrics.ResolvedCapacityMbps;
        entry.BandwidthUtilizationRatio = metrics.BandwidthUtilizationRatio;
        entry.CapacitySource = metrics.CapacitySource;
        entry.CreateRoomGuardApplies = metrics.CreateRoomGuardApplies;
        entry.CreateRoomGuardStatus = metrics.CreateRoomGuardStatus;
        entry.SupportsModSyncV051Plus = metrics.ModSyncEnabled &&
                                               metrics.ModSyncProtocolVersion >= LanConnectModSyncCapabilities.ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(metrics.DisplayName)) entry.DisplayName = metrics.DisplayName;
    }

    private static bool IsFeaturedAddress(string address) =>
        string.Equals(NormalizeAddress(address), NormalizeAddress(FeaturedServerAddress), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAddress(string address) => address.Trim().TrimEnd('/');

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
