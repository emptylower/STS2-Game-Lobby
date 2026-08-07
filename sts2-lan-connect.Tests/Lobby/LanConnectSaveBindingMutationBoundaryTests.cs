using System.Reflection;
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

    private static bool ContainsMetadataToken(MethodInfo method, int metadataToken)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        return il.AsSpan().IndexOf(BitConverter.GetBytes(metadataToken)) >= 0;
    }
}
