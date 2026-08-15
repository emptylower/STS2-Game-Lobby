namespace Sts2LanConnect.Scripts;

// Legacy strings remain at the unversioned API boundary. Runtime wire decisions come
// exclusively from the immutable session selection.
internal static class LanConnectProtocolProfiles
{
    public const string Legacy4p = "legacy_4p";
    public const string Extended8p = "extended_8p";

    public static string GetActiveProfile() =>
        LanConnectSessionProtocolState.Shared.Current.Selection?.Profile.ToCanonical() ?? "none";

    public static int GetActiveMaxPlayers() =>
        LanConnectSessionProtocolState.Shared.Current.Selection?.MaxPlayers
        ?? LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();

    public static int GetActiveSlotIdBitWidth() => RequireSelection().Profile switch
    {
        LanConnectProtocolProfile.Compat4x5V1 => LanConnectConstants.ExtendedSlotIdBits,
        LanConnectProtocolProfile.TailV1 => LanConnectConstants.VanillaSlotIdBits,
        _ => throw LanConnectProtocolFailureMapper.FromLocalException("protocol_profile_unsupported")
    };

    public static int GetActiveLobbyListBitWidth() => RequireSelection().Profile switch
    {
        LanConnectProtocolProfile.Compat4x5V1 => LanConnectConstants.ExtendedLobbyListBits,
        LanConnectProtocolProfile.TailV1 => LanConnectConstants.VanillaLobbyListBits,
        _ => throw LanConnectProtocolFailureMapper.FromLocalException("protocol_profile_unsupported")
    };

    private static LanConnectProtocolSelection RequireSelection() =>
        LanConnectSessionProtocolState.Shared.Current.Selection
        ?? throw LanConnectProtocolFailureMapper.FromLocalException(
            "protocol_selection_missing",
            "Protocol serialization started without an active session lease.");
}
