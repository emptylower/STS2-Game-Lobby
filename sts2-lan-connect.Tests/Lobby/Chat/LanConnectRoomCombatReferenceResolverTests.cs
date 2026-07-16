using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectRoomCombatReferenceResolverTests
{
    [Theory]
    [InlineData(-32768)]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(32767)]
    public void Power_capture_preserves_signed_amount_and_optional_current_peers(int amount)
    {
        FakeContext context = ReadyContext();
        LanConnectRoomCombatReferenceResolver resolver = new(context);

        Assert.True(resolver.TryCreatePowerRun(
            new LanConnectPowerCaptureCandidate(
                "MegaCrit.Strength",
                amount,
                "session-1",
                "net:owner",
                "net:applier"),
            out LanConnectCombatRun run));

        LanConnectPowerStateSegment segment = Assert.IsType<LanConnectPowerStateSegment>(run.Segment);
        Assert.Equal("MegaCrit.Strength", segment.ModelId);
        Assert.Equal((short)amount, segment.Amount);
        Assert.Equal("session-1", segment.RoomSessionId);
        Assert.Equal("net:owner", segment.OwnerPlayerNetId);
        Assert.Equal("net:applier", segment.ApplierPlayerNetId);
        Assert.True(resolver.CanCommit(run));
    }

    [Fact]
    public void Current_player_target_capture_uses_exact_net_id_and_generation()
    {
        FakeContext context = ReadyContext();
        LanConnectRoomCombatReferenceResolver resolver = new(context);

        Assert.True(resolver.TryCreatePlayerTargetRun(
            new LanConnectPlayerTargetCaptureCandidate("net:target", "session-1"),
            out LanConnectCombatRun run));

        Assert.Equal(
            new LanConnectTargetRefSegment("player", "net:target", "session-1"),
            Assert.IsType<LanConnectTargetRefSegment>(run.Segment));
        Assert.True(resolver.CanCommit(run));
    }

    [Theory]
    [InlineData("", "session-1", "MegaCrit.Strength", 1, null, null)]
    [InlineData("session-2", "session-1", "MegaCrit.Strength", 1, null, null)]
    [InlineData("session-1", "session-1", "", 1, null, null)]
    [InlineData("session-1", "session-1", "Mega Crit.Strength", 1, null, null)]
    [InlineData("session-1", "session-1", "MegaCrit.Strength", -32769, null, null)]
    [InlineData("session-1", "session-1", "MegaCrit.Strength", 32768, null, null)]
    [InlineData("session-1", "session-1", "MegaCrit.Strength", 1, "net:unknown", null)]
    [InlineData("session-1", "session-1", "MegaCrit.Strength", 1, null, "net:unknown")]
    public void Power_capture_rejects_absent_stale_or_invalid_context(
        string observedSession,
        string activeSession,
        string modelId,
        int amount,
        string? owner,
        string? applier)
    {
        FakeContext context = ReadyContext();
        context.ActiveRoomSessionId = activeSession;

        Assert.False(new LanConnectRoomCombatReferenceResolver(context).TryCreatePowerRun(
            new LanConnectPowerCaptureCandidate(
                modelId,
                amount,
                observedSession,
                owner,
                applier),
            out _));
    }

    [Fact]
    public void Missing_local_power_or_context_exception_fails_closed()
    {
        FakeContext context = ReadyContext();
        context.Powers.Clear();
        LanConnectRoomCombatReferenceResolver resolver = new(context);
        LanConnectPowerCaptureCandidate candidate = new(
            "MegaCrit.Strength",
            2,
            "session-1");

        Assert.False(resolver.TryCreatePowerRun(candidate, out _));
        context.Throw = true;
        Assert.False(resolver.TryCreatePowerRun(candidate, out _));
    }

    [Theory]
    [InlineData("", "session-1", "net:target")]
    [InlineData("session-2", "session-1", "net:target")]
    [InlineData("session-1", "session-1", "net:unknown")]
    [InlineData("session-1", "session-1", "bad target")]
    public void Player_target_rejects_absent_stale_unknown_or_invalid_peer(
        string observedSession,
        string activeSession,
        string playerNetId)
    {
        FakeContext context = ReadyContext();
        context.ActiveRoomSessionId = activeSession;

        Assert.False(new LanConnectRoomCombatReferenceResolver(context).TryCreatePlayerTargetRun(
            new LanConnectPlayerTargetCaptureCandidate(playerNetId, observedSession),
            out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Commit_revalidates_generation_and_peer_membership(bool replaceGeneration)
    {
        FakeContext context = ReadyContext();
        LanConnectRoomCombatReferenceResolver resolver = new(context);
        Assert.True(resolver.TryCreatePlayerTargetRun(
            new LanConnectPlayerTargetCaptureCandidate("net:target", "session-1"),
            out LanConnectCombatRun run));

        if (replaceGeneration)
        {
            context.ActiveRoomSessionId = "session-2";
        }
        else
        {
            context.Peers.Remove("net:target");
        }

        Assert.False(resolver.CanCommit(run));
    }

    [Fact]
    public void Resolution_uses_local_power_and_current_peer_then_localized_fallbacks()
    {
        FakeContext context = ReadyContext();
        LanConnectRoomCombatReferenceResolver resolver = new(context, new LanConnectChatLocalizer());
        Assert.True(resolver.TryCreatePowerRun(
            new LanConnectPowerCaptureCandidate("MegaCrit.Strength", -2, "session-1"),
            out LanConnectCombatRun power));
        Assert.True(resolver.TryCreatePlayerTargetRun(
            new LanConnectPlayerTargetCaptureCandidate("net:target", "session-1"),
            out LanConnectCombatRun player));

        LanConnectResolvedCombatReference resolvedPower = resolver.Resolve(power, "en");
        LanConnectResolvedCombatReference resolvedPlayer = resolver.Resolve(player, "en");
        Assert.Equal(LanConnectResolvedCombatReferenceStatus.Resolved, resolvedPower.Status);
        Assert.Equal("Strength", resolvedPower.Label);
        Assert.Equal(LanConnectResolvedCombatReferenceStatus.Resolved, resolvedPlayer.Status);
        Assert.Equal("Silent", resolvedPlayer.Label);

        context.Powers.Clear();
        context.Peers.Remove("net:target");
        Assert.Equal("未知能力", resolver.Resolve(power, "zh-CN").Label);
        Assert.Equal("目标已不可用", resolver.Resolve(player, "zh-CN").Label);
    }

    [Fact]
    public void Combat_draft_and_room_send_decision_accept_8192_and_reject_8193()
    {
        string roomId = new('R', 128);
        string sessionId = new('S', 128);
        string senderName = new('N', 32);
        string playerNetId = new('P', 128);
        LanConnectChatFeatureVersions features = new(1, 1, 1, 1);
        LanConnectChatContent Boundary(short firstAmount)
        {
            List<LanConnectChatSegment> segments =
                [new LanConnectTextSegment(new string('T', 38))];
            segments.AddRange(Enumerable.Range(0, 11).Select(index =>
                (LanConnectChatSegment)new LanConnectPowerStateSegment(
                    new string('M', 160),
                    index == 0 ? firstAmount : short.MinValue,
                    sessionId,
                    playerNetId,
                    playerNetId)));
            return LanConnectRoomChatSessionContext.Canonicalize(
                new LanConnectChatContent(1, segments),
                features,
                sessionId);
        }
        LanConnectRichDraft DraftFrom(LanConnectChatContent content) =>
            LanConnectRichDraft.FromRuns(content.Segments.Select(static segment => segment switch
            {
                LanConnectTextSegment text => (LanConnectDraftRun)new LanConnectTextRun(text.Text),
                LanConnectPowerStateSegment power => new LanConnectCombatRun(power),
                _ => throw new InvalidOperationException("Unexpected boundary segment.")
            }));
        LanConnectChatContent exact8192 = Boundary(short.MaxValue);
        LanConnectChatContent exact8193 = Boundary(short.MinValue);
        LanConnectRoomChatReadyEnvelope ready = new()
        {
            RoomId = roomId,
            RoomSessionId = sessionId,
            EnabledFeatures = features
        };

        LanConnectDraftMeasure atBoundary = DraftFrom(exact8192).Measure(features, senderName);
        LanConnectDraftMeasure overBoundary = DraftFrom(exact8193).Measure(features, senderName);
        Assert.Equal(8192, atBoundary.WorstCaseInboundBytes);
        Assert.True(atBoundary.CanSubmit);
        Assert.Equal(8193, overBoundary.WorstCaseInboundBytes);
        Assert.False(overBoundary.CanSubmit);

        Assert.True(LanConnectLobbyRuntime.DecideRoomChatSend(
            exact8192,
            ready,
            roomId,
            sessionId,
            senderName).Enabled);
        LanConnectRoomChatSendDecision rejected = LanConnectLobbyRuntime.DecideRoomChatSend(
            exact8193,
            ready,
            roomId,
            sessionId,
            senderName);
        Assert.False(rejected.Enabled);
        Assert.Contains("room wire envelope exceeds budget", rejected.DisabledReason, StringComparison.Ordinal);
    }

    private static FakeContext ReadyContext() => new()
    {
        ActiveRoomSessionId = "session-1",
        Peers = { "net:owner", "net:applier", "net:target" },
        Names = { ["net:target"] = "Silent" },
        Powers =
        {
            ["MegaCrit.Strength"] = new LanConnectLocalPowerReference(
                "Strength",
                "Increases attack damage.")
        }
    };

    private sealed class FakeContext : ILanConnectRoomCombatContext
    {
        public string ActiveRoomSessionId { get; set; } = string.Empty;

        internal HashSet<string> Peers { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, LanConnectLocalPowerReference> Powers { get; } =
            new(StringComparer.Ordinal);

        internal bool Throw { get; set; }

        public bool IsCurrentPeer(string playerNetId)
        {
            ThrowIfRequested();
            return Peers.Contains(playerNetId);
        }

        public bool TryGetCurrentPeerName(string playerNetId, out string name)
        {
            ThrowIfRequested();
            return Names.TryGetValue(playerNetId, out name!);
        }

        public bool TryResolveLocalPower(
            string modelId,
            out LanConnectLocalPowerReference power)
        {
            ThrowIfRequested();
            return Powers.TryGetValue(modelId, out power);
        }

        private void ThrowIfRequested()
        {
            if (Throw)
            {
                throw new InvalidOperationException("context failed");
            }
        }
    }
}
