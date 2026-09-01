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
        MethodInfo result = ResolveClosedBusSerializerForTesting(
            typeof(CompatibleMessageBus),
            typeof(PlayerListMessage<StartRunLobbyPlayer>));

        Assert.True(result.IsGenericMethod);
        Assert.False(result.ContainsGenericParameters);
        Assert.Equal(typeof(PlayerListMessage<StartRunLobbyPlayer>), result.GetGenericArguments()[0]);
    }

    [Fact]
    public void Rejects_message_bus_without_expected_generic_serializer_shape()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ResolveClosedBusSerializerForTesting(
                typeof(IncompatibleMessageBus),
                typeof(PlayerListMessage<StartRunLobbyPlayer>)));
    }

    // 测试专用：解析闭环泛型总线 serializer（生产端的泛型解析器已随桌面泛型计划删除）。
    private static MethodInfo ResolveClosedBusSerializerForTesting(Type busType, Type messageType) =>
        busType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "SerializeMessage"
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 3)
            .MakeGenericMethod(messageType);

    // begin-run 边界 prefix 随桌面泛型计划一并删除：native_bus_v1 下恒不注册该目标。
    [Fact]
    public void Begin_run_message_bus_boundary_is_never_patched_under_native_bus()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "LanConnectSerializationPatches.cs"));
        Assert.DoesNotContain("beginRunMessageBusSerialize = Resolve", source, StringComparison.Ordinal);
        Assert.Contains("native_bus_v1 恒为 null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_serialization_source_contains_no_private_Ritsu_composition_bridge()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "LanConnectSerializationPatches.cs"));
        string[] forbidden =
        {
            "DetachedBeginRunPostfix",
            "DetachRitsuBeginRunPostfix",
            "CreateBeginRunPostfixInvoker",
            "ritsuTailBridge",
            "RestorePostfixAfterCompatibleCompositionRollback",
            "Harmony.GetPatchInfo"
        };
        foreach (string marker in forbidden)
        {
            Assert.DoesNotContain(marker, source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
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
