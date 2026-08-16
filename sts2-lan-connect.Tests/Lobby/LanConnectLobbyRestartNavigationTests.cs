namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyRestartNavigationTests
{
    [Fact]
    public void Reopening_a_cached_multiplayer_submenu_explicitly_resumes_pending_restart()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "Lobby",
            "LanConnectLobbyRuntime.cs"));

        int openIndex = source.IndexOf("mainMenu.OpenMultiplayerSubmenu();", StringComparison.Ordinal);
        int deferredResumeIndex = source.IndexOf(
            "ResumePendingRestartFromCachedSubmenu(mainMenu, source)",
            StringComparison.Ordinal);
        int readyIndex = source.IndexOf("OnMultiplayerSubmenuReady(submenu);", deferredResumeIndex, StringComparison.Ordinal);

        Assert.True(openIndex >= 0, "Restart navigation must open the multiplayer submenu.");
        Assert.True(
            deferredResumeIndex > openIndex,
            "Restart navigation must defer resuming until the cached submenu has entered the tree.");
        Assert.True(
            readyIndex > deferredResumeIndex,
            "The cached submenu path must explicitly resume pending restart work without relying on _Ready.");
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
