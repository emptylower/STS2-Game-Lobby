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

        bool sidecarAvailable = LanConnectRitsuLibSidecarCarrier.Shared.TryEnsureRegistered(loaded)
                                && LanConnectRitsuLibSidecarCarrier.Shared.IsReady;
        return new LanConnectExternalCapabilitySnapshot(true, sidecarAvailable);
    }
}
