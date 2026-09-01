using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.ProtocolPlanTests;

public sealed class LanConnectTailPatchPlanTests
{
    [Fact]
    public void Native_plan_has_the_stable_sixteen_step_non_generic_shape()
    {
        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly);

        Assert.Equal("native_bus_v1", plan.Profile);
        Assert.Equal(10, plan.ResolvedKinds.Count);
        Assert.Equal(9, plan.MessageTypes.Count);
        Assert.Equal(16, plan.Steps.Count);
        Assert.Equal(16, plan.Steps.Select(static step => step.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(0, plan.GenericTargetCount);
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
                "tail.writer_reset",
                "tail.receive.host",
                "tail.receive.client",
                "tail.deserialize",
                "tail.dispatch_barrier",
                "tail.transport.host",
                "tail.transport.client"
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
    public void Native_plan_patches_no_generic_targets_and_no_buffer_toggle()
    {
        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly);

        Assert.DoesNotContain(
            plan.Steps,
            static step => step.Target.Name == nameof(NetMessageBus.SetBufferMessages));
        Assert.DoesNotContain(
            plan.Steps,
            static step => step.Target.Name is nameof(NetMessageBus.SerializeMessage)
                or "SendMessage" or "SendMessageToClientInternal");
        Assert.Equal(2, plan.Steps.Count(static step => step.Category == "transport"));
        Assert.Equal(1, plan.Steps.Count(static step => step.Category == "dispatch_barrier"));
        Assert.All(
            plan.Steps.Where(static step => step.Category == "transport"),
            static step => Assert.NotNull(step.Prefix));
    }

    [Fact]
    public void Ten_message_kinds_resolve_without_omission_to_nine_types()
    {
        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly);

        Assert.Equal(Enum.GetValues<LanConnectSidecarMessageKind>(), plan.ResolvedKinds.Select(static item => item.Kind));
        Assert.Equal(
            plan.ResolvedKinds.Single(static item => item.Kind == LanConnectSidecarMessageKind.InitialGameInfo).Type,
            plan.ResolvedKinds.Single(static item => item.Kind == LanConnectSidecarMessageKind.ConnectionFailed).Type);
        Assert.DoesNotContain(plan.ResolvedKinds, static item => item.Type == null);
    }
}
