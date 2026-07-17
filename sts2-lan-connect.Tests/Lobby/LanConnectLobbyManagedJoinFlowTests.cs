using System.Reflection;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyManagedJoinFlowTests
{
    [Fact]
    public void Accepts_identical_game_versions()
    {
        Assert.Null(LanConnectLobbyManagedJoinFlow.GetGameVersionMismatchMessage("v0.109.0", "v0.109.0"));
    }

    [Theory]
    [InlineData("v0.109.0", "0.109.0")]
    [InlineData("V0.109.0", "v0.109.0")]
    public void Accepts_equivalent_game_versions_with_optional_v_prefix(string host, string local)
    {
        Assert.Null(LanConnectLobbyManagedJoinFlow.GetGameVersionMismatchMessage(host, local));
    }

    [Fact]
    public void Rejects_different_game_versions_with_actionable_message()
    {
        string message = Assert.IsType<string>(
            LanConnectLobbyManagedJoinFlow.GetGameVersionMismatchMessage("v0.108.0", "v0.109.0"));

        Assert.Contains("游戏版本不匹配", message, StringComparison.Ordinal);
        Assert.Contains("房主版本：v0.108.0", message, StringComparison.Ordinal);
        Assert.Contains("当前版本：v0.109.0", message, StringComparison.Ordinal);
        Assert.Contains("完全相同的游戏版本", message, StringComparison.Ordinal);
    }

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
