using System.Text.Json;
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
    public async Task Room_channel_is_editable_and_send_gate_clears_draft_and_restores_focus_by_default()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
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

        AssertThat(panel.TestState.InputEditable).IsTrue();
        SetDraft(panel, "room hello");
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();

        AssertThat(sentTexts.Count).IsEqual(1);
        AssertThat(sentTexts[0]).IsEqual("room hello");
        AssertThat(state.Draft).IsEqual(string.Empty);
        AssertThat(FindNode<LineEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual(string.Empty);
        AssertThat(panel.TestState.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
    }

    [TestCase]
    public async Task Connection_presentation_states_drive_status_and_editability_without_clearing_draft()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            InstanceId = "presentation-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures()
        });
        state.SetDraft("keep this draft");
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        AssertPresentation(panel, state, LanConnectServerChatPresentation.Connecting, "正在连接频道...", editable: false);
        AssertPresentation(panel, state, LanConnectServerChatPresentation.Reconnecting, "频道连接中断，正在重连...", editable: false);
        AssertPresentation(panel, state, LanConnectServerChatPresentation.TransportFailure, "频道连接失败：network down", editable: false, "network down");
        AssertPresentation(panel, state, LanConnectServerChatPresentation.Disabled, "频道已由服务器停用", editable: false);
        AssertPresentation(panel, state, LanConnectServerChatPresentation.Unsupported, "当前服务器不支持频道聊天", editable: false);
        AssertPresentation(panel, state, LanConnectServerChatPresentation.Ready, "频道可用", editable: true);

        AssertThat(state.Draft).IsEqual("keep this draft");
        AssertThat(FindNode<LineEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual("keep this draft");
    }

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
    public async Task Synchronous_send_queue_is_rendered_on_the_next_idle_frame()
    {
        LanConnectChatChannelState state = EnabledState();
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(
            state,
            text =>
            {
                state.BeginPendingText("sync-pending", "Me", text);
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        SetDraft(panel, "queued immediately");
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();

        AssertThat(panel.TestState.PendingCount).IsEqual(1);
        AssertThat(HasLabel(panel, "发送中")).IsTrue();
    }

    [TestCase]
    public async Task Rebinding_while_send_is_in_flight_isolates_new_draft_status_and_editability()
    {
        LanConnectChatChannelState stateA = EnabledState();
        LanConnectChatChannelState stateB = EnabledState();
        stateB.SetDraft("draft B");
        TaskCompletionSource releaseA = new(TaskCreationOptions.RunContinuationsAsynchronously);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(stateA, _ => releaseA.Task, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        SetDraft(panel, "draft A");
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        panel.Bind(stateB, _ => Task.CompletedTask, _ => Task.CompletedTask);

        AssertThat(panel.TestState.InputEditable).IsTrue();
        AssertThat(FindNode<LineEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual("draft B");
        releaseA.SetResult();
        await runner.AwaitIdleFrame();

        AssertThat(stateB.Draft).IsEqual("draft B");
        AssertThat(FindNode<LineEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual("draft B");
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text).IsEqual("频道可用");
        AssertThat(panel.TestState.InputEditable).IsTrue();
    }

    [TestCase]
    public async Task Rebinding_while_retry_is_in_flight_ignores_old_completion_status()
    {
        LanConnectChatChannelState stateA = WithDeliveryStates();
        LanConnectChatChannelState stateB = EnabledState();
        TaskCompletionSource releaseA = new(TaskCreationOptions.RunContinuationsAsynchronously);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(stateA, _ => releaseA.Task, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        FindNode<Button>(panel, LanConnectConstants.ChatRetryButtonPrefix + "client-failed")
            .EmitSignal(Button.SignalName.Pressed);
        panel.Bind(stateB, _ => Task.CompletedTask, _ => Task.CompletedTask);
        releaseA.SetResult();
        await runner.AwaitIdleFrame();

        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text).IsEqual("频道可用");
        AssertThat(panel.TestState.InputEditable).IsTrue();
    }

    [TestCase("failed")]
    [TestCase("unknown")]
    public async Task Retry_remains_disabled_across_rebuild_and_suppresses_duplicate_press(string delivery)
    {
        LanConnectChatChannelState state = WithDeliveryStates();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int retryCount = 0;
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(
            state,
            text =>
            {
                retryCount++;
                state.BeginPendingText("new-pending-from-retry", "Me", text);
                return release.Task;
            },
            _ =>
            {
                retryCount++;
                state.AppendConfirmedForTests("refresh-unknown-retry", "A", "refresh", 80, false);
                return release.Task;
            });
        await runner.AwaitIdleFrame();

        string clientMessageId = delivery == "failed" ? "client-failed" : "client-unknown";
        string retryName = LanConnectConstants.ChatRetryButtonPrefix + clientMessageId;
        FindNode<Button>(panel, retryName).EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();

        Button rebuiltRetry = FindNode<Button>(panel, retryName);
        AssertThat(rebuiltRetry.Disabled).IsTrue();
        rebuiltRetry.EmitSignal(Button.SignalName.Pressed);
        AssertThat(retryCount).IsEqual(1);

        release.SetResult();
        await runner.AwaitIdleFrame();

        AssertThat(FindNode<Button>(panel, retryName).Disabled).IsFalse();
    }

    [TestCase]
    public async Task Completing_send_after_panel_is_freed_does_not_touch_invalid_controls()
    {
        LanConnectChatChannelState state = EnabledState();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Control root = AutoFree(new Control())!;
        LanConnectBasicChatPanel panel = new();
        root.AddChild(panel);
        using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        panel.Bind(state, _ => release.Task, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        SetDraft(panel, "in flight");
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        panel.QueueFree();
        await runner.AwaitIdleFrame();
        release.SetResult();
        await runner.AwaitIdleFrame();

        AssertThat(GodotObject.IsInstanceValid(panel)).IsFalse();
    }

    [TestCase("ack")]
    [TestCase("clear")]
    [TestCase("rebind")]
    public async Task Invalidated_disconnected_unknown_confirmation_does_not_send(string invalidation)
    {
        LanConnectChatChannelState state = DisconnectedUnknownState("confirm-invalidated", "possibly sent");
        int sendCount = 0;
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(
            state,
            _ =>
            {
                sendCount++;
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        FindNode<Button>(panel, LanConnectConstants.ChatRetryButtonPrefix + "confirm-invalidated")
            .EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();
        ConfirmationDialog dialog = FindNode<ConfirmationDialog>(panel, "DisconnectedUnknownConfirmation");
        switch (invalidation)
        {
            case "ack":
                state.Apply(BuildAck("confirm-invalidated", "confirmed-after-dialog", "possibly sent"));
                break;
            case "clear":
                state.ClearForContextChange();
                break;
            case "rebind":
                panel.Bind(EnabledState(), _ => Task.CompletedTask, _ => Task.CompletedTask);
                break;
        }

        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        await runner.AwaitIdleFrame();

        AssertThat(sendCount).IsEqual(0);
    }

    [TestCase]
    public async Task Revision_added_during_refresh_is_rendered_on_the_next_frame()
    {
        LanConnectChatChannelState state = EnabledState();
        state.AppendConfirmedForTests("before-refresh", "A", "before refresh", 1, false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        VBoxContainer list = FindNode<VBoxContainer>(panel, LanConnectConstants.ChatMessagesListName);
        bool mutated = false;
        list.Connect(Node.SignalName.ChildEnteredTree, Callable.From<Node>(_ =>
        {
            if (mutated)
            {
                return;
            }

            mutated = true;
            state.AppendConfirmedForTests("during-refresh", "B", "during refresh", 2, false);
        }));

        state.AppendConfirmedForTests("refresh-trigger", "A", "trigger", 3, false);
        await panel.RefreshForTests();
        await runner.AwaitIdleFrame();

        AssertThat(mutated).IsTrue();
        AssertThat(HasLabel(panel, "during refresh")).IsTrue();
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
        await runner.AwaitIdleFrame();
        ScrollBar bar = FindNode<ScrollContainer>(panel, LanConnectConstants.ChatMessagesScrollName).GetVScrollBar();
        double offset = Math.Max(1, Math.Min(72, BottomValue(bar) / 2));
        panel.SetScrollForTests(offset, atBottom: false);
        await runner.AwaitIdleFrame();

        state.AppendConfirmedForTests("new", "A", "new", 100, false);
        await panel.RefreshForTests();
        await runner.AwaitIdleFrame();

        AssertThat(panel.TestState.ScrollOffset).IsEqual(offset);
        AssertThat(bar.Value).IsEqual(offset);
        AssertThat(panel.TestState.IsAtBottom).IsFalse();
        AssertThat(panel.TestState.NewMessagesBelowCount).IsEqual(1);
        Button newMessages = FindNode<Button>(panel, LanConnectConstants.ChatNewMessagesButtonName);
        AssertThat(newMessages.Visible).IsTrue();
        AssertThat(newMessages.Text).IsEqual("有 1 条新消息");

        newMessages.EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(panel.TestState.IsAtBottom).IsTrue();
        AssertThat(panel.TestState.NewMessagesBelowCount).IsEqual(0);
        AssertThat(bar.Value).IsEqual(BottomValue(bar));
    }

    [TestCase]
    public async Task Rebinding_from_empty_channel_restores_saved_non_bottom_offset_after_layout()
    {
        LanConnectChatChannelState room = new(LanConnectChatChannel.Room);
        for (int index = 0; index < 30; index++)
        {
            room.AppendConfirmedForTests($"room-{index}", "A", $"room message {index}", index + 1, false);
        }
        LanConnectChatChannelState server = EnabledState();
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel
        {
            CustomMinimumSize = new Vector2(480, 300)
        })!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(room, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        ScrollBar bar = FindNode<ScrollContainer>(panel, LanConnectConstants.ChatMessagesScrollName).GetVScrollBar();
        double savedOffset = Math.Max(1, Math.Min(96, BottomValue(bar) / 2));
        panel.SetScrollForTests(savedOffset, atBottom: false);

        panel.Bind(server, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        panel.Bind(room, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        AssertThat(room.ScrollOffset).IsEqual(savedOffset);
        AssertThat(bar.Value).IsEqual(savedOffset);
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
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }

    private static LanConnectChatChannelState DisconnectedUnknownState(string clientMessageId, string text)
    {
        LanConnectChatChannelState state = EnabledState();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.BeginPendingText(clientMessageId, "A", text, queuedAt: now - TimeSpan.FromSeconds(20));
        state.MarkTimedOut(now);
        state.MarkDisconnected();
        return state;
    }

    private static ServerChatInboundEnvelope BuildAck(string clientMessageId, string messageId, string text)
    {
        ServerChatAckEnvelope envelope = new()
        {
            ClientMessageId = clientMessageId,
            Message = new ServerChatCanonicalMessage
            {
                MessageId = messageId,
                SenderId = "sender",
                SenderName = "A",
                Content = new ServerChatContent
                {
                    Segments = new List<ServerChatTextSegment>
                    {
                        new() { Text = text }
                    }
                },
                PlainTextFallback = text,
                SentAt = DateTimeOffset.UtcNow
            }
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(
            JsonSerializer.Serialize(envelope, LanConnectJson.Options),
            LanConnectJson.Options)!;
    }

    private static void SetDraft(Node panel, string text)
    {
        LineEdit input = FindNode<LineEdit>(panel, LanConnectConstants.ChatDraftInputName);
        input.Text = text;
        input.EmitSignal(LineEdit.SignalName.TextChanged, text);
    }

    private static void AssertPresentation(
        LanConnectBasicChatPanel panel,
        LanConnectChatChannelState state,
        LanConnectServerChatPresentation presentation,
        string status,
        bool editable,
        string? detail = null)
    {
        state.SetPresentationForTests(presentation, detail);
        panel.RefreshForTests().GetAwaiter().GetResult();
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text).IsEqual(status);
        AssertThat(panel.TestState.InputEditable).IsEqual(editable);
        AssertThat(state.Draft).IsEqual("keep this draft");
    }

    private static double BottomValue(ScrollBar bar) =>
        Math.Max(bar.MinValue, bar.MaxValue - bar.Page);

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
