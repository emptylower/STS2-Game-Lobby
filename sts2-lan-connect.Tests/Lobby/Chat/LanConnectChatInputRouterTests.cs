using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatInputRouterTests
{
    [Theory]
    [InlineData(true, false, false, (int)LanConnectChatInputAction.Send)]
    [InlineData(true, true, false, (int)LanConnectChatInputAction.InsertNewline)]
    [InlineData(false, false, false, (int)LanConnectChatInputAction.OpenAndFocus)]
    [InlineData(false, false, true, (int)LanConnectChatInputAction.None)]
    public void Enter_routes_by_focus_shift_and_modal(
        bool inputFocused,
        bool shift,
        bool modal,
        int expected)
    {
        Assert.Equal((LanConnectChatInputAction)expected, LanConnectChatInputRouter.RouteEnter(inputFocused, shift, modal));
    }

    [Theory]
    [InlineData(true, true, true, true, (int)LanConnectChatInputAction.ClosePopup)]
    [InlineData(false, true, true, true, (int)LanConnectChatInputAction.ClosePreview)]
    [InlineData(false, false, true, true, (int)LanConnectChatInputAction.ReleaseInputFocus)]
    [InlineData(false, false, false, true, (int)LanConnectChatInputAction.CloseOverlay)]
    [InlineData(false, false, false, false, (int)LanConnectChatInputAction.None)]
    public void Escape_closes_one_layer(
        bool popup,
        bool preview,
        bool input,
        bool overlayOpen,
        int expected)
    {
        Assert.Equal((LanConnectChatInputAction)expected, LanConnectChatInputRouter.RouteEscape(popup, preview, input, overlayOpen));
    }

    [Theory]
    [InlineData(false, false, false, (int)LanConnectChatInputAction.ShowOverlay)]
    [InlineData(true, false, false, (int)LanConnectChatInputAction.PinOverlay)]
    [InlineData(true, true, false, (int)LanConnectChatInputAction.HideOverlay)]
    [InlineData(false, false, true, (int)LanConnectChatInputAction.None)]
    [InlineData(true, false, true, (int)LanConnectChatInputAction.None)]
    [InlineData(true, true, true, (int)LanConnectChatInputAction.None)]
    public void F8_routes_show_pin_hide_and_modal_block(
        bool overlayOpen,
        bool pinned,
        bool modal,
        int expected)
    {
        Assert.Equal((LanConnectChatInputAction)expected, LanConnectChatInputRouter.RouteF8(overlayOpen, pinned, modal));
    }
}
