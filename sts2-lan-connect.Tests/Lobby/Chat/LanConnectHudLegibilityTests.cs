using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectHudLegibilityTests
{
    [Theory]
    [InlineData(68f, 36f, 68f, 44f)]
    [InlineData(104f, 36f, 104f, 44f)]
    [InlineData(132f, 44f, 132f, 44f)]
    [InlineData(0f, 0f, 44f, 44f)]
    [InlineData(56f, 56f, 56f, 56f)]
    public void Touch_targets_are_raised_to_the_floor_without_shrinking(
        float requestedX,
        float requestedY,
        float expectedX,
        float expectedY)
    {
        Vector2 result = LanConnectHudLegibility.EnsureTouchTarget(new Vector2(requestedX, requestedY));

        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
    }

    [Fact]
    public void Rest_plate_is_translucent_enough_to_read_as_chrome_free()
    {
        Assert.InRange(LanConnectHudLegibility.RestPlateColor.A, 0.40f, 0.50f);
        Assert.True(LanConnectHudLegibility.HoverPlateColor.A > LanConnectHudLegibility.RestPlateColor.A);
        Assert.True(LanConnectHudLegibility.PressedPlateColor.A > LanConnectHudLegibility.HoverPlateColor.A);
    }

    [Fact]
    public void Outline_is_dark_and_thick_enough_to_survive_a_bright_background()
    {
        Assert.True(LanConnectHudLegibility.OutlineColor.A >= 0.75f);
        Assert.True(LanConnectHudLegibility.OutlineColor.R <= 0.1f);
        Assert.True(LanConnectHudLegibility.OutlineColor.G <= 0.1f);
        Assert.True(LanConnectHudLegibility.OutlineColor.B <= 0.1f);

        // Locked at 2, not >= 2: the screenshot harness (LanConnectHudLegibilityScreenshotTests)
        // showed 3px visibly clogging the 13px Chinese sample's counters on a 10px pixel test font,
        // while 2px stayed legible on the brightest background band. Don't "improve" this back up.
        Assert.Equal(2, LanConnectHudLegibility.OutlineSize);
    }
}
