using System.Reflection;
using HarmonyLib;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectAtomicPatchDispatcherTests
{
    [Fact]
    public void Failed_plan_rolls_back_only_the_dispatcher_owner()
    {
        string dispatcherId = $"{LanConnectProtocolPatchDispatcher.HarmonyId}.test.{Guid.NewGuid():N}";
        string externalId = $"external.test.{Guid.NewGuid():N}";
        Harmony dispatcher = new(dispatcherId);
        Harmony external = new(externalId);
        MethodInfo target = typeof(LanConnectAtomicPatchDispatcherTests).GetMethod(
            nameof(Target), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo prefix = typeof(LanConnectAtomicPatchDispatcherTests).GetMethod(
            nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo postfix = typeof(LanConnectAtomicPatchDispatcherTests).GetMethod(
            nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            external.Patch(target, postfix: new HarmonyMethod(postfix));
            Assert.Throws<InvalidOperationException>(() =>
                LanConnectProtocolPatchDispatcher.ApplyAtomicForTesting(
                    dispatcher,
                    [
                        harmony => harmony.Patch(target, prefix: new HarmonyMethod(prefix)),
                        _ => throw new InvalidOperationException("required target missing")
                    ]));

            HarmonyLib.Patches patches = Harmony.GetPatchInfo(target)!;
            Assert.DoesNotContain(patches.Prefixes, patch => patch.owner == dispatcherId);
            Assert.Contains(patches.Postfixes, patch => patch.owner == externalId);
        }
        finally
        {
            dispatcher.UnpatchAll(dispatcherId);
            external.UnpatchAll(externalId);
        }
    }

    [Fact]
    public void Compat_and_tail_profiles_choose_fixed_4_5_and_vanilla_2_3_widths()
    {
        Assert.Equal(LanConnectConstants.ExtendedSlotIdBits, 4);
        Assert.Equal(LanConnectConstants.ExtendedLobbyListBits, 5);
        Assert.Equal(LanConnectConstants.VanillaSlotIdBits, 2);
        Assert.Equal(LanConnectConstants.VanillaLobbyListBits, 3);
    }

    [Fact]
    public void Dispatcher_preserves_the_existing_RMP_full_patch_skip_guard()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "Protocol",
            "Patches",
            "LanConnectProtocolPatchDispatcher.cs"));

        Assert.Contains("LanConnectExternalModDetection.IsRmpModLoaded", source, StringComparison.Ordinal);
        Assert.Contains("skipping all LAN protocol patches", source, StringComparison.Ordinal);
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

    private static int Target(int value) => value;
    private static void Prefix() { }
    private static void Postfix() { }
}
