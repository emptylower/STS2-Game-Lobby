using Godot;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectChatInputAction
{
    None,
    OpenAndFocus,
    Send,
    InsertNewline,
    CloseEmojiPicker,
    ClosePopup,
    ClosePreview,
    ReleaseInputFocus,
    CloseOverlay,
    ShowOverlay,
    PinOverlay,
    HideOverlay
}

internal static class LanConnectChatInputRouter
{
    internal static bool IsReferenceToggle(
        Key key,
        bool pressed,
        bool echo,
        bool altPressed) =>
        key == Key.R && pressed && !echo && altPressed;

    internal static LanConnectChatInputAction RouteEnter(
        bool inputFocused,
        bool shiftPressed,
        bool blockingModalVisible) =>
        (inputFocused, shiftPressed, blockingModalVisible) switch
        {
            (_, _, true) => LanConnectChatInputAction.None,
            (true, true, false) => LanConnectChatInputAction.InsertNewline,
            (true, false, false) => LanConnectChatInputAction.Send,
            (false, _, false) => LanConnectChatInputAction.OpenAndFocus
        };

    internal static LanConnectChatInputAction RouteEscape(
        bool emojiPickerVisible,
        bool popupVisible,
        bool previewVisible,
        bool inputFocused,
        bool overlayOpen) =>
        (emojiPickerVisible, popupVisible, previewVisible, inputFocused, overlayOpen) switch
        {
            (true, _, _, _, _) => LanConnectChatInputAction.CloseEmojiPicker,
            (false, true, _, _, _) => LanConnectChatInputAction.ClosePopup,
            (false, false, true, _, _) => LanConnectChatInputAction.ClosePreview,
            (false, false, false, true, _) => LanConnectChatInputAction.ReleaseInputFocus,
            (false, false, false, false, true) => LanConnectChatInputAction.CloseOverlay,
            _ => LanConnectChatInputAction.None
        };

    internal static LanConnectChatInputAction RouteF8(
        bool overlayOpen,
        bool pinned,
        bool blockingModalVisible) =>
        (overlayOpen, pinned, blockingModalVisible) switch
        {
            (_, _, true) => LanConnectChatInputAction.None,
            (false, _, false) => LanConnectChatInputAction.ShowOverlay,
            (true, false, false) => LanConnectChatInputAction.PinOverlay,
            (true, true, false) => LanConnectChatInputAction.HideOverlay
        };
}
