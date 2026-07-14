namespace Sts2LanConnect.Scripts;

internal enum LanConnectAccessibilityHotkey
{
    F7Invite,
    F8Chat,
    TChat
}

internal enum LanConnectAccessibilityHotkeyAction
{
    None,
    AcceptVisibleInvite,
    ShowClipboardInvite,
    ToggleChat
}

internal readonly record struct LanConnectAccessibilityHotkeyContext(
    bool TextInputHasFocus,
    bool InviteDialogVisible,
    bool ClipboardHasInvite,
    bool ChatAvailable,
    bool BlockingModalVisible = false);

internal readonly record struct LanConnectAccessibilityHotkeyRoute(
    LanConnectAccessibilityHotkeyAction Action);

internal static class LanConnectAccessibilityHotkeyRouter
{
    public static LanConnectAccessibilityHotkeyRoute Route(
        LanConnectAccessibilityHotkey hotkey,
        LanConnectAccessibilityHotkeyContext context)
    {
        if (context.BlockingModalVisible && hotkey == LanConnectAccessibilityHotkey.F8Chat)
        {
            return new LanConnectAccessibilityHotkeyRoute(LanConnectAccessibilityHotkeyAction.None);
        }

        if (context.TextInputHasFocus && hotkey == LanConnectAccessibilityHotkey.TChat)
        {
            return new LanConnectAccessibilityHotkeyRoute(LanConnectAccessibilityHotkeyAction.None);
        }

        return hotkey switch
        {
            LanConnectAccessibilityHotkey.F7Invite when context.InviteDialogVisible => new LanConnectAccessibilityHotkeyRoute(LanConnectAccessibilityHotkeyAction.AcceptVisibleInvite),
            LanConnectAccessibilityHotkey.F7Invite when context.ClipboardHasInvite => new LanConnectAccessibilityHotkeyRoute(LanConnectAccessibilityHotkeyAction.ShowClipboardInvite),
            LanConnectAccessibilityHotkey.F8Chat when context.ChatAvailable => new LanConnectAccessibilityHotkeyRoute(LanConnectAccessibilityHotkeyAction.ToggleChat),
            LanConnectAccessibilityHotkey.TChat when context.ChatAvailable => new LanConnectAccessibilityHotkeyRoute(LanConnectAccessibilityHotkeyAction.ToggleChat),
            _ => new LanConnectAccessibilityHotkeyRoute(LanConnectAccessibilityHotkeyAction.None)
        };
    }
}
