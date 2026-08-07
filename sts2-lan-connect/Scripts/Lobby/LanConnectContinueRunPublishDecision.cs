using System;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectContinueRunPublishDecisionKind
{
    Publish,
    SkipLanOrigin,
    Prompt
}

internal static class LanConnectContinueRunPublishDecision
{
    public static LanConnectContinueRunPublishDecisionKind Decide(string? persistedHostChannel, int schemaVersion)
    {
        if (!LanConnectHostChannels.IsValid(persistedHostChannel))
        {
            return LanConnectContinueRunPublishDecisionKind.Prompt;
        }

        string resolvedHostChannel = persistedHostChannel!.Trim().ToLowerInvariant();
        if (string.Equals(resolvedHostChannel, LanConnectHostChannels.Lan, StringComparison.Ordinal))
        {
            return schemaVersion < LanConnectSavedRoomBinding.CurrentSchemaVersion
                ? LanConnectContinueRunPublishDecisionKind.Prompt
                : LanConnectContinueRunPublishDecisionKind.SkipLanOrigin;
        }

        return LanConnectContinueRunPublishDecisionKind.Publish;
    }

    public static string ToLogToken(LanConnectContinueRunPublishDecisionKind decision)
    {
        return decision switch
        {
            LanConnectContinueRunPublishDecisionKind.SkipLanOrigin => "skip_lan_origin",
            LanConnectContinueRunPublishDecisionKind.Prompt => "prompt",
            _ => "publish"
        };
    }
}
