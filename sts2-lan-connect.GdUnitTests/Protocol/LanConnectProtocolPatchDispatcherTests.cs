using GdUnit4;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectProtocolPatchDispatcherTests
{
    [TestCase]
    public void Atomic_failure_unpatches_only_dispatcher_owner_and_preserves_external_patch()
    {
        string dispatcherId = $"sts2_lan_connect.tests.dispatcher.owner.{Guid.NewGuid():N}";
        string externalId = $"sts2_lan_connect.tests.external.owner.{Guid.NewGuid():N}";
        Harmony dispatcher = new(dispatcherId);
        Harmony external = new(externalId);
        System.Reflection.MethodInfo target = AccessTools.Method(typeof(PacketWriter), nameof(PacketWriter.Reset))!;
        System.Reflection.MethodInfo dispatcherPrefix = AccessTools.Method(
            typeof(LanConnectProtocolPatchDispatcherTests),
            nameof(DispatcherResetPrefix))!;
        System.Reflection.MethodInfo externalPrefix = AccessTools.Method(
            typeof(LanConnectProtocolPatchDispatcherTests),
            nameof(ExternalResetPrefix))!;

        try
        {
            external.Patch(target, prefix: new HarmonyMethod(externalPrefix));
            InvalidOperationException? exception = null;
            try
            {
                LanConnectProtocolPatchDispatcher.ApplyAtomicForTesting(
                    dispatcher,
                    [
                        harmony => harmony.Patch(target, prefix: new HarmonyMethod(dispatcherPrefix)),
                        _ => throw new InvalidOperationException("forced patch failure")
                    ]);
            }
            catch (InvalidOperationException ex)
            {
                exception = ex;
            }

            AssertObject(exception).IsNotNull();
            Patches patches = Harmony.GetPatchInfo(target)!;
            AssertBool(patches.Prefixes.Any(patch => patch.owner == dispatcherId)).IsFalse();
            AssertBool(patches.Prefixes.Any(patch => patch.owner == externalId)).IsTrue();
        }
        finally
        {
            dispatcher.UnpatchAll(dispatcherId);
            external.UnpatchAll(externalId);
        }
    }

    [TestCase]
    public void Atomic_apply_failure_resets_serialization_state_so_retry_reapplies_it()
    {
        Harmony harmony = new($"sts2_lan_connect.tests.dispatcher.{Guid.NewGuid():N}");
        LanConnectProtocolPatchDispatcher.SetAppliedForTesting(false);
        LanConnectSerializationPatches.SetAppliedForTesting(false);
        int serializationApplyCount = 0;

        try
        {
            InvalidOperationException? exception = null;
            try
            {
                LanConnectProtocolPatchDispatcher.ApplyAtomicForTesting(
                    harmony,
                    [
                        _ =>
                        {
                            serializationApplyCount++;
                            LanConnectSerializationPatches.SetAppliedForTesting(true);
                            LanConnectProtocolPatchDispatcher.SetAppliedForTesting(true);
                        },
                        _ => throw new InvalidOperationException("forced tail patch failure")
                    ]);
            }
            catch (InvalidOperationException ex)
            {
                exception = ex;
            }

            AssertObject(exception).IsNotNull();
            AssertThat(exception!.Message).IsEqual("forced tail patch failure");
            AssertBool(LanConnectProtocolPatchDispatcher.IsAppliedForTesting).IsFalse();
            AssertBool(LanConnectSerializationPatches.IsAppliedForTesting).IsFalse();

            LanConnectProtocolPatchDispatcher.ApplyAtomicForTesting(
                harmony,
                [
                    _ =>
                    {
                        AssertBool(LanConnectSerializationPatches.IsAppliedForTesting).IsFalse();
                        serializationApplyCount++;
                        LanConnectSerializationPatches.SetAppliedForTesting(true);
                    },
                    _ => LanConnectProtocolPatchDispatcher.SetAppliedForTesting(true)
                ]);

            AssertInt(serializationApplyCount).IsEqual(2);
            AssertBool(LanConnectSerializationPatches.IsAppliedForTesting).IsTrue();
            AssertBool(LanConnectProtocolPatchDispatcher.IsAppliedForTesting).IsTrue();
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            LanConnectProtocolPatchDispatcher.SetAppliedForTesting(false);
            LanConnectSerializationPatches.SetAppliedForTesting(false);
        }
    }

    private static void DispatcherResetPrefix() { }

    private static void ExternalResetPrefix() { }
}
