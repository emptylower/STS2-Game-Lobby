using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyKickBindingTests
{
    [Fact]
    public void Modern_kick_carries_slot_and_opaque_binding_with_contract_casing()
    {
        LobbyControlEnvelope envelope = LanConnectLobbyRuntime.BuildKickPlayerEnvelope(
            "room-1",
            "save-slot-owner",
            "Current Occupant",
            "binding-current");

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(envelope, LanConnectJson.Options));
        Assert.Equal("kick_player", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("save-slot-owner", json.RootElement.GetProperty("playerNetId").GetString());
        Assert.Equal("binding-current", json.RootElement.GetProperty("bindingId").GetString());
        Assert.Equal("save-slot-owner", json.RootElement.GetProperty("targetPlayerNetId").GetString());
        Assert.False(json.RootElement.TryGetProperty("clientInstallationId", out _));
    }

    [Fact]
    public void Missing_server_handle_uses_only_the_legacy_slot_field()
    {
        LobbyControlEnvelope envelope = LanConnectLobbyRuntime.BuildKickPlayerEnvelope(
            "room-1",
            "save-slot-owner",
            "Legacy Occupant",
            null);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(envelope, LanConnectJson.Options));
        Assert.Equal("save-slot-owner", json.RootElement.GetProperty("targetPlayerNetId").GetString());
        Assert.False(json.RootElement.TryGetProperty("playerNetId", out _));
        Assert.False(json.RootElement.TryGetProperty("bindingId", out _));
    }
}
