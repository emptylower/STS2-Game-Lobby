using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Accessibility;

public sealed class LanConnectAccessibilityHotkeyRouterTests
{
    [Fact]
    public void Route_f7_accepts_visible_invite_dialog()
    {
        LanConnectAccessibilityHotkeyRoute route = LanConnectAccessibilityHotkeyRouter.Route(
            LanConnectAccessibilityHotkey.F7Invite,
            new LanConnectAccessibilityHotkeyContext(
                TextInputHasFocus: false,
                InviteDialogVisible: true,
                ClipboardHasInvite: false,
                ChatAvailable: false));

        Assert.Equal(LanConnectAccessibilityHotkeyAction.AcceptVisibleInvite, route.Action);
    }

    [Fact]
    public void Route_f7_shows_clipboard_invite_even_when_text_input_has_focus()
    {
        LanConnectAccessibilityHotkeyRoute route = LanConnectAccessibilityHotkeyRouter.Route(
            LanConnectAccessibilityHotkey.F7Invite,
            new LanConnectAccessibilityHotkeyContext(
                TextInputHasFocus: true,
                InviteDialogVisible: false,
                ClipboardHasInvite: true,
                ChatAvailable: false));

        Assert.Equal(LanConnectAccessibilityHotkeyAction.ShowClipboardInvite, route.Action);
    }

    [Fact]
    public void Route_f8_toggles_chat_when_chat_is_available_and_text_input_is_not_focused()
    {
        LanConnectAccessibilityHotkeyRoute route = LanConnectAccessibilityHotkeyRouter.Route(
            LanConnectAccessibilityHotkey.F8Chat,
            new LanConnectAccessibilityHotkeyContext(
                TextInputHasFocus: false,
                InviteDialogVisible: false,
                ClipboardHasInvite: false,
                ChatAvailable: true));

        Assert.Equal(LanConnectAccessibilityHotkeyAction.ToggleChat, route.Action);
    }

    [Fact]
    public void Route_t_chat_shortcut_is_ignored_when_text_input_has_focus()
    {
        LanConnectAccessibilityHotkeyRoute route = LanConnectAccessibilityHotkeyRouter.Route(
            LanConnectAccessibilityHotkey.TChat,
            new LanConnectAccessibilityHotkeyContext(
                TextInputHasFocus: true,
                InviteDialogVisible: false,
                ClipboardHasInvite: false,
                ChatAvailable: true));

        Assert.Equal(LanConnectAccessibilityHotkeyAction.None, route.Action);
    }

    [Fact]
    public void Route_f8_chat_shortcut_still_toggles_when_text_input_has_focus()
    {
        LanConnectAccessibilityHotkeyRoute route = LanConnectAccessibilityHotkeyRouter.Route(
            LanConnectAccessibilityHotkey.F8Chat,
            new LanConnectAccessibilityHotkeyContext(
                TextInputHasFocus: true,
                InviteDialogVisible: false,
                ClipboardHasInvite: false,
                ChatAvailable: true));

        Assert.Equal(LanConnectAccessibilityHotkeyAction.ToggleChat, route.Action);
    }

    [Fact]
    public void Route_f8_chat_shortcut_is_ignored_while_blocking_modal_is_visible()
    {
        LanConnectAccessibilityHotkeyRoute route = LanConnectAccessibilityHotkeyRouter.Route(
            LanConnectAccessibilityHotkey.F8Chat,
            new LanConnectAccessibilityHotkeyContext(
                TextInputHasFocus: false,
                InviteDialogVisible: false,
                ClipboardHasInvite: false,
                ChatAvailable: true,
                BlockingModalVisible: true));

        Assert.Equal(LanConnectAccessibilityHotkeyAction.None, route.Action);
    }
}
