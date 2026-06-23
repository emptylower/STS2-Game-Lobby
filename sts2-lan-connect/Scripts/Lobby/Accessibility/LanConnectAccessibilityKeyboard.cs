using Godot;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectAccessibilityKeyboard
{
    public static bool ShouldCaptureChatTextSubmitKey(
        Key key,
        bool pressed,
        bool echo,
        Control? focusOwner,
        LineEdit? chatInput,
        bool panelOpen)
    {
        if (!panelOpen || !pressed || echo || chatInput == null)
        {
            return false;
        }

        if (!ReferenceEquals(focusOwner, chatInput))
        {
            return false;
        }

        return key is Key.Enter or Key.KpEnter;
    }
}
