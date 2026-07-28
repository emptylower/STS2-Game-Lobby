using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatModerationReviewTests
{
    private static readonly Uri BaseUri = new("https://lobby.example/base/");
    private static readonly Uri ChatUri = new("wss://chat.example/session");
    private static readonly Guid FixedGuid = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid SecondGuid = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public void NameModerationErrorsUseGamePanelCopyInsteadOfRawServiceErrors()
    {
        LobbyServiceException blocked = new("raw backend message", "content_blocked", 400);
        Assert.True(LanConnectModerationUiMessages.IsContentBlocked(blocked));
        Assert.Equal(
            "包含敏感词，请修改房间名或用户名后重试。",
            LanConnectModerationUiMessages.DescribeCreateRoomFailure(blocked));
        Assert.Equal("包含敏感词，请修改用户名后重试。", LanConnectModerationUiMessages.PlayerNameBlocked);

        LobbyServiceException other = new("连接失败", "http_error", 500);
        Assert.False(LanConnectModerationUiMessages.IsContentBlocked(other));
        Assert.Equal("大厅服务创建房间失败：连接失败", LanConnectModerationUiMessages.DescribeCreateRoomFailure(other));
    }

    // ------------------------------------------------------------------
    // Channel state machine
    // ------------------------------------------------------------------

    [Fact]
    public void ReviewPendingMarksOnlyPendingEntriesAndIsIdempotent()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.BeginPendingText("m1", "Me", "hello", "net-1");
        state.BeginPendingText("m2", "Me", "world", "net-1");

        state.MarkReviewPending("m1");
        Assert.Equal(ServerChatDeliveryState.Reviewing, FindMessage(state, "m1").Delivery);
        Assert.Equal(ServerChatDeliveryState.Pending, FindMessage(state, "m2").Delivery);
        Assert.True(state.HasReviewPending);
        long revision = state.Revision;

        state.MarkReviewPending("m1");
        Assert.Equal(revision, state.Revision);
        state.MarkReviewPending("missing-id");
        Assert.Equal(revision, state.Revision);

        state.Apply(BuildCanonicalAckEnvelope("m2", "srv-2"), "net-1");
        Assert.Equal(ServerChatDeliveryState.Confirmed, FindMessage(state, "m2").Delivery);
        state.MarkReviewPending("m2");
        Assert.Equal(ServerChatDeliveryState.Confirmed, FindMessage(state, "m2").Delivery);
    }

    [Fact]
    public void AckAfterReviewConfirmsOriginalEntryExactlyOnce()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.BeginPendingText("m1", "Me", "hello", "net-1");
        state.MarkReviewPending("m1");

        state.Apply(BuildCanonicalAckEnvelope("m1", "srv-1"), "net-1");

        ServerChatMessageState message = Assert.Single(state.Messages);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
        Assert.Equal("srv-1", message.MessageId);
        Assert.True(message.IsLocal);
        Assert.False(state.HasReviewPending);
        // No "review passed" notice is ever raised for a successful ACK.
        Assert.Equal(LanConnectChatModerationNotice.None, state.ModerationNotice);
        Assert.Equal(0, state.ModerationNoticeSequence);
    }

    [Fact]
    public void PublicListProjectionHidesPendingAndReviewingUntilTerminalDelivery()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.BeginPendingText("m1", "Me", "hello", "net-1");
        ServerChatMessageState pending = Assert.Single(state.Messages);
        Assert.False(LanConnectBasicChatPanel.IsVisibleInPublicMessageList(pending));

        state.MarkReviewPending("m1");
        ServerChatMessageState reviewing = Assert.Single(state.Messages);
        Assert.False(LanConnectBasicChatPanel.IsVisibleInPublicMessageList(reviewing));

        state.Apply(BuildCanonicalAckEnvelope("m1", "srv-1"), "net-1");
        ServerChatMessageState confirmed = Assert.Single(state.Messages);
        Assert.True(LanConnectBasicChatPanel.IsVisibleInPublicMessageList(confirmed));
    }

    [Fact]
    public void ContentBlockedRemovesEntryAndRaisesNoticeExactlyOnce()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.BeginPendingText("m1", "Me", "hello", "net-1");
        state.MarkReviewPending("m1");

        Assert.True(state.MarkContentBlocked("m1"));

        Assert.Empty(state.Messages);
        Assert.False(state.HasReviewPending);
        Assert.Equal(LanConnectChatModerationNotice.ContentBlocked, state.ModerationNotice);
        Assert.Equal(1, state.ModerationNoticeSequence);

        // A replayed terminal frame must not raise a second notice.
        Assert.False(state.MarkContentBlocked("m1"));
        Assert.Equal(1, state.ModerationNoticeSequence);
    }

    [Fact]
    public void ContentBlockedAfterAckKeepsConfirmedMessageAndStaysSilent()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.BeginPendingText("m1", "Me", "hello", "net-1");
        state.Apply(BuildCanonicalAckEnvelope("m1", "srv-1"), "net-1");

        Assert.False(state.MarkContentBlocked("m1"));

        Assert.Single(state.Messages);
        Assert.Equal(LanConnectChatModerationNotice.None, state.ModerationNotice);
        Assert.Equal(0, state.ModerationNoticeSequence);
    }

    [Fact]
    public void ModerationBusyDropsOnlyTheNewerSend()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.BeginPendingText("m1", "Me", "first", "net-1");
        state.MarkReviewPending("m1");
        state.BeginPendingText("m2", "Me", "second", "net-1");

        Assert.True(state.MarkModerationBusy("m2"));

        ServerChatMessageState remaining = Assert.Single(state.Messages);
        Assert.Equal("m1", remaining.ClientMessageId);
        Assert.Equal(ServerChatDeliveryState.Reviewing, remaining.Delivery);
        Assert.True(state.HasReviewPending);
        Assert.Equal(LanConnectChatModerationNotice.ModerationBusy, state.ModerationNotice);
        Assert.Equal(1, state.ModerationNoticeSequence);

        Assert.False(state.MarkModerationBusy("m2"));
        Assert.Equal(1, state.ModerationNoticeSequence);
    }

    [Fact]
    public void ReviewTimeoutDegradesToUnknownWithoutMarkingSuccess()
    {
        DateTimeOffset queuedAt = DateTimeOffset.Parse("2026-07-13T04:05:06.123Z");
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.BeginPendingText("m1", "Me", "hello", "net-1", queuedAt);
        state.MarkReviewPending("m1");

        state.MarkTimedOut(queuedAt + TimeSpan.FromSeconds(11));

        ServerChatMessageState message = Assert.Single(state.Messages);
        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, message.Delivery);
        Assert.False(state.HasReviewPending);
    }

    [Fact]
    public void DisconnectDuringReviewMarksUnknownAndDisconnectedNeverConfirmed()
    {
        DateTimeOffset queuedAt = DateTimeOffset.Parse("2026-07-13T04:05:06.123Z");
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.BeginPendingText("m1", "Me", "hello", "net-1", queuedAt);
        state.MarkReviewPending("m1");

        // Mirrors LanConnectServerChatClient.MarkDisconnected.
        state.MarkTimedOut(queuedAt + TimeSpan.FromSeconds(10));
        state.MarkDisconnected();

        ServerChatMessageState message = Assert.Single(state.Messages);
        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, message.Delivery);
        Assert.True(message.DisconnectedAfterUnknown);
        Assert.False(state.HasReviewPending);
    }

    [Fact]
    public void ContextChangeClearsReviewStateAndNotice()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        state.BeginPendingText("m1", "Me", "hello", "net-1");
        state.MarkReviewPending("m1");
        state.BeginPendingText("m2", "Me", "blocked", "net-1");
        state.MarkContentBlocked("m2");
        Assert.Equal(1, state.ModerationNoticeSequence);

        state.ClearForContextChange();

        Assert.Empty(state.Messages);
        Assert.False(state.HasReviewPending);
        Assert.Equal(LanConnectChatModerationNotice.None, state.ModerationNotice);
    }

    [Fact]
    public void RedactionRemovesOnlyConfirmedServerIdsAndRollsBackUnreadTracking()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.AppendConfirmedForTests("srv-1", "Alice", "习", 1, isLocal: false);
        state.AppendConfirmedForTests("srv-2", "Alice", "近", 2, isLocal: false);
        state.BeginPendingText("pending-1", "Me", "平", "net-1");

        state.RemoveConfirmedMessages(["srv-1", "missing"]);

        Assert.DoesNotContain(state.Messages, message => message.MessageId == "srv-1");
        Assert.Contains(state.Messages, message => message.MessageId == "srv-2");
        Assert.Contains(state.Messages, message => message.ClientMessageId == "pending-1");
        Assert.Equal(1, state.UnreadCount);
    }

    // ------------------------------------------------------------------
    // Server channel client (public server chat)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ServerChannelReviewThenAckConfirmsSingleMessage()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        FakeDelay delay = new();
        await using LanConnectServerChatClient client = CreateClient(api, transport, delay: delay, uuid: () => FixedGuid);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");
        Assert.Equal(ServerChatDeliveryState.Pending, Assert.Single(client.State.Messages).Delivery);

        transport.Emit(Serialize(new LanConnectChatReviewPendingEnvelope
        {
            ClientMessageId = FixedGuid.ToString("D"),
            ReviewId = "review-1",
            StartedAt = "2026-07-13T04:05:06.123Z",
            TimeoutMs = 5000
        }));

        Assert.Equal(ServerChatDeliveryState.Reviewing, Assert.Single(client.State.Messages).Delivery);
        Assert.True(client.State.HasReviewPending);

        transport.Emit(BuildCanonicalAck(FixedGuid.ToString("D"), "srv-1"));

        ServerChatMessageState message = Assert.Single(client.State.Messages);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
        Assert.Equal("hello", message.Text);
        Assert.False(client.State.HasReviewPending);
        Assert.True(delay.Tokens.Single().IsCancellationRequested);
        Assert.Equal(LanConnectChatModerationNotice.None, client.State.ModerationNotice);
    }

    [Fact]
    public async Task ServerChannelReviewThenContentBlockedDropsMessageAndCancelsTimeout()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        FakeDelay delay = new();
        await using LanConnectServerChatClient client = CreateClient(api, transport, delay: delay, uuid: () => FixedGuid);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");
        transport.Emit(Serialize(new LanConnectChatReviewPendingEnvelope
        {
            ClientMessageId = FixedGuid.ToString("D"),
            ReviewId = "review-1",
            StartedAt = "2026-07-13T04:05:06.123Z",
            TimeoutMs = 5000
        }));

        transport.Emit(BuildError(FixedGuid.ToString("D"), "content_blocked"));

        Assert.Empty(client.State.Messages);
        Assert.False(client.State.HasReviewPending);
        Assert.Equal(LanConnectChatModerationNotice.ContentBlocked, client.State.ModerationNotice);
        Assert.Equal(1, client.State.ModerationNoticeSequence);
        Assert.True(delay.Tokens.Single().IsCancellationRequested);

        // A replayed terminal frame stays a no-op (ACK/error vs local state race idempotency).
        transport.Emit(BuildError(FixedGuid.ToString("D"), "content_blocked"));
        Assert.Empty(client.State.Messages);
        Assert.Equal(1, client.State.ModerationNoticeSequence);
    }

    [Fact]
    public async Task ServerChannelModerationBusyKeepsOriginalReviewAndDropsSecondSend()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        Queue<Guid> ids = new([FixedGuid, SecondGuid]);
        await using LanConnectServerChatClient client = CreateClient(api, transport, uuid: ids.Dequeue);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("first");
        transport.Emit(Serialize(new LanConnectChatReviewPendingEnvelope
        {
            ClientMessageId = FixedGuid.ToString("D"),
            ReviewId = "review-1",
            StartedAt = "2026-07-13T04:05:06.123Z",
            TimeoutMs = 5000
        }));
        await client.SendTextAsync("second");
        Assert.Equal(2, client.State.Messages.Count);

        transport.Emit(BuildError(SecondGuid.ToString("D"), "moderation_busy"));

        ServerChatMessageState remaining = Assert.Single(client.State.Messages);
        Assert.Equal(FixedGuid.ToString("D"), remaining.ClientMessageId);
        Assert.Equal(ServerChatDeliveryState.Reviewing, remaining.Delivery);
        Assert.Equal(LanConnectChatModerationNotice.ModerationBusy, client.State.ModerationNotice);

        // The original review still completes normally afterwards.
        transport.Emit(BuildCanonicalAck(FixedGuid.ToString("D"), "srv-1"));
        Assert.Equal(ServerChatDeliveryState.Confirmed, Assert.Single(client.State.Messages).Delivery);
    }

    [Fact]
    public async Task ServerChannelSameIdResendReusesReviewState()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = CreateClient(api, transport, uuid: () => FixedGuid);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");
        transport.Emit(Serialize(new LanConnectChatReviewPendingEnvelope
        {
            ClientMessageId = FixedGuid.ToString("D"),
            ReviewId = "review-1",
            StartedAt = "2026-07-13T04:05:06.123Z",
            TimeoutMs = 5000
        }));
        Assert.True(client.State.HasReviewPending);

        // Same clientMessageId + same content: the server replays the pending frame.
        await client.RetryAsync(FixedGuid.ToString("D"));
        transport.Emit(Serialize(new LanConnectChatReviewPendingEnvelope
        {
            ClientMessageId = FixedGuid.ToString("D"),
            ReviewId = "review-1",
            StartedAt = "2026-07-13T04:05:06.123Z",
            TimeoutMs = 5000
        }));

        Assert.Equal(ServerChatDeliveryState.Reviewing, Assert.Single(client.State.Messages).Delivery);
        Assert.True(client.State.HasReviewPending);

        transport.Emit(BuildCanonicalAck(FixedGuid.ToString("D"), "srv-1"));
        Assert.Equal(ServerChatDeliveryState.Confirmed, Assert.Single(client.State.Messages).Delivery);
    }

    [Fact]
    public async Task ServerChannelSameIdDifferentContentStaysNormalFailure()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = CreateClient(api, transport, uuid: () => FixedGuid);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");

        transport.Emit(BuildError(FixedGuid.ToString("D"), "duplicate_message"));

        ServerChatMessageState message = Assert.Single(client.State.Messages);
        Assert.Equal(ServerChatDeliveryState.Failed, message.Delivery);
        Assert.Equal("duplicate_message", message.ErrorCode);
        Assert.Equal(LanConnectChatModerationNotice.None, client.State.ModerationNotice);
    }

    [Fact]
    public async Task ServerChannelUnknownFramesStayBackwardCompatible()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = CreateClient(api, transport);
        await ConnectReadyAsync(client, transport);

        // Unknown frame types and future-versioned review frames must never drop the connection.
        transport.Emit("""{"type":"chat_future_feature","protocolVersion":1,"extra":"ignored"}""");
        transport.Emit("""{"type":"chat_review_pending","protocolVersion":2,"clientMessageId":"x"}""");

        Assert.False(client.IsPermanentlyStopped);
        Assert.True(client.CanSend);
        Assert.Equal(LanConnectServerChatPresentation.Ready, client.State.Presentation);
        Assert.Empty(client.State.Messages);
    }

    [Fact]
    public async Task ServerChannelReviewTimeoutEndsIndicatorWithoutSuccess()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        MutableClock clock = new();
        FakeDelay delay = new();
        await using LanConnectServerChatClient client = CreateClient(api, transport, clock, delay, uuid: () => FixedGuid);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");
        transport.Emit(Serialize(new LanConnectChatReviewPendingEnvelope
        {
            ClientMessageId = FixedGuid.ToString("D"),
            ReviewId = "review-1",
            StartedAt = "2026-07-13T04:05:06.123Z",
            TimeoutMs = 5000
        }));
        Assert.True(client.State.HasReviewPending);

        TaskCompletionSource timedOut = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += () =>
        {
            if (client.State.Messages.Single().Delivery == ServerChatDeliveryState.DeliveryUnknown)
            {
                timedOut.TrySetResult();
            }
        };
        clock.Now += TimeSpan.FromSeconds(10);
        delay.CompleteNext();
        await timedOut.Task.WaitAsync(TestTimeout);

        ServerChatMessageState message = Assert.Single(client.State.Messages);
        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, message.Delivery);
        Assert.False(client.State.HasReviewPending);
    }

    [Fact]
    public async Task ServerChannelDisconnectDuringReviewNeverConfirmsMessage()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = CreateClient(api, transport, uuid: () => FixedGuid);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");
        transport.Emit(Serialize(new LanConnectChatReviewPendingEnvelope
        {
            ClientMessageId = FixedGuid.ToString("D"),
            ReviewId = "review-1",
            StartedAt = "2026-07-13T04:05:06.123Z",
            TimeoutMs = 5000
        }));

        transport.EmitClosed();

        ServerChatMessageState message = Assert.Single(client.State.Messages);
        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, message.Delivery);
        Assert.True(message.DisconnectedAfterUnknown);
        Assert.False(client.State.HasReviewPending);
        Assert.NotEqual(LanConnectServerChatPresentation.Ready, client.State.Presentation);
    }

    [Fact]
    public async Task ServerChannelRedactionFrameRemovesPreviouslyConfirmedMessages()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = CreateClient(api, transport);
        await ConnectReadyAsync(client, transport);
        client.State.AppendConfirmedForTests("srv-1", "Alice", "习", 1, isLocal: false);
        client.State.AppendConfirmedForTests("srv-2", "Alice", "近", 2, isLocal: false);

        transport.Emit(Serialize(new LanConnectChatMessagesRedactedEnvelope
        {
            MessageIds = ["srv-1", "srv-2"],
            Reason = "content_blocked",
            RedactedAt = "2026-07-13T04:05:06.123Z"
        }));

        Assert.Empty(client.State.Messages);
        Assert.False(client.IsPermanentlyStopped);
    }

    // ------------------------------------------------------------------
    // Room control channel
    // ------------------------------------------------------------------

    [Fact]
    public async Task RoomReviewPendingRequiresActiveReadyGeneration()
    {
        FakeWebSocket socket = new();
        await using LobbyControlClient client = new(socket);
        List<LanConnectRoomChatReviewPendingEnvelope> events = [];
        client.RoomChatReviewPendingReceived += events.Add;
        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            "session-1",
            CancellationToken.None);

        client.HandlePayloadForTests(
            """{"type":"room_chat_review_pending","protocolVersion":1,"clientMessageId":"m1","reviewId":"r1","startedAt":"2026-07-13T04:05:06.123Z","timeoutMs":5000}""");
        Assert.Empty(events);

        client.HandlePayloadForTests(
            """{"type":"room_chat_ready","protocolVersion":1,"roomId":"room-1","roomSessionId":"session-1","enabledFeatures":{"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":1}}""");
        client.HandlePayloadForTests(
            """{"type":"room_chat_review_pending","protocolVersion":1,"clientMessageId":"m1","reviewId":"r1","startedAt":"2026-07-13T04:05:06.123Z","timeoutMs":5000}""");

        LanConnectRoomChatReviewPendingEnvelope envelope = Assert.Single(events);
        Assert.Equal("m1", envelope.ClientMessageId);
        Assert.Equal(5000, envelope.TimeoutMs);
    }

    [Fact]
    public async Task RoomReviewPendingWithMismatchedVersionIsIgnored()
    {
        FakeWebSocket socket = new();
        await using LobbyControlClient client = new(socket);
        int events = 0;
        client.RoomChatReviewPendingReceived += _ => events++;
        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            "session-1",
            CancellationToken.None);
        client.HandlePayloadForTests(
            """{"type":"room_chat_ready","protocolVersion":1,"roomId":"room-1","roomSessionId":"session-1","enabledFeatures":{"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":1}}""");

        client.HandlePayloadForTests(
            """{"type":"room_chat_review_pending","protocolVersion":2,"clientMessageId":"m1","reviewId":"r1","startedAt":"2026-07-13T04:05:06.123Z","timeoutMs":5000}""");

        Assert.Equal(0, events);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task RoomRedactionRequiresAndCarriesTheActiveRoomGeneration()
    {
        FakeWebSocket socket = new();
        await using LobbyControlClient client = new(socket);
        List<LanConnectRoomChatMessagesRedactedEnvelope> events = [];
        client.RoomChatMessagesRedactedReceived += events.Add;
        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            "session-1",
            CancellationToken.None);
        client.HandlePayloadForTests(
            """{"type":"room_chat_ready","protocolVersion":1,"roomId":"room-1","roomSessionId":"session-1","enabledFeatures":{"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":1}}""");

        client.HandlePayloadForTests(
            """{"type":"room_chat_messages_redacted","protocolVersion":1,"roomId":"room-1","roomSessionId":"session-old","messageIds":["srv-1"],"reason":"content_blocked","redactedAt":"2026-07-13T04:05:06.123Z"}""");
        Assert.Empty(events);
        client.HandlePayloadForTests(
            """{"type":"room_chat_messages_redacted","protocolVersion":1,"roomId":"room-1","roomSessionId":"session-1","messageIds":["srv-1"],"reason":"content_blocked","redactedAt":"2026-07-13T04:05:06.123Z"}""");

        Assert.Equal("srv-1", Assert.Single(Assert.Single(events).MessageIds));
    }

    // ------------------------------------------------------------------
    // Runtime chat coordinator (room channel state + timeouts)
    // ------------------------------------------------------------------

    [Fact]
    public void RoomV2ReviewThenAckConfirmsAndCancelsTimeout()
    {
        FakeServerChatClient client = new();
        CapturingDelay delay = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client, delay: delay.Invoke);
        coordinator.EnterRoom("room-a");
        coordinator.BeginRoomPending("m1", "Me", "net-1", TextContent("hello"), DateTimeOffset.UtcNow);
        coordinator.ApplyRoomReviewPending(new LanConnectRoomChatReviewPendingEnvelope
        {
            ClientMessageId = "m1",
            ReviewId = "r1",
            StartedAt = "2026-07-13T04:05:06.123Z",
            TimeoutMs = 5000
        });
        Assert.True(coordinator.State.Room.HasReviewPending);

        coordinator.ApplyRoomAck(BuildRoomAck("m1", "srv-1"), "net-1");

        ServerChatMessageState message = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
        Assert.False(coordinator.State.Room.HasReviewPending);
        Assert.All(delay.Tokens, token => Assert.True(token.IsCancellationRequested));
    }

    [Fact]
    public void RoomV2ContentBlockedDropsMessageAndKeepsListClean()
    {
        FakeServerChatClient client = new();
        CapturingDelay delay = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client, delay: delay.Invoke);
        coordinator.EnterRoom("room-a");
        coordinator.BeginRoomPending("m1", "Me", "net-1", TextContent("hello"), DateTimeOffset.UtcNow);
        coordinator.ApplyRoomReviewPending(new LanConnectRoomChatReviewPendingEnvelope { ClientMessageId = "m1" });

        coordinator.ApplyRoomError(new LanConnectRoomChatErrorEnvelope
        {
            ClientMessageId = "m1",
            Code = "content_blocked",
            Message = "blocked"
        });

        Assert.Empty(coordinator.State.Room.Messages);
        Assert.Equal(LanConnectChatModerationNotice.ContentBlocked, coordinator.State.Room.ModerationNotice);
        Assert.All(delay.Tokens, token => Assert.True(token.IsCancellationRequested));
    }

    [Fact]
    public void RoomV2ModerationBusyKeepsReviewingMessage()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        coordinator.EnterRoom("room-a");
        coordinator.BeginRoomPending("m1", "Me", "net-1", TextContent("first"), DateTimeOffset.UtcNow);
        coordinator.ApplyRoomReviewPending(new LanConnectRoomChatReviewPendingEnvelope { ClientMessageId = "m1" });
        coordinator.BeginRoomPending("m2", "Me", "net-1", TextContent("second"), DateTimeOffset.UtcNow);

        coordinator.ApplyRoomError(new LanConnectRoomChatErrorEnvelope
        {
            ClientMessageId = "m2",
            Code = "moderation_busy",
            Message = "busy"
        });

        ServerChatMessageState remaining = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal("m1", remaining.ClientMessageId);
        Assert.Equal(ServerChatDeliveryState.Reviewing, remaining.Delivery);
        Assert.Equal(LanConnectChatModerationNotice.ModerationBusy, coordinator.State.Room.ModerationNotice);
    }

    [Fact]
    public void RoomSwitchClearsPendingReviewState()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        coordinator.EnterRoom("room-a");
        coordinator.BeginRoomPending("m1", "Me", "net-1", TextContent("hello"), DateTimeOffset.UtcNow);
        coordinator.ApplyRoomReviewPending(new LanConnectRoomChatReviewPendingEnvelope { ClientMessageId = "m1" });
        Assert.True(coordinator.State.Room.HasReviewPending);

        coordinator.EnterRoom("room-b");

        Assert.False(coordinator.State.Room.HasReviewPending);
        Assert.Empty(coordinator.State.Room.Messages);
        Assert.Equal(LanConnectChatModerationNotice.None, coordinator.State.Room.ModerationNotice);

        coordinator.LeaveRoom();
        Assert.False(coordinator.State.Room.HasReviewPending);
    }

    [Fact]
    public void LegacyRoomChatFlowIsUnchangedByReviewProtocol()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        coordinator.EnterRoom("room-a");
        coordinator.BeginRoomPending("legacy-1", "Me", "net-1", "hello", DateTimeOffset.UtcNow);

        coordinator.ConfirmRoomSend("legacy-1");

        ServerChatMessageState message = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
        Assert.False(coordinator.State.Room.HasReviewPending);
        Assert.Equal(LanConnectChatModerationNotice.None, coordinator.State.Room.ModerationNotice);

        // A stray terminal moderation frame for a legacy confirmed id is inert.
        coordinator.ApplyRoomError(new LanConnectRoomChatErrorEnvelope
        {
            ClientMessageId = "legacy-1",
            Code = "content_blocked",
            Message = "blocked"
        });
        Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(LanConnectChatModerationNotice.None, coordinator.State.Room.ModerationNotice);
    }

    [Fact]
    public void RuntimeReviewPendingHelperValidatesRoomGeneration()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        coordinator.EnterRoom("room-a");
        coordinator.BeginRoomPending("m1", "Me", "net-1", TextContent("hello"), DateTimeOffset.UtcNow);
        LanConnectRoomChatReviewPendingEnvelope review = new() { ClientMessageId = "m1" };

        // No ready: stale frame for an inactive generation.
        Assert.False(LanConnectLobbyRuntime.TryApplyRoomChatReviewPending(
            coordinator, review, null, "room-a", "session-1"));
        Assert.False(coordinator.State.Room.HasReviewPending);

        // Ready from a different generation.
        LanConnectRoomChatReadyEnvelope staleReady = new()
        {
            RoomId = "room-a",
            RoomSessionId = "session-old"
        };
        Assert.False(LanConnectLobbyRuntime.TryApplyRoomChatReviewPending(
            coordinator, review, staleReady, "room-a", "session-1"));
        Assert.False(coordinator.State.Room.HasReviewPending);

        LanConnectRoomChatReadyEnvelope ready = new()
        {
            RoomId = "room-a",
            RoomSessionId = "session-1"
        };
        Assert.True(LanConnectLobbyRuntime.TryApplyRoomChatReviewPending(
            coordinator, review, ready, "room-a", "session-1"));
        Assert.True(coordinator.State.Room.HasReviewPending);
    }

    [Fact]
    public void RuntimeRedactionHelperValidatesGenerationAndRemovesRoomMessages()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        coordinator.EnterRoom("room-a");
        coordinator.State.Room.AppendConfirmedForTests("srv-1", "Alice", "习", 1, isLocal: false);
        LanConnectRoomChatMessagesRedactedEnvelope redaction = new()
        {
            RoomId = "room-a",
            RoomSessionId = "session-1",
            MessageIds = ["srv-1"],
            Reason = "content_blocked"
        };

        Assert.False(LanConnectLobbyRuntime.TryApplyRoomChatMessagesRedacted(
            coordinator, redaction, "room-a", "session-old"));
        Assert.Single(coordinator.State.Room.Messages);
        Assert.True(LanConnectLobbyRuntime.TryApplyRoomChatMessagesRedacted(
            coordinator, redaction, "room-a", "session-1"));
        Assert.Empty(coordinator.State.Room.Messages);
    }

    // ------------------------------------------------------------------
    // Builders and fakes
    // ------------------------------------------------------------------

    private static ServerChatMessageState FindMessage(LanConnectChatChannelState state, string clientMessageId) =>
        state.Messages.Single(message =>
            string.Equals(message.ClientMessageId, clientMessageId, StringComparison.Ordinal));

    private static LanConnectChatContent TextContent(string text) =>
        new(1, [new LanConnectTextSegment(text)]);

    private static LanConnectServerChatAckEnvelope BuildCanonicalAckEnvelope(string clientMessageId, string messageId) =>
        new()
        {
            ClientMessageId = clientMessageId,
            Message = new LanConnectServerChatMessagePayload
            {
                MessageId = messageId,
                SenderId = "net-1",
                SenderName = "Me",
                Content = TextContent("hello"),
                PlainTextFallback = "hello",
                SentAt = "2026-07-13T04:05:06.123Z"
            }
        };

    private static LanConnectRoomChatAckEnvelope BuildRoomAck(string clientMessageId, string messageId) =>
        new()
        {
            ClientMessageId = clientMessageId,
            Message = new LanConnectRoomChatMessagePayload
            {
                RoomId = "room-a",
                RoomSessionId = "session-1",
                MessageId = messageId,
                SenderId = "net-1",
                SenderName = "Me",
                Content = TextContent("hello"),
                PlainTextFallback = "hello",
                SentAt = "2026-07-13T04:05:06.123Z"
            }
        };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, LanConnectJson.Options);

    private static string BuildCanonicalAck(string clientMessageId, string messageId) =>
        Serialize(BuildCanonicalAckEnvelope(clientMessageId, messageId));

    private static string BuildError(string clientMessageId, string code) =>
        Serialize(new LanConnectServerChatErrorEnvelope
        {
            ClientMessageId = clientMessageId,
            Code = code,
            Message = "error: " + code
        });

    private static LanConnectServerChatClient CreateClient(
        FakeApi api,
        FakeTransport transport,
        MutableClock? clock = null,
        FakeDelay? delay = null,
        Func<Guid>? uuid = null) =>
        new(
            _ => api,
            () => transport,
            clock is null ? null : () => clock.Now,
            delay is null ? null : delay.DelayAsync,
            uuidFactory: uuid);

    private static async Task ConnectReadyAsync(LanConnectServerChatClient client, FakeTransport transport)
    {
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        transport.Emit(Serialize(new ServerChatReadyEnvelope
        {
            ProtocolVersion = 1,
            Channel = LanConnectChatChannel.Server,
            InstanceId = "instance-1",
            HistoryEpoch = 1,
            ChatEnabled = true,
            ServerChatVersion = 1
        }));
        transport.Emit(Serialize(new ServerChatSnapshotBeginEnvelope
        {
            ProtocolVersion = 1,
            SnapshotId = "snapshot-1",
            InstanceId = "instance-1",
            HistoryEpoch = 1,
            TotalMessages = 0
        }));
        transport.Emit(Serialize(new ServerChatSnapshotEndEnvelope
        {
            ProtocolVersion = 1,
            SnapshotId = "snapshot-1",
            HistoryEpoch = 1
        }));
    }

    private sealed class FakeApi : ILanConnectServerChatApi
    {
        public int DisposeCalls { get; private set; }

        public Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new LobbyProbeResponse
            {
                Ok = true,
                Capabilities = new LobbyProbeCapabilities { ServerChatVersion = 1 }
            });

        public Task<ServerChatTicketResponse> CreateServerChatTicketAsync(
            ServerChatTicketRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ServerChatTicketResponse
            {
                Ticket = "one-time-secret",
                WebSocketUrl = ChatUri.AbsoluteUri,
                ProtocolVersion = 1
            });

        public void Dispose() => DisposeCalls++;
    }

    private sealed class FakeTransport : ILanConnectServerChatTransport
    {
        public event Action<string>? PayloadReceived;
        public event Action<Exception>? Faulted
        {
            add { }
            remove { }
        }
        public event Action? Closed;

        public Task ConnectAsync(
            Uri uri,
            IReadOnlyDictionary<string, string>? requestHeaders,
            CancellationToken connectCancellationToken,
            CancellationToken receiveLifetimeCancellationToken) =>
            Task.CompletedTask;

        public Task SendAsync(string payload, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Emit(string payload) => PayloadReceived?.Invoke(payload);

        public void EmitClosed() => Closed?.Invoke();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MutableClock
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-07-13T04:05:06.123Z");
    }

    private sealed class FakeDelay
    {
        private readonly Queue<TaskCompletionSource> _pending = new();
        public List<CancellationToken> Tokens { get; } = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _pending.Enqueue(completion);
            Tokens.Add(cancellationToken);
            return completion.Task;
        }

        public void CompleteNext()
        {
            while (_pending.Count > 0 && _pending.Peek().Task.IsCompleted)
            {
                _pending.Dequeue();
            }
            _pending.Dequeue().TrySetResult();
        }
    }

    private sealed class CapturingDelay
    {
        public List<CancellationToken> Tokens { get; } = [];

        public Task Invoke(TimeSpan duration, CancellationToken cancellationToken)
        {
            Tokens.Add(cancellationToken);
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }
    }

    private sealed class FakeServerChatClient : ILanConnectServerChatClient
    {
        internal FakeServerChatClient()
        {
            State = new LanConnectChatChannelState(LanConnectChatChannel.Server);
        }

        public LanConnectChatChannelState State { get; }

        public event Action? StateChanged;

        public Task ConnectAsync(Uri lobbyBaseUri, string playerNetId, string playerName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendAsync(LanConnectChatContent content, string clientMessageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RetryAsync(string clientMessageId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal void RaiseStateChanged() => StateChanged?.Invoke();
    }

    private sealed class FakeWebSocket : ILanConnectWebSocket
    {
        private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>();

        public WebSocketState State { get; private set; } = WebSocketState.None;

        public List<string> SentPayloads { get; } = [];

        public void SetRequestHeader(string headerName, string headerValue)
        {
        }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            State = WebSocketState.Open;
            return Task.CompletedTask;
        }

        public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            Frame frame = await _frames.Reader.ReadAsync(cancellationToken);
            frame.Payload.CopyTo(buffer);
            if (frame.MessageType == WebSocketMessageType.Close)
            {
                State = WebSocketState.CloseReceived;
            }
            return new ValueWebSocketReceiveResult(frame.Payload.Length, frame.MessageType, frame.EndOfMessage);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            SentPayloads.Add(Encoding.UTF8.GetString(buffer.Span));
            return ValueTask.CompletedTask;
        }

        public Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken)
        {
            State = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public void Abort()
        {
            State = WebSocketState.Aborted;
            _frames.Writer.TryComplete(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
        }

        public ValueTask DisposeAsync()
        {
            State = WebSocketState.Closed;
            return ValueTask.CompletedTask;
        }

        public void QueueText(string payload, bool endOfMessage = true) =>
            _frames.Writer.TryWrite(new Frame(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, endOfMessage));
    }

    private sealed record Frame(byte[] Payload, WebSocketMessageType MessageType, bool EndOfMessage);
}
