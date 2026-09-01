using GdUnit4;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol.NativeBus;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectNativeBusStartupCheckTests
{
    [TestCase]
    public void Table_validation_rejects_oversize_and_byte_aliasing()
    {
        AssertThat(LanConnectNativeBusStartupCheck.ValidateTable(257, [])).IsNotNull();
        int[] aliasing = new int[512];
        for (int index = 0; index < aliasing.Length; index++)
        {
            aliasing[index] = index;
        }
        AssertThat(LanConnectNativeBusStartupCheck.ValidateTable(512, aliasing)).IsNotNull();

        int[] unique = new int[256];
        for (int index = 0; index < unique.Length; index++)
        {
            unique[index] = index;
        }
        AssertThat(LanConnectNativeBusStartupCheck.ValidateTable(256, unique)).IsNull();
    }

    [TestCase]
    public void Run_against_the_vanilla_registry_reports_ready_with_a_fingerprint()
    {
        AssemblyInfo.Init();
        typeof(MessageTypes).GetField("_cache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, new NetTypeCache<INetMessage>(INetMessageSubtypes.All.ToList()));
        LanConnectNativeBusSender.TypeIdResolverForTesting = () => 200;

        try
        {
            LanConnectNativeBusStartupCheck.Result result = LanConnectNativeBusStartupCheck.Run();
            AssertThat(result.Ok).IsTrue();
            AssertThat(result.LocalTypeId!.Value).IsEqual(200);
            AssertBool(result.RegistryFingerprint!.StartsWith("sha256:v1:")).IsTrue();
        }
        finally
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
        }
    }

    [TestCase]
    public void Run_reports_a_baselib_collision_as_disable_reason()
    {
        AssemblyInfo.Init();
        typeof(MessageTypes).GetField("_cache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, new NetTypeCache<INetMessage>(INetMessageSubtypes.All.ToList()));
        LanConnectNativeBusSender.TypeIdResolverForTesting = () => 128;

        try
        {
            LanConnectNativeBusStartupCheck.Result result = LanConnectNativeBusStartupCheck.Run();
            AssertThat(result.Ok).IsFalse();
            AssertBool(result.Reason!.Contains("BaseLib")).IsTrue();
        }
        finally
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
        }
    }
}
