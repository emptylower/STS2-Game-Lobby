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
    public async Task Focused_entity_chip_passes_global_keys_and_replaces_selection_with_viewport_unicode()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        LanConnectRichDraft draft = fixture.State.Room.RichDraft;
        draft.ReplaceAllWithText("a");
        draft.InsertEntity(new LanConnectEmojiRun("heart"));
        draft.InsertText("b");
        await fixture.Overlay.RefreshForTests();
        await fixture.Runner.AwaitIdleFrame();
        Button chip = FindEntityChip(fixture.Overlay);
        SelectChip(chip);

        PushUnicode(fixture.Overlay.GetViewport(), Key.X, '界');
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(draft.Runs).ContainsExactly(new LanConnectTextRun("a界b"));

        draft.InsertEntity(new LanConnectEmojiRun("heart"));
        await fixture.Runner.AwaitIdleFrame();
        chip = FindEntityChip(fixture.Overlay);
        SelectChip(chip);
        PushKey(fixture.Overlay.GetViewport(), Key.Escape);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        AssertThat(fixture.Overlay.TestState.FocusOwnerName)
            .IsNotEqual(chip.Name.ToString());

        chip = FindEntityChip(fixture.Overlay);
        SelectChip(chip);
        PushKey(fixture.Overlay.GetViewport(), Key.Tab);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.FocusOwnerName)
            .IsNotEqual(chip.Name.ToString());

        chip = FindEntityChip(fixture.Overlay);
        SelectChip(chip);
        PushKey(fixture.Overlay.GetViewport(), Key.F8);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.Pinned).IsTrue();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
    }

    [TestCase]
    public async Task F8_shows_pins_hides_and_modal_blocks_without_changing_channel_or_draft()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        fixture.Overlay.SetDraftForTests("keep server draft");
        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Room, sequence: 40);
        await fixture.Overlay.CloseForTests();

        fixture.Overlay.RouteKeyForTests(Key.F8, blockingModalVisible: true);
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();

        fixture.Overlay.RouteKeyForTests(Key.F8);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
        AssertThat(fixture.Overlay.TestState.Pinned).IsFalse();
        AssertChannelAndDraftPreserved(fixture.Overlay, "keep server draft");
        AssertThat(fixture.Overlay.TestState.RoomUnread).IsEqual(1);

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
    public async Task Visible_registered_modal_blocks_real_viewport_f8_input()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        await fixture.Overlay.CloseForTests();
        Control modal = new() { Visible = true };
        modal.AddToGroup(LanConnectConstants.BlockingModalGroupName);
        fixture.Overlay.GetViewport().AddChild(modal);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.FocusOwnerName)
            .IsNotEqual(LanConnectConstants.ChatDraftInputName);

        PushKey(fixture.Overlay.GetViewport(), Key.F8);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();

        modal.Visible = false;
        PushKey(fixture.Overlay.GetViewport(), Key.F8);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsTrue();
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.KpEnter)]
    public async Task Visible_registered_modal_blocks_focused_draft_submit(Key key)
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.SetDraftForTests("blocked");
        fixture.Overlay.FocusDraftForTests();
        Control modal = new() { Visible = true };
        modal.AddToGroup(LanConnectConstants.BlockingModalGroupName);
        fixture.Overlay.GetViewport().AddChild(modal);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.FocusOwnerName)
            .IsNotEqual(LanConnectConstants.ChatDraftInputName);

        PushKey(fixture.Overlay.GetViewport(), key);
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.Draft).IsEqual("blocked");

        modal.Visible = false;
        fixture.Overlay.FocusDraftForTests();
        PushKey(fixture.Overlay.GetViewport(), key);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.TestState.Draft).IsEqual(string.Empty);
    }

    [TestCase]
    public async Task Production_modal_roots_register_with_blocking_contract()
    {
        using LobbyOverlayFixture lobby = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        int dialogShellCount = 0;
        foreach (Node node in lobby.Overlay.FindChildren("*", "Control", recursive: true, owned: false))
        {
            if (node.IsInGroup(LanConnectConstants.BlockingModalGroupName))
            {
                dialogShellCount++;
            }
        }

        LanConnectServerSelectionDialog serverPicker = AutoFree(new LanConnectServerSelectionDialog())!;
        LanConnectRoomManagementPanel roomManagement = AutoFree(new LanConnectRoomManagementPanel())!;

        AssertThat(dialogShellCount).IsEqual(7);
        AssertThat(serverPicker.IsInGroup(LanConnectConstants.BlockingModalGroupName)).IsTrue();
        AssertThat(roomManagement.IsInGroup(LanConnectConstants.BlockingModalGroupName)).IsTrue();
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

    private static void PushKey(Viewport viewport, Key key)
    {
        viewport.PushInput(new InputEventKey { Keycode = key, Pressed = true, Echo = false });
        viewport.PushInput(new InputEventKey { Keycode = key, Pressed = false, Echo = false });
    }

    private static void PushUnicode(Viewport viewport, Key key, char value)
    {
        viewport.PushInput(new InputEventKey
        {
            Keycode = key,
            Unicode = value,
            Pressed = true,
            Echo = false
        });
        viewport.PushInput(new InputEventKey
        {
            Keycode = key,
            Unicode = value,
            Pressed = false,
            Echo = false
        });
    }

    private static Button FindEntityChip(Node root) => root.FindChildren(
            LanConnectConstants.ChatEntityChipPrefix + "*",
            "Button",
            recursive: true,
            owned: false)
        .OfType<Button>()
        .Single();

    private static void SelectChip(Button chip)
    {
        chip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true
        });
        chip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false
        });
        chip.GrabFocus();
    }

    private static void AssertChannelAndDraftPreserved(
        LanConnectRoomChatOverlay overlay,
        string expectedDraft)
    {
        AssertThat(overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Server);
        AssertThat(overlay.TestState.Draft).IsEqual(expectedDraft);
    }
}
