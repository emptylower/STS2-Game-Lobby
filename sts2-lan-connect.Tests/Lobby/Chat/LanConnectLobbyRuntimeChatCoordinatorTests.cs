using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectLobbyRuntimeChatCoordinatorTests
{
    [Fact]
    public void RuntimeChatRejectsAccessBeforeReadyInitialization()
    {
        LanConnectLobbyRuntime runtime =
            (LanConnectLobbyRuntime)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(LanConnectLobbyRuntime));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => runtime.Chat);

        Assert.Contains("before the lobby runtime is ready", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorOwnsClientServerStateAndFreshRoomState()
    {
        FakeServerChatClient client = new();

        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);

        Assert.Same(client.State, coordinator.State.Server);
        Assert.Equal(LanConnectChatChannel.Room, coordinator.State.Room.Channel);
        Assert.Null(coordinator.State.ActiveRoomId);
    }

    [Fact]
    public void RealServerClientImplementsRuntimePort()
    {
        Assert.Contains(
            typeof(ILanConnectServerChatClient),
            typeof(LanConnectServerChatClient).GetInterfaces());
    }

    [Fact]
    public void ClientStateChangeIsForwardedWithExactRevision()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        List<long> revisions = new();
        coordinator.StateChanged += () => revisions.Add(coordinator.State.Server.Revision);

        client.State.AppendConfirmedForTests("server-1", "A", "hello", 1, false);
        client.RaiseStateChanged();

        Assert.Equal(new[] { client.State.Revision }, revisions);
    }

    [Fact]
    public async Task ServerOperationsDelegateWithoutReplacingState()
    {
        FakeServerChatClient client = new();
        await using LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        Uri uri = new("https://lobby.example/");
        using CancellationTokenSource cancellation = new();

        await coordinator.ConnectServerAsync(uri, "net-id", "Player", cancellation.Token);
        await coordinator.SendServerAsync("hello", cancellation.Token);
        await coordinator.RetryServerAsync("message-id", cancellation.Token);
        await coordinator.StopServerAsync(cancellation.Token);

        Assert.Equal((uri, "net-id", "Player", cancellation.Token), client.ConnectCall);
        Assert.Equal(("hello", cancellation.Token), client.SendCall);
        Assert.Equal(("message-id", cancellation.Token), client.RetryCall);
        Assert.Equal(cancellation.Token, client.StopToken);
        Assert.Same(client.State, coordinator.State.Server);
    }

    [Fact]
    public void EnterAndLeaveRoomPreserveServerContextAndNotifyRoomRevision()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        client.State.SetDraft("server draft");
        client.State.AppendConfirmedForTests("server-1", "A", "server", 1, false);
        List<long> roomRevisions = new();
        coordinator.StateChanged += () => roomRevisions.Add(coordinator.State.Room.Revision);

        coordinator.EnterRoom("room-a");
        coordinator.State.Room.SetDraft("room draft");
        coordinator.LeaveRoom();

        Assert.Null(coordinator.State.ActiveRoomId);
        Assert.Equal(string.Empty, coordinator.State.Room.Draft);
        Assert.Equal("server draft", client.State.Draft);
        Assert.Single(client.State.Messages);
        Assert.Equal(2, roomRevisions.Count);
        Assert.True(roomRevisions[1] > roomRevisions[0]);
    }

    [Fact]
    public void RepeatedRoomLifecycleCallsAreIdempotentAndOnlyNotifyOnMutation()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        int notifications = 0;
        coordinator.StateChanged += () => notifications++;

        coordinator.EnterRoom("room-a");
        coordinator.EnterRoom("room-a");
        coordinator.LeaveRoom();
        coordinator.LeaveRoom();

        Assert.Equal(2, notifications);
    }

    [Fact]
    public void RoomSendMovesPendingToConfirmedAndRaisesExactRevisions()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        coordinator.EnterRoom("room-a");
        List<long> revisions = new();
        coordinator.StateChanged += () => revisions.Add(coordinator.State.Room.Revision);

        coordinator.BeginRoomPending("local-1", "Player", "hello");
        coordinator.ConfirmRoomSend("local-1");

        ServerChatMessageState message = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
        Assert.True(message.IsLocal);
        Assert.Equal(new[] { revisions[0], revisions[0] + 1 }, revisions);
    }

    [Fact]
    public void RoomSendFailureIsRetainedForDisplay()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        coordinator.EnterRoom("room-a");

        coordinator.BeginRoomPending("local-1", "Player", "hello");
        coordinator.FailRoomSend("local-1", "send_failed", "offline");

        ServerChatMessageState message = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(ServerChatDeliveryState.Failed, message.Delivery);
        Assert.Equal("send_failed", message.ErrorCode);
        Assert.Equal("offline", message.ErrorMessage);
    }

    [Fact]
    public void RemoteRoomMessagesAreScopedAndDeduplicatedByMessageId()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        coordinator.EnterRoom("room-a");

        coordinator.AppendRoomConfirmed("stale-room", "remote-1", "A", "stale", false);
        coordinator.AppendRoomConfirmed("room-a", "remote-1", "A", "hello", false);
        coordinator.AppendRoomConfirmed("room-a", "remote-1", "A", "duplicate", false);

        ServerChatMessageState message = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal("hello", message.Text);
        Assert.False(message.IsLocal);
    }

    [Fact]
    public void LegacyEchoIsDeduplicatedAgainstConfirmedLocalClientMessageId()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        coordinator.EnterRoom("room-a");
        coordinator.BeginRoomPending("local-1", "Player", "hello");
        coordinator.ConfirmRoomSend("local-1");

        coordinator.AppendRoomConfirmed("room-a", "local-1", "Player", "hello", false);

        ServerChatMessageState message = Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(ServerChatDeliveryState.Confirmed, message.Delivery);
        Assert.True(message.IsLocal);
    }

    [Fact]
    public void HostedRoomEnvelopePreservesHostIdentity()
    {
        DateTimeOffset sentAt = DateTimeOffset.FromUnixTimeMilliseconds(1234);

        LobbyControlEnvelope envelope = LanConnectLobbyRuntime.CreateHostedRoomChatEnvelope(
            "room-a", "control-a", "Host", "net-host", "message-a", "hello", sentAt);

        Assert.Equal("room_chat", envelope.Type);
        Assert.Equal("room-a", envelope.RoomId);
        Assert.Equal("control-a", envelope.ControlChannelId);
        Assert.Equal("host", envelope.Role);
        Assert.Null(envelope.TicketId);
        Assert.Equal("Host", envelope.PlayerName);
        Assert.Equal("net-host", envelope.PlayerNetId);
        Assert.Equal("message-a", envelope.MessageId);
        Assert.Equal("hello", envelope.MessageText);
        Assert.Equal(1234, envelope.SentAtUnixMs);
    }

    [Fact]
    public void JoinedRoomEnvelopePreservesClientTicketAndIdentity()
    {
        DateTimeOffset sentAt = DateTimeOffset.FromUnixTimeMilliseconds(5678);

        LobbyControlEnvelope envelope = LanConnectLobbyRuntime.CreateJoinedRoomChatEnvelope(
            "room-a", "control-a", "ticket-a", "Client", "net-client", "message-a", "hello", sentAt);

        Assert.Equal("room_chat", envelope.Type);
        Assert.Equal("room-a", envelope.RoomId);
        Assert.Equal("control-a", envelope.ControlChannelId);
        Assert.Equal("client", envelope.Role);
        Assert.Equal("ticket-a", envelope.TicketId);
        Assert.Equal("Client", envelope.PlayerName);
        Assert.Equal("net-client", envelope.PlayerNetId);
        Assert.Equal("message-a", envelope.MessageId);
        Assert.Equal("hello", envelope.MessageText);
        Assert.Equal(5678, envelope.SentAtUnixMs);
    }

    [Fact]
    public async Task DisposeUnsubscribesAndDisposesClientExactlyOnce()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        int notifications = 0;
        coordinator.StateChanged += () => notifications++;

        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();
        client.State.AppendConfirmedForTests("server-after", "A", "ignored", 2, false);
        client.RaiseStateChanged();

        Assert.Equal(1, client.DisposeCount);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task ConcurrentStopAndDisposeCompleteWithoutDuplicateDispose()
    {
        FakeServerChatClient client = new();
        TaskCompletionSource stopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StopImplementation = async _ =>
        {
            stopEntered.SetResult();
            await releaseStop.Task;
        };
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);

        Task stop = coordinator.StopServerAsync();
        await stopEntered.Task;
        Task dispose = coordinator.DisposeAsync().AsTask();
        releaseStop.SetResult();
        await Task.WhenAll(stop, dispose);

        Assert.Equal(1, client.StopCount);
        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task OperationsAfterDisposeAreRejected()
    {
        FakeServerChatClient client = new();
        LanConnectLobbyRuntimeChatCoordinator coordinator = new(client);
        await coordinator.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.ConnectServerAsync(new Uri("https://lobby.example"), "id", "name"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.SendServerAsync("text"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.RetryServerAsync("id"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.StopServerAsync());
        Assert.Throws<ObjectDisposedException>(() => coordinator.EnterRoom("room"));
        Assert.Throws<ObjectDisposedException>(() => coordinator.LeaveRoom());
    }

    private sealed class FakeServerChatClient : ILanConnectServerChatClient
    {
        internal FakeServerChatClient()
        {
            State = new LanConnectChatChannelState(LanConnectChatChannel.Server);
        }

        public LanConnectChatChannelState State { get; }

        public event Action? StateChanged;

        internal (Uri Uri, string PlayerNetId, string PlayerName, CancellationToken Token)? ConnectCall { get; private set; }

        internal (string Text, CancellationToken Token)? SendCall { get; private set; }

        internal (string MessageId, CancellationToken Token)? RetryCall { get; private set; }

        internal CancellationToken? StopToken { get; private set; }

        internal int StopCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal Func<CancellationToken, Task>? StopImplementation { get; set; }

        public Task ConnectAsync(Uri lobbyBaseUri, string playerNetId, string playerName, CancellationToken cancellationToken = default)
        {
            ConnectCall = (lobbyBaseUri, playerNetId, playerName, cancellationToken);
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            SendCall = (text, cancellationToken);
            return Task.CompletedTask;
        }

        public Task RetryAsync(string clientMessageId, CancellationToken cancellationToken = default)
        {
            RetryCall = (clientMessageId, cancellationToken);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            StopToken = cancellationToken;
            return StopImplementation?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        internal void RaiseStateChanged() => StateChanged?.Invoke();
    }
}
