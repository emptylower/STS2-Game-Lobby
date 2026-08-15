namespace Sts2LanConnect.Scripts;

internal static class LanConnectProtocolFailureMapper
{
    private static readonly string[] TailCodes =
    [
        "client_update_required",
        "protocol_profile_unsupported",
        "ritsulib_not_allowed_in_compat_mode",
        "ritsulib_presence_mismatch",
        "game_version_mismatch",
        "wire_cache_mismatch",
        "lan_tail_required",
        "lan_tail_malformed",
        "lan_protocol_version_mismatch",
        "ritsulib_sidecar_unavailable"
    ];

    public static LanConnectProtocolFailure FromService(LobbyServiceException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string canonicalCode = exception.Code switch
        {
            "wire_cache_signature_mismatch" => "wire_cache_mismatch",
            _ => exception.Code
        };
        return new LanConnectProtocolFailure(
            canonicalCode,
            exception.Details?.RequiredClientVersion,
            exception.Details?.RequiredRitsuLibPresent,
            exception.Details?.Detail ?? exception.Message).Validate();
    }

    public static LanConnectProtocolFailure FromTail(
        int reasonCode,
        string? requiredClientVersion = null,
        bool? requiredRitsuLibPresent = null,
        string? detail = null)
    {
        string code = reasonCode is >= 1 and <= 10
            ? TailCodes[reasonCode - 1]
            : $"unknown_tail_rejection_{reasonCode}";
        return new LanConnectProtocolFailure(code, requiredClientVersion, requiredRitsuLibPresent, detail).Validate();
    }

    public static LanConnectProtocolFailure FromLocal(
        string code,
        string? detail = null,
        string? requiredClientVersion = null,
        bool? requiredRitsuLibPresent = null) =>
        new LanConnectProtocolFailure(code, requiredClientVersion, requiredRitsuLibPresent, detail).Validate();

    public static LanConnectProtocolException FromLocalException(
        string code,
        string? detail = null,
        string? requiredClientVersion = null,
        bool? requiredRitsuLibPresent = null) =>
        new(FromLocal(code, detail, requiredClientVersion, requiredRitsuLibPresent));

    public static bool IsKnownProtocolServiceCode(string? code) =>
        code is not null
        && (TailCodes.Contains(code, StringComparer.Ordinal)
            || code is "capability_digest_mismatch" or "wire_cache_signature_mismatch");
}
