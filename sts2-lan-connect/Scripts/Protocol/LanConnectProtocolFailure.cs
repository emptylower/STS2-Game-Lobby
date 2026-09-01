using System.Text;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectProtocolFailure(
    string Code,
    string? RequiredClientVersion = null,
    bool? RequiredRitsuLibPresent = null,
    string? Detail = null)
{
    public const int MaxCodeBytes = 64;
    public const int MaxVersionBytes = 32;
    public const int MaxDetailBytes = 512;

    public LanConnectProtocolFailure Validate()
    {
        ValidateBounded(Code, nameof(Code), MaxCodeBytes, required: true);
        ValidateBounded(RequiredClientVersion, nameof(RequiredClientVersion), MaxVersionBytes, required: false);
        ValidateBounded(Detail, nameof(Detail), MaxDetailBytes, required: false);
        return this;
    }

    public static LanConnectProtocolFailure ClientUpdateRequired(string requiredVersion, string? detail = null) =>
        new LanConnectProtocolFailure("client_update_required", requiredVersion, null, detail).Validate();

    public static LanConnectProtocolFailure RitsuLibPresenceMismatch(bool requiredPresent, string? detail = null) =>
        new LanConnectProtocolFailure("ritsulib_presence_mismatch", null, requiredPresent, detail).Validate();

    public static LanConnectProtocolFailure RitsuLibNotAllowedInCompat(string? detail = null) =>
        new LanConnectProtocolFailure("ritsulib_not_allowed_in_compat_mode", null, false, detail).Validate();

    private static void ValidateBounded(string? value, string name, int maxBytes, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(value))
            || (value is not null && Encoding.UTF8.GetByteCount(value) > maxBytes))
        {
            throw new ArgumentException($"{name} must be {(required ? "non-empty and " : string.Empty)}at most {maxBytes} UTF-8 bytes.", name);
        }
    }
}
