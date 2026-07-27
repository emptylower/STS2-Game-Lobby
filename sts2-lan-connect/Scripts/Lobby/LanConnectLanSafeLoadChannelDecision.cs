using System;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectLanSafeLoadChannelActionKind
{
    KeepBinding,
    MigrateToLan
}

internal static class LanConnectLanSafeLoadChannelDecision
{
    /// <summary>
    /// Decides what the LAN safe-load path should do with the save's persisted host channel.
    /// Lobby-bound saves must keep their binding so the continue-run auto-publisher can
    /// republish the lobby room once the load screen is ready; migrating them to lan would
    /// silently break lobby resume (skip_lan_origin). Everything else (lan / missing /
    /// unknown) migrates to lan as before.
    /// </summary>
    public static LanConnectLanSafeLoadChannelActionKind Decide(string? persistedHostChannel)
    {
        return string.Equals(persistedHostChannel?.Trim(), LanConnectHostChannels.Lobby, StringComparison.OrdinalIgnoreCase)
            ? LanConnectLanSafeLoadChannelActionKind.KeepBinding
            : LanConnectLanSafeLoadChannelActionKind.MigrateToLan;
    }
}
