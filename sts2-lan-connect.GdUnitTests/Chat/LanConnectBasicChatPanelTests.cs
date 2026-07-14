using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectBasicChatPanelTests
{
    [TestCase]
    public async Task Renders_pending_failed_unknown_and_confirmed_with_stable_nodes()
    {
        LanConnectChatChannelState state = WithDeliveryStates();
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);

        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        AssertThat(FindNode<ScrollContainer>(panel, LanConnectConstants.ChatMessagesScrollName)).IsNotNull();
        AssertThat(FindNode<VBoxContainer>(panel, LanConnectConstants.ChatMessagesListName)).IsNotNull();
        AssertThat(FindNode<LineEdit>(panel, LanConnectConstants.ChatDraftInputName)).IsNotNull();
        AssertThat(FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName)).IsNotNull();
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName)).IsNotNull();
        AssertThat(FindNode<Button>(panel, LanConnectConstants.ChatNewMessagesButtonName)).IsNotNull();
        AssertThat(panel.TestState.MessageCount).IsEqual(4);
        AssertThat(panel.TestState.PendingCount).IsEqual(1);
        AssertThat(panel.TestState.FailedCount).IsEqual(1);
        AssertThat(panel.TestState.DeliveryUnknownCount).IsEqual(1);
        AssertThat(panel.TestState.RetryButtonCount).IsEqual(2);
        AssertThat(HasLabel(panel, "发送中")).IsTrue();
        AssertThat(HasLabel(panel, "发送失败：请求过于频繁")).IsTrue();
        AssertThat(HasLabel(panel, "投递状态未知")).IsTrue();
    }

    [TestCase]
    public async Task Failed_retry_sends_new_text_while_same_session_unknown_reuses_id()
    {
        LanConnectChatChannelState state = WithDeliveryStates();
        List<string> sentTexts = new();
        List<string> retriedIds = new();
        TaskCompletionSource sendRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(
            state,
            text =>
            {
                sentTexts.Add(text);
                state.AppendConfirmedForTests("refresh-during-retry", "A", "refresh", 50, false);
                return sendRelease.Task;
            },
            id =>
            {
                retriedIds.Add(id);
                return Task.CompletedTask;
            });
        await runner.AwaitIdleFrame();

        FindNode<Button>(panel, LanConnectConstants.ChatRetryButtonPrefix + "client-failed")
            .EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();
        sendRelease.SetResult();
        await runner.AwaitIdleFrame();
        FindNode<Button>(panel, LanConnectConstants.ChatRetryButtonPrefix + "client-unknown")
            .EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();

        AssertThat(sentTexts.Count).IsEqual(1);
        AssertThat(sentTexts[0]).IsEqual("failed text");
        AssertThat(retriedIds.Count).IsEqual(1);
        AssertThat(retriedIds[0]).IsEqual("client-unknown");
    }

    [TestCase]
    public async Task Disconnected_unknown_requires_confirmation_before_new_send()
    {
        LanConnectChatChannelState state = EnabledState();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.BeginPendingText("client-cross-session", "A", "possibly sent", queuedAt: now - TimeSpan.FromSeconds(20));
        state.MarkTimedOut(now);
        state.MarkDisconnected();
        List<string> sentTexts = new();
        List<string> retriedIds = new();
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(
            state,
            text =>
            {
                sentTexts.Add(text);
                return Task.CompletedTask;
            },
            id =>
            {
                retriedIds.Add(id);
                return Task.CompletedTask;
            });
        await runner.AwaitIdleFrame();

        AssertThat(HasLabel(panel, "可能已发送，确认后重发")).IsTrue();
        FindNode<Button>(panel, LanConnectConstants.ChatRetryButtonPrefix + "client-cross-session")
            .EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();

        ConfirmationDialog dialog = FindNode<ConfirmationDialog>(panel, "DisconnectedUnknownConfirmation");
        AssertThat(dialog.Visible).IsTrue();
        AssertThat(sentTexts.Count).IsEqual(0);
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        await runner.AwaitIdleFrame();

        AssertThat(sentTexts.Count).IsEqual(1);
        AssertThat(sentTexts[0]).IsEqual("possibly sent");
        AssertThat(retriedIds.Count).IsEqual(0);
    }

    [TestCase]
    public async Task Draft_and_send_controls_update_state_and_clear_after_success()
    {
        LanConnectChatChannelState state = EnabledState();
        List<string> sentTexts = new();
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(
            state,
            text =>
            {
                sentTexts.Add(text);
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        LineEdit input = FindNode<LineEdit>(panel, LanConnectConstants.ChatDraftInputName);
        input.Text = "new draft";
        input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
        AssertThat(state.Draft).IsEqual("new draft");
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();

        AssertThat(sentTexts.Count).IsEqual(1);
        AssertThat(sentTexts[0]).IsEqual("new draft");
        AssertThat(state.Draft).IsEqual(string.Empty);
        AssertThat(input.Text).IsEqual(string.Empty);
        AssertThat(panel.TestState.InputEditable).IsTrue();
        AssertThat(panel.TestState.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
    }

    [TestCase]
    public async Task Scrolled_up_panel_preserves_offset_and_exposes_new_message_action()
    {
        LanConnectChatChannelState state = EnabledState();
        for (int index = 0; index < 20; index++)
        {
            state.AppendConfirmedForTests($"message-{index}", "A", $"message {index}", index + 1, false);
        }

        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel
        {
            CustomMinimumSize = new Vector2(480, 300)
        })!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        panel.SetScrollForTests(72, atBottom: false);

        state.AppendConfirmedForTests("new", "A", "new", 100, false);
        await panel.RefreshForTests();

        AssertThat(panel.TestState.ScrollOffset).IsEqual(72d);
        AssertThat(panel.TestState.IsAtBottom).IsFalse();
        AssertThat(panel.TestState.NewMessagesBelowCount).IsEqual(1);
        Button newMessages = FindNode<Button>(panel, LanConnectConstants.ChatNewMessagesButtonName);
        AssertThat(newMessages.Visible).IsTrue();
        AssertThat(newMessages.Text).IsEqual("有 1 条新消息");

        newMessages.EmitSignal(Button.SignalName.Pressed);
        AssertThat(panel.TestState.IsAtBottom).IsTrue();
        AssertThat(panel.TestState.NewMessagesBelowCount).IsEqual(0);
    }

    private static LanConnectChatChannelState WithDeliveryStates()
    {
        LanConnectChatChannelState state = EnabledState();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.AppendConfirmedForTests("confirmed", "A", "confirmed text", 1, false);
        state.BeginPendingText("client-pending", "Me", "pending text", queuedAt: now);
        state.BeginPendingText("client-failed", "Me", "failed text", queuedAt: now);
        state.MarkFailed("client-failed", "rate_limited", "请求过于频繁");
        state.BeginPendingText("client-unknown", "Me", "unknown text", queuedAt: now - TimeSpan.FromSeconds(20));
        state.MarkTimedOut(now);
        return state;
    }

    private static LanConnectChatChannelState EnabledState()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            InstanceId = "panel-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures()
        });
        return state;
    }

    private static bool HasLabel(Node root, string text)
    {
        foreach (Node node in root.FindChildren("*", "Label", recursive: true, owned: false))
        {
            if (node is Label label && label.Text == text)
            {
                return true;
            }
        }

        return false;
    }

    private static T FindNode<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);
}
