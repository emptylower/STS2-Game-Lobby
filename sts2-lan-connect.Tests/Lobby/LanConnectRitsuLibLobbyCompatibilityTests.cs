using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectRitsuLibLobbyCompatibilityTests
{
    [Fact]
    public void Compatibility_entrypoint_uses_only_the_public_sidecar_carrier()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "Lobby",
            "LanConnectRitsuLibLobbyCompatibility.cs"));

        Assert.Contains("public typed sidecar carrier enabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled in v0.6 alpha", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveLobbyNetService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TranspileRunManagerSend", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InjectResolverAfterGetter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RunManager send fallback", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Ritsu_client_session_binding_waits_for_ENet_to_assign_the_real_net_id()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "Lobby",
            "LanConnectLobbyJoinFlow.cs"));

        int prepareIndex = source.IndexOf("beforeConnect(netService);", StringComparison.Ordinal);
        int connectIndex = source.IndexOf(
            "await client.ConnectToHost(netId, ip, port, cancelToken)",
            StringComparison.Ordinal);
        int activateIndex = source.IndexOf("afterConnect(netService);", StringComparison.Ordinal);

        Assert.True(prepareIndex >= 0, "Ritsu sidecar flow must be prepared before ENet connects.");
        Assert.True(connectIndex > prepareIndex, "ENet must connect after sidecar flow preparation.");
        Assert.True(
            activateIndex > connectIndex,
            "RitsuLib must first observe the client service after ENet assigns its real net id.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ritsu_host_session_activates_only_after_control_binding_and_peer_connection(
        bool controlBindingArrivesFirst)
    {
        const ulong peerNetId = 6321410324222093731UL;
        LanConnectHostSidecarActivationGate gate = new();

        bool firstObservation = controlBindingArrivesFirst
            ? gate.ObserveControlBinding(peerNetId)
            : gate.ObservePeerConnected(peerNetId);
        bool secondObservation = controlBindingArrivesFirst
            ? gate.ObservePeerConnected(peerNetId)
            : gate.ObserveControlBinding(peerNetId);

        Assert.False(firstObservation);
        Assert.True(secondObservation);
    }

    [Fact]
    public void Ritsu_host_session_disconnect_clears_the_prepared_flow()
    {
        const ulong peerNetId = 6321410324222093731UL;
        LanConnectHostSidecarActivationGate gate = new();
        Assert.False(gate.ObserveControlBinding(peerNetId));
        Assert.True(gate.ObservePeerConnected(peerNetId));

        gate.ObservePeerDisconnected(peerNetId);

        Assert.False(gate.ObservePeerConnected(peerNetId));
        Assert.True(gate.ObserveControlBinding(peerNetId));
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
