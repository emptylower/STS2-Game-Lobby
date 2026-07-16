using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectMonsterTargetIdPrototypeTests
{
    [Fact]
    public void Default_production_gate_rejects_every_unstable_fallback_source()
    {
        LanConnectMonsterTargetIdPrototype prototype = new(adapter: null);
        object[] unstableSources =
        [
            "/root/CombatRoom/EnemyContainer/Monster",
            "Localized Monster Name",
            "same-model-index:0",
            new object().GetHashCode(),
            123456789UL,
            Guid.NewGuid().ToString("D")
        ];

        Assert.False(prototype.IsEnabled);
        foreach (object source in unstableSources)
        {
            Assert.False(prototype.TryGetStableId(source, out string stableId));
            Assert.Equal(string.Empty, stableId);
        }
    }

    [Theory]
    [InlineData(false, "MegaCrit.Sts2.PublicNetworkMonster.Id")]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public void Adapter_requires_network_replication_and_exact_public_member(
        bool isNetworkReplicated,
        string publicMember)
    {
        FakeAdapter adapter = new(publicMember, isNetworkReplicated);
        object monster = new();
        adapter.Add(monster, "monster:1");
        LanConnectMonsterTargetIdPrototype prototype = new(adapter);

        Assert.False(prototype.IsEnabled);
        Assert.False(prototype.TryGetStableId(monster, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\nid")]
    [InlineData("bad\u007fid")]
    [InlineData("怪物")]
    public void Stable_id_must_be_1_to_128_printable_ascii(string stableId)
    {
        FakeAdapter adapter = VerifiedAdapter();
        object monster = new();
        adapter.Add(monster, stableId);

        Assert.False(new LanConnectMonsterTargetIdPrototype(adapter).TryGetStableId(
            monster,
            out _));
    }

    [Fact]
    public void Stable_id_accepts_exact_ascii_boundaries_and_requires_receive_resolution()
    {
        FakeAdapter adapter = VerifiedAdapter();
        object first = new();
        object last = new();
        adapter.Add(first, "M");
        adapter.Add(last, new string('Z', 128));
        LanConnectMonsterTargetIdPrototype prototype = new(adapter);

        Assert.True(prototype.TryGetStableId(first, out string firstId));
        Assert.Equal("M", firstId);
        Assert.True(prototype.TryGetStableId(last, out string lastId));
        Assert.Equal(128, lastId.Length);

        adapter.RemoveResolution(firstId);
        Assert.False(prototype.TryGetStableId(first, out _));
        Assert.False(prototype.CanResolveStableId(firstId));
        Assert.False(prototype.CanResolveStableId(new string('X', 129)));
    }

    [Fact]
    public void Stable_id_rejects_a_129_character_capture_and_a_different_resolved_object()
    {
        FakeAdapter adapter = VerifiedAdapter();
        object oversizedMonster = new();
        object mismatchedMonster = new();
        adapter.Add(oversizedMonster, new string('X', 129));
        adapter.Add(mismatchedMonster, "monster:mismatch");
        adapter.ReplaceResolution("monster:mismatch", new object());
        LanConnectMonsterTargetIdPrototype prototype = new(adapter);

        Assert.False(prototype.TryGetStableId(oversizedMonster, out _));
        Assert.False(prototype.TryGetStableId(mismatchedMonster, out _));
    }

    [Fact]
    public void Adapter_exceptions_fail_closed()
    {
        FakeAdapter adapter = VerifiedAdapter();
        object monster = new();
        adapter.Add(monster, "monster:1");
        adapter.Throw = true;
        LanConnectMonsterTargetIdPrototype prototype = new(adapter);

        Assert.False(prototype.TryGetStableId(monster, out _));
        Assert.False(prototype.CanResolveStableId("monster:1"));

        adapter.Throw = false;
        adapter.ThrowOnMetadata = true;
        Assert.False(prototype.IsEnabled);
        Assert.False(prototype.TryGetStableId(monster, out _));
        Assert.False(prototype.CanResolveStableId("monster:1"));
    }

    [Fact]
    public void Resolver_and_content_gate_keep_monsters_off_without_proof_but_preserve_power_and_player()
    {
        FakeCombatContext context = new() { ActiveRoomSessionId = "session-1" };
        context.Peers.Add("net:player");
        context.Powers.Add("MegaCrit.Strength");
        object monster = new();
        LanConnectRoomCombatReferenceResolver defaultResolver = new(context);

        Assert.False(defaultResolver.TryCreateMonsterTargetRun(
            new LanConnectMonsterTargetCaptureCandidate(monster, "session-1"),
            out _));
        Assert.False(LanConnectChatFeatureResolver.MonsterTargetRefsEnabled);
        LanConnectCombatRun receivedMonster = new(
            new LanConnectTargetRefSegment("monster", "monster:1", "session-1"));
        Assert.False(defaultResolver.CanCommit(receivedMonster));
        Assert.Equal(
            LanConnectResolvedCombatReferenceStatus.TargetExpired,
            defaultResolver.Resolve(receivedMonster, "en").Status);
        LanConnectChatFeatureVersions combat = new(1, 1, 1, 1);
        Assert.False(LanConnectChatFeatureResolver.SupportsContent(
            new(1, [new LanConnectTargetRefSegment("monster", "monster:1", "session-1")]),
            combat));
        Assert.True(LanConnectChatFeatureResolver.SupportsContent(
            new(1, [new LanConnectPowerStateSegment("MegaCrit.Strength", 1, "session-1")]),
            combat));
        Assert.True(LanConnectChatFeatureResolver.SupportsContent(
            new(1, [new LanConnectTargetRefSegment("player", "net:player", "session-1")]),
            combat));

        context.ActiveRoomSessionId = "session-2";
        Assert.False(defaultResolver.TryCreateMonsterTargetRun(
            new LanConnectMonsterTargetCaptureCandidate(monster, "session-1"),
            out _));
    }

    [Fact]
    public void Verified_adapter_only_proves_prototype_contract_and_does_not_unlock_combat_v1()
    {
        object monster = new();
        FakeAdapter adapter = VerifiedAdapter();
        adapter.Add(monster, "monster:1");
        LanConnectMonsterTargetIdPrototype prototype = new(adapter);

        Assert.True(prototype.TryGetStableId(monster, out string stableId));
        Assert.Equal("monster:1", stableId);
        Assert.True(prototype.CanResolveStableId(stableId));

        FakeCombatContext context = new() { ActiveRoomSessionId = "session-1" };
        LanConnectRoomCombatReferenceResolver productionResolver = new(context);
        Assert.False(productionResolver.TryCreateMonsterTargetRun(
            new LanConnectMonsterTargetCaptureCandidate(monster, "session-1"),
            out _));
        Assert.False(LanConnectChatFeatureResolver.SupportsContent(
            new(1, [new LanConnectTargetRefSegment("monster", stableId, "session-1")]),
            new(1, 1, 1, 1)));
    }

    [Fact(Skip = LanConnectMonsterTargetIdPrototype.MissingProofReason)]
    public void Two_client_stable_monster_id_proof()
    {
        TwoClientMonsterProofEvidence evidence = new(
            ClientAFirstMonsterId: string.Empty,
            ClientBFirstMonsterId: string.Empty,
            ClientASecondSameModelMonsterId: string.Empty,
            ClientBSecondSameModelMonsterId: string.Empty,
            ClientAResolvesBoth: false,
            ClientBResolvesBoth: false,
            ClientAResolvesFirstAfterDespawn: true,
            ClientBResolvesFirstAfterDespawn: true,
            NextCombatFirstMonsterId: string.Empty);

        Assert.Equal(evidence.ClientAFirstMonsterId, evidence.ClientBFirstMonsterId);
        Assert.NotEmpty(evidence.ClientAFirstMonsterId);
        Assert.Equal(
            evidence.ClientASecondSameModelMonsterId,
            evidence.ClientBSecondSameModelMonsterId);
        Assert.NotEmpty(evidence.ClientASecondSameModelMonsterId);
        Assert.NotEqual(
            evidence.ClientAFirstMonsterId,
            evidence.ClientASecondSameModelMonsterId);
        Assert.True(evidence.ClientAResolvesBoth);
        Assert.True(evidence.ClientBResolvesBoth);
        Assert.False(evidence.ClientAResolvesFirstAfterDespawn);
        Assert.False(evidence.ClientBResolvesFirstAfterDespawn);
        Assert.NotEqual(evidence.ClientAFirstMonsterId, evidence.NextCombatFirstMonsterId);
    }

    private static FakeAdapter VerifiedAdapter() => new(
        "MegaCrit.Sts2.PublicNetworkMonster.Id",
        isNetworkReplicated: true);

    private sealed class FakeAdapter(
        string publicNetworkIdMember,
        bool isNetworkReplicated) : ILanConnectStableMonsterIdAdapter
    {
        private readonly Dictionary<object, string> _ids = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, object> _resolved = new(StringComparer.Ordinal);

        public string PublicNetworkIdMember
        {
            get
            {
                ThrowIfMetadataRequested();
                return publicNetworkIdMember;
            }
        }

        public bool IsNetworkReplicated
        {
            get
            {
                ThrowIfMetadataRequested();
                return isNetworkReplicated;
            }
        }

        internal bool Throw { get; set; }

        internal bool ThrowOnMetadata { get; set; }

        internal void Add(object monster, string stableId)
        {
            _ids[monster] = stableId;
            _resolved[stableId] = monster;
        }

        internal void RemoveResolution(string stableId) => _resolved.Remove(stableId);

        internal void ReplaceResolution(string stableId, object monster) =>
            _resolved[stableId] = monster;

        public bool TryGetNetworkId(object monster, out string stableId)
        {
            ThrowIfRequested();
            return _ids.TryGetValue(monster, out stableId!);
        }

        public bool TryResolveNetworkId(string stableId, out object monster)
        {
            ThrowIfRequested();
            return _resolved.TryGetValue(stableId, out monster!);
        }

        private void ThrowIfRequested()
        {
            if (Throw)
            {
                throw new InvalidOperationException("adapter failed");
            }
        }

        private void ThrowIfMetadataRequested()
        {
            if (ThrowOnMetadata)
            {
                throw new InvalidOperationException("adapter metadata failed");
            }
        }
    }

    private sealed record TwoClientMonsterProofEvidence(
        string ClientAFirstMonsterId,
        string ClientBFirstMonsterId,
        string ClientASecondSameModelMonsterId,
        string ClientBSecondSameModelMonsterId,
        bool ClientAResolvesBoth,
        bool ClientBResolvesBoth,
        bool ClientAResolvesFirstAfterDespawn,
        bool ClientBResolvesFirstAfterDespawn,
        string NextCombatFirstMonsterId);

    private sealed class FakeCombatContext : ILanConnectRoomCombatContext
    {
        public string ActiveRoomSessionId { get; set; } = string.Empty;

        internal HashSet<string> Peers { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> Powers { get; } = new(StringComparer.Ordinal);

        public bool IsCurrentPeer(string playerNetId) => Peers.Contains(playerNetId);

        public bool TryGetCurrentPeerName(string playerNetId, out string name)
        {
            name = playerNetId;
            return Peers.Contains(playerNetId);
        }

        public bool TryResolveLocalPower(
            string modelId,
            out LanConnectLocalPowerReference power)
        {
            power = new LanConnectLocalPowerReference(modelId, modelId);
            return Powers.Contains(modelId);
        }
    }
}
