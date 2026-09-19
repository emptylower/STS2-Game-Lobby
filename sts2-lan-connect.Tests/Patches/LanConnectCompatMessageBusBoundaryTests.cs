using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Patches;

// compat_4_5_v1 桌面房主回归（0.6.1 起）：tail 计划把 SerializeMessage<T> 闭合实例化替换为
// 全优化 DynamicMethod 后，RyuJIT 会按原始 IL 内联 LobbyBeginRunMessage / JoinResponse 的
// Serialize，绕过 compat 位宽 transpiler。这里钉住消息总线边界强制路径的判定面：
// 仅 compat profile 强制；tail_v1 / 无租约一律走原路径；桌面包含边界目标而 Android 不包含。
public sealed class LanConnectCompatMessageBusBoundaryTests
{
    [Fact]
    public void Compat_profile_forces_the_message_bus_boundary()
    {
        LanConnectProtocolSelection selection = LanConnectProtocolSelection.CreateLocalCompat(
            8,
            "0.110.1",
            wireCacheSignature: null);
        using LanConnectSessionProtocolLease lease =
            LanConnectSessionProtocolState.Shared.FreezeHost(selection, "boundary-compat");

        Assert.True(LanConnectCompatWirePatches.ShouldForceCompatWireAtMessageBusBoundary());
    }

    [Fact]
    public void Tail_profile_walks_the_original_serialization_path()
    {
        LanConnectProtocolSelection selection = TailSelection();
        using LanConnectSessionProtocolLease lease =
            LanConnectSessionProtocolState.Shared.FreezeHost(selection, "boundary-tail");

        Assert.False(LanConnectCompatWirePatches.ShouldForceCompatWireAtMessageBusBoundary());
    }

    [Fact]
    public void Missing_profile_walks_the_original_serialization_path()
    {
        Assert.False(LanConnectCompatWirePatches.ShouldForceCompatWireAtMessageBusBoundary());
    }

    [Fact]
    public void Message_bus_boundary_targets_cover_desktop_but_not_android()
    {
        Assert.True(LanConnectSerializationPatches.ShouldPatchCompatMessageBusBoundary(isAndroid: false));
        Assert.False(LanConnectSerializationPatches.ShouldPatchCompatMessageBusBoundary(isAndroid: true));
    }

    [Fact]
    public void Both_boundary_prefixes_gate_on_the_compat_profile_before_touching_the_writer()
    {
        string source = ReadProductionSource();
        Assert.Equal(
            2,
            CountOccurrences(source, "if (!LanConnectCompatWirePatches.ShouldForceCompatWireAtMessageBusBoundary())"));
        Assert.Contains("nameof(SerializeJoinResponseAtMessageBusPrefix)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_prefixes_stay_below_the_tail_seam_prefix_priority()
    {
        // tail seam prefix 挂 Priority.First + 100：compat 边界 prefix 必须在其后运行，
        // 才能与 tail 的 __state 投影共存（compat 下 tail __state 恒为 null）。
        string source = ReadProductionSource();
        Assert.Equal(
            2,
            CountOccurrences(source, "[HarmonyPriority(Priority.First)]"));
    }

    private static string ReadProductionSource() => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "sts2-lan-connect",
        "Scripts",
        "LanConnectSerializationPatches.cs"));

    private static int CountOccurrences(string source, string marker)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }

    private static LanConnectProtocolSelection TailSelection()
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            LanConnectConstants.TailLanProtocolVersion,
            LanConnectProtocolCarrier.NativeBusV1,
            "0.6.3-alpha.1",
            8,
            "0.110.1",
            null,
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
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
}
