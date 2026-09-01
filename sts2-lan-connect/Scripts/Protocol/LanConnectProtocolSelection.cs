using System.Text;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectProtocolSelection(
    LanConnectProtocolProfile Profile,
    int SelectedLanProtocolVersion,
    LanConnectProtocolCarrier Carrier,
    string MinimumClientVersion,
    int MaxPlayers,
    string GameVersion,
    string? WireCacheSignature,
    bool RitsuLibPresent,
    string CapabilityDigest)
{
    public LanConnectProtocolSelection Validate(LanConnectProtocolOffer localOffer)
    {
        ArgumentNullException.ThrowIfNull(localOffer);
        localOffer.Validate();
        if (MaxPlayers is < LanConnectConstants.ProtocolMinPlayers or > LanConnectConstants.ProtocolMaxPlayers)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "protocol_profile_unsupported",
                $"Selection maxPlayers {MaxPlayers} is outside 2..8.");
        }

        _ = LanConnectClientVersion.ParseSupported(MinimumClientVersion);
        ValidateBounded(GameVersion, "gameVersion", 32, required: true);
        ValidateBounded(WireCacheSignature, "wireCacheSignature", 64, required: false, asciiOnly: true);
        if (CapabilityDigest.Length != 64
            || CapabilityDigest.Any(static value => !char.IsAsciiHexDigit(value) || char.IsAsciiLetterUpper(value)))
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_protocol_version_mismatch",
                "Capability digest must be exactly 64 lowercase hexadecimal characters.");
        }

        switch (Profile)
        {
            case LanConnectProtocolProfile.Compat4x5V1:
                if (SelectedLanProtocolVersion != 0 || Carrier != LanConnectProtocolCarrier.None)
                {
                    throw LanConnectProtocolFailureMapper.FromLocalException(
                        "lan_protocol_version_mismatch",
                        "Compat selection must use protocol 0 and carrier none.");
                }

                if (RitsuLibPresent || localOffer.RitsuLibPresent)
                {
                    throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibNotAllowedInCompat());
                }

                break;
            case LanConnectProtocolProfile.TailV1:
                if (!localOffer.Supports(SelectedLanProtocolVersion))
                {
                    throw LanConnectProtocolFailureMapper.FromLocalException(
                        "lan_protocol_version_mismatch",
                        $"Local offer does not support selected LAN protocol {SelectedLanProtocolVersion}.");
                }

                if (RitsuLibPresent != localOffer.RitsuLibPresent)
                {
                    throw new LanConnectProtocolException(
                        LanConnectProtocolFailure.RitsuLibPresenceMismatch(RitsuLibPresent));
                }

                if (Carrier is LanConnectProtocolCarrier.LegacyTailV1 or LanConnectProtocolCarrier.LegacySidecarV1)
                {
                    throw LanConnectProtocolFailureMapper.FromLocalException(
                        "lan_legacy_carrier_unsupported",
                        "该房间使用旧版协议载体，请升级 LAN Connect。",
                        requiredRitsuLibPresent: RitsuLibPresent);
                }

                if (Carrier != LanConnectProtocolCarrier.NativeBusV1)
                {
                    throw LanConnectProtocolFailureMapper.FromLocalException(
                        "lan_protocol_version_mismatch",
                        $"Carrier {Carrier.ToWireValue()} is not a valid Tail v1 carrier.",
                        requiredRitsuLibPresent: RitsuLibPresent);
                }

                // tail_v1 完全忽略 sidecar 可用性（native 载体不经 sidecar 就绪检查）。
                break;
            default:
                throw LanConnectProtocolFailureMapper.FromLocalException("protocol_profile_unsupported");
        }

        string expectedDigest = LanConnectCapabilityDigest.Compute(this with { CapabilityDigest = string.Empty });
        string? legacyLowercaseDigest = WireCacheSignature is null
            ? null
            : LanConnectCapabilityDigest.Compute(this with
            {
                WireCacheSignature = WireCacheSignature.ToLowerInvariant(),
                CapabilityDigest = string.Empty
            });
        if (!string.Equals(CapabilityDigest, expectedDigest, StringComparison.Ordinal)
            && !string.Equals(CapabilityDigest, legacyLowercaseDigest, StringComparison.Ordinal))
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_protocol_version_mismatch",
                "Server capability digest does not match the canonical selection.");
        }

        return this;
    }

    public static LanConnectProtocolSelection CreateLocalCompat(
        int maxPlayers,
        string gameVersion,
        string? wireCacheSignature = null)
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.Compat4x5V1,
            0,
            LanConnectProtocolCarrier.None,
            "0.3.0",
            Math.Clamp(maxPlayers, LanConnectConstants.ProtocolMinPlayers, LanConnectConstants.ProtocolMaxPlayers),
            gameVersion,
            wireCacheSignature,
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }

    private static void ValidateBounded(string? value, string name, int maxBytes, bool required, bool asciiOnly = false)
    {
        if ((required && string.IsNullOrWhiteSpace(value))
            || (value is not null && Encoding.UTF8.GetByteCount(value) > maxBytes)
            || (asciiOnly && value is not null && value.Any(static character => !char.IsAscii(character))))
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_protocol_version_mismatch",
                $"Invalid {name}.");
        }
    }
}
