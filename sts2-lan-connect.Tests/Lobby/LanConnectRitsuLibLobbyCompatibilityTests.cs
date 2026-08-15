namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectRitsuLibLobbyCompatibilityTests
{
    [Fact]
    public void Compatibility_entrypoint_uses_only_the_public_sidecar_carrier()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "Lobby",
            "LanConnectRitsuLibLobbyCompatibility.cs"));

        Assert.Contains("public typed sidecar carrier enabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled in v0.6 alpha", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveLobbyNetService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TranspileRunManagerSend", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InjectResolverAfterGetter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RunManager send fallback", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
