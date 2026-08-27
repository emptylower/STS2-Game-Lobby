namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectProtocolOffer(
    int LanProtocolMin,
    int LanProtocolMax,
    string ClientVersion,
    bool RitsuLibPresent,
    bool RitsuLibSidecarAvailable)
{
    public static LanConnectProtocolOffer CreateCurrent()
    {
        LanConnectExternalCapabilitySnapshot capabilities = LanConnectExternalCapabilityCollector.Collect();
        string clientVersion = LanConnectClientVersion.ParseSupported(LanConnectBuildInfo.GetModVersion()).Canonical;
        int tailLanProtocolVersion = LanConnectTailRuntimeSupport.IsAvailable
            ? LanConnectConstants.TailLanProtocolVersion
            : 0;
        return new LanConnectProtocolOffer(
            tailLanProtocolVersion,
            tailLanProtocolVersion,
            clientVersion,
            capabilities.RitsuLibPresent,
            capabilities.RitsuLibSidecarAvailable);
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
        if (!RitsuLibPresent && RitsuLibSidecarAvailable)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "ritsulib_sidecar_unavailable",
                "RitsuLib sidecar cannot be available when RitsuLib is absent.");
        }

        return this;
    }

    public bool Supports(int version) => version >= LanProtocolMin && version <= LanProtocolMax;
}
