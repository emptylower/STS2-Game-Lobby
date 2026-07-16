using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectRoomCombatLifecycleTests
{
    [Fact]
    public void Generation_a_requires_fresh_ready_after_reconnect_and_never_revives_in_generation_b()
    {
        LifecycleContext context = ReadyContext();
        LanConnectRoomCombatReferenceResolver resolver = new(context, new LanConnectChatLocalizer());
        LanConnectCombatRun power = new(new LanConnectPowerStateSegment(
            "MegaCrit.Strength",
            4,
            "generation-a"));
        LanConnectCombatRun target = new(new LanConnectTargetRefSegment(
            "player",
            "3",
            "generation-a"));

        Assert.Equal("Strength +4", resolver.Resolve(power, "en").Label);
        Assert.Equal("Silent", resolver.Resolve(target, "en").Label);

        context.ActiveRoomSessionId = string.Empty;
        Assert.Equal("Unknown power", resolver.Resolve(power, "en").Label);
        Assert.Equal("Target is no longer available", resolver.Resolve(target, "en").Label);

        context.ActiveRoomSessionId = "generation-a";
        Assert.Equal("Strength +4", resolver.Resolve(power, "en").Label);
        Assert.Equal("Silent", resolver.Resolve(target, "en").Label);

        context.ActiveRoomSessionId = "generation-b";
        Assert.Equal("未知能力", resolver.Resolve(power, "zh-CN").Label);
        Assert.Equal("目标已不可用", resolver.Resolve(target, "zh-CN").Label);
    }

    [Fact]
    public void Removed_peer_missing_model_and_description_exception_degrade_independently()
    {
        LifecycleContext context = ReadyContext();
        LanConnectRoomCombatReferenceResolver resolver = new(context, new LanConnectChatLocalizer());
        LanConnectCombatRun power = new(new LanConnectPowerStateSegment(
            "MegaCrit.Strength",
            -2,
            "generation-a"));
        LanConnectCombatRun target = new(new LanConnectTargetRefSegment(
            "player",
            "3",
            "generation-a"));

        context.Peers.Remove("3");
        Assert.Equal("Target is no longer available", resolver.Resolve(target, "en").Label);
        Assert.Equal("Strength -2", resolver.Resolve(power, "en").Label);

        context.Powers.Clear();
        Assert.Equal("Unknown power", resolver.Resolve(power, "en").Label);

        context.Powers["MegaCrit.Strength"] = new("Strength", "Gain attack damage.");
        context.ThrowOnPower = true;
        Assert.Equal("Unknown power", resolver.Resolve(power, "en").Label);
    }

    [Fact]
    public void Channel_history_keeps_typed_runs_and_static_items_across_generation_changes()
    {
        LanConnectChatContent content = new(1,
        [
            new LanConnectTextSegment("before "),
            new LanConnectPowerStateSegment("MegaCrit.Strength", 2, "generation-a"),
            new LanConnectTextSegment(" middle "),
            new LanConnectItemRefSegment("relic", "MegaCrit.Anchor"),
            new LanConnectTextSegment(" after")
        ]);
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        state.AppendConfirmedContentForTests("typed", "Silent", content, 1, false);

        ServerChatMessageState stored = Assert.Single(state.Messages);
        Assert.Same(content, stored.Content);
        Assert.Collection(
            stored.Content.Segments,
            segment => Assert.IsType<LanConnectTextSegment>(segment),
            segment => Assert.IsType<LanConnectPowerStateSegment>(segment),
            segment => Assert.IsType<LanConnectTextSegment>(segment),
            segment => Assert.Equal(
                "MegaCrit.Anchor",
                Assert.IsType<LanConnectItemRefSegment>(segment).ModelId),
            segment => Assert.IsType<LanConnectTextSegment>(segment));
    }

    [Fact]
    public void Combat_context_fingerprint_is_stable_and_changes_for_each_live_dependency()
    {
        LifecycleContext context = ReadyContext();
        LanConnectRoomCombatReferenceResolver resolver = new(context);
        LanConnectRoomCombatRenderContext baseline = new(
            resolver,
            "en-US",
            "mods-a",
            "generation-a",
            "1:1:4:Host\u001e1:3:6:Silent",
            FreshReady: true);

        Assert.Equal(baseline.StableFingerprint, baseline.StableFingerprint);
        Assert.NotEqual(baseline.StableFingerprint, (baseline with { Locale = "zh-CN" }).StableFingerprint);
        Assert.NotEqual(baseline.StableFingerprint, (baseline with { ModFingerprint = "mods-b" }).StableFingerprint);
        Assert.NotEqual(baseline.StableFingerprint, (baseline with { RoomSessionId = "generation-b" }).StableFingerprint);
        Assert.NotEqual(
            baseline.StableFingerprint,
            (baseline with { PeerTargetDirectoryFingerprint = "1:1:4:Host" }).StableFingerprint);
        Assert.NotEqual(baseline.StableFingerprint, (baseline with { FreshReady = false }).StableFingerprint);
    }

    [Fact]
    public void Peer_snapshot_normalization_is_atomic_multi_peer_and_fail_closed()
    {
        HashSet<ulong> first = LanConnectLobbyRuntime.NormalizeRoomPeerDirectory(
        [
            Entry("1", "Host"),
            Entry("2", "Ironclad"),
            Entry("3", "Silent"),
            Entry("bad", "Ignored"),
            Entry("4", " ")
        ]);
        HashSet<ulong> replacement = LanConnectLobbyRuntime.NormalizeRoomPeerDirectory(
        [
            Entry("1", "Host"),
            Entry("3", "Silent")
        ]);

        Assert.Equal([1UL, 2UL, 3UL], first.Order());
        Assert.Equal([1UL, 3UL], replacement.Order());
        Assert.DoesNotContain(2UL, replacement);
    }

    [Fact]
    public void Host_snapshot_filters_stale_names_to_current_multi_peer_membership()
    {
        List<LobbyPlayerNameEntry> filtered = LanConnectLobbyRuntime.FilterCurrentRoomPeerNames(
        [
            Entry("3", "Silent"),
            Entry("1", "Host"),
            Entry("2", "Disconnected"),
            Entry("4", "Defect")
        ],
        [1UL, 3UL, 4UL]);

        Assert.Equal(["1", "3", "4"], filtered.Select(static entry => entry.PlayerNetId));
        Assert.DoesNotContain(filtered, static entry => entry.PlayerName == "Disconnected");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Superseded_or_closing_host_sync_cannot_mutate_directory_or_broadcast(bool markClosing)
    {
        LanConnectRoomSessionAuthority authority = new();
        LanConnectLegacyControlEnvelopeOrchestrator orchestrator = new(authority);
        object candidate = new();
        object activeSession = candidate;
        bool closing = false;
        Dictionary<ulong, string> directory = new() { [1] = "Current host" };
        int broadcasts = 0;
        authority.AfterOptimisticCheckForTests = () => authority.RunExclusive(() =>
        {
            if (markClosing)
            {
                closing = true;
            }
            else
            {
                activeSession = new object();
            }
        });

        bool applied = await orchestrator.ApplyHostedPlayerNameSyncAsync(
            () => activeSession,
            candidate,
            () => closing,
            () => directory[2] = "Stale peer",
            () =>
            {
                broadcasts++;
                return Task.CompletedTask;
            });

        Assert.False(applied);
        Assert.Equal(new Dictionary<ulong, string> { [1] = "Current host" }, directory);
        Assert.Equal(0, broadcasts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Superseded_or_closing_joined_snapshot_cannot_mutate_names_or_peer_set(bool markClosing)
    {
        LanConnectRoomSessionAuthority authority = new();
        LanConnectLegacyControlEnvelopeOrchestrator orchestrator = new(authority);
        object candidate = new();
        object activeSession = candidate;
        bool closing = false;
        Dictionary<ulong, string> directory = new() { [1] = "Current host", [3] = "Current peer" };
        IReadOnlySet<ulong> peerSet = new HashSet<ulong> { 1, 3 };
        authority.AfterOptimisticCheckForTests = () => authority.RunExclusive(() =>
        {
            if (markClosing)
            {
                closing = true;
            }
            else
            {
                activeSession = new object();
            }
        });

        bool mutated = orchestrator.ApplyJoinedPlayerNameSnapshot(
            () => activeSession,
            candidate,
            () => closing,
            () => true,
            _ =>
            {
                directory = new Dictionary<ulong, string> { [1] = "Old host", [2] = "Old peer" };
                peerSet = new HashSet<ulong> { 1, 2 };
            });

        Assert.False(mutated);
        Assert.Equal("Current host", directory[1]);
        Assert.Equal("Current peer", directory[3]);
        Assert.False(directory.ContainsKey(2));
        Assert.Equal([1UL, 3UL], peerSet.Order());
    }

    [Fact]
    public async Task Send_starts_under_authority_lock_and_revalidates_after_await()
    {
        LanConnectRoomSessionAuthority authority = new();
        LanConnectLegacyControlEnvelopeOrchestrator orchestrator = new(authority);
        object candidate = new();
        object activeSession = candidate;
        bool closing = false;
        TaskCompletionSource sendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSend = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<bool> send = orchestrator.ApplyHostedPlayerNameSyncAsync(
            () => activeSession,
            candidate,
            () => closing,
            () => { },
            () =>
            {
                sendStarted.TrySetResult();
                return releaseSend.Task;
            });
        await sendStarted.Task;
        authority.RunExclusive(() => activeSession = new object());
        releaseSend.TrySetResult();

        Assert.False(await send);
    }

    [Fact]
    public void Delayed_old_same_room_close_cannot_clear_new_generation_directory()
    {
        LanConnectRoomSessionAuthority authority = new();
        LanConnectLegacyControlEnvelopeOrchestrator orchestrator = new(authority);
        object oldSession = new();
        object activeSession = oldSession;
        Dictionary<ulong, string> directory = new() { [1] = "Old host" };
        object newSession = new();
        authority.RunExclusive(() =>
        {
            activeSession = newSession;
            directory.Clear();
            directory[9] = "New host";
        });
        bool cleared = orchestrator.CleanupCurrentGeneration(
            () => activeSession,
            oldSession,
            directory.Clear);

        Assert.False(cleared);
        Assert.Equal(new Dictionary<ulong, string> { [9] = "New host" }, directory);
    }

    private static LobbyPlayerNameEntry Entry(string id, string name) => new()
    {
        PlayerNetId = id,
        PlayerName = name
    };

    private static LifecycleContext ReadyContext() => new()
    {
        ActiveRoomSessionId = "generation-a",
        Peers = { "1", "2", "3" },
        Names = { ["1"] = "Host", ["2"] = "Ironclad", ["3"] = "Silent" },
        Powers = { ["MegaCrit.Strength"] = new("Strength", "Gain attack damage.") }
    };

    private sealed class LifecycleContext : ILanConnectRoomCombatContext
    {
        public string ActiveRoomSessionId { get; set; } = string.Empty;

        internal HashSet<string> Peers { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, LanConnectLocalPowerReference> Powers { get; } = new(StringComparer.Ordinal);

        internal bool ThrowOnPower { get; set; }

        public bool IsCurrentPeer(string playerNetId) => Peers.Contains(playerNetId);

        public bool TryGetCurrentPeerName(string playerNetId, out string name) =>
            Names.TryGetValue(playerNetId, out name!);

        public bool TryResolveLocalPower(string modelId, out LanConnectLocalPowerReference power)
        {
            if (ThrowOnPower)
            {
                throw new InvalidOperationException("description failed");
            }
            return Powers.TryGetValue(modelId, out power);
        }
    }
}
