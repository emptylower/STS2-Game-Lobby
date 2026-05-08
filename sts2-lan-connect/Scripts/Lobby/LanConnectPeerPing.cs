using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal enum PingBucket { Low, Mid, High, Unreachable }

internal static class LanConnectPeerPing
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const int LowMs = 500;
    private const int MidMs = 2000;

    public static async Task<(int Ms, PingBucket Bucket)> PingAsync(string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return (-1, PingBucket.Unreachable);
        var url = $"{baseUrl.TrimEnd('/')}/peers/health?ping=1";
        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            var sw = Stopwatch.StartNew();
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await client.SendAsync(req, ct);
            sw.Stop();
            if (!resp.IsSuccessStatusCode) return ((int)sw.ElapsedMilliseconds, PingBucket.Unreachable);
            int ms = (int)sw.ElapsedMilliseconds;
            return (ms, ms <= LowMs ? PingBucket.Low : ms <= MidMs ? PingBucket.Mid : PingBucket.High);
        }
        catch
        {
            return (-1, PingBucket.Unreachable);
        }
    }
}
