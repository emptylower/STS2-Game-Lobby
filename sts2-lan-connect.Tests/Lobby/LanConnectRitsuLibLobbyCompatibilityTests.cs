using System.Reflection;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectRitsuLibLobbyCompatibilityTests
{
    [Fact]
    public void Finds_every_run_manager_net_service_read()
    {
        MethodInfo unrelated = typeof(ProbeRunManager).GetProperty(nameof(ProbeRunManager.Other))!.GetMethod!;
        MethodInfo getter = typeof(ProbeRunManager).GetProperty(nameof(ProbeRunManager.NetService))!.GetMethod!;

        int[] indices = LanConnectRitsuLibLobbyCompatibility.FindGetterReadIndices(
            [unrelated, getter, null, getter],
            getter);

        Assert.Equal([1, 3], indices);
    }

    [Fact]
    public void Reports_no_match_when_run_manager_send_shape_changes()
    {
        MethodInfo unrelated = typeof(ProbeRunManager).GetProperty(nameof(ProbeRunManager.Other))!.GetMethod!;
        MethodInfo getter = typeof(ProbeRunManager).GetProperty(nameof(ProbeRunManager.NetService))!.GetMethod!;

        int[] indices = LanConnectRitsuLibLobbyCompatibility.FindGetterReadIndices(
            [unrelated, null],
            getter);

        Assert.Empty(indices);
    }

    private sealed class ProbeRunManager
    {
        public object? NetService { get; init; }

        public object? Other { get; init; }
    }
}
