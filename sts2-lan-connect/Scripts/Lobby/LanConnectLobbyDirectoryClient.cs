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
    private const string PreferredRegistryBaseUrl = "https://sts.exacg.cc";

    public static async Task<IReadOnlyList<LobbyDirectoryServerEntry>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        List<string> sources = BuildDirectorySources();
        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(10d)
        };

        List<LobbyDirectoryServerEntry> mergedServers = new();
        HashSet<string> seenBaseUrls = new(StringComparer.OrdinalIgnoreCase);
        bool anySourceSucceeded = false;
        LobbyServiceException? lastLobbyError = null;
        Exception? lastUnexpectedError = null;

        foreach (string sourceBaseUrl in sources)
        {
            try
            {
                IReadOnlyList<LobbyDirectoryServerEntry> sourceServers = await GetServersFromSourceAsync(client, sourceBaseUrl, cancellationToken);
                anySourceSucceeded = true;
                AppendUniqueServers(mergedServers, seenBaseUrls, sourceServers);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (LobbyServiceException ex)
            {
                lastLobbyError = ex;
                GD.Print($"sts2_lan_connect directory api: source '{sourceBaseUrl}' failed - {ex.Code} {ex.Message}");
            }
            catch (Exception ex)
            {
                lastUnexpectedError = ex;
                GD.Print($"sts2_lan_connect directory api: source '{sourceBaseUrl}' failed - {ex.Message}");
            }
        }

        if (anySourceSucceeded)
        {
            return mergedServers;
        }

        if (lastLobbyError != null)
        {
            throw new LobbyServiceException("中心服务器列表请求失败。", "directory_all_sources_failed", lastLobbyError.StatusCode);
        }

        if (lastUnexpectedError != null)
        {
            throw new LobbyServiceException("中心服务器列表请求失败。", "directory_all_sources_failed");
        }

        return new List<LobbyDirectoryServerEntry>();
    }

    private static List<string> BuildDirectorySources()
    {
        List<string> sources = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        TryAddDirectorySource(sources, seen, PreferredRegistryBaseUrl);
        TryAddDirectorySource(sources, seen, LanConnectConfig.LobbyRegistryBaseUrl);
        return sources;
    }

    private static void TryAddDirectorySource(List<string> sources, HashSet<string> seen, string? value)
    {
        string normalized = NormalizeBaseUrl(value);
        if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
        {
            return;
        }

        sources.Add(normalized);
    }

    private static async Task<IReadOnlyList<LobbyDirectoryServerEntry>> GetServersFromSourceAsync(HttpClient client, string baseUrl, CancellationToken cancellationToken)
    {
        Uri requestUri = new($"{baseUrl}/servers/");

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

    private static void AppendUniqueServers(List<LobbyDirectoryServerEntry> mergedServers, HashSet<string> seenBaseUrls, IReadOnlyList<LobbyDirectoryServerEntry> sourceServers)
    {
        foreach (LobbyDirectoryServerEntry entry in sourceServers)
        {
            string normalizedBaseUrl = NormalizeBaseUrl(entry.BaseUrl);
            if (string.IsNullOrWhiteSpace(normalizedBaseUrl) || !seenBaseUrls.Add(normalizedBaseUrl))
            {
                continue;
            }

            entry.BaseUrl = normalizedBaseUrl;
            mergedServers.Add(entry);
        }
    }

    private static string NormalizeBaseUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().TrimEnd('/');
    }
}
