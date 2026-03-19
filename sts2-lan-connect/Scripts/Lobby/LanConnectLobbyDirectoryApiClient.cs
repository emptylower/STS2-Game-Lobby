using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GD = Godot.GD;

namespace Sts2LanConnect.Scripts;

internal sealed class LobbyDirectoryApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public LobbyDirectoryApiClient(string baseUrl)
    {
        string normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        _baseUri = new Uri(normalizedBaseUrl, UriKind.Absolute);
        _httpClient = new HttpClient
        {
            BaseAddress = _baseUri,
            Timeout = TimeSpan.FromSeconds(10d)
        };
    }

    public static LobbyDirectoryApiClient CreateConfigured()
    {
        return new LobbyDirectoryApiClient(LanConnectConstants.DefaultLobbyRegistryBaseUrl);
    }

    public async Task<IReadOnlyList<LobbyDirectoryServerEntry>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        return await SendAsync<List<LobbyDirectoryServerEntry>>("registry/servers", HttpMethod.Get, null, cancellationToken)
               ?? new List<LobbyDirectoryServerEntry>();
    }

    public Task SubmitServerAsync(LobbyDirectorySubmissionRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<object>("registry/submissions", HttpMethod.Post, request, cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<T> SendAsync<T>(string path, HttpMethod method, object? payload, CancellationToken cancellationToken)
    {
        Uri requestUri = new(_baseUri, path);
        GD.Print($"sts2_lan_connect registry api: {method.Method} {requestUri}");
        using HttpRequestMessage request = new(method, path);
        if (payload != null)
        {
            string json = JsonSerializer.Serialize(payload, LanConnectJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(text, (int)response.StatusCode);
        }

        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(text))
        {
            return (T)(object)new object();
        }

        T? parsed = JsonSerializer.Deserialize<T>(text, LanConnectJson.Options);
        if (parsed == null)
        {
            throw new LobbyServiceException("服务器目录返回了空响应。", "empty_registry_response", (int)response.StatusCode);
        }

        return parsed;
    }

    private static string NormalizeBaseUrl(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? LanConnectConstants.DefaultLobbyRegistryBaseUrl
            : value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new LobbyServiceException("当前客户端尚未绑定中心注册表服务。", "missing_registry_base_url");
        }

        return normalized.TrimEnd('/') + "/";
    }

    private static LobbyServiceException BuildException(string responseBody, int statusCode)
    {
        try
        {
            LobbyErrorResponse? error = JsonSerializer.Deserialize<LobbyErrorResponse>(responseBody, LanConnectJson.Options);
            if (error != null)
            {
                return new LobbyServiceException(error.Message, error.Code, statusCode, error.Details);
            }
        }
        catch
        {
        }

        string message = string.IsNullOrWhiteSpace(responseBody)
            ? "服务器目录请求失败。"
            : responseBody.Trim();
        return new LobbyServiceException(message, "registry_http_error", statusCode);
    }
}
