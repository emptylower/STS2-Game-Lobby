using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

// Room-chat-HUD-redesign phase 3, Task 3: acceptance tests for the flattened HUD message
// row (BuildFlatMessageRow) and the mandatory guard that the lobby sidebar row
// (BuildLobbyMessageRow) is untouched. See docs/superpowers/plans/2026-07-26-room-chat-hud-phase-3.md.
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectFlatMessageRowTests
{
    private static readonly Regex TimestampShape = new(@"^\d{1,2}:\d{2}$", RegexOptions.Compiled);

    [TestCase]
    public async Task Hud_style_row_has_no_timestamp_label_anywhere_in_it()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedForTests("flat-timestamp", "Toadpole", "hello there", 1, false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Control row = FindNode<Control>(panel, "ChatMessageRow0");
        foreach (Label label in row.FindChildren("*", "Label", true, false).OfType<Label>())
        {
            AssertThat(TimestampShape.IsMatch(label.Text)).IsFalse();
        }
    }

    [TestCase]
    public async Task Hud_style_row_has_no_bubble_panel_container()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedForTests("flat-bubble", "Toadpole", "hello there", 1, false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Control row = FindNode<Control>(panel, "ChatMessageRow0");
        AssertThat(row is PanelContainer).IsFalse();
        AssertThat(row.FindChildren("*", "PanelContainer", true, false).Count).IsEqual(0);
    }

    [TestCase]
    public async Task Hud_style_name_and_content_share_one_view_and_the_name_span_uses_the_sender_colour()
    {
        LanConnectChatChannelState state = EnabledState();
        const string sender = "Toadpole";
        state.AppendConfirmedForTests("flat-name-colour", sender, "hello there", 1, false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Control row = FindNode<Control>(panel, "ChatMessageRow0");
        List<LanConnectRichMessageView> views = row.FindChildren("*", string.Empty, true, false)
            .OfType<LanConnectRichMessageView>()
            .ToList();
        AssertThat(views.Count).IsEqual(1);

        RichTextLabel inline = views[0].FindChildren("*", string.Empty, true, false)
            .OfType<RichTextLabel>()
            .Single();
        AssertThat(inline.GetParsedText()).Contains(sender);
        AssertThat(inline.GetParsedText()).Contains("hello there");

        IReadOnlyList<LanConnectRichMessageSpan> spans = SpansOf(views[0]);
        LanConnectRichMessageSpan nameSpan = spans.Single(span => span.DisplayText == sender);
        AssertThat(nameSpan.Color).IsEqual(LanConnectChatNameColor.ForSender(sender, isLocal: false));
    }

    [TestCase]
    public async Task Lobby_sidebar_style_keeps_its_bubble_and_timestamp_unchanged()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedForTests("lobby-guard", "Toadpole", "hello there", 1, false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem)
        {
            ChatVisualStyle = LanConnectChatVisualStyle.LobbySidebar
        })!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        // The bubble row itself must still be a PanelContainer (CreatePanelStyle), pixel
        // for pixel as before -- this is the mandatory guard against scope creep into the
        // lobby sidebar (spec §2.2 non-goal 1).
        PanelContainer row = FindNode<PanelContainer>(panel, "ChatMessageRow0");
        AssertThat(row).IsNotNull();

        bool hasTimestampLabel = row.FindChildren("*", "Label", true, false)
            .OfType<Label>()
            .Any(label => TimestampShape.IsMatch(label.Text));
        AssertThat(hasTimestampLabel).IsTrue();
    }

    [TestCase]
    public async Task Failed_messages_retry_control_is_tab_focusable_and_enter_triggerable()
    {
        LanConnectChatChannelState state = EnabledState();
        state.BeginPendingText("flat-retry", "Me", "will fail", queuedAt: DateTimeOffset.UtcNow);
        state.MarkFailed("flat-retry", "offline", "offline");
        int retries = 0;
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        // A Failed message's retry resends the content via SendContent (not the Retry
        // callback -- that path is only for DeliveryUnknown), per RetryMessageAsync.
        panel.BindStructured(
            state,
            (_, _) =>
            {
                retries++;
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Button retry = FindNode<Button>(panel, LanConnectConstants.ChatRetryButtonPrefix + "flat-retry");
        AssertThat(retry.FocusMode).IsEqual(Control.FocusModeEnum.All);
        retry.GrabFocus();
        AssertThat(retry.HasFocus()).IsTrue();

        retry.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.AwaitIdleFrame();
        AssertThat(retries).IsEqual(1);
    }

    [TestCase]
    public async Task Single_item_reference_message_renders_the_verb_phrase()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedContentForTests(
            "flat-verb-solo",
            "Toadpole",
            new LanConnectChatContent(1, [new LanConnectItemRefSegment("relic", "MegaCrit.Anchor")]),
            sequence: 1,
            isLocal: false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        RichTextLabel inline = RichMessageText(FindNode<Control>(panel, "ChatMessageRow0"));
        string parsedText = inline.GetParsedText();
        AssertThat(parsedText).Contains("Toadpole");
        AssertThat(parsedText).Contains("分享了遗物：");
        AssertThat(parsedText).Contains("Anchor");
    }

    [TestCase]
    public async Task Mixed_text_and_item_reference_message_keeps_the_plain_form()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedContentForTests(
            "flat-verb-mixed",
            "Toadpole",
            new LanConnectChatContent(1,
            [
                new LanConnectTextSegment("check this out "),
                new LanConnectItemRefSegment("relic", "MegaCrit.Anchor")
            ]),
            sequence: 1,
            isLocal: false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        RichTextLabel inline = RichMessageText(FindNode<Control>(panel, "ChatMessageRow0"));
        string parsedText = inline.GetParsedText();
        AssertThat(parsedText).Contains("Toadpole");
        AssertThat(parsedText.Contains("分享了遗物：", StringComparison.Ordinal)).IsFalse();
        AssertThat(parsedText).Contains("check this out");
        AssertThat(parsedText).Contains("Anchor");
    }

    private static IReadOnlyList<LanConnectRichMessageSpan> SpansOf(LanConnectRichMessageView view)
    {
        FieldInfo field = typeof(LanConnectRichMessageView).GetField(
            "_spans",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (IReadOnlyList<LanConnectRichMessageSpan>)field.GetValue(view)!;
    }

    private static RichTextLabel RichMessageText(Node root) => root
        .FindChildren("*", string.Empty, true, false)
        .OfType<RichTextLabel>()
        .Single();

    private static T FindNode<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);

    private static LanConnectResolvedItem ResolveItem(LanConnectItemRun run) => run.ItemType switch
    {
        "relic" => new LanConnectResolvedItem(
            LanConnectResolvedItemStatus.Resolved,
            "relic",
            "chat.relic",
            "Anchor",
            "Anchor",
            new LanConnectHoverTipPreviewData("relic", "Anchor", "Description", null)),
        _ => new LanConnectResolvedItem(
            LanConnectResolvedItemStatus.Unknown,
            run.ItemType,
            "chat.unknown_item",
            null,
            "unknown",
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
            InstanceId = "flat-row-tests",
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
}
