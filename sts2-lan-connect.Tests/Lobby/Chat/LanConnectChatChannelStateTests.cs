using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatChannelStateTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-07-13T04:05:06.000Z");

    private static ServerChatPendingMessage Pending(string clientMessageId, string senderName, string text) =>
        PendingAt(clientMessageId, senderName, text, FixedNow + TimeSpan.FromSeconds(20));

    private static ServerChatPendingMessage PendingAt(string clientMessageId, string senderName, string text, DateTimeOffset queuedAt) =>
        new()
        {
            ClientMessageId = clientMessageId,
            SenderName = senderName,
            Text = text,
            QueuedAt = queuedAt
        };

    [Fact]
    public void QueueThenAckProducesOneAuthoritativeConfirmedMessage()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);

        state.Queue(Pending("client-1", "Ironclad", "hello"));

        LanConnectChatApplyResult afterQueue = state.Apply(BuildAck("client-1", "server-msg-1", "Ironclad", "hello"));
        LanConnectChatApplyResult afterSelfBroadcast = state.Apply(BuildMessage("server-msg-1", "Ironclad", "hello"));

        ServerChatMessageState message = Assert.Single(state.Messages);
        Assert.Equal("server-msg-1", message.MessageId);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
        Assert.True(message.IsLocal);
        Assert.Equal(0, message.Sequence);
        Assert.False(afterQueue.ReconnectRequired);
        Assert.False(afterSelfBroadcast.ReconnectRequired);
        Assert.True(state.Revision > 0);
    }

    [Fact]
    public void MessageIdDedupeIgnoresRepeatBroadcasts()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);

        state.Apply(BuildMessage("dup-1", "Ironclad", "first"));
        state.Apply(BuildMessage("dup-1", "Ironclad", "first"));

        ServerChatMessageState message = Assert.Single(state.Messages);
        Assert.Equal("dup-1", message.MessageId);
        Assert.Equal("first", message.Text);
        Assert.Equal(1, state.Messages.Count);
    }

    [Fact]
    public void ErrorMarksPendingMessageFailed()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);

        state.Queue(Pending("client-err", "Ironclad", "hello"));
        LanConnectChatApplyResult result = state.Apply(BuildError("client-err", "rate_limited", "请求过于频繁。"));

        Assert.False(result.ReconnectRequired);
        ServerChatMessageState message = Assert.Single(state.Messages);
        Assert.Equal(ServerChatDeliveryState.Failed, message.Delivery);
        Assert.Equal("rate_limited", message.ErrorCode);
        Assert.Equal("请求过于频繁。", message.ErrorMessage);
    }

    [Fact]
    public void TenSecondStalePendingBecomesDeliveryUnknown()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);

        state.Queue(PendingAt("client-slow", "Ironclad", "hello", FixedNow - TimeSpan.FromSeconds(20)));

        state.MarkTimedOut(FixedNow + TimeSpan.FromSeconds(10));

        ServerChatMessageState message = Assert.Single(state.Messages);
        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, message.Delivery);
        Assert.Equal("client-slow", message.ClientMessageId);
    }

    [Fact]
    public void DisconnectPreservesPendingFailedAndUnknownWithoutMutation()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);

        state.Queue(Pending("client-pend", "Ironclad", "p"));
        state.Queue(Pending("client-fail", "Ironclad", "f"));
        state.Apply(BuildError("client-fail", "rate_limited", "请求过于频繁。"));
        state.Queue(PendingAt("client-unknown", "Ironclad", "u", FixedNow - TimeSpan.FromSeconds(20)));
        state.MarkTimedOut(FixedNow + TimeSpan.FromSeconds(10));

        long revisionBefore = state.Revision;
        state.MarkDisconnected();

        Assert.Equal(revisionBefore, state.Revision);
        Assert.Equal(3, state.Messages.Count);
        Assert.Contains(state.Messages, m => m.ClientMessageId == "client-pend" && m.Delivery == ServerChatDeliveryState.Pending);
        Assert.Contains(state.Messages, m => m.ClientMessageId == "client-fail" && m.Delivery == ServerChatDeliveryState.Failed);
        Assert.Contains(state.Messages, m => m.ClientMessageId == "client-unknown" && m.Delivery == ServerChatDeliveryState.DeliveryUnknown);
    }

    [Fact]
    public void DisconnectDropsIncompleteSnapshotAssemblySoReconnectCanReplaceHistory()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.AppendConfirmedForTests("old-1", "Ironclad", "old", 0, isLocal: false);
        state.Apply(BuildSnapshotBegin("abandoned", "instance-1", historyEpoch: 1, totalMessages: 1));

        state.MarkDisconnected();
        LanConnectChatApplyResult begin = state.Apply(
            BuildSnapshotBegin("replacement", "instance-2", historyEpoch: 2, totalMessages: 1));
        LanConnectChatApplyResult chunk = state.Apply(
            BuildSnapshotChunk("replacement", 0, BuildCanonical("fresh-1", "Silent", "fresh", sequence: 0)));
        LanConnectChatApplyResult end = state.Apply(BuildSnapshotEnd("replacement", historyEpoch: 2));

        Assert.False(begin.ReconnectRequired);
        Assert.False(chunk.ReconnectRequired);
        Assert.False(end.ReconnectRequired);
        Assert.Equal("fresh-1", Assert.Single(state.Messages).MessageId);
    }

    [Fact]
    public void RichInboundUsesPlainTextFallbackWhenNoOtherText()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        string rich = BuildRichOnlyMessage("rich-1", "Ironclad", "hello-emoji");

        LanConnectChatApplyResult result = state.Apply(JsonSerializer.Deserialize<ServerChatInboundEnvelope>(rich, LanConnectJson.Options)!);

        Assert.False(result.ReconnectRequired);
        ServerChatMessageState message = Assert.Single(state.Messages);
        Assert.Equal("hello-emoji", message.Text);
        Assert.Equal("rich-1", message.MessageId);
        Assert.False(message.IsLocal);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
    }

    [Fact]
    public void RichInboundWithoutUsableTextIsIgnored()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        long revisionBefore = state.Revision;
        string rich = BuildRichOnlyMessage("rich-empty", "Ironclad", string.Empty);

        LanConnectChatApplyResult result = state.Apply(JsonSerializer.Deserialize<ServerChatInboundEnvelope>(rich, LanConnectJson.Options)!);

        Assert.False(result.ReconnectRequired);
        Assert.Empty(state.Messages);
        Assert.Equal(revisionBefore, state.Revision);
    }

    [Fact]
    public void RichInboundAndAckUseCompleteFallbackWhenTextAndEntitySegmentsAreMixed()
    {
        ServerChatCanonicalMessage rich = BuildRichCanonical(
            "rich-mixed",
            "Ironclad",
            partialText: "played Strike",
            plainTextFallback: "played Strike [card: Strike]");
        LanConnectChatChannelState live = new(LanConnectChatChannel.Server);
        LanConnectChatChannelState ack = new(LanConnectChatChannel.Server);
        ack.Queue(Pending("client-rich", "Ironclad", "local draft"));

        live.Apply(BuildMessage(rich));
        ack.Apply(BuildAck("client-rich", rich));

        Assert.Equal("played Strike [card: Strike]", Assert.Single(live.Messages).Text);
        ServerChatMessageState confirmed = Assert.Single(ack.Messages);
        Assert.Equal("played Strike [card: Strike]", confirmed.Text);
        Assert.Equal(ServerChatDeliveryState.Confirmed, confirmed.Delivery);
    }

    [Fact]
    public void RichWithoutFallbackIsIgnoredConsistentlyByLiveAndSnapshot()
    {
        ServerChatCanonicalMessage rich = BuildRichCanonical(
            "rich-empty",
            "Ironclad",
            partialText: "partial",
            plainTextFallback: string.Empty);
        LanConnectChatChannelState live = new(LanConnectChatChannel.Server);
        LanConnectChatChannelState snapshot = new(LanConnectChatChannel.Server);
        snapshot.AppendConfirmedForTests("old-1", "Silent", "old", 0, isLocal: false);

        live.Apply(BuildMessage(rich));
        snapshot.Apply(BuildSnapshotBegin("snap-rich", "instance-1", historyEpoch: 1, totalMessages: 1));
        snapshot.Apply(BuildSnapshotChunk("snap-rich", 0, rich));
        LanConnectChatApplyResult end = snapshot.Apply(BuildSnapshotEnd("snap-rich", historyEpoch: 1));

        Assert.Empty(live.Messages);
        Assert.False(end.ReconnectRequired);
        Assert.Empty(snapshot.Messages);
    }

    [Fact]
    public void SnapshotReplacesConfirmedMessagesAndAdvancesRevision()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.AppendConfirmedForTests("stale-1", "OldSender", "old", 0, isLocal: false);
        long revisionBefore = state.Revision;

        state.Apply(BuildSnapshotBegin("snap-1", "instance-1", historyEpoch: 1, totalMessages: 2));
        state.Apply(BuildSnapshotChunk("snap-1", 0, BuildCanonical("fresh-1", "Ironclad", "fresh-a", sequence: 0)));
        state.Apply(BuildSnapshotChunk("snap-1", 1, BuildCanonical("fresh-2", "Ironclad", "fresh-b", sequence: 1)));
        LanConnectChatApplyResult endResult = state.Apply(BuildSnapshotEnd("snap-1", historyEpoch: 1));

        Assert.False(endResult.ReconnectRequired);
        Assert.Equal(2, state.Messages.Count);
        Assert.DoesNotContain(state.Messages, m => m.MessageId == "stale-1");
        Assert.Contains(state.Messages, m => m.MessageId == "fresh-1");
        Assert.True(state.Revision > revisionBefore);
    }

    [Fact]
    public void SnapshotWithMismatchedIdRequestsReconnectAndRetainsOldData()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.AppendConfirmedForTests("keep-1", "Ironclad", "keep", 0, isLocal: false);
        long revisionBefore = state.Revision;

        state.Apply(BuildSnapshotBegin("snap-a", "instance-1", historyEpoch: 1, totalMessages: 1));
        LanConnectChatApplyResult chunkResult = state.Apply(BuildSnapshotChunk("snap-b", 0, BuildCanonical("fresh-1", "Ironclad", "fresh", sequence: 0)));

        Assert.True(chunkResult.ReconnectRequired);
        ServerChatMessageState keep = Assert.Single(state.Messages);
        Assert.Equal("keep-1", keep.MessageId);
        Assert.Equal(revisionBefore, state.Revision);
    }

    [Theory]
    [InlineData("out_of_order")]
    [InlineData("duplicate_message")]
    [InlineData("missing_message")]
    public void InvalidSnapshotContinuityRequestsReconnectAndRetainsOldData(string failure)
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.AppendConfirmedForTests("keep-1", "Ironclad", "keep", 0, isLocal: false);
        long revisionBefore = state.Revision;

        state.Apply(BuildSnapshotBegin("snap-1", "instance-1", historyEpoch: 1, totalMessages: 2));
        LanConnectChatApplyResult result;
        if (failure == "out_of_order")
        {
            result = state.Apply(BuildSnapshotChunk("snap-1", 1, BuildCanonical("fresh-1", "Ironclad", "fresh", sequence: 0)));
        }
        else
        {
            state.Apply(BuildSnapshotChunk("snap-1", 0, BuildCanonical("fresh-1", "Ironclad", "fresh", sequence: 0)));
            result = failure == "duplicate_message"
                ? state.Apply(BuildSnapshotChunk("snap-1", 1, BuildCanonical("fresh-1", "Ironclad", "duplicate", sequence: 1)))
                : state.Apply(BuildSnapshotEnd("snap-1", historyEpoch: 1));
        }

        Assert.True(result.ReconnectRequired);
        ServerChatMessageState keep = Assert.Single(state.Messages);
        Assert.Equal("keep-1", keep.MessageId);
        Assert.Equal(revisionBefore, state.Revision);
    }

    [Fact]
    public void NewInstanceIdClearsConfirmedAndKeepsPendingAndUnknown()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(BuildReady(instanceId: "instance-1", historyEpoch: 4));
        state.AppendConfirmedForTests("stale-1", "Ironclad", "stale", 0, isLocal: false);
        state.Queue(Pending("client-pend", "Ironclad", "p"));
        state.Queue(PendingAt("client-unknown", "Ironclad", "u", FixedNow - TimeSpan.FromSeconds(20)));
        state.MarkTimedOut(FixedNow + TimeSpan.FromSeconds(10));

        long revisionBefore = state.Revision;
        state.Apply(BuildReady(instanceId: "instance-2", historyEpoch: 4));

        Assert.Equal(2, state.Messages.Count);
        Assert.DoesNotContain(state.Messages, m => m.MessageId == "stale-1");
        Assert.Contains(state.Messages, m => m.ClientMessageId == "client-pend" && m.Delivery == ServerChatDeliveryState.Pending);
        Assert.Contains(state.Messages, m => m.ClientMessageId == "client-unknown" && m.Delivery == ServerChatDeliveryState.DeliveryUnknown);
        Assert.True(state.Revision > revisionBefore);
    }

    [Fact]
    public void GreaterHistoryEpochClearsConfirmedAndKeepsPendingAndUnknown()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.AppendConfirmedForTests("stale-1", "Ironclad", "stale", 0, isLocal: false);
        state.Queue(Pending("client-pend", "Ironclad", "p"));
        state.Queue(PendingAt("client-unknown", "Ironclad", "u", FixedNow - TimeSpan.FromSeconds(20)));
        state.MarkTimedOut(FixedNow + TimeSpan.FromSeconds(10));

        state.Apply(BuildHistoryCleared(historyEpoch: 5));

        Assert.Equal(2, state.Messages.Count);
        Assert.DoesNotContain(state.Messages, m => m.MessageId == "stale-1");
        Assert.Contains(state.Messages, m => m.ClientMessageId == "client-pend" && m.Delivery == ServerChatDeliveryState.Pending);
        Assert.Contains(state.Messages, m => m.ClientMessageId == "client-unknown" && m.Delivery == ServerChatDeliveryState.DeliveryUnknown);
    }

    [Fact]
    public void ClearForContextChangeClearsConfirmedPendingFailedAndUnknown()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.AppendConfirmedForTests("stale-1", "Ironclad", "stale", 0, isLocal: false);
        state.Queue(Pending("client-pend", "Ironclad", "p"));
        state.Queue(Pending("client-fail", "Ironclad", "f"));
        state.MarkFailed("client-fail", "rejected", "Rejected");
        state.Queue(PendingAt("client-unknown", "Ironclad", "u", FixedNow - TimeSpan.FromSeconds(20)));
        state.MarkTimedOut(FixedNow + TimeSpan.FromSeconds(10));

        state.ClearForContextChange();

        Assert.Empty(state.Messages);
    }

    [Fact]
    public void BroadcastBeforeAckMergesPendingAndRollsBackHiddenUnread()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Queue(Pending("client-1", "Ironclad", "hello"));
        state.Apply(BuildMessage("server-msg-1", "Ironclad", "hello"));
        state.Apply(BuildMessage("remote-2", "Silent", "other"));
        long otherSequence = Assert.Single(state.Messages, message => message.MessageId == "remote-2").Sequence;
        Assert.Equal(2, state.UnreadCount);

        state.Apply(BuildAck("client-1", "server-msg-1", "Ironclad", "hello"));
        state.Apply(BuildMessage("server-msg-1", "Ironclad", "hello"));

        Assert.Equal(2, state.Messages.Count);
        ServerChatMessageState message = Assert.Single(state.Messages, entry => entry.MessageId == "server-msg-1");
        Assert.Equal("server-msg-1", message.MessageId);
        Assert.Equal("client-1", message.ClientMessageId);
        Assert.True(message.IsLocal);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
        Assert.Equal(1, state.UnreadCount);
        Assert.Equal(otherSequence, state.FirstUnreadSequence);
    }

    [Fact]
    public void BroadcastBeforeAckMergesPendingAndRollsBackBelowCount()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.SetVisible(true);
        state.SetScrollState(84, atBottom: false);
        state.Queue(Pending("client-1", "Ironclad", "hello"));
        state.Apply(BuildMessage("server-msg-1", "Ironclad", "hello"));
        state.Apply(BuildMessage("remote-2", "Silent", "other"));
        Assert.Equal(2, state.NewMessagesBelowCount);

        state.Apply(BuildAck("client-1", "server-msg-1", "Ironclad", "hello"));

        Assert.Equal(2, state.Messages.Count);
        Assert.Equal(1, state.NewMessagesBelowCount);
        Assert.Equal(84, state.ScrollOffset);
    }

    [Fact]
    public void SnapshotBeforeAckMergesPendingIntoOneAuthoritativeMessage()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Queue(Pending("client-1", "Ironclad", "draft"));
        state.Apply(BuildSnapshotBegin("snapshot", "instance-1", historyEpoch: 1, totalMessages: 1));
        state.Apply(BuildSnapshotChunk(
            "snapshot", 0, BuildCanonical("server-msg-1", "Ironclad", "canonical", sequence: 0)));
        state.Apply(BuildSnapshotEnd("snapshot", historyEpoch: 1));
        Assert.Equal(2, state.Messages.Count);

        state.Apply(BuildAck("client-1", "server-msg-1", "Ironclad", "canonical"));

        ServerChatMessageState message = Assert.Single(state.Messages);
        Assert.Equal("server-msg-1", message.MessageId);
        Assert.Equal("client-1", message.ClientMessageId);
        Assert.Equal("canonical", message.Text);
        Assert.True(message.IsLocal);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
        long revisionAfterAck = state.Revision;
        state.MarkTimedOut(FixedNow + TimeSpan.FromDays(1));
        state.Apply(BuildMessage("server-msg-1", "Ironclad", "canonical"));
        Assert.Equal(revisionAfterAck, state.Revision);
        Assert.Single(state.Messages);
    }

    [Fact]
    public void EquivalentAckErrorAndMarkFailedAreRevisionNoOps()
    {
        LanConnectChatChannelState ack = new(LanConnectChatChannel.Server);
        ack.Queue(Pending("ack", "Ironclad", "hello"));
        ServerChatInboundEnvelope ackEnvelope = BuildAck("ack", "server-ack", "Ironclad", "hello");
        ack.Apply(ackEnvelope);
        long ackRevision = ack.Revision;
        ack.Apply(ackEnvelope);
        Assert.Equal(ackRevision, ack.Revision);

        LanConnectChatChannelState error = new(LanConnectChatChannel.Server);
        error.Queue(Pending("error", "Ironclad", "hello"));
        error.Apply(BuildError("error", "rejected", "Rejected"));
        long errorRevision = error.Revision;
        error.Apply(BuildError("error", "rejected", "Rejected"));
        Assert.Equal(errorRevision, error.Revision);
        error.Apply(BuildError("error", "rejected", "Changed"));
        Assert.Equal(errorRevision + 1, error.Revision);

        LanConnectChatChannelState failed = new(LanConnectChatChannel.Server);
        failed.Queue(Pending("failed", "Ironclad", "hello"));
        failed.MarkFailed("failed", "send_failed", "Failed");
        long failedRevision = failed.Revision;
        failed.MarkFailed("failed", "send_failed", "Failed");
        Assert.Equal(failedRevision, failed.Revision);
        failed.MarkFailed("failed", "send_failed", "Changed");
        Assert.Equal(failedRevision + 1, failed.Revision);
    }

    private static ServerChatInboundEnvelope BuildAck(string clientMessageId, string serverMessageId, string senderName, string text)
    {
        ServerChatAckEnvelope envelope = new()
        {
            ClientMessageId = clientMessageId,
            Message = BuildCanonical(serverMessageId, senderName, text, sequence: 0)
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildAck(string clientMessageId, ServerChatCanonicalMessage message)
    {
        ServerChatAckEnvelope envelope = new()
        {
            ClientMessageId = clientMessageId,
            Message = message
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildMessage(string messageId, string senderName, string text)
    {
        ServerChatMessageEnvelope envelope = new()
        {
            Message = BuildCanonical(messageId, senderName, text, sequence: 0)
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildMessage(ServerChatCanonicalMessage message)
    {
        ServerChatMessageEnvelope envelope = new() { Message = message };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildError(string clientMessageId, string code, string message)
    {
        ServerChatErrorEnvelope envelope = new()
        {
            ClientMessageId = clientMessageId,
            Code = code,
            Message = message
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildSnapshotBegin(string snapshotId, string instanceId, int historyEpoch, int totalMessages)
    {
        ServerChatSnapshotBeginEnvelope envelope = new()
        {
            SnapshotId = snapshotId,
            InstanceId = instanceId,
            HistoryEpoch = historyEpoch,
            TotalMessages = totalMessages
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildSnapshotChunk(string snapshotId, int index, ServerChatCanonicalMessage message)
    {
        ServerChatSnapshotChunkEnvelope envelope = new()
        {
            SnapshotId = snapshotId,
            ChunkIndex = index,
            Messages = [message]
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildSnapshotEnd(string snapshotId, int historyEpoch)
    {
        ServerChatSnapshotEndEnvelope envelope = new()
        {
            SnapshotId = snapshotId,
            HistoryEpoch = historyEpoch
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildReady(string instanceId, int historyEpoch)
    {
        ServerChatReadyEnvelope envelope = new()
        {
            Channel = LanConnectChatChannel.Server,
            InstanceId = instanceId,
            HistoryEpoch = historyEpoch,
            ChatEnabled = true,
            ServerChatVersion = 1
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatInboundEnvelope BuildHistoryCleared(int historyEpoch)
    {
        ServerChatHistoryClearedEnvelope envelope = new()
        {
            HistoryEpoch = historyEpoch,
            ChangedAt = FixedNow
        };
        return JsonSerializer.Deserialize<ServerChatInboundEnvelope>(JsonSerializer.Serialize(envelope, LanConnectJson.Options), LanConnectJson.Options)!;
    }

    private static ServerChatCanonicalMessage BuildCanonical(string messageId, string senderName, string text, long sequence)
    {
        return new ServerChatCanonicalMessage
        {
            MessageId = messageId,
            SenderId = "sender_abcdefghijklmn",
            SenderName = senderName,
            Content = new ServerChatContent
            {
                Segments = [new ServerChatTextSegment { Text = text }]
            },
            PlainTextFallback = text,
            SentAt = FixedNow.AddSeconds(sequence)
        };
    }

    private static ServerChatCanonicalMessage BuildRichCanonical(
        string messageId,
        string senderName,
        string partialText,
        string plainTextFallback) =>
        new()
        {
            MessageId = messageId,
            SenderId = "sender_abcdefghijklmn",
            SenderName = senderName,
            Content = new ServerChatContent
            {
                FormatVersion = 1,
                Segments =
                [
                    new ServerChatTextSegment { Kind = "text", Text = partialText },
                    new ServerChatTextSegment { Kind = "item_ref", Text = "Strike" }
                ]
            },
            PlainTextFallback = plainTextFallback,
            SentAt = FixedNow
        };

    private static string BuildRichOnlyMessage(string messageId, string senderName, string plainTextFallback)
    {
        string safeName = senderName.Replace("\"", "\\\"");
        string safeFallback = plainTextFallback.Replace("\"", "\\\"");
        return $$"""
            {
                "type": "chat_message",
                "protocolVersion": 1,
                "message": {
                    "messageId": "{{messageId}}",
                    "senderId": "sender_abcdefghijklmn",
                    "senderName": "{{safeName}}",
                    "content": {
                        "formatVersion": 2,
                        "segments": [
                            { "kind": "emoji", "token": ":smile:" }
                        ]
                    },
                    "plainTextFallback": "{{safeFallback}}",
                    "sentAt": "2026-07-13T04:05:06.000Z"
                }
            }
            """;
    }
}
