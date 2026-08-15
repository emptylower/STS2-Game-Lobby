using System.Reflection;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectExternalCapabilitySnapshot(
    bool RitsuLibPresent,
    bool RitsuLibSidecarAvailable);

internal static class LanConnectExternalCapabilityCollector
{
    public static LanConnectExternalCapabilitySnapshot Collect(IEnumerable<Assembly>? assemblies = null)
    {
        Assembly[] loaded = (assemblies ?? AppDomain.CurrentDomain.GetAssemblies()).ToArray();
        bool present = loaded.Any(static assembly =>
            string.Equals(assembly.GetName().Name, "STS2-RitsuLib", StringComparison.OrdinalIgnoreCase));
        if (!present)
        {
            return new LanConnectExternalCapabilitySnapshot(false, false);
        }

        // Public API shape alone is not enough to prove the sidecar carrier is usable.
        // Keep Ritsu Tail rooms fail-closed until the real two-process carrier/barrier gate is green.
        return new LanConnectExternalCapabilitySnapshot(true, false);
    }
}
