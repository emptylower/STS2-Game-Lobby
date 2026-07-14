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
        public void Persist(string baseUrl)
        {
            calls.Add($"persist:{baseUrl}");
        }
    }
}
