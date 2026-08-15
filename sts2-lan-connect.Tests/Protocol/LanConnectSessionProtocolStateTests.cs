using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectSessionProtocolStateTests
{
    [Fact]
    public void Frozen_selection_cannot_change_until_the_owner_lease_is_disposed()
    {
        LanConnectSessionProtocolState state = new();
        LanConnectProtocolSelection tail = Selection(LanConnectProtocolProfile.TailV1);
        LanConnectProtocolSelection compat = Selection(LanConnectProtocolProfile.Compat4x5V1);
        using LanConnectSessionProtocolLease lease = state.FreezeHost(tail, "room-a");

        Assert.Throws<LanConnectProtocolException>(() => state.FreezeClient(compat, "room-a"));
        Assert.Equal(tail, state.Current.Selection);
    }

    [Fact]
    public void Client_candidate_is_tentative_until_attached()
    {
        LanConnectSessionProtocolState state = new();
        using LanConnectSessionProtocolLease lease = state.FreezeClient(
            Selection(LanConnectProtocolProfile.TailV1),
            "ticket-a");

        Assert.Equal(LanConnectSessionProtocolPhase.Tentative, state.Current.Phase);
        lease.Attach();
        Assert.Equal(LanConnectSessionProtocolPhase.Frozen, state.Current.Phase);
    }

    [Fact]
    public void Candidate_retry_can_observe_the_same_lease_without_replacing_it()
    {
        LanConnectSessionProtocolState state = new();
        LanConnectProtocolSelection selection = Selection(LanConnectProtocolProfile.TailV1);
        using LanConnectSessionProtocolLease owner = state.FreezeClient(selection, "ticket-a");
        using LanConnectSessionProtocolLease retry = state.FreezeClient(selection, "ticket-a");

        retry.Dispose();

        Assert.Equal(selection, state.Current.Selection);
        Assert.Equal(LanConnectSessionProtocolPhase.Tentative, state.Current.Phase);
    }

    [Fact]
    public void Wrong_owner_cannot_reset_and_owner_disposal_releases_selection()
    {
        LanConnectSessionProtocolState state = new();
        LanConnectSessionProtocolLease lease = state.FreezeHost(
            Selection(LanConnectProtocolProfile.Compat4x5V1),
            "room-a");

        Assert.False(state.TryReset("room-b"));
        Assert.NotEqual(LanConnectSessionProtocolPhase.Empty, state.Current.Phase);
        lease.Dispose();
        Assert.Equal(LanConnectSessionProtocolPhase.Empty, state.Current.Phase);
    }

    [Fact]
    public void Closing_state_rejects_idempotent_reattach()
    {
        LanConnectSessionProtocolState state = new();
        LanConnectProtocolSelection selection = Selection(LanConnectProtocolProfile.Compat4x5V1);
        using LanConnectSessionProtocolLease lease = state.FreezeHost(selection, "room-a");
        lease.MarkClosing();

        Assert.Throws<LanConnectProtocolException>(() => state.FreezeHost(selection, "room-a"));
        Assert.Equal(LanConnectSessionProtocolPhase.Closing, state.Current.Phase);
    }

    private static LanConnectProtocolSelection Selection(LanConnectProtocolProfile profile)
    {
        LanConnectProtocolCarrier carrier = profile == LanConnectProtocolProfile.Compat4x5V1
            ? LanConnectProtocolCarrier.None
            : LanConnectProtocolCarrier.StandaloneTailV1;
        LanConnectProtocolSelection selection = new(
            profile,
            profile == LanConnectProtocolProfile.Compat4x5V1 ? 0 : 1,
            carrier,
            profile == LanConnectProtocolProfile.Compat4x5V1 ? "0.3.0" : "0.6.0-alpha.1",
            8,
            "0.110.1",
            "aabb",
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }
}
