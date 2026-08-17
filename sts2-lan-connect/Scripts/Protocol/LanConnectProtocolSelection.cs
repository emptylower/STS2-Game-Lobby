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

                LanConnectProtocolCarrier expectedCarrier = RitsuLibPresent
                    ? LanConnectProtocolCarrier.RitsuLibSidecarV1
                    : LanConnectProtocolCarrier.StandaloneTailV1;
                if (Carrier != expectedCarrier)
                {
                    throw LanConnectProtocolFailureMapper.FromLocalException(
                        "ritsulib_presence_mismatch",
                        $"Carrier {Carrier.ToWireValue()} conflicts with frozen RitsuLib presence {RitsuLibPresent}.",
                        requiredRitsuLibPresent: RitsuLibPresent);
                }

                if (RitsuLibPresent && !localOffer.RitsuLibSidecarAvailable)
                {
                    throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibSidecarUnavailable());
                }

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
