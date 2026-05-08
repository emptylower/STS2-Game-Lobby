using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GD = Godot.GD;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectLobbyDirectoryClient
{
    public static async Task<IReadOnlyList<LobbyDirectoryServerEntry>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        string baseUrl = LanConnectConfig.LobbyRegistryBaseUrl;
        Uri requestUri = new($"{baseUrl.TrimEnd('/')}/servers/");
        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(10d)
        };

        GD.Print($"sts2_lan_connect directory api: GET {requestUri}");
        using HttpResponseMessage response = await client.GetAsync(requestUri, cancellationToken);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        GD.Print($"sts2_lan_connect directory api: GET {requestUri} -> {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            throw new LobbyServiceException("中心服务器列表请求失败。", "directory_http_error", (int)response.StatusCode);
        }

        LobbyDirectoryServerListResponse? parsed = JsonSerializer.Deserialize<LobbyDirectoryServerListResponse>(text, LanConnectJson.Options);
        if (parsed == null)
        {
            throw new LobbyServiceException("中心服务器返回了无效数据。", "directory_invalid_response", (int)response.StatusCode);
        }

        return parsed.Servers ?? new List<LobbyDirectoryServerEntry>();
    }

    public static async Task<List<CfServerEntry>> GetPeersAsync(string lobbyBaseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lobbyBaseUrl)) return new List<CfServerEntry>();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
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
