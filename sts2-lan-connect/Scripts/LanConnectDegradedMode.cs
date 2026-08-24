using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

// Degraded mode: protocol patch application failed (typically a foreign patch conflict),
// so the mod keeps loading and installs the lobby UI, but every host/join funnel must
// refuse multiplayer. Vanilla single-player is unaffected.
internal static class LanConnectDegradedMode
{
    public const string ProtocolPatchConflictCode = "protocol_patch_conflict";

    private static string? _reasonCode;
    private static string? _exceptionFingerprint;
    private static bool _lobbyEntryNoticePending = true;

    // Test seam: xUnit hosts cannot enter Godot's GD-based logging.
    internal static Action<string> LogErrorSink = static message => Log.Error(message);

    public static bool IsActive => _reasonCode != null;
    public static string? ReasonCode => _reasonCode;
    public static string? ExceptionFingerprint => _exceptionFingerprint;

    public static void Enter(string reasonCode, string? exceptionFingerprint)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            reasonCode = ProtocolPatchConflictCode;
        }

        _reasonCode = reasonCode;
        _exceptionFingerprint = exceptionFingerprint;
        _lobbyEntryNoticePending = true;
        LogErrorSink(
            "sts2_lan_connect DEGRADED MODE: 联机功能已停用（联机协议补丁未能完整安装），" +
            $"单机不受影响。reason={_reasonCode} fingerprint={_exceptionFingerprint ?? "none"}");
    }

    // Every host/join funnel must check this before doing any work. The failure code is
    // mapped to user-facing text by LanConnectProtocolUiMessages.
    public static LanConnectProtocolFailure? CreateBlockingFailure() =>
        IsActive
            ? new LanConnectProtocolFailure(
                _reasonCode!,
                Detail: $"fingerprint={_exceptionFingerprint ?? "none"}").Validate()
            : null;

    // The lobby entry shows one proactive native popup per session; repeated host/join
    // attempts keep presenting the blocking failure on every click.
    public static bool TryConsumeLobbyEntryNotice(out LanConnectProtocolFailure failure)
    {
        if (IsActive && _lobbyEntryNoticePending)
        {
            _lobbyEntryNoticePending = false;
            failure = CreateBlockingFailure()!;
            return true;
        }

        failure = null!;
        return false;
    }

    internal static void ResetForTesting()
    {
        _reasonCode = null;
        _exceptionFingerprint = null;
        _lobbyEntryNoticePending = true;
        LogErrorSink = static message => Log.Error(message);
    }
}
