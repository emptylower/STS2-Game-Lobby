using System.Reflection;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectTailRuntimeSupportResult(bool Available, string? UnavailableReason)
{
    public static readonly LanConnectTailRuntimeSupportResult Supported = new(true, null);
}

internal static class LanConnectTailRuntimeSupport
{
    private const string Sts2AssemblyName = "sts2";
    private static readonly object Sync = new();
    private static LanConnectTailRuntimeSupportResult? _current;

    public static LanConnectTailRuntimeSupportResult Current
    {
        get
        {
            lock (Sync)
            {
                _current ??= ComputeCurrent();
                return _current;
            }
        }
    }

    public static bool IsAvailable => Current.Available;

    internal static void ResetForTesting()
    {
        lock (Sync)
        {
            _current = null;
        }
    }

    internal static void SetForTesting(LanConnectTailRuntimeSupportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (Sync)
        {
            _current = result;
        }
    }

    private static LanConnectTailRuntimeSupportResult ComputeCurrent()
    {
        try
        {
            LanConnectTailRuntimeSupportResult probed = TryLoadGameAssembly(out Assembly? sts2Assembly)
                ? Probe(sts2Assembly!)
                : new(false, $"{Sts2AssemblyName} game assembly is not available");
            string gameVersion;
            try
            {
                gameVersion = LanConnectBuildInfo.GetGameVersion();
            }
            catch
            {
                gameVersion = "unavailable";
            }

            // 测试宿主进程没有 sts2.dll，游戏日志器类型解析会失败；日志缺失不应影响探测结论。
            Log.Info(
                $"sts2_lan_connect tail_runtime: available={probed.Available}, "
                + $"gameVersion={gameVersion}, "
                + $"reason={probed.UnavailableReason ?? "none"}");
            return probed;
        }
        catch (FileNotFoundException)
        {
            return new(false, $"{Sts2AssemblyName} game assembly is not available");
        }
    }

    private static bool TryLoadGameAssembly(out Assembly? sts2Assembly)
    {
        foreach (Assembly candidate in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(candidate.GetName().Name, Sts2AssemblyName, StringComparison.Ordinal))
            {
                sts2Assembly = candidate;
                return true;
            }
        }

