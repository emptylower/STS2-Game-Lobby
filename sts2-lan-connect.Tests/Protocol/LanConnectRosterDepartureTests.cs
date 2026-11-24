using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectRosterDepartureTests
{
    private const ulong HostId = 100;

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void Observed_departure_allows_remaining_players_to_begin(int count)
    {
        (LanConnectRosterAuthorityState host, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(count);
        ulong departed = initial.Players[^1].PlayerId;
        client.RecordLobbyDeparture(HostId, departed);
        host.RecordHostLobbyDeparture(departed);
        LanConnectRosterSnapshot next = host.CommitHostSnapshot(initial.Players.Take(initial.Players.Count - 1).ToArray());

        client.Accept(HostId, next, LanConnectRosterSnapshotUse.StateTransition);

        Assert.Equal(2u, client.Current!.RosterRevision);
        Assert.DoesNotContain(client.Current.Players, player => player.PlayerId == departed);
    }

    [Fact]
    public void Multiple_departures_coalesce_into_one_host_snapshot_revision()
    {
        (LanConnectRosterAuthorityState host, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(5);
        foreach (ulong departed in initial.Players.Skip(3).Select(static player => player.PlayerId))
        {
            client.RecordLobbyDeparture(HostId, departed);
            host.RecordHostLobbyDeparture(departed);
        }
        LanConnectRosterSnapshot next = host.CommitHostSnapshot(initial.Players.Take(3).ToArray());

        client.Accept(HostId, next, LanConnectRosterSnapshotUse.StateTransition);

        Assert.Equal(2u, next.RosterRevision);
        Assert.Equal(3, client.Current!.Players.Count);
    }

    [Fact]
    public void Current_state_remains_strict_and_does_not_consume_pending_removals()
    {
        (LanConnectRosterAuthorityState host, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(5);
        ulong first = initial.Players[3].PlayerId;
        ulong second = initial.Players[4].PlayerId;
        client.RecordLobbyDeparture(HostId, first);
        client.RecordLobbyDeparture(HostId, second);
        LanConnectRosterSnapshot partial = host.CommitHostSnapshot(initial.Players.Where(player => player.PlayerId != first).ToArray());
        Assert.Throws<InvalidDataException>(() => client.Accept(HostId, partial, LanConnectRosterSnapshotUse.CurrentState));
        client.Accept(HostId, partial, LanConnectRosterSnapshotUse.StateTransition);
        LanConnectRosterSnapshot next = host.CommitHostSnapshot(initial.Players.Take(3).ToArray());

        client.Accept(HostId, next, LanConnectRosterSnapshotUse.StateTransition);

        Assert.Equal(3u, client.Current!.RosterRevision);
        Assert.Equal(3, client.Current.Players.Count);
    }

    [Fact]
    public void A_pending_departure_does_not_authorize_other_removals_or_mutable_current_state()
    {
        (_, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(4);
        client.RecordLobbyDeparture(HostId, initial.Players[3].PlayerId);
        LanConnectRosterSnapshot unrelatedRemoval = initial with
        {
            RosterRevision = 2,
            Players = initial.Players.Where(player => player.PlayerId != initial.Players[1].PlayerId).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => client.Accept(
            HostId, unrelatedRemoval, LanConnectRosterSnapshotUse.StateTransition));
        LanConnectRosterSnapshot mutableOnly = initial with
        {
            RosterRevision = 2,
            Players = [Changed(initial.Players[0]), .. initial.Players.Skip(1)]
        };
        Assert.Throws<InvalidDataException>(() => client.Accept(
            HostId, mutableOnly, LanConnectRosterSnapshotUse.CurrentState));

        client.Accept(HostId, initial with { RosterRevision = 2, Players = initial.Players.Take(3).ToArray() },
            LanConnectRosterSnapshotUse.StateTransition);
        Assert.Equal(3, client.Current!.Players.Count);
    }

    [Fact]
    public void Departures_do_not_authorize_revision_jumps_or_changed_survivor_slots()
    {
        (_, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(3);
        client.RecordLobbyDeparture(HostId, initial.Players[2].PlayerId);
        LanConnectRosterSnapshot next = initial with { RosterRevision = 2, Players = initial.Players.Take(2).ToArray() };
        Assert.Throws<InvalidDataException>(() => client.Accept(
            HostId, next with { RosterRevision = 3 }, LanConnectRosterSnapshotUse.StateTransition));
        Assert.Throws<InvalidDataException>(() => client.Accept(
            HostId, next with { RosterRevision = 1 }, LanConnectRosterSnapshotUse.StateTransition));
        Assert.Throws<InvalidDataException>(() => client.Accept(HostId, next with
        {
            Players = [next.Players[0], new(next.Players[1].PlayerId, 7, 8, [1])]
        }, LanConnectRosterSnapshotUse.StateTransition));

        client.Accept(HostId, next, LanConnectRosterSnapshotUse.StateTransition);
        Assert.Throws<InvalidDataException>(() => client.Accept(
            HostId, initial, LanConnectRosterSnapshotUse.StateTransition));
    }

    [Fact]
    public void Same_identity_and_slot_reconnect_still_commits_and_accepts_exactly_one_new_revision()
    {
        (LanConnectRosterAuthorityState host, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(3);
        ulong reconnecting = initial.Players[2].PlayerId;
        client.RecordLobbyDeparture(HostId, reconnecting);
        host.RecordHostLobbyDeparture(reconnecting);
        LanConnectRosterSnapshot next = host.CommitHostSnapshot(initial.Players);

        client.Accept(HostId, next, LanConnectRosterSnapshotUse.MembershipMutation, connectedPeerId: reconnecting);

        Assert.Equal(2u, next.RosterRevision);
        Assert.Equal(2u, host.CommitHostSnapshot(initial.Players).RosterRevision);
        Assert.Throws<InvalidDataException>(() => client.Accept(
            HostId, next with { RosterRevision = 3, Players = next.Players.Take(2).ToArray() },
            LanConnectRosterSnapshotUse.StateTransition));
    }

    [Fact]
    public void Replacement_can_reuse_a_departed_slot_but_cannot_move_survivors()
    {
        (_, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(3);
        client.RecordLobbyDeparture(HostId, initial.Players[2].PlayerId);
        LanConnectRosterSnapshot replacement = initial with
        {
            RosterRevision = 2,
            Players = [.. initial.Players.Take(2), new(999, initial.Players[2].RealSlotId, 8, [1])]
        };
        Assert.Throws<InvalidDataException>(() => client.Accept(HostId, replacement with
        {
            Players = [replacement.Players[0], new(replacement.Players[1].PlayerId, 7, 8, [2]), replacement.Players[2]]
        }, LanConnectRosterSnapshotUse.MembershipMutation, connectedPeerId: 999));

        client.Accept(HostId, replacement, LanConnectRosterSnapshotUse.MembershipMutation, connectedPeerId: 999);

        Assert.Equal(999UL, client.Current!.Players[2].PlayerId);
    }

    [Fact]
    public void A_verified_same_id_reconnect_can_take_a_different_free_slot()
    {
        (_, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(3);
        ulong reconnecting = initial.Players[2].PlayerId;
        client.RecordLobbyDeparture(HostId, reconnecting);
        LanConnectRosterSnapshot next = initial with
        {
            RosterRevision = 2,
            Players = [.. initial.Players.Take(2), new(reconnecting, 7, 8, [1])]
        };

        client.Accept(HostId, next, LanConnectRosterSnapshotUse.MembershipMutation, connectedPeerId: reconnecting);

        Assert.Equal(7, client.Current!.Players[2].RealSlotId);
    }

    [Fact]
    public void Duplicate_join_requires_a_new_observed_departure()
    {
        (_, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(3);
        Assert.Throws<InvalidDataException>(() => client.Accept(
            HostId, initial with { RosterRevision = 2 }, LanConnectRosterSnapshotUse.MembershipMutation,
            connectedPeerId: initial.Players[2].PlayerId));
    }

    [Fact]
    public void Unknown_duplicate_and_prebootstrap_departures_do_not_expand_authority()
    {
        LanConnectRosterAuthorityState fresh = new(HostId);
        Assert.False(fresh.RecordLobbyDeparture(HostId, 22));
        Assert.False(fresh.RecordHostLobbyDeparture(22));
        (_, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(3);
        Assert.False(client.RecordLobbyDeparture(HostId, 999));
        Assert.True(client.RecordLobbyDeparture(HostId, initial.Players[2].PlayerId));
        Assert.False(client.RecordLobbyDeparture(HostId, initial.Players[2].PlayerId));
        Assert.Throws<InvalidDataException>(() => client.RecordLobbyDeparture(999, initial.Players[1].PlayerId));
        Assert.Throws<InvalidDataException>(() => client.RecordLobbyDeparture(HostId, HostId));
        Assert.Throws<InvalidDataException>(() => client.RecordHostLobbyDeparture(HostId));
    }

    [Fact]
    public void Late_host_disconnect_does_not_dirty_an_already_reduced_snapshot()
    {
        (LanConnectRosterAuthorityState host, _, LanConnectRosterSnapshot initial) = Pair(3);
        LanConnectRosterSnapshot reduced = host.CommitHostSnapshot(initial.Players.Take(2).ToArray());

        Assert.False(host.RecordHostLobbyDeparture(initial.Players[2].PlayerId));
        Assert.Equal(reduced.RosterRevision, host.CommitHostSnapshot(reduced.Players).RosterRevision);
    }

    [Fact]
    public void Host_dirty_evidence_survives_failed_encoding_and_is_ignored_by_non_lobby_snapshots()
    {
        (LanConnectRosterAuthorityState host, _, LanConnectRosterSnapshot initial) = Pair(3);
        host.RecordHostLobbyDeparture(initial.Players[2].PlayerId);
        Assert.Throws<InvalidDataException>(() => host.CommitHostSnapshot([initial.Players[0], initial.Players[0]]));
        Assert.Equal(1u, host.CommitHostSnapshot(initial.Players, includeLobbyDepartures: false).RosterRevision);
        Assert.Equal(2u, host.CommitHostSnapshot(initial.Players).RosterRevision);
    }

    [Fact]
    public void One_player_snapshot_stays_fail_closed_without_consuming_departure_evidence()
    {
        (LanConnectRosterAuthorityState host, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(2);
        ulong reconnecting = initial.Players[1].PlayerId;
        host.RecordHostLobbyDeparture(reconnecting);
        client.RecordLobbyDeparture(HostId, reconnecting);
        Assert.Throws<InvalidDataException>(() => host.CommitHostSnapshot([initial.Players[0]]));
        Assert.Throws<InvalidDataException>(() => client.Accept(HostId,
            initial with { RosterRevision = 2, Players = [initial.Players[0]] }, LanConnectRosterSnapshotUse.StateTransition));
        Assert.Equal(1u, host.Current!.RosterRevision);
        Assert.Equal(1u, client.Current!.RosterRevision);

        LanConnectRosterSnapshot reconnected = host.CommitHostSnapshot(initial.Players);
        client.Accept(HostId, reconnected, LanConnectRosterSnapshotUse.MembershipMutation, connectedPeerId: reconnecting);
        Assert.Equal(2u, client.Current!.RosterRevision);
    }

    [Fact]
    public void Phase_cleanup_and_reset_discard_departure_evidence_without_rebinding_current_state()
    {
        (LanConnectRosterAuthorityState host, LanConnectRosterAuthorityState client, LanConnectRosterSnapshot initial) = Pair(3);
        client.RecordLobbyDeparture(HostId, initial.Players[2].PlayerId);
        host.RecordHostLobbyDeparture(initial.Players[2].PlayerId);
        client.ClearLobbyDepartureEvidence();
        host.ClearLobbyDepartureEvidence();
        Assert.Equal(1u, host.CommitHostSnapshot(initial.Players).RosterRevision);
        Assert.Throws<InvalidDataException>(() => client.Accept(HostId, initial with
        {
            RosterRevision = 2, Players = initial.Players.Take(2).ToArray()
        }, LanConnectRosterSnapshotUse.StateTransition));

        client.RecordLobbyDeparture(HostId, initial.Players[2].PlayerId);
        client.Reset();
        client.Accept(HostId, initial, LanConnectRosterSnapshotUse.Bootstrap);
        Assert.Throws<InvalidDataException>(() => client.Accept(HostId, initial with
        {
            RosterRevision = 2, Players = initial.Players.Take(2).ToArray()
        }, LanConnectRosterSnapshotUse.StateTransition));
    }

    private static (LanConnectRosterAuthorityState Host, LanConnectRosterAuthorityState Client,
        LanConnectRosterSnapshot Snapshot) Pair(int count)
    {
        LanConnectRosterAuthorityState host = new(HostId);
        LanConnectRosterAuthorityState client = new(HostId);
        LanConnectRosterSnapshot initial = host.CommitHostSnapshot(Enumerable.Range(0, count)
            .Select(index => new LanConnectRosterPlayerCarrier(index == 0 ? HostId : (ulong)index,
                (byte)index, 8, [(byte)index])).ToArray());
        client.Accept(HostId, initial, LanConnectRosterSnapshotUse.Bootstrap);
        return (host, client, initial);
    }

    private static LanConnectRosterPlayerCarrier Changed(LanConnectRosterPlayerCarrier player) =>
        new(player.PlayerId, player.RealSlotId, 8, [0xff]);
}
