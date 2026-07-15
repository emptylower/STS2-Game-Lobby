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

    private static LanConnectChatContent Content() => new(1,
    [
        new LanConnectTextSegment("before "),
        new LanConnectItemRefSegment("card", "MegaCrit.Strike", 2),
        new LanConnectTextSegment(" middle "),
        new LanConnectItemRefSegment("relic", "PrivateMod.SecretRelic"),
        new LanConnectEmojiSegment("heart"),
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
}
