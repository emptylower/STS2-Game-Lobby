using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.ProtocolPlanTests;

public sealed class LanConnectCapacityConstructorPatchTests
{
    [Fact]
    public void Current_game_constructor_accepts_the_production_capacity_postfix()
    {
        ConstructorInfo? target = LanConnectStartRunLobbyConstructorCompatibility.Resolve(
            typeof(StartRunLobby), typeof(GameMode), typeof(INetGameService), typeof(IStartRunLobbyListener));
        Assert.NotNull(target);
        MethodInfo? postfix = typeof(LanConnectLobbyCapacityPatches).GetMethod(
            "StartRunLobbyCtorPostfix", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(postfix);
        Harmony harmony = new("sts2_lan_connect.tests.capacity-constructor." + Guid.NewGuid().ToString("N"));

        try
        {
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));

            Assert.Contains(Harmony.GetPatchInfo(target)!.Postfixes, patch =>
                patch.owner == harmony.Id && patch.PatchMethod == postfix);
        }
        finally
        {
            harmony.Unpatch(target, HarmonyPatchType.Postfix, harmony.Id);
        }
    }
}
