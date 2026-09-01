namespace Sts2LanConnect.Scripts;

internal enum LanConnectProtocolProfile
{
    Compat4x5V1,
    TailV1
}

internal enum LanConnectProtocolCarrier
{
    None,
    LegacyTailV1,
    LegacySidecarV1,
    NativeBusV1
}

internal static class LanConnectProtocolProfileExtensions
{
    public const string CompatCanonical = "compat_4_5_v1";
    public const string TailCanonical = "tail_v1";

    public static string ToCanonical(this LanConnectProtocolProfile profile) => profile switch
    {
        LanConnectProtocolProfile.Compat4x5V1 => CompatCanonical,
        LanConnectProtocolProfile.TailV1 => TailCanonical,
        _ => throw LanConnectProtocolFailureMapper.FromLocalException(
            "protocol_profile_unsupported",
            $"Unknown protocol profile enum value {(int)profile}.")
    };

    public static LanConnectProtocolProfile ParseCanonical(string? value) => value switch
    {
        CompatCanonical => LanConnectProtocolProfile.Compat4x5V1,
        TailCanonical => LanConnectProtocolProfile.TailV1,
        _ => throw LanConnectProtocolFailureMapper.FromLocalException(
            "protocol_profile_unsupported",
            $"Unknown canonical protocol profile '{value ?? "<missing>"}'.")
    };

    public static LanConnectProtocolProfile ParseApiProjection(
        string? canonicalProfile,
        string? legacyProfile,
        LanConnectClientApiGeneration generation)
    {
        if (generation == LanConnectClientApiGeneration.Canonical06Plus)
        {
            return ParseCanonical(canonicalProfile);
        }

        return legacyProfile switch
        {
            LanConnectProtocolProfiles.Extended8p =>
                LanConnectProtocolProfile.Compat4x5V1,
            _ => throw LanConnectProtocolFailureMapper.FromLocalException(
                "protocol_profile_unsupported",
                $"Unknown legacy protocol profile '{legacyProfile ?? "<missing>"}'.")
        };
    }

    public static string ToWireValue(this LanConnectProtocolCarrier carrier) => carrier switch
    {
        LanConnectProtocolCarrier.None => "none",
        LanConnectProtocolCarrier.LegacyTailV1 => "standalone_tail_v1",
        LanConnectProtocolCarrier.LegacySidecarV1 => "ritsulib_sidecar_v1",
        _ => throw LanConnectProtocolFailureMapper.FromLocalException(
            "protocol_profile_unsupported",
            $"Unknown protocol carrier enum value {(int)carrier}.")
    };

    public static LanConnectProtocolCarrier ParseCarrier(string? value) => value switch
    {
        "none" => LanConnectProtocolCarrier.None,
        "standalone_tail_v1" => LanConnectProtocolCarrier.LegacyTailV1,
        "native_bus_v1" => LanConnectProtocolCarrier.NativeBusV1,
        "ritsulib_sidecar_v1" => LanConnectProtocolCarrier.LegacySidecarV1,
        _ => throw LanConnectProtocolFailureMapper.FromLocalException(
            "protocol_profile_unsupported",
            $"Unknown protocol carrier '{value ?? "<missing>"}'.")
    };
}
