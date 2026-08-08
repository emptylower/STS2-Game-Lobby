namespace Sts2LanConnect.Scripts;

internal enum LanConnectWireCacheHandshakeDecisionKind
{
    Match,
    Mismatch,
    LocalUnavailable,
    RemoteAbsent
}

internal sealed record LanConnectWireCacheHandshakeDecision(
    LanConnectWireCacheHandshakeDecisionKind Kind,
    bool IsAllowed,
    bool ShouldWarn,
    LanConnectWireCacheHandshakeToken? LocalToken,
    LanConnectWireCacheHandshakeToken? RemoteToken,
    string Detail)
{
    internal static LanConnectWireCacheHandshakeDecision Evaluate(
        LanConnectWireCacheCaptureResult localCapture,
        LanConnectWireCacheHandshakeTokenParseResult remoteParse,
        bool relaxedCompatibility)
    {
        ArgumentNullException.ThrowIfNull(localCapture);
        ArgumentNullException.ThrowIfNull(remoteParse);

        // A wire mismatch is fatal under every profile. Keep the parameter explicit so
        // callers cannot accidentally apply relaxed compatibility around this policy.
        _ = relaxedCompatibility;

        if (!localCapture.IsAvailable)
        {
            return new LanConnectWireCacheHandshakeDecision(
                LanConnectWireCacheHandshakeDecisionKind.LocalUnavailable,
                IsAllowed: true,
                ShouldWarn: true,
                LocalToken: null,
                RemoteToken: remoteParse.Token,
                localCapture.FailureReason ?? "local wire cache signature unavailable");
        }

        LanConnectWireCacheHandshakeToken localToken =
            LanConnectWireCacheHandshakeToken.FromSnapshot(localCapture.Snapshot!);
        if (remoteParse.Status != LanConnectWireCacheHandshakeTokenStatus.Valid ||
            remoteParse.Token == null)
        {
            return new LanConnectWireCacheHandshakeDecision(
                LanConnectWireCacheHandshakeDecisionKind.RemoteAbsent,
                IsAllowed: true,
                ShouldWarn: true,
                localToken,
                RemoteToken: null,
                $"remote sentinel status={remoteParse.Status}");
        }

        LanConnectWireCacheHandshakeToken remoteToken = remoteParse.Token;
        if (string.Equals(localToken.Signature, remoteToken.Signature, StringComparison.Ordinal))
        {
            return new LanConnectWireCacheHandshakeDecision(
                LanConnectWireCacheHandshakeDecisionKind.Match,
                IsAllowed: true,
                ShouldWarn: false,
                localToken,
                remoteToken,
                "wire cache signatures match");
        }

        return new LanConnectWireCacheHandshakeDecision(
            LanConnectWireCacheHandshakeDecisionKind.Mismatch,
            IsAllowed: false,
            ShouldWarn: false,
            localToken,
            remoteToken,
            BuildMismatchMessage(localToken, remoteToken));
    }

    private static string BuildMismatchMessage(
        LanConnectWireCacheHandshakeToken localToken,
        LanConnectWireCacheHandshakeToken remoteToken) =>
        "联机内容或 Mod 数据表不一致，网络编码不兼容，无法安全加入。" +
        "请双方对齐 Mod 列表和版本后重试。\n" +
        $"当前签名：{localToken.Signature}\n" +
        $"房主签名：{remoteToken.Signature}\n" +
        $"当前位宽：{localToken.FormatWidths()}\n" +
        $"房主位宽：{remoteToken.FormatWidths()}";
}
