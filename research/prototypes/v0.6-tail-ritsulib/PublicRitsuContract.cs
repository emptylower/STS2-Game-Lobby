using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sts2TailPrototype;

internal sealed record PublicRitsuContract(
    bool CanEnumerateAllNetworkExtensions,
    bool CanClassifyCriticality,
    bool CanCoordinateTailOrder,
    IReadOnlyList<string> ReferencedMembers)
{
    internal static PublicRitsuContract Load(Assembly assembly)
    {
        Type[] publicTypes = assembly.GetExportedTypes();
        string[] publicMembers = publicTypes
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(member => $"{member.DeclaringType?.FullName}.{member.Name}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return PublicContractRules.Evaluate(publicTypes, publicMembers);
    }
}

internal static class PublicContractRules
{
    private const string MessageExtensionsNamespace = "STS2RitsuLib.Networking.MessageExtensions";
    private const string TailExtensionsTypeName =
        MessageExtensionsNamespace + ".RitsuNetMessageTailExtensions";

    internal static PublicRitsuContract Evaluate(Type[] publicTypes, IReadOnlyList<string> publicMembers)
    {
        ArgumentNullException.ThrowIfNull(publicTypes);
        ArgumentNullException.ThrowIfNull(publicMembers);

        // Tail-order/cursor coordination: the public RitsuNetMessageTailExtensions
        // Write/Read contract hands the caller-owned PacketWriter/PacketReader cursor to
        // RitsuLib immediately after the vanilla body, and RegisterBytes performs a real
        // registration. RitsuInteropMatrixTests invokes all three members against real
        // registrations in every install combination.
        bool canCoordinateTailOrder =
            publicMembers.Contains(TailExtensionsTypeName + ".RegisterBytes", StringComparer.Ordinal) &&
            publicMembers.Contains(TailExtensionsTypeName + ".Write", StringComparer.Ordinal) &&
            publicMembers.Contains(TailExtensionsTypeName + ".Read", StringComparer.Ordinal);

        // Complete network-extension inventory requires a named, exported accessor for
        // registrations. An arbitrary enumerable is not evidence that the full mutable
        // extension set is available to callers.
        bool canEnumerateAllNetworkExtensions = publicTypes
            .Where(type => type.Namespace == MessageExtensionsNamespace)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Cast<MemberInfo>()
                .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)))
            .Any(IsNamedRegistrationInventory);

        // Critical classification requires a criticality indicator exposed on the public
        // network-extension registration surface. A string match on an unrelated public
        // type is insufficient: it must be exposed by the registration descriptor or its
        // registration method.
        bool canClassifyCriticality = publicTypes
            .Where(type => type.Namespace == MessageExtensionsNamespace)
            .Where(type => type.Name.Contains("Extension", StringComparison.Ordinal))
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .SelectMany(ExpandCriticalityIndicators)
            .Any(indicator => indicator);

        return new PublicRitsuContract(
            CanEnumerateAllNetworkExtensions: canEnumerateAllNetworkExtensions,
            CanClassifyCriticality: canClassifyCriticality,
            CanCoordinateTailOrder: canCoordinateTailOrder,
            ReferencedMembers: publicMembers);
    }

    private static bool IsNamedRegistrationInventory(MemberInfo member)
    {
        if (!member.Name.Contains("Registration", StringComparison.Ordinal) &&
            !member.Name.Contains("Extension", StringComparison.Ordinal))
        {
            return false;
        }

        Type? returnType = member switch
        {
            MethodInfo method => method.ReturnType,
            PropertyInfo property => property.PropertyType,
            _ => null,
        };
        if (returnType == null || returnType == typeof(string) || returnType == typeof(byte[]))
        {
            return false;
        }

        return returnType != typeof(void) &&
               typeof(System.Collections.IEnumerable).IsAssignableFrom(returnType) &&
               (returnType.Name.Contains("Registration", StringComparison.Ordinal) ||
                returnType.GenericTypeArguments.Any(argument =>
                    argument.Name.Contains("Registration", StringComparison.Ordinal) ||
                    argument.Name.Contains("Extension", StringComparison.Ordinal)));
    }

    private static IEnumerable<bool> ExpandCriticalityIndicators(MemberInfo member)
    {
        if (member.Name.Contains("Critical", StringComparison.OrdinalIgnoreCase))
        {
            yield return true;
        }

        if (member is MethodInfo method)
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.Name?.Contains("critical", StringComparison.OrdinalIgnoreCase) == true;
            }
        }
    }
}
