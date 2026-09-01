namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectCreateRoomIntent(
    LanConnectProtocolProfile Profile,
    int MaxPlayers,
    LanConnectProtocolOffer Offer)
{
    public LanConnectCreateRoomIntent Validate()
    {
        if (MaxPlayers is < LanConnectConstants.ProtocolMinPlayers or > LanConnectConstants.ProtocolMaxPlayers)
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "protocol_profile_unsupported",
                $"Room maxPlayers {MaxPlayers} is outside 2..8.");
        }

        Offer.Validate();
        if (Profile == LanConnectProtocolProfile.TailV1
            && !Offer.Supports(LanConnectConstants.TailLanProtocolVersion))
        {
            throw LanConnectProtocolFailureMapper.FromLocalException(
                "lan_protocol_version_mismatch",
                "Tail runtime is unavailable on this game version.");
        }

        if (Profile == LanConnectProtocolProfile.Compat4x5V1 && Offer.RitsuLibPresent)
        {
            throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibNotAllowedInCompat());
        }

        // native_bus_v1：tail 房间不再要求 sidecar 可用（0.5.18 事故状态照常建房）。
        return this;
    }

    public static LanConnectCreateRoomIntent CreateDefaultCompat(int maxPlayers) =>
        new LanConnectCreateRoomIntent(
            LanConnectProtocolProfile.Compat4x5V1,
            maxPlayers,
            LanConnectProtocolOffer.CreateCurrent());

    public static LanConnectCreateRoomIntent CreateTailV1(int maxPlayers) =>
        new LanConnectCreateRoomIntent(
            LanConnectProtocolProfile.TailV1,
            maxPlayers,
            LanConnectProtocolOffer.CreateCurrent());
}
