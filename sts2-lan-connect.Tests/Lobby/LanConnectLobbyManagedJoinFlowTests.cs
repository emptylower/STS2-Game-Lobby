using System.Reflection;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyManagedJoinFlowTests
{
    [Fact]
    public void Resolves_legacy_connect_signature_with_concrete_service()
    {
        MethodInfo method = LanConnectLobbyManagedJoinFlow.ResolveCompatibleConnectMethod(
            typeof(ILegacyInitializer),
            typeof(TestNetService));

        Assert.Equal(typeof(TestNetService), method.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void Resolves_current_connect_signature_with_service_interface()
    {
        MethodInfo method = LanConnectLobbyManagedJoinFlow.ResolveCompatibleConnectMethod(
            typeof(ICurrentInitializer),
            typeof(TestNetService));

        Assert.Equal(typeof(ITestNetService), method.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void Rejects_connect_signature_that_cannot_accept_service()
    {
        Assert.Throws<MissingMethodException>(() =>
            LanConnectLobbyManagedJoinFlow.ResolveCompatibleConnectMethod(
                typeof(IIncompatibleInitializer),
                typeof(TestNetService)));
    }

    private interface ITestNetService;

    private sealed class TestNetService : ITestNetService;

    private sealed class OtherNetService;

    private interface ILegacyInitializer
    {
        Task<int> Connect(TestNetService service, CancellationToken cancellationToken);
    }

    private interface ICurrentInitializer
    {
        Task<int> Connect(ITestNetService service, CancellationToken cancellationToken);
    }

    private interface IIncompatibleInitializer
    {
        Task<int> Connect(OtherNetService service, CancellationToken cancellationToken);
    }
}
