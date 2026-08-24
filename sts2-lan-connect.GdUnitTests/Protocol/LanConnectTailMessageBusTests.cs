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
                typeof(PacketWriter).Assembly,
                isAndroid: true);
            LanConnectTailMessagePatches.ApplyPlanForTesting(harmony, plan);

            AssertInt(plan.Steps.Count).IsEqual(15);
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
                MethodInfo busSerialize = LanConnectSerializationPatches.ResolveGenericSerializeMessageMethod(
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
    public void Standalone_tail_uses_authenticated_transport_sender_before_dispatch()
    {
        Harmony harmony = new($"sts2_lan_connect.tests.tail_bus.{Guid.NewGuid():N}");
        FakeRuntime runtime = new();
        LanConnectProtocolOffer offer = Offer();
        LanConnectProtocolSelection selection = Selection();
        using LanConnectSessionProtocolLease lease =
            LanConnectSessionProtocolState.Shared.FreezeHost(selection, harmony.Id);

        try
        {
            LanConnectTailMessagePatches.ConfigureRuntime(runtime);
            NetMessageBus bus = new(new PacketReader(), new PacketWriter());
            AssemblyInfo.Init();
            typeof(MessageTypes).GetField("_cache", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, new NetTypeCache<INetMessage>(INetMessageSubtypes.All.ToList()));
            LobbyBeginRunMessage request = new()
            {
                playersInLobby = [],
                seed = string.Empty,
                modifiers = [],
                act1 = string.Empty
            };
            _ = bus.SerializeMessage(41, request, out _);
            LanConnectTailMessagePatches.Apply(harmony);

            byte[] packet = bus.SerializeMessage(41, request, out int length)
                .AsSpan(0, length)
                .ToArray();
            using (LanConnectTailMessagePatches.PushTransportSenderForTesting(41))
            {
                bool decoded = bus.TryDeserializeMessage(packet, out INetMessage? message, out ulong? sender);
                AssertThat(decoded).IsTrue();
                AssertThat(message is LobbyBeginRunMessage).IsTrue();
                AssertThat(sender == 41).IsTrue();
            }

            AssertThat(runtime.PreparedKinds).Contains(LanConnectSidecarMessageKind.LobbyBeginRun);
            AssertThat(runtime.ValidatedSenders).Contains(41UL);

            bool decodedWithoutSenderContext = bus.TryDeserializeMessage(packet, out _, out _);
            AssertThat(decodedWithoutSenderContext).IsFalse();
            AssertThat(runtime.RejectedSenders.Contains(0UL)).IsFalse();

            using (LanConnectTailMessagePatches.PushTransportSenderForTesting(99))
            {
                bool decoded = bus.TryDeserializeMessage(packet, out _, out _);
                AssertThat(decoded).IsFalse();
            }
            AssertThat(runtime.RejectedSenders).Contains(99UL);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            LanConnectTailMessagePatches.ConfigureRuntime(LanConnectTailMessageRuntime.Shared);
        }
    }

    [TestCase]
    public void Ritsu_sidecar_capability_is_absent_without_the_public_assembly()
    {
        LanConnectExternalCapabilitySnapshot snapshot = LanConnectExternalCapabilityCollector.Collect([]);
        AssertThat(snapshot.RitsuLibPresent).IsFalse();
        AssertThat(snapshot.RitsuLibSidecarAvailable).IsFalse();
    }

    private static LanConnectProtocolOffer Offer() =>
        new(1, 1, "0.6.0-alpha.1", false, false);

    private static LanConnectProtocolSelection Selection()
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            1,
            LanConnectProtocolCarrier.StandaloneTailV1,
            "0.6.0-alpha.1",
            8,
            "0.110.1",
            "aabb",
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }

    private sealed class FakeRuntime : ILanConnectAndroidTailMessageRuntime
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
            out LanConnectAndroidPreparedMessage? prepared)
        {
            LanConnectProtocolSelection? selection = LanConnectSessionProtocolState.Shared.Current.Selection;
            LanConnectPreparedTailMessage runtimePrepared =
                PrepareOutgoing(null!, messageKind, 0, message, selection!);
            prepared = new LanConnectAndroidPreparedMessage(null!, writer, messageKind, 0, selection!, runtimePrepared);
            return true;
        }

        public void CompleteConcreteOutgoing(LanConnectAndroidPreparedMessage prepared)
        {
            if (prepared.Selection.Carrier == LanConnectProtocolCarrier.StandaloneTailV1)
            {
                _ = LanConnectStandaloneTailCarrier.Write(
                    prepared.Writer,
                    prepared.Prepared.Container,
                    prepared.Selection);
            }
        }

        public void ClearPendingOutgoing(PacketWriter writer)
        {
        }

        public LanConnectAndroidTransportState? SubmitPendingSidecarBeforeVanilla(
            object transport,
            bool isHostTransport,
            ulong recipientPeerId,
            byte[] buffer,
            int length) =>
            null;

        public void HandleVanillaTransportFailure(LanConnectAndroidTransportState state, Exception exception)
        {
        }

        public void SubmitSidecarBeforeVanilla(
            NetMessageBus messageBus,
            LanConnectSidecarMessageKind messageKind,
            ulong senderPeerId,
            object message,
            byte[] container,
            LanConnectProtocolSelection selection) =>
            throw new InvalidOperationException();

        public void ValidateStandaloneIncoming(
            NetMessageBus messageBus,
            LanConnectSidecarMessageKind messageKind,
            ulong transportSenderPeerId,
            INetMessage message,
            byte[] container,
            LanConnectProtocolSelection selection)
        {
            _ = LanConnectTailCodec.Decode(container);
            ValidatedSenders.Add(transportSenderPeerId);
        }

        public void HandleIncomingFailure(
            NetMessageBus messageBus,
            ulong transportSenderPeerId,
            Exception exception,
            LanConnectProtocolSelection selection)
        {
            RejectedSenders.Add(transportSenderPeerId);
        }

        public bool TryPairSidecarIncoming(
            NetMessageBus messageBus,
            LanConnectSidecarMessageKind messageKind,
            ulong senderPeerId,
            INetMessage message,
            LanConnectProtocolSelection selection) => false;
    }
}
