using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal sealed class CfServerEntry
{
    public string Address { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string LastSeen { get; set; } = string.Empty;
}

internal static class LanConnectCfDiscoveryClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task<List<CfServerEntry>> GetServersAsync(string cfBaseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cfBaseUrl)) return new List<CfServerEntry>();
        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            using var resp = await client.GetAsync($"{cfBaseUrl.TrimEnd('/')}/v1/servers", ct);
            if (!resp.IsSuccessStatusCode) return new List<CfServerEntry>();
            string text = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("servers", out var servers)) return new List<CfServerEntry>();
            var result = new List<CfServerEntry>();
            foreach (var s in servers.EnumerateArray())
            {
                result.Add(new CfServerEntry
                {
                    Address = s.GetProperty("address").GetString() ?? "",
                    PublicKey = s.TryGetProperty("publicKey", out var pk) ? (pk.GetString() ?? "") : "",
                    DisplayName = s.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
                    LastSeen = s.TryGetProperty("lastSeen", out var ls) ? (ls.GetString() ?? "") : "",
                });
            }
            return result;
        }
        catch
        {
            return new List<CfServerEntry>();
        }
    }
}
