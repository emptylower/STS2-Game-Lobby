using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRichDraftEditorTests
{
    [TestCase]
    public async Task Ordered_mixed_draft_renders_text_and_atomic_entities_in_document_order()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromRuns(
        [
            new LanConnectTextRun("look "),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            new LanConnectTextRun(" then "),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun(" now")
        ]);
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);

        await runner.AwaitIdleFrame();

        LanConnectRichDraftEditorTestState state = editor.TestState;
        AssertThat(state.RunKinds)
            .ContainsExactly("text", "item_ref:card", "text", "emoji", "text");
        AssertThat(state.EntityChipCount).IsEqual(2);
        AssertThat(state.TextEditorCount).IsEqual(3);
        AssertThat(state.SegmentCount).IsEqual(5);
        AssertThat(state.Selection).IsEqual(draft.Selection);
        AssertThat(state.Budget.TextScalars).IsEqual(15);
        AssertThat(state.Budget.EntityCount).IsEqual(2);
        AssertThat(state.ChildControlsMutable).IsFalse();

        TextEdit[] textRuns = editor.FindChildren(
                "*",
                "TextEdit",
                recursive: true,
                owned: false)
            .OfType<TextEdit>()
            .ToArray();
        Button[] entityChips = editor.FindChildren(
                LanConnectConstants.ChatEntityChipPrefix + "*",
                "Button",
                recursive: true,
                owned: false)
            .OfType<Button>()
            .ToArray();
        AssertThat(textRuns.Length).IsEqual(3);
        AssertThat(entityChips.Length).IsEqual(2);
        AssertThat(textRuns.Any(run => run.Text.Contains("MegaCrit", StringComparison.Ordinal))).IsFalse();
        AssertThat(entityChips.All(chip => chip.ClipText)).IsTrue();
    }

    [TestCase]
    public async Task Focusing_and_reconciling_preserves_document_caret_for_newline_insertion()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("server draft");
        AssertThat(draft.Selection.Active.TextOffset).IsEqual("server draft".Length);
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();
        AssertThat(draft.Selection.Active.TextOffset).IsEqual("server draft".Length);

        editor.FocusEditor();
        await runner.AwaitIdleFrame();
        AssertThat(draft.Selection.Active.TextOffset).IsEqual("server draft".Length);

        editor.InsertNewline();

        AssertThat(draft.ToCompatibilityText()).IsEqual("server draft\n");
    }

    [TestCase]
    public async Task Space_in_a_text_run_remains_normal_text_input()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("ab");
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();
        TextEdit input = AssertTextRun(editor, "ab");
        input.GrabFocus();
        input.SetCaretColumn(2);
        await runner.AwaitIdleFrame();

        PushGuiKey(input, Key.Space);

        AssertThat(draft.Selection).IsEqual(new LanConnectDraftSelection(
            new LanConnectDraftPosition(0, 2),
            new LanConnectDraftPosition(0, 2)));
        input.Text = "ab ";
        input.EmitSignal(TextEdit.SignalName.TextChanged);
        await runner.AwaitIdleFrame();
        AssertThat(draft.ToCompatibilityText()).IsEqual("ab ");
    }

    [TestCase]
    public async Task Boundary_backspace_removes_one_whole_entity_and_merges_text_runs()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromRuns(
        [
            new LanConnectTextRun("a"),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun("b")
        ]);
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();
        TextEdit trailing = AssertTextRun(editor, "b");
        trailing.GrabFocus();
        trailing.SetCaretLine(0);
        trailing.SetCaretColumn(0);
        await runner.AwaitIdleFrame();

        PushGuiKey(trailing, Key.Backspace);
        await runner.AwaitIdleFrame();

        AssertThat(draft.Runs).ContainsExactly(new LanConnectTextRun("ab"));
        AssertThat(editor.TestState.EntityChipCount).IsEqual(0);
    }

    [TestCase]
    public async Task Left_right_shift_selection_and_delete_cross_exactly_one_chip()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromRuns(
        [
            new LanConnectTextRun("a"),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun("b")
        ]);
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();

        TextEdit leading = AssertTextRun(editor, "a");
        leading.GrabFocus();
        leading.SetCaretColumn(1);
        await runner.AwaitIdleFrame();
        PushGuiKey(leading, Key.Right, shiftPressed: true);
        await runner.AwaitIdleFrame();
        AssertThat(draft.Selection.Anchor).IsEqual(new LanConnectDraftPosition(0, 1));
        AssertThat(draft.Selection.Active).IsEqual(new LanConnectDraftPosition(1, 1));
        AssertThat(editor.TestState.FocusOwnerName).IsEqual(LanConnectConstants.ChatEntityChipPrefix + "1");

        TextEdit trailing = AssertTextRun(editor, "b");
        trailing.GrabFocus();
        trailing.SetCaretColumn(0);
        await runner.AwaitIdleFrame();
        PushGuiKey(trailing, Key.Left);
        await runner.AwaitIdleFrame();
        AssertThat(draft.Selection.Active).IsEqual(new LanConnectDraftPosition(1, 0));

        leading = AssertTextRun(editor, "a");
        leading.GrabFocus();
        leading.SetCaretColumn(1);
        await runner.AwaitIdleFrame();
        PushGuiKey(leading, Key.Delete);
        await runner.AwaitIdleFrame();
        AssertThat(draft.Runs).ContainsExactly(new LanConnectTextRun("ab"));
    }

    [TestCase]
    public async Task Shift_click_and_drag_extend_selection_without_duplicate_content_signals()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromRuns(
        [
            new LanConnectTextRun("a"),
            new LanConnectEmojiRun("heart"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            new LanConnectTextRun("b")
        ]);
        draft.SetCaret(new LanConnectDraftPosition(0, 1));
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        int changes = 0;
        editor.DraftChanged += () => changes++;
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();
        Button emoji = FindChip(editor, 1);

        emoji.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            ShiftPressed = true
        });
        Button card = FindChip(editor, 2);
        card.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion
        {
            ButtonMask = MouseButtonMask.Left
        });

        AssertThat(draft.Selection.Anchor).IsEqual(new LanConnectDraftPosition(0, 1));
        AssertThat(draft.Selection.Active).IsEqual(new LanConnectDraftPosition(2, 1));
        AssertThat(changes).IsEqual(0);
    }

    [TestCase]
    public async Task Chip_selection_replacement_and_json_paste_stay_document_commands()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromRuns(
        [
            new LanConnectTextRun("a"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            new LanConnectTextRun("b")
        ]);
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();
        Button chip = editor.FindChildren(
                LanConnectConstants.ChatEntityChipPrefix + "*",
                "Button",
                recursive: true,
                owned: false)
            .OfType<Button>()
            .Single();

        chip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true
        });
        editor.PastePlainText("X");
        editor.PastePlainText("{\"kind\":\"emoji\",\"emojiId\":\"heart\"}");
        await runner.AwaitIdleFrame();

        LanConnectTextRun text = (LanConnectTextRun)draft.Runs.Single();
        AssertThat(text.Text).IsEqual("aX{\"kind\":\"emoji\",\"emojiId\":\"heart\"}b");
        AssertThat(draft.Runs.Any(run => run is LanConnectEmojiRun or LanConnectItemRun)).IsFalse();
    }

    [TestCase]
    public async Task Copy_preserves_document_order_and_never_exposes_structured_identity()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromRuns(
        [
            new LanConnectTextRun("a"),
            new LanConnectItemRun("card", "Secret.Model", 1),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun("b")
        ]);
        draft.SetSelection(new LanConnectDraftSelection(
            new LanConnectDraftPosition(0, 0),
            new LanConnectDraftPosition(3, 1)));
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();

        editor.CopySelectionToClipboard();

        string copied = DisplayServer.ClipboardGet();
        AssertThat(copied).IsEqual("aCard Strike+1Emoji heartb");
        AssertThat(copied.Contains("Secret.Model", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase]
    public async Task Reconciliation_preserves_focus_and_each_command_raises_one_signal()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("a");
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        int draftChanges = 0;
        int submits = 0;
        editor.DraftChanged += () => draftChanges++;
        editor.SubmitRequested += () => submits++;
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();
        editor.FocusEditor();
        await runner.AwaitIdleFrame();

        editor.PastePlainText("x");
        await runner.AwaitIdleFrame();

        AssertThat(draftChanges).IsEqual(1);
        AssertThat(editor.TestState.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
        TextEdit focused = (TextEdit)editor.FocusTarget!;
        PushGuiKey(focused, Key.Enter);
        AssertThat(submits).IsEqual(1);
        AssertThat(draftChanges).IsEqual(1);
    }

    [TestCase]
    public async Task Background_draft_changes_are_pumped_on_main_thread_once_per_frame()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("before");
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        int changes = 0;
        editor.DraftChanged += () => changes++;
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();

        await Task.Run(() => draft.ReplaceAllWithText("background"));
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        AssertThat(changes).IsEqual(1);
        AssertThat(AssertTextRun(editor, "background").Text).IsEqual("background");

        changes = 0;
        await Task.Run(() => draft.Clear());
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        AssertThat(changes).IsEqual(1);
        AssertThat(AssertTextRun(editor, string.Empty).Text).IsEqual(string.Empty);
    }

    [TestCase]
    public async Task Rebind_discards_old_draft_notifications_and_notifies_each_new_revision_once()
    {
        LanConnectRichDraft oldDraft = LanConnectRichDraft.FromText("old");
        LanConnectRichDraft newDraft = LanConnectRichDraft.FromText("new");
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(oldDraft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        int changes = 0;
        editor.DraftChanged += () => changes++;
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();

        await Task.Run(() => oldDraft.ReplaceAllWithText("old pending"));
        changes = 0;
        editor.Bind(newDraft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(changes).IsEqual(0);
        AssertThat(AssertTextRun(editor, "new").Text).IsEqual("new");

        await Task.Run(() => oldDraft.ReplaceAllWithText("old ignored"));
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(changes).IsEqual(0);
        AssertThat(AssertTextRun(editor, "new").Text).IsEqual("new");

        await Task.Run(() => newDraft.ReplaceAllWithText("new revision"));
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(changes).IsEqual(1);
        AssertThat(AssertTextRun(editor, "new revision").Text).IsEqual("new revision");

        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(changes).IsEqual(1);
    }

    [TestCase]
    public void Budget_reasons_prioritize_actionable_limits_schema_and_features()
    {
        AssertBlocked(LanConnectRichDraft.FromText(string.Empty), new(1, 1, 1, 0), "Ironclad", "请输入消息");
        AssertBlocked(LanConnectRichDraft.FromText(new string('x', 301)), new(1, 1, 1, 0), "Ironclad", "消息文字超过 300 字符");
        AssertBlocked(SegmentedDraft(33), new(1, 1, 1, 0), "Ironclad", "消息分段超过 32 段");
        AssertBlocked(LanConnectRichDraft.FromRuns(Enumerable.Range(0, 13).Select(_ =>
            (LanConnectDraftRun)new LanConnectEmojiRun("smile"))), new(1, 1, 1, 0), "Ironclad", "消息实体超过 12 个");
        AssertBlocked(LanConnectRichDraft.FromRuns([new LanConnectEmojiRun("unknown")]), new(1, 1, 1, 0), "Ironclad", "消息内容无效");
        AssertBlocked(LanConnectRichDraft.FromRuns([new LanConnectEmojiRun("heart")]), new(), "Ironclad", "当前频道不支持草稿中的富内容");

        LanConnectRichDraft wireDraft = LanConnectRichDraft.FromText("hello");
        string senderAt8192 = FindExactSenderName(wireDraft.ToContent(), 8192);
        AssertBlocked(wireDraft, new(1, 1, 1, 0), senderAt8192 + "S", "消息传输大小超过 8192 字节");

        LanConnectRichDraftEditor entityOnly = AutoFree(new LanConnectRichDraftEditor())!;
        entityOnly.Bind(
            LanConnectRichDraft.FromRuns([new LanConnectEmojiRun("heart")]),
            new(1, 1, 1, 0),
            "Ironclad",
            AccessibleLabel);
        AssertThat(entityOnly.CanSubmit).IsTrue();
        AssertThat(entityOnly.IsBlank).IsFalse();
        AssertThat(entityOnly.BlockingReason).IsEqual(string.Empty);

        LanConnectRichDraftEditor blank = AutoFree(new LanConnectRichDraftEditor())!;
        blank.Bind(LanConnectRichDraft.FromText(" \n "), new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        AssertThat(blank.IsBlank).IsTrue();
    }

    private void AssertBlocked(
        LanConnectRichDraft draft,
        LanConnectChatFeatureVersions enabled,
        string senderName,
        string reason)
    {
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, enabled, senderName, AccessibleLabel);
        AssertThat(editor.CanSubmit).IsFalse();
        AssertThat(editor.BlockingReason).IsEqual(reason);
    }

    private static LanConnectRichDraft SegmentedDraft(int count) => LanConnectRichDraft.FromRuns(
        Enumerable.Range(0, count).Select(index => index % 2 == 0
            ? (LanConnectDraftRun)new LanConnectTextRun("x")
            : new LanConnectEmojiRun("smile")));

    private static string FindExactSenderName(LanConnectChatContent content, int target)
    {
        LanConnectChatContent canonical = LanConnectServerChatProtocol.Canonicalize(
            content,
            new LanConnectChatFeatureVersions(1, 1, 1, 0));
        int baseline = LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(canonical, "S");
        string senderName = new('S', target - baseline + 1);
        AssertThat(LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(canonical, senderName))
            .IsEqual(target);
        return senderName;
    }

    private static TextEdit AssertTextRun(Node editor, string text) => editor.FindChildren(
            "*",
            "TextEdit",
            recursive: true,
            owned: false)
        .OfType<TextEdit>()
        .Single(run => run.Text == text);

    private static Button FindChip(Node editor, int runIndex) => editor.FindChildren(
            LanConnectConstants.ChatEntityChipPrefix + runIndex,
            "Button",
            recursive: true,
            owned: false)
        .OfType<Button>()
        .Single();

    private static void PushGuiKey(Control control, Key key, bool shiftPressed = false)
    {
        control.EmitSignal(Control.SignalName.GuiInput, new InputEventKey
        {
            Keycode = key,
            Pressed = true,
            Echo = false,
            ShiftPressed = shiftPressed
        });
    }

    private static string AccessibleLabel(LanConnectDraftRun run) => run switch
    {
        LanConnectEmojiRun => "Emoji heart",
        LanConnectItemRun { ItemType: "card" } => "Card Strike+1",
        _ => "Entity"
    };
}
