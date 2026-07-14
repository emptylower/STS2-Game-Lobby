using System.Collections.Concurrent;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatChannelViewStateTests
{
    [Fact]
    public void TwoRemoteMessagesWhileHiddenTrackUnreadAndFirstSequence()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);

        state.AppendConfirmedForTests("remote-10", "Silent", "first", sequence: 10, isLocal: false);
        state.AppendConfirmedForTests("remote-11", "Silent", "second", sequence: 11, isLocal: false);

        Assert.Equal(2, state.UnreadCount);
        Assert.Equal(10, state.FirstUnreadSequence);
        Assert.Equal(0, state.NewMessagesBelowCount);
    }

    [Fact]
    public void RemoteMessageWhileVisibleAndScrolledUpTracksBelowWithoutMovingScroll()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.SetVisible(true);
        state.SetScrollState(84, atBottom: false);

        state.AppendConfirmedForTests("remote-10", "Silent", "new", sequence: 10, isLocal: false);

        Assert.Equal(0, state.UnreadCount);
        Assert.Null(state.FirstUnreadSequence);
        Assert.Equal(1, state.NewMessagesBelowCount);
        Assert.Equal(84, state.ScrollOffset);
        Assert.False(state.IsAtBottom);
    }

    [Fact]
    public void ContextChangeClearsMessagesAndViewStateButPreservesProtocolCapability()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(BuildReady(chatEnabled: true));
        state.SetDraft("draft");
        state.SetScrollState(84, atBottom: false);
        state.AppendConfirmedForTests("remote-10", "Silent", "new", sequence: 10, isLocal: false);
        state.Queue(new ServerChatPendingMessage { ClientMessageId = "pending", Text = "pending" });
        state.Queue(new ServerChatPendingMessage { ClientMessageId = "failed", Text = "failed" });
        state.MarkFailed("failed", "rejected", "Rejected");
        state.Queue(new ServerChatPendingMessage { ClientMessageId = "unknown", Text = "unknown" });
        state.MarkTimedOut(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(11));
        state.SetVisible(true);

        state.ClearForContextChange();

        Assert.Empty(state.Messages);
        Assert.Equal(string.Empty, state.Draft);
        Assert.Equal(0, state.UnreadCount);
        Assert.Null(state.FirstUnreadSequence);
        Assert.Equal(0, state.ScrollOffset);
        Assert.True(state.IsAtBottom);
        Assert.Equal(0, state.NewMessagesBelowCount);
        Assert.False(state.IsVisible);
        Assert.True(state.ChatEnabled);
    }

    [Fact]
    public void LiveRemoteMessagesUseOneMonotonicArrivalSequenceAcrossChannels()
    {
        LanConnectChatChannelState server = new(LanConnectChatChannel.Server);
        LanConnectChatChannelState room = new(LanConnectChatChannel.Room);

        server.Apply(BuildMessage("server-first"));
        room.Apply(BuildMessage("room-second"));

        ServerChatMessageState serverMessage = Assert.Single(server.Messages);
        ServerChatMessageState roomMessage = Assert.Single(room.Messages);
        Assert.True(serverMessage.Sequence > 0);
        Assert.True(roomMessage.Sequence > serverMessage.Sequence);
        Assert.Equal(serverMessage.Sequence, server.FirstUnreadSequence);
        Assert.Equal(roomMessage.Sequence, room.FirstUnreadSequence);
    }

    [Fact]
    public void LocalAndDuplicateRemoteMessagesDoNotInflateUnread()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.AppendConfirmedForTests("local", "Ironclad", "local", sequence: 4, isLocal: true);

        state.Apply(BuildMessage("remote"));
        state.Apply(BuildMessage("remote"));

        Assert.Equal(1, state.UnreadCount);
        Assert.Equal(Assert.Single(state.Messages, message => message.MessageId == "remote").Sequence,
            state.FirstUnreadSequence);
    }

    [Fact]
    public void BecomingVisibleMarksHiddenMessagesReadWithOneRevisionChange()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.AppendConfirmedForTests("remote", "Silent", "new", sequence: 10, isLocal: false);
        long revisionBefore = state.Revision;

        state.SetVisible(true);

        Assert.True(state.IsVisible);
        Assert.Equal(0, state.UnreadCount);
        Assert.Null(state.FirstUnreadSequence);
        Assert.Equal(revisionBefore + 1, state.Revision);
    }

    [Fact]
    public void ScrollStateClampsNegativeOffsetAndReturningToBottomClearsBelow()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.SetVisible(true);
        state.SetScrollState(42, atBottom: false);
        state.AppendConfirmedForTests("remote", "Silent", "new", sequence: 10, isLocal: false);

        state.SetScrollState(-12, atBottom: true);

        Assert.Equal(0, state.ScrollOffset);
        Assert.True(state.IsAtBottom);
        Assert.Equal(0, state.NewMessagesBelowCount);
    }

    [Fact]
    public void DraftAndMarkReadTouchRevisionOnlyWhenStateChanges()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        long initialRevision = state.Revision;

        state.SetDraft(null);
        state.MarkRead();
        Assert.Equal(initialRevision, state.Revision);

        state.SetDraft("hello");
        Assert.Equal("hello", state.Draft);
        Assert.Equal(initialRevision + 1, state.Revision);

        state.AppendConfirmedForTests("remote", "Silent", "new", sequence: 10, isLocal: false);
        long beforeMarkRead = state.Revision;
        state.MarkRead();
        Assert.Equal(beforeMarkRead + 1, state.Revision);
        Assert.Equal(0, state.UnreadCount);
        Assert.Null(state.FirstUnreadSequence);
    }

    [Fact]
    public void ChannelOwnsOneRichDraftWhileTextCompatibilitySetterReusesIt()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        LanConnectRichDraft owned = state.RichDraft;

        state.SetDraft("hello");
        Assert.Same(owned, state.RichDraft);
        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("hello") }, owned.Runs);
        Assert.Equal("hello", state.Draft);

        long beforeRichEditGeneration = state.DraftGeneration;
        long beforeRichEditRevision = state.Revision;
        owned.SetCaret(new LanConnectDraftPosition(0, 5));
        owned.InsertEntity(new LanConnectEmojiRun("heart"));
        Assert.Equal("hello[Emoji]", state.Draft);
        Assert.Equal(beforeRichEditGeneration + 1, state.DraftGeneration);
        Assert.Equal(beforeRichEditRevision + 1, state.Revision);

        state.SetDraft("replacement");
        Assert.Same(owned, state.RichDraft);
        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("replacement") }, owned.Runs);

        state.ClearForContextChange();
        Assert.Same(owned, state.RichDraft);
        Assert.True(owned.IsEmpty);
        Assert.Equal(string.Empty, state.Draft);
    }

    [Fact]
    public async Task ConcurrentDraftEditsReadsAndContextClearsRemainConsistent()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        LanConnectRichDraft draft = state.RichDraft;
        ConcurrentQueue<Exception> failures = new();

        Task[] workers =
        [
            Task.Run(() => RepeatSafely(600, failures, index =>
            {
                draft.SetCaret(new LanConnectDraftPosition(index % 8, index % 16));
                draft.InsertText(index % 2 == 0 ? "x" : "\U0001F600");
                if (index % 3 == 0)
                {
                    draft.Backspace();
                }
            })),
            Task.Run(() => RepeatSafely(600, failures, index =>
            {
                draft.SetSelection(new LanConnectDraftSelection(
                    new LanConnectDraftPosition(index % 5, index % 11),
                    new LanConnectDraftPosition((index + 1) % 5, (index + 3) % 11)));
                draft.InsertEntity(new LanConnectEmojiRun("smile"));
                if (index % 2 == 0)
                {
                    draft.Delete();
                }
            })),
            Task.Run(() => RepeatSafely(800, failures, index =>
            {
                int observed = draft.Runs.Count +
                    draft.Selection.Active.RunIndex +
                    draft.ToContent().Segments.Count +
                    draft.ToCompatibilityText().Length +
                    state.Draft.Length +
                    index;
                GC.KeepAlive(observed);
            })),
            Task.Run(() => RepeatSafely(250, failures, index =>
            {
                if (index % 2 == 0)
                {
                    state.ClearForContextChange();
                }
                else
                {
                    state.SetDraft("context");
                }
            }))
        ];

        await Task.WhenAll(workers);

        Assert.Empty(failures);
        state.ClearForContextChange();
        long generationAfterClear = state.DraftGeneration;
        long revisionAfterClear = state.Revision;
        Assert.True(draft.IsEmpty);
        Assert.Equal(string.Empty, state.Draft);
        Assert.Equal(generationAfterClear, state.DraftGeneration);
        Assert.Equal(revisionAfterClear, state.Revision);

        draft.InsertText("final");
        Assert.Equal(generationAfterClear + 1, state.DraftGeneration);
        Assert.Equal(revisionAfterClear + 1, state.Revision);
        Assert.Equal("final", state.Draft);
    }

    [Fact]
    public async Task ContextClearDoesNotSuppressAnEditThatStartsAfterItsClearRevision()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        LanConnectRichDraft draft = state.RichDraft;
        state.SetDraft("before");
        long generationBefore = state.DraftGeneration;
        long stateRevisionBefore = state.Revision;
        long expectedClearRevision = draft.ContentRevision + 1;
        using ManualResetEventSlim clearNotificationEntered = new(false);
        using ManualResetEventSlim releaseClearNotification = new(false);
        draft.ContentChanged += revision =>
        {
            if (revision == expectedClearRevision)
            {
                clearNotificationEntered.Set();
                releaseClearNotification.Wait(TimeSpan.FromSeconds(5));
            }
        };

        Task clear = Task.Run(state.ClearForContextChange);
        Assert.True(clearNotificationEntered.Wait(TimeSpan.FromSeconds(5)));
        Task edit = Task.Run(() => draft.InsertText("after"));
        Assert.True(SpinWait.SpinUntil(
            () => draft.ContentRevision > expectedClearRevision,
            TimeSpan.FromSeconds(5)));
        releaseClearNotification.Set();

        await Task.WhenAll(clear, edit);

        Assert.Equal("after", state.Draft);
        Assert.Equal(generationBefore + 2, state.DraftGeneration);
        Assert.Equal(stateRevisionBefore + 2, state.Revision);
    }

    [Fact]
    public async Task ConditionalClearReconcilesPendingRichEditWithSameCompatibilityText()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        LanConnectRichDraft draft = state.RichDraft;
        state.SetDraft("[Emoji]");
        long expectedContextGeneration = state.ContextGeneration;
        long expectedDraftGeneration = state.DraftGeneration;
        long expectedStateRevision = state.Revision;
        long contentRevisionBefore = draft.ContentRevision;
        System.Reflection.FieldInfo? lockField = typeof(LanConnectChatChannelState).GetField(
            "_mutationLock",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(lockField);
        object mutationLock = Assert.IsType<object>(lockField!.GetValue(state));
        Task edit;

        lock (mutationLock)
        {
            edit = Task.Run(() =>
            {
                draft.SetSelection(new LanConnectDraftSelection(
                    new LanConnectDraftPosition(0, 0),
                    new LanConnectDraftPosition(0, "[Emoji]".Length)));
                draft.InsertEntity(new LanConnectEmojiRun("smile"));
            });
            Assert.True(SpinWait.SpinUntil(
                () => draft.ContentRevision > contentRevisionBefore,
                TimeSpan.FromSeconds(5)));
            Assert.Equal("[Emoji]", draft.ToCompatibilityText());

            Assert.False(state.ClearDraftIfMatches(
                expectedContextGeneration,
                expectedDraftGeneration,
                "[Emoji]"));
            Assert.False(draft.IsEmpty);
            Assert.Equal(expectedDraftGeneration + 1, state.DraftGeneration);
            Assert.Equal(expectedStateRevision + 1, state.Revision);
        }

        await edit;

        Assert.Equal(new LanConnectDraftRun[] { new LanConnectEmojiRun("smile") }, draft.Runs);
        Assert.Equal("[Emoji]", state.Draft);
        Assert.Equal(expectedDraftGeneration + 1, state.DraftGeneration);
        Assert.Equal(expectedStateRevision + 1, state.Revision);
    }

    private static void RepeatSafely(
        int count,
        ConcurrentQueue<Exception> failures,
        Action<int> action)
    {
        for (int index = 0; index < count; index++)
        {
            try
            {
                action(index);
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        }
    }

    [Fact]
    public void HiddenSnapshotReplacementDoesNotInflateUnread()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.SetVisible(true);
        state.SetScrollState(42, atBottom: false);
        state.AppendConfirmedForTests("below", "Silent", "below", sequence: 3, isLocal: false);
        state.SetVisible(false);
        state.AppendConfirmedForTests("unread", "Silent", "unread", sequence: 4, isLocal: false);

        state.Apply(BuildSnapshotBegin("snapshot", totalMessages: 1));
        state.Apply(BuildSnapshotChunk("snapshot", BuildCanonical("fresh")));
        state.Apply(BuildSnapshotEnd("snapshot"));

        Assert.Equal("fresh", Assert.Single(state.Messages).MessageId);
        Assert.Equal(0, state.UnreadCount);
        Assert.Null(state.FirstUnreadSequence);
        Assert.Equal(0, state.NewMessagesBelowCount);
    }

    [Fact]
    public void HistoryEpochClearResetsDerivedIncomingCountsButKeepsDraftAndScroll()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.SetDraft("draft");
        state.SetVisible(true);
        state.SetScrollState(42, atBottom: false);
        state.AppendConfirmedForTests("below", "Silent", "below", sequence: 3, isLocal: false);
        state.SetVisible(false);
        state.AppendConfirmedForTests("unread", "Silent", "unread", sequence: 4, isLocal: false);

        state.Apply(BuildHistoryCleared(historyEpoch: 2));

        Assert.Empty(state.Messages);
        Assert.Equal(0, state.UnreadCount);
        Assert.Null(state.FirstUnreadSequence);
        Assert.Equal(0, state.NewMessagesBelowCount);
        Assert.Equal("draft", state.Draft);
        Assert.Equal(42, state.ScrollOffset);
        Assert.False(state.IsAtBottom);
        Assert.False(state.IsVisible);
    }

    [Fact]
    public async Task SharedArrivalClockIsUniqueAndMonotonicAcrossConcurrentStates()
    {
        LanConnectChatArrivalSequenceClock clock = new();
        LanConnectChatChannelState[] states = Enumerable.Range(0, 64)
            .Select(index => new LanConnectChatChannelState(
                index % 2 == 0 ? LanConnectChatChannel.Server : LanConnectChatChannel.Room, clock))
            .ToArray();

        await Task.WhenAll(states.Select((state, index) => Task.Run(() =>
            state.Apply(BuildMessage($"message-{index}")))));

        long[] sequences = states.Select(state => Assert.Single(state.Messages).Sequence).Order().ToArray();
        Assert.Equal(Enumerable.Range(1, states.Length).Select(value => (long)value), sequences);
    }

    [Fact]
    public void ExhaustedArrivalClockFailsBeforeMutatingChannelState()
    {
        LanConnectChatArrivalSequenceClock clock = new(long.MaxValue);
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server, clock);

        Assert.Throws<InvalidOperationException>(() => state.Apply(BuildMessage("overflow")));

        Assert.Empty(state.Messages);
        Assert.Equal(0, state.UnreadCount);
        Assert.Null(state.FirstUnreadSequence);
        Assert.Equal(0, state.Revision);
    }

    [Fact]
    public void ExplicitMaximumSequenceIsRejectedBeforeHelperMutation()
    {
        LanConnectChatArrivalSequenceClock clock = new();
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server, clock);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.AppendConfirmedForTests("poison", "Silent", "poison", long.MaxValue, isLocal: false));

        Assert.Empty(state.Messages);
        state.Apply(BuildMessage("healthy"));
        Assert.Equal(1, Assert.Single(state.Messages).Sequence);
    }

    private static ServerChatInboundEnvelope BuildReady(bool chatEnabled)
    {
        ServerChatReadyEnvelope envelope = new()
        {
            InstanceId = "instance-1",
            HistoryEpoch = 1,
            ChatEnabled = chatEnabled,
            EnabledFeatures = new ServerChatEnabledFeatures()
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(
            JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildMessage(string messageId)
    {
        ServerChatMessageEnvelope envelope = new()
        {
            Message = new ServerChatCanonicalMessage
            {
                MessageId = messageId,
                SenderName = "Silent",
                SentAt = DateTimeOffset.Parse("2026-07-14T00:00:00Z"),
                Content = new ServerChatContent
                {
                    FormatVersion = 1,
                    Segments = [new ServerChatTextSegment { Kind = "text", Text = messageId }]
                },
                PlainTextFallback = messageId
            }
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(
            JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildSnapshotBegin(string snapshotId, int totalMessages)
    {
        ServerChatSnapshotBeginEnvelope envelope = new()
        {
            SnapshotId = snapshotId,
            InstanceId = "instance-1",
            HistoryEpoch = 1,
            TotalMessages = totalMessages
        };
        return Project(envelope);
    }

    private static ServerChatInboundEnvelope BuildSnapshotChunk(string snapshotId, ServerChatCanonicalMessage message)
    {
        ServerChatSnapshotChunkEnvelope envelope = new()
        {
            SnapshotId = snapshotId,
            ChunkIndex = 0,
            Messages = [message]
        };
        return Project(envelope);
    }

    private static ServerChatInboundEnvelope BuildSnapshotEnd(string snapshotId)
    {
        ServerChatSnapshotEndEnvelope envelope = new()
        {
            SnapshotId = snapshotId,
            HistoryEpoch = 1
        };
        return Project(envelope);
    }

    private static ServerChatInboundEnvelope BuildHistoryCleared(int historyEpoch)
    {
        ServerChatHistoryClearedEnvelope envelope = new()
        {
            HistoryEpoch = historyEpoch,
            ChangedAt = DateTimeOffset.Parse("2026-07-14T00:01:00Z")
        };
        return Project(envelope);
    }

    private static ServerChatCanonicalMessage BuildCanonical(string messageId) => new()
    {
        MessageId = messageId,
        SenderName = "Silent",
        SentAt = DateTimeOffset.Parse("2026-07-14T00:00:00Z"),
        Content = new ServerChatContent
        {
            FormatVersion = 1,
            Segments = [new ServerChatTextSegment { Kind = "text", Text = messageId }]
        },
        PlainTextFallback = messageId
    };

    private static ServerChatInboundEnvelope Project<T>(T envelope) =>
        JsonSerializer.Deserialize<ServerChatInboundEnvelope>(
            JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
}