        try
        {
            sts2Assembly = Assembly.Load(Sts2AssemblyName);
            return true;
        }
        catch
        {
            sts2Assembly = null;
            return false;
        }
    }

    // 纯反射探测：不触碰任何 0.111 专有类型的静态引用，缺失成员只会在这里被报告。
    internal static LanConnectTailRuntimeSupportResult Probe(Assembly sts2Assembly)
    {
        ArgumentNullException.ThrowIfNull(sts2Assembly);

        // 自包含地复用 LanConnectTailMessagePatchPlan.ResolveAllMessageKinds 的判定逻辑：
        // 不直接调用后者，避免触发其宿主类初始化（其中含 sts2 类型的静态引用）。
        LanConnectSidecarMessageKind[] kinds = Enum.GetValues<LanConnectSidecarMessageKind>();
        if (kinds.Length != 10)
        {
            return new(false, $"tail message kind matrix changed: found={kinds.Length}, expected=10.");
        }

        HashSet<string> resolvedMessageTypes = [];
        foreach (LanConnectSidecarMessageKind kind in kinds)
        {
            string typeName =
                $"MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.{LanConnectTailMessageTypeMatrix.GetTypeName(kind)}";
            if (sts2Assembly.GetType(typeName, throwOnError: false, ignoreCase: false) == null)
            {
                return new(false, $"tail message kind {kind} requires missing concrete type {typeName}.");
            }

            resolvedMessageTypes.Add(typeName);
        }

        if (resolvedMessageTypes.Count != 9)
        {
            return new(
                false,
                "tail message kind matrix must resolve 10 kinds to 9 concrete types; "
                + $"found={resolvedMessageTypes.Count}.");
        }

        Type? startRunPlayer = FindType(
            sts2Assembly,
            "MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer",
            out string? startRunFailure);
        if (startRunPlayer == null)
        {
            return new(false, startRunFailure!);
        }

        Type? loadRunPlayer = FindType(
            sts2Assembly,
            "MegaCrit.Sts2.Core.Entities.Multiplayer.LoadRunLobbyPlayer",
            out string? loadRunFailure);
        if (loadRunPlayer == null)
        {
            return new(false, loadRunFailure!);
        }

        if (!HasReadableMember(startRunPlayer, "id", typeof(ulong), out string? failure))
        {
            return new(false, failure!);
        }

        if (!HasWritableMember(startRunPlayer, "slotId", typeof(int), out failure))
        {
            return new(false, failure!);
        }

        if (!HasReadableMember(loadRunPlayer, "id", typeof(ulong), out failure))
        {
            return new(false, failure!);
        }

        Type? joinResponse = FindType(
            sts2Assembly,
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage",
            out string? joinResponseFailure);
        if (joinResponse == null)
        {
            return new(false, joinResponseFailure!);
        }

        if (!MemberHasType(joinResponse, "playersInLobby", typeof(List<>).MakeGenericType(startRunPlayer)))
        {
            return new(false,
                $"{joinResponse.FullName}.playersInLobby must be List<StartRunLobbyPlayer>.");
        }

        Type? beginRun = FindType(
            sts2Assembly,
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyBeginRunMessage",
            out string? beginRunFailure);
        if (beginRun == null)
        {
            return new(false, beginRunFailure!);
        }

        if (!MemberHasType(beginRun, "playersInLobby", typeof(List<>).MakeGenericType(startRunPlayer)))
        {
            return new(false,
                $"{beginRun.FullName}.playersInLobby must be List<StartRunLobbyPlayer>.");
        }

        Type? loadJoinResponse = FindType(
            sts2Assembly,
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLoadJoinResponseMessage",
            out string? loadJoinResponseFailure);
        if (loadJoinResponse == null)
        {
            return new(false, loadJoinResponseFailure!);
        }

        if (!MemberHasType(loadJoinResponse, "playersAlreadyConnected", typeof(List<>).MakeGenericType(loadRunPlayer)))
        {
            return new(false,
                $"{loadJoinResponse.FullName}.playersAlreadyConnected must be List<LoadRunLobbyPlayer>.");
        }

        Type? playerJoined = FindType(
            sts2Assembly,
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerJoinedMessage",
            out string? playerJoinedFailure);
        if (playerJoined == null)
        {
            return new(false, playerJoinedFailure!);
        }

        if (!MemberHasType(playerJoined, "lobbyPlayer", startRunPlayer))
        {
            return new(false,
                $"{playerJoined.FullName}.lobbyPlayer must be StartRunLobbyPlayer.");
        }

        if (FindType(
                sts2Assembly,
                "MegaCrit.Sts2.Core.Multiplayer.Game.INetClientGameService",
                out string? netServiceFailure) == null)
        {
            return new(false, netServiceFailure!);
        }

        return LanConnectTailRuntimeSupportResult.Supported;
    }

    private static Type? FindType(Assembly assembly, string fullName, out string? failure)
    {
        Type? type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
        failure = type == null ? $"missing {fullName}" : null;
        return type;
    }

    private static MemberInfo? FindPublicInstanceMember(Type type, string name)
    {
        return (MemberInfo?)type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
    }

    private static bool HasReadableMember(Type type, string memberName, Type memberType, out string? failure)
    {
        if (FindPublicInstanceMember(type, memberName) is not { } member)
        {
            failure = $"missing {type.FullName}.{memberName}";
            return false;
        }

        if (GetMemberType(member) != memberType)
        {
            failure = $"{type.FullName}.{memberName} is {GetMemberType(member).FullName}, expected {memberType.FullName}";
            return false;
        }

        failure = null;
        return true;
    }

    private static bool HasWritableMember(Type type, string memberName, Type memberType, out string? failure)
    {
        if (FindPublicInstanceMember(type, memberName) is not { } member)
        {
            failure = $"missing {type.FullName}.{memberName}";
            return false;
        }

        bool writable = member switch
        {
            PropertyInfo property => property.CanWrite,
            FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
            _ => false,
        };
        if (!writable)
        {
            failure = $"{type.FullName}.{memberName} is not writable";
            return false;
        }

        if (GetMemberType(member) != memberType)
        {
            failure = $"{type.FullName}.{memberName} is {GetMemberType(member).FullName}, expected {memberType.FullName}";
            return false;
        }

        failure = null;
        return true;
    }

    private static bool MemberHasType(Type type, string memberName, Type expectedType)
    {
        return FindPublicInstanceMember(type, memberName) is { } member
               && GetMemberType(member) == expectedType;
    }

    private static Type GetMemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => throw new InvalidOperationException($"Unsupported tail probe member kind {member.MemberType}."),
    };
}
