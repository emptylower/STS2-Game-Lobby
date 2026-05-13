using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

// Decentralized peer-list helper kept only for the cache-expander warm-up.
// The legacy /servers/ aggregate (mother registry) call has been removed —
// the server picker now goes through LanConnectServerListBootstrap which
// reads the CF discovery aggregator plus per-peer /peers/metrics.
internal static class LanConnectLobbyDirectoryClient
{
    public static async Task<List<CfServerEntry>> GetPeersAsync(string lobbyBaseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lobbyBaseUrl)) return new List<CfServerEntry>();
        try
        {
            using var client = new HttpClient { Timeout = System.TimeSpan.FromSeconds(5) };
            using var resp = await client.GetAsync($"{lobbyBaseUrl.TrimEnd('/')}/peers", ct);
            if (!resp.IsSuccessStatusCode) return new List<CfServerEntry>();
            string text = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("peers", out var peers)) return new List<CfServerEntry>();
            var result = new List<CfServerEntry>();
            foreach (var p in peers.EnumerateArray())
            {
                result.Add(new CfServerEntry
                {
                    Address = p.GetProperty("address").GetString() ?? "",
                    PublicKey = p.TryGetProperty("publicKey", out var pk) ? (pk.GetString() ?? "") : "",
                    DisplayName = p.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
                    LastSeen = p.TryGetProperty("lastSeen", out var ls) ? (ls.GetString() ?? "") : "",
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
