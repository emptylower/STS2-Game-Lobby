using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectProtocolProfiles
{
    public const string Legacy4p = "legacy_4p";
    public const string Extended8p = "extended_8p";

    private static readonly Version LegacyCompatibleMaxVersion = new(0, 2, 2, 0);
    private static readonly string[] RmpModNames =
    {
        "RemoveMultiplayerPlayerLimit"
    };

    private static string _activeProfile = Extended8p;
    private static int _activeMaxPlayers = LanConnectConstants.DefaultMaxPlayers;

    public static string DefaultProfile => Extended8p;

    public static string Normalize(string? value)
    {
        return string.Equals(value?.Trim(), Legacy4p, StringComparison.OrdinalIgnoreCase)
            ? Legacy4p
            : Extended8p;
    }

    public static bool IsLegacy(string? value)
    {
        return string.Equals(Normalize(value), Legacy4p, StringComparison.Ordinal);
    }

    public static string DetermineProfileForMaxPlayers(int maxPlayers)
    {
        return maxPlayers <= LanConnectConstants.MinMaxPlayers
            ? Legacy4p
            : Extended8p;
    }

    public static string ResolvePublishedProfile(
        string? requestedProfile,
        int maxPlayers,
        string? modVersion,
        IEnumerable<string>? modList)
    {
        if (!string.IsNullOrWhiteSpace(requestedProfile))
        {
            return Normalize(requestedProfile);
        }

        if (maxPlayers == LanConnectConstants.MinMaxPlayers
            && IsLegacyCompatibleModVersion(modVersion)
            && !AdvertisesRmpMod(modList))
        {
            return Legacy4p;
        }

        return Extended8p;
    }

    public static bool AdvertisesRmpMod(IEnumerable<string>? modList)
    {
        if (modList == null)
        {
            return false;
        }

        return modList.Any(value =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            return RmpModNames.Any(name =>
                string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase));
        });
    }

    public static string GetActiveProfile()
    {
        return _activeProfile;
    }

    public static int GetActiveMaxPlayers()
    {
        return _activeMaxPlayers;
    }

    public static void SetActiveProfile(string? value, string source)
    {
        SetActiveProfile(value, null, source);
    }

    public static void SetActiveProfile(string? value, int? maxPlayers, string source)
    {
        string normalized = Normalize(value);
        int resolvedMaxPlayers = ResolveActiveMaxPlayers(normalized, maxPlayers);
        if (string.Equals(_activeProfile, normalized, StringComparison.Ordinal)
            && _activeMaxPlayers == resolvedMaxPlayers)
        {
            return;
        }

        _activeProfile = normalized;
        _activeMaxPlayers = resolvedMaxPlayers;
        Log.Info(
            $"sts2_lan_connect protocol profile -> {_activeProfile} maxPlayers={_activeMaxPlayers} " +
            $"slotBits={GetActiveSlotIdBitWidth()} lobbyListBits={GetActiveLobbyListBitWidth()} source={source}");
    }

    public static void ResetActiveProfile(string source)
    {
        SetActiveProfile(DefaultProfile, LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers(), source);
    }

    public static int GetActiveSlotIdBitWidth()
    {
        return IsLegacy(_activeProfile)
            ? LanConnectConstants.Legacy4pSlotIdBits
            : LanConnectConstants.ExtendedSlotIdBits;
    }

    public static int GetActiveLobbyListBitWidth()
    {
        return IsLegacy(_activeProfile)
            ? LanConnectConstants.VanillaLobbyListBits
            : LanConnectConstants.ExtendedLobbyListBits;
    }

    private static bool IsLegacyCompatibleModVersion(string? modVersion)
    {
        if (string.IsNullOrWhiteSpace(modVersion) || !Version.TryParse(modVersion.Trim(), out Version? parsed))
        {
            return false;
        }

        return parsed <= LegacyCompatibleMaxVersion;
    }

    private static int ResolveActiveMaxPlayers(string normalizedProfile, int? maxPlayers)
    {
        int fallback = maxPlayers.HasValue
            ? Math.Clamp(maxPlayers.Value, LanConnectConstants.MinMaxPlayers, LanConnectConstants.MaxMaxPlayers)
            : LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();

        if (string.Equals(normalizedProfile, Legacy4p, StringComparison.Ordinal))
        {
            return LanConnectConstants.MinMaxPlayers;
        }

        return Math.Clamp(fallback, LanConnectConstants.MinMaxPlayers, LanConnectConstants.MaxMaxPlayers);
    }
}
