using System;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Networking.Sidecar;
using Xunit;

namespace Sts2TailPrototype;

public sealed class PublicContractInspectionTests
{
    [Fact]
    public void Public_api_exposes_complete_network_extension_inventory()
    {
        PublicRitsuContract contract = PublicRitsuContract.Load(typeof(RitsuLibFramework).Assembly);
        Assert.True(contract.CanEnumerateAllNetworkExtensions);
        Assert.True(contract.CanClassifyCriticality);
        Assert.True(contract.CanCoordinateTailOrder);
        Assert.DoesNotContain(contract.ReferencedMembers, name => name.Contains("SerializePatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Sidecar_handshake_drive_methods_are_public_and_invocable()
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;

        MethodInfo observeNetService = typeof(RitsuLibSidecarSessionManager).GetMethod(
            nameof(RitsuLibSidecarSessionManager.ObserveNetService),
            publicStatic,
            binder: null,
            types: [typeof(INetGameService)],
            modifiers: null) ?? throw new MissingMethodException(
                typeof(RitsuLibSidecarSessionManager).FullName, "ObserveNetService(INetGameService)");
        Assert.NotNull(observeNetService.CreateDelegate(typeof(Action<INetGameService>)));

        MethodInfo trySendHello = typeof(RitsuLibSidecarConnectionExchange).GetMethod(
            nameof(RitsuLibSidecarConnectionExchange.TrySendClientHelloIfReachable),
            publicStatic,
            binder: null,
            types: [typeof(INetGameService)],
            modifiers: null) ?? throw new MissingMethodException(
                typeof(RitsuLibSidecarConnectionExchange).FullName, "TrySendClientHelloIfReachable(INetGameService)");
        Assert.NotNull(trySendHello.CreateDelegate(typeof(Action<INetGameService>)));

        // The parameterless drives are no-ops without an active session; invoke them for
        // real against the loaded assembly instead of trusting signatures alone.
        RitsuLibSidecarConnectionExchange.TickHandshakeNegotiation();
        RitsuLibSidecarSessionManager.RefreshAllReachabilityFromProviders();
    }

    [Fact]
    public void Sidecar_high_level_send_exposes_public_netservice_overloads()
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;

        // The RunManager-based overloads the RC4 bridge transpiles.
        foreach (string methodName in new[]
        {
            nameof(RitsuLibSidecarHighLevelSend.TrySendAsClient),
            nameof(RitsuLibSidecarHighLevelSend.TrySendAsHostToPeer),
        })
        {
            MethodInfo[] overloads = typeof(RitsuLibSidecarHighLevelSend)
                .GetMethods(publicStatic)
                .Where(method => method.Name == methodName)
                .ToArray();
            Assert.Contains(overloads, method =>
                method.GetParameters().Length > 0 &&
                method.GetParameters()[0].ParameterType == typeof(RunManager));
        }

        // Public INetGameService overloads usable without any transpiler.
        foreach (string methodName in new[]
        {
            nameof(RitsuLibSidecarHighLevelSend.TrySendAsClient),
            nameof(RitsuLibSidecarHighLevelSend.TrySendAsHostToPeer),
            nameof(RitsuLibSidecarHighLevelSend.TrySendAsHostBroadcast),
            nameof(RitsuLibSidecarHighLevelSend.TrySendAsHostBroadcastToAllConnected),
        })
        {
            MethodInfo[] overloads = typeof(RitsuLibSidecarHighLevelSend)
                .GetMethods(publicStatic)
                .Where(method => method.Name == methodName)
                .ToArray();
            Assert.Contains(overloads, method =>
                method.GetParameters().Length > 0 &&
                method.GetParameters()[0].ParameterType == typeof(INetGameService));
        }
    }

    [Fact]
    public void Sidecar_has_no_public_netservice_resolver_hook()
    {
        // Records the negative space: no exported member lets a caller substitute the
        // INetGameService used by the RunManager-based send overloads, so the RC4
        // transpiler redirection cannot be reproduced through the public API.
        string[] resolverLikeMembers = typeof(RitsuLibFramework).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(RitsuLibSidecar).Namespace)
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(member => member.Name)
            .Where(name =>
                name.Contains("NetServiceResolver", StringComparison.Ordinal) ||
                name.Contains("ResolveNetService", StringComparison.Ordinal) ||
                name.Contains("NetServiceOverride", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(resolverLikeMembers);
    }
}
