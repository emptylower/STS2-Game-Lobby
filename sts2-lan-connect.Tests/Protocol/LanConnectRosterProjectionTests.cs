using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectRosterProjectionTests
{
    [Fact]
    public void Projects_the_first_four_canonical_players_with_vanilla_slots()
    {
        FakePlayer[] players =
        [
            new(80, 7), new(10, 0), new(50, 4), new(40, 3),
            new(20, 1), new(30, 2), new(60, 5), new(70, 6)
        ];

        IReadOnlyList<LanConnectRosterProjectionItem<FakePlayer>> projection = LanConnectRosterProjection.Create(
            players,
            static player => player.Id,
            static player => player.Slot,
            static (player, slot) => player with { Slot = slot });

        Assert.Equal([10UL, 20UL, 30UL, 40UL], projection.Select(item => item.PlayerId));
        Assert.Equal([0, 1, 2, 3], projection.Select(item => item.VanillaPlayer.Slot));
    }

    [Fact]
    public void Restores_real_slots_only_after_exact_identity_slot_and_bit_consumption()
    {
        LanConnectRosterSnapshot snapshot = Snapshot();
        IReadOnlyList<FakePlayer> restored = LanConnectRosterProjection.Restore(
            snapshot,
            carrier => (new FakePlayer(carrier.PlayerId, carrier.RealSlotId == 7 ? 1 : 0), carrier.VanillaPlayerBitLength),
            static player => player.Id,
            static player => player.Slot,
            static (player, slot) => player with { Slot = slot });

        Assert.Equal([0, 7], restored.Select(player => player.Slot));
        Assert.Throws<InvalidDataException>(() => LanConnectRosterProjection.Restore(
            snapshot,
            carrier => (new FakePlayer(carrier.PlayerId, 0), carrier.VanillaPlayerBitLength - 1),
            static player => player.Id,
            static player => player.Slot,
            static (player, slot) => player with { Slot = slot }));
    }

    [Fact]
    public void Authority_state_enforces_bootstrap_current_and_strict_mutation_revisions()
    {
        LanConnectRosterAuthorityState state = new(100);
        LanConnectRosterSnapshot revisionFive = Snapshot() with { AuthorityPeerId = 100, RosterRevision = 5 };
        state.Accept(100, revisionFive, LanConnectRosterSnapshotUse.Bootstrap, [11UL, 22UL]);
        state.Accept(100, revisionFive, LanConnectRosterSnapshotUse.CurrentState, [11UL, 22UL]);

        Assert.Throws<InvalidDataException>(() => state.Accept(
            100,
            revisionFive with { Players = [revisionFive.Players[0], new(22, 6, 8, [0xaa])] },
            LanConnectRosterSnapshotUse.CurrentState));
        Assert.Throws<InvalidDataException>(() => state.Accept(
            100,
            revisionFive,
            LanConnectRosterSnapshotUse.MembershipMutation));

        LanConnectRosterSnapshot mutation = revisionFive with
        {
            RosterRevision = 6,
            Players = [.. revisionFive.Players, new LanConnectRosterPlayerCarrier(33, 6, 8, [0])]
        };
        state.Accept(100, mutation, LanConnectRosterSnapshotUse.MembershipMutation, [11UL, 22UL, 33UL], 33);
        Assert.Equal(6u, state.Current!.RosterRevision);
    }

    [Fact]
    public void Host_revision_increments_once_only_for_real_snapshot_change()
    {
        LanConnectRosterAuthorityState state = new(100);
        LanConnectRosterSnapshot initial = state.CommitHostSnapshot(Snapshot().Players);
        LanConnectRosterSnapshot unchanged = state.CommitHostSnapshot(Snapshot().Players.Reverse().ToArray());
        LanConnectRosterSnapshot changed = state.CommitHostSnapshot(
            [Snapshot().Players[0], new LanConnectRosterPlayerCarrier(22, 6, 8, [0xaa])]);

        Assert.Equal(1u, initial.RosterRevision);
        Assert.Equal(1u, unchanged.RosterRevision);
        Assert.Equal(2u, changed.RosterRevision);
    }

    private static LanConnectRosterSnapshot Snapshot() => new(
        100,
        1,
        [new(11, 0, 3, [0x05]), new(22, 7, 8, [0xaa])]);

    private sealed record FakePlayer(ulong Id, int Slot);
}
