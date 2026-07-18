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
        using var client = new HttpClient { Timeout = Timeout };
        return await FetchAsync(baseUrl, client, ct);
    }

    internal static async Task<PeerMetricsResponse?> FetchAsync(
        string baseUrl,
        HttpMessageHandler httpMessageHandler,
        CancellationToken ct = default)
    {
        using var client = new HttpClient(httpMessageHandler, disposeHandler: false) { Timeout = Timeout };
        return await FetchAsync(baseUrl, client, ct);
    }

    private static async Task<PeerMetricsResponse?> FetchAsync(
        string baseUrl,
        HttpClient client,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        string trimmed = baseUrl.TrimEnd('/');
        try
        {
            using var resp = await client.GetAsync($"{trimmed}/peers/metrics", ct);
            if (!resp.IsSuccessStatusCode) return null;
            string text = await resp.Content.ReadAsStringAsync(ct);
            PeerMetricsResponse? metrics = JsonSerializer.Deserialize<PeerMetricsResponse>(text, LanConnectJson.Options);
            if (metrics == null || metrics.ModSyncProtocolVersion > 0) return metrics;

            try
            {
                using var probeResponse = await client.GetAsync($"{trimmed}/probe", ct);
                if (!probeResponse.IsSuccessStatusCode) return metrics;
                string probeText = await probeResponse.Content.ReadAsStringAsync(ct);
                LobbyProbeResponse? probe = JsonSerializer.Deserialize<LobbyProbeResponse>(probeText, LanConnectJson.Options);
                if (probe?.Ok != true) return metrics;

                metrics.ModSyncProtocolVersion = probe.Capabilities.ModSyncProtocolVersion;
                metrics.ModSyncEnabled = probe.Capabilities.ModSyncEnabled;
                metrics.ModSyncMinimumClientVersion = probe.Capabilities.ModSyncMinimumClientVersion;
            }
            catch
            {
                // Metrics remain useful when a legacy or transient /probe request fails.
            }

            return metrics;
        }
        catch
        {
            return null;
        }
    }
}
