using GdUnit4;
using HarmonyLib;
using System.Reflection;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectTailMessageBusTests
{
    [TestCase]
    public void Default_plan_installs_all_concrete_non_generic_serializer_hooks()
    {
        Harmony harmony = new($"sts2_lan_connect.tests.android_tail_plan.{Guid.NewGuid():N}");

        try
        {
            LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
                typeof(PacketWriter).Assembly);
            LanConnectTailMessagePatches.ApplyPlanForTesting(harmony, plan);

            AssertInt(plan.Steps.Count).IsEqual(16);
            AssertInt(plan.GenericTargetCount).IsEqual(0);
            foreach (LanConnectTailPatchStep step in plan.Steps.Take(9))
            {
                Patch[] prefixes = Harmony.GetPatchInfo(step.Target)!.Prefixes
                    .Where(patch => patch.owner == harmony.Id)
                    .ToArray();
                Patch[] postfixes = Harmony.GetPatchInfo(step.Target)!.Postfixes
                    .Where(patch => patch.owner == harmony.Id)
                    .ToArray();
                AssertInt(prefixes.Length).IsEqual(1);
                AssertInt(postfixes.Length).IsEqual(1);
                AssertBool(prefixes[0].PatchMethod.IsGenericMethod).IsFalse();
                AssertBool(prefixes[0].PatchMethod.ContainsGenericParameters).IsFalse();
                AssertBool(postfixes[0].PatchMethod.IsGenericMethod).IsFalse();
                AssertBool(postfixes[0].PatchMethod.ContainsGenericParameters).IsFalse();
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [TestCase]
    public void Outgoing_tail_hooks_are_concrete_and_never_touch_closed_generics()
    {
        Harmony harmony = new($"sts2_lan_connect.tests.tail_prefixes.{Guid.NewGuid():N}");

        try
        {
            LanConnectTailMessagePatches.Apply(harmony);
            Assembly assembly = typeof(PacketWriter).Assembly;
            Type[] messageTypes = Enum.GetValues<LanConnectSidecarMessageKind>()
                .Select(kind => assembly.GetType(
                    $"MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.{LanConnectTailMessageTypeMatrix.GetTypeName(kind)}",
                    throwOnError: true,
                    ignoreCase: false)!)
                .Distinct()
                .ToArray();

            foreach (Type messageType in messageTypes)
            {
                // The default plan patches the concrete per-type Serialize method with
                // non-generic hooks...
                MethodInfo concreteSerialize = AccessTools.Method(messageType, "Serialize", [typeof(PacketWriter)])!;
                Patch[] prefixes = Harmony.GetPatchInfo(concreteSerialize)!.Prefixes
                    .Where(patch => patch.owner == harmony.Id)
                    .ToArray();
                Patch[] postfixes = Harmony.GetPatchInfo(concreteSerialize)!.Postfixes
                    .Where(patch => patch.owner == harmony.Id)
                    .ToArray();

                AssertThat(prefixes.Length).IsEqual(1);
                AssertThat(prefixes[0].PatchMethod.IsGenericMethod).IsFalse();
                AssertThat(prefixes[0].PatchMethod.ContainsGenericParameters).IsFalse();
                AssertThat(postfixes.Length).IsEqual(1);
                AssertThat(postfixes[0].PatchMethod.IsGenericMethod).IsFalse();
                AssertThat(postfixes[0].PatchMethod.ContainsGenericParameters).IsFalse();

                // ...and never touches the closed generic bus serializer, which foreign
                // generic-declared patches can poison (RitsuLib conflict, see alpha.9 F2).
                MethodInfo busSerialize = ResolveClosedBusSerializerForTesting(
                    typeof(NetMessageBus),
                    messageType);
                Patches? busPatches = Harmony.GetPatchInfo(busSerialize);
                AssertThat(busPatches?.Prefixes.Count(patch => patch.owner == harmony.Id) ?? 0).IsEqual(0);
                AssertThat(busPatches?.Postfixes.Count(patch => patch.owner == harmony.Id) ?? 0).IsEqual(0);
                AssertThat(busPatches?.Transpilers.Count(patch => patch.owner == harmony.Id) ?? 0).IsEqual(0);
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [TestCase]
    public void Native_deserialize_guards_intercept_short_frames_and_offset9_native_frames()
    {
        Harmony harmony = new($"sts2_lan_connect.tests.tail_bus.{Guid.NewGuid():N}");
        FakeRuntime runtime = new();
        using LanConnectSessionProtocolLease lease =
            LanConnectSessionProtocolState.Shared.FreezeHost(Selection(), "native-bus-guards");

        try
        {
            LanConnectTailMessagePatches.ConfigureRuntime(runtime);
            LanConnectNativeBusSender.TypeIdResolverForTesting = () => 200;
            NetMessageBus bus = new(new PacketReader(), new PacketWriter());
            AssemblyInfo.Init();
            typeof(MessageTypes).GetField("_cache", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, new NetTypeCache<INetMessage>(INetMessageSubtypes.All.ToList()));
            LanConnectTailMessagePatches.Apply(harmony);

            // ① <9 字节且首字节为本类型 ID：原版读取 senderId 会越界，prefix 拦截并转结构化失败。
            byte[] shortFrame = [(byte)200, 0x01, 0x02, 0x03];
            using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(41))
            {
                AssertThat(bus.TryDeserializeMessage(shortFrame, out _, out _)).IsFalse();
            }
            AssertThat(runtime.RejectedSenders).Contains(41UL);

            // ② 未知 ID + offset-9 完整外层帧：升级为 lan_type_id_mismatch 结构化失败。
            LanConnectSidecarFrame frame = new(
                LanConnectSidecarMessageKind.LobbyBeginRun,
                new byte[LanConnectSidecarFrameCodec.FlowNonceBytes],
                1,
                LanConnectTailCodec.Encode(
                    1,
                    [new LanConnectTailEntry(
                        LanConnectTailEntry.CapabilitiesId,
                        1,
                        true,
                        LanConnectCapabilitiesCodec.EncodeSessionSelection(Selection()))]));
            LanConnectNativeBusMessage native = new();
            native.Configure(200, LanConnectSidecarFrameCodec.Encode(frame));
            PacketWriter wire = new() { WarnOnGrow = false };
            wire.WriteByte(199); // 未知 typeId
            wire.WriteULong(41);
            native.Serialize(wire);
            byte[] displaced = wire.Buffer.AsSpan(0, wire.BytePosition).ToArray();
            using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(41))
            {
                AssertThat(bus.TryDeserializeMessage(displaced, out _, out _)).IsFalse();
            }
            AssertThat(runtime.RejectedSenders.Contains(41UL)).IsTrue();

            // ③ 第三方消息 payload 恰以前缀相似开头（magic/ver 相同但 frameLen 越界）：
            //    维持原版"警告后丢弃"，不误伤断开。
            byte[] prefixSimilar = (byte[])displaced.Clone();
            prefixSimilar[17] = 0xFF;
            prefixSimilar[18] = 0xFF;
            prefixSimilar[19] = 0xFF;
            prefixSimilar[20] = 0xFF;
            runtime.RejectedSenders.Clear();
            using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(41))
            {
                AssertThat(bus.TryDeserializeMessage(prefixSimilar, out _, out _)).IsFalse();
            }
            AssertThat(runtime.RejectedSenders.Count).IsEqual(0);
        }
        finally
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
            harmony.UnpatchAll(harmony.Id);
            LanConnectTailMessagePatches.ConfigureRuntime(LanConnectTailMessageRuntime.Shared);
        }
    }

    [TestCase]
    public void Ritsu_sidecar_capability_is_absent_without_the_public_assembly()
    {
        LanConnectExternalCapabilitySnapshot snapshot = LanConnectExternalCapabilityCollector.Collect([]);
        AssertThat(snapshot.RitsuLibPresent).IsFalse();
        AssertThat(snapshot.LegacySidecarAvailable).IsFalse();
    }

    // 测试专用：解析闭环泛型总线 serializer（生产端解析器已删除；本断言只证明我们不碰它）。
    private static MethodInfo ResolveClosedBusSerializerForTesting(Type busType, Type messageType) =>
        busType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == nameof(NetMessageBus.SerializeMessage)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 3)
            .MakeGenericMethod(messageType);

    private static LanConnectProtocolOffer Offer() =>
        new(1, 1, "0.6.1-alpha.1", false, false);

    private static LanConnectProtocolSelection Selection()
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            1,
            LanConnectProtocolCarrier.NativeBusV1,
            "0.6.1-alpha.1",
            8,
            "0.110.1",
            "aabb",
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }

    private sealed class FakeRuntime : ILanConnectTailMessageRuntime
    {
        internal List<LanConnectSidecarMessageKind> PreparedKinds { get; } = [];
        internal List<ulong> ValidatedSenders { get; } = [];
        internal List<ulong> RejectedSenders { get; } = [];

        public LanConnectPreparedTailMessage PrepareOutgoing(
            NetMessageBus messageBus,
            LanConnectSidecarMessageKind messageKind,
            ulong senderPeerId,
            object message,
            LanConnectProtocolSelection selection)
        {
            PreparedKinds.Add(messageKind);
            byte[] capabilities = LanConnectCapabilitiesCodec.EncodeSessionSelection(selection);
            return new LanConnectPreparedTailMessage(
                message,
                LanConnectTailCodec.Encode(
                    1,
                    [new LanConnectTailEntry(LanConnectTailEntry.CapabilitiesId, 1, true, capabilities)]));
        }

        public bool TryPrepareConcreteOutgoing(
            PacketWriter writer,
            LanConnectSidecarMessageKind messageKind,
            object message,
            out LanConnectNativePreparedMessage? prepared)
        {
            LanConnectProtocolSelection? selection = LanConnectSessionProtocolState.Shared.Current.Selection;
            LanConnectPreparedTailMessage runtimePrepared =
                PrepareOutgoing(null!, messageKind, 0, message, selection!);
            prepared = new LanConnectNativePreparedMessage(null!, writer, messageKind, 0, selection!, runtimePrepared);
            return true;
        }

        public void CompleteConcreteOutgoing(LanConnectNativePreparedMessage prepared)
        {
        }

        public void ClearPendingOutgoing(PacketWriter writer)
        {
        }

        public LanConnectNativeSendContext? BeginNativeTransport(
            object transport,
            bool isHostTransport,
            ulong recipientPeerId,
            byte[] buffer,
            int length) => null;

        public void CompleteNativeTransport(LanConnectNativeSendContext? state, bool vanillaPeerReachable)
        {
        }

        public void HandleNativeTransportFailure(LanConnectNativeSendContext? state, Exception exception)
        {
        }

        public bool TryEnterNativeDispatch(NetMessageBus messageBus, INetMessage message, ulong senderId) => true;

        public void HandleIncomingFailure(
            NetMessageBus messageBus,
            ulong transportSenderPeerId,
            Exception exception,
            LanConnectProtocolSelection selection)
        {
            RejectedSenders.Add(transportSenderPeerId);
        }
    }
}
