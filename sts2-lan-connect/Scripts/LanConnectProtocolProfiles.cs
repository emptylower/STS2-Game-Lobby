using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

// Legacy strings remain at the unversioned API boundary. Runtime wire decisions come
// exclusively from the immutable session selection.
internal static class LanConnectProtocolProfiles
{
    public const string Legacy4p = "legacy_4p";
    public const string Extended8p = "extended_8p";

    public static string DefaultProfile => Extended8p;

    public static string Normalize(string? value) => value switch
    {
        Legacy4p => Legacy4p,
        Extended8p => Extended8p,
        _ => Extended8p
    };

    public static bool IsLegacy(string? value) =>
        string.Equals(value?.Trim(), Legacy4p, StringComparison.OrdinalIgnoreCase);

    [Obsolete("Runtime protocol selection must come from LanConnectCreateRoomIntent or a server selection.")]
    public static string DetermineProfileForMaxPlayers(int _) => Extended8p;

    public static string ResolvePublishedProfile(
        string? requestedProfile,
        int _,
        string? __,
        IEnumerable<string>? ___) => Normalize(requestedProfile);

    public static bool AdvertisesRmpMod(IEnumerable<string>? modList) =>
        modList?.Any(static value =>
            string.Equals(value?.Trim(), "RemoveMultiplayerPlayerLimit", StringComparison.OrdinalIgnoreCase)) == true;

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
