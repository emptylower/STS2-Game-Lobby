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

    /// <summary>
    /// semver `>=` 比较（含预发布规则：预发布版本小于同号正式版；数字标识按数值、
    /// 字母标识按 ASCII 比较；无预发布标签大于有预发布标签）。与服务端 TS 实现共用向量。
    /// </summary>
    public static bool IsAtLeast(string candidate, string required)
    {
        LanConnectClientVersion candidateVersion = ParseSupported(candidate);
        LanConnectClientVersion requiredVersion = ParseSupported(required);
        int comparison = Compare(candidateVersion, requiredVersion);
        return comparison >= 0;
    }

    public static int Compare(LanConnectClientVersion left, LanConnectClientVersion right)
    {
        if (left.Major != right.Major)
        {
            return left.Major.CompareTo(right.Major);
        }

        if (left.Minor != right.Minor)
        {
            return left.Minor.CompareTo(right.Minor);
        }

        if (left.Patch != right.Patch)
        {
            return left.Patch.CompareTo(right.Patch);
        }

        return ComparePrerelease(left.Prerelease, right.Prerelease);
    }

    private static int ComparePrerelease(string? left, string? right)
    {
        if (left == null && right == null)
        {
            return 0;
        }

        // 正式版大于任何预发布版本。
        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        string[] leftIdentifiers = left.Split('.');
        string[] rightIdentifiers = right.Split('.');
        int count = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);
        for (int index = 0; index < count; index++)
        {
            string leftIdentifier = leftIdentifiers[index];
            string rightIdentifier = rightIdentifiers[index];
            bool leftNumeric = leftIdentifier.All(char.IsAsciiDigit);
            bool rightNumeric = rightIdentifier.All(char.IsAsciiDigit);
            int comparison = (leftNumeric, rightNumeric) switch
            {
                (true, true) => long.Parse(leftIdentifier).CompareTo(long.Parse(rightIdentifier)),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftIdentifier, rightIdentifier)
            };
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
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
