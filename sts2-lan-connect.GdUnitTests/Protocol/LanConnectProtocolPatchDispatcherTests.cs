using GdUnit4;
using HarmonyLib;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectProtocolPatchDispatcherTests
{
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
}
