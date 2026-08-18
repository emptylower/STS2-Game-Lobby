using Sts2LanConnect.Scripts;

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

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Restart_only_clears_a_stale_service_after_the_run_has_ended(
        bool isRunInProgress,
        bool hasRunNetService,
        bool expected)
    {
        object? runNetService = hasRunNetService ? new object() : null;

        Assert.Equal(
            expected,
            LanConnectMultiplayerSaveCompatibility.ShouldClearStaleRunNetServiceForRestart(
                isRunInProgress,
                runNetService));
    }

    [Fact]
    public void Main_menu_restart_clears_the_stale_run_service_before_opening_multiplayer()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "Lobby",
            "LanConnectLobbyRuntime.cs"));

        int clearIndex = source.IndexOf(
            "TryClearStaleRunNetServiceForRestart();",
            StringComparison.Ordinal);
        int openIndex = source.IndexOf(
            "TryOpenMultiplayerSubmenu(mainMenu, \"main_menu_ready\")",
            clearIndex,
            StringComparison.Ordinal);

        Assert.True(clearIndex >= 0, "Restart must clear RunManager's disconnected service.");
        Assert.True(openIndex > clearIndex, "The stale service must be cleared before the replacement lobby starts.");
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
