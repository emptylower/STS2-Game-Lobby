using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal enum PingBucket { Low, Mid, High, Unreachable }

internal readonly record struct PeerProbeResult(int Ms, PingBucket Bucket, string? DisplayName);

internal static class LanConnectPeerPing
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const int LowMs = 500;
    private const int MidMs = 2000;

    /// <summary>
    /// Two-tier probe so the picker works against both v0.3 (peer subsystem)
    /// and v0.2 (legacy /probe) lobby servers.
    ///
    /// 1. GET <c>/peers/health?challenge=…</c> — v0.3+ servers respond 200
    ///    with the operator-set displayName plus an ed25519 signature. We do
    ///    not verify the signature here (the picker is for triage, not trust);
    ///    we just trust the address+displayName that came back over the wire,
    ///    same as any HTTP discovery service.
    /// 2. If that fails (HTTP error, timeout, missing endpoint), fall back to
    ///    GET <c>/probe</c> — present on every lobby-service version. Gives a
    ///    ping-only result with no displayName.
    ///
    /// Each tier samples up to twice on success and keeps the minimum round
    /// trip; a failing first sample never triggers a second one on that tier
    /// and falls through instead. A caller-cancelled token stops all further
    /// samples and fallbacks immediately and reports Unreachable (never
    /// throws), while a plain per-request timeout (token still live) is just a
    /// normal failure that follows the fallback rules.
    /// </summary>
    public static async Task<PeerProbeResult> ProbeAsync(string baseUrl, CancellationToken ct = default)
    {
        using var handler = new HttpClientHandler();
        return await ProbeAsync(baseUrl, handler, ct);
    }

    internal static async Task<PeerProbeResult> ProbeAsync(string baseUrl, HttpMessageHandler handler, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return new PeerProbeResult(-1, PingBucket.Unreachable, null);
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new PeerProbeResult(-1, PingBucket.Unreachable, null);
        }

        string trimmed = baseUrl.TrimEnd('/');
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout };

        // Tier 1 — peer-aware health endpoint, returns displayName.
        string challenge = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
        string healthUrl = $"{trimmed}/peers/health?challenge={challenge}";
        var (firstHealthMs, displayName) = await TimeRequestAsync(client, healthUrl, readDisplayName: true, ct);
        if (ct.IsCancellationRequested)
        {
            return new PeerProbeResult(-1, PingBucket.Unreachable, null);
        }

        if (firstHealthMs != null)
        {
            var (secondHealthMs, _) = await TimeRequestAsync(client, healthUrl, readDisplayName: false, ct);
            if (ct.IsCancellationRequested)
            {
                return new PeerProbeResult(-1, PingBucket.Unreachable, null);
            }

            int healthMs = MinSample(firstHealthMs.Value, secondHealthMs);
            return new PeerProbeResult(healthMs, BucketFor(healthMs), displayName);
        }

        // Tier 2 — legacy /probe (ping-only, all server versions).
        string probeUrl = $"{trimmed}/probe";
        var (firstProbeMs, _) = await TimeRequestAsync(client, probeUrl, readDisplayName: false, ct);
        if (ct.IsCancellationRequested)
        {
            return new PeerProbeResult(-1, PingBucket.Unreachable, null);
        }

        if (firstProbeMs != null)
        {
            var (secondProbeMs, _) = await TimeRequestAsync(client, probeUrl, readDisplayName: false, ct);
            if (ct.IsCancellationRequested)
            {
                return new PeerProbeResult(-1, PingBucket.Unreachable, null);
            }

            int probeMs = MinSample(firstProbeMs.Value, secondProbeMs);
            return new PeerProbeResult(probeMs, BucketFor(probeMs), null);
        }

        return new PeerProbeResult(-1, PingBucket.Unreachable, null);
    }

    /// <summary>Legacy entry point preserved for callers that only need ping.</summary>
    public static async Task<(int Ms, PingBucket Bucket)> PingAsync(string baseUrl, CancellationToken ct = default)
    {
        PeerProbeResult result = await ProbeAsync(baseUrl, ct);
        return (result.Ms, result.Bucket);
    }

    private static async Task<(int? Ms, string? DisplayName)> TimeRequestAsync(
        HttpClient client,
        string url,
        bool readDisplayName,
        CancellationToken ct)
    {
        try
        {
            Stopwatch sw = Stopwatch.StartNew();
            using HttpResponseMessage resp = await client.GetAsync(url, ct);
            sw.Stop();
            if (!resp.IsSuccessStatusCode)
            {
                return (null, null);
            }

            string? displayName = null;
            if (readDisplayName)
            {
                try
                {
                    string body = await resp.Content.ReadAsStringAsync(ct);
                    using JsonDocument doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("displayName", out JsonElement dn)
                        && dn.ValueKind == JsonValueKind.String)
                    {
                        string s = dn.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(s)) displayName = s.Trim();
                    }
                }
                catch
                {
                    // Body unparseable — keep ping but no displayName.
                }
            }

            return ((int)sw.ElapsedMilliseconds, displayName);
        }
        catch
        {
            // Caller cancellation must stop the whole probe (checked by the
            // caller via ct.IsCancellationRequested); everything else —
            // including the HttpClient 5s timeout — is a plain tier failure.
            return (null, null);
        }
    }

    private static int MinSample(int first, int? second) => second is int value ? Math.Min(first, value) : first;

    private static PingBucket BucketFor(int ms) =>
        ms <= LowMs ? PingBucket.Low :
        ms <= MidMs ? PingBucket.Mid :
        PingBucket.High;
}
