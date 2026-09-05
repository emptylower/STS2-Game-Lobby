using System.Reflection;
using System.Runtime.ExceptionServices;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// `MegaCrit.Sts2.Core.Modding.AssemblyInfo`（0.111 引入，0.107.1 不存在）的窄反射适配器：
/// 源码中不得出现对该类型的直接成员引用（MemberRef 黑名单契约测试强制），一律经本适配器访问。
/// 解析结果（类型 + 三个成员）经 `Lazy&lt;&gt;` 只解析一次；属性值每次调用都经反射读取，
/// 与直接访问静态属性的时序语义一致。写法参考 `LanConnectTailRuntimeSupport`。
/// `MegaCrit.Sts2.Core.Modding.Mod` 在 0.107.1 已存在（ModManager 同时代），可强类型引用。
/// </summary>
internal static class LanConnectAssemblyInfoAdapter
{
    private const string AssemblyInfoTypeName = "MegaCrit.Sts2.Core.Modding.AssemblyInfo";

    private static readonly Lazy<ResolvedMembers> Members =
        new(ResolveMembers, LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed record ResolvedMembers(
        PropertyInfo? ModMap,
        PropertyInfo? MockTypes,
        MethodInfo? ModForTypeMethod)
    {
        public bool IsAvailable => ModMap != null && MockTypes != null && ModForTypeMethod != null;
    }

    /// <summary>类型存在且三个成员（ModMap / MockTypes / ModForType）全部解析成功；0.107.1 上为 false。</summary>
    internal static bool IsAvailable => Members.Value.IsAvailable;

    /// <summary>等价于直接访问的 `ModMap != null || MockTypes != null`（不可用时恒为 false）。</summary>
    internal static bool IsInitialized =>
        IsAvailable && (ReadStaticOrNull(Members.Value.ModMap) != null || ReadStaticOrNull(Members.Value.MockTypes) != null);

    /// <summary>
    /// 反射调用 `AssemblyInfo.ModForType(Type, out bool)`；目标内部异常原样重抛以保持直接调用的异常形态。
    /// 适配器不可用（0.107.1）时抛 <see cref="InvalidOperationException"/>——调用方（指纹计算）
    /// 只在 IsInitialized 守卫之后触达本方法，正常流程不会走到这里。
    /// </summary>
    internal static Mod? ModForType(Type type, out bool isBaseGame)
    {
        ArgumentNullException.ThrowIfNull(type);

        ResolvedMembers members = Members.Value;
        if (!members.IsAvailable || members.ModForTypeMethod == null)
        {
            throw new InvalidOperationException("AssemblyInfo is unavailable on this game version.");
        }

        object?[] arguments = [type, false];
        object? result;
        try
        {
            result = members.ModForTypeMethod.Invoke(null, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw; // 上一行必抛，仅为满足可达性分析。
        }

        isBaseGame = (bool)(arguments[1] ?? false);
        return result as Mod;
    }

    private static ResolvedMembers ResolveMembers()
    {
        Type? type = typeof(MessageTypes).Assembly.GetType(AssemblyInfoTypeName, throwOnError: false, ignoreCase: false);
        if (type == null)
        {
            return new ResolvedMembers(null, null, null);
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        return new ResolvedMembers(
            type.GetProperty("ModMap", flags),
            type.GetProperty("MockTypes", flags),
            type.GetMethod("ModForType", flags));
    }

    private static object? ReadStaticOrNull(PropertyInfo? property) => property?.GetValue(null);
}
