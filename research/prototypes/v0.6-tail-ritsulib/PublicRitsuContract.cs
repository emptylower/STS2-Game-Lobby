using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sts2TailPrototype;

internal sealed record PublicRitsuContract(bool HasTypedRequiredDescriptor, bool HasDirectNetServiceSend, bool HasPublicReachabilityHint, bool HasSessionReachability, IReadOnlyList<string> ReferencedMembers)
{
    internal static PublicRitsuContract Load(Assembly assembly)
    {
        Type[] publicTypes = assembly.GetExportedTypes();
        string[] publicMembers = publicTypes.SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)).Select(member => $"{member.DeclaringType?.FullName}.{member.Name}").OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return PublicContractRules.Evaluate(publicTypes, publicMembers);
    }
}

internal static class PublicContractRules
{
    private const string SidecarNamespace = "STS2RitsuLib.Networking.Sidecar";
    private const string TypedRegistry = SidecarNamespace + ".RitsuLibSidecarTypedMessageRegistry";
    private const string SessionManager = SidecarNamespace + ".RitsuLibSidecarSessionManager";
    private const string NetService = "MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService";

    internal static PublicRitsuContract Evaluate(Type[] publicTypes, IReadOnlyList<string> publicMembers)
    {
        ArgumentNullException.ThrowIfNull(publicTypes);
        ArgumentNullException.ThrowIfNull(publicMembers);
        Type? registry = publicTypes.SingleOrDefault(type => type.FullName == TypedRegistry);
        Type? session = publicTypes.SingleOrDefault(type => type.FullName == SessionManager);
        bool descriptor = publicTypes.Any(type => type.FullName == SidecarNamespace + ".RitsuLibSidecarMessageDescriptor`1") && HasGenericMethod(registry, "Register", 1);
        bool directSend = HasMethodWithFirstParameter(registry, "SendToHost", NetService) && HasMethodWithFirstParameter(registry, "SendToPeer", NetService);
        bool hints = HasMethodWithFirstParameter(session, "SetPeerReachabilityHint", "System.UInt64") && HasMethodWithFirstParameter(session, "ObserveNetService", NetService) && HasMethodWithFirstParameter(session, "CanSendToPeer", "System.UInt64");
        bool sessionReachability = HasEvent(session, "SessionBound") && HasEvent(session, "SessionUnbound") && HasEvent(session, "PeerReachabilityChanged") && HasGenericMethod(registry, "Subscribe", 1);
        return new PublicRitsuContract(descriptor, directSend, hints, sessionReachability, publicMembers);
    }

    private static bool HasGenericMethod(Type? type, string name, int genericArity) => type?.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(method => method.Name == name && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == genericArity) == true;
    private static bool HasMethodWithFirstParameter(Type? type, string name, string firstParameter) => type?.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(method => method.Name == name && method.GetParameters().FirstOrDefault()?.ParameterType.FullName == firstParameter) == true;
    private static bool HasEvent(Type? type, string name) => type?.GetEvent(name, BindingFlags.Public | BindingFlags.Static) != null;
}
