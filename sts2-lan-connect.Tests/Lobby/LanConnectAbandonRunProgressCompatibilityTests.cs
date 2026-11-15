using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectAbandonRunProgressCompatibilityTests
{
    [Fact]
    public void Records_abandonment_as_loss_on_legacy_void_method()
    {
        LegacySaveManager manager = new();
        object run = new();

        LanConnectMultiplayerSaveCompatibility.UpdateProgressWithAbandonedRun(manager, run);

        Assert.Same(run, manager.Run);
        Assert.False(manager.Victory);
    }

    [Fact]
    public void Records_abandonment_as_loss_on_current_result_method()
    {
        CurrentSaveManager manager = new();
        object run = new();

        LanConnectMultiplayerSaveCompatibility.UpdateProgressWithAbandonedRun(manager, run);

        Assert.Same(run, manager.Run);
        Assert.False(manager.Victory);
    }

    private sealed class LegacySaveManager
    {
        public object? Run { get; private set; }
        public bool Victory { get; private set; } = true;

        public void UpdateProgressWithRunData(object run, bool victory)
        {
            Run = run;
            Victory = victory;
        }
    }

    private sealed class CurrentSaveManager
    {
        public object? Run { get; private set; }
        public bool Victory { get; private set; } = true;

        public object UpdateProgressWithRunData(object run, bool victory)
        {
            Run = run;
            Victory = victory;
            return new object();
        }
    }
}
