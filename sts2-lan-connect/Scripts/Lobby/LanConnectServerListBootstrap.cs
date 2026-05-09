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
            PeerProbeResult result = await LanConnectPeerPing.ProbeAsync(e.Address, ct);
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
        }).ToList();
        await Task.WhenAll(tasks);
    }
}
