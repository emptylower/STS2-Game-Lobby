using System.Globalization;
using System.Text.RegularExpressions;

namespace Sts2LanConnect.Scripts;

internal enum ServerVersionSource { Unknown, Inferred, Reported }

internal enum ServerProbeState { Pending, Reachable, Unreachable }

internal sealed record ServerVersionInfo(
    ServerVersionSource Source,
    int Major,
    int Minor,
    string Display)
{
    public static readonly ServerVersionInfo Unknown = new(ServerVersionSource.Unknown, 0, 0, "未知");

    public bool IsKnown => Source != ServerVersionSource.Unknown;

    // True only when the version is known and below the 0.6 major.minor tier
    // (0.6.0 is the first service that speaks protocolSelection). Unknown is
    // never flagged — we cannot prove the server is old.
    public bool IsTooOldForThisClient => IsKnown && (Major, Minor).CompareTo((0, 6)) < 0;
}

// Pure parsing rules for the server-version tier used by the picker ranking.
// Mirrors lobby-service's probe shape; see the v0.6.2-alpha.2 plan §4.3.
internal static class LanConnectServerVersionResolver
{
    private static readonly Regex ReportedVersionPattern =
        new(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", RegexOptions.Compiled);

    public static ServerVersionInfo? FromReported(string? serviceVersion)
    {
        if (string.IsNullOrWhiteSpace(serviceVersion))
        {
            return null;
        }

        string trimmed = serviceVersion.Trim();
        if (trimmed.Length is < 1 or > 64 || !ReportedVersionPattern.IsMatch(trimmed))
        {
            return null;
        }

        string[] segments = trimmed.Split('.');
        if (!int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
        {
            return null;
        }

        return new ServerVersionInfo(ServerVersionSource.Reported, major, minor, trimmed);
    }

    public static ServerVersionInfo FromProbe(LobbyProbeResponse? probe)
    {
        if (probe == null || probe.Ok != true)
        {
            return ServerVersionInfo.Unknown;
        }

        // System.Text.Json writes an explicit JSON null into the property even
        // though the declared type is non-nullable with a default instance, so
        // a runtime null check is required (missing field keeps `= new()`).
        if (probe.Capabilities == null)
        {
            return ServerVersionInfo.Unknown;
        }

        ServerVersionInfo? reported = FromReported(probe.Capabilities.ServiceVersion);
        if (reported != null)
        {
            return reported;
        }

        if (probe.Capabilities.DualProtocolApiVersion >= 1)
        {
            return new ServerVersionInfo(ServerVersionSource.Inferred, 0, 6, "0.6.x（推断）");
        }

        if (probe.Capabilities.ModSyncProtocolVersion >= 1 || probe.Capabilities.ServerChatVersion >= 1)
        {
            return new ServerVersionInfo(ServerVersionSource.Inferred, 0, 5, "0.5.x（推断）");
        }

        return new ServerVersionInfo(ServerVersionSource.Inferred, 0, 4, "0.4.x 或更早（推断）");
    }
}
