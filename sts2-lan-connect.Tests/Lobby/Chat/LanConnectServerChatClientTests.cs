using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectServerChatClientTests
{
    private static readonly Uri BaseUri = new("https://lobby.example/base/");
    private static readonly Uri ChatUri = new("wss://chat.example/session");
    private static readonly Guid FixedGuid = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public async Task ConnectUnsupportedProbeStopsBeforeTicketOrTransport()
    {
        List<string> operations = [];
        FakeApi api = new(operations) { ProbeVersion = 0 };
        int transportCreations = 0;
        await using LanConnectServerChatClient client = CreateClient(api, () =>
        {
            transportCreations++;
            return new FakeTransport(operations);
        });

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None));

        Assert.Equal(["probe"], operations);
        Assert.Equal(0, api.TicketCalls);
        Assert.Equal(0, transportCreations);
        Assert.True(client.IsPermanentlyStopped);
    }

    [Fact]
    public async Task ConnectProbesThenUsesExactTicketIdentityBearerAndResponseUrl()
    {
        List<string> operations = [];
        FakeApi api = new(operations);
        FakeTransport transport = new(operations);
        await using LanConnectServerChatClient client = CreateClient(api, () => transport);

        await client.ConnectAsync(BaseUri, "net-1", " Ironclad ", CancellationToken.None);

        Assert.Equal(["probe", "ticket", "transport", "connect"], operations);
        Assert.Equal(1, api.Request!.ProtocolVersion);
        Assert.Equal("net-1", api.Request.PlayerNetId);
        Assert.Equal("Ironclad", api.Request.PlayerName);
        Assert.Equal(ChatUri, transport.ConnectedUri);
        Assert.Equal("Bearer one-time-secret", transport.Headers!["Authorization"]);
        Assert.Equal(LanConnectChatChannel.Server, client.State.Channel);
        Assert.False(client.CanSend);
    }

    [Fact]
    public async Task ConnectEnablesSendOnlyAfterReadyAndCompleteSnapshot()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        await using LanConnectServerChatClient client = CreateClient(api, () => transport);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);

        transport.Emit(BuildReady());
        Assert.False(client.CanSend);
        transport.Emit(BuildSnapshotBegin(totalMessages: 0));
        Assert.False(client.CanSend);
        transport.Emit(BuildSnapshotEnd());

        Assert.True(client.CanSend);
    }

    [Fact]
    public async Task ConnectProtocolMismatchPermanentlyStops()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        await using LanConnectServerChatClient client = CreateClient(api, () => transport);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);

        transport.Emit("""{"type":"chat_ready","protocolVersion":2,"channel":"server","instanceId":"instance-1","historyEpoch":1,"chatEnabled":true,"serverChatVersion":2}""");

        Assert.True(client.IsPermanentlyStopped);
        Assert.False(client.CanSend);
    }

    [Fact]
    public async Task ConnectRaisesStateChangedOnlyWhenRevisionChanges()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        await using LanConnectServerChatClient client = CreateClient(api, () => transport);
        int changes = 0;
        client.StateChanged += () => changes++;
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);

        transport.Emit("""{"type":"unknown","protocolVersion":1}""");
        Assert.Equal(0, changes);
        transport.Emit(BuildMessage("server-1", "hello"));

        Assert.Equal(1, changes);
        Assert.Equal("hello", Assert.Single(client.State.Messages).Text);
    }

    [Fact]
    public async Task SendQueuesBeforeOneSerializedTransportSendAndUsesOneUuid()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        MutableClock clock = new();
        FakeDelay delay = new();
        int uuidCalls = 0;
        await using LanConnectServerChatClient client = CreateClient(
            api,
            () => transport,
            clock,
            delay,
            () => { uuidCalls++; return FixedGuid; });
        await ConnectReadyAsync(client, transport);
        transport.BeforeSend = payload =>
        {
            ServerChatMessageState pending = Assert.Single(client.State.Messages);
            Assert.Equal(ServerChatDeliveryState.Pending, pending.Delivery);
            ServerChatSendEnvelope observed =
                JsonSerializer.Deserialize<ServerChatSendEnvelope>(payload, LanConnectJson.Options)!;
            Assert.Equal("café\nworld", Assert.Single(observed.Content.Segments).Text);
        };

        await client.SendTextAsync("  cafe\u0301\r\nworld  ");

        Assert.Equal(1, uuidCalls);
        string payload = Assert.Single(transport.SentPayloads);
        ServerChatSendEnvelope sent = JsonSerializer.Deserialize<ServerChatSendEnvelope>(payload, LanConnectJson.Options)!;
        Assert.Equal(FixedGuid.ToString("D"), sent.ClientMessageId);
        Assert.Equal(LanConnectChatChannel.Server, sent.Channel);
        Assert.Equal("café\nworld", Assert.Single(sent.Content.Segments).Text);
        Assert.Equal([TimeSpan.FromSeconds(10)], delay.Durations);
    }

    [Fact]
    public async Task SendTimeoutMarksDeliveryUnknownAtTenSeconds()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        MutableClock clock = new();
        FakeDelay delay = new();
        await using LanConnectServerChatClient client = CreateClient(api, () => transport, clock, delay);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");
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
        await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, Assert.Single(client.State.Messages).Delivery);
    }

    [Theory]
    [InlineData("ack", "Confirmed")]
    [InlineData("error", "Failed")]
    public async Task SendAckOrErrorCancelsTimeout(string response, string expected)
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        FakeDelay delay = new();
        await using LanConnectServerChatClient client = CreateClient(api, () => transport, delay: delay);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");

        transport.Emit(response == "ack" ? BuildAck(FixedGuid.ToString("D")) : BuildError(FixedGuid.ToString("D")));

        Assert.True(delay.Tokens.Single().IsCancellationRequested);
        Assert.Equal(expected, Assert.Single(client.State.Messages).Delivery.ToString());
    }

    [Fact]
    public async Task SendRetryInSameSessionReusesClientMessageId()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        int uuidCalls = 0;
        await using LanConnectServerChatClient client = CreateClient(api, () => transport, uuid: () =>
        {
            uuidCalls++;
            return FixedGuid;
        });
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");
        transport.Emit(BuildError(FixedGuid.ToString("D")));

        await client.RetryAsync(FixedGuid.ToString("D"));

        Assert.Equal(1, uuidCalls);
        Assert.Equal(2, transport.SentPayloads.Count);
        Assert.All(transport.SentPayloads, payload => Assert.Contains(FixedGuid.ToString("D"), payload, StringComparison.Ordinal));
        Assert.Equal(ServerChatDeliveryState.Pending, Assert.Single(client.State.Messages).Delivery);
    }

    [Fact]
    public async Task SendStopAndDisconnectAreIdempotentAndPreserveState()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        LanConnectServerChatClient client = CreateClient(api, () => transport);
        await ConnectReadyAsync(client, transport);
        await client.SendTextAsync("hello");

        await client.StopAsync();
        await client.DisconnectAsync();
        await client.StopAsync();

        Assert.Single(client.State.Messages);
        Assert.Equal(1, transport.DisposeCalls);
        Assert.Equal(1, api.DisposeCalls);
        await client.DisposeAsync();
    }

    [Theory]
    [InlineData(0.0, 0.8, 1.6, 3.2, 6.4, 12.0)]
    [InlineData(0.5, 1.0, 2.0, 4.0, 8.0, 15.0)]
    [InlineData(1.0, 1.2, 2.4, 4.8, 9.6, 18.0)]
    public async Task ReconnectUsesJitteredBackoffAndFreshProbeTicketTransport(
        double random,
        double first,
        double second,
        double third,
        double fourth,
        double fifth)
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        await using LanConnectServerChatClient client = CreateReconnectClient(apis, transports, delay, random);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);

        double[] expected = [first, second, third, fourth, fifth];
        for (int index = 0; index < expected.Length; index++)
        {
            transports[index].EmitClosed();
            await WaitUntilAsync(() => delay.Durations.Count == index + 1);
            Assert.Equal(TimeSpan.FromSeconds(expected[index]), delay.Durations[index]);
            delay.CompleteNext();
            await WaitUntilAsync(() => transports.Count == index + 2);
        }

        Assert.Equal(6, apis.Count);
        Assert.All(apis, api => Assert.Equal(1, api.TicketCalls));
        Assert.All(transports, transport => Assert.Equal("Bearer one-time-secret", transport.Headers!["Authorization"]));
        Assert.All(apis.Take(5), api => Assert.Equal(1, api.DisposeCalls));
        Assert.All(transports.Take(5), transport => Assert.Equal(1, transport.DisposeCalls));
    }

    [Fact]
    public async Task ReconnectReadySnapshotResetsBackoffToOneSecond()
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        await using LanConnectServerChatClient client = CreateReconnectClient(apis, transports, delay, random: 0.5);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);

        transports[0].EmitClosed();
        await WaitUntilAsync(() => delay.Durations.Count == 1);
        delay.CompleteNext();
        await WaitUntilAsync(() => transports.Count == 2);
        ConnectReady(transports[1]);
        transports[1].EmitClosed();
        await WaitUntilAsync(() => delay.Durations.Count == 2);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], delay.Durations);
    }

    [Fact]
    public async Task ReconnectStopCancelsPendingDelayMarksPendingUnknownAndPreservesContext()
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        LanConnectServerChatClient client = CreateReconnectClient(apis, transports, delay, random: 0.5);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);
        await client.SendTextAsync("keep me");
        transports[0].EmitClosed();
        await WaitUntilAsync(() => delay.Durations.Any(duration => duration == TimeSpan.FromSeconds(1)));

        await client.StopAsync();

        Assert.True(delay.Tokens.Last().IsCancellationRequested);
        ServerChatMessageState message = Assert.Single(client.State.Messages);
        Assert.Equal("keep me", message.Text);
        Assert.Equal(ServerChatDeliveryState.DeliveryUnknown, message.Delivery);
        Assert.Single(transports);
        await client.DisposeAsync();
    }

    [Theory]
    [InlineData("error")]
    [InlineData("fault")]
    public async Task ReconnectProtocolMismatchPermanentlyStopsWithoutRetry(string source)
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        await using LanConnectServerChatClient client = CreateReconnectClient(apis, transports, delay, random: 0.5);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);

        if (source == "error")
        {
            transports[0].Emit(Serialize(new ServerChatErrorEnvelope
            {
                ProtocolVersion = 1,
                ClientMessageId = "client-1",
                Code = "protocol_mismatch",
                Message = "upgrade required"
            }));
        }
        else
        {
            transports[0].EmitFaulted(new NotSupportedException("protocol mismatch"));
        }

        await Task.Yield();
        Assert.True(client.IsPermanentlyStopped);
        Assert.Empty(delay.Durations);
        Assert.Single(transports);
    }

    [Fact]
    public async Task ReconnectSnapshotReplacesConfirmedWithoutAutomaticUnknownReplay()
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        await using LanConnectServerChatClient client = CreateReconnectClient(apis, transports, delay, random: 0.5);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);
        transports[0].Emit(BuildMessage("old-1", "old"));

        transports[0].EmitClosed();
        await WaitUntilAsync(() => delay.Durations.Count == 1);
        delay.CompleteNext();
        await WaitUntilAsync(() => transports.Count == 2);
        transports[1].Emit(BuildReady("instance-2", historyEpoch: 2));
        transports[1].Emit(BuildSnapshotBegin(1, "instance-2", historyEpoch: 2));
        transports[1].Emit(BuildSnapshotChunk("fresh-1", "fresh"));
        transports[1].Emit(BuildSnapshotEnd(historyEpoch: 2));

        ServerChatMessageState fresh = Assert.Single(client.State.Messages);
        Assert.Equal("fresh-1", fresh.MessageId);
        Assert.Empty(transports[1].SentPayloads);
    }

    [Fact]
    public async Task RetryUnknownRequiresNewReadySessionAndNewIdAfterDisconnect()
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        Queue<Guid> ids = new([
            FixedGuid,
            Guid.Parse("66666666-6666-4666-8666-666666666666")
        ]);
        await using LanConnectServerChatClient client = CreateReconnectClient(
            apis,
            transports,
            delay,
            random: 0.5,
            uuid: () => ids.Dequeue());
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);
        await client.SendTextAsync("hello");
        transports[0].EmitClosed();
        await WaitUntilAsync(() => delay.Durations.Any(duration => duration == TimeSpan.FromSeconds(1)));
        delay.CompleteNext();
        await WaitUntilAsync(() => transports.Count == 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RetryUnknownAsync(FixedGuid.ToString("D"), startNewSession: false));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RetryUnknownAsync(FixedGuid.ToString("D"), startNewSession: true));
        Assert.Empty(transports[1].SentPayloads);

        ConnectReady(transports[1], instanceId: "instance-2", historyEpoch: 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RetryUnknownAsync(FixedGuid.ToString("D"), startNewSession: false));
        await client.RetryUnknownAsync(FixedGuid.ToString("D"), startNewSession: true);

        ServerChatSendEnvelope retried = JsonSerializer.Deserialize<ServerChatSendEnvelope>(
            Assert.Single(transports[1].SentPayloads), LanConnectJson.Options)!;
        Assert.Equal("66666666-6666-4666-8666-666666666666", retried.ClientMessageId);
        Assert.Contains(client.State.Messages, message => message.ClientMessageId == FixedGuid.ToString("D") && message.Delivery == ServerChatDeliveryState.DeliveryUnknown);
        Assert.Contains(client.State.Messages, message => message.ClientMessageId == retried.ClientMessageId && message.Delivery == ServerChatDeliveryState.Pending);
    }

    [Fact]
    public async Task ReconnectConcurrentCloseAndFaultStartsOnlyOneLoop()
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        await using LanConnectServerChatClient client = CreateReconnectClient(apis, transports, delay, random: 0.5);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);

        transports[0].EmitClosed();
        transports[0].EmitFaulted(new InvalidOperationException("lost"));
        await WaitUntilAsync(() => delay.Durations.Count > 0);

        Assert.Single(delay.Durations);
    }

    [Fact]
    public async Task ReconnectInvalidSnapshotContinuitySchedulesFreshAttempt()
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        await using LanConnectServerChatClient client = CreateReconnectClient(apis, transports, delay, random: 0.5);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        transports[0].Emit(BuildReady());
        transports[0].Emit(BuildSnapshotBegin(totalMessages: 1));
        transports[0].Emit(Serialize(new ServerChatSnapshotChunkEnvelope
        {
            ProtocolVersion = 1,
            SnapshotId = "wrong-snapshot",
            ChunkIndex = 0,
            Messages = [Canonical("fresh-1", "Silent", "fresh")]
        }));

        await WaitUntilAsync(() => delay.Durations.Count == 1);

        Assert.Equal(TimeSpan.FromSeconds(1), delay.Durations[0]);
        Assert.False(client.CanSend);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("fault")]
    public async Task ReconnectHandshakeWindowDisconnectIsNotDropped(string signal)
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        await using LanConnectServerChatClient client = CreateReconnectClient(
            apis,
            transports,
            delay,
            random: 0.5,
            configureTransport: (transport, index) =>
            {
                if (index == 1)
                {
                    transport.DuringConnect = () =>
                    {
                        if (signal == "close")
                        {
                            transport.EmitClosed();
                        }
                        else
                        {
                            transport.EmitFaulted(new InvalidOperationException("handshake lost"));
                        }
                    };
                }
            });
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);

        transports[0].EmitClosed();
        await WaitUntilAsync(() => delay.Durations.Count == 1);
        delay.CompleteNext();
        await WaitUntilAsync(() => transports.Count == 2);
        await WaitUntilAsync(() => delay.Durations.Count == 2);
        delay.CompleteNext();
        await WaitUntilAsync(() => transports.Count == 3);

        Assert.Equal(3, apis.Count);
        Assert.All(apis, api => Assert.Equal(1, api.TicketCalls));
    }

    [Fact]
    public async Task ReconnectDisconnectAtIdleHandoffStartsAnotherAttempt()
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        TaskCompletionSource secondAttemptStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int beforeDisposeCalls = 0;
        await using LanConnectServerChatClient client = CreateReconnectClient(
            apis,
            transports,
            delay,
            random: 0.5,
            checkpoint: checkpoint =>
            {
                if (checkpoint == LanConnectServerChatClientCheckpoint.ReconnectBeforeIdleHandoff)
                {
                    transports[1].EmitClosed();
                }
                else if (checkpoint == LanConnectServerChatClientCheckpoint.ReconnectBeforeDispose &&
                         Interlocked.Increment(ref beforeDisposeCalls) == 2)
                {
                    secondAttemptStarted.TrySetResult();
                }
            });
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);

        transports[0].EmitClosed();
        await WaitUntilAsync(() => delay.Durations.Count == 1);
        delay.CompleteNext();
        await secondAttemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, delay.Durations.Count);
    }

    [Fact]
    public async Task ReconnectDuplicateOldConnectionEventsDoNotRetryHealthyReplacement()
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        TaskCompletionSource idleHandoff = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource extraAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int beforeDisposeCalls = 0;
        await using LanConnectServerChatClient client = CreateReconnectClient(
            apis,
            transports,
            delay,
            random: 0.5,
            checkpoint: checkpoint =>
            {
                if (checkpoint == LanConnectServerChatClientCheckpoint.ReconnectBeforeDispose &&
                    Interlocked.Increment(ref beforeDisposeCalls) == 1)
                {
                    transports[0].EmitClosed();
                    transports[0].EmitFaulted(new InvalidOperationException("duplicate close/fault"));
                }
                else if (checkpoint == LanConnectServerChatClientCheckpoint.ReconnectBeforeDispose)
                {
                    extraAttempt.TrySetResult();
                }
                else if (checkpoint == LanConnectServerChatClientCheckpoint.ReconnectBeforeIdleHandoff)
                {
                    idleHandoff.TrySetResult();
                }
            });
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);

        transports[0].EmitClosed();
        await WaitUntilAsync(() => delay.Durations.Count == 1);
        delay.CompleteNext();
        Task completed = await Task.WhenAny(idleHandoff.Task, extraAttempt.Task)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Same(idleHandoff.Task, completed);
        Assert.Single(delay.Durations);
        Assert.Equal(2, transports.Count);
    }

    [Fact]
    public async Task ConnectChatStateDynamicallyUpdatesCanSendAndPublishesRevisionChanges()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        await using LanConnectServerChatClient client = CreateClient(api, () => transport);
        await ConnectReadyAsync(client, transport);
        List<bool> observedCanSend = [];
        client.StateChanged += () => observedCanSend.Add(client.CanSend);

        transport.Emit(Serialize(new ServerChatStateEnvelope
        {
            ProtocolVersion = 1,
            ChatEnabled = false,
            HistoryEpoch = 1,
            EnabledFeatures = new ServerChatEnabledFeatures()
        }));
        transport.Emit(Serialize(new ServerChatStateEnvelope
        {
            ProtocolVersion = 1,
            ChatEnabled = true,
            HistoryEpoch = 1,
            EnabledFeatures = new ServerChatEnabledFeatures()
        }));

        Assert.Equal([false, true], observedCanSend);
        Assert.True(client.CanSend);
    }

    [Fact]
    public async Task ConnectSnapshotEndStateChangedCallbackObservesReadyCanSend()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        await using LanConnectServerChatClient client = CreateClient(api, () => transport);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        transport.Emit(BuildReady());
        transport.Emit(BuildSnapshotBegin(totalMessages: 0));
        bool? observedCanSend = null;
        client.StateChanged += () => observedCanSend = client.CanSend;

        transport.Emit(BuildSnapshotEnd());

        Assert.True(observedCanSend.HasValue && observedCanSend.Value);
    }

    [Fact]
    public async Task ReconnectIgnoresLateCallbacksCapturedFromOldTransport()
    {
        FakeDelay delay = new();
        List<FakeApi> apis = [];
        List<FakeTransport> transports = [];
        await using LanConnectServerChatClient client = CreateReconnectClient(apis, transports, delay, random: 0.5);
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        ConnectReady(transports[0]);
        FakeTransport.CapturedCallbacks stale = transports[0].CaptureCallbacks();

        transports[0].EmitClosed();
        await WaitUntilAsync(() => delay.Durations.Count == 1);
        delay.CompleteNext();
        await WaitUntilAsync(() => transports.Count == 2);
        ConnectReady(transports[1], instanceId: "instance-2", historyEpoch: 2);
        int delaysBefore = delay.Durations.Count;

        stale.Payload("""{"type":"chat_ready","protocolVersion":2,"channel":"server"}""");
        stale.Closed();
        stale.Faulted(new InvalidOperationException("late fault"));
        await Task.Yield();

        Assert.False(client.IsPermanentlyStopped);
        Assert.True(client.CanSend);
        Assert.Equal(delaysBefore, delay.Durations.Count);
        Assert.Equal(2, transports.Count);
    }

    [Fact]
    public async Task ConnectPermanentProtocolStopDisposesCurrentResourcesAndPublishesDisabledState()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        await using LanConnectServerChatClient client = CreateClient(api, () => transport);
        await ConnectReadyAsync(client, transport);
        List<bool> observedCanSend = [];
        client.StateChanged += () => observedCanSend.Add(client.CanSend);

        transport.Emit("""{"type":"chat_ready","protocolVersion":2,"channel":"server"}""");
        await WaitUntilAsync(() => transport.DisposeCalls == 1 && api.DisposeCalls == 1);

        Assert.True(client.IsPermanentlyStopped);
        Assert.False(client.CanSend);
        Assert.Contains(false, observedCanSend);
    }

    [Fact]
    public async Task PermanentStopPublishesCleanupOwnershipBeforeConcurrentDispose()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        Task? concurrentDispose = null;
        bool? disposeCompletedInsideCheckpoint = null;
        using ManualResetEventSlim cleanupMayDispose = new(false);
        LanConnectServerChatClient? client = null;
        client = CreateClient(
            api,
            () => transport,
            checkpoint: checkpoint =>
            {
                if (checkpoint == LanConnectServerChatClientCheckpoint.PermanentCleanupBeforeDispose)
                {
                    if (!cleanupMayDispose.Wait(TimeSpan.FromSeconds(2)))
                    {
                        throw new TimeoutException("Permanent cleanup ownership was not published.");
                    }
                    return;
                }
                if (checkpoint != LanConnectServerChatClientCheckpoint.PermanentStopAfterCleanupOwnership)
                {
                    return;
                }
                concurrentDispose = client!.DisposeAsync().AsTask();
                disposeCompletedInsideCheckpoint = concurrentDispose.IsCompleted;
                cleanupMayDispose.Set();
            });
        await ConnectReadyAsync(client, transport);

        transport.Emit("""{"type":"chat_ready","protocolVersion":2,"channel":"server"}""");

        Assert.False(disposeCompletedInsideCheckpoint);
        Assert.NotNull(concurrentDispose);
        await concurrentDispose.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, transport.DisposeCalls);
        Assert.Equal(1, api.DisposeCalls);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task ValidatedPermanentPayloadDoesNothingAfterConcurrentDisposeOwnsLifecycle()
    {
        FakeApi api = new([]);
        FakeTransport transport = new([]);
        TaskCompletionSource payloadValidated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim releasePayload = new(false);
        int checkpointArmed = 0;
        LanConnectServerChatClient client = CreateClient(
            api,
            () => transport,
            checkpoint: checkpoint =>
            {
                if (checkpoint == LanConnectServerChatClientCheckpoint.PayloadAfterCurrentConnectionValidation &&
                    Volatile.Read(ref checkpointArmed) != 0)
                {
                    payloadValidated.TrySetResult();
                    releasePayload.Wait();
                }
            });
        await ConnectReadyAsync(client, transport);
        Volatile.Write(ref checkpointArmed, 1);
        Task callback = Task.Run(() =>
            transport.Emit("""{"type":"chat_ready","protocolVersion":2,"channel":"server"}"""));
        await payloadValidated.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            await client.DisposeAsync();
        }
        finally
        {
            releasePayload.Set();
        }
        Exception? callbackException = await Record.ExceptionAsync(async () => await callback);

        Assert.Null(callbackException);
        Assert.Equal(1, transport.DisposeCalls);
        Assert.Equal(1, api.DisposeCalls);
        await client.DisposeAsync();
    }

    private static LanConnectServerChatClient CreateClient(
        FakeApi api,
        Func<ILanConnectServerChatTransport> transportFactory,
        MutableClock? clock = null,
        FakeDelay? delay = null,
        Func<Guid>? uuid = null,
        Action<LanConnectServerChatClientCheckpoint>? checkpoint = null) =>
        new(
            _ => api,
            () =>
            {
                api.Operations.Add("transport");
                return transportFactory();
            },
            clock: () => (clock ?? MutableClock.Default).Now,
            delay: (delay ?? FakeDelay.Never).DelayAsync,
            random: () => 0.5,
            uuidFactory: uuid ?? (() => FixedGuid),
            checkpoint: checkpoint);

    private static LanConnectServerChatClient CreateReconnectClient(
        List<FakeApi> apis,
        List<FakeTransport> transports,
        FakeDelay delay,
        double random,
        Func<Guid>? uuid = null,
        Action<FakeTransport, int>? configureTransport = null,
        Action<LanConnectServerChatClientCheckpoint>? checkpoint = null) =>
        new(
            _ =>
            {
                FakeApi api = new([]);
                apis.Add(api);
                return api;
            },
            () =>
            {
                FakeTransport transport = new([]);
                transports.Add(transport);
                configureTransport?.Invoke(transport, transports.Count - 1);
                return transport;
            },
            clock: () => MutableClock.Default.Now,
            delay: delay.DelayAsync,
            random: () => random,
            uuidFactory: uuid ?? (() => FixedGuid),
            checkpoint: checkpoint);

    private static async Task ConnectReadyAsync(LanConnectServerChatClient client, FakeTransport transport)
    {
        await client.ConnectAsync(BaseUri, "net-1", "Ironclad", CancellationToken.None);
        transport.Emit(BuildReady());
        transport.Emit(BuildSnapshotBegin(totalMessages: 0));
        transport.Emit(BuildSnapshotEnd());
        Assert.True(client.CanSend);
    }

    private static void ConnectReady(FakeTransport transport, string instanceId = "instance-1", int historyEpoch = 1)
    {
        transport.Emit(BuildReady(instanceId, historyEpoch));
        transport.Emit(BuildSnapshotBegin(0, instanceId, historyEpoch));
        transport.Emit(BuildSnapshotEnd(historyEpoch));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private static string BuildReady(string instanceId = "instance-1", int historyEpoch = 1) => Serialize(new ServerChatReadyEnvelope
    {
        ProtocolVersion = 1,
        Channel = LanConnectChatChannel.Server,
        InstanceId = instanceId,
        HistoryEpoch = historyEpoch,
        ChatEnabled = true,
        ServerChatVersion = 1
    });

    private static string BuildSnapshotBegin(int totalMessages, string instanceId = "instance-1", int historyEpoch = 1) => Serialize(new ServerChatSnapshotBeginEnvelope
    {
        ProtocolVersion = 1,
        SnapshotId = "snapshot-1",
        InstanceId = instanceId,
        HistoryEpoch = historyEpoch,
        TotalMessages = totalMessages
    });

    private static string BuildSnapshotEnd(int historyEpoch = 1) => Serialize(new ServerChatSnapshotEndEnvelope
    {
        ProtocolVersion = 1,
        SnapshotId = "snapshot-1",
        HistoryEpoch = historyEpoch
    });

    private static string BuildSnapshotChunk(string id, string text) => Serialize(new ServerChatSnapshotChunkEnvelope
    {
        ProtocolVersion = 1,
        SnapshotId = "snapshot-1",
        ChunkIndex = 0,
        Messages = [Canonical(id, "Silent", text)]
    });

    private static string BuildMessage(string id, string text) => Serialize(new ServerChatMessageEnvelope
    {
        ProtocolVersion = 1,
        Message = Canonical(id, "Silent", text)
    });

    private static string BuildAck(string id) => Serialize(new ServerChatAckEnvelope
    {
        ProtocolVersion = 1,
        ClientMessageId = id,
        Message = Canonical("server-1", "Ironclad", "hello")
    });

    private static string BuildError(string id) => Serialize(new ServerChatErrorEnvelope
    {
        ProtocolVersion = 1,
        ClientMessageId = id,
        Code = "rate_limited",
        Message = "slow down"
    });

    private static ServerChatCanonicalMessage Canonical(string id, string senderName, string text) => new()
    {
        MessageId = id,
        SenderId = "sender-1",
        SenderName = senderName,
        Content = new ServerChatContent
        {
            Segments = [new ServerChatTextSegment { Text = text }]
        },
        PlainTextFallback = text,
        SentAt = DateTimeOffset.Parse("2026-07-13T04:05:06.123Z")
    };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, LanConnectJson.Options);

    private sealed class FakeApi(List<string> operations) : ILanConnectServerChatApi
    {
        public List<string> Operations { get; } = operations;
        public int ProbeVersion { get; set; } = 1;
        public int TicketCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public ServerChatTicketRequest? Request { get; private set; }

        public Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken)
        {
            Operations.Add("probe");
            return Task.FromResult(new LobbyProbeResponse
            {
                Ok = true,
                Capabilities = new LobbyProbeCapabilities { ServerChatVersion = ProbeVersion }
            });
        }

        public Task<ServerChatTicketResponse> CreateServerChatTicketAsync(ServerChatTicketRequest request, CancellationToken cancellationToken)
        {
            Operations.Add("ticket");
            TicketCalls++;
            Request = request;
            return Task.FromResult(new ServerChatTicketResponse
            {
                Ticket = "one-time-secret",
                WebSocketUrl = ChatUri.AbsoluteUri,
                ProtocolVersion = 1
            });
        }

        public void Dispose() => DisposeCalls++;
    }

    private sealed class FakeTransport(List<string> operations) : ILanConnectServerChatTransport
    {
        public event Action<string>? PayloadReceived;
        public event Action<Exception>? Faulted;
        public event Action? Closed;
        public Uri? ConnectedUri { get; private set; }
        public IReadOnlyDictionary<string, string>? Headers { get; private set; }
        public List<string> SentPayloads { get; } = [];
        public Action<string>? BeforeSend { get; set; }
        public Action? DuringConnect { get; set; }
        public int DisposeCalls { get; private set; }

        public Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string>? requestHeaders, CancellationToken connectCancellationToken, CancellationToken receiveLifetimeCancellationToken)
        {
            operations.Add("connect");
            ConnectedUri = uri;
            Headers = requestHeaders;
            DuringConnect?.Invoke();
            return Task.CompletedTask;
        }

        public Task SendAsync(string payload, CancellationToken cancellationToken)
        {
            BeforeSend?.Invoke(payload);
            SentPayloads.Add(payload);
            return Task.CompletedTask;
        }

        public void Emit(string payload) => PayloadReceived?.Invoke(payload);

        public void EmitFaulted(Exception exception) => Faulted?.Invoke(exception);

        public void EmitClosed() => Closed?.Invoke();

        public CapturedCallbacks CaptureCallbacks() => new(
            PayloadReceived ?? (_ => { }),
            Faulted ?? (_ => { }),
            Closed ?? (() => { }));

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }

        public readonly record struct CapturedCallbacks(
            Action<string> Payload,
            Action<Exception> Faulted,
            Action Closed);
    }

    private sealed class MutableClock
    {
        public static MutableClock Default { get; } = new();
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-07-13T04:05:06.123Z");
    }

    private sealed class FakeDelay
    {
        public static FakeDelay Never { get; } = new();
        private readonly Queue<TaskCompletionSource> _pending = new();
        public List<TimeSpan> Durations { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _pending.Enqueue(completion);
            Tokens.Add(cancellationToken);
            Durations.Add(duration);
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
}
