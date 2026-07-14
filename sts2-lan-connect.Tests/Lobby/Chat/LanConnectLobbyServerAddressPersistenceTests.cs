using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectLobbyServerAddressPersistenceTests
{
    [Fact]
    public void Bundled_default_persists_empty_override_and_actual_last_used_authority()
    {
        LanConnectPersistedLobbyServerAddress result = LanConnectConfig.NormalizePersistedLobbyServerAddress(
            "  HTTPS://lobby.example.com/  ",
            "https://lobby.example.com");

        Assert.Equal(string.Empty, result.ServerOverride);
        Assert.Equal("HTTPS://lobby.example.com/", result.LastUsedServerAddress);
    }

    [Fact]
    public void Custom_server_persists_trimmed_override_and_last_used_authority()
    {
        LanConnectPersistedLobbyServerAddress result = LanConnectConfig.NormalizePersistedLobbyServerAddress(
            "  https://custom.example.com/path  ",
            "https://lobby.example.com");

        Assert.Equal("https://custom.example.com/path", result.ServerOverride);
        Assert.Equal("https://custom.example.com/path", result.LastUsedServerAddress);
    }
}
