using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.ProtocolPlanTests;

public sealed class LanConnectTailPatchPlanTests
{
    [Fact]
    public void Desktop_plan_has_the_stable_thirty_step_generic_shape()
    {
        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly,
            isAndroid: false);

        Assert.Equal(LanConnectTailPatchPlan.DesktopProfile, plan.Profile);
        Assert.Equal(10, plan.ResolvedKinds.Count);
        Assert.Equal(9, plan.MessageTypes.Count);
        Assert.Equal(30, plan.Steps.Count);
        Assert.Equal(30, plan.Steps.Select(static step => step.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.True(plan.GenericTargetCount > 0);
        Assert.Equal(
            [
                "tail.serialize.initial_game_info",
                "tail.serialize.lobby_join_request",
                "tail.serialize.lobby_join_response",
                "tail.serialize.load_join_request",
                "tail.serialize.load_join_response",
                "tail.serialize.rejoin_request",
                "tail.serialize.rejoin_response",
                "tail.serialize.player_joined",
                "tail.serialize.lobby_begin_run",
                "tail.deserialize",
                "tail.receive.host",
                "tail.receive.client",
                "tail.host.broadcast.initial_game_info",
                "tail.host.targeted.initial_game_info",
                "tail.host.broadcast.lobby_join_request",
                "tail.host.targeted.lobby_join_request",
                "tail.host.broadcast.lobby_join_response",
                "tail.host.targeted.lobby_join_response",
                "tail.host.broadcast.load_join_request",
                "tail.host.targeted.load_join_request",
                "tail.host.broadcast.load_join_response",
                "tail.host.targeted.load_join_response",
                "tail.host.broadcast.rejoin_request",
                "tail.host.targeted.rejoin_request",
                "tail.host.broadcast.rejoin_response",
                "tail.host.targeted.rejoin_response",
                "tail.host.broadcast.player_joined",
                "tail.host.targeted.player_joined",
                "tail.host.broadcast.lobby_begin_run",
                "tail.host.targeted.lobby_begin_run"
            ],
            plan.Steps.Select(static step => step.Id));
    }

    [Fact]
    public void Android_plan_has_fifteen_concrete_non_generic_steps()
    {
        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly,
            isAndroid: true);

        Assert.Equal(LanConnectTailPatchPlan.AndroidProfile, plan.Profile);
        Assert.Equal(10, plan.ResolvedKinds.Count);
        Assert.Equal(9, plan.MessageTypes.Count);
        Assert.Equal(15, plan.Steps.Count);
        Assert.Equal(0, plan.GenericTargetCount);
        Assert.Equal(15, plan.Steps.Select(static step => step.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [
                "tail.android.serialize.initial_game_info",
                "tail.android.serialize.lobby_join_request",
                "tail.android.serialize.lobby_join_response",
                "tail.android.serialize.load_join_request",
                "tail.android.serialize.load_join_response",
                "tail.android.serialize.rejoin_request",
                "tail.android.serialize.rejoin_response",
                "tail.android.serialize.player_joined",
                "tail.android.serialize.lobby_begin_run",
                "tail.android.writer_reset",
                "tail.deserialize",
                "tail.receive.host",
                "tail.receive.client",
                "tail.android.transport.host",
                "tail.android.transport.client"
            ],
            plan.Steps.Select(static step => step.Id));

        foreach (LanConnectTailPatchStep step in plan.Steps)
        {
            Assert.False(step.Target.IsGenericMethod);
            Assert.False(step.Target.ContainsGenericParameters);
            Assert.False(step.Target.DeclaringType?.ContainsGenericParameters ?? false);
            Assert.All(step.Hooks, static hook =>
            {
                Assert.False(hook.IsGenericMethod);
                Assert.False(hook.ContainsGenericParameters);
                Assert.False(hook.DeclaringType?.ContainsGenericParameters ?? false);
            });
        }
    }

    [Fact]
    public void Ten_message_kinds_resolve_without_omission_to_nine_types()
    {
        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly,
            isAndroid: true);

        Assert.Equal(Enum.GetValues<LanConnectSidecarMessageKind>(), plan.ResolvedKinds.Select(static item => item.Kind));
        Assert.Equal(
            plan.ResolvedKinds.Single(static item => item.Kind == LanConnectSidecarMessageKind.InitialGameInfo).Type,
            plan.ResolvedKinds.Single(static item => item.Kind == LanConnectSidecarMessageKind.ConnectionFailed).Type);
        Assert.DoesNotContain(plan.ResolvedKinds, static item => item.Type == null);
    }
}
