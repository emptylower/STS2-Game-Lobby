namespace Sts2LanConnect.Scripts;

internal enum LanConnectInviteEntryAction
{
    ShowServerPicker,
    ShowInvite
}

internal readonly record struct LanConnectInviteEntryDecision(
    LanConnectInviteEntryAction Action,
    LanConnectInvitePayload? Payload);

internal static class LanConnectInviteEntryDecider
{
    public static LanConnectInviteEntryDecision Decide(string? clipboardText)
    {
        return LanConnectInviteCode.TryDecode(clipboardText, out LanConnectInvitePayload? payload) && payload != null
            ? new LanConnectInviteEntryDecision(LanConnectInviteEntryAction.ShowInvite, payload)
            : new LanConnectInviteEntryDecision(LanConnectInviteEntryAction.ShowServerPicker, null);
    }
}
