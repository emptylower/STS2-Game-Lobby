using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class LanConnectItemLinkCaptureUiTests
{
    [TestCase("card", "MegaCrit.Strike", 2)]
    [TestCase("relic", "MegaCrit.Anchor", null)]
    [TestCase("potion", "MegaCrit.FirePotion", null)]
    public async Task Successful_capture_inserts_at_current_caret_focuses_and_marks_handled(
        string itemType,
        string modelId,
        int? upgradeLevel)
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("ab");
        draft.SetCaret(new LanConnectDraftPosition(0, 1));
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", _ => "Item");
        Control root = AutoFree(new Control())!;
        root.AddChild(editor);
        TestItemHolder holder = new(itemType, new LanConnectItemRun(itemType, modelId, upgradeLevel));
        Control hitbox = new();
        holder.AddChild(hitbox);
        root.AddChild(holder);
        using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.AwaitIdleFrame();

        TestCapturePorts ports = new(editor)
        {
            Hovered = hitbox,
            SelectedChannel = LanConnectChatChannel.Room
        };
        ports.Drafts[LanConnectChatChannel.Room] = draft;
        bool handled = false;
        bool consumed = LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(),
            new LanConnectItemLinkCapture(ports),
            () => handled = true);
        await runner.AwaitIdleFrame();

        AssertThat(consumed).IsTrue();
        AssertThat(handled).IsTrue();
        AssertThat(draft.Runs).ContainsExactly(
            new LanConnectTextRun("a"),
            new LanConnectItemRun(itemType, modelId, upgradeLevel),
            new LanConnectTextRun("b"));
        AssertThat(editor.HasEditorFocus).IsTrue();
        AssertThat(ports.SendCalls).IsEqual(0);
    }

    [TestCase]
    public async Task Enabled_server_and_room_channels_insert_into_only_the_selected_draft()
    {
        LanConnectRichDraft serverDraft = LanConnectRichDraft.FromText("S");
        LanConnectRichDraft roomDraft = LanConnectRichDraft.FromText("R");
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(serverDraft, new(1, 1, 1, 0), "Ironclad", _ => "Item");
        using ISceneRunner runner = ISceneRunner.Load(editor, autoFree: true);
        await runner.AwaitIdleFrame();

        TestItemHolder serverHolder = new(
            "card",
            new LanConnectItemRun("card", "MegaCrit.Strike", 1));
        TestItemHolder roomHolder = new(
            "potion",
            new LanConnectItemRun("potion", "MegaCrit.FirePotion"));
        editor.AddChild(serverHolder);
        editor.AddChild(roomHolder);
        TestCapturePorts ports = new(editor);
        ports.Drafts[LanConnectChatChannel.Server] = serverDraft;
        ports.Drafts[LanConnectChatChannel.Room] = roomDraft;
        LanConnectItemLinkCapture capture = new(ports);

        ports.SelectedChannel = LanConnectChatChannel.Server;
        ports.Hovered = serverHolder;
        bool serverHandled = false;
        AssertThat(LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(), capture, () => serverHandled = true)).IsTrue();
        ports.SelectedChannel = LanConnectChatChannel.Room;
        ports.Hovered = roomHolder;
        bool roomHandled = false;
        AssertThat(LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(), capture, () => roomHandled = true)).IsTrue();

        AssertThat(serverHandled).IsTrue();
        AssertThat(roomHandled).IsTrue();
        AssertThat(serverDraft.Runs).ContainsExactly(
            new LanConnectTextRun("S"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1));
        AssertThat(roomDraft.Runs).ContainsExactly(
            new LanConnectTextRun("R"),
            new LanConnectItemRun("potion", "MegaCrit.FirePotion"));
        AssertThat(ports.OpenAndFocusChannels)
            .ContainsExactly(LanConnectChatChannel.Server, LanConnectChatChannel.Room);
        AssertThat(ports.SendCalls).IsEqual(0);
    }

    [TestCase]
    public async Task Unsupported_button_and_preview_boundary_are_not_handled_and_normal_click_survives()
    {
        Button button = AutoFree(new Button { Text = "normal" })!;
        using ISceneRunner runner = ISceneRunner.Load(button, autoFree: true);
        await runner.AwaitIdleFrame();
        int clicks = 0;
        button.Pressed += () => clicks++;
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        TestCapturePorts ports = new(editor) { Hovered = button };
        LanConnectItemLinkCapture capture = new(ports);
        bool handled = false;

        bool consumed = LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(), capture, () => handled = true);
        button.EmitSignal(Button.SignalName.Pressed);

        AssertThat(consumed).IsFalse();
        AssertThat(handled).IsFalse();
        AssertThat(clicks).IsEqual(1);
        AssertThat(ports.OpenAndFocusChannels).IsEmpty();

        TestItemHolder preview = AutoFree(new TestItemHolder(
            "card_preview",
            new LanConnectItemRun("card", "MegaCrit.Strike", 1)))!;
        ports.Hovered = preview;
        AssertThat(LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(), capture, () => handled = true)).IsFalse();
        AssertThat(handled).IsFalse();
    }

    private static InputEventMouseButton AltLeftPress() => new()
    {
        AltPressed = true,
        ButtonIndex = MouseButton.Left,
        Pressed = true
    };

    private sealed partial class TestItemHolder : Control
    {
        internal TestItemHolder(string kind, LanConnectItemRun item)
        {
            Kind = kind;
            Item = item;
        }

        internal string Kind { get; }

        internal LanConnectItemRun Item { get; }
    }

    private sealed class TestCapturePorts : ILanConnectItemLinkCapturePorts
    {
        private readonly LanConnectRichDraftEditor _editor;

        internal TestCapturePorts(LanConnectRichDraftEditor editor)
        {
            _editor = editor;
        }

        internal Control? Hovered { get; set; }

        internal LanConnectChatChannel SelectedChannel { get; set; }

        internal Dictionary<LanConnectChatChannel, LanConnectRichDraft> Drafts { get; } = new();

        internal List<LanConnectChatChannel> OpenAndFocusChannels { get; } = new();

        internal int SendCalls { get; private set; }

        public bool IsChatInteractionBlocking { get; set; }

        public bool ItemRefsEnabledForSelectedChannel { get; set; } = true;

        public object? GuiGetHoveredControl() => Hovered;

        public object? GetParent(object node) => ((Node)node).GetParent();

        public bool IsCaptureBoundary(object node) =>
            node is TestItemHolder { Kind: "card_preview" };

        public bool IsSupportedHolder(object node) =>
            node is TestItemHolder { Kind: "card" or "relic" or "potion" };

        public bool TryResolveCard(object node, out LanConnectItemRun run) =>
            TryResolve("card", node, out run);

        public bool TryResolveRelic(object node, out LanConnectItemRun run) =>
            TryResolve("relic", node, out run);

        public bool TryResolvePotion(object node, out LanConnectItemRun run) =>
            TryResolve("potion", node, out run);

        public bool InsertAndFocus(LanConnectItemRun run)
        {
            if (!Drafts.TryGetValue(SelectedChannel, out LanConnectRichDraft? draft))
            {
                return false;
            }
            draft.InsertEntity(run);
            _editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", _ => "Item");
            _editor.RefreshFromDraft();
            _editor.FocusEditor();
            OpenAndFocusChannels.Add(SelectedChannel);
            return true;
        }

        private static bool TryResolve(string kind, object node, out LanConnectItemRun run)
        {
            if (node is TestItemHolder holder && holder.Kind == kind)
            {
                run = holder.Item;
                return true;
            }
            run = null!;
            return false;
        }
    }
}
