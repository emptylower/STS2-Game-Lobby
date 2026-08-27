using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.LanHost;

public sealed class LanConnectNetGameServiceFactoryTests : IDisposable
{
    // 工厂按“参数类型简单名 == PeerVersionInfo”匹配，因此测试假类型的简单名必须同名。
    internal sealed class PeerVersionInfo
    {
        public static PeerVersionInfo DefaultInstance { get; } = new();

        public static PeerVersionInfo LocalDefault() => DefaultInstance;
    }

    private sealed class VersionedConstructorService
    {
        internal PeerVersionInfo? Received { get; }

        public VersionedConstructorService(PeerVersionInfo versionInfo)
        {
            Received = versionInfo;
        }

        public VersionedConstructorService()
        {
        }
    }

    private sealed class ParameterlessOnlyService;

    private sealed class LocalDefaultMissingAndNoFallbackService
    {
        public LocalDefaultMissingAndNoFallbackService(Alt.PeerVersionInfo versionInfo)
        {
            _ = versionInfo;
        }
    }

    private sealed class ParameterlessWithVersionedCtorService
    {
        public ParameterlessWithVersionedCtorService(Alt.PeerVersionInfo versionInfo)
        {
            _ = versionInfo;
        }

        public ParameterlessWithVersionedCtorService()
        {
        }
    }

    [Fact]
    public void Prefers_versioned_constructor_with_local_default_value()
    {
        object created = LanConnectNetGameServiceFactory.Create(typeof(VersionedConstructorService));

        VersionedConstructorService service = Assert.IsType<VersionedConstructorService>(created);
        Assert.Same(PeerVersionInfo.DefaultInstance, service.Received);
    }

    [Fact]
    public void Falls_back_to_parameterless_constructor_when_only_that_exists()
    {
        object created = LanConnectNetGameServiceFactory.Create(typeof(ParameterlessOnlyService));

        Assert.IsType<ParameterlessOnlyService>(created);
    }

    [Fact]
    public void Falls_back_to_parameterless_constructor_when_local_default_is_absent()
    {
        object created = LanConnectNetGameServiceFactory.Create(typeof(ParameterlessWithVersionedCtorService));

        Assert.IsType<ParameterlessWithVersionedCtorService>(created);
    }

    [Fact]
    public void Throws_missing_method_when_no_usable_constructor_exists()
    {
        MissingMethodException exception = Assert.Throws<MissingMethodException>(() =>
            LanConnectNetGameServiceFactory.Create(typeof(LocalDefaultMissingAndNoFallbackService)));

        Assert.Contains(nameof(LocalDefaultMissingAndNoFallbackService), exception.Message, StringComparison.Ordinal);
        Assert.Contains("Available constructors:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Alt+PeerVersionInfo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strategy_cache_can_be_reset_between_tests()
    {
        object first = LanConnectNetGameServiceFactory.Create(typeof(ParameterlessOnlyService));
        LanConnectNetGameServiceFactory.ResetForTesting();
        object second = LanConnectNetGameServiceFactory.Create(typeof(ParameterlessOnlyService));

        Assert.IsType<ParameterlessOnlyService>(first);
        Assert.IsType<ParameterlessOnlyService>(second);
        Assert.NotSame(first, second);
    }

    // 第二套同名假类型：用于“PeerVersionInfo 存在但没有 LocalDefault()”的场景。
    // 工厂只比较参数类型的简单名（Name），因此用嵌套容器类提供同名类型即可。
    private static class Alt
    {
        internal sealed class PeerVersionInfo;
    }

    public void Dispose()
    {
        LanConnectNetGameServiceFactory.ResetForTesting();
    }
}
