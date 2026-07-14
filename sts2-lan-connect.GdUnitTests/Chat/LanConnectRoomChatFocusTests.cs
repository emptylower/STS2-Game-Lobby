using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomChatFocusTests
{
    [TestCase]
    public async Task Focus_next_follows_tabs_messages_draft_send_and_pin()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.FocusFirstForTests();
        await fixture.Runner.AwaitIdleFrame();

        string[] expected =
        {
            "RoomChatTab",
            "ServerChatTab",
            LanConnectConstants.ChatMessagesScrollName,
            LanConnectConstants.ChatDraftInputName,
            LanConnectConstants.ChatSendButtonName,
            "ChatPinButton"
        };
        await AssertFocusSequence(fixture, expected);
    }

    [TestCase]
    public async Task Focus_next_includes_visible_retry_after_send()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.State.Room.BeginPendingText("focus-failed", "Me", "failed");
        fixture.State.Room.MarkFailed("focus-failed", "send_failed", "offline");
        await fixture.Overlay.RefreshForTests();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        fixture.Overlay.FocusFirstForTests();
        await fixture.Runner.AwaitIdleFrame();

        string[] expected =
        {
            "RoomChatTab",
            "ServerChatTab",
            LanConnectConstants.ChatMessagesScrollName,
            LanConnectConstants.ChatDraftInputName,
            LanConnectConstants.ChatSendButtonName,
            LanConnectConstants.ChatRetryButtonPrefix + "focus-failed",
            "ChatPinButton"
        };
        await AssertFocusSequence(fixture, expected);
    }

    [TestCase]
    public async Task Focus_next_places_new_messages_action_between_messages_and_draft()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.SetScrollForTests(24, atBottom: false);
        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Room, sequence: 10);
        await fixture.Overlay.RefreshForTests();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        fixture.Overlay.FocusFirstForTests();
        await fixture.Runner.AwaitIdleFrame();

        string[] expected =
        {
            "RoomChatTab",
            "ServerChatTab",
            LanConnectConstants.ChatMessagesScrollName,
            LanConnectConstants.ChatNewMessagesButtonName,
            LanConnectConstants.ChatDraftInputName,
            LanConnectConstants.ChatSendButtonName,
            "ChatPinButton"
        };
        await AssertFocusSequence(fixture, expected);
    }

    [TestCase]
    public async Task Focus_next_skips_hidden_server_tab()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.State.Server.SetPresentationForTests(LanConnectServerChatPresentation.Unsupported);
        await fixture.Overlay.RefreshForTests();
        await fixture.Runner.AwaitIdleFrame();
        fixture.Overlay.FocusFirstForTests();
        await fixture.Runner.AwaitIdleFrame();

        PushFocusNext(fixture.Overlay.GetViewport());
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.FocusOwnerName)
            .IsEqual(LanConnectConstants.ChatMessagesScrollName);
    }

    [TestCase]
    public async Task Escape_releases_draft_focus_then_closes_overlay()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.FocusDraftForTests();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.FocusOwnerName)
            .IsEqual(LanConnectConstants.ChatDraftInputName);

        fixture.Overlay.RouteKeyForTests(Key.Escape);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        AssertThat(fixture.Overlay.TestState.FocusOwnerName)
            .IsNotEqual(LanConnectConstants.ChatDraftInputName);

        fixture.Overlay.RouteKeyForTests(Key.Escape);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();
    }

    [TestCase]
    public async Task F8_shows_pins_hides_and_modal_blocks_without_changing_channel_or_draft()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        fixture.Overlay.SetDraftForTests("keep server draft");
        await fixture.Overlay.CloseForTests();

        fixture.Overlay.RouteKeyForTests(Key.F8, blockingModalVisible: true);
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();

        fixture.Overlay.RouteKeyForTests(Key.F8);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        AssertThat(fixture.Overlay.TestState.Pinned).IsFalse();
        AssertChannelAndDraftPreserved(fixture.Overlay, "keep server draft");

        fixture.Overlay.RouteKeyForTests(Key.F8);
        AssertThat(fixture.Overlay.TestState.Pinned).IsTrue();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        AssertChannelAndDraftPreserved(fixture.Overlay, "keep server draft");

        fixture.Overlay.RouteKeyForTests(Key.F8, blockingModalVisible: true);
        AssertThat(fixture.Overlay.TestState.Pinned).IsTrue();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();

        fixture.Overlay.RouteKeyForTests(Key.F8);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();
        AssertThat(fixture.Overlay.TestState.Pinned).IsFalse();
        AssertChannelAndDraftPreserved(fixture.Overlay, "keep server draft");
    }

    [TestCase]
    public async Task Enter_on_open_overlay_only_focuses_current_channel_and_shift_enter_inserts_newline()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        fixture.Overlay.SetDraftForTests("server draft");
        fixture.Overlay.FocusFirstForTests();
        await fixture.Runner.AwaitIdleFrame();

        fixture.Overlay.RouteKeyForTests(Key.Enter);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.FocusOwnerName)
            .IsEqual(LanConnectConstants.ChatDraftInputName);
        AssertChannelAndDraftPreserved(fixture.Overlay, "server draft");

        fixture.Overlay.RouteKeyForTests(Key.KpEnter, shiftPressed: true);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.Draft).IsEqual("server draft\n");
    }

    private static async Task AssertFocusSequence(RoomChatFixture fixture, IReadOnlyList<string> expected)
    {
        AssertThat(fixture.Overlay.TestState.FocusOwnerName).IsEqual(expected[0]);
        for (int index = 1; index < expected.Count; index++)
        {
            PushFocusNext(fixture.Overlay.GetViewport());
            await fixture.Runner.AwaitIdleFrame();
            AssertThat(fixture.Overlay.TestState.FocusOwnerName).IsEqual(expected[index]);
        }
    }

    private static void PushFocusNext(Viewport viewport)
    {
        viewport.PushInput(new InputEventAction
        {
            Action = "ui_focus_next",
            Pressed = true,
            Strength = 1f
        });
        viewport.PushInput(new InputEventAction
        {
            Action = "ui_focus_next",
            Pressed = false,
            Strength = 0f
        });
    }

    private static void AssertChannelAndDraftPreserved(
        LanConnectRoomChatOverlay overlay,
        string expectedDraft)
    {
        AssertThat(overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Server);
        AssertThat(overlay.TestState.Draft).IsEqual(expectedDraft);
    }
}
