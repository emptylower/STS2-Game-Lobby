using System.Net;
using System.Text;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyInstallationIdentityTests
{
    [Fact]
    public void Create_and_join_requests_serialize_client_installation_id_with_contract_casing()
    {
        const string installationId = "76561198999999999";

        using JsonDocument create = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyCreateRoomRequest { ClientInstallationId = installationId },
            LanConnectJson.Options));
        using JsonDocument join = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyJoinRoomRequest { ClientInstallationId = installationId },
            LanConnectJson.Options));

        Assert.Equal(installationId, create.RootElement.GetProperty("clientInstallationId").GetString());
        Assert.Equal(installationId, join.RootElement.GetProperty("clientInstallationId").GetString());
    }

    [Fact]
    public void Missing_installation_ids_are_omitted_for_legacy_service_compatibility()
    {
        using JsonDocument create = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyCreateRoomRequest { ClientInstallationId = null! },
            LanConnectJson.Options));
        using JsonDocument join = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyJoinRoomRequest { ClientInstallationId = null! },
            LanConnectJson.Options));

        Assert.False(create.RootElement.TryGetProperty("clientInstallationId", out _));
        Assert.False(join.RootElement.TryGetProperty("clientInstallationId", out _));
    }

    [Fact]
    public async Task Create_api_sends_the_installation_id_in_the_http_request()
    {
        RecordingHandler handler = new("""{"roomId":"room-1","room":{}}""");
        using LobbyApiClient client = new(
            "https://lobby.example",
            httpMessageHandler: handler,
            diagnosticSink: _ => { });

        await client.CreateRoomAsync(new LobbyCreateRoomRequest
        {
            RoomName = "Room",
            HostPlayerName = "Host",
            ClientInstallationId = "install-host",
            Version = "1.0.0",
            ModVersion = "1.0.0",
            MaxPlayers = 4,
            HostConnectionInfo = new LobbyHostConnectionInfo { EnetPort = 33771 }
        });

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://lobby.example/rooms", handler.Uri);
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("install-host", body.RootElement.GetProperty("clientInstallationId").GetString());
    }

    [Fact]
    public async Task Join_api_sends_slot_and_installation_ids_as_distinct_fields()
    {
        RecordingHandler handler = new("""{"ticketId":"ticket-1"}""");
        using LobbyApiClient client = new(
            "https://lobby.example",
            httpMessageHandler: handler,
            diagnosticSink: _ => { });

        LobbyJoinRoomResponse response = await client.JoinRoomAsync("room /1", new LobbyJoinRoomRequest
        {
            PlayerName = "Joiner",
            PlayerNetId = "save-slot-owner",
            ClientInstallationId = "install-current-occupant",
            Version = "1.0.0",
            ModVersion = "1.0.0"
        });

        Assert.Equal("ticket-1", response.TicketId);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://lobby.example/rooms/room%20%2F1/join", handler.Uri);
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("save-slot-owner", body.RootElement.GetProperty("playerNetId").GetString());
        Assert.Equal(
            "install-current-occupant",
            body.RootElement.GetProperty("clientInstallationId").GetString());
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public string? Uri { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri!.AbsoluteUri;
            Body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
