using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Accessibility;

public sealed class LanConnectInviteEntryDecisionTests
{
    [Fact]
    public void Decide_returns_show_invite_when_clipboard_has_valid_invite()
    {
        string code = LanConnectInviteCode.Encode("http://127.0.0.1:8787", "room-123", password: null);

        LanConnectInviteEntryDecision decision = LanConnectInviteEntryDecider.Decide(code);

        Assert.Equal(LanConnectInviteEntryAction.ShowInvite, decision.Action);
        Assert.NotNull(decision.Payload);
        Assert.Equal("http://127.0.0.1:8787", decision.Payload!.S);
        Assert.Equal("room-123", decision.Payload.R);
    }

    [Fact]
    public void Decide_returns_show_server_picker_when_clipboard_is_not_invite()
    {
        LanConnectInviteEntryDecision decision = LanConnectInviteEntryDecider.Decide("not an invite");

        Assert.Equal(LanConnectInviteEntryAction.ShowServerPicker, decision.Action);
        Assert.Null(decision.Payload);
    }
}
