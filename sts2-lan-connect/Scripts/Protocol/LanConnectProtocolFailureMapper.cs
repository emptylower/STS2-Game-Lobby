namespace Sts2LanConnect.Scripts;

internal static class LanConnectProtocolFailureMapper
{
    // 与 LanConnectRejectionCodec 共用同一张 tail 拒绝码表，避免两表漂移。
    private static readonly string[] TailCodes = LanConnectRejectionCodec.ReasonCodes;

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
        string code = reasonCode >= 1 && reasonCode <= TailCodes.Length
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
