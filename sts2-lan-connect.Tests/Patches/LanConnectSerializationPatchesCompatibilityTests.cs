using System.Reflection;
using HarmonyLib;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Patches;

public sealed class LanConnectSerializationPatchesCompatibilityTests
{
    [Fact]
    public void Resolves_slot_carrier_from_legacy_player_list_shape()
    {
        Type result = LanConnectSerializationPatches.ResolveSlotIdCarrierType(
            typeof(PlayerListMessage<LegacyLobbyPlayer>),
            typeof(PlayerListMessage<LegacyLobbyPlayer>));

        Assert.Equal(typeof(LegacyLobbyPlayer), result);
    }

    [Fact]
    public void Resolves_slot_carrier_from_split_start_run_player_list_shape()
    {
        Type result = LanConnectSerializationPatches.ResolveSlotIdCarrierType(
            typeof(PlayerListMessage<StartRunLobbyPlayer>),
            typeof(PlayerListMessage<StartRunLobbyPlayer>));

        Assert.Equal(typeof(StartRunLobbyPlayer), result);
    }

    [Fact]
    public void Rejects_disagreeing_join_and_begin_run_player_types()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LanConnectSerializationPatches.ResolveSlotIdCarrierType(
                typeof(PlayerListMessage<LegacyLobbyPlayer>),
                typeof(PlayerListMessage<StartRunLobbyPlayer>)));

        Assert.Contains("wire types disagree", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_player_wire_type_without_integer_slot_id()
    {
        Assert.Throws<MissingFieldException>(() =>
            LanConnectSerializationPatches.ResolveSlotIdCarrierType(
                typeof(PlayerListMessage<PlayerWithoutSlotId>),
                typeof(PlayerListMessage<PlayerWithoutSlotId>)));
    }

    [Fact]
    public void Resolves_closed_generic_message_bus_serializer()
    {
        MethodInfo result = LanConnectSerializationPatches.ResolveGenericSerializeMessageMethod(
            typeof(CompatibleMessageBus),
            typeof(PlayerListMessage<StartRunLobbyPlayer>));

        Assert.True(result.IsGenericMethod);
        Assert.False(result.ContainsGenericParameters);
        Assert.Equal(typeof(PlayerListMessage<StartRunLobbyPlayer>), result.GetGenericArguments()[0]);
    }

    [Fact]
    public void Rejects_message_bus_without_expected_generic_serializer_shape()
    {
        Assert.Throws<MissingMethodException>(() =>
            LanConnectSerializationPatches.ResolveGenericSerializeMessageMethod(
                typeof(IncompatibleMessageBus),
                typeof(PlayerListMessage<StartRunLobbyPlayer>)));
    }

    [Fact]
    public void Detaches_existing_begin_run_postfix_before_installing_message_bus_prefix()
    {
        string ritsuHarmonyId = $"sts2_lan_connect.tests.ritsu.{Guid.NewGuid():N}";
        string lanHarmonyId = $"sts2_lan_connect.tests.lan.{Guid.NewGuid():N}";
        Harmony ritsuHarmony = new(ritsuHarmonyId);
        Harmony lanHarmony = new(lanHarmonyId);
        MethodInfo target = LanConnectSerializationPatches.ResolveGenericSerializeMessageMethod(
            typeof(CompatibleMessageBus),
            typeof(PlayerListMessage<StartRunLobbyPlayer>));
        MethodInfo postfix = typeof(LanConnectSerializationPatchesCompatibilityTests).GetMethod(
            nameof(FakeRitsuBeginRunPostfix),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo prefix = typeof(LanConnectSerializationPatchesCompatibilityTests).GetMethod(
            nameof(FakeLanBeginRunPrefix),
            BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            ritsuHarmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Assert.Contains(Harmony.GetPatchInfo(target)!.Postfixes,
                patch => patch.owner == ritsuHarmonyId && patch.PatchMethod == postfix);

            LanConnectSerializationPatches.DetachPostfixForCompatibleComposition(
                lanHarmony,
                target,
                postfix);
            Assert.DoesNotContain(Harmony.GetPatchInfo(target)!.Postfixes,
                patch => patch.owner == ritsuHarmonyId && patch.PatchMethod == postfix);

            lanHarmony.Patch(target, prefix: new HarmonyMethod(prefix));
            Assert.Contains(Harmony.GetPatchInfo(target)!.Prefixes,
                patch => patch.owner == lanHarmonyId && patch.PatchMethod == prefix);

            lanHarmony.UnpatchAll(lanHarmonyId);
            LanConnectSerializationPatches.RestorePostfixAfterCompatibleCompositionRollback(
                ritsuHarmony,
                target,
                postfix,
                Priority.Last,
                [],
                [],
                false);
            Patch restored = Assert.Single(Harmony.GetPatchInfo(target)!.Postfixes,
                patch => patch.owner == ritsuHarmonyId && patch.PatchMethod == postfix);
            Assert.Equal(Priority.Last, restored.priority);
        }
        finally
        {
            lanHarmony.UnpatchAll(lanHarmonyId);
            ritsuHarmony.UnpatchAll(ritsuHarmonyId);
        }
    }

    [Fact]
    public void Detaching_selected_postfix_keeps_unrelated_postfix_installed()
    {
        string ritsuHarmonyId = $"sts2_lan_connect.tests.ritsu.selected.{Guid.NewGuid():N}";
        string unrelatedHarmonyId = $"sts2_lan_connect.tests.unrelated.{Guid.NewGuid():N}";
        Harmony ritsuHarmony = new(ritsuHarmonyId);
        Harmony unrelatedHarmony = new(unrelatedHarmonyId);
        Harmony lanHarmony = new($"sts2_lan_connect.tests.lan.selected.{Guid.NewGuid():N}");
        MethodInfo target = LanConnectSerializationPatches.ResolveGenericSerializeMessageMethod(
            typeof(CompatibleMessageBus),
            typeof(PlayerListMessage<StartRunLobbyPlayer>));
        MethodInfo postfix = typeof(LanConnectSerializationPatchesCompatibilityTests).GetMethod(
            nameof(FakeRitsuBeginRunPostfix),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo unrelatedPostfix = typeof(LanConnectSerializationPatchesCompatibilityTests).GetMethod(
            nameof(FakeUnrelatedBeginRunPostfix),
            BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            ritsuHarmony.Patch(target, postfix: new HarmonyMethod(postfix));
            unrelatedHarmony.Patch(target, postfix: new HarmonyMethod(unrelatedPostfix));

            LanConnectSerializationPatches.DetachPostfixForCompatibleComposition(
                lanHarmony,
                target,
                postfix);

            Assert.DoesNotContain(Harmony.GetPatchInfo(target)!.Postfixes,
                patch => patch.owner == ritsuHarmonyId);
            Assert.Contains(Harmony.GetPatchInfo(target)!.Postfixes,
                patch => patch.owner == unrelatedHarmonyId && patch.PatchMethod == unrelatedPostfix);
        }
        finally
        {
            lanHarmony.UnpatchAll(lanHarmony.Id);
            unrelatedHarmony.UnpatchAll(unrelatedHarmonyId);
            ritsuHarmony.UnpatchAll(ritsuHarmonyId);
        }
    }

    private static void FakeRitsuBeginRunPostfix(
        ref int length,
        ref byte[] __result)
    {
    }

    private static void FakeUnrelatedBeginRunPostfix(ref int length, ref byte[] __result)
    {
    }

    private static bool FakeLanBeginRunPrefix(ref int length, ref byte[] __result)
    {
        length = 0;
        __result = [];
        return false;
    }

    private struct PlayerListMessage<TPlayer>
    {
#pragma warning disable CS0649
        public List<TPlayer>? playersInLobby;
#pragma warning restore CS0649
    }

    private struct LegacyLobbyPlayer
    {
#pragma warning disable CS0649
        public int slotId;
#pragma warning restore CS0649
    }

    private struct StartRunLobbyPlayer
    {
#pragma warning disable CS0649
        public int slotId;
#pragma warning restore CS0649
    }

    private struct PlayerWithoutSlotId
    {
    }

    private sealed class IncompatibleMessageBus
    {
        public byte[] SerializeMessage(ulong senderId, object message, out int length)
        {
            length = 0;
            return [];
        }
    }

    private sealed class CompatibleMessageBus
    {
        public byte[] SerializeMessage<T>(ulong senderId, T message, out int length)
        {
            length = 0;
            return [];
        }
    }
}
