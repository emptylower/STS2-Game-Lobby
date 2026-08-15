using System.Text;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectClientApiGeneration
{
    Compat0305,
    Canonical06Plus
}

internal sealed record LanConnectClientVersion(
    int Major,
    int Minor,
    int Patch,
    string? Prerelease,
    string Canonical,
    LanConnectClientApiGeneration Generation)
{
    private const int MaxVersionBytes = 32;

    public static bool TryParseSupported(string? value, out LanConnectClientVersion? version)
    {
        version = null;
        if (!TryParse(value, out int major, out int minor, out int patch, out string? prerelease, out string canonical))
        {
            return false;
        }

        LanConnectClientApiGeneration generation;
        if (major == 0 && minor is >= 3 and <= 5)
        {
            generation = LanConnectClientApiGeneration.Compat0305;
        }
        else if (major > 0 || (major == 0 && minor >= 6))
        {
            generation = LanConnectClientApiGeneration.Canonical06Plus;
        }
        else
        {
            return false;
        }

        version = new LanConnectClientVersion(major, minor, patch, prerelease, canonical, generation);
        return true;
    }

    public static LanConnectClientVersion ParseSupported(string? value)
    {
        if (TryParseSupported(value, out LanConnectClientVersion? version))
        {
            return version!;
        }

        throw new LanConnectProtocolException(
            LanConnectProtocolFailure.ClientUpdateRequired("0.3.0", $"Unsupported client version '{value ?? "<missing>"}'."));
    }

    private static bool TryParse(
        string? value,
        out int major,
        out int minor,
        out int patch,
        out string? prerelease,
        out string canonical)
    {
        major = minor = patch = 0;
        prerelease = null;
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (Encoding.UTF8.GetByteCount(trimmed) > MaxVersionBytes || trimmed.Contains('+', StringComparison.Ordinal))
        {
            return false;
        }

        int dash = trimmed.IndexOf('-');
        string core = dash < 0 ? trimmed : trimmed[..dash];
        prerelease = dash < 0 ? null : trimmed[(dash + 1)..];
        if (prerelease is not null && !IsValidPrerelease(prerelease))
        {
            return false;
        }

        string[] parts = core.Split('.');
        if (parts.Length != 3
            || !TryParseNumericPart(parts[0], out major)
            || !TryParseNumericPart(parts[1], out minor)
            || !TryParseNumericPart(parts[2], out patch))
        {
            return false;
        }

        canonical = $"{major}.{minor}.{patch}" + (prerelease is null ? string.Empty : $"-{prerelease}");
        return string.Equals(trimmed, canonical, StringComparison.Ordinal);
    }

    private static bool TryParseNumericPart(string part, out int value)
    {
        value = 0;
        return part.Length > 0
               && (part.Length == 1 || part[0] != '0')
               && part.All(char.IsAsciiDigit)
               && int.TryParse(part, out value);
    }

    private static bool IsValidPrerelease(string value) =>
        value.Length > 0
        && value.Split('.').All(static identifier =>
            identifier.Length > 0
            && identifier.All(static character => char.IsAsciiLetterOrDigit(character) || character == '-'));
}
