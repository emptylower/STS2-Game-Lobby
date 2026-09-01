using System.Reflection;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectExternalCapabilitySnapshot(
    bool RitsuLibPresent,
    bool LegacySidecarAvailable,
    string? RitsuLibVersion = null);

internal static class LanConnectExternalCapabilityCollector
{
    public static LanConnectExternalCapabilitySnapshot Collect(IEnumerable<Assembly>? assemblies = null)
    {
        Assembly[] loaded = (assemblies ?? AppDomain.CurrentDomain.GetAssemblies()).ToArray();
        Assembly? ritsu = loaded.FirstOrDefault(static assembly =>
            string.Equals(assembly.GetName().Name, "STS2-RitsuLib", StringComparison.OrdinalIgnoreCase));
        if (ritsu == null)
        {
            return new LanConnectExternalCapabilitySnapshot(false, false);
        }

        // native_bus_v1：只保留 presence/version 探测（诊断与预检 UX），
        // 不再探测/依赖 sidecar 可用性（0.5.18 事故状态照常放行）。
        string? version = null;
        try
        {
            version = ritsu.GetName().Version?.ToString();
        }
        catch
        {
            // 诊断字段：解析失败按 unknown 处理。
        }

        return new LanConnectExternalCapabilitySnapshot(true, true, version);
    }
}
