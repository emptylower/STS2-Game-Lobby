using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectSaveEventGuardTests
{
    [Fact]
    public void Successful_body_returns_true()
    {
        bool ran = false;

        bool result = LanConnectSaveEventGuard.Run(
            "save_event",
            () => ran = true,
            _ => { });

        Assert.True(result);
        Assert.True(ran);
    }

    [Fact]
    public void Protocol_exception_returns_false_and_logs_failure_code()
    {
        var logged = new System.Collections.Generic.List<string>();

        bool result = LanConnectSaveEventGuard.Run(
            "save_event",
            () => throw LanConnectProtocolFailureMapper.FromLocalException(
                "protocol_profile_unsupported",
                "Unknown protocol carrier enum value 3."),
            logged.Add);

        Assert.False(result);
        string entry = Assert.Single(logged);
        Assert.Contains("sts2_lan_connect save_binding: persist failed source=save_event", entry);
        Assert.Contains("failureCode=protocol_profile_unsupported", entry);
        Assert.Contains("LanConnectProtocolException", entry);
    }

    [Fact]
    public void Plain_exception_returns_false_and_logs_type_and_message()
    {
        var logged = new System.Collections.Generic.List<string>();

        bool result = LanConnectSaveEventGuard.Run(
            "save_event",
            () => throw new InvalidOperationException("boom"),
            logged.Add);

        Assert.False(result);
        string entry = Assert.Single(logged);
        Assert.Contains("error=InvalidOperationException: boom", entry);
        Assert.DoesNotContain("failureCode=", entry);
    }
}
