using Godot;
using GdUnit4;
using MegaCrit.Sts2.addons.mega_text;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRichChatReferenceUxPhaseZeroTests
{
    private const string ReferenceButtonName = "ChatReferenceButton";

    [TestCase]
    public async Task Entity_only_draft_keeps_a_real_trailing_text_edit_for_ime_composition()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText(string.Empty);
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", AccessibleLabel);
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();

        AssertThat(editor.InsertItem(new LanConnectItemRun("card", "MegaCrit.Strike", 1))).IsTrue();
        await runner.AwaitIdleFrame();

        TextEdit[] textInputs = editor.FindChildren("*", "TextEdit", recursive: true, owned: false)
            .OfType<TextEdit>()
            .Where(input => !input.IsQueuedForDeletion())
            .ToArray();
        AssertThat(textInputs.Length).IsEqual(1);
        AssertThat(editor.GetViewport().GuiGetFocusOwner() is TextEdit).IsTrue();

        TextEdit compositionTarget = textInputs.Single();
        compositionTarget.Text = "中文";
        compositionTarget.EmitSignal(TextEdit.SignalName.TextChanged);
        await runner.AwaitIdleFrame();

        AssertThat(string.Concat(draft.Runs.Select(run => run switch
        {
            LanConnectTextRun text => text.Text,
            LanConnectItemRun => "[Card]",
            _ => "[Entity]"
        }))).IsEqual("[Card]中文");
    }

    [TestCase]
    public async Task Android_reference_button_arms_touch_capture_and_inserts_only_once()
    {
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(EnabledRoomState(), (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Button? referenceButton = panel.FindChild(
            ReferenceButtonName,
            recursive: true,
            owned: false) as Button;
        AssertThat(referenceButton != null).IsTrue();
        referenceButton!.EmitSignal(Button.SignalName.Pressed);

        TouchCapturePorts ports = new();
        LanConnectItemLinkCapture capture = new(ports);
        LanConnectReferenceMode mode = new();
        LanConnectReferenceModeContext context = new(
            HasChatTarget: true,
            LanConnectChatChannel.Room,
            panel.State!,
            RoomId: "room-1",
            RoomSessionId: "session-1",
            LanConnectReferenceTargetKind.Item | LanConnectReferenceTargetKind.Combat);
        AssertThat(mode.Toggle(context, LanConnectReferenceModeSource.TouchButton)).IsTrue();
        long armedGeneration = mode.ArmedGeneration;
        InputEventScreenTouch touch = new()
        {
            Index = 0,
            Position = new Vector2(180, 240),
            Pressed = true
        };
        bool handled = false;

        bool firstCapture = mode.CanCapture(
                                armedGeneration,
                                context,
                                LanConnectReferenceTargetKind.Item) &&
                            LanConnectItemLinkCaptureInputRoute.TryRoute(
            touch,
            capture,
            () => handled = true);
        AssertThat(firstCapture).IsTrue();
        AssertThat(mode.CaptureSucceeded(
            armedGeneration,
            context,
            LanConnectReferenceTargetKind.Item)).IsTrue();
        AssertThat(handled).IsTrue();
        AssertThat(ports.InsertCalls).IsEqual(1);

        AssertThat(mode.CanCapture(
            armedGeneration,
            context,
            LanConnectReferenceTargetKind.Item)).IsFalse();
        AssertThat(ports.InsertCalls).IsEqual(1);
    }

    [TestCase]
    public async Task Touching_a_message_reference_opens_a_pinned_preview_until_explicit_close()
    {
        LanConnectChatChannelState state = EnabledRoomState();
        state.AppendConfirmedContentForTests(
            "touch-preview",
            "Silent",
            new LanConnectChatContent(1,
            [
                new LanConnectTextSegment("look "),
                new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion")
            ]),
            sequence: 1,
            isLocal: false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Control touchTarget = panel.FindChild("ChatMessageRun1", true, false) as Control ??
            panel.FindChildren("*", string.Empty, true, false).OfType<MegaRichTextLabel>().Single();
        touchTarget.EmitSignal(Control.SignalName.GuiInput, new InputEventScreenTouch
        {
            Index = 0,
            Position = touchTarget.Size / 2f,
            Pressed = true
        });
        await runner.AwaitIdleFrame();

        AssertThat(panel.PreviewVisible).IsTrue();
        touchTarget.EmitSignal(Control.SignalName.MouseExited);
        await runner.AwaitIdleFrame();
        AssertThat(panel.PreviewVisible).IsTrue();

        panel.ItemPreviewForTests.ClosePreview();
        await runner.AwaitIdleFrame();
        AssertThat(panel.PreviewVisible).IsFalse();
    }

    [TestCase]
    public async Task Mixed_text_emoji_and_references_use_one_native_inline_rich_text_control()
    {
        LanConnectChatChannelState state = EnabledRoomState();
        state.AppendConfirmedContentForTests(
            "inline-layout",
            "Silent",
            new LanConnectChatContent(1,
            [
                new LanConnectTextSegment("这是一段需要自然换行的中文 mixed text "),
                new LanConnectEmojiSegment("heart"),
                new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion"),
                new LanConnectTextSegment(" after reference")
            ]),
            sequence: 1,
            isLocal: false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Control[] richTextControls = panel.FindChildren("*", string.Empty, recursive: true, owned: false)
            .OfType<MegaRichTextLabel>()
            .OfType<Control>()
            .ToArray();
        AssertThat(richTextControls.Length).IsEqual(1);
        AssertThat(panel.FindChild("ChatMessageRun0", true, false) == null).IsTrue();
        AssertThat(panel.FindChildren("*", "HFlowContainer", true, false)
            .OfType<Control>()
            .All(control => !control.IsAncestorOf(richTextControls[0]))).IsTrue();
    }

    [TestCase(1280, 720, "desktop-1280x720")]
    [TestCase(1920, 1080, "desktop-1920x1080")]
    [TestCase(1080, 1920, "touch-portrait-simulated-1080x1920")]
    [TestCase(2400, 1080, "touch-landscape-simulated-2400x1080")]
    public async Task Capture_phase_zero_rich_chat_baseline(int width, int height, string id)
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(width, height), 1f);
        await fixture.ShowRichMatrixOnly();
        fixture.DisableCaretBlink();
        await fixture.AwaitTwoFrames();

        string outputDirectory = Path.Combine(Path.GetTempPath(), "sts2-v052-phase0-baseline");
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, id + ".png");
        using Image image = await fixture.CaptureImage();

        AssertThat(fixture.RichMessageViews().Length).IsEqual(1);
        AssertThat(fixture.RichMessageTexts().Length).IsEqual(1);
        AssertThat(fixture.RichMessageTexts().Single().GetGlobalRect()
            .Intersects(fixture.ViewportRect, includeBorders: false)).IsTrue();
        AssertThat(image.SavePng(path)).IsEqual(Error.Ok);
        AssertThat(new FileInfo(path).Length).IsGreater(1024L);
    }

    private static string AccessibleLabel(LanConnectDraftRun run) => run switch
    {
        LanConnectItemRun => "Card",
        _ => "Entity"
    };

    private static LanConnectChatChannelState EnabledRoomState()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        state.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 1));
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }

    private static LanConnectResolvedItem ResolveItem(LanConnectItemRun run) => new(
        LanConnectResolvedItemStatus.Resolved,
        run.ItemType,
        "chat.item." + run.ItemType,
        "Localized " + run.ItemType,
        "Localized " + run.ItemType,
        new LanConnectHoverTipPreviewData(
            run.ItemType,
            "Localized " + run.ItemType,
            "Complete local description",
            null));

    private sealed class TouchCapturePorts : ILanConnectItemLinkCapturePorts
    {
        private readonly object _holder = new();

        internal int InsertCalls { get; private set; }

        public object? GuiGetHoveredControl() => _holder;
        public object? GuiGetControlAtPosition(Vector2 position) => _holder;
        public object? GetParent(object node) => null;
        public bool IsChatInteractionBlocking => false;
        public bool ItemRefsEnabledForSelectedChannel => true;
        public bool CombatRefsEnabledForSelectedChannel => true;
        public bool IsRoomChannelSelected => true;
        public bool IsCaptureBoundary(object node) => false;
        public bool IsSupportedHolder(object node) => ReferenceEquals(node, _holder);
        public bool IsPowerHolder(object node) => false;
        public bool IsPlayerHolder(object node) => false;
        public bool TryResolveCard(object node, out LanConnectItemRun run)
        {
            run = new LanConnectItemRun("card", "MegaCrit.Strike", 1);
            return ReferenceEquals(node, _holder);
        }
        public bool TryResolveRelic(object node, out LanConnectItemRun run) { run = null!; return false; }
        public bool TryResolvePotion(object node, out LanConnectItemRun run) { run = null!; return false; }
        public bool TryResolvePower(object node, out LanConnectCombatRun run) { run = null!; return false; }
        public bool TryResolvePlayerTarget(object node, out LanConnectCombatRun run) { run = null!; return false; }
        public bool InsertAndFocus(LanConnectItemRun run) { InsertCalls++; return true; }
        public bool InsertCombatAndFocus(LanConnectCombatRun run) => false;
        public void ShowCombatRoomOnlyWarning() { }
    }
}
