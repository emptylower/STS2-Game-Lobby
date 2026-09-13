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

    internal sealed record PeerDiscoverySnapshot(PeerMetricsResponse? Metrics, ServerVersionInfo Version);

    public static async Task<PeerDiscoverySnapshot> FetchSnapshotAsync(string baseUrl, CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = Timeout };
        return await FetchSnapshotAsync(baseUrl, client, ct);
    }

    internal static async Task<PeerDiscoverySnapshot> FetchSnapshotAsync(
        string baseUrl,
        HttpMessageHandler httpMessageHandler,
        CancellationToken ct = default)
    {
        using var client = new HttpClient(httpMessageHandler, disposeHandler: false) { Timeout = Timeout };
        return await FetchSnapshotAsync(baseUrl, client, ct);
    }

    public static async Task<PeerMetricsResponse?> FetchAsync(string baseUrl, CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = Timeout };
        return (await FetchSnapshotAsync(baseUrl, client, ct)).Metrics;
    }

    internal static async Task<PeerMetricsResponse?> FetchAsync(
        string baseUrl,
        HttpMessageHandler httpMessageHandler,
        CancellationToken ct = default)
    {
        using var client = new HttpClient(httpMessageHandler, disposeHandler: false) { Timeout = Timeout };
        return (await FetchSnapshotAsync(baseUrl, client, ct)).Metrics;
    }

    // Snapshot orchestration (plan §4.4): at most one `/peers/metrics` and one
    // `/probe` per server per round. The probe is fetched only when the metrics
    // payload has no usable version OR the mod-sync capability is missing
    // (legacy fallback); its single response feeds both the version inference
    // and the mod-sync backfill. A reported metrics version is never overridden
    // by an older or failing probe.
    private static async Task<PeerDiscoverySnapshot> FetchSnapshotAsync(
        string baseUrl,
        HttpClient client,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return new PeerDiscoverySnapshot(null, ServerVersionInfo.Unknown);
        string trimmed = baseUrl.TrimEnd('/');
        try
        {
            PeerMetricsResponse? metrics = null;
            try
            {
                using var resp = await client.GetAsync($"{trimmed}/peers/metrics", ct);
                if (resp.IsSuccessStatusCode)
                {
                    string text = await resp.Content.ReadAsStringAsync(ct);
                    metrics = JsonSerializer.Deserialize<PeerMetricsResponse>(text, LanConnectJson.Options);
                }
            }
            catch
            {
                // Metrics endpoint down (legacy node or transient failure) —
                // the snapshot continues with metrics = null.
            }

            ServerVersionInfo? reported = LanConnectServerVersionResolver.FromReported(metrics?.ServiceVersion);
            bool needsProbe = reported == null || (metrics != null && metrics.ModSyncProtocolVersion <= 0);
            if (!needsProbe)
            {
                return new PeerDiscoverySnapshot(metrics, reported!);
            }

            // A cancelled caller token must never trigger the /probe request —
            // a superseded refresh round leaves the snapshot at Unknown
            // without throwing (plan §4.4 no-throw contract).
            if (ct.IsCancellationRequested)
            {
                return new PeerDiscoverySnapshot(metrics, ServerVersionInfo.Unknown);
            }

            ServerVersionInfo probeVersion = ServerVersionInfo.Unknown;
            try
            {
                using var probeResponse = await client.GetAsync($"{trimmed}/probe", ct);
                if (probeResponse.IsSuccessStatusCode)
                {
                    string probeText = await probeResponse.Content.ReadAsStringAsync(ct);
                    LobbyProbeResponse? probe = JsonSerializer.Deserialize<LobbyProbeResponse>(probeText, LanConnectJson.Options);
                    probeVersion = LanConnectServerVersionResolver.FromProbe(probe);
                    if (metrics != null &&
                        metrics.ModSyncProtocolVersion <= 0 &&
                        probe?.Ok == true &&
                        probe.Capabilities != null)
                    {
                        metrics.ModSyncProtocolVersion = probe.Capabilities.ModSyncProtocolVersion;
                        metrics.ModSyncEnabled = probe.Capabilities.ModSyncEnabled;
                        metrics.ModSyncMinimumClientVersion = probe.Capabilities.ModSyncMinimumClientVersion;
                    }
                }
            }
            catch
            {
                // Metrics remain useful when a legacy or transient /probe request fails.
            }

            return new PeerDiscoverySnapshot(metrics, reported ?? probeVersion);
        }
        catch
        {
            return new PeerDiscoverySnapshot(null, ServerVersionInfo.Unknown);
        }
    }
}
