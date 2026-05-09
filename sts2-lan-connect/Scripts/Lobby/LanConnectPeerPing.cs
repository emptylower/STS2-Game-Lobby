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
    /// </summary>
    public static async Task<PeerProbeResult> ProbeAsync(string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new PeerProbeResult(-1, PingBucket.Unreachable, null);
        }

        string trimmed = baseUrl.TrimEnd('/');
        using var client = new HttpClient { Timeout = Timeout };

        // Tier 1 — peer-aware health endpoint, returns displayName.
        try
        {
            string challenge = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
            string healthUrl = $"{trimmed}/peers/health?challenge={challenge}";
            Stopwatch sw = Stopwatch.StartNew();
            using HttpResponseMessage resp = await client.GetAsync(healthUrl, ct);
            sw.Stop();
            if (resp.IsSuccessStatusCode)
            {
                int ms = (int)sw.ElapsedMilliseconds;
                string? displayName = null;
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
                return new PeerProbeResult(ms, BucketFor(ms), displayName);
            }
            // Non-2xx — fall through to /probe so we can still report ping
            // for legacy servers that don't expose /peers/health.
        }
        catch
        {
            // Network failure on /peers/health — try the legacy /probe.
        }

        // Tier 2 — legacy /probe (ping-only, all server versions).
        try
        {
            string probeUrl = $"{trimmed}/probe";
            Stopwatch sw = Stopwatch.StartNew();
            using HttpResponseMessage resp = await client.GetAsync(probeUrl, ct);
            sw.Stop();
            if (resp.IsSuccessStatusCode)
            {
                int ms = (int)sw.ElapsedMilliseconds;
                return new PeerProbeResult(ms, BucketFor(ms), null);
            }
        }
        catch
        {
            // Both tiers down — Unreachable.
        }

        return new PeerProbeResult(-1, PingBucket.Unreachable, null);
    }

    /// <summary>Legacy entry point preserved for callers that only need ping.</summary>
    public static async Task<(int Ms, PingBucket Bucket)> PingAsync(string baseUrl, CancellationToken ct = default)
    {
        PeerProbeResult result = await ProbeAsync(baseUrl, ct);
        return (result.Ms, result.Bucket);
    }

    private static PingBucket BucketFor(int ms) =>
        ms <= LowMs ? PingBucket.Low :
        ms <= MidMs ? PingBucket.Mid :
        PingBucket.High;
}
