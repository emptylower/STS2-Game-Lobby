using System;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectContinueRunPublishDecisionKind
{
    Publish,
    SkipLanOrigin
}

internal static class LanConnectContinueRunPublishDecision
{
    public static LanConnectContinueRunPublishDecisionKind Decide(string effectiveHostChannel)
    {
        return string.Equals(effectiveHostChannel, LanConnectHostChannels.Lan, StringComparison.Ordinal)
            ? LanConnectContinueRunPublishDecisionKind.SkipLanOrigin
            : LanConnectContinueRunPublishDecisionKind.Publish;
    }

    public static string ToLogToken(LanConnectContinueRunPublishDecisionKind decision)
    {
        return decision == LanConnectContinueRunPublishDecisionKind.SkipLanOrigin
            ? "skip_lan_origin"
            : "publish";
    }
}
