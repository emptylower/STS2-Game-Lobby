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
        if (Profile == LanConnectProtocolProfile.Compat4x5V1 && Offer.RitsuLibPresent)
        {
            throw new LanConnectProtocolException(LanConnectProtocolFailure.RitsuLibNotAllowedInCompat());
        }

        return this;
    }

    public static LanConnectCreateRoomIntent CreateDefaultCompat(int maxPlayers) =>
        new LanConnectCreateRoomIntent(
            LanConnectProtocolProfile.Compat4x5V1,
            maxPlayers,
            LanConnectProtocolOffer.CreateCurrent());
}
