using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GD = Godot.GD;

namespace Sts2LanConnect.Scripts;

internal sealed class LobbyApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly Uri _controlUri;

    public LobbyApiClient(string baseUrl)
    {
        string normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        string normalizedWsUrl = NormalizeWsUrl(normalizedBaseUrl);
        _baseUri = new Uri(normalizedBaseUrl, UriKind.Absolute);
        _controlUri = new Uri(normalizedWsUrl, UriKind.Absolute);
        _httpClient = new HttpClient
        {
            BaseAddress = _baseUri,
            Timeout = TimeSpan.FromSeconds(10d)
        };
    }

    public static LobbyApiClient CreateConfigured()
    {
        return new LobbyApiClient(LanConnectConfig.LobbyServerBaseUrl);
    }

    public Uri BuildHostControlUri(string controlChannelId, string roomId, string hostToken)
    {
        return BuildControlUri(controlChannelId, "host", roomId, "token", hostToken);
    }

    public Uri BuildClientControlUri(string controlChannelId, string roomId, string ticketId)
    {
        return BuildControlUri(controlChannelId, "client", roomId, "ticketId", ticketId);
    }

    public async Task<IReadOnlyList<LobbyRoomSummary>> GetRoomsAsync(CancellationToken cancellationToken = default)
    {
        return await SendAsync<List<LobbyRoomSummary>>("rooms", HttpMethod.Get, null, cancellationToken) ?? new List<LobbyRoomSummary>();
    }

    public async Task<double> MeasureProbeRttAsync(CancellationToken cancellationToken = default)
    {
        Uri requestUri = new(_baseUri, "probe");
        GD.Print($"sts2_lan_connect lobby api: GET {requestUri} via {GetEndpointSource()} (probe)");
        using HttpRequestMessage request = new(HttpMethod.Get, "probe");
        Stopwatch stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        stopwatch.Stop();
        GD.Print(
            $"sts2_lan_connect lobby api: GET {requestUri} -> {(int)response.StatusCode} probeRttMs={stopwatch.Elapsed.TotalMilliseconds:0}");
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    public Task<LobbyAnnouncementResponse> GetAnnouncementAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<LobbyAnnouncementResponse>("announcement", HttpMethod.Get, null, cancellationToken);
    }

    public Task<LobbyCreateRoomResponse> CreateRoomAsync(LobbyCreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<LobbyCreateRoomResponse>("rooms", HttpMethod.Post, request, cancellationToken);
    }

    public Task<LobbyJoinRoomResponse> JoinRoomAsync(string roomId, LobbyJoinRoomRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<LobbyJoinRoomResponse>($"rooms/{Uri.EscapeDataString(roomId)}/join", HttpMethod.Post, request, cancellationToken);
    }

    public Task SendHeartbeatAsync(string roomId, LobbyHeartbeatRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<object>($"rooms/{Uri.EscapeDataString(roomId)}/heartbeat", HttpMethod.Post, request, cancellationToken);
    }

    public Task DeleteRoomAsync(string roomId, LobbyDeleteRoomRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<object>($"rooms/{Uri.EscapeDataString(roomId)}", HttpMethod.Delete, request, cancellationToken);
    }

    public Task ReportConnectionEventAsync(string roomId, LobbyConnectionEventRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<object>($"rooms/{Uri.EscapeDataString(roomId)}/connection-events", HttpMethod.Post, request, cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<T> SendAsync<T>(string path, HttpMethod method, object? payload, CancellationToken cancellationToken)
    {
        Uri requestUri = new(_baseUri, path);
        GD.Print($"sts2_lan_connect lobby api: {method.Method} {requestUri} via {GetEndpointSource()} ({DescribePayload(payload)})");
        using HttpRequestMessage request = new(method, path);
        if (payload != null)
        {
            string json = JsonSerializer.Serialize(payload, LanConnectJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        GD.Print($"sts2_lan_connect lobby api: {method.Method} {requestUri} -> {(int)response.StatusCode}");
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
            throw new LobbyServiceException("大厅服务返回了空响应。", "empty_response", (int)response.StatusCode);
        }

        return parsed;
    }

    private Uri BuildControlUri(string controlChannelId, string role, string roomId, string credentialName, string credentialValue)
    {
        string separator = string.IsNullOrEmpty(_controlUri.Query) ? "?" : "&";
        string uri = $"{_controlUri}{separator}controlChannelId={Uri.EscapeDataString(controlChannelId)}&role={Uri.EscapeDataString(role)}&roomId={Uri.EscapeDataString(roomId)}&{Uri.EscapeDataString(credentialName)}={Uri.EscapeDataString(credentialValue)}";
        return new Uri(uri, UriKind.Absolute);
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
            ? "大厅服务请求失败。"
            : responseBody.Trim();
        return new LobbyServiceException(message, "http_error", statusCode);
    }

    private static string NormalizeBaseUrl(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? LanConnectLobbyEndpointDefaults.GetDefaultBaseUrl()
            : value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new LobbyServiceException("当前客户端尚未绑定大厅服务。请在开发网络设置里填写 HTTP 覆盖地址，或随构建提供 lobby-defaults.json。", "missing_lobby_base_url");
        }

        return normalized.TrimEnd('/') + "/";
    }

    private static string NormalizeWsUrl(string baseUrl)
    {
        return LanConnectLobbyEndpointDefaults.DeriveWsUrl(baseUrl);
    }

    private static string GetEndpointSource()
    {
        if (LanConnectConfig.HasLobbyServerOverrides)
        {
            return "override";
        }

        return LanConnectLobbyEndpointDefaults.HasBundledDefaults()
            ? "bundled"
            : "missing";
    }

    private static string DescribePayload(object? payload)
    {
        return payload switch
        {
            null => "no-payload",
            LobbyCreateRoomRequest create => $"create room='{create.RoomName}', passwordSet={!string.IsNullOrWhiteSpace(create.Password)}, maxPlayers={create.MaxPlayers}, localAddressCount={create.HostConnectionInfo.LocalAddresses.Count}, savedRunSlots={create.SavedRun?.Slots.Count ?? 0}",
            LobbyJoinRoomRequest join => $"join player='{join.PlayerName}', passwordSet={!string.IsNullOrWhiteSpace(join.Password)}, desiredSavePlayerNetId={(string.IsNullOrWhiteSpace(join.DesiredSavePlayerNetId) ? "<none>" : join.DesiredSavePlayerNetId)}",
            LobbyHeartbeatRequest heartbeat => $"heartbeat currentPlayers={heartbeat.CurrentPlayers}, status={heartbeat.Status}, connectedSaveSlots={heartbeat.ConnectedPlayerNetIds?.Count ?? 0}",
            LobbyDeleteRoomRequest => "delete-room",
            LobbyConnectionEventRequest connectionEvent => $"connection-event phase={connectionEvent.Phase}, candidate={connectionEvent.CandidateLabel ?? "<none>"}, endpoint={connectionEvent.CandidateEndpoint ?? "<none>"}",
            _ => payload.GetType().Name
        };
    }
}

internal sealed class LobbyServiceException : Exception
{
    public LobbyServiceException(string message, string code, int? statusCode = null, LobbyErrorDetails? details = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details;
    }

    public string Code { get; }

    public int? StatusCode { get; }

    public LobbyErrorDetails? Details { get; }
}
