using System.Reflection;
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
