namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectProtocolOffer(
    int LanProtocolMin,
    int LanProtocolMax,
    string ClientVersion,
    bool RitsuLibPresent,
    bool LegacySidecarAvailable,
    string? RegistryFingerprint = null,
    string? RitsuLibVersion = null)
{
    public static LanConnectProtocolOffer CreateCurrent()
    {
        LanConnectExternalCapabilitySnapshot capabilities = LanConnectExternalCapabilityCollector.Collect();
        string clientVersion = LanConnectClientVersion.ParseSupported(LanConnectBuildInfo.GetModVersion()).Canonical;
        int tailLanProtocolVersion = LanConnectTailRuntimeSupport.IsAvailable
            ? LanConnectConstants.TailLanProtocolVersion
            : 0;
        // 指纹需要游戏消息注册表已初始化；不可用时留空（服务端创建门禁会拒绝并给出明确错误）。
        string? fingerprint = null;
        try
        {
            fingerprint = LanConnectRegistryFingerprint.Compute();
        }
        catch (Exception)
        {
            // 游戏消息注册表不可用（如测试宿主）：留空；创建门禁会给出结构化错误而非崩溃。
        }

        return new LanConnectProtocolOffer(
            tailLanProtocolVersion,
            tailLanProtocolVersion,
            clientVersion,
            capabilities.RitsuLibPresent,
            capabilities.LegacySidecarAvailable,
            fingerprint,
            capabilities.RitsuLibVersion);
    }

    public LanConnectProtocolOffer Validate()
    {
        if (LanProtocolMin < 0 || LanProtocolMax < LanProtocolMin || LanProtocolMax > ushort.MaxValue)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_protocol_version_mismatch",
                $"Invalid LAN protocol range {LanProtocolMin}..{LanProtocolMax}.");
        }

        _ = LanConnectClientVersion.ParseSupported(ClientVersion);
        // native_bus_v1：sidecar 可用性仅是诊断位，不再参与 offer 校验（0.5.18 事故正面回归）。
        return this;
    }

    public bool Supports(int version) => version >= LanProtocolMin && version <= LanProtocolMax;
}
