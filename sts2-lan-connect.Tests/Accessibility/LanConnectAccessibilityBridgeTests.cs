using System.Reflection;
using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Accessibility
{
    public sealed class LanConnectAccessibilityBridgeTests
    {
        [Fact]
        public void TryFindSetFocusedControl_accepts_current_say_the_spire2_signature_shape()
        {
        MethodInfo? method = LanConnectAccessibilityBridge.TryFindSetFocusedControlMethod(
                new[] { typeof(Fakes.SayTheSpire2.UI.UIManager).Assembly },
                "Sts2LanConnect.Tests.Accessibility.Fakes.SayTheSpire2.UI.UIManager");

            Assert.NotNull(method);
            Assert.Equal("SetFocusedControl", method!.Name);
        }

        [Fact]
        public void TryFindSetFocusedControl_rejects_changed_signature_shape()
        {
            MethodInfo? method = LanConnectAccessibilityBridge.TryFindSetFocusedControlMethod(
                new[] { typeof(BrokenSayTheSpire2.UI.UIManager).Assembly },
                "BrokenSayTheSpire2.UI.UIManager");

            Assert.Null(method);
        }
    }
}

namespace Sts2LanConnect.Tests.Accessibility.Fakes.SayTheSpire2.UI
{
    public static class UIManager
    {
        public static void SetFocusedControl(Control control, object? preResolved = null)
        {
        }
    }
}

namespace BrokenSayTheSpire2.UI
{
    public static class UIManager
    {
        public static void SetFocusedControl(string notAControl)
        {
        }
    }
}
