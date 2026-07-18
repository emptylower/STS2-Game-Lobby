using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.ModSync;

public sealed class LanConnectModSyncWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lan-connect-workflow-{Guid.NewGuid():N}");

    [Fact]
    public async Task Workflow_queries_real_metadata_before_consent_and_does_not_subscribe()
    {
        FakeProvider provider = new();
        LanConnectModSyncWorkflow sut = Workflow(provider, Difference(missingWorkshop: true));

        LanConnectModSyncViewState state = await sut.PrepareAsync();

        Assert.Equal(["metadata:123"], provider.Calls);
        Assert.Equal("Steam title", Assert.Single(state.Rows).Metadata?.Title);
        Assert.Equal(LanConnectModSyncViewKind.AutomaticSync, state.Kind);
    }

    [Fact]
    public async Task Workflow_success_persists_pending_join_only_after_install_verification()
    {
        FakeProvider provider = new();
        LanConnectPendingModSyncJoinStore store = Store();
        LanConnectModSyncWorkflow sut = Workflow(provider, Difference(missingWorkshop: true), store);
        await sut.PrepareAsync();

        LanConnectModSyncWorkflowResult result = await sut.ApplyAsync(
            Room(),
            "https://lobby.example",
            "slot-2",
            selectedExtraIds: [],
            disableConfirmed: false);

        Assert.Equal(LanConnectModSyncWorkflowStatus.RestartRequired, result.Status);
        Assert.Equal(["metadata:123", "submit:123", "poll"], provider.Calls);
        LanConnectPendingModSyncJoin pending = Assert.IsType<LanConnectPendingModSyncJoin>(store.Load());
        Assert.Equal("room-1", pending.RoomId);
        Assert.Equal("slot-2", pending.DesiredSavePlayerNetId);
    }

    [Fact]
    public async Task Workflow_requires_second_confirmation_before_selective_disable()
    {
        FakeProvider provider = new();
        FakeDisableSettings settings = new();
        LanConnectModSyncWorkflow sut = Workflow(
            provider,
            Difference(extra: true),
            disableSettings: settings);
        await sut.PrepareAsync();

        LanConnectModSyncWorkflowResult result = await sut.ApplyAsync(
            Room(),
            "https://lobby.example",
            null,
            selectedExtraIds: ["extra"],
            disableConfirmed: false);

        Assert.Equal(LanConnectModSyncWorkflowStatus.RequiresDisableConfirmation, result.Status);
        Assert.Empty(settings.Calls);

        result = await sut.ApplyAsync(
            Room(),
            "https://lobby.example",
            null,
            selectedExtraIds: ["extra"],
            disableConfirmed: true);
        Assert.Equal(LanConnectModSyncWorkflowStatus.RestartRequired, result.Status);
        Assert.Equal(["set:extra:False", "save"], settings.Calls);
    }

    [Fact]
    public async Task Workflow_retry_uses_provider_fresh_attempt_and_then_persists_pending()
    {
        FakeProvider provider = new() { FailFirstPoll = true };
        LanConnectPendingModSyncJoinStore store = Store();
        LanConnectModSyncWorkflow sut = Workflow(provider, Difference(missingWorkshop: true), store);
        await sut.PrepareAsync();
        LanConnectModSyncWorkflowResult failed = await sut.ApplyAsync(
            Room(), "https://lobby.example", null, [], disableConfirmed: false);

        Assert.Equal(LanConnectModSyncWorkflowStatus.Failed, failed.Status);
        LanConnectModSyncWorkflowResult retried = await sut.RetryAsync();

        Assert.Equal(LanConnectModSyncWorkflowStatus.RestartRequired, retried.Status);
        Assert.Contains("retry", provider.Calls);
        Assert.NotNull(store.Load());
    }

    private LanConnectModSyncWorkflow Workflow(
        FakeProvider provider,
        LobbyModPreflightResponse response,
        LanConnectPendingModSyncJoinStore? store = null,
        FakeDisableSettings? disableSettings = null) => new(
            provider,
            new LanConnectModDisableApplier(disableSettings ?? new FakeDisableSettings()),
            store ?? Store(),
            response,
            pollDelay: _ => Task.CompletedTask);

    private LanConnectPendingModSyncJoinStore Store() => new(
        Path.Combine(_directory, "pending.json"),
        () => new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));

    private static LobbyRoomSummary Room() => new() { RoomId = "room-1", RoomName = "Room" };

    private static LobbyModPreflightResponse Difference(bool missingWorkshop = false, bool extra = false) => new()
    {
        Enabled = true,
        ProtocolVersion = 1,
        HostInventoryAvailable = true,
        GameVersion = new LanConnectGameVersionComparison { Host = "0.109.0", Local = "0.109.0", ExactMatch = true },
        MissingWorkshopMods = missingWorkshop
            ? [new LobbyModDescriptor
            {
                Id = "needed",
                Version = "1.0.0",
                Role = LanConnectModRoles.Gameplay,
                Source = LanConnectModSources.SteamWorkshop,
                WorkshopFileId = "123"
            }]
            : [],
        ExtraGameplayMods = extra
            ? [new LobbyModDescriptor
            {
                Id = "extra",
                Version = "1.0.0",
                Role = LanConnectModRoles.Gameplay,
                Source = LanConnectModSources.ModsDirectory
            }]
            : []
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeProvider : ILanConnectModSyncProvider
    {
        private readonly Guid _jobId = Guid.NewGuid();
        public List<string> Calls { get; } = [];
        public bool FailFirstPoll { get; init; }
        private bool _failed;
        public LanConnectModSyncAvailability Availability => LanConnectModSyncAvailability.Available;

        public Task<LanConnectWorkshopMetadataQueryResult> QueryMetadataAsync(
            LobbyModDescriptor descriptor,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"metadata:{descriptor.WorkshopFileId}");
            return Task.FromResult(LanConnectWorkshopMetadataQueryResult.Succeeded(
                new LanConnectWorkshopMetadata(descriptor.WorkshopFileId!, 2868840, "Steam title", "Publisher")));
        }

        public Task<LanConnectWorkshopJobSnapshot> SubmitAsync(
            LobbyModDescriptor descriptor,
            LanConnectWorkshopMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"submit:{descriptor.WorkshopFileId}");
            return Task.FromResult(Snapshot(LanConnectWorkshopJobState.Downloading));
        }

        public LanConnectWorkshopJobSnapshot Poll(Guid jobId)
        {
            Calls.Add("poll");
            if (FailFirstPoll && !_failed)
            {
                _failed = true;
                return Snapshot(LanConnectWorkshopJobState.Failed) with
                {
                    FailureCode = "offline",
                    Message = "offline"
                };
            }
            return Snapshot(LanConnectWorkshopJobState.Installed) with
            {
                RequiresRestart = true,
                RequiresRepreflight = true
            };
        }

        public LanConnectWorkshopJobSnapshot Snapshot(Guid jobId) => Snapshot(LanConnectWorkshopJobState.Downloading);
        public bool Cancel(Guid jobId) => true;
        public Task<LanConnectWorkshopJobSnapshot> RetryAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            Calls.Add("retry");
            return Task.FromResult(Snapshot(LanConnectWorkshopJobState.Downloading));
        }
        public void Dispose() { }

        private LanConnectWorkshopJobSnapshot Snapshot(LanConnectWorkshopJobState state) => new()
        {
            JobId = _jobId,
            State = state,
            StateHistory = [LanConnectWorkshopJobState.Pending, state],
            Descriptor = new LobbyModDescriptor
            {
                Id = "needed",
                Version = "1.0.0",
                Role = LanConnectModRoles.Gameplay,
                Source = LanConnectModSources.SteamWorkshop,
                WorkshopFileId = "123"
            },
            Metadata = new LanConnectWorkshopMetadata("123", 2868840, "Steam title", "Publisher")
        };
    }

    private sealed class FakeDisableSettings : ILanConnectModDisableSettings
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<LanConnectModSettingEntry> Snapshot() =>
            [new LanConnectModSettingEntry("extra", LanConnectModSources.ModsDirectory, true)];

        public void SetEnabled(string id, string source, bool enabled) => Calls.Add($"set:{id}:{enabled}");
        public void SaveSettings() => Calls.Add("save");
    }
}
