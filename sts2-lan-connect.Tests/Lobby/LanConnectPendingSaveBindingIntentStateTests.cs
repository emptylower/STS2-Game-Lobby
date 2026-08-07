using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectPendingSaveBindingIntentStateTests
{
    [Fact]
    public void Save_then_teardown_consumes_the_pending_intent()
    {
        LanConnectPendingSaveBindingIntentState state = new();
        LanConnectPendingSaveBindingIntentState.BindingIntent captured =
            state.Capture("大厅续局", "secret", "standard", "save-1");

        Assert.True(state.TryGet(out LanConnectPendingSaveBindingIntentState.BindingIntent beforeSave));
        Assert.Same(captured, beforeSave);
        Assert.True(state.Complete(beforeSave));

        state.PreserveAcrossHostedSessionTeardown();

        Assert.False(state.TryGet(out _));
    }

    [Fact]
    public void Teardown_then_save_preserves_and_consumes_the_pending_intent()
    {
        LanConnectPendingSaveBindingIntentState state = new();
        LanConnectPendingSaveBindingIntentState.BindingIntent captured =
            state.Capture("大厅续局", null, "custom", "save-2");

        state.PreserveAcrossHostedSessionTeardown();

        Assert.True(state.TryGet(out LanConnectPendingSaveBindingIntentState.BindingIntent afterTeardown));
        Assert.Same(captured, afterTeardown);
        Assert.Equal("大厅续局", afterTeardown.RoomName);
        Assert.Equal("custom", afterTeardown.GameMode);
        Assert.Equal("save-2", afterTeardown.SaveKey);
        Assert.True(state.Complete(afterTeardown));
        Assert.False(state.TryGet(out _));
    }

    [Fact]
    public void New_non_lobby_session_discards_a_stale_intent()
    {
        LanConnectPendingSaveBindingIntentState state = new();
        state.Capture("旧大厅", null, "standard", null);
        state.PreserveAcrossHostedSessionTeardown();

        state.Discard();

        Assert.False(state.TryGet(out _));
    }
}
