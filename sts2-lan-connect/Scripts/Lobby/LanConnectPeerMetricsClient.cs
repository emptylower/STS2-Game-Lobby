using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

// Per-server live snapshot used by the server picker to show rooms/bandwidth
// directly from each lobby. Replaces the legacy mother-server aggregate
// `/servers/` call: instead of one aggregated read from a central registry,
// the client fans out to each candidate's `/peers/metrics` and merges.
internal static class LanConnectPeerMetricsClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task<PeerMetricsResponse?> FetchAsync(string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            using var resp = await client.GetAsync($"{baseUrl.TrimEnd('/')}/peers/metrics", ct);
            if (!resp.IsSuccessStatusCode) return null;
            string text = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<PeerMetricsResponse>(text, LanConnectJson.Options);
        }
        catch
        {
            return null;
        }
    }
}
