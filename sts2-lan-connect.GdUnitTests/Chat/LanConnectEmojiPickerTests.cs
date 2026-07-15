using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectEmojiPickerTests
{
    [TestCase]
    public void Production_loader_decodes_catalog_and_visible_deterministic_fallbacks()
    {
        LanConnectLucideIconLoader loader = new();
        foreach (string iconName in LanConnectChatEmojiSet.Version1
                     .Select(emoji => emoji.LucideIcon)
                     .Concat(["send", "refresh-cw", "pin"]))
        {
            Texture2D icon = loader.Get(iconName, 20, Colors.White);
            AssertThat(icon.GetWidth()).IsEqual(20);
            AssertThat(icon.GetHeight()).IsEqual(20);
        }

        Texture2D missing = loader.Get("missing-icon", 20, Colors.Black);
        AssertThat(ReferenceEquals(missing, loader.Get("missing-icon", 20, Colors.Black))).IsTrue();
        AssertThat(HasVisiblePixel(missing.GetImage())).IsTrue();

        LanConnectLucideIconLoader malformedLoader = new(new MalformedLucideResources());
        Texture2D malformed = malformedLoader.Get("malformed", 20, Colors.Black);
        AssertThat(HasVisiblePixel(malformed.GetImage())).IsTrue();
    }

    [TestCase]
    public async Task Picker_builds_stable_six_by_three_icon_only_accessible_grid()
    {
        using PickerFixture fixture = await PickerFixture.Create();

        LanConnectEmojiPickerTestState state = fixture.Picker.TestState;
        AssertThat(state.Columns).IsEqual(6);
        AssertThat(state.Rows).IsEqual(3);
        AssertThat(state.EmojiIds).ContainsExactly(
            LanConnectChatEmojiSet.Version1.Select(emoji => emoji.Id));
        Button[] buttons = EmojiButtons(fixture.Picker);
        AssertThat(buttons.Length).IsEqual(18);
        AssertThat(buttons.All(button => string.IsNullOrEmpty(button.Text))).IsTrue();
        AssertThat(buttons.All(button => button.Icon != null)).IsTrue();
        AssertThat(buttons.All(button => !string.IsNullOrWhiteSpace(button.TooltipText))).IsTrue();
        AssertThat(buttons.All(button => button.AccessibilityName == button.TooltipText)).IsTrue();
    }

    [TestCase]
    public async Task Picker_uses_the_injected_localizer_for_english_and_chinese_accessibility()
    {
        using PickerFixture english = await PickerFixture.Create(
            localize: key => key == "chat.emoji.smile" ? "Smile" : key);
        AssertThat(EmojiButtons(english.Picker)[0].AccessibilityName).IsEqual("Smile");

        using PickerFixture chinese = await PickerFixture.Create(
            localize: key => key == "chat.emoji.smile" ? "微笑" : key);
        AssertThat(EmojiButtons(chinese.Picker)[0].AccessibilityName).IsEqual("微笑");
    }

    [TestCase]
    public async Task Mouse_and_accept_keys_insert_one_at_caret_keep_open_and_never_send()
    {
        using PickerFixture fixture = await PickerFixture.Create("ab", caret: 1);
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        Button smile = EmojiButtons(fixture.Picker)[0];

        ClickViewport(fixture.Runner, smile);
        await fixture.Runner.AwaitInputProcessed();
        AssertThat(fixture.Draft.Runs).ContainsExactly(
            new LanConnectTextRun("a"),
            new LanConnectEmojiRun("smile"),
            new LanConnectTextRun("b"));
        AssertThat(fixture.Picker.Visible).IsTrue();

        Button laugh = EmojiButtons(fixture.Picker)[1];
        PushKey(laugh, Key.Enter);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Draft.Runs.Count(run => run is LanConnectEmojiRun)).IsEqual(2);
        AssertThat(fixture.Draft.Runs.OfType<LanConnectEmojiRun>().Last().EmojiId).IsEqual("laugh");

        Button heart = EmojiButtons(fixture.Picker)[2];
        PushAction(heart, "ui_accept");
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Draft.Runs.Count(run => run is LanConnectEmojiRun)).IsEqual(3);
        AssertThat(fixture.Draft.Runs.OfType<LanConnectEmojiRun>().Last().EmojiId).IsEqual("heart");
        AssertThat(fixture.Picker.Visible).IsTrue();
    }

    [TestCase]
    public async Task Keyboard_and_controller_arrows_wrap_within_six_by_three_grid()
    {
        using PickerFixture fixture = await PickerFixture.Create();
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        Button[] buttons = EmojiButtons(fixture.Picker);

        buttons[0].GrabFocus();
        PushKey(buttons[0], Key.Left);
        AssertThat(fixture.Picker.TestState.FocusedIndex).IsEqual(5);
        PushKey(buttons[5], Key.Right);
        AssertThat(fixture.Picker.TestState.FocusedIndex).IsEqual(0);
        PushAction(buttons[0], "ui_up");
        AssertThat(fixture.Picker.TestState.FocusedIndex).IsEqual(12);
        PushAction(buttons[12], "ui_down");
        AssertThat(fixture.Picker.TestState.FocusedIndex).IsEqual(0);
    }

    [TestCase]
    public async Task Real_viewport_keyboard_and_controller_accept_insert_exactly_once()
    {
        using PickerFixture fixture = await PickerFixture.Create();
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        Button[] buttons = EmojiButtons(fixture.Picker);

        buttons[0].GrabFocus();
        PushViewportKey(buttons[0].GetViewport(), Key.Enter);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Draft.Runs.Count(run => run is LanConnectEmojiRun)).IsEqual(1);

        buttons[1].GrabFocus();
        PushViewportAction(buttons[1].GetViewport(), "ui_accept");
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Draft.Runs.Count(run => run is LanConnectEmojiRun)).IsEqual(2);
        AssertThat(fixture.Draft.Runs.OfType<LanConnectEmojiRun>().Last().EmojiId).IsEqual("laugh");

        buttons[2].GrabFocus();
        PushViewportKey(buttons[2].GetViewport(), Key.Space);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Draft.Runs.Count(run => run is LanConnectEmojiRun)).IsEqual(3);
        AssertThat(fixture.Draft.Runs.OfType<LanConnectEmojiRun>().Last().EmojiId).IsEqual("heart");
    }

    [TestCase]
    public async Task Real_viewport_joypad_actions_wrap_insert_once_never_send_and_cancel_to_draft()
    {
        const int device = 42;
        using JoypadActionBindings bindings = new(device);
        bindings.AddButton("ui_left", JoyButton.Paddle1);
        bindings.AddButton("ui_right", JoyButton.Paddle2);
        bindings.AddMotion("ui_up", JoyAxis.RightY, -1f);
        bindings.AddMotion("ui_down", JoyAxis.RightY, 1f);
        bindings.AddButton("ui_accept", JoyButton.Paddle3);
        bindings.AddButton("ui_cancel", JoyButton.Paddle4);

        LanConnectChatChannelState state = EnabledState(emojiVersion: 1);
        state.RichDraft.ReplaceAllWithText("ab");
        state.RichDraft.SetCaret(new LanConnectDraftPosition(0, 1));
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        int sends = 0;
        panel.Bind(state, _ => { sends++; return Task.CompletedTask; }, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName)
            .EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();
        LanConnectEmojiPicker picker = FindNode<LanConnectEmojiPicker>(
            panel,
            LanConnectEmojiPicker.PickerName);
        Button[] buttons = EmojiButtons(picker);
        Viewport viewport = buttons[0].GetViewport();

        PushViewportJoypadButton(viewport, JoyButton.Paddle1, device);
        await runner.AwaitInputProcessed();
        AssertThat(picker.TestState.FocusedIndex).IsEqual(5);
        PushViewportJoypadButton(viewport, JoyButton.Paddle2, device);
        await runner.AwaitInputProcessed();
        AssertThat(picker.TestState.FocusedIndex).IsEqual(0);
        PushViewportJoypadMotion(viewport, JoyAxis.RightY, -1f, device);
        await runner.AwaitInputProcessed();
        AssertThat(picker.TestState.FocusedIndex).IsEqual(12);
        PushViewportJoypadMotion(viewport, JoyAxis.RightY, 1f, device);
        await runner.AwaitInputProcessed();
        AssertThat(picker.TestState.FocusedIndex).IsEqual(0);

        PushViewportJoypadButton(viewport, JoyButton.Paddle3, device);
        await runner.AwaitInputProcessed();
        AssertThat(state.RichDraft.Runs).ContainsExactly(
            new LanConnectTextRun("a"),
            new LanConnectEmojiRun("smile"),
            new LanConnectTextRun("b"));
        AssertThat(sends).IsEqual(0);
        AssertThat(picker.Visible).IsTrue();

        PushViewportJoypadButton(viewport, JoyButton.Paddle4, device);
        await runner.AwaitInputProcessed();
        await runner.AwaitIdleFrame();
        AssertThat(picker.Visible).IsFalse();
        AssertThat(panel.DraftHasFocus).IsTrue();
        AssertThat(sends).IsEqual(0);
    }

    [TestCase]
    public async Task Rebind_closes_picker_and_next_open_inserts_only_into_new_draft()
    {
        LanConnectChatChannelState first = EnabledState(emojiVersion: 1);
        first.RichDraft.ReplaceAllWithText("first");
        LanConnectChatChannelState second = EnabledState(emojiVersion: 1);
        second.RichDraft.ReplaceAllWithText("second");
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(first, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName)
            .EmitSignal(Button.SignalName.Pressed);
        LanConnectEmojiPicker picker = FindNode<LanConnectEmojiPicker>(
            panel,
            LanConnectEmojiPicker.PickerName);
        AssertThat(picker.Visible).IsTrue();

        panel.Bind(second, _ => Task.CompletedTask, _ => Task.CompletedTask);
        AssertThat(picker.Visible).IsFalse();
        await runner.AwaitIdleFrame();
        FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName)
            .EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();
        EmojiButtons(picker)[0].EmitSignal(Button.SignalName.Pressed);

        AssertThat(first.RichDraft.Runs).ContainsExactly(new LanConnectTextRun("first"));
        AssertThat(second.RichDraft.Runs).ContainsExactly(
            new LanConnectTextRun("second"),
            new LanConnectEmojiRun("smile"));
    }

    [TestCase]
    public async Task Context_reset_and_room_leave_close_an_open_picker()
    {
        LanConnectChatChannelState state = EnabledState(emojiVersion: 1);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName)
            .EmitSignal(Button.SignalName.Pressed);
        LanConnectEmojiPicker picker = FindNode<LanConnectEmojiPicker>(
            panel,
            LanConnectEmojiPicker.PickerName);
        AssertThat(picker.Visible).IsTrue();

        state.ClearForContextChange();
        await runner.AwaitIdleFrame();
        AssertThat(picker.Visible).IsFalse();

        using RoomChatFixture room = await RoomChatFixture.OpenWithServerSupport();
        ApplyFeatures(room.State.Server, richVersion: 1, emojiVersion: 1);
        room.State.Server.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        room.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        await room.Overlay.RefreshForTests();
        await room.Runner.AwaitIdleFrame();
        Button roomToggle = FindNode<Button>(room.Overlay, LanConnectEmojiPicker.ToggleButtonName);
        roomToggle.EmitSignal(Button.SignalName.Pressed);
        LanConnectEmojiPicker roomPicker = FindNode<LanConnectEmojiPicker>(
            room.Overlay,
            LanConnectEmojiPicker.PickerName);
        AssertThat(roomPicker.Visible).IsTrue();

        await room.Overlay.LeaveRoomForTests();
        await room.Runner.AwaitIdleFrame();
        AssertThat(roomPicker.Visible).IsFalse();
    }

    [TestCase(1, 1, true)]
    [TestCase(0, 1, false)]
    [TestCase(1, 0, false)]
    [TestCase(2, 1, false)]
    [TestCase(1, 2, false)]
    [TestCase(2, 2, false)]
    public async Task Capability_requires_exact_rich_and_emoji_version_one(
        int richVersion,
        int emojiVersion,
        bool expected)
    {
        LanConnectChatChannelState state = EnabledState(emojiVersion, richVersion);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        AssertThat(FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName).Visible)
            .IsEqual(expected);
    }

    [TestCase]
    public async Task Capability_downgrade_closes_picker_and_restores_draft_focus()
    {
        LanConnectChatChannelState state = EnabledState(emojiVersion: 1);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        panel.FocusDraft();
        FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName)
            .EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();
        LanConnectEmojiPicker picker = FindNode<LanConnectEmojiPicker>(
            panel,
            LanConnectEmojiPicker.PickerName);
        AssertThat(picker.Visible).IsTrue();

        ApplyFeatures(state, richVersion: 2, emojiVersion: 2);
        await panel.RefreshForTests();
        await runner.AwaitIdleFrame();

        AssertThat(picker.Visible).IsFalse();
        AssertThat(FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName).Visible).IsFalse();
        AssertThat(panel.DraftHasFocus).IsTrue();
    }

    [TestCase]
    public async Task Focus_intents_do_not_override_reopen_or_external_focus()
    {
        using PickerFixture fixture = await PickerFixture.Create();
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        fixture.Picker.ClosePicker();
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Picker.Visible).IsTrue();
        AssertThat(EmojiButtons(fixture.Picker)[0].HasFocus()).IsTrue();

        Button external = new() { Name = "ExternalFocus" };
        fixture.Root.AddChild(external);
        await fixture.Runner.AwaitIdleFrame();
        fixture.Picker.ClosePicker();
        external.GrabFocus();
        AssertThat(external.HasFocus()).IsTrue();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(external.HasFocus()).IsTrue();
        AssertThat(fixture.Picker.Visible).IsFalse();
    }

    [TestCase]
    public async Task Open_remove_readd_stays_hidden_and_can_open_and_focus_again()
    {
        using PickerFixture fixture = await PickerFixture.Create();
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Picker.Visible).IsTrue();

        fixture.Root.RemoveChild(fixture.Picker);
        bool hiddenAfterRemove = !fixture.Picker.Visible;
        fixture.Root.AddChild(fixture.Picker);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(hiddenAfterRemove).IsTrue();
        AssertThat(fixture.Picker.Visible).IsFalse();

        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Picker.Visible).IsTrue();
        AssertThat(EmojiButtons(fixture.Picker)[0].HasFocus()).IsTrue();
    }

    [TestCase("rebind")]
    [TestCase("context")]
    [TestCase("downgrade")]
    public async Task Engine_popup_hide_invalidates_intent_without_later_stealing_external_focus(
        string invalidation)
    {
        LanConnectChatChannelState state = EnabledState(emojiVersion: 1);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        Button external = new() { Name = "PopupHideExternalFocus" };
        panel.AddChild(external);
        await runner.AwaitIdleFrame();
        FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName)
            .EmitSignal(Button.SignalName.Pressed);
        LanConnectEmojiPicker picker = FindNode<LanConnectEmojiPicker>(
            panel,
            LanConnectEmojiPicker.PickerName);
        AssertThat(picker.Visible).IsTrue();

        picker.Hide();
        external.GrabFocus();
        AssertThat(external.HasFocus()).IsTrue();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(picker.Visible).IsFalse();
        AssertThat(external.HasFocus()).IsTrue();

        switch (invalidation)
        {
            case "rebind":
                panel.Bind(
                    EnabledState(emojiVersion: 1),
                    _ => Task.CompletedTask,
                    _ => Task.CompletedTask);
                break;
            case "context":
                state.ClearForContextChange();
                break;
            case "downgrade":
                ApplyFeatures(state, richVersion: 2, emojiVersion: 2);
                await panel.RefreshForTests();
                break;
        }
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        AssertThat(picker.Visible).IsFalse();
        AssertThat(external.HasFocus()).IsTrue();
        AssertThat(panel.DraftHasFocus).IsFalse();
    }

    [TestCase]
    public async Task Explicit_hide_while_hidden_does_not_mask_the_next_engine_popup_hide()
    {
        using PickerFixture fixture = await PickerFixture.Create();
        Button external = new() { Name = "NextPopupHideExternalFocus" };
        fixture.Root.AddChild(external);
        await fixture.Runner.AwaitIdleFrame();

        fixture.Picker.ClosePicker(restoreDraftFocus: false);
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Picker.Visible).IsTrue();

        fixture.Picker.Hide();
        external.GrabFocus();
        await fixture.Runner.AwaitIdleFrame();
        fixture.Picker.Bind(
            fixture.Editor,
            LanConnectChatEmojiSet.Version1,
            _ => PickerFixture.Icon(),
            key => "label:" + key);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Picker.Visible).IsFalse();
        AssertThat(external.HasFocus()).IsTrue();
        AssertThat(fixture.Editor.HasEditorFocus).IsFalse();
    }

    [TestCase]
    public async Task Repeated_bind_same_frame_keeps_eighteen_stable_nodes_and_focus()
    {
        using PickerFixture fixture = await PickerFixture.Create();
        for (int iteration = 0; iteration < 3; iteration++)
        {
            fixture.Picker.Bind(
                fixture.Editor,
                LanConnectChatEmojiSet.Version1,
                _ => PickerFixture.Icon(),
                key => "label:" + key);
        }

        GridContainer grid = FindNode<GridContainer>(fixture.Picker, LanConnectEmojiPicker.GridName);
        AssertThat(grid.GetChildCount()).IsEqual(18);
        AssertThat(EmojiButtons(fixture.Picker).Select(button => button.Name.ToString()))
            .ContainsExactly(LanConnectChatEmojiSet.Version1.Select(
                emoji => LanConnectEmojiPicker.ButtonPrefix + emoji.Id));

        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(EmojiButtons(fixture.Picker)[0].HasFocus()).IsTrue();
    }

    [TestCase]
    public async Task Escape_closes_only_picker_restores_draft_and_tab_requests_external_focus()
    {
        using PickerFixture fixture = await PickerFixture.Create("draft", caret: 3);
        fixture.Editor.FocusEditor();
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        Button first = EmojiButtons(fixture.Picker)[0];
        AssertThat(first.HasFocus()).IsTrue();

        PushKey(first, Key.Escape);
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Picker.Visible).IsFalse();
        AssertThat(fixture.Editor.HasEditorFocus).IsTrue();
        AssertThat(fixture.Draft.ToCompatibilityText()).IsEqual("draft");

        bool? backwards = null;
        fixture.Picker.FocusExitRequested += value => backwards = value;
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        PushKey(EmojiButtons(fixture.Picker)[0], Key.Tab, shiftPressed: true);
        AssertThat(backwards == true).IsTrue();
        AssertThat(fixture.Picker.Visible).IsFalse();

        backwards = null;
        fixture.Picker.OpenPicker();
        await fixture.Runner.AwaitIdleFrame();
        PushKey(EmojiButtons(fixture.Picker)[0], Key.Tab);
        AssertThat(backwards == false).IsTrue();
        AssertThat(fixture.Picker.Visible).IsFalse();
    }

    [TestCase]
    public async Task Panel_hides_capability_zero_and_enabled_picker_inserts_without_send()
    {
        LanConnectChatChannelState disabled = EnabledState(emojiVersion: 0);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        int sends = 0;
        panel.Bind(disabled, _ => { sends++; return Task.CompletedTask; }, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        Button toggle = FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName);
        AssertThat(toggle.Visible).IsFalse();
        AssertThat(panel.PopupVisible).IsFalse();

        LanConnectChatChannelState enabled = EnabledState(emojiVersion: 1);
        enabled.RichDraft.ReplaceAllWithText("ab");
        enabled.RichDraft.SetCaret(new LanConnectDraftPosition(0, 1));
        panel.Bind(enabled, _ => { sends++; return Task.CompletedTask; }, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        toggle = FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName);
        AssertThat(toggle.Visible).IsTrue();
        AssertThat(toggle.Icon?.GetWidth() ?? 0).IsEqual(20);
        AssertThat(FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).Icon?.GetWidth() ?? 0)
            .IsEqual(18);
        AssertThat(panel.GetFocusChainControls().Select(control => control.Name.ToString()).Take(4))
            .ContainsExactly(
                LanConnectConstants.ChatMessagesScrollName,
                LanConnectConstants.ChatDraftInputName,
                LanConnectEmojiPicker.ToggleButtonName,
                LanConnectConstants.ChatSendButtonName);
        toggle.EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();
        LanConnectEmojiPicker picker = FindNode<LanConnectEmojiPicker>(panel, LanConnectEmojiPicker.PickerName);
        EmojiButtons(picker)[0].EmitSignal(Button.SignalName.Pressed);

        AssertThat(enabled.RichDraft.Runs).ContainsExactly(
            new LanConnectTextRun("a"),
            new LanConnectEmojiRun("smile"),
            new LanConnectTextRun("b"));
        AssertThat(sends).IsEqual(0);
    }

    [TestCase]
    public async Task Retry_and_room_pin_controls_use_shared_loader_textures()
    {
        LanConnectChatChannelState state = EnabledState(emojiVersion: 1);
        state.BeginPendingText("icon-retry", "Me", "retry this");
        state.MarkFailed("icon-retry", "offline", "offline");
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner panelRunner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await panelRunner.AwaitIdleFrame();
        Button retry = FindNode<Button>(
            panel,
            LanConnectConstants.ChatRetryButtonPrefix + "icon-retry");
        AssertThat(retry.Icon?.GetWidth() ?? 0).IsEqual(16);

        using RoomChatFixture room = await RoomChatFixture.OpenWithServerSupport();
        Button pin = FindNode<Button>(room.Overlay, "ChatPinButton");
        AssertThat(pin.Icon?.GetWidth() ?? 0).IsEqual(18);
    }

    private static LanConnectChatChannelState EnabledState(int emojiVersion, int? richVersion = null)
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        ApplyFeatures(state, richVersion ?? (emojiVersion > 0 ? 1 : 0), emojiVersion);
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }

    private static void ApplyFeatures(
        LanConnectChatChannelState state,
        int richVersion,
        int emojiVersion) =>
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            Channel = state.Channel,
            ServerChatVersion = 1,
            InstanceId = "emoji-picker-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures
            {
                RichContentVersion = richVersion,
                EmojiSetVersion = emojiVersion
            }
        });

    private static Button[] EmojiButtons(Node picker) => picker.FindChildren(
            LanConnectEmojiPicker.ButtonPrefix + "*",
            "Button",
            recursive: true,
            owned: false)
        .OfType<Button>()
        .Where(button => !button.IsQueuedForDeletion())
        .OrderBy(button => button.GetMeta("emoji_index").AsInt32())
        .ToArray();

    private static void PushKey(Control control, Key key, bool shiftPressed = false) =>
        control.EmitSignal(Control.SignalName.GuiInput, new InputEventKey
        {
            Keycode = key,
            Pressed = true,
            Echo = false,
            ShiftPressed = shiftPressed
        });

    private static void PushAction(Control control, string action) =>
        control.EmitSignal(Control.SignalName.GuiInput, new InputEventAction
        {
            Action = action,
            Pressed = true,
            Strength = 1f
        });

    private static void PushViewportKey(Viewport viewport, Key key)
    {
        viewport.PushInput(new InputEventKey { Keycode = key, Pressed = true, Echo = false });
        viewport.PushInput(new InputEventKey { Keycode = key, Pressed = false, Echo = false });
    }

    private static void PushViewportAction(Viewport viewport, string action)
    {
        viewport.PushInput(new InputEventAction
        {
            Action = action,
            Pressed = true,
            Strength = 1f
        });
        viewport.PushInput(new InputEventAction
        {
            Action = action,
            Pressed = false,
            Strength = 0f
        });
    }

    private static void PushViewportJoypadButton(
        Viewport viewport,
        JoyButton button,
        int device)
    {
        viewport.PushInput(new InputEventJoypadButton
        {
            Device = device,
            ButtonIndex = button,
            Pressed = true,
        });
        viewport.PushInput(new InputEventJoypadButton
        {
            Device = device,
            ButtonIndex = button,
            Pressed = false,
        });
    }

    private static void PushViewportJoypadMotion(
        Viewport viewport,
        JoyAxis axis,
        float value,
        int device)
    {
        viewport.PushInput(new InputEventJoypadMotion
        {
            Device = device,
            Axis = axis,
            AxisValue = value
        });
        viewport.PushInput(new InputEventJoypadMotion
        {
            Device = device,
            Axis = axis,
            AxisValue = 0f
        });
    }

    private static void ClickViewport(ISceneRunner runner, Button button)
    {
        Vector2 position = button.GetScreenTransform() * (button.Size / 2f);
        runner.SimulateMouseMove(position).SimulateMouseButtonPressed(MouseButton.Left);
    }

    private static bool HasVisiblePixel(Image image)
    {
        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A > 0f)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static T FindNode<T>(Node root, string name) where T : Node =>
        root.FindChild(name, recursive: true, owned: false) as T ??
        throw new InvalidOperationException($"Node '{name}' was not found.");

    private sealed class PickerFixture : IDisposable
    {
        private PickerFixture(
            Control root,
            LanConnectRichDraft draft,
            LanConnectRichDraftEditor editor,
            LanConnectEmojiPicker picker,
            ISceneRunner runner)
        {
            Root = root;
            Draft = draft;
            Editor = editor;
            Picker = picker;
            Runner = runner;
        }

        internal Control Root { get; }
        internal LanConnectRichDraft Draft { get; }
        internal LanConnectRichDraftEditor Editor { get; }
        internal LanConnectEmojiPicker Picker { get; }
        internal ISceneRunner Runner { get; }

        internal static async Task<PickerFixture> Create(
            string text = "",
            int? caret = null,
            Func<string, string>? localize = null)
        {
            LanConnectRichDraft draft = LanConnectRichDraft.FromText(text);
            if (caret != null)
            {
                draft.SetCaret(new LanConnectDraftPosition(0, caret.Value));
            }
            LanConnectRichDraftEditor editor = new();
            editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", _ => "Entity");
            LanConnectEmojiPicker picker = new();
            picker.Bind(
                editor,
                LanConnectChatEmojiSet.Version1,
                _ => Icon(),
                localize ?? (key => "label:" + key));
            VBoxContainer root = AutoFree(new VBoxContainer())!;
            root.AddChild(editor);
            root.AddChild(picker);
            ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
            await runner.AwaitIdleFrame();
            return new PickerFixture(root, draft, editor, picker, runner);
        }

        public void Dispose() => Runner.Dispose();

        internal static Texture2D Icon()
        {
            Image image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            image.Fill(Colors.White);
            return ImageTexture.CreateFromImage(image);
        }
    }

    private sealed class JoypadActionBindings : IDisposable
    {
        private readonly int _device;
        private readonly List<(StringName Action, InputEvent Input)> _bindings = [];

        internal JoypadActionBindings(int device) => _device = device;

        internal void AddButton(string action, JoyButton button) => Add(
            action,
            new InputEventJoypadButton
            {
                Device = _device,
                ButtonIndex = button
            });

        internal void AddMotion(string action, JoyAxis axis, float value) => Add(
            action,
            new InputEventJoypadMotion
            {
                Device = _device,
                Axis = axis,
                AxisValue = value
            });

        public void Dispose()
        {
            foreach ((StringName action, InputEvent input) in _bindings)
            {
                InputMap.ActionEraseEvent(action, input);
                input.Dispose();
            }
            _bindings.Clear();
        }

        private void Add(string action, InputEvent input)
        {
            StringName actionName = action;
            if (!InputMap.HasAction(actionName))
            {
                input.Dispose();
                throw new InvalidOperationException($"Input action '{action}' does not exist.");
            }
            InputMap.ActionAddEvent(actionName, input);
            _bindings.Add((actionName, input));
        }
    }

    private sealed class MalformedLucideResources : ILanConnectLucideResources
    {
        public bool TryReadSvg(string iconName, out string svg)
        {
            svg = "<svg><path></svg>";
            return true;
        }
    }
}
