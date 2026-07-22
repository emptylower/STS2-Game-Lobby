using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRichMessageRenderingTests
{
    [TestCase]
    public async Task Android_message_text_keeps_the_chat_font_size_instead_of_native_auto_sizing()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedContentForTests(
            "android-font-size",
            "Silent",
            new LanConnectChatContent(1, [new LanConnectTextSegment("房间聊天。")]),
            sequence: 1,
            isLocal: false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        panel.CustomMinimumSize = new Vector2(288f, 360f);
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel inline =
            (MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel)RichMessageText(
                FindNode<Control>(panel, "ChatMessageRow0"));

        AssertThat(inline.AutoSizeEnabled).IsFalse();
        AssertThat(inline.GetThemeFontSize("normal_font_size", "RichTextLabel")).IsEqual(14);
    }

    [TestCase]
    public async Task Mixed_message_uses_one_native_inline_label_with_opaque_meta_and_safe_copy_text()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedContentForTests(
            "inline-message",
            "Silent",
            Content(),
            sequence: 1,
            isLocal: false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        RichTextLabel inline = panel.FindChildren("*", string.Empty, recursive: true, owned: false)
            .OfType<RichTextLabel>()
            .Single();
        Control view = (Control)inline.GetParent();
        string parsedText = inline.GetParsedText();
        string copyText = view.GetMeta("lan_connect_copy_text").AsString();
        string opaqueKeys = view.GetMeta("lan_connect_reference_keys").AsString();

        AssertThat(panel.FindChild("ChatMessageRun0", true, false) == null).IsTrue();
        AssertThat(parsedText).Contains("before ");
        AssertThat(parsedText).Contains("Localized Strike+2");
        AssertThat(parsedText).Contains("未知遗物");
        AssertThat(parsedText).Contains("Localized Potion");
        AssertThat(copyText).Contains("[卡牌]");
        AssertThat(copyText).Contains("[遗物]");
        AssertThat(copyText).Contains("[药水]");
        AssertThat(copyText.Contains("MegaCrit.", StringComparison.Ordinal)).IsFalse();
        AssertThat(inline.AccessibilityName.Contains("MegaCrit.", StringComparison.Ordinal)).IsFalse();
        AssertThat(opaqueKeys.Contains("MegaCrit.", StringComparison.Ordinal)).IsFalse();
        AssertThat(opaqueKeys.Split(',', StringSplitOptions.RemoveEmptyEntries).Length).IsEqual(2);
    }

    [TestCase]
    public async Task Resolver_failure_degrades_one_inline_segment_without_hiding_later_content()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedContentForTests(
            "isolated-inline-failure",
            "Silent",
            Content(),
            sequence: 1,
            isLocal: false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            run => run.ItemType == "relic"
                ? throw new InvalidOperationException("resolver failed")
                : ResolveItem(run)))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        RichTextLabel inline = panel.FindChildren("*", string.Empty, recursive: true, owned: false)
            .OfType<RichTextLabel>()
            .Single();
        string parsedText = inline.GetParsedText();

        AssertThat(parsedText).Contains("未知遗物");
        AssertThat(parsedText).Contains("Localized Potion");
        AssertThat(parsedText.Contains("PrivateMod.SecretRelic", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase]
    public async Task Renders_ordered_rich_runs_with_local_titles_unknown_placeholder_and_status()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedContentForTests(
            "message-1",
            "Silent",
            Content(),
            sequence: 1,
            isLocal: false);
        state.Queue(new ServerChatPendingMessage
        {
            ClientMessageId = "pending-1",
            SenderName = "Me",
            Content = new LanConnectChatContent(1, [new LanConnectTextSegment("pending")])
        });
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(
            state,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Control confirmedRow = FindNode<Control>(panel, "ChatMessageRow0");
        RichTextLabel inline = RichMessageText(confirmedRow);
        string parsedText = inline.GetParsedText();
        int before = parsedText.IndexOf("before ", StringComparison.Ordinal);
        int card = parsedText.IndexOf("Localized Strike+2", StringComparison.Ordinal);
        int middle = parsedText.IndexOf(" middle ", StringComparison.Ordinal);
        int relic = parsedText.IndexOf("未知遗物", StringComparison.Ordinal);
        int potion = parsedText.IndexOf("Localized Potion", StringComparison.Ordinal);
        AssertThat(before >= 0 && before < card && card < middle && middle < relic && relic < potion).IsTrue();
        AssertThat(Labels(panel).Any(text => text.Contains("PrivateMod.SecretRelic", StringComparison.Ordinal))).IsFalse();
        AssertThat(confirmedRow.FindChildren("*", string.Empty, true, false)
            .OfType<RichTextLabel>().Count()).IsEqual(1);
        AssertThat(confirmedRow.FindChild("ChatMessageRun0", true, false) == null).IsTrue();
        Control view = (Control)inline.GetParent();
        AssertThat(view.GetMeta("lan_connect_reference_keys").AsString()).IsEqual("ref-1,ref-5");
        AssertThat(Labels(panel)).Contains("发送中");
    }

    [TestCase]
    public async Task One_resolver_exception_isolated_to_that_segment_and_preview_invalidates_on_rebind()
    {
        LanConnectChatChannelState first = EnabledState();
        first.AppendConfirmedContentForTests(
            "message-1",
            "Silent",
            Content(),
            sequence: 1,
            isLocal: false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            run => run.ItemType == "relic"
                ? throw new InvalidOperationException("resolver failed")
                : ResolveItem(run)))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(first, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Control firstRow = FindNode<Control>(panel, "ChatMessageRow0");
        RichTextLabel inline = RichMessageText(firstRow);
        AssertThat(inline.GetParsedText()).Contains("未知遗物");
        AssertThat(inline.GetParsedText()).Contains("Localized Potion");
        inline.EmitSignal(RichTextLabel.SignalName.MetaHoverStarted, "ref-5");
        await runner.AwaitIdleFrame();
        AssertThat(panel.ItemPreviewForTests.TestState.Visible).IsTrue();

        LanConnectChatChannelState second = EnabledState();
        panel.BindStructured(second, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        AssertThat(panel.ItemPreviewForTests.TestState.Visible).IsFalse();
        AssertThat(panel.ItemPreviewForTests.TestState.ContentNodeCount).IsEqual(0);
    }

    [TestCase]
    public async Task Keyboard_and_controller_open_the_single_reference_as_a_pinned_preview()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedContentForTests(
            "accessible-preview",
            "Silent",
            new LanConnectChatContent(1,
                [new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion")]),
            sequence: 1,
            isLocal: false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        RichTextLabel inline = RichMessageText(FindNode<Control>(panel, "ChatMessageRow0"));
        inline.EmitSignal(Control.SignalName.GuiInput, new InputEventKey
        {
            Keycode = Key.Enter,
            Pressed = true
        });
        await runner.AwaitIdleFrame();
        AssertThat(panel.ItemPreviewForTests.TestState.Pinned).IsTrue();

        panel.ItemPreviewForTests.ClosePreview();
        inline.EmitSignal(Control.SignalName.GuiInput, new InputEventJoypadButton
        {
            ButtonIndex = JoyButton.A,
            Pressed = true
        });
        await runner.AwaitIdleFrame();
        AssertThat(panel.ItemPreviewForTests.TestState.Pinned).IsTrue();
    }

    [TestCase]
    public async Task Room_ready_features_enable_emoji_but_item_v0_keeps_item_draft_disabled()
    {
        LanConnectChatChannelState room = new(LanConnectChatChannel.Room);
        room.RichDraft.InsertEntity(new LanConnectEmojiRun("heart"));
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(room, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Button emoji = FindNode<Button>(panel, LanConnectEmojiPicker.ToggleButtonName);
        Button send = FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName);
        AssertThat(emoji.Visible).IsFalse();
        AssertThat(send.Disabled).IsTrue();

        room.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 0, 0));
        await runner.AwaitIdleFrame();
        AssertThat(emoji.Visible).IsTrue();
        AssertThat(send.Disabled).IsFalse();

        room.RichDraft.InsertEntity(new LanConnectItemRun("relic", "MegaCrit.Anchor"));
        await runner.AwaitIdleFrame();
        AssertThat(send.Disabled).IsTrue();
        AssertThat(room.RichDraft.Runs.Count).IsEqual(2);
    }

    [TestCase]
    public async Task Resolver_context_locale_and_mod_changes_miss_cache_rerender_and_close_preview()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedContentForTests(
            "context-message",
            "Silent",
            new LanConnectChatContent(1,
                [new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion")]),
            sequence: 1,
            isLocal: false);
        ContextModelDbPort port = new() { Title = "Potion EN" };
        LanConnectItemModelResolver resolver = new(port);
        LanConnectItemResolverContext context = new("en-US", "mods-a");
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            resolver,
            () => context))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        RichTextLabel initial = RichMessageText(FindNode<Control>(panel, "ChatMessageRow0"));
        AssertThat(initial.GetParsedText()).IsEqual("Potion EN");
        initial.EmitSignal(RichTextLabel.SignalName.MetaHoverStarted, "ref-0");
        await runner.AwaitIdleFrame();
        AssertThat(panel.ItemPreviewForTests.TestState.Visible).IsTrue();

        port.Title = "药水 CN";
        context = new LanConnectItemResolverContext("zh-CN", "mods-a");
        await runner.AwaitIdleFrame();
        AssertThat(RichMessageText(FindNode<Control>(panel, "ChatMessageRow0")).GetParsedText()).IsEqual("药水 CN");
        AssertThat(panel.ItemPreviewForTests.TestState.Visible).IsFalse();

        port.Title = "Potion Mod B";
        context = new LanConnectItemResolverContext("zh-CN", "mods-b");
        await runner.AwaitIdleFrame();
        AssertThat(RichMessageText(FindNode<Control>(panel, "ChatMessageRow0")).GetParsedText()).IsEqual("Potion Mod B");
        AssertThat(port.PotionLookups).IsEqual(3);
    }

    [TestCase]
    public async Task Twelve_entities_cannot_collapse_multiline_text_run_or_escape_1280x720()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedContentForTests(
            "bounds-message",
            new string('N', 32),
            TwelveEntityContent(),
            sequence: 1,
            isLocal: false);
        Control root = AutoFree(new Control { Size = new Vector2(1280, 720) })!;
        LanConnectBasicChatPanel panel = new(
            LanConnectChatUiComposition.Icons,
            ResolveItem)
        {
            Position = new Vector2(8, 8),
            Size = new Vector2(1264, 704)
        };
        root.AddChild(panel);
        using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        Control row = FindNode<Control>(panel, "ChatMessageRow0");
        RichTextLabel text = RichMessageText(row);
        Rect2 rect = text.GetGlobalRect();
        Rect2 visibleRect = rect.Intersection(FindNode<Control>(panel, "ChatMessagesScroll").GetGlobalRect());
        AssertThat(rect.Size.X).IsGreater(0f);
        AssertThat(rect.Size.Y).IsGreater(0f);
        AssertThat(visibleRect.Size.X).IsGreater(0f);
        AssertThat(visibleRect.Size.Y).IsGreater(0f);
        AssertThat(visibleRect.Position.X).IsGreaterEqual(0f);
        AssertThat(visibleRect.Position.Y).IsGreaterEqual(0f);
        AssertThat(visibleRect.End.X).IsLessEqual(1280f);
        AssertThat(visibleRect.End.Y).IsLessEqual(720f);
        AssertThat(row.FindChildren("*", string.Empty, true, false)
            .OfType<RichTextLabel>().Count()).IsEqual(1);
        AssertThat(row.FindChildren("*", "HFlowContainer", true, false).Count).IsEqual(0);
    }

    private static LanConnectChatContent Content() => new(1,
    [
        new LanConnectTextSegment("before "),
        new LanConnectItemRefSegment("card", "MegaCrit.Strike", 2),
        new LanConnectTextSegment(" middle "),
        new LanConnectItemRefSegment("relic", "PrivateMod.SecretRelic"),
        new LanConnectEmojiSegment("heart"),
        new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion")
    ]);

    private static LanConnectChatContent TwelveEntityContent() => new(1,
    [
        new LanConnectTextSegment("mixed entities\nwrapped line "),
        new LanConnectEmojiSegment("smile"),
        new LanConnectItemRefSegment("card", "MegaCrit.Strike", 1),
        new LanConnectItemRefSegment("relic", "PrivateMod.SecretRelic"),
        new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion"),
        new LanConnectEmojiSegment("heart"),
        new LanConnectItemRefSegment("card", "MegaCrit.Strike", 2),
        new LanConnectItemRefSegment("relic", "MegaCrit.Anchor"),
        new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion"),
        new LanConnectEmojiSegment("laugh"),
        new LanConnectItemRefSegment("card", "MegaCrit.Strike", 0),
        new LanConnectItemRefSegment("relic", "MegaCrit.Anchor"),
        new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion")
    ]);

    private static LanConnectResolvedItem ResolveItem(LanConnectItemRun run) => run.ItemType switch
    {
        "card" => new LanConnectResolvedItem(
            LanConnectResolvedItemStatus.Resolved,
            "card",
            "chat.card",
            "Localized Strike+2",
            "Localized Strike+2",
            new LanConnectCardPreviewData(new object(), 2)),
        "potion" => new LanConnectResolvedItem(
            LanConnectResolvedItemStatus.Resolved,
            "potion",
            "chat.potion",
            "Localized Potion",
            "Localized Potion",
            new LanConnectHoverTipPreviewData("potion", "Localized Potion", "Description", null)),
        _ => new LanConnectResolvedItem(
            LanConnectResolvedItemStatus.Unknown,
            run.ItemType,
            "chat.unknown_relic",
            null,
            "未知遗物",
            null)
    };

    private static LanConnectChatChannelState EnabledState()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            ProtocolVersion = 1,
            Channel = LanConnectChatChannel.Server,
            InstanceId = "test-instance",
            HistoryEpoch = 1,
            ChatEnabled = true,
            ServerChatVersion = 1,
            EnabledFeatures = new ServerChatEnabledFeatures
            {
                RichContentVersion = 1,
                EmojiSetVersion = 1,
                ItemRefVersion = 1
            }
        });
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }

    private static RichTextLabel RichMessageText(Node root) => root
        .FindChildren("*", string.Empty, true, false)
        .OfType<RichTextLabel>()
        .Single();

    private static string[] Labels(Node root) => root.FindChildren("*", "Label", true, false)
        .OfType<Label>()
        .Select(label => label.Text)
        .ToArray();

    private static T FindNode<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);

    private sealed class ContextModelDbPort : ILanConnectModelDbPort
    {
        internal string Title { get; set; } = string.Empty;
        internal int PotionLookups { get; private set; }
        public object DeserializeModelId(string value) => value;
        public bool TryGetCard(object id, out object model) { model = null!; return false; }
        public bool TryGetRelic(object id, out object model) { model = null!; return false; }
        public bool TryGetPotion(object id, out object model)
        {
            PotionLookups++;
            model = id;
            return true;
        }
        public string GetLocalizedTitle(object model) => Title;
        public int GetSupportedCardUpgradeLevel(object card) => 0;
        public object CreateCardPreviewCopy(object card, int upgradeLevel) => card;
        public LanConnectHoverTipPreviewData CreateRelicPreviewData(object relic) =>
            new("relic", Title, "Description", null);
        public LanConnectHoverTipPreviewData CreatePotionPreviewData(object potion) =>
            new("potion", Title, "Description", null);
    }
}
