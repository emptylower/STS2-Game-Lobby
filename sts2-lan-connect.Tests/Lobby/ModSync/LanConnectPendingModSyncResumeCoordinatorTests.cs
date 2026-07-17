using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.ModSync;

public sealed class LanConnectPendingModSyncResumeCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lan-connect-resume-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resume_restores_server_then_room_and_repreflights_public_room()
    {
        FakeResumePorts ports = new();
        LanConnectPendingModSyncJoinStore store = Store();
        store.Save("https://saved.example", "room-1", "Room", "slot-2");
        LanConnectPendingModSyncResumeCoordinator sut = new(store, ports);

        LanConnectPendingModSyncResumeOutcome outcome = await sut.ResumeAsync();

        Assert.Equal(LanConnectPendingModSyncResumeOutcome.PublicJoinCompleted, outcome);
        Assert.Equal(["server:https://saved.example/", "rooms", "join:room-1:slot-2"], ports.Calls);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Resume_password_room_prompts_again_without_persisted_password()
    {
        FakeResumePorts ports = new() { RequiresPassword = true };
        LanConnectPendingModSyncJoinStore store = Store();
        store.Save("https://saved.example", "room-1", "Room", null);
        LanConnectPendingModSyncResumeCoordinator sut = new(store, ports);

        LanConnectPendingModSyncResumeOutcome outcome = await sut.ResumeAsync();

        Assert.Equal(LanConnectPendingModSyncResumeOutcome.PasswordPromptCompleted, outcome);
        Assert.Equal(["server:https://saved.example/", "rooms", "password:room-1"], ports.Calls);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Resume_is_single_flight_and_missing_room_is_cleared()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeResumePorts ports = new() { RoomExists = false, RoomsEntered = entered, RoomsRelease = release };
        LanConnectPendingModSyncJoinStore store = Store();
        store.Save("https://saved.example", "missing", "Gone", null);
        LanConnectPendingModSyncResumeCoordinator sut = new(store, ports);

        Task<LanConnectPendingModSyncResumeOutcome> first = sut.ResumeAsync();
        await entered.Task;
        Assert.Equal(LanConnectPendingModSyncResumeOutcome.Busy, await sut.ResumeAsync());
        release.SetResult();

        Assert.Equal(LanConnectPendingModSyncResumeOutcome.RoomMissing, await first);
        Assert.Null(store.Load());
        Assert.Equal(1, ports.Calls.Count(call => call == "rooms"));
    }

    private LanConnectPendingModSyncJoinStore Store() =>
        new(Path.Combine(_directory, "pending.json"), () => _now);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeResumePorts : ILanConnectPendingModSyncResumePorts
    {
        public List<string> Calls { get; } = [];
        public bool RequiresPassword { get; init; }
        public bool RoomExists { get; init; } = true;
        public TaskCompletionSource? RoomsEntered { get; init; }
        public TaskCompletionSource? RoomsRelease { get; init; }

        public Task RestoreServerAsync(string serverBaseUrl, CancellationToken cancellationToken)
        {
            Calls.Add($"server:{serverBaseUrl}");
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<LobbyRoomSummary>> GetRoomsAsync(CancellationToken cancellationToken)
        {
            Calls.Add("rooms");
            RoomsEntered?.TrySetResult();
            if (RoomsRelease != null)
            {
                await RoomsRelease.Task.WaitAsync(cancellationToken);
            }
            return RoomExists
                ? [new LobbyRoomSummary { RoomId = "room-1", RoomName = "Room", RequiresPassword = RequiresPassword }]
                : [];
        }

        public Task<bool> RepreflightPublicJoinAsync(
            LobbyRoomSummary room,
            string? desiredSavePlayerNetId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"join:{room.RoomId}:{desiredSavePlayerNetId}");
            return Task.FromResult(true);
        }

        public Task<bool> PromptPasswordAndRepreflightAsync(
            LobbyRoomSummary room,
            string? desiredSavePlayerNetId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"password:{room.RoomId}");
            return Task.FromResult(true);
        }
    }
}
