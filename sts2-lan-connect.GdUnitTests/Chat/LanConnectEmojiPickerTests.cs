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

    private static LanConnectChatChannelState EnabledState(int emojiVersion)
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            Channel = LanConnectChatChannel.Server,
            ServerChatVersion = 1,
            InstanceId = "emoji-picker-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures
            {
                RichContentVersion = emojiVersion > 0 ? 1 : 0,
                EmojiSetVersion = emojiVersion
            }
        });
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }

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

    private static void ClickViewport(ISceneRunner runner, Button button)
    {
        Vector2 position = button.GetScreenTransform() * (button.Size / 2f);
        runner.SimulateMouseMove(position).SimulateMouseButtonPressed(MouseButton.Left);
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

        private static Texture2D Icon()
        {
            Image image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            image.Fill(Colors.White);
            return ImageTexture.CreateFromImage(image);
        }
    }
}
