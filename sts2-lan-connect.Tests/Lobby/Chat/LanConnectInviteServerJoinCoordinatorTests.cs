using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectInviteServerJoinCoordinatorTests
{
    [Fact]
    public async Task CrossServerInviteSwitchesBeforeLookupAndDoesNotRestoreOldNode()
    {
        FakeInvitePorts ports = new("https://old.example") { RoomExists = false };
        LanConnectInviteServerJoinCoordinator sut = new(ports);

        LanConnectInviteJoinResult result = await sut.JoinAsync(
            Invite("https://new.example/", "missing"),
            CancellationToken.None);

        Assert.Equal(new[]
        {
            "switch:https://new.example",
            "rooms:https://new.example"
        }, ports.Calls);
        Assert.Equal(LanConnectInviteJoinResult.RoomNotFound, result);
        Assert.Equal("https://new.example", ports.CurrentServer);
    }

    [Fact]
    public async Task SameServerInviteNormalizesTrailingSlashAndCaseBeforeComparison()
    {
        FakeInvitePorts ports = new("https://ONE.example/") { RoomExists = true };
        LanConnectInviteServerJoinCoordinator sut = new(ports);

        LanConnectInviteJoinResult result = await sut.JoinAsync(
            Invite("https://one.example", "room-a", "pw"),
            CancellationToken.None);

        Assert.DoesNotContain(ports.Calls, call => call.StartsWith("switch:", StringComparison.Ordinal));
        Assert.Equal(new[]
        {
            "rooms:https://ONE.example/",
            "join:room-a:pw"
        }, ports.Calls);
        Assert.Equal(1, ports.JoinCalls);
        Assert.Equal(LanConnectInviteJoinResult.JoinStarted, result);
    }

    [Fact]
    public async Task RoomIdMatchIsOrdinalAndDoesNotStartWrongJoin()
    {
        FakeInvitePorts ports = new("http://one.example")
        {
            Rooms = [new LobbyRoomSummary { RoomId = "ROOM-A" }]
        };
        LanConnectInviteServerJoinCoordinator sut = new(ports);

        LanConnectInviteJoinResult result = await sut.JoinAsync(
            Invite("http://one.example/", "room-a"),
            CancellationToken.None);

        Assert.Equal(LanConnectInviteJoinResult.RoomNotFound, result);
        Assert.Equal(0, ports.JoinCalls);
    }

    [Fact]
    public async Task ExactRoomStartsJoinOnlyOnce()
    {
        FakeInvitePorts ports = new("https://one.example")
        {
            Rooms =
            [
                new LobbyRoomSummary { RoomId = "room-a" },
                new LobbyRoomSummary { RoomId = "room-a" }
            ]
        };
        LanConnectInviteServerJoinCoordinator sut = new(ports);

        LanConnectInviteJoinResult result = await sut.JoinAsync(
            Invite("https://one.example", "room-a", "secret"),
            CancellationToken.None);

        Assert.Equal(LanConnectInviteJoinResult.JoinStarted, result);
        Assert.Equal(1, ports.JoinCalls);
        Assert.Equal("join:room-a:secret", ports.Calls[^1]);
    }

    [Fact]
    public async Task JoinFailurePropagatesWithoutRestoringOldServer()
    {
        FakeInvitePorts ports = new("https://old.example")
        {
            RoomExists = true,
            JoinException = new InvalidOperationException("join failed")
        };
        LanConnectInviteServerJoinCoordinator sut = new(ports);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.JoinAsync(Invite("https://new.example", "room-a"), CancellationToken.None));

        Assert.Equal("join failed", error.Message);
        Assert.Equal("https://new.example", ports.CurrentServer);
        Assert.Equal(1, ports.JoinCalls);
        Assert.DoesNotContain(ports.Calls, call => call == "switch:https://old.example");
    }

    [Fact]
    public async Task SupersededSwitchContextStopsBeforeRoomLookup()
    {
        using CancellationTokenSource superseded = new();
        superseded.Cancel();
        FakeInvitePorts ports = new("https://old.example")
        {
            SwitchContextToken = superseded.Token,
            RoomExists = true
        };
        LanConnectInviteServerJoinCoordinator sut = new(ports);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.JoinAsync(Invite("https://new.example", "room-a"), CancellationToken.None));

        Assert.Equal(new[] { "switch:https://new.example" }, ports.Calls);
        Assert.Equal(0, ports.JoinCalls);
    }

    [Fact]
    public async Task ContextCanceledDuringLookupDoesNotStartJoin()
    {
        using CancellationTokenSource context = new();
        FakeInvitePorts ports = new("https://one.example")
        {
            CurrentServerContextToken = context.Token,
            RoomExists = true,
            GetRoomsImplementation = _ =>
            {
                context.Cancel();
                return Task.FromResult<IReadOnlyList<LobbyRoomSummary>>(
                    [new LobbyRoomSummary { RoomId = "room-a" }]);
            }
        };
        LanConnectInviteServerJoinCoordinator sut = new(ports);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.JoinAsync(Invite("https://one.example", "room-a"), CancellationToken.None));

        Assert.Equal(0, ports.JoinCalls);
    }

    [Fact]
    public async Task SameServerInviteDoesNotLookupDuringConcurrentSwitch()
    {
        FakeInvitePorts ports = new("https://one.example")
        {
            IsSwitchInProgress = true,
            RoomExists = true
        };
        LanConnectInviteServerJoinCoordinator sut = new(ports);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.JoinAsync(Invite("https://one.example", "room-a"), CancellationToken.None));

        Assert.Empty(ports.Calls);
        Assert.Equal(0, ports.JoinCalls);
    }

    [Theory]
    [InlineData("ftp://one.example")]
    [InlineData("one.example")]
    [InlineData("")]
    public async Task PayloadServerMustBeAbsoluteHttpOrHttps(string server)
    {
        FakeInvitePorts ports = new("https://old.example");
        LanConnectInviteServerJoinCoordinator sut = new(ports);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.JoinAsync(Invite(server, "room-a"), CancellationToken.None));

        Assert.Empty(ports.Calls);
    }

    private static LanConnectInvitePayload Invite(string server, string roomId, string? password = null) =>
        new() { S = server, R = roomId, P = password, V = 1 };

    private sealed class FakeInvitePorts(string currentServer) : ILanConnectInviteServerJoinPorts
    {
        public List<string> Calls { get; } = new();

        public string CurrentServer { get; private set; } = currentServer;

        public CancellationToken CurrentServerContextToken { get; set; }

        public bool IsSwitchInProgress { get; set; }

        public CancellationToken SwitchContextToken { get; set; }

        public bool RoomExists { get; set; }

        public IReadOnlyList<LobbyRoomSummary>? Rooms { get; set; }

        public Exception? JoinException { get; set; }

        public Func<CancellationToken, Task<IReadOnlyList<LobbyRoomSummary>>>? GetRoomsImplementation { get; set; }

        public int JoinCalls { get; private set; }

        public Task<CancellationToken> SwitchServerAsync(string server, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Add($"switch:{server}");
            CurrentServer = server;
            CurrentServerContextToken = SwitchContextToken;
            return Task.FromResult(SwitchContextToken);
        }

        public Task<IReadOnlyList<LobbyRoomSummary>> GetRoomsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Add($"rooms:{CurrentServer}");
            if (GetRoomsImplementation != null)
            {
                return GetRoomsImplementation(token);
            }
            IReadOnlyList<LobbyRoomSummary> rooms = Rooms ?? (RoomExists
                ? [new LobbyRoomSummary { RoomId = "room-a" }]
                : []);
            return Task.FromResult(rooms);
        }

        public Task BeginJoinAsync(LobbyRoomSummary room, string? password, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            JoinCalls++;
            Calls.Add($"join:{room.RoomId}:{password}");
            return JoinException == null ? Task.CompletedTask : Task.FromException(JoinException);
        }
    }
}
