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

        Control[] runs = Enumerable.Range(0, 6)
            .Select(index => FindNode<Control>(panel, $"ChatMessageRun{index}"))
            .ToArray();
        AssertThat(RunText(runs[0])).IsEqual("before ");
        AssertThat(RunText(runs[1])).IsEqual("Localized Strike+2");
        AssertThat(RunText(runs[2])).IsEqual(" middle ");
        AssertThat(RunText(runs[3])).IsEqual("未知遗物");
        AssertThat(runs[4] is TextureRect).IsTrue();
        AssertThat(RunText(runs[5])).IsEqual("Localized Potion");
        AssertThat(Labels(panel).Any(text => text.Contains("PrivateMod.SecretRelic", StringComparison.Ordinal))).IsFalse();
        AssertThat(runs[1].HasMeta("lan_connect_resolved_item")).IsTrue();
        AssertThat(runs[3].HasMeta("lan_connect_resolved_item")).IsFalse();
        AssertThat(runs[5].HasMeta("lan_connect_resolved_item")).IsTrue();
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

        AssertThat(Enumerable.Range(0, 6)
            .All(index => panel.FindChild($"ChatMessageRun{index}", true, false) != null)).IsTrue();
        Control potion = FindNode<Control>(panel, "ChatMessageRun5");
        potion.EmitSignal(Control.SignalName.MouseEntered);
        await runner.AwaitIdleFrame();
        AssertThat(panel.ItemPreviewForTests.TestState.Visible).IsTrue();

        LanConnectChatChannelState second = EnabledState();
        panel.BindStructured(second, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        AssertThat(panel.ItemPreviewForTests.TestState.Visible).IsFalse();
        AssertThat(panel.ItemPreviewForTests.TestState.ContentNodeCount).IsEqual(0);
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

        AssertThat(RunText(FindNode<Control>(panel, "ChatMessageRun0"))).IsEqual("Potion EN");
        FindNode<Control>(panel, "ChatMessageRun0").EmitSignal(Control.SignalName.MouseEntered);
        await runner.AwaitIdleFrame();
        AssertThat(panel.ItemPreviewForTests.TestState.Visible).IsTrue();

        port.Title = "药水 CN";
        context = new LanConnectItemResolverContext("zh-CN", "mods-a");
        await runner.AwaitIdleFrame();
        AssertThat(RunText(FindNode<Control>(panel, "ChatMessageRun0"))).IsEqual("药水 CN");
        AssertThat(panel.ItemPreviewForTests.TestState.Visible).IsFalse();

        port.Title = "Potion Mod B";
        context = new LanConnectItemResolverContext("zh-CN", "mods-b");
        await runner.AwaitIdleFrame();
        AssertThat(RunText(FindNode<Control>(panel, "ChatMessageRun0"))).IsEqual("Potion Mod B");
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

        Control text = FindNode<Control>(panel, "ChatMessageRun0");
        Rect2 rect = text.GetGlobalRect();
        AssertThat(rect.Size.X).IsGreaterEqual(96f);
        AssertThat(rect.Size.Y).IsGreater(0f);
        AssertThat(rect.Position.X).IsGreaterEqual(0f);
        AssertThat(rect.Position.Y).IsGreaterEqual(0f);
        AssertThat(rect.End.X).IsLessEqual(1280f);
        AssertThat(rect.End.Y).IsLessEqual(720f);
        AssertThat(Enumerable.Range(1, 12)
            .All(index => panel.FindChild($"ChatMessageRun{index}", true, false) != null)).IsTrue();
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

    private static string RunText(Control run) => run switch
    {
        Label label => label.Text,
        _ => run.FindChildren("*", "Label", true, false)
            .OfType<Label>()
            .Select(label => label.Text)
            .FirstOrDefault() ?? string.Empty
    };

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
