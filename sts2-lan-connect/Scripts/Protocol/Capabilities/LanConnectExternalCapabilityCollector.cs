using System.Reflection;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectExternalCapabilitySnapshot(
    bool RitsuLibPresent,
    bool RitsuLibSidecarAvailable);

internal static class LanConnectExternalCapabilityCollector
{
    private const string SidecarRegistryType =
        "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarTypedMessageRegistry";
    private const string SidecarSessionManagerType =
        "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarSessionManager";

    public static LanConnectExternalCapabilitySnapshot Collect(IEnumerable<Assembly>? assemblies = null)
    {
        Assembly[] loaded = (assemblies ?? AppDomain.CurrentDomain.GetAssemblies()).ToArray();
        bool present = loaded.Any(static assembly =>
            string.Equals(assembly.GetName().Name, "STS2-RitsuLib", StringComparison.OrdinalIgnoreCase));
        if (!present)
        {
            return new LanConnectExternalCapabilitySnapshot(false, false);
        }

        bool registryAvailable = FindPublicType(loaded, SidecarRegistryType) is not null;
        Type? sessionManager = FindPublicType(loaded, SidecarSessionManagerType);
        bool sessionAvailable = sessionManager?.GetMethod(
            "ObserveNetService",
            BindingFlags.Public | BindingFlags.Static) is not null
            && sessionManager.GetMethod("CanSendToPeer", BindingFlags.Public | BindingFlags.Static) is not null
            && sessionManager.GetMethod("SetPeerReachabilityHint", BindingFlags.Public | BindingFlags.Static) is not null;
        return new LanConnectExternalCapabilitySnapshot(true, registryAvailable && sessionAvailable);
    }

    private static Type? FindPublicType(IEnumerable<Assembly> assemblies, string fullName) =>
        assemblies.Select(assembly => assembly.GetType(fullName, throwOnError: false, ignoreCase: false))
            .FirstOrDefault(static type => type?.IsPublic == true);
}
