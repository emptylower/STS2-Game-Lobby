using System.Reflection;
using System.Runtime.CompilerServices;
using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectSaveBindingMutationBoundaryTests
{
    [Fact]
    public void Safe_load_does_not_persist_a_host_channel()
    {
        MethodInfo safeLoad = typeof(LanConnectMultiplayerSaveCompatibility).GetMethod(
            nameof(LanConnectMultiplayerSaveCompatibility.StartLoadedRunAsLanHostAsync),
            BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo persist = typeof(LanConnectMultiplayerSaveRoomBinding).GetMethod(
            nameof(LanConnectMultiplayerSaveRoomBinding.PersistHostBinding),
            BindingFlags.Public | BindingFlags.Static)!;

        Assert.False(ContainsMetadataToken(safeLoad, persist.MetadataToken));
    }

    [Fact]
    public void Repair_does_not_remove_the_saved_room_binding()
    {
        MethodInfo repair = typeof(LanConnectMultiplayerSaveRepair).GetMethod(
            "RepairCurrentProfile",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo remove = typeof(LanConnectConfig).GetMethod(
            nameof(LanConnectConfig.RemoveSaveRoomBinding),
            BindingFlags.Public | BindingFlags.Static)!;

        Assert.False(ContainsMetadataToken(repair, remove.MetadataToken));
    }

    [Fact]
    public void Preserved_lobby_binding_still_publishes_after_repair()
    {
        LanConnectSavedRoomBinding existing = new()
        {
            SchemaVersion = LanConnectSavedRoomBinding.CurrentSchemaVersion,
            SaveKey = "save-1",
            RoomName = "续局房间",
            HostChannel = LanConnectHostChannels.Lobby
        };

        LanConnectSavedRoomBinding preserved = LanConnectConfig.CloneBindingForPersistence(existing);

        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Publish,
            LanConnectContinueRunPublishDecision.Decide(preserved.HostChannel, preserved.SchemaVersion));
    }

    [Fact]
    public void Hosted_restart_persists_binding_before_returning_to_main_menu()
    {
        MethodInfo restart = typeof(LanConnectLobbyRuntime).GetMethod(
            "StartHostedRunRestartAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Type stateMachine = restart.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        MethodInfo moveNext = stateMachine.GetMethod(
            nameof(IAsyncStateMachine.MoveNext),
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!;
        MethodInfo persist = typeof(LanConnectMultiplayerSaveRoomBinding).GetMethod(
            nameof(LanConnectMultiplayerSaveRoomBinding.PersistHostBinding),
            BindingFlags.Public | BindingFlags.Static)!;

        Assert.True(ContainsMetadataToken(moveNext, persist.MetadataToken));
    }

    private static bool ContainsMetadataToken(MethodInfo method, int metadataToken)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        return il.AsSpan().IndexOf(BitConverter.GetBytes(metadataToken)) >= 0;
    }
}
