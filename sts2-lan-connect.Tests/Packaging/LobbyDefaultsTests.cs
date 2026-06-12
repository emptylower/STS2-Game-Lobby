using System.Text.Json;

namespace Sts2LanConnect.Tests.Packaging;

public sealed class LobbyDefaultsTests
{
    [Fact]
    public void Checked_in_lobby_defaults_include_discovery_endpoint_and_seed_peers()
    {
        string root = FindRepositoryRoot();
        string defaultsJson = File.ReadAllText(Path.Combine(root, "sts2-lan-connect", "lobby-defaults.json"));
        string seedsJson = File.ReadAllText(Path.Combine(root, "data", "seeds.json"));

        using JsonDocument defaults = JsonDocument.Parse(defaultsJson);
        using JsonDocument seeds = JsonDocument.Parse(seedsJson);

        JsonElement rootElement = defaults.RootElement;
        Assert.Equal("http://47.111.146.69:8787", rootElement.GetProperty("baseUrl").GetString());
        Assert.Equal("https://sts2-gamelobby-register.xyz", rootElement.GetProperty("cfDiscoveryBaseUrl").GetString());

        string[] packagedSeeds = rootElement.GetProperty("seedPeers")
            .EnumerateArray()
            .Select(seed => seed.GetString() ?? string.Empty)
            .ToArray();
        string[] sourceSeeds = seeds.RootElement.GetProperty("seeds")
            .EnumerateArray()
            .Select(seed => seed.GetProperty("address").GetString() ?? string.Empty)
            .ToArray();

        Assert.NotEmpty(packagedSeeds);
        Assert.Equal(sourceSeeds, packagedSeeds);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "sts2-lan-connect", "lobby-defaults.json")) &&
                File.Exists(Path.Combine(directory.FullName, "data", "seeds.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate STS2-Game-Lobby repository root.");
    }
}
