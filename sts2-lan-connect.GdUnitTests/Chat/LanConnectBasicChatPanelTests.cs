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
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual(string.Empty);
        AssertThat(panel.TestState.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
    }

    [TestCase]
    public async Task Rebinding_updates_stable_channel_title_and_ready_status()
    {
        LanConnectChatChannelState room = new(LanConnectChatChannel.Room);
        LanConnectChatChannelState server = EnabledState();
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);

        panel.Bind(room, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        AssertThat(FindNode<Label>(panel, "ChatChannelTitle").Text).IsEqual("房间聊天");
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text)
            .IsEqual("请输入消息");

        panel.Bind(server, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        AssertThat(FindNode<Label>(panel, "ChatChannelTitle").Text).IsEqual("频道聊天");
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text)
            .IsEqual("请输入消息");
    }

    [TestCase]
    public async Task Rich_entity_draft_tracks_hidden_budget_and_blocks_legacy_send_without_clearing()
    {
        LanConnectChatChannelState state = EnabledState(rich: true);
        state.RichDraft.InsertEntity(new LanConnectEmojiRun("heart"));
        int sendCalls = 0;
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ =>
        {
            sendCalls++;
            return Task.CompletedTask;
        }, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Button send = FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName);
        Label budget = FindNode<Label>(panel, LanConnectConstants.ChatDraftBudgetName);
        AssertThat(send.Disabled).IsTrue();
        AssertThat(budget.Visible).IsFalse();
        AssertThat(budget.Text.Contains("字符 0/300 · 分段 1/32 · 实体 1/12", StringComparison.Ordinal))
            .IsTrue();
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text)
            .IsEqual("当前频道不支持草稿中的富内容");

        send.EmitSignal(Button.SignalName.Pressed);

        AssertThat(sendCalls).IsEqual(0);
        AssertThat(state.RichDraft.Runs).ContainsExactly(new LanConnectEmojiRun("heart"));
    }

    [TestCase]
    public async Task Closing_and_switching_channels_never_flattens_ordered_rich_runs()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        LanConnectRichDraft draft = fixture.State.Server.RichDraft;
        draft.ReplaceAllWithText("before ");
        draft.InsertEntity(new LanConnectEmojiRun("heart"));
        draft.InsertText(" after");
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        await fixture.Overlay.RefreshForTests();
        await fixture.Runner.AwaitIdleFrame();

        await fixture.Overlay.CloseForTests();
        await fixture.Overlay.OpenForTests();
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Room);
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(draft.Runs).ContainsExactly(
            new LanConnectTextRun("before "),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun(" after"));
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
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual("keep this draft");
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
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName)).IsNotNull();
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

        TextEdit input = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName);
        input.Text = "new draft";
        input.EmitSignal(TextEdit.SignalName.TextChanged);
        AssertThat(state.Draft).IsEqual("new draft");
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();

        AssertThat(sentTexts.Count).IsEqual(1);
        AssertThat(sentTexts[0]).IsEqual("new draft");
        AssertThat(state.Draft).IsEqual(string.Empty);
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text)
            .IsEqual(string.Empty);
        AssertThat(panel.TestState.InputEditable).IsTrue();
        AssertThat(panel.TestState.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
    }

    [TestCase]
    public async Task Structured_send_failure_retains_text_entity_order_and_real_text_focus()
    {
        LanConnectChatChannelState state = EnabledState(rich: true);
        state.RichDraft.ReplaceAllWithText("发送前");
        state.RichDraft.InsertEntity(new LanConnectItemRun("card", "MegaCrit.Strike", 1));
        state.RichDraft.InsertText("发送后");
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(
            state,
            (_, _) => Task.FromException(new InvalidOperationException("offline")),
            _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName)
            .EmitSignal(Button.SignalName.Pressed);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        AssertThat(state.RichDraft.Runs).ContainsExactly(
            new LanConnectTextRun("发送前"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            new LanConnectTextRun("发送后"));
        AssertThat(panel.DraftHasFocus).IsTrue();
        AssertThat(panel.GetViewport().GuiGetFocusOwner() is TextEdit).IsTrue();
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text)
            .Contains("offline");
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
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual("draft B");
        releaseA.SetResult();
        await runner.AwaitIdleFrame();

        AssertThat(stateA.Draft).IsEqual(string.Empty);
        AssertThat(stateB.Draft).IsEqual("draft B");
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual("draft B");
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text).IsEqual("频道可用");
        AssertThat(panel.TestState.InputEditable).IsTrue();
    }

    [TestCase]
    public async Task Failed_send_after_rebind_preserves_old_and_new_channel_drafts()
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
        releaseA.SetException(new InvalidOperationException("offline"));
        await runner.AwaitIdleFrame();

        AssertThat(stateA.Draft).IsEqual("draft A");
        AssertThat(stateB.Draft).IsEqual("draft B");
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual("draft B");
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text).IsEqual("频道可用");
    }

    [TestCase]
    public async Task Old_success_cannot_clear_retyped_same_text_after_returning_to_channel()
    {
        LanConnectChatChannelState stateA = EnabledState();
        LanConnectChatChannelState stateB = EnabledState();
        TaskCompletionSource releaseA = new(TaskCreationOptions.RunContinuationsAsynchronously);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(stateA, _ => releaseA.Task, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        SetDraft(panel, "hello");
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        panel.Bind(stateB, _ => Task.CompletedTask, _ => Task.CompletedTask);
        panel.Bind(stateA, _ => Task.CompletedTask, _ => Task.CompletedTask);
        SetDraft(panel, "other");
        SetDraft(panel, "hello");
        releaseA.SetResult();
        await runner.AwaitIdleFrame();

        AssertThat(stateA.Draft).IsEqual("hello");
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual("hello");
        AssertThat(panel.TestState.InputEditable).IsTrue();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Send_in_flight_stays_owned_by_channel_across_tab_round_trip(bool succeeds)
    {
        LanConnectChatChannelState stateA = EnabledState();
        LanConnectChatChannelState stateB = EnabledState();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int sendCalls = 0;
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(stateA, _ =>
        {
            sendCalls++;
            return release.Task;
        }, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        SetDraft(panel, "hello");
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        panel.Bind(stateB, _ => Task.CompletedTask, _ => Task.CompletedTask);
        AssertThat(panel.TestState.InputEditable).IsTrue();
        panel.Bind(stateA, _ =>
        {
            sendCalls++;
            return release.Task;
        }, _ => Task.CompletedTask);
        AssertThat(panel.TestState.InputEditable).IsFalse();
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        AssertThat(sendCalls).IsEqual(1);

        if (succeeds)
        {
            release.SetResult();
        }
        else
        {
            release.SetException(new InvalidOperationException("offline"));
        }
        await runner.AwaitIdleFrame();

        AssertThat(panel.TestState.InputEditable).IsTrue();
        AssertThat(stateA.Draft).IsEqual(succeeds ? string.Empty : "hello");
    }

    [TestCase]
    public async Task Retry_in_flight_stays_disabled_across_tab_round_trip()
    {
        LanConnectChatChannelState stateA = WithDeliveryStates();
        LanConnectChatChannelState stateB = EnabledState();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int retryCalls = 0;
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        Func<string, Task> send = _ =>
        {
            retryCalls++;
            return release.Task;
        };
        panel.Bind(stateA, send, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        string buttonName = LanConnectConstants.ChatRetryButtonPrefix + "client-failed";

        FindNode<Button>(panel, buttonName).EmitSignal(Button.SignalName.Pressed);
        panel.Bind(stateB, _ => Task.CompletedTask, _ => Task.CompletedTask);
        panel.Bind(stateA, send, _ => Task.CompletedTask);
        Button retry = FindNode<Button>(panel, buttonName);
        AssertThat(retry.Disabled).IsTrue();
        retry.EmitSignal(Button.SignalName.Pressed);
        AssertThat(retryCalls).IsEqual(1);

        release.SetResult();
        await runner.AwaitIdleFrame();

        AssertThat(FindNode<Button>(panel, buttonName).Disabled).IsFalse();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Old_room_send_completion_cannot_mutate_reused_state_in_new_room(bool succeeds)
    {
        LanConnectDualChatState chat = new(new LanConnectChatChannelState(LanConnectChatChannel.Server));
        chat.EnterRoom("room-a");
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(chat.Room, _ => release.Task, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        SetDraft(panel, "same draft");
        FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).EmitSignal(Button.SignalName.Pressed);
        chat.LeaveRoom();
        chat.EnterRoom("room-b");
        chat.Room.SetDraft("same draft");
        await runner.AwaitIdleFrame();
        FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).GrabFocus();
        AssertThat(panel.TestState.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
        if (succeeds)
        {
            release.SetResult();
        }
        else
        {
            release.SetException(new InvalidOperationException("offline"));
        }
        await runner.AwaitIdleFrame();

        AssertThat(chat.Room.Draft).IsEqual("same draft");
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text).IsEqual("same draft");
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text)
            .IsEqual("房间聊天可用");
        AssertThat(panel.TestState.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
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

        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text).IsEqual("请输入消息");
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
    public async Task Deferred_message_focus_restore_never_steals_a_new_external_focus_target()
    {
        LanConnectChatChannelState state = EnabledState();
        state.BeginPendingText("focus-retry", "Me", "failed");
        state.MarkFailed("focus-retry", "offline", "offline");
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        Button retry = FindNode<Button>(panel, LanConnectConstants.ChatRetryButtonPrefix + "focus-retry");
        retry.GrabFocus();

        state.AppendConfirmedForTests("new-row", "A", "new row", 2, false);
        await panel.RefreshForTests();
        Button send = FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName);
        send.GrabFocus();
        await runner.AwaitIdleFrame();

        AssertThat(panel.TestState.FocusOwnerName).IsEqual(LanConnectConstants.ChatSendButtonName);
    }

    [TestCase]
    public async Task Deferred_message_focus_restore_never_steals_another_retry_in_the_same_list()
    {
        LanConnectChatChannelState state = EnabledState();
        state.BeginPendingText("focus-retry-a", "Me", "failed a");
        state.MarkFailed("focus-retry-a", "offline", "offline");
        state.BeginPendingText("focus-retry-b", "Me", "failed b");
        state.MarkFailed("focus-retry-b", "offline", "offline");
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        FindNode<Button>(panel, LanConnectConstants.ChatRetryButtonPrefix + "focus-retry-a")
            .GrabFocus();

        state.AppendConfirmedForTests("new-row", "A", "new row", 3, false);
        await panel.RefreshForTests();
        Button retryB = FindNode<Button>(
            panel,
            LanConnectConstants.ChatRetryButtonPrefix + "focus-retry-b");
        retryB.GrabFocus();
        await runner.AwaitIdleFrame();

        AssertThat(panel.TestState.FocusOwnerName)
            .IsEqual(LanConnectConstants.ChatRetryButtonPrefix + "focus-retry-b");
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

    [TestCase(Key.Enter)]
    [TestCase(Key.KpEnter)]
    public async Task Draft_gui_input_enter_submits_without_inserting_newline(Key key)
    {
        LanConnectChatChannelState state = EnabledState();
        int sendCount = 0;
        string sentText = string.Empty;
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(
            state,
            text =>
            {
                sendCount++;
                sentText = text;
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        SetDraft(panel, "send from text edit");

        TextEdit input = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName);
        input.GrabFocus();
        PushKey(panel.GetViewport(), key, shiftPressed: false);
        await runner.AwaitIdleFrame();

        AssertThat(sendCount).IsEqual(1);
        AssertThat(sentText).IsEqual("send from text edit");
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text)
            .IsEqual(string.Empty);
    }

    [TestCase]
    public async Task Draft_gui_input_shift_enter_inserts_newline_without_sending()
    {
        LanConnectChatChannelState state = EnabledState();
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
        SetDraft(panel, "first line");

        TextEdit input = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName);
        input.SetCaretLine(0);
        input.SetCaretColumn(input.Text.Length);
        input.GrabFocus();
        PushKey(panel.GetViewport(), Key.Enter, shiftPressed: true);
        await runner.AwaitIdleFrame();

        AssertThat(sendCount).IsEqual(0);
        AssertThat(FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName).Text)
            .IsEqual("first line\n");
        AssertThat(state.Draft).IsEqual("first line\n");
    }

    [TestCase]
    public async Task Draft_height_grows_from_one_to_three_lines_and_stays_capped()
    {
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel
        {
            CustomMinimumSize = new Vector2(360, 300)
        })!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(EnabledState(), _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        SetDraft(panel, "one");
        await runner.AwaitIdleFrame();
        float oneLineHeight = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName)
            .CustomMinimumSize.Y;

        SetDraft(panel, "one\ntwo\nthree");
        await runner.AwaitIdleFrame();
        float threeLineHeight = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName)
            .CustomMinimumSize.Y;

        SetDraft(panel, "one\ntwo\nthree\nfour\nfive");
        await runner.AwaitIdleFrame();
        float fiveLineHeight = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName)
            .CustomMinimumSize.Y;

        AssertThat(threeLineHeight).IsGreater(oneLineHeight);
        AssertThat(fiveLineHeight).IsEqual(threeLineHeight);
    }

    [TestCase]
    public async Task Draft_over_scalar_limit_preserves_text_and_disables_send_with_specific_reason()
    {
        LanConnectChatChannelState state = EnabledState();
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel())!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        string accepted = new('a', 300);
        SetDraft(panel, accepted);

        TextEdit input = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName);
        input.SetCaretLine(0);
        input.SetCaretColumn(150);
        input.InsertTextAtCaret("😀");
        await runner.AwaitIdleFrame();

        TextEdit rebuilt = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName);
        AssertThat(state.Draft).IsEqual(accepted[..150] + "😀" + accepted[150..]);
        AssertThat(LanConnectChatTextProtocol.CountUnicodeScalars(rebuilt.Text)).IsEqual(301);
        AssertThat(FindNode<Button>(panel, LanConnectConstants.ChatSendButtonName).Disabled).IsTrue();
        AssertThat(FindNode<Label>(panel, LanConnectConstants.ChatStatusLabelName).Text)
            .IsEqual("消息文字超过 300 字符");
        AssertThat(rebuilt.Text[^1].ToString()).IsEqual("a");
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

    private static LanConnectChatChannelState EnabledState(bool rich = false)
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            InstanceId = "panel-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures
            {
                RichContentVersion = rich ? 1 : 0,
                EmojiSetVersion = rich ? 1 : 0,
                ItemRefVersion = rich ? 1 : 0
            }
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
        TextEdit input = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName);
        input.Text = text;
        input.EmitSignal(TextEdit.SignalName.TextChanged);
    }

    private static void PushKey(Viewport viewport, Key key, bool shiftPressed)
    {
        viewport.PushInput(new InputEventKey
        {
            Keycode = key,
            Pressed = true,
            Echo = false,
            ShiftPressed = shiftPressed
        });
        viewport.PushInput(new InputEventKey
        {
            Keycode = key,
            Pressed = false,
            Echo = false,
            ShiftPressed = shiftPressed
        });
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
