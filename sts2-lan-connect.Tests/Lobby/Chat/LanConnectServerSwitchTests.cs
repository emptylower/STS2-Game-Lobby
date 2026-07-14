using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectServerSwitchTests
{
    [Fact]
    public async Task SwitchOrdersLeaveStopClearPersistAndConnect()
    {
        List<string> calls = new();
        FakeRoomLifecycle room = new(calls) { HasActiveRoom = true };
        FakeSwitchChat chat = new(calls);
        FakeAddressStore store = new(calls);
        LanConnectServerSwitchCoordinator sut = new(room, chat, store);

        await sut.SwitchAsync("https://new.example/path?ignored=1", "p1", "Silent");

        Assert.Equal(new[]
        {
            "leave-room",
            "stop-chat",
            "clear-server",
            "persist:https://new.example",
            "connect:https://new.example/:p1:Silent"
        }, calls);
    }

    [Fact]
    public async Task SwitchWithoutActiveRoomSkipsLeave()
    {
        List<string> calls = new();
        FakeRoomLifecycle room = new(calls);
        FakeSwitchChat chat = new(calls);
        LanConnectServerSwitchCoordinator sut = new(room, chat, new FakeAddressStore(calls));

        await sut.SwitchAsync("http://localhost:8787", "p1", "Silent");

        Assert.DoesNotContain("leave-room", calls);
        Assert.Equal("connect:http://localhost:8787/:p1:Silent", calls[^1]);
    }

    [Fact]
    public async Task LatestSwitchCancelsEarlierSwitchBeforePersistOrConnect()
    {
        List<string> calls = new();
        FakeRoomLifecycle room = new(calls);
        FakeSwitchChat chat = new(calls);
        FakeAddressStore store = new(calls);
        TaskCompletionSource firstStopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int stopCalls = 0;
        chat.StopImplementation = async token =>
        {
            if (Interlocked.Increment(ref stopCalls) == 1)
            {
                firstStopEntered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        LanConnectServerSwitchCoordinator sut = new(room, chat, store);

        Task first = sut.SwitchAsync("https://one.example", "p1", "Silent");
        await firstStopEntered.Task;
        Task second = sut.SwitchAsync("https://two.example", "p1", "Silent");
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.DoesNotContain("persist:https://one.example", calls);
        Assert.DoesNotContain(calls, call => call.StartsWith("connect:https://one.example", StringComparison.Ordinal));
        Assert.Contains("persist:https://two.example", calls);
        Assert.Contains("connect:https://two.example/:p1:Silent", calls);
    }

    [Fact]
    public async Task SupersededSwitchReturnsCanceledContextWhileLatestContextStaysCurrent()
    {
        List<string> calls = new();
        FakeSwitchChat chat = new(calls);
        TaskCompletionSource firstStopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int stopCalls = 0;
        chat.StopImplementation = async _ =>
        {
            if (Interlocked.Increment(ref stopCalls) == 1)
            {
                firstStopEntered.SetResult();
                await releaseFirstStop.Task;
            }
        };
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            chat,
            new FakeAddressStore(calls));

        Task<CancellationToken> first = sut.SwitchWithContextAsync(
            "https://one.example", "p1", "Silent");
        await firstStopEntered.Task;
        Task<CancellationToken> second = sut.SwitchWithContextAsync(
            "https://two.example", "p1", "Silent");
        releaseFirstStop.SetResult();

        CancellationToken[] contexts = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(contexts[0].IsCancellationRequested);
        Assert.False(contexts[1].IsCancellationRequested);
        Assert.Equal(contexts[1], sut.CurrentServerContextToken);
        Assert.False(sut.IsSwitchInProgress);
        await sut.DisposeAsync();
        Assert.True(contexts[1].IsCancellationRequested);
    }

    [Fact]
    public async Task SwitchRegistrationCancelsPreviousServerContextAndReportsInProgress()
    {
        List<string> calls = new();
        FakeSwitchChat chat = new(calls);
        TaskCompletionSource stopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        chat.StopImplementation = async _ =>
        {
            stopEntered.SetResult();
            await releaseStop.Task;
        };
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            chat,
            new FakeAddressStore(calls));
        CancellationToken previousContext = sut.CurrentServerContextToken;

        Task<CancellationToken> switching = sut.SwitchWithContextAsync(
            "https://new.example", "p1", "Silent");
        await stopEntered.Task;

        Assert.True(previousContext.IsCancellationRequested);
        Assert.True(sut.IsSwitchInProgress);
        releaseStop.SetResult();
        CancellationToken currentContext = await switching.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(currentContext.IsCancellationRequested);
        Assert.False(sut.IsSwitchInProgress);
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task PersistCommitIsAtomicWithGenerationRegistration()
    {
        List<string> calls = new();
        FakeAddressStore store = new(calls);
        TaskCompletionSource firstPersistEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstPersist = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondReachedRegistration = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstPersistToken = default;
        int persistCalls = 0;
        store.PersistImplementation = async token =>
        {
            if (Interlocked.Increment(ref persistCalls) == 1)
            {
                firstPersistToken = token;
                firstPersistEntered.SetResult();
                await releaseFirstPersist.Task;
            }
        };
        int registrationCalls = 0;
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            new FakeSwitchChat(calls),
            store,
            checkpoint =>
            {
                if (checkpoint == LanConnectServerSwitchCoordinatorCheckpoint.BeforeGenerationRegistration &&
                    Interlocked.Increment(ref registrationCalls) == 2)
                {
                    secondReachedRegistration.SetResult();
                }
            });

        Task first = Task.Run(() => sut.SwitchAsync("https://one.example", "p1", "Silent"));
        await firstPersistEntered.Task;
        Task second = Task.Run(() => sut.SwitchAsync("https://two.example", "p1", "Silent"));
        await secondReachedRegistration.Task;
        Assert.False(firstPersistToken.IsCancellationRequested);
        releaseFirstPersist.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));

        int firstPersist = calls.IndexOf("persist:https://one.example");
        int secondPersist = calls.IndexOf("persist:https://two.example");
        Assert.True(firstPersist >= 0);
        Assert.True(secondPersist > firstPersist);
    }

    [Fact]
    public async Task SupersededSwitchCanFinishNonCooperativeStopWithoutDisposedTokenFailure()
    {
        List<string> calls = new();
        FakeSwitchChat chat = new(calls);
        TaskCompletionSource firstStopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int stopCalls = 0;
        chat.StopImplementation = async _ =>
        {
            if (Interlocked.Increment(ref stopCalls) == 1)
            {
                firstStopEntered.SetResult();
                await releaseFirstStop.Task;
            }
        };
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            chat,
            new FakeAddressStore(calls));

        Task first = sut.SwitchAsync("https://one.example", "p1", "Silent");
        await firstStopEntered.Task;
        Task second = sut.SwitchAsync("https://two.example", "p1", "Silent");
        releaseFirstStop.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.DoesNotContain("persist:https://one.example", calls);
        Assert.Contains("persist:https://two.example", calls);
        Assert.Contains("connect:https://two.example/:p1:Silent", calls);
    }

    [Fact]
    public async Task RotatingChatPortReplacesPermanentlyStoppedClientBeforeConnect()
    {
        FakeServerChatClient initial = new();
        FakeServerChatClient replacement = new();
        await using LanConnectRotatingServerChatPort port = new(initial, () => replacement);
        int notifications = 0;
        port.StateChanged += () => notifications++;

        await port.StopAsync(CancellationToken.None);
        port.ClearForContextChange();
        await port.ConnectAsync(new Uri("https://new.example/"), "p1", "Silent", CancellationToken.None);

        Assert.Equal(1, initial.StopCount);
        Assert.Equal(1, initial.DisposeCount);
        Assert.Null(initial.ConnectCall);
        Assert.Equal(new Uri("https://new.example/"), replacement.ConnectCall?.Uri);
        Assert.Equal("p1", replacement.ConnectCall?.PlayerNetId);
        Assert.Same(replacement.State, port.Current.State.Server);
        int notificationsAfterReplacement = notifications;
        replacement.RaiseStateChanged();
        Assert.Equal(notificationsAfterReplacement + 1, notifications);
    }

    [Fact]
    public async Task RotatingChatPortRemainsReplaceableWhenConnectIsCanceledAfterOldDispose()
    {
        FakeServerChatClient initial = new();
        FakeServerChatClient canceledReplacement = new();
        FakeServerChatClient finalReplacement = new();
        Queue<FakeServerChatClient> replacements = new(new[] { canceledReplacement, finalReplacement });
        await using LanConnectRotatingServerChatPort port = new(initial, () => replacements.Dequeue());
        await port.StopAsync(CancellationToken.None);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.ConnectAsync(
            new Uri("https://canceled.example/"), "p1", "Silent", canceled.Token));

        await port.StopAsync(CancellationToken.None);
        port.ClearForContextChange();
        await port.ConnectAsync(
            new Uri("https://final.example/"), "p1", "Silent", CancellationToken.None);
        Assert.Equal(1, canceledReplacement.StopCount);
        Assert.Equal(1, canceledReplacement.DisposeCount);
        Assert.Equal(new Uri("https://final.example/"), finalReplacement.ConnectCall?.Uri);
    }

    [Fact]
    public async Task RotatingChatPortKeepsCurrentWhenReplacementConstructionFails()
    {
        FakeServerChatClient initial = new();
        FakeServerChatClient invalidReplacement = new() { ThrowOnStateChangedSubscribe = true };
        FakeServerChatClient finalReplacement = new();
        Queue<FakeServerChatClient> replacements = new(new[] { invalidReplacement, finalReplacement });
        await using LanConnectRotatingServerChatPort port = new(initial, () => replacements.Dequeue());
        int notifications = 0;
        port.StateChanged += () => notifications++;
        await port.StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ConnectAsync(
            new Uri("https://invalid.example/"), "p1", "Silent", CancellationToken.None));

        Assert.Same(initial.State, port.Current.State.Server);
        Assert.Equal(1, invalidReplacement.DisposeCount);
        initial.RaiseStateChanged();
        Assert.Equal(1, notifications);
        await port.ConnectAsync(
            new Uri("https://final.example/"), "p1", "Silent", CancellationToken.None);
        Assert.Equal(new Uri("https://final.example/"), finalReplacement.ConnectCall?.Uri);
    }

    [Fact]
    public async Task RotatingChatPortRecoversWhenPreviousDisposeFailsAfterSwap()
    {
        FakeServerChatClient initial = new()
        {
            DisposeImplementation = () => Task.FromException(new InvalidOperationException("dispose failed"))
        };
        FakeServerChatClient firstReplacement = new();
        FakeServerChatClient finalReplacement = new();
        Queue<FakeServerChatClient> replacements = new(new[] { firstReplacement, finalReplacement });
        await using LanConnectRotatingServerChatPort port = new(initial, () => replacements.Dequeue());
        await port.StopAsync(CancellationToken.None);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            port.ConnectAsync(new Uri("https://first.example/"), "p1", "Silent", CancellationToken.None));

        Assert.Equal("dispose failed", error.Message);
        Assert.Same(firstReplacement.State, port.Current.State.Server);
        await port.StopAsync(CancellationToken.None);
        await port.ConnectAsync(
            new Uri("https://final.example/"), "p1", "Silent", CancellationToken.None);
        Assert.Equal(new Uri("https://final.example/"), finalReplacement.ConnectCall?.Uri);
    }

    [Fact]
    public async Task ConcurrentRotatingPortDisposeCallsShareCompletionAndFailure()
    {
        TaskCompletionSource disposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeServerChatClient initial = new()
        {
            DisposeImplementation = async () =>
            {
                disposeEntered.SetResult();
                await releaseDispose.Task;
            }
        };
        LanConnectRotatingServerChatPort port = new(initial, () => new FakeServerChatClient());

        Task first = port.DisposeAsync().AsTask();
        await disposeEntered.Task;
        Task second = port.DisposeAsync().AsTask();

        Assert.Same(first, second);
        Assert.False(second.IsCompleted);
        releaseDispose.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, initial.DisposeCount);
    }

    [Fact]
    public async Task PreCanceledRequestDoesNotSupersedeActiveSwitch()
    {
        List<string> calls = new();
        FakeSwitchChat chat = new(calls);
        TaskCompletionSource firstStopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        chat.StopImplementation = async token =>
        {
            firstToken = token;
            firstStopEntered.SetResult();
            await releaseFirstStop.Task;
        };
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            chat,
            new FakeAddressStore(calls));
        Task first = sut.SwitchAsync("https://one.example", "p1", "Silent");
        await firstStopEntered.Task;
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.SwitchAsync("https://two.example", "p1", "Silent", canceled.Token));

        Assert.False(firstToken.IsCancellationRequested);
        releaseFirstStop.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("persist:https://one.example", calls);
        Assert.Contains("connect:https://one.example/:p1:Silent", calls);
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task DisposeCancelsAndDrainsActiveSwitchAndRejectsLaterRequests()
    {
        List<string> calls = new();
        FakeSwitchChat chat = new(calls);
        TaskCompletionSource stopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        chat.StopImplementation = async token =>
        {
            stopEntered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            chat,
            new FakeAddressStore(calls));
        Task switching = sut.SwitchAsync("https://one.example", "p1", "Silent");
        await stopEntered.Task;

        Task dispose = sut.DisposeAsync().AsTask();

        await Task.WhenAll(switching, dispose).WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.SwitchAsync("https://two.example", "p1", "Silent"));
        Assert.DoesNotContain(calls, call => call.StartsWith("persist:", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, call => call.StartsWith("connect:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisposeWaitsForRegisteredSwitchBeforeItAcquiresGate()
    {
        List<string> calls = new();
        TaskCompletionSource generationRegistered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim releaseRegistration = new(false);
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            new FakeSwitchChat(calls),
            new FakeAddressStore(calls),
            checkpoint =>
            {
                if (checkpoint == LanConnectServerSwitchCoordinatorCheckpoint.AfterGenerationRegistration)
                {
                    generationRegistered.SetResult();
                    releaseRegistration.Wait();
                }
            });
        Task switching = Task.Run(() =>
            sut.SwitchAsync("https://one.example", "p1", "Silent"));
        await generationRegistered.Task;

        Task dispose = sut.DisposeAsync().AsTask();

        Assert.False(dispose.IsCompleted);
        releaseRegistration.Set();
        await Task.WhenAll(switching, dispose).WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.SwitchAsync("https://two.example", "p1", "Silent"));
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("ftp://lobby.example")]
    [InlineData("file:///tmp/lobby")]
    public async Task InvalidServerAddressIsRejectedBeforeSideEffects(string address)
    {
        List<string> calls = new();
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            new FakeSwitchChat(calls),
            new FakeAddressStore(calls));

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SwitchAsync(address, "p1", "Silent"));

        Assert.Empty(calls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesAndPreventsPersistAndConnect()
    {
        List<string> calls = new();
        FakeSwitchChat chat = new(calls)
        {
            StopImplementation = token => Task.Delay(Timeout.InfiniteTimeSpan, token)
        };
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            chat,
            new FakeAddressStore(calls));
        using CancellationTokenSource cancellation = new();

        Task switching = sut.SwitchAsync("https://one.example", "p1", "Silent", cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => switching);
        Assert.DoesNotContain(calls, call => call.StartsWith("persist:", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, call => call.StartsWith("connect:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopFailurePreventsClearPersistAndConnect()
    {
        List<string> calls = new();
        FakeSwitchChat chat = new(calls)
        {
            StopImplementation = _ => Task.FromException(new InvalidOperationException("stop failed"))
        };
        LanConnectServerSwitchCoordinator sut = new(
            new FakeRoomLifecycle(calls),
            chat,
            new FakeAddressStore(calls));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SwitchAsync("https://one.example", "p1", "Silent"));

        Assert.Equal("stop failed", error.Message);
        Assert.Equal(new[] { "stop-chat" }, calls);
    }

    private sealed class FakeRoomLifecycle(List<string> calls) : ILanConnectRoomLifecycle
    {
        public bool HasActiveRoom { get; set; }

        public Task LeaveActiveRoomAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("leave-room");
            HasActiveRoom = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSwitchChat(List<string> calls) : ILanConnectServerSwitchChat
    {
        public Func<CancellationToken, Task>? StopImplementation { get; set; }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            calls.Add("stop-chat");
            return StopImplementation?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public void ClearForContextChange()
        {
            calls.Add("clear-server");
        }

        public Task ConnectAsync(
            Uri lobbyBaseUri,
            string playerNetId,
            string playerName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add($"connect:{lobbyBaseUri}:{playerNetId}:{playerName}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAddressStore(List<string> calls) : ILanConnectServerAddressStore
    {
        public Func<CancellationToken, Task>? PersistImplementation { get; set; }

        public void Persist(string baseUrl, CancellationToken cancellationToken)
        {
            PersistImplementation?.Invoke(cancellationToken).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add($"persist:{baseUrl}");
        }
    }

    private sealed class FakeServerChatClient : ILanConnectServerChatClient
    {
        private Action? _stateChanged;

        public LanConnectChatChannelState State { get; } = new(LanConnectChatChannel.Server);

        public event Action? StateChanged
        {
            add
            {
                if (ThrowOnStateChangedSubscribe)
                {
                    throw new InvalidOperationException("subscribe failed");
                }
                _stateChanged += value;
            }
            remove => _stateChanged -= value;
        }

        internal bool ThrowOnStateChangedSubscribe { get; set; }

        internal Func<Task>? DisposeImplementation { get; set; }

        internal int StopCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal (Uri Uri, string PlayerNetId, string PlayerName)? ConnectCall { get; private set; }

        public Task ConnectAsync(
            Uri lobbyBaseUri,
            string playerNetId,
            string playerName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StopCount != 0)
            {
                throw new InvalidOperationException("A stopped server chat client cannot reconnect.");
            }
            ConnectCall = (lobbyBaseUri, playerNetId, playerName);
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RetryAsync(string clientMessageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (DisposeImplementation != null)
            {
                await DisposeImplementation();
            }
        }

        internal void RaiseStateChanged() => _stateChanged?.Invoke();
    }
}
