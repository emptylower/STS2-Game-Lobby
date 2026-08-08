using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectContinueRunPublishDecisionTests
{
    [Theory]
    [InlineData("lobby")]
    [InlineData("LOBBY")]
    [InlineData(" lobby ")]
    public void Lobby_bindings_publish_without_prompt(string hostChannel)
    {
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Publish,
            LanConnectContinueRunPublishDecision.Decide(hostChannel, schemaVersion: 0));
    }

    [Fact]
    public void Current_lan_binding_skips_without_prompt()
    {
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.SkipLanOrigin,
            LanConnectContinueRunPublishDecision.Decide(
                LanConnectHostChannels.Lan,
                LanConnectSavedRoomBinding.CurrentSchemaVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("steam")]
    public void Missing_or_unknown_binding_requires_prompt(string? hostChannel)
    {
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Prompt,
            LanConnectContinueRunPublishDecision.Decide(
                hostChannel,
                LanConnectSavedRoomBinding.CurrentSchemaVersion));
    }

    [Fact]
    public void Legacy_lan_binding_requires_one_time_migration_prompt()
    {
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Prompt,
            LanConnectContinueRunPublishDecision.Decide(LanConnectHostChannels.Lan, schemaVersion: 0));
    }

    [Theory]
    [InlineData("lobby", "Publish")]
    [InlineData("lan", "SkipLanOrigin")]
    public void Persisted_prompt_choice_is_not_prompted_again(
        string selectedHostChannel,
        string expected)
    {
        Assert.Equal(
            expected,
            LanConnectContinueRunPublishDecision.Decide(
                selectedHostChannel,
                LanConnectSavedRoomBinding.CurrentSchemaVersion).ToString());
    }
}
