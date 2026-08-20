using System.Reflection;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectPatchDiagnosticDescriptor(
    string PlanId,
    int Ordinal,
    int Total,
    string Category,
    string? MessageType,
    MethodBase Target,
    MethodInfo Hook,
    string HarmonyOwner,
    int HarmonyPriority)
{
    public string? PlanProfile { get; init; }

    public IReadOnlyList<MethodInfo> AdditionalHooks { get; init; } = [];

    public IReadOnlyList<int> AdditionalHookPriorities { get; init; } = [];

    public IReadOnlyList<MethodInfo> AllHooks => [Hook, .. AdditionalHooks];

    public IReadOnlyList<int> AllHookPriorities => [HarmonyPriority, .. AdditionalHookPriorities];
}
