using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.ModSync;

public sealed class LanConnectSteamWorkshopSyncProviderTests
{
    [Fact]
    public void Native_provider_has_no_AutoModSubscriber_runtime_dependency()
    {
        using var assemblyStream = File.OpenRead(typeof(LanConnectSteamWorkshopSyncProvider).Assembly.Location);
        using var peReader = new PEReader(assemblyStream);
        MetadataReader metadata = peReader.GetMetadataReader();

        Assert.DoesNotContain(
            metadata.AssemblyReferences.Select(handle => metadata.GetAssemblyReference(handle)),
            reference => metadata.GetString(reference.Name).Contains("AutoModSubscriber", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle)),
            type => metadata.GetString(type.Name).Contains("AutoModSubscriber", StringComparison.OrdinalIgnoreCase)
                || metadata.GetString(type.Namespace).Contains("AutoModSubscriber", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Job_transitions_pending_validating_subscribing_downloading_waiting_install_installed()
    {
        FakeWorkshopApi api = new();
        FakeManifestVerifier verifier = new();
        FakeWorkshopClock clock = new();
        using LanConnectSteamWorkshopSyncProvider sut = new(api, verifier, clock);

        LanConnectWorkshopMetadataQueryResult query = await sut.QueryMetadataAsync(Descriptor());
        LanConnectWorkshopJobSnapshot submitted = await sut.SubmitAsync(Descriptor(), query.Metadata!);
        await WaitForStateAsync(sut, submitted.JobId, LanConnectWorkshopJobState.Downloading);
        api.Status = InstalledStatus("/steam/workshop/item");
        api.RaiseItemChanged();

        LanConnectWorkshopJobSnapshot installed = await WaitForStateAsync(
            sut,
            submitted.JobId,
            LanConnectWorkshopJobState.Installed);

        Assert.Equal(
        [
            LanConnectWorkshopJobState.Pending,
            LanConnectWorkshopJobState.Validating,
            LanConnectWorkshopJobState.Subscribing,
            LanConnectWorkshopJobState.Downloading,
            LanConnectWorkshopJobState.WaitingInstall,
            LanConnectWorkshopJobState.Installed
        ], installed.StateHistory);
        Assert.True(installed.RequiresRestart);
        Assert.True(installed.RequiresRepreflight);
    }

    [Fact]
    public async Task Job_waits_for_terminal_state_beyond_five_seconds()
    {
        FakeWorkshopApi api = new();
        FakeWorkshopClock clock = new();
        using LanConnectSteamWorkshopSyncProvider sut = new(api, new FakeManifestVerifier(), clock);
        LanConnectWorkshopMetadata metadata = (await sut.QueryMetadataAsync(Descriptor())).Metadata!;
        LanConnectWorkshopJobSnapshot submitted = await sut.SubmitAsync(Descriptor(), metadata);
        await WaitForStateAsync(sut, submitted.JobId, LanConnectWorkshopJobState.Downloading);

        clock.Advance(TimeSpan.FromSeconds(6));
        api.RaiseItemChanged();
        await Task.Delay(10);

        Assert.Equal(LanConnectWorkshopJobState.Downloading, sut.Snapshot(submitted.JobId).State);
    }

    [Fact]
    public async Task Job_times_out_at_five_minutes()
    {
        FakeWorkshopClock clock = new();
        using LanConnectSteamWorkshopSyncProvider sut = new(new FakeWorkshopApi(), new FakeManifestVerifier(), clock);
        LanConnectWorkshopMetadata metadata = (await sut.QueryMetadataAsync(Descriptor())).Metadata!;
        LanConnectWorkshopJobSnapshot submitted = await sut.SubmitAsync(Descriptor(), metadata);
        await WaitForStateAsync(sut, submitted.JobId, LanConnectWorkshopJobState.Downloading);

        clock.Advance(TimeSpan.FromMinutes(5));

        LanConnectWorkshopJobSnapshot timedOut = await WaitForStateAsync(
            sut,
            submitted.JobId,
            LanConnectWorkshopJobState.TimedOut);
        Assert.Equal("workshop_timeout", timedOut.FailureCode);
    }

    [Fact]
    public async Task Job_cancel_is_idempotent_and_reaches_canceled()
    {
        FakeWorkshopApi api = new();
        using LanConnectSteamWorkshopSyncProvider sut = new(api, new FakeManifestVerifier(), new FakeWorkshopClock());
        LanConnectWorkshopMetadata metadata = (await sut.QueryMetadataAsync(Descriptor())).Metadata!;
        LanConnectWorkshopJobSnapshot submitted = await sut.SubmitAsync(Descriptor(), metadata);
        await WaitForStateAsync(sut, submitted.JobId, LanConnectWorkshopJobState.Downloading);

        Assert.True(sut.Cancel(submitted.JobId));
        Assert.True(sut.Cancel(submitted.JobId));
        await WaitForStateAsync(sut, submitted.JobId, LanConnectWorkshopJobState.Canceled);

        Assert.Equal(1, api.UnsubscribeCalls);
    }

    [Fact]
    public async Task Job_cancel_during_subscribe_still_attempts_new_subscription_cleanup()
    {
        FakeWorkshopApi api = new() { BlockSubscribe = true };
        using LanConnectSteamWorkshopSyncProvider sut = new(api, new FakeManifestVerifier(), new FakeWorkshopClock());
        LanConnectWorkshopMetadata metadata = (await sut.QueryMetadataAsync(Descriptor())).Metadata!;
        LanConnectWorkshopJobSnapshot submitted = await sut.SubmitAsync(Descriptor(), metadata);
        await WaitForStateAsync(sut, submitted.JobId, LanConnectWorkshopJobState.Subscribing);

        sut.Cancel(submitted.JobId);
        LanConnectWorkshopJobSnapshot canceled = await WaitForStateAsync(
            sut,
            submitted.JobId,
            LanConnectWorkshopJobState.Canceled);

        Assert.Equal("workshop_canceled", canceled.FailureCode);
        Assert.Equal(1, api.UnsubscribeCalls);
    }

    [Fact]
    public async Task Job_retry_creates_a_fresh_attempt_after_failure()
    {
        FakeWorkshopApi api = new() { SubscribeSucceeds = false };
        using LanConnectSteamWorkshopSyncProvider sut = new(api, new FakeManifestVerifier(), new FakeWorkshopClock());
        LanConnectWorkshopMetadata metadata = (await sut.QueryMetadataAsync(Descriptor())).Metadata!;
        LanConnectWorkshopJobSnapshot submitted = await sut.SubmitAsync(Descriptor(), metadata);
        await WaitForStateAsync(sut, submitted.JobId, LanConnectWorkshopJobState.Failed);

        api.SubscribeSucceeds = true;
        api.Status = InstalledStatus("/steam/workshop/item");
        LanConnectWorkshopJobSnapshot retry = await sut.RetryAsync(submitted.JobId);
        LanConnectWorkshopJobSnapshot installed = await WaitForStateAsync(
            sut,
            retry.JobId,
            LanConnectWorkshopJobState.Installed);

        Assert.NotEqual(submitted.JobId, retry.JobId);
        Assert.Equal(2, installed.Attempt);
    }

    [Fact]
    public async Task Provider_holds_callbacks_for_the_full_job_lifetime()
    {
        FakeWorkshopApi api = new();
        LanConnectSteamWorkshopSyncProvider sut = new(api, new FakeManifestVerifier(), new FakeWorkshopClock());
        LanConnectWorkshopMetadata metadata = (await sut.QueryMetadataAsync(Descriptor())).Metadata!;
        LanConnectWorkshopJobSnapshot submitted = await sut.SubmitAsync(Descriptor(), metadata);
        api.Status = InstalledStatus("/steam/workshop/item");
        api.RaiseItemChanged();
        await WaitForStateAsync(sut, submitted.JobId, LanConnectWorkshopJobState.Installed);

        Assert.Equal(1, api.ItemChangedSubscriberCount);
        Assert.False(api.IsDisposed);

        sut.Dispose();
        Assert.Equal(0, api.ItemChangedSubscriberCount);
        Assert.True(api.IsDisposed);
    }

    [Fact]
    public async Task Provider_rejects_metadata_for_app_id_other_than_2868840()
    {
        FakeWorkshopApi api = new()
        {
            Metadata = Metadata(consumerAppId: 123)
        };
        using LanConnectSteamWorkshopSyncProvider sut = new(api, new FakeManifestVerifier(), new FakeWorkshopClock());

        LanConnectWorkshopMetadataQueryResult result = await sut.QueryMetadataAsync(Descriptor());

        Assert.False(result.Success);
        Assert.Equal("workshop_wrong_app", result.ErrorCode);
    }

    [Fact]
    public async Task Provider_rejects_non_workshop_descriptors_before_querying_steam()
    {
        FakeWorkshopApi api = new();
        using LanConnectSteamWorkshopSyncProvider sut = new(api, new FakeManifestVerifier(), new FakeWorkshopClock());
        LobbyModDescriptor descriptor = Descriptor();
        descriptor.Source = LanConnectModSources.ModsDirectory;

        LanConnectWorkshopMetadataQueryResult result = await sut.QueryMetadataAsync(descriptor);

        Assert.False(result.Success);
        Assert.Equal("workshop_source_required", result.ErrorCode);
        Assert.Equal(0, api.QueryCalls);
    }

    [Theory]
    [InlineData("+3747497501")]
    [InlineData(" 3747497501")]
    [InlineData("3747497501 ")]
    public async Task Provider_rejects_spoofed_workshop_ids_before_querying_steam(string workshopFileId)
    {
        FakeWorkshopApi api = new();
        using LanConnectSteamWorkshopSyncProvider sut = new(api, new FakeManifestVerifier(), new FakeWorkshopClock());
        LobbyModDescriptor descriptor = Descriptor();
        descriptor.WorkshopFileId = workshopFileId;

        LanConnectWorkshopMetadataQueryResult result = await sut.QueryMetadataAsync(descriptor);

        Assert.False(result.Success);
        Assert.Equal("workshop_descriptor_invalid", result.ErrorCode);
        Assert.Equal(0, api.QueryCalls);
    }

    [Fact]
    public async Task Provider_surfaces_real_title_and_publisher_before_consent()
    {
        using LanConnectSteamWorkshopSyncProvider sut = new(
            new FakeWorkshopApi { Metadata = Metadata(title: "Steam title", publisher: "Steam publisher") },
            new FakeManifestVerifier(),
            new FakeWorkshopClock());

        LanConnectWorkshopMetadataQueryResult result = await sut.QueryMetadataAsync(Descriptor());

        Assert.True(result.Success);
        Assert.Equal("Steam title", result.Metadata!.Title);
        Assert.Equal("Steam publisher", result.Metadata.Publisher);
    }

    [Fact]
    public void Provider_never_downloads_from_host_service_or_arbitrary_url()
    {
        string[] memberTypes = typeof(ILanConnectSteamWorkshopApi)
            .GetMembers()
            .SelectMany(member => member switch
            {
                System.Reflection.MethodInfo method => method.GetParameters().Select(parameter => parameter.ParameterType.FullName ?? ""),
                System.Reflection.PropertyInfo property => [property.PropertyType.FullName ?? ""],
                _ => []
            })
            .ToArray();

        Assert.DoesNotContain(memberTypes, type => type.Contains("HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(memberTypes, type => type.Contains("Uri", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(ILanConnectSteamWorkshopApi).GetMembers(), member =>
            member.Name.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
            member.Name.Contains("Host", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Provider_returns_structured_unsupported_when_SteamAPI_is_unavailable()
    {
        FakeWorkshopApi api = new()
        {
            Availability = LanConnectModSyncAvailability.Unsupported(
                "steam_api_unavailable",
                "SteamAPI is unavailable.")
        };
        using LanConnectSteamWorkshopSyncProvider sut = new(api, new FakeManifestVerifier(), new FakeWorkshopClock());

        LanConnectWorkshopMetadataQueryResult result = await sut.QueryMetadataAsync(Descriptor());

        Assert.False(result.Success);
        Assert.Equal("steam_api_unavailable", result.ErrorCode);
        Assert.False(result.Availability.IsAvailable);
        Assert.Equal(0, api.QueryCalls);
    }

    [Fact]
    public void Native_availability_reports_android_and_missing_steam_without_throwing()
    {
        LanConnectModSyncAvailability android =
            LanConnectNativeWorkshopAvailability.Resolve(isAndroid: true, steamInitialized: false);
        LanConnectModSyncAvailability noSteam =
            LanConnectNativeWorkshopAvailability.Resolve(isAndroid: false, steamInitialized: false);

        Assert.False(android.IsAvailable);
        Assert.Equal("android_manual_only", android.Code);
        Assert.False(noSteam.IsAvailable);
        Assert.Equal("steam_api_unavailable", noSteam.Code);
    }

    private static LobbyModDescriptor Descriptor() => new()
    {
        Id = "test.gameplay.mod",
        Version = "1.0.0",
        Role = LanConnectModRoles.Gameplay,
        Source = LanConnectModSources.SteamWorkshop,
        WorkshopFileId = "3747497501"
    };

    private static LanConnectWorkshopMetadata Metadata(
        uint consumerAppId = LanConnectModSyncCapabilities.SteamAppId,
        string title = "Workshop title",
        string publisher = "Workshop publisher") =>
        new("3747497501", consumerAppId, title, publisher);

    private static LanConnectWorkshopItemStatus InstalledStatus(string folder) => new(
        IsSubscribed: true,
        IsInstalled: true,
        NeedsUpdate: false,
        IsDownloading: false,
        IsDownloadPending: false,
        BytesDownloaded: 100,
        BytesTotal: 100,
        InstallFolder: folder);

    private static async Task<LanConnectWorkshopJobSnapshot> WaitForStateAsync(
        LanConnectSteamWorkshopSyncProvider provider,
        Guid jobId,
        LanConnectWorkshopJobState expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            LanConnectWorkshopJobSnapshot snapshot = provider.Snapshot(jobId);
            if (snapshot.State == expected)
            {
                return snapshot;
            }
            await Task.Delay(1);
        }
        throw new Xunit.Sdk.XunitException(
            $"Job did not reach {expected}; current={provider.Snapshot(jobId).State}.");
    }

    private sealed class FakeWorkshopApi : ILanConnectSteamWorkshopApi
    {
        private Action<ulong>? _itemChanged;

        public LanConnectModSyncAvailability Availability { get; set; } = LanConnectModSyncAvailability.Available;
        public LanConnectWorkshopMetadata Metadata { get; set; } = LanConnectSteamWorkshopSyncProviderTests.Metadata();
        public LanConnectWorkshopItemStatus Status { get; set; } = new(false, false, true, true, false, 1, 100, null);
        public bool SubscribeSucceeds { get; set; } = true;
        public bool BlockSubscribe { get; set; }
        public bool DownloadSucceeds { get; set; } = true;
        public int QueryCalls { get; private set; }
        public int UnsubscribeCalls { get; private set; }
        public int ItemChangedSubscriberCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public event Action<ulong>? ItemChanged
        {
            add { _itemChanged += value; ItemChangedSubscriberCount++; }
            remove { _itemChanged -= value; ItemChangedSubscriberCount--; }
        }

        public Task<LanConnectWorkshopMetadata> QueryMetadataAsync(ulong workshopFileId, CancellationToken cancellationToken)
        {
            QueryCalls++;
            return Task.FromResult(Metadata);
        }

        public async Task<bool> SubscribeAsync(ulong workshopFileId, CancellationToken cancellationToken)
        {
            if (BlockSubscribe)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return SubscribeSucceeds;
        }

        public Task<bool> UnsubscribeAsync(ulong workshopFileId, CancellationToken cancellationToken)
        {
            UnsubscribeCalls++;
            return Task.FromResult(true);
        }

        public bool Download(ulong workshopFileId) => DownloadSucceeds;

        public LanConnectWorkshopItemStatus GetItemStatus(ulong workshopFileId) => Status;

        public void RaiseItemChanged() => _itemChanged?.Invoke(3747497501);

        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeManifestVerifier : ILanConnectWorkshopManifestVerifier
    {
        public Task<LanConnectWorkshopManifestVerification> VerifyInstalledManifestAsync(
            string installFolder,
            LobbyModDescriptor expected,
            CancellationToken cancellationToken) =>
            Task.FromResult(LanConnectWorkshopManifestVerification.Verified("1.0.0"));
    }

    private sealed class FakeWorkshopClock : ILanConnectWorkshopClock
    {
        private readonly object _sync = new();
        private readonly List<TaskCompletionSource> _delays = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            lock (_sync)
            {
                _delays.Add(completion);
            }
            return completion.Task;
        }

        public void Advance(TimeSpan elapsed)
        {
            List<TaskCompletionSource> delays;
            lock (_sync)
            {
                UtcNow += elapsed;
                delays = [.. _delays];
                _delays.Clear();
            }
            foreach (TaskCompletionSource delay in delays)
            {
                delay.TrySetResult();
            }
        }
    }
}
