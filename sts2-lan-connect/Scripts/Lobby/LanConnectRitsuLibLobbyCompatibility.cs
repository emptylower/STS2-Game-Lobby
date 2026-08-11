using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectRitsuLibLobbyCompatibility
{
    private const string HighLevelSendTypeName =
        "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarHighLevelSend";
    private const string ConnectionExchangeTypeName =
        "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarConnectionExchange";
    private const string SessionManagerTypeName =
        "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarSessionManager";

    private static readonly object PatchLock = new();
    private static readonly MethodInfo RunManagerNetServiceGetter =
        AccessTools.PropertyGetter(typeof(RunManager), nameof(RunManager.NetService)) ??
        throw new MissingMethodException(typeof(RunManager).FullName, $"get_{nameof(RunManager.NetService)}");
    private static readonly MethodInfo ResolveLobbyNetServiceMethod =
        AccessTools.Method(typeof(LanConnectRitsuLibLobbyCompatibility), nameof(ResolveLobbyNetService)) ??
        throw new MissingMethodException(typeof(LanConnectRitsuLibLobbyCompatibility).FullName, nameof(ResolveLobbyNetService));

    private static Harmony? _harmony;
    private static bool _patchApplied;
    private static bool _assemblyLoadHookRegistered;
    private static INetGameService? _trackedLobbyNetService;
    private static Action<INetGameService>? _observeNetService;
    private static Action? _tickHandshakeNegotiation;
    private static Action? _refreshReachability;
    private static Action<INetGameService>? _trySendHello;
    private static int _tickFailureLogged;

    internal static void Apply(Harmony harmony)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        _harmony = harmony;
        TryApply(harmony, "mod_init");
    }

    internal static void TrackLobbyNetService(INetGameService netService)
    {
        ArgumentNullException.ThrowIfNull(netService);
        Volatile.Write(ref _trackedLobbyNetService, netService);
    }

    internal static void ReleaseLobbyNetService(INetGameService netService)
    {
        ArgumentNullException.ThrowIfNull(netService);
        Interlocked.CompareExchange(ref _trackedLobbyNetService, null, netService);
    }

    internal static void Tick(INetGameService? netService)
    {
        netService ??= Volatile.Read(ref _trackedLobbyNetService);
        if (!_patchApplied || netService?.IsConnected != true)
        {
            return;
        }

        TrackLobbyNetService(netService);
        try
        {
            _observeNetService?.Invoke(netService);
            _tickHandshakeNegotiation?.Invoke();
            _refreshReachability?.Invoke();
            _trySendHello?.Invoke(netService);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _tickFailureLogged, 1) == 0)
            {
                Log.Warn(
                    $"sts2_lan_connect ritsulib_compatibility: lobby handshake drive failed; " +
                    $"later frames will retry. {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    internal static List<CodeInstruction> InjectResolverAfterGetter(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo netServiceGetter,
        MethodInfo resolver)
    {
        List<CodeInstruction> original = instructions.ToList();
        int[] getterReadIndices = FindGetterReadIndices(
            original.Select(static instruction => instruction.operand as MethodInfo).ToArray(),
            netServiceGetter);
        if (getterReadIndices.Length == 0)
        {
            throw new InvalidOperationException(
                "RitsuLib RunManager send overload no longer reads RunManager.NetService.");
        }

        HashSet<int> getterReads = [.. getterReadIndices];
        List<CodeInstruction> patched = [];
        for (var index = 0; index < original.Count; index++)
        {
            CodeInstruction instruction = original[index];
            patched.Add(instruction);
            if (!getterReads.Contains(index))
            {
                continue;
            }

            patched.Add(new CodeInstruction(OpCodes.Call, resolver));
        }

        return patched;
    }

    internal static int[] FindGetterReadIndices(
        IReadOnlyList<MethodInfo?> instructionOperands,
        MethodInfo netServiceGetter)
    {
        ArgumentNullException.ThrowIfNull(instructionOperands);
        ArgumentNullException.ThrowIfNull(netServiceGetter);
        return instructionOperands
            .Select((operand, index) => (operand, index))
            .Where(candidate => candidate.operand == netServiceGetter)
            .Select(static candidate => candidate.index)
            .ToArray();
    }

    private static void TryApply(Harmony harmony, string source)
    {
        lock (PatchLock)
        {
            if (_patchApplied)
            {
                return;
            }

            Type? sendType = AccessTools.TypeByName(HighLevelSendTypeName);
            Type? exchangeType = AccessTools.TypeByName(ConnectionExchangeTypeName);
            Type? sessionType = AccessTools.TypeByName(SessionManagerTypeName);
            if (sendType == null || exchangeType == null || sessionType == null)
            {
                RegisterAssemblyLoadRetry();
                return;
            }

            MethodInfo clientRunManagerSend = RequireRunManagerOverload(sendType, "TrySendAsClient");
            MethodInfo hostRunManagerSend = RequireRunManagerOverload(sendType, "TrySendAsHostToPeer");
            Action<INetGameService> observeNetService = CreateNetServiceAction(
                sessionType,
                "ObserveNetService");
            Action tickHandshakeNegotiation = CreateAction(exchangeType, "TickHandshakeNegotiation");
            Action refreshReachability = CreateAction(sessionType, "RefreshAllReachabilityFromProviders");
            Action<INetGameService> trySendHello = CreateNetServiceAction(
                exchangeType,
                "TrySendClientHelloIfReachable");

            HarmonyMethod transpiler = new(
                typeof(LanConnectRitsuLibLobbyCompatibility),
                nameof(TranspileRunManagerSend));
            harmony.Patch(clientRunManagerSend, transpiler: transpiler);
            harmony.Patch(hostRunManagerSend, transpiler: transpiler);

            _observeNetService = observeNetService;
            _tickHandshakeNegotiation = tickHandshakeNegotiation;
            _refreshReachability = refreshReachability;
            _trySendHello = trySendHello;
            _patchApplied = true;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            Log.Info(
                $"sts2_lan_connect ritsulib_compatibility: enabled lobby handshake drive and " +
                $"RunManager send fallback, source={source}, assembly={sendType.Assembly.GetName().Name}, " +
                $"version={sendType.Assembly.GetName().Version}.");
        }
    }

    private static MethodInfo RequireRunManagerOverload(Type sendType, string methodName)
    {
        MethodInfo[] matches = sendType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length > 0 && parameters[0].ParameterType == typeof(RunManager);
            })
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new MissingMethodException(
                sendType.FullName,
                $"{methodName}(RunManager, ...) unique overload; found={matches.Length}");
    }

    private static Action CreateAction(Type declaringType, string methodName)
    {
        MethodInfo method = declaringType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null) ?? throw new MissingMethodException(declaringType.FullName, methodName);
        return method.CreateDelegate<Action>();
    }

    private static Action<INetGameService> CreateNetServiceAction(Type declaringType, string methodName)
    {
        MethodInfo method = declaringType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(INetGameService)],
            modifiers: null) ?? throw new MissingMethodException(declaringType.FullName, methodName);
        return method.CreateDelegate<Action<INetGameService>>();
    }

    private static IEnumerable<CodeInstruction> TranspileRunManagerSend(
        IEnumerable<CodeInstruction> instructions) =>
        // RitsuLib handles the lobby handshake before RunManager owns the LAN service.
        InjectResolverAfterGetter(
            instructions,
            RunManagerNetServiceGetter,
            ResolveLobbyNetServiceMethod);

    private static INetGameService? ResolveLobbyNetService(INetGameService? runNetService)
    {
        INetGameService? lobbyNetService = Volatile.Read(ref _trackedLobbyNetService);
        return lobbyNetService?.IsConnected == true
            ? lobbyNetService
            : runNetService;
    }

    private static void RegisterAssemblyLoadRetry()
    {
        if (_assemblyLoadHookRegistered)
        {
            return;
        }

        _assemblyLoadHookRegistered = true;
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        Log.Info(
            "sts2_lan_connect ritsulib_compatibility: RitsuLib not loaded; deferring compatibility patch.");
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (_patchApplied || _harmony == null)
        {
            return;
        }

        string? assemblyName = args.LoadedAssembly.GetName().Name;
        if (assemblyName?.Contains("RitsuLib", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        try
        {
            TryApply(_harmony, "assembly_load");
        }
        catch (Exception ex)
        {
            Log.Error(
                $"sts2_lan_connect ritsulib_compatibility: deferred patch failed: " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
