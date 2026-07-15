using System.Text.Json;
using System.Net.WebSockets;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectRichChannelSubmissionTests
{
    private static readonly Uri BaseUri = new("https://lobby.example/");

    [Fact]
    public async Task Server_sends_exact_ordered_canonical_content_and_retry_reuses_it()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = new(
            _ => api,
            () => transport,
            uuidFactory: () => Guid.Parse("11111111-1111-4111-8111-111111111111"));
        await ConnectReadyAsync(client, transport);
        LanConnectChatContent content = RichContent();

        await client.SendAsync(content, "client-rich-1");

        LanConnectServerChatSendEnvelope sent = JsonSerializer.Deserialize<LanConnectServerChatSendEnvelope>(
            Assert.Single(transport.SentPayloads), LanConnectJson.Options)!;
        Assert.Equal("chat_send", sent.Type);
        Assert.Equal("client-rich-1", sent.ClientMessageId);
        AssertContentEqual(content, sent.Content);
        ServerChatMessageState pending = Assert.Single(client.State.Messages);
        AssertContentEqual(content, pending.Content);
        Assert.Equal(ServerChatDeliveryState.Pending, pending.Delivery);

        transport.Emit(JsonSerializer.Serialize(new LanConnectServerChatErrorEnvelope
        {
            ClientMessageId = "client-rich-1",
            Code = "rate_limited",
            Message = "retry"
        }, LanConnectJson.Options));
        await client.RetryAsync("client-rich-1");

        LanConnectServerChatSendEnvelope retried = JsonSerializer.Deserialize<LanConnectServerChatSendEnvelope>(
            transport.SentPayloads[1], LanConnectJson.Options)!;
        AssertContentEqual(content, retried.Content);
    }

    [Fact]
    public async Task Server_rejects_reserved_combat_segments_without_queueing_or_sending()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = new(_ => api, () => transport);
        await ConnectReadyAsync(client, transport);
        LanConnectChatContent content = new(1,
        [
            new LanConnectTextSegment("blocked"),
            new LanConnectPowerStateSegment("MegaCrit.Strength", 1, "room-session-1")
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendAsync(content, "client-combat"));

        Assert.Empty(client.State.Messages);
        Assert.Empty(transport.SentPayloads);
    }

    [Fact]
    public async Task Invalid_rich_inbound_isolated_and_next_valid_message_still_applies()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = new(_ => api, () => transport);
        await ConnectReadyAsync(client, transport);

        transport.Emit("""{"type":"chat_message","protocolVersion":1,"message":{"messageId":"bad","senderId":"net-2","senderName":"Silent","content":{"formatVersion":1,"segments":[{"kind":"future_kind"}]},"plainTextFallback":"bad","sentAt":"2026-07-12T12:00:00Z"}}""");
        transport.Emit(JsonSerializer.Serialize(new LanConnectServerChatMessageEnvelope
        {
            Message = new LanConnectServerChatMessagePayload
            {
                MessageId = "good",
                SenderId = "net-2",
                SenderName = "Silent",
                Content = new LanConnectChatContent(1, [new LanConnectTextSegment("still works")]),
                PlainTextFallback = "still works",
                SentAt = "2026-07-12T12:00:01Z"
            }
        }, LanConnectJson.Options));

        ServerChatMessageState message = Assert.Single(client.State.Messages);
        Assert.Equal("good", message.MessageId);
        Assert.Equal("still works", message.Text);
        Assert.True(client.CanSend);
    }

    [Fact]
    public async Task Invalid_server_ack_does_not_cancel_delivery_timeout()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        RoomDelay delay = new();
        await using LanConnectServerChatClient client = new(
            _ => api,
            () => transport,
            delay: delay.DelayAsync);
        await ConnectReadyAsync(
            client,
            transport,
            new LanConnectChatFeatureVersions(1, 1, 0, 0));
        LanConnectChatContent emoji = new(1, [new LanConnectEmojiSegment("heart")]);
        await client.SendAsync(emoji, "server-client-invalid-ack");
        CancellationToken deliveryTimeout = Assert.Single(delay.Tokens);

        transport.Emit(JsonSerializer.Serialize(new LanConnectServerChatAckEnvelope
        {
            ClientMessageId = "server-client-invalid-ack",
            Message = ServerMessage(
                "server-invalid-ack",
                new LanConnectChatContent(1,
                    [new LanConnectItemRefSegment("relic", "MegaCrit.Anchor")]))
        }, LanConnectJson.Options));

        Assert.Equal(ServerChatDeliveryState.Pending, Assert.Single(client.State.Messages).Delivery);
        Assert.False(deliveryTimeout.IsCancellationRequested);

        transport.Emit(JsonSerializer.Serialize(new LanConnectServerChatAckEnvelope
        {
            ClientMessageId = "server-client-invalid-ack",
            Message = ServerMessage("server-valid-ack", emoji)
        }, LanConnectJson.Options));
        Assert.Equal(ServerChatDeliveryState.Confirmed, Assert.Single(client.State.Messages).Delivery);
        Assert.True(deliveryTimeout.IsCancellationRequested);
    }

    [Fact]
    public async Task Rich_snapshot_chunk_preserves_ordered_content_and_server_payload_metadata()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = new(_ => api, () => transport);
        await ConnectReadyAsync(client, transport);
        transport.Emit("""{"type":"chat_snapshot_begin","protocolVersion":1,"snapshotId":"rich-snapshot","instanceId":"instance-2","historyEpoch":2,"totalMessages":1}""");
        transport.Emit("""{"type":"chat_snapshot_chunk","protocolVersion":1,"snapshotId":"rich-snapshot","chunkIndex":0,"messages":[{"messageId":"snapshot-rich-1","senderId":"net-2","senderName":"Silent","content":{"formatVersion":1,"segments":[{"kind":"text","text":"before "},{"kind":"emoji","emojiId":"heart"},{"kind":"item_ref","itemType":"relic","modelId":"MegaCrit.Anchor"}]},"plainTextFallback":"before [Emoji][Relic]","sentAt":"2026-07-12T12:00:00Z"}]}""");
        transport.Emit("""{"type":"chat_snapshot_end","protocolVersion":1,"snapshotId":"rich-snapshot","historyEpoch":2}""");

        ServerChatMessageState message = Assert.Single(client.State.Messages);
        Assert.Equal("snapshot-rich-1", message.MessageId);
        Assert.Collection(
            message.Content.Segments,
            segment => Assert.Equal(new LanConnectTextSegment("before "), segment),
            segment => Assert.Equal(new LanConnectEmojiSegment("heart"), segment),
            segment => Assert.Equal(new LanConnectItemRefSegment("relic", "MegaCrit.Anchor"), segment));
        Assert.NotNull(message.ServerPayload);
        Assert.Equal("before [Emoji][Relic]", message.ServerPayload!.PlainTextFallback);
    }

    [Fact]
    public async Task Unknown_snapshot_segment_rejects_only_active_snapshot_and_requires_reconnect()
    {
        FakeApi api = new();
        FakeTransport transport = new();
        await using LanConnectServerChatClient client = new(_ => api, () => transport);
        await ConnectReadyAsync(client, transport);
        transport.Emit(JsonSerializer.Serialize(new LanConnectServerChatMessageEnvelope
        {
            Message = ServerMessage(
                "history-before-corruption",
                new LanConnectChatContent(1, [new LanConnectTextSegment("retained")]))
        }, LanConnectJson.Options));

        transport.Emit("""{"type":"chat_snapshot_begin","protocolVersion":1,"snapshotId":"corrupt","instanceId":"instance-1","historyEpoch":1,"totalMessages":1}""");
        transport.Emit("""{"type":"chat_snapshot_chunk","protocolVersion":1,"snapshotId":"corrupt","chunkIndex":0,"messages":[{"messageId":"bad","senderId":"net-2","senderName":"Silent","content":{"formatVersion":1,"segments":[{"kind":"future_kind"}]},"plainTextFallback":"bad","sentAt":"2026-07-12T12:00:00Z"}]}""");

        ServerChatMessageState retained = Assert.Single(client.State.Messages);
        Assert.Equal("history-before-corruption", retained.MessageId);
        Assert.Equal("retained", retained.Text);
        Assert.False(client.CanSend);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("room")]
    public void Negotiated_item_zero_rejects_item_inbound_without_mutation_but_accepts_emoji(string channelName)
    {
        LanConnectChatChannel channel = channelName == "server"
            ? LanConnectChatChannel.Server
            : LanConnectChatChannel.Room;
        LanConnectChatChannelState state = new(channel);
        state.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 0, 0));
        long revision = state.Revision;
        LanConnectChatContent itemContent = new(1,
            [new LanConnectItemRefSegment("relic", "MegaCrit.Anchor")]);
        if (channel == LanConnectChatChannel.Server)
        {
            state.Apply(new LanConnectServerChatMessageEnvelope
            {
                Message = ServerMessage("item-rejected", itemContent)
            }, "local");
        }
        else
        {
            state.Apply(new LanConnectRoomChatMessageEnvelope
            {
                Message = RoomMessage("item-rejected", itemContent)
            }, "local");
        }

        Assert.Equal(revision, state.Revision);
        Assert.Empty(state.Messages);
        LanConnectChatContent emojiContent = new(1, [new LanConnectEmojiSegment("heart")]);
        if (channel == LanConnectChatChannel.Server)
        {
            state.Apply(new LanConnectServerChatMessageEnvelope
            {
                Message = ServerMessage("emoji-accepted", emojiContent)
            }, "local");
        }
        else
        {
            state.Apply(new LanConnectRoomChatMessageEnvelope
            {
                Message = RoomMessage("emoji-accepted", emojiContent)
            }, "local");
        }
        Assert.Equal("emoji-accepted", Assert.Single(state.Messages).MessageId);
    }

    [Fact]
    public async Task Room_v2_timeout_retry_and_disconnect_preserve_session_scoped_content()
    {
        CoordinatorClient server = new();
        MutableRoomClock clock = new();
        RoomDelay delay = new();
        await using LanConnectLobbyRuntimeChatCoordinator coordinator = new(
            server,
            clock: () => clock.Now,
            delay: delay.DelayAsync);
        coordinator.EnterRoom("room-1");
        coordinator.SetRoomRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 0));
        LanConnectChatContent content = RichContent();
        coordinator.BeginRoomPending(
            "room-client-1",
            "Ironclad",
            "net-1",
            content,
            clock.Now);

        Assert.Equal([TimeSpan.FromSeconds(10)], delay.Durations);
        clock.Now += TimeSpan.FromSeconds(10);
        delay.CompleteNext();
        await WaitUntilAsync(() => coordinator.State.Room.Messages.Single().Delivery ==
            ServerChatDeliveryState.DeliveryUnknown);

        List<(string Id, LanConnectChatContent Content)> retries = [];
        await coordinator.RetryRoomAsync(
            "room-client-1",
            (retryContent, retryId, _) =>
            {
                retries.Add((retryId, retryContent));
                return Task.CompletedTask;
            });
        Assert.Equal("room-client-1", Assert.Single(retries).Id);
        AssertContentEqual(content, retries[0].Content);
        Assert.Equal(ServerChatDeliveryState.Pending, coordinator.State.Room.Messages.Single().Delivery);

        coordinator.MarkRoomDisconnected();
        ServerChatMessageState disconnected = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, disconnected.Delivery);
        Assert.True(disconnected.DisconnectedAfterUnknown);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RetryRoomAsync(
            "room-client-1",
            (_, _, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task Room_disconnect_during_retry_failure_preserves_delivery_unknown_state()
    {
        CoordinatorClient server = new();
        MutableRoomClock clock = new();
        RoomDelay delay = new();
        await using LanConnectLobbyRuntimeChatCoordinator coordinator = new(
            server,
            clock: () => clock.Now,
            delay: delay.DelayAsync);
        coordinator.EnterRoom("room-1");
        coordinator.BeginRoomPending(
            "disconnect-during-retry",
            "Ironclad",
            "net-1",
            RichContent(),
            clock.Now);
        clock.Now += TimeSpan.FromSeconds(10);
        delay.CompleteNext();
        await WaitUntilAsync(() => coordinator.State.Room.Messages.Single().Delivery ==
            ServerChatDeliveryState.DeliveryUnknown);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RetryRoomAsync(
            "disconnect-during-retry",
            (_, _, _) =>
            {
                coordinator.MarkRoomDisconnected();
                throw new InvalidOperationException("transport closed");
            }));

        ServerChatMessageState message = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, message.Delivery);
        Assert.True(message.DisconnectedAfterUnknown);
    }

    [Fact]
    public async Task Room_timeout_registry_cancels_only_on_terminal_evidence_leave_and_dispose()
    {
        CoordinatorClient server = new();
        MutableRoomClock clock = new();
        RoomDelay delay = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(
            server,
            clock: () => clock.Now,
            delay: delay.DelayAsync);
        coordinator.EnterRoom("room-1");
        coordinator.SetRoomRichFeatures(new LanConnectChatFeatureVersions(1, 1, 0, 0));
        LanConnectChatContent emoji = new(1, [new LanConnectEmojiSegment("heart")]);

        coordinator.BeginRoomPending("room-ack", "Ironclad", "net-1", emoji, clock.Now);
        CancellationToken ackTimeout = delay.Tokens[^1];
        coordinator.ApplyRoomAck(new LanConnectRoomChatAckEnvelope
        {
            ClientMessageId = "room-ack",
            Message = RoomMessage(
                "invalid-room-ack",
                new LanConnectChatContent(1,
                    [new LanConnectItemRefSegment("relic", "MegaCrit.Anchor")]))
        }, "net-1");
        Assert.Equal(ServerChatDeliveryState.Pending, coordinator.State.Room.Messages.Single().Delivery);
        Assert.False(ackTimeout.IsCancellationRequested);
        coordinator.ApplyRoomAck(new LanConnectRoomChatAckEnvelope
        {
            ClientMessageId = "room-ack",
            Message = RoomMessage("valid-room-ack", emoji)
        }, "net-1");
        Assert.True(ackTimeout.IsCancellationRequested);

        coordinator.BeginRoomPending("room-error", "Ironclad", "net-1", emoji, clock.Now);
        CancellationToken errorTimeout = delay.Tokens[^1];
        coordinator.ApplyRoomError(new LanConnectRoomChatErrorEnvelope
        {
            ClientMessageId = "room-error",
            Code = "invalid_content",
            Message = "rejected"
        });
        Assert.True(errorTimeout.IsCancellationRequested);

        coordinator.BeginRoomPending("room-leave", "Ironclad", "net-1", emoji, clock.Now);
        CancellationToken leaveTimeout = delay.Tokens[^1];
        coordinator.LeaveRoom();
        Assert.True(leaveTimeout.IsCancellationRequested);

        coordinator.EnterRoom("room-2");
        coordinator.BeginRoomPending("room-dispose", "Ironclad", "net-1", emoji, clock.Now);
        CancellationToken disposeTimeout = delay.Tokens[^1];
        await coordinator.DisposeAsync();
        Assert.True(disposeTimeout.IsCancellationRequested);
    }

    [Fact]
    public async Task Runtime_room_retry_reuses_exact_session_id_and_content_and_native_close_marks_unknown()
    {
        CoordinatorClient server = new();
        MutableRoomClock clock = new();
        RoomDelay delay = new();
        await using LanConnectLobbyRuntimeChatCoordinator coordinator = new(
            server,
            clock: () => clock.Now,
            delay: delay.DelayAsync);
        coordinator.EnterRoom("room-1");
        LanConnectChatContent content = RichContent();
        coordinator.BeginRoomPending("runtime-retry", "Ironclad", "net-1", content, clock.Now);
        clock.Now += TimeSpan.FromSeconds(10);
        delay.CompleteNext();
        await WaitUntilAsync(() => coordinator.State.Room.Messages.Single().Delivery ==
            ServerChatDeliveryState.DeliveryUnknown);

        StubWebSocket socket = new();
        await using LobbyControlClient control = new(socket);
        await control.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            "session-1",
            CancellationToken.None);
        control.HandlePayloadForTests("""{"type":"room_chat_ready","protocolVersion":1,"roomId":"room-1","roomSessionId":"session-1","enabledFeatures":{"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":0}}""");
        LanConnectLobbyRuntime.BindRoomChatDisconnect(control, coordinator, () => true);

        await LanConnectLobbyRuntime.RetryRoomChatAsync(
            coordinator,
            control,
            "room-1",
            "session-1",
            "runtime-retry");

        LanConnectRoomChatV2Envelope retry = JsonSerializer.Deserialize<LanConnectRoomChatV2Envelope>(
            socket.SentPayloads[^1],
            LanConnectJson.Options)!;
        Assert.Equal("runtime-retry", retry.ClientMessageId);
        Assert.Equal("room-1", retry.RoomId);
        Assert.Equal("session-1", retry.RoomSessionId);
        AssertContentEqual(content, retry.Content);

        control.HandleTransportClosedForTests();
        ServerChatMessageState disconnected = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, disconnected.Delivery);
        Assert.True(disconnected.DisconnectedAfterUnknown);
        Assert.True(delay.Tokens[^1].IsCancellationRequested);
    }

    [Fact]
    public async Task Room_self_broadcast_does_not_cancel_pending_timeout_without_ack()
    {
        CoordinatorClient server = new();
        MutableRoomClock clock = new();
        RoomDelay delay = new();
        await using LanConnectLobbyRuntimeChatCoordinator coordinator = new(
            server,
            clock: () => clock.Now,
            delay: delay.DelayAsync);
        coordinator.EnterRoom("room-1");
        LanConnectChatContent content = RichContent();
        coordinator.BeginRoomPending("self-broadcast-client", "Ironclad", "net-1", content, clock.Now);
        CancellationToken timeout = Assert.Single(delay.Tokens);

        coordinator.ApplyRoomMessage(new LanConnectRoomChatMessageEnvelope
        {
            Message = RoomMessage("self-broadcast-server", content) with
            {
                SenderId = "net-1"
            }
        }, "net-1");

        Assert.False(timeout.IsCancellationRequested);
        Assert.Contains(coordinator.State.Room.Messages, message =>
            message.ClientMessageId == "self-broadcast-client" &&
            message.Delivery == ServerChatDeliveryState.Pending);
    }

    [Fact]
    public void Production_resolver_context_tracks_locale_and_stabilizes_loaded_mod_fingerprint()
    {
        string locale = "en-US";
        List<string> mods = ["Z Mod", "A Mod", "A Mod"];
        LanConnectProductionItemResolverContextProvider provider = new(
            () => locale,
            () => mods);
        LanConnectItemResolverContext first = provider.Current;

        locale = "zh-CN";
        mods.Add("Late Mod");
        LanConnectItemResolverContext second = provider.Current;

        Assert.Equal("en-US", first.Locale);
        Assert.Equal("zh-CN", second.Locale);
        Assert.Equal(first.ModFingerprint, second.ModFingerprint);
        Assert.NotEmpty(first.ModFingerprint);
    }

    [Fact]
    public void Canonical_ack_and_self_broadcast_merge_one_structured_message()
    {
        LanConnectChatChannelState state = EnabledState(LanConnectChatChannel.Server);
        LanConnectChatContent content = RichContent();
        state.Queue(new ServerChatPendingMessage
        {
            ClientMessageId = "client-1",
            SenderName = "Ironclad",
            SenderNetId = "net-1",
            Content = content
        });
        LanConnectServerChatMessagePayload message = ServerMessage("server-1", content);

        state.Apply(new LanConnectServerChatMessageEnvelope { Message = message }, "net-1");
        state.Apply(new LanConnectServerChatAckEnvelope
        {
            ClientMessageId = "client-1",
            Message = message
        }, "net-1");

        ServerChatMessageState merged = Assert.Single(state.Messages);
        Assert.Equal("server-1", merged.MessageId);
        Assert.Equal("client-1", merged.ClientMessageId);
        AssertContentEqual(content, merged.Content);
        Assert.True(merged.IsLocal);
        Assert.Equal(ServerChatDeliveryState.Confirmed, merged.Delivery);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    public void Room_ready_feature_versions_allow_emoji_but_gate_item_refs(
        int itemVersion,
        bool containsItem,
        bool expectedEnabled)
    {
        LanConnectRoomChatReadyEnvelope ready = new()
        {
            RoomId = "room-1",
            RoomSessionId = "session-1",
            EnabledFeatures = new LanConnectChatFeatureVersions(1, 1, itemVersion, 0)
        };
        LanConnectChatContent content = containsItem
            ? new LanConnectChatContent(1, [new LanConnectItemRefSegment("relic", "MegaCrit.Anchor")])
            : new LanConnectChatContent(1, [new LanConnectTextSegment("hi "), new LanConnectEmojiSegment("heart")]);

        LanConnectRoomChatSendDecision decision = LanConnectLobbyRuntime.DecideRoomChatSend(
            content,
            ready,
            "room-1",
            "session-1");

        Assert.Equal(expectedEnabled, decision.Enabled);
        Assert.Equal(expectedEnabled, decision.UseV2);
    }

    [Fact]
    public void Before_room_ready_only_pure_text_uses_legacy_and_entities_remain_disabled()
    {
        LanConnectRoomChatSendDecision text = LanConnectLobbyRuntime.DecideRoomChatSend(
            new LanConnectChatContent(1, [new LanConnectTextSegment("legacy")]),
            ready: null,
            "room-1",
            "session-1");
        LanConnectRoomChatSendDecision entity = LanConnectLobbyRuntime.DecideRoomChatSend(
            new LanConnectChatContent(1, [new LanConnectEmojiSegment("heart")]),
            ready: null,
            "room-1",
            "session-1");

        Assert.True(text.Enabled);
        Assert.False(text.UseV2);
        Assert.False(entity.Enabled);
        Assert.Contains("ready", entity.DisabledReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Control_ready_is_room_scoped_updates_features_and_emits_typed_room_events()
    {
        StubWebSocket socket = new();
        await using LobbyControlClient client = new(socket);
        LanConnectChatChannelState room = new(LanConnectChatChannel.Room);
        int readyEvents = 0;
        int ackEvents = 0;
        int messageEvents = 0;
        int errorEvents = 0;
        client.RoomChatReadyReceived += ready =>
        {
            readyEvents++;
            room.SetEnabledRichFeatures(ready.EnabledFeatures);
        };
        client.RoomChatAckReceived += _ => ackEvents++;
        client.RoomChatMessageReceived += _ => messageEvents++;
        client.RoomChatErrorReceived += _ => errorEvents++;
        await client.ConnectHostAsync(
            new Uri("wss://lobby.example/control"),
            "room-1",
            "control-1",
            "Host",
            "session-1",
            CancellationToken.None);

        client.HandlePayloadForTests("""{"type":"room_chat_ready","protocolVersion":1,"roomId":"other","roomSessionId":"session-1","enabledFeatures":{"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":0}}""");
        client.HandlePayloadForTests("""{"type":"room_chat_ready","protocolVersion":1,"roomId":"room-1","roomSessionId":"session-1","enabledFeatures":{"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":0,"combatRefVersion":0}}""");

        Assert.Equal(1, readyEvents);
        Assert.Equal(1, room.EnabledRichFeatures.EmojiSetVersion);
        Assert.Equal(0, room.EnabledRichFeatures.ItemRefVersion);
        LanConnectRoomChatMessagePayload payload = new()
        {
            RoomId = "room-1",
            RoomSessionId = "session-1",
            MessageId = "server-1",
            SenderId = "net-1",
            SenderName = "Host",
            Content = new LanConnectChatContent(1, [new LanConnectEmojiSegment("heart")]),
            PlainTextFallback = "[Emoji]",
            SentAt = "2026-07-12T12:00:00Z"
        };
        client.HandlePayloadForTests(JsonSerializer.Serialize(new LanConnectRoomChatAckEnvelope
        {
            ClientMessageId = "client-1",
            Message = payload
        }, LanConnectJson.Options));
        client.HandlePayloadForTests(JsonSerializer.Serialize(
            new LanConnectRoomChatMessageEnvelope { Message = payload },
            LanConnectJson.Options));
        client.HandlePayloadForTests(JsonSerializer.Serialize(new LanConnectRoomChatErrorEnvelope
        {
            ClientMessageId = "client-2",
            Code = "invalid_content",
            Message = "rejected"
        }, LanConnectJson.Options));

        Assert.Equal(1, ackEvents);
        Assert.Equal(1, messageEvents);
        Assert.Equal(1, errorEvents);
        await client.SendRoomChatV2Async(new LanConnectRoomChatV2Envelope
        {
            ClientMessageId = "client-3",
            RoomId = "room-1",
            RoomSessionId = "session-1",
            Content = new LanConnectChatContent(1, [new LanConnectEmojiSegment("heart")])
        });
        LanConnectRoomChatV2Envelope sent = JsonSerializer.Deserialize<LanConnectRoomChatV2Envelope>(
            socket.SentPayloads[^1], LanConnectJson.Options)!;
        Assert.Equal("room_chat_v2", sent.Type);

        room.ClearForContextChange();
        Assert.Equal(new LanConnectChatFeatureVersions(), room.EnabledRichFeatures);
    }

    private static LanConnectChatContent RichContent() => new(1,
    [
        new LanConnectTextSegment("before "),
        new LanConnectEmojiSegment("heart"),
        new LanConnectItemRefSegment("card", "MegaCrit.Strike", 2),
        new LanConnectTextSegment(" after")
    ]);

    private static void AssertContentEqual(LanConnectChatContent expected, LanConnectChatContent actual) =>
        Assert.Equal(
            LanConnectServerChatProtocol.DeterministicContentJson(expected),
            LanConnectServerChatProtocol.DeterministicContentJson(actual));

    private static LanConnectServerChatMessagePayload ServerMessage(
        string messageId,
        LanConnectChatContent content) => new()
    {
        MessageId = messageId,
        SenderId = "net-1",
        SenderName = "Ironclad",
        Content = content,
        PlainTextFallback = "before [Emoji][Card] after",
        SentAt = "2026-07-12T12:00:00Z"
    };

    private static LanConnectRoomChatMessagePayload RoomMessage(
        string messageId,
        LanConnectChatContent content) => new()
    {
        RoomId = "room-1",
        RoomSessionId = "session-1",
        MessageId = messageId,
        SenderId = "net-2",
        SenderName = "Silent",
        Content = content,
        PlainTextFallback = LanConnectServerChatProtocol.RenderGenericFallback(content),
        SentAt = "2026-07-12T12:00:00Z"
    };

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private static LanConnectChatChannelState EnabledState(LanConnectChatChannel channel)
    {
        LanConnectChatChannelState state = new(channel);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            ProtocolVersion = 1,
            Channel = channel,
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

    private static async Task ConnectReadyAsync(
        LanConnectServerChatClient client,
        FakeTransport transport,
        LanConnectChatFeatureVersions? features = null)
    {
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        transport.Emit(JsonSerializer.Serialize(new LanConnectServerChatReadyEnvelope
        {
            Channel = LanConnectChatChannel.Server,
            InstanceId = "instance-1",
            HistoryEpoch = 1,
            ChatEnabled = true,
            ServerChatVersion = 1,
            EnabledFeatures = features ?? new LanConnectChatFeatureVersions(1, 1, 1, 0)
        }, LanConnectJson.Options));
        transport.Emit("""{"type":"chat_snapshot_begin","protocolVersion":1,"snapshotId":"snapshot-1","instanceId":"instance-1","historyEpoch":1,"totalMessages":0}""");
        transport.Emit("""{"type":"chat_snapshot_end","protocolVersion":1,"snapshotId":"snapshot-1","historyEpoch":1}""");
        Assert.True(client.CanSend);
    }

    private sealed class FakeApi : ILanConnectServerChatApi
    {
        public Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new LobbyProbeResponse
            {
                Ok = true,
                Capabilities = new LobbyProbeCapabilities { ServerChatVersion = 1 }
            });

        public Task<ServerChatTicketResponse> CreateServerChatTicketAsync(
            ServerChatTicketRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new ServerChatTicketResponse
        {
            Ticket = "ticket",
            WebSocketUrl = "wss://chat.example/session",
            ProtocolVersion = 1
        });

        public void Dispose()
        {
        }
    }

    private sealed class FakeTransport : ILanConnectServerChatTransport
    {
        public event Action<string>? PayloadReceived;
        public event Action<Exception>? Faulted
        {
            add { }
            remove { }
        }
        public event Action? Closed
        {
            add { }
            remove { }
        }

        internal List<string> SentPayloads { get; } = [];

        public Task ConnectAsync(
            Uri uri,
            IReadOnlyDictionary<string, string>? requestHeaders,
            CancellationToken connectCancellationToken,
            CancellationToken receiveLifetimeCancellationToken) => Task.CompletedTask;

        public Task SendAsync(string payload, CancellationToken cancellationToken)
        {
            SentPayloads.Add(payload);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal void Emit(string payload) => PayloadReceived?.Invoke(payload);
    }

    private sealed class StubWebSocket : ILanConnectWebSocket
    {
        public WebSocketState State { get; private set; } = WebSocketState.None;

        internal List<string> SentPayloads { get; } = [];

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
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return default;
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            SentPayloads.Add(System.Text.Encoding.UTF8.GetString(buffer.Span));
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

        public void Abort() => State = WebSocketState.Aborted;

        public ValueTask DisposeAsync()
        {
            State = WebSocketState.Closed;
            return ValueTask.CompletedTask;
        }

    }

    private sealed class CoordinatorClient : ILanConnectServerChatClient
    {
        public LanConnectChatChannelState State { get; } = new(LanConnectChatChannel.Server);
        public event Action? StateChanged { add { } remove { } }
        public Task ConnectAsync(Uri lobbyBaseUri, string playerNetId, string playerName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendAsync(LanConnectChatContent content, string clientMessageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RetryAsync(string clientMessageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MutableRoomClock
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-07-12T12:00:00Z");
    }

    private sealed class RoomDelay
    {
        private readonly Queue<TaskCompletionSource> _pending = new();
        internal List<TimeSpan> Durations { get; } = [];
        internal List<CancellationToken> Tokens { get; } = [];

        internal Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Durations.Add(duration);
            Tokens.Add(cancellationToken);
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _pending.Enqueue(completion);
            return completion.Task;
        }

        internal void CompleteNext() => _pending.Dequeue().TrySetResult();
    }
}
