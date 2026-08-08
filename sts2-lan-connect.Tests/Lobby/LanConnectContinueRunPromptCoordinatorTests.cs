using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectContinueRunPromptCoordinatorTests
{
    [Fact]
    public async Task Owner_teardown_while_prompt_is_open_leaves_the_save_promptable()
    {
        LanConnectContinueRunPromptCoordinator coordinator = new();
        Assert.True(coordinator.TryBegin(1, "save-1", out var firstLease));
        Task<LanConnectContinueRunPromptCoordinator.PromptResolution> resolving = coordinator.ResolveAsync(
            firstLease,
            cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ContinueWith(
                    _ => (string?)null,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default),
            _ => throw new Xunit.Sdk.XunitException("Owner teardown must not persist a choice."));

        coordinator.ClearScreen(1);

        LanConnectContinueRunPromptCoordinator.PromptResolution resolution =
            await resolving.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(resolution.Choice);
        Assert.False(resolution.Persisted);
        Assert.True(coordinator.TryBegin(2, "save-1", out var retryLease));
        coordinator.ClearScreen(retryLease.ScreenId);
    }

    [Fact]
    public async Task Cancellation_persists_nothing_and_releases_the_save_key()
    {
        LanConnectContinueRunPromptCoordinator coordinator = new();
        List<string> writes = new();
        Assert.True(coordinator.TryBegin(10, "save-2", out var lease));

        LanConnectContinueRunPromptCoordinator.PromptResolution result = await coordinator.ResolveAsync(
            lease,
            _ => Task.FromResult<string?>(null),
            choice =>
            {
                writes.Add(choice);
                return true;
            });

        Assert.Null(result.Choice);
        Assert.False(result.Persisted);
        Assert.Empty(writes);
        Assert.True(coordinator.TryBegin(11, "save-2", out var retryLease));
        coordinator.ClearScreen(retryLease.ScreenId);
    }

    [Theory]
    [InlineData("lobby")]
    [InlineData("lan")]
    public async Task Explicit_button_choice_is_the_only_persisted_outcome(string choice)
    {
        LanConnectContinueRunPromptCoordinator coordinator = new();
        List<string> writes = new();
        Assert.True(coordinator.TryBegin(20, "save-3", out var lease));

        LanConnectContinueRunPromptCoordinator.PromptResolution result = await coordinator.ResolveAsync(
            lease,
            _ => Task.FromResult<string?>(choice),
            selected =>
            {
                writes.Add(selected);
                return true;
            });

        Assert.Equal(choice, result.Choice);
        Assert.True(result.Persisted);
        Assert.Equal(choice, Assert.Single(writes));
    }

    [Fact]
    public async Task Failed_persistence_does_not_report_the_choice_as_recorded()
    {
        LanConnectContinueRunPromptCoordinator coordinator = new();
        Assert.True(coordinator.TryBegin(30, "save-4", out var lease));

        LanConnectContinueRunPromptCoordinator.PromptResolution result = await coordinator.ResolveAsync(
            lease,
            _ => Task.FromResult<string?>(LanConnectHostChannels.Lobby),
            _ => false);

        Assert.Equal(LanConnectHostChannels.Lobby, result.Choice);
        Assert.False(result.Persisted);
    }
}
