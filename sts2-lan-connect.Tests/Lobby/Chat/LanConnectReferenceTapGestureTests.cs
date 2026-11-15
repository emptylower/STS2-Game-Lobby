using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectReferenceTapGestureTests
{
    [Fact]
    public void One_tap_activates_on_release_and_suppresses_duplicate_meta_click_and_hover()
    {
        LanConnectReferenceTapGesture gesture = new();
        gesture.Press(0, new Vector2(100, 100), 1_000);
        Assert.False(gesture.TryActivateReference(1_001));
        Assert.True(gesture.IgnoreHover(1_001));

        Assert.True(gesture.Release(0, new Vector2(104, 103), 1_100, cancelled: false));
        Assert.True(gesture.TryActivateReference(1_100));
        Assert.False(gesture.TryActivateReference(1_101));
        Assert.True(gesture.IgnoreHover(1_101));
        Assert.False(gesture.IgnoreHover(1_701));
        Assert.True(gesture.TryActivateReference(1_701));
    }

    [Fact]
    public void Scrolling_does_not_activate_even_if_the_finger_returns_to_its_start()
    {
        LanConnectReferenceTapGesture gesture = new();
        gesture.Press(0, Vector2.Zero, 1_000);
        gesture.Drag(0, new Vector2(0, 50), 1_010);
        gesture.Drag(0, Vector2.Zero, 1_020);

        Assert.False(gesture.Release(0, Vector2.Zero, 1_030, cancelled: false));
        Assert.False(gesture.TryActivateReference(1_031));
    }

    [Fact]
    public void Cancelled_touch_and_multitouch_do_not_activate()
    {
        LanConnectReferenceTapGesture gesture = new();
        gesture.Press(0, Vector2.Zero, 1_000);
        Assert.False(gesture.Release(0, Vector2.Zero, 1_001, cancelled: true));
        Assert.False(gesture.TryActivateReference(1_002));

        gesture.Press(0, Vector2.Zero, 2_000);
        gesture.Press(1, Vector2.One, 2_001);
        Assert.False(gesture.Release(0, Vector2.Zero, 2_002, cancelled: false));
        Assert.False(gesture.TryActivateReference(2_003));
    }

    [Fact]
    public void Another_finger_release_does_not_finish_the_original_tap()
    {
        LanConnectReferenceTapGesture gesture = new();
        gesture.Press(0, Vector2.Zero, 1_000);

        Assert.False(gesture.Release(1, Vector2.Zero, 1_010, cancelled: false));
        Assert.True(gesture.TouchInProgress);
        Assert.False(gesture.TryActivateReference(1_011));
        Assert.True(gesture.Release(0, Vector2.Zero, 1_020, cancelled: false));
    }

    [Fact]
    public void A_real_mouse_press_can_resume_reference_interaction_after_touch()
    {
        LanConnectReferenceTapGesture gesture = new();
        gesture.Press(0, Vector2.Zero, 1_000);
        gesture.Release(0, Vector2.Zero, 1_010, cancelled: false);
        gesture.TryActivateReference(1_010);

        gesture.UseMouse();

        Assert.True(gesture.TryActivateReference(1_020));
        Assert.False(gesture.IgnoreHover(1_020));
    }

    [Fact]
    public void Touch_mouse_sequence_ignores_the_matching_press_but_preserves_outside_mouse_click()
    {
        LanConnectReferenceTapGesture gesture = new();
        LanConnectReferenceTouchMouseDeduplicator dedupe = new();
        Vector2 message = new(100, 100);
        gesture.Press(0, message, 1_000);
        dedupe.ObserveTouch(message, 1_000);
        Assert.True(dedupe.IsSyntheticMouse(message, 1_001));
        Assert.False(gesture.TryActivateReference(1_001));

        dedupe.ObserveTouch(message, 1_100);
        Assert.True(gesture.Release(0, message, 1_100, cancelled: false));
        Assert.True(gesture.TryActivateReference(1_100));
        Assert.True(dedupe.IsSyntheticMouse(message, 1_101));
        Assert.False(gesture.TryActivateReference(1_102));

        Assert.False(dedupe.IsSyntheticMouse(new Vector2(500, 500), 1_103));
    }
}
