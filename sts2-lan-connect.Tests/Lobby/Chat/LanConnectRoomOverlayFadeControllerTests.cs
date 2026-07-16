using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectRoomOverlayFadeControllerTests
{
    [Fact]
    public void Eligible_for_exactly_five_seconds_emits_one_normal_fade_edge()
    {
        FakeClock clock = new();
        LanConnectRoomOverlayFadeController controller = new(clock);
        LanConnectRoomOverlayFadeInput input = Eligible();

        Assert.Equal(LanConnectRoomOverlayFadePhase.Waiting, controller.Tick(input).Phase);
        clock.NowSeconds = 4.999d;
        Assert.Null(controller.Tick(input).Command);
        clock.NowSeconds = 5d;
        LanConnectRoomOverlayFadeTick edge = controller.Tick(input);

        Assert.Equal(LanConnectRoomOverlayFadePhase.Fading, edge.Phase);
        Assert.Equal(0f, edge.Command?.TargetAlpha);
        Assert.Equal(0.6d, edge.Command?.DurationSeconds);
        Assert.True(edge.Command?.KillTween);
        Assert.Null(controller.Tick(input).Command);

        clock.NowSeconds = 5.601d;
        Assert.Equal(LanConnectRoomOverlayFadePhase.Faded, controller.Tick(input).Phase);
        Assert.Null(controller.Tick(input).Command);
    }

    [Fact]
    public void Reduced_motion_switches_to_faded_without_a_tween()
    {
        FakeClock clock = new();
        LanConnectRoomOverlayFadeController controller = new(clock);
        LanConnectRoomOverlayFadeInput input = Eligible() with { ReducedMotion = true };
        controller.Tick(input);
        clock.NowSeconds = 5d;

        LanConnectRoomOverlayFadeTick edge = controller.Tick(input);

        Assert.Equal(LanConnectRoomOverlayFadePhase.Faded, edge.Phase);
        Assert.Equal(0d, edge.Command?.DurationSeconds);
        Assert.Equal(0f, edge.Command?.TargetAlpha);
        Assert.Null(controller.Tick(input).Command);
    }

    [Fact]
    public void Every_blocker_prevents_fade_until_it_is_continuously_absent()
    {
        LanConnectRoomOverlayFadeInput baseline = Eligible();
        LanConnectRoomOverlayFadeInput[] blocked =
        [
            baseline with { Open = false },
            baseline with { Pinned = true },
            baseline with { HasFocus = true },
            baseline with { Hovered = true },
            baseline with { Dragging = true },
            baseline with { PreviewVisible = true },
            baseline with { PickerOrConfirmationVisible = true },
            baseline with { ModalVisible = true },
            baseline with { HasDeliveryBlocker = true },
            baseline with { RoomUnread = 1 },
            baseline with { ServerUnread = 1 },
            baseline with { RoomNewBelow = 1 },
            baseline with { ServerNewBelow = 1 }
        ];

        foreach (LanConnectRoomOverlayFadeInput input in blocked)
        {
            FakeClock clock = new();
            LanConnectRoomOverlayFadeController controller = new(clock);
            controller.Tick(input);
            clock.NowSeconds = 10d;
            Assert.Equal(LanConnectRoomOverlayFadePhase.Awake, controller.Tick(input).Phase);
            Assert.Null(controller.Tick(input).Command);

            controller.Tick(baseline);
            clock.NowSeconds = 14.999d;
            Assert.Null(controller.Tick(baseline).Command);
            clock.NowSeconds = 15d;
            Assert.Equal(0f, controller.Tick(baseline).Command?.TargetAlpha);
        }
    }

    [Fact]
    public void Activity_and_incoming_tokens_wake_same_frame_and_restart_idle_delay()
    {
        FakeClock clock = new();
        LanConnectRoomOverlayFadeController controller = new(clock);
        LanConnectRoomOverlayFadeInput input = Eligible();
        Fade(controller, clock, input);

        LanConnectRoomOverlayFadeTick activityWake = controller.Tick(input with { ActivityToken = 1 });
        AssertWake(activityWake);
        clock.NowSeconds = 9.999d;
        Assert.Null(controller.Tick(input with { ActivityToken = 1 }).Command);
        clock.NowSeconds = 10d;
        Assert.Equal(0f, controller.Tick(input with { ActivityToken = 1 }).Command?.TargetAlpha);

        LanConnectRoomOverlayFadeTick incomingWake = controller.Tick(input with
        {
            ActivityToken = 1,
            RoomRemoteArrivalRevision = 1
        });
        AssertWake(incomingWake);
        Assert.Equal(LanConnectRoomOverlayFadePhase.Waiting, incomingWake.Phase);

        Fade(controller, clock, input with
        {
            ActivityToken = 1,
            RoomRemoteArrivalRevision = 1
        });
        LanConnectRoomOverlayFadeTick serverWake = controller.Tick(input with
        {
            ActivityToken = 1,
            RoomRemoteArrivalRevision = 1,
            ServerRemoteArrivalRevision = 1
        });
        AssertWake(serverWake);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Text_effects_preference_maps_to_reduced_motion(
        bool textEffectsEnabled,
        bool expectedReducedMotion)
    {
        Assert.Equal(
            expectedReducedMotion,
            LanConnectRoomChatOverlay.ReducedMotionFromTextEffectsEnabled(textEffectsEnabled));
    }

    [Fact]
    public void Focus_hover_unread_and_delivery_blockers_wake_and_reset_continuous_timer()
    {
        LanConnectRoomOverlayFadeInput baseline = Eligible();
        LanConnectRoomOverlayFadeInput[] wakeInputs =
        [
            baseline with { HasFocus = true },
            baseline with { Hovered = true },
            baseline with { RoomUnread = 1 },
            baseline with { HasDeliveryBlocker = true }
        ];
        foreach (LanConnectRoomOverlayFadeInput wakeInput in wakeInputs)
        {
            FakeClock clock = new();
            LanConnectRoomOverlayFadeController controller = new(clock);
            Fade(controller, clock, baseline);
            AssertWake(controller.Tick(wakeInput));
            Assert.Null(controller.Tick(wakeInput).Command);

            controller.Tick(baseline);
            clock.NowSeconds = 9.999d;
            Assert.Null(controller.Tick(baseline).Command);
            clock.NowSeconds = 10d;
            Assert.Equal(0f, controller.Tick(baseline).Command?.TargetAlpha);
        }
    }

    [Fact]
    public void Clock_regression_fails_safe_to_visible_and_restarts_timer()
    {
        FakeClock clock = new() { NowSeconds = 100d };
        LanConnectRoomOverlayFadeController controller = new(clock);
        LanConnectRoomOverlayFadeInput input = Eligible();
        controller.Tick(input);
        clock.NowSeconds = 104d;
        controller.Tick(input);
        clock.NowSeconds = 90d;

        LanConnectRoomOverlayFadeTick regressed = controller.Tick(input);

        AssertWake(regressed);
        clock.NowSeconds = 94.999d;
        Assert.Null(controller.Tick(input).Command);
        clock.NowSeconds = 95d;
        Assert.Equal(0f, controller.Tick(input).Command?.TargetAlpha);
    }

    private static void Fade(
        LanConnectRoomOverlayFadeController controller,
        FakeClock clock,
        LanConnectRoomOverlayFadeInput input)
    {
        controller.Tick(input);
        clock.NowSeconds += 5d;
        Assert.Equal(0f, controller.Tick(input).Command?.TargetAlpha);
    }

    private static void AssertWake(LanConnectRoomOverlayFadeTick tick)
    {
        Assert.Equal(1f, tick.Command?.TargetAlpha);
        Assert.Equal(0d, tick.Command?.DurationSeconds);
        Assert.True(tick.Command?.KillTween);
    }

    private static LanConnectRoomOverlayFadeInput Eligible() => new(
        Open: true,
        Pinned: false,
        HasFocus: false,
        Hovered: false,
        Dragging: false,
        PreviewVisible: false,
        PickerOrConfirmationVisible: false,
        ModalVisible: false,
        HasDeliveryBlocker: false,
        RoomUnread: 0,
        ServerUnread: 0,
        RoomNewBelow: 0,
        ServerNewBelow: 0,
        ActivityToken: 0,
        RoomRemoteArrivalRevision: 0,
        ServerRemoteArrivalRevision: 0,
        ReducedMotion: false);

    private sealed class FakeClock : ILanConnectMonotonicClock
    {
        public double NowSeconds { get; set; }
    }
}
