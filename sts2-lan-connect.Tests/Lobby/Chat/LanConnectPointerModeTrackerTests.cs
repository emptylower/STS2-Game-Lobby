using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectPointerModeTrackerTests
{
    [Fact]
    public void Synthetic_mouse_button_inside_lockout_window_does_not_change_effective_mode()
    {
        FakeClock clock = new() { NowSeconds = 0d };
        LanConnectPointerModeTracker tracker = new(clock, LanConnectPointerMode.Touch);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Touch, Safe());
        clock.NowSeconds = 0.2d;
        tracker.ReportEvent(LanConnectPointerModeEventKind.Mouse, Safe());

        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);
        Assert.Equal(LanConnectPointerMode.Touch, tracker.InternalMode);
    }

    [Fact]
    public void Real_mouse_motion_after_lockout_window_changes_effective_mode()
    {
        FakeClock clock = new() { NowSeconds = 0d };
        LanConnectPointerModeTracker tracker = new(clock, LanConnectPointerMode.Touch);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Touch, Safe());
        clock.NowSeconds = 1.5d;
        tracker.ReportEvent(LanConnectPointerModeEventKind.Mouse, Safe());

        Assert.Equal(LanConnectPointerMode.Mouse, tracker.EffectiveMode);
        Assert.Equal(LanConnectPointerMode.Mouse, tracker.InternalMode);
    }

    [Fact]
    public void Key_event_changes_mode()
    {
        FakeClock clock = new() { NowSeconds = 0d };
        LanConnectPointerModeTracker tracker = new(clock, LanConnectPointerMode.Touch);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Key, Safe());

        Assert.Equal(LanConnectPointerMode.Mouse, tracker.EffectiveMode);
        Assert.Equal(LanConnectPointerMode.Mouse, tracker.InternalMode);
    }

    [Fact]
    public void Initial_mode_honors_constructor_parameter_touch()
    {
        AssertInitialModeHonoursConstructorParameter(LanConnectPointerMode.Touch);
    }

    [Fact]
    public void Initial_mode_honors_constructor_parameter_mouse()
    {
        AssertInitialModeHonoursConstructorParameter(LanConnectPointerMode.Mouse);
    }

    private static void AssertInitialModeHonoursConstructorParameter(LanConnectPointerMode initialMode)
    {
        FakeClock clock = new();
        LanConnectPointerModeTracker tracker = new(clock, initialMode);

        Assert.Equal(initialMode, tracker.InternalMode);
        Assert.Equal(initialMode, tracker.EffectiveMode);
    }

    [Fact]
    public void Mouse_to_touch_is_immediate_even_with_panel_open_dragging_and_tween_running()
    {
        FakeClock clock = new() { NowSeconds = 0d };
        LanConnectPointerModeTracker tracker = new(clock, LanConnectPointerMode.Mouse);
        LanConnectPointerModeContext hostile = new(PanelOpen: true, Dragging: true, TweenRunning: true);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Touch, hostile);

        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);
        Assert.Equal(LanConnectPointerMode.Touch, tracker.InternalMode);
    }

    [Fact]
    public void Touch_to_mouse_is_withheld_while_panel_is_open_and_lands_once_it_closes()
    {
        FakeClock clock = new() { NowSeconds = 0d };
        LanConnectPointerModeTracker tracker = new(clock, LanConnectPointerMode.Touch);
        LanConnectPointerModeContext panelOpen = new(PanelOpen: true, Dragging: false, TweenRunning: false);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Key, panelOpen);

        Assert.Equal(LanConnectPointerMode.Mouse, tracker.InternalMode);
        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);

        LanConnectPointerModeContext panelClosed = panelOpen with { PanelOpen = false };
        tracker.NotifyContextChanged(panelClosed);

        Assert.Equal(LanConnectPointerMode.Mouse, tracker.EffectiveMode);
    }

    [Fact]
    public void Touch_to_mouse_is_withheld_while_dragging_and_lands_once_drag_ends()
    {
        FakeClock clock = new() { NowSeconds = 0d };
        LanConnectPointerModeTracker tracker = new(clock, LanConnectPointerMode.Touch);
        LanConnectPointerModeContext dragging = new(PanelOpen: false, Dragging: true, TweenRunning: false);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Key, dragging);

        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);

        LanConnectPointerModeContext dragEnded = dragging with { Dragging = false };
        tracker.NotifyContextChanged(dragEnded);

        Assert.Equal(LanConnectPointerMode.Mouse, tracker.EffectiveMode);
    }

    [Fact]
    public void Touch_to_mouse_is_withheld_while_tween_is_running_and_lands_once_it_stops()
    {
        FakeClock clock = new() { NowSeconds = 0d };
        LanConnectPointerModeTracker tracker = new(clock, LanConnectPointerMode.Touch);
        LanConnectPointerModeContext tweening = new(PanelOpen: false, Dragging: false, TweenRunning: true);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Key, tweening);

        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);

        LanConnectPointerModeContext tweenStopped = tweening with { TweenRunning = false };
        tracker.NotifyContextChanged(tweenStopped);

        Assert.Equal(LanConnectPointerMode.Mouse, tracker.EffectiveMode);
    }

    [Fact]
    public void Touch_during_pending_switch_cancels_it_and_keeps_touch()
    {
        FakeClock clock = new() { NowSeconds = 0d };
        LanConnectPointerModeTracker tracker = new(clock, LanConnectPointerMode.Touch);
        LanConnectPointerModeContext panelOpen = new(PanelOpen: true, Dragging: false, TweenRunning: false);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Key, panelOpen);
        Assert.Equal(LanConnectPointerMode.Mouse, tracker.InternalMode);
        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Touch, panelOpen);
        Assert.Equal(LanConnectPointerMode.Touch, tracker.InternalMode);
        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);

        LanConnectPointerModeContext panelClosed = panelOpen with { PanelOpen = false };
        tracker.NotifyContextChanged(panelClosed);

        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);
    }

    [Fact]
    public void Clock_regression_and_invalid_values_do_not_wedge_the_state_machine()
    {
        FakeClock clock = new() { NowSeconds = double.NaN };
        LanConnectPointerModeTracker tracker = new(clock, LanConnectPointerMode.Touch);

        tracker.ReportEvent(LanConnectPointerModeEventKind.Touch, Safe());

        clock.NowSeconds = double.PositiveInfinity;
        tracker.ReportEvent(LanConnectPointerModeEventKind.Mouse, Safe());
        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);

        clock.NowSeconds = -50d;
        tracker.ReportEvent(LanConnectPointerModeEventKind.Mouse, Safe());
        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);

        clock.NowSeconds = 10_000d;
        tracker.ReportEvent(LanConnectPointerModeEventKind.Mouse, Safe());
        Assert.Equal(LanConnectPointerMode.Mouse, tracker.EffectiveMode);

        clock.NowSeconds = 5d;
        tracker.ReportEvent(LanConnectPointerModeEventKind.Touch, Safe());
        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);

        clock.NowSeconds = double.NaN;
        tracker.ReportEvent(LanConnectPointerModeEventKind.Mouse, Safe());
        Assert.Equal(LanConnectPointerMode.Touch, tracker.EffectiveMode);
    }

    private static LanConnectPointerModeContext Safe() =>
        new(PanelOpen: false, Dragging: false, TweenRunning: false);

    private sealed class FakeClock : ILanConnectMonotonicClock
    {
        public double NowSeconds { get; set; }
    }
}
