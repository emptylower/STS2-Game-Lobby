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

    private static void InitializeRegistry()
    {
        AssemblyInfo.Init();
        typeof(MessageTypes).GetField("_cache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, new NetTypeCache<INetMessage>(INetMessageSubtypes.All.ToList()));
    }

    private static void ClearRegistry()
    {
        typeof(MessageTypes).GetField("_cache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, null);
    }

    [TestCase]
    public void Run_without_the_registry_returns_pending_and_never_disables()
    {
        ClearRegistry();
        LanConnectNativeBusStartupCheck.ResetForTesting();

        LanConnectNativeBusStartupCheck.Result result = LanConnectNativeBusStartupCheck.Run();
        AssertThat(result.Pending).IsTrue();
        AssertThat(result.Ok).IsFalse();
    }

    [TestCase]
    public void Run_with_the_registry_but_without_AssemblyInfo_returns_pending_and_never_disables()
    {
        // 复现 2026-09-05 Windows 0.111.0 反馈：第三方 mod 在 mod 初始化阶段就把 MessageTypes 建好了，
        // 但 AssemblyInfo.Init() 要到 ExecuteEssential 才跑；此时 ModForType 抛无消息的
        // InvalidOperationException，自检必须挂起（延后到首次 tail 会话），不能降级。
        InitializeRegistry();
        AssemblyInfo.ClearForTests();
        LanConnectNativeBusSender.TypeIdResolverForTesting = () => 200;
        LanConnectNativeBusStartupCheck.ResetForTesting();

        try
        {
            LanConnectNativeBusStartupCheck.Result result = LanConnectNativeBusStartupCheck.Run();
            AssertThat(result.Pending).IsTrue();
            AssertThat(result.Ok).IsFalse();
        }
        finally
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
            AssemblyInfo.Init();
            LanConnectNativeBusStartupCheck.ResetForTesting();
        }
    }

    [TestCase]
    public void Run_against_the_vanilla_registry_reports_ready_with_a_fingerprint()
    {
        InitializeRegistry();
        LanConnectNativeBusSender.TypeIdResolverForTesting = () => 200;
        LanConnectNativeBusStartupCheck.ResetForTesting();

        try
        {
            LanConnectNativeBusStartupCheck.Result result = LanConnectNativeBusStartupCheck.Run();
            AssertThat(result.Ok).IsTrue();
            AssertThat(result.Pending).IsFalse();
            AssertThat(result.LocalTypeId!.Value).IsEqual(200);
            AssertBool(result.RegistryFingerprint!.StartsWith("sha256:v1:")).IsTrue();
        }
        finally
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
            LanConnectNativeBusStartupCheck.ResetForTesting();
        }
    }

    [TestCase]
    public void Ensure_ready_passes_when_ready_and_disables_on_a_definitive_failure()
    {
        InitializeRegistry();
        LanConnectNativeBusSender.TypeIdResolverForTesting = () => 200;
        LanConnectNativeBusStartupCheck.ResetForTesting();
        LanConnectDegradedMode.ResetForTesting();

        try
        {
            // 就绪：门禁直通并缓存裁决。
            LanConnectNativeBusStartupCheck.EnsureReadyOrThrow();
            AssertThat(LanConnectNativeBusStartupCheck.CachedVerdict.Ok).IsTrue();
        }
        finally
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
            LanConnectDegradedMode.ResetForTesting();
        }

        // 终局失败：抛结构化异常并进入降级模式。
        LanConnectNativeBusSender.TypeIdResolverForTesting = () => 128;
        LanConnectNativeBusStartupCheck.ResetForTesting();
        LanConnectDegradedMode.ResetForTesting();
        try
        {
            AssertThrown(() => LanConnectNativeBusStartupCheck.EnsureReadyOrThrow());
            AssertBool(LanConnectDegradedMode.IsActive).IsTrue();
        }
        finally
        {
            LanConnectNativeBusSender.TypeIdResolverForTesting = null;
            LanConnectDegradedMode.ResetForTesting();
            LanConnectNativeBusStartupCheck.ResetForTesting();
        }
    }
}
