using System.Collections.Concurrent;
using System.Reflection;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace Sts2LanConnect.Scripts;

// 兼容路径上的 NetHostGameService / NetClientGameService 构造入口。
//
// 游戏 0.107.1 没有 MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo（0.110.x 引入），
// 因此本类所有方法体都不得出现 PeerVersionInfo 标识符——只能按名字符串反射解析。
// 在 0.111 上选择 (PeerVersionInfo.LocalDefault()) 构造；在旧版本上自动退回无参构造，
// 与 v0.5.6 的行为一致（版本信息补丁由 LanConnectPeerVersionInfoPatches 容错安装）。
internal static class LanConnectNetGameServiceFactory
{
    private const string VersionInfoParameterName = "PeerVersionInfo";
    private const string LocalDefaultMethodName = "LocalDefault";

    private static readonly ConcurrentDictionary<Type, Func<object>> StrategyCache = new();

    public static NetHostGameService CreateHost() => (NetHostGameService)Create(typeof(NetHostGameService));

    public static NetClientGameService CreateClient() => (NetClientGameService)Create(typeof(NetClientGameService));

    internal static object Create(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return StrategyCache.GetOrAdd(serviceType, ResolveStrategy)();
    }

    internal static void ResetForTesting()
    {
        StrategyCache.Clear();
    }

    private static Func<object> ResolveStrategy(Type serviceType)
    {
        ConstructorInfo? versionedConstructor = FindVersionedConstructor(serviceType);
        if (versionedConstructor != null)
        {
            MethodInfo? localDefault =
                FindLocalDefaultMethod(versionedConstructor.GetParameters()[0].ParameterType);
            if (localDefault != null)
            {
                return () => versionedConstructor.Invoke([localDefault.Invoke(null, [])]);
            }
        }

        ConstructorInfo? parameterlessConstructor = serviceType.GetConstructor(Type.EmptyTypes);
        if (parameterlessConstructor != null)
        {
            return () => parameterlessConstructor.Invoke([]);
        }

        throw new MissingMethodException(
            $"{serviceType.FullName} has no usable constructor. Available constructors: "
            + DescribeConstructors(serviceType));
    }

    private static ConstructorInfo? FindVersionedConstructor(Type serviceType)
    {
        return serviceType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(constructor =>
                constructor.GetParameters().Length == 1
                && string.Equals(
                    constructor.GetParameters()[0].ParameterType.Name,
                    VersionInfoParameterName,
                    StringComparison.Ordinal));
    }

    private static MethodInfo? FindLocalDefaultMethod(Type parameterType)
    {
        return parameterType.GetMethod(
            LocalDefaultMethodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
    }

    private static string DescribeConstructors(Type serviceType)
    {
        List<string> signatures = serviceType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(constructor => string.Join(
                ", ",
                constructor.GetParameters().Select(static parameter =>
                    $"{parameter.ParameterType.FullName ?? parameter.ParameterType.Name} {parameter.Name}")))
            .ToList();
        return signatures.Count == 0 ? "<none>" : string.Join("; ", signatures);
    }
}
