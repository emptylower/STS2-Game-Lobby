using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.ProtocolPlanTests;

public sealed class LanConnectTailPatchPlanTests
{
    [Fact]
    public void Desktop_serialize_steps_target_the_message_bus_SerializeMessage_instantiation()
    {
        // 2026-09-05 本机双实例复现：RitsuLib 0.5.18 给 NetMessageBus.SerializeMessage<InitialGameInfoMessage>
        // 打补丁后，Harmony 生成的优化 DynamicMethod 会把小结构体 InitialGameInfoMessage.Serialize 内联进去，
        // 我们挂在 T.Serialize 上的 prefix 永远不触发（无 RitsuLib 时触发）。桌面平台的容器生产 seam 必须
        // 挂在 SerializeMessage<T> 的具体实例化上（安卓 gshared 不支持闭合泛型目标，保持 T.Serialize）。
        if (OperatingSystem.IsAndroid())
        {
            return;
        }

        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly);
        foreach (LanConnectTailPatchStep step in plan.Steps.Where(static step => step.Category == "serialize"))
        {
            Assert.Equal(typeof(NetMessageBus), step.Target.DeclaringType);
            Assert.Equal(nameof(NetMessageBus.SerializeMessage), step.Target.Name);
            Assert.True(step.Target.IsGenericMethod && !step.Target.IsGenericMethodDefinition);
            Assert.Equal(step.MessageType, step.Target.GetGenericArguments()[0]);
        }
    }

    [Fact]
    public void Native_plan_has_the_stable_sixteen_step_shape()
    {
        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly);

        Assert.Equal("native_bus_v1", plan.Profile);
        Assert.Equal(10, plan.ResolvedKinds.Count);
        Assert.Equal(9, plan.MessageTypes.Count);
        Assert.Equal(16, plan.Steps.Count);
        Assert.Equal(16, plan.Steps.Select(static step => step.Id).Distinct(StringComparer.Ordinal).Count());
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

        if (OperatingSystem.IsAndroid())
        {
            // 安卓 gshared：全部目标非泛型（闭合泛型 wrapper 不可用）。
            Assert.Equal(0, plan.GenericTargetCount);
            foreach (LanConnectTailPatchStep step in plan.Steps)
            {
                Assert.False(step.Target.IsGenericMethod);
                Assert.False(step.Target.ContainsGenericParameters);
                Assert.False(step.Target.DeclaringType?.ContainsGenericParameters ?? false);
            }
        }
        else
        {
            // 桌面：仅 serialize 类别的 9 步是 SerializeMessage 闭合实例化，其余目标非泛型。
            Assert.Equal(9, plan.GenericTargetCount);
            foreach (LanConnectTailPatchStep step in plan.Steps)
            {
                if (step.Category == "serialize")
                {
                    Assert.True(step.Target.IsGenericMethod && !step.Target.IsGenericMethodDefinition);
                    Assert.False(step.Target.ContainsGenericParameters);
                    Assert.NotNull(step.FallbackTarget);
                    Assert.NotNull(step.FallbackPrefix);
                    Assert.False(step.FallbackTarget.IsGenericMethod);
                }
                else
                {
                    Assert.False(step.Target.IsGenericMethod);
                    Assert.False(step.Target.ContainsGenericParameters);
                    Assert.Null(step.FallbackTarget);
                    Assert.Null(step.FallbackPrefix);
                }
            }
        }

        // Hooks（prefix/postfix/finalizer/回退 prefix）永远是非泛型具体方法。
        foreach (LanConnectTailPatchStep step in plan.Steps)
        {
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
        // SerializeMessage 目标按平台语义出现：桌面 9 个 serialize 步、安卓 0 个；
        // SendMessage / SendMessageToClientInternal 任何平台都不允许。
        Assert.Equal(
            OperatingSystem.IsAndroid() ? 0 : 9,
            plan.Steps.Count(static step => step.Target.Name == nameof(NetMessageBus.SerializeMessage)));
        Assert.DoesNotContain(
            plan.Steps,
            static step => step.Target.Name is "SendMessage" or "SendMessageToClientInternal");
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
