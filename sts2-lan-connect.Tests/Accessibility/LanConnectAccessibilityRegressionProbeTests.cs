using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Accessibility;

public sealed class LanConnectAccessibilityRegressionProbeTests
{
    [Theory]
    [InlineData(Key.D)]
    [InlineData(Key.F)]
    public void CurrentChatCaptureDoesNotConsumePrintableGameKeys(Key key)
    {
        LineEdit chatInput = Uninitialized<LineEdit>();

        bool captured = LanConnectAccessibilityKeyboard.ShouldCaptureChatTextSubmitKey(
            key,
            pressed: true,
            echo: false,
            chatInput,
            chatInput,
            panelOpen: true);

        Assert.False(captured);
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.KpEnter)]
    public void CurrentChatCaptureOnlyConsumesEnterForSubmission(Key key)
    {
        LineEdit chatInput = Uninitialized<LineEdit>();

        bool captured = LanConnectAccessibilityKeyboard.ShouldCaptureChatTextSubmitKey(
            key,
            pressed: true,
            echo: false,
            chatInput,
            chatInput,
            panelOpen: true);

        Assert.True(captured);
    }

    private static T Uninitialized<T>() where T : class
    {
        return (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
