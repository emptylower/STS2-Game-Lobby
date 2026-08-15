using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectProtocolUiMessagesTests
{
    [Fact]
    public void Ritsu_sidecar_descriptor_contract_is_stable()
    {
        Assert.Equal("sts2_lan_connect", LanConnectRitsuLibSidecarCarrier.ModuleId);
        Assert.Equal("protocol_v1", LanConnectRitsuLibSidecarCarrier.MessageKey);
        Assert.Equal("StableSync", LanConnectRitsuLibSidecarCarrier.DeliverySemantics);
        Assert.True(LanConnectRitsuLibSidecarCarrier.IsRequired);
    }

    [Theory]
    [InlineData("client_update_required")]
    [InlineData("protocol_profile_unsupported")]
    [InlineData("ritsulib_not_allowed_in_compat_mode")]
    [InlineData("ritsulib_presence_mismatch")]
    [InlineData("ritsulib_sidecar_unavailable")]
    [InlineData("game_version_mismatch")]
    [InlineData("wire_cache_mismatch")]
    [InlineData("lan_protocol_version_mismatch")]
    [InlineData("lan_tail_required")]
    [InlineData("lan_tail_malformed")]
    public void Known_protocol_failures_have_stable_user_messages(string code)
    {
        string message = LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure(
            code,
            code == "client_update_required" ? "0.6.0-alpha.1" : null,
            code == "ritsulib_presence_mismatch"));

        Assert.NotEmpty(message);
        Assert.DoesNotContain(code, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_protocol_failure_preserves_code_for_diagnostics()
    {
        string message = LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure("future_code"));

        Assert.Contains("future_code", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_protocol_descriptions_match_the_alpha_ui_contract()
    {
        Assert.Equal(
            "支持 0.3-0.5，不支持 RitsuLib",
            LanConnectLobbyOverlay.CreateProtocolDescriptionForTestsStatic(300));
        Assert.Equal(
            "仅支持 0.6+；RitsuLib 状态必须一致",
            LanConnectLobbyOverlay.CreateProtocolDescriptionForTestsStatic(301));
    }

    [Fact]
    public void Player_count_does_not_select_the_create_protocol()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.1", false, false);

        Assert.Equal(
            LanConnectProtocolProfile.Compat4x5V1,
            LanConnectLobbyOverlay.BuildCreateRoomIntentForTests(300, 2, offer).Validate().Profile);
        Assert.Equal(
            LanConnectProtocolProfile.Compat4x5V1,
            LanConnectLobbyOverlay.BuildCreateRoomIntentForTests(300, 8, offer).Validate().Profile);
        Assert.Equal(
            LanConnectProtocolProfile.TailV1,
            LanConnectLobbyOverlay.BuildCreateRoomIntentForTests(301, 2, offer).Validate().Profile);
        Assert.Equal(
            LanConnectProtocolProfile.TailV1,
            LanConnectLobbyOverlay.BuildCreateRoomIntentForTests(301, 8, offer).Validate().Profile);
    }

    [Fact]
    public void Tail_with_Ritsu_fails_closed_when_public_sidecar_is_unavailable()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.1", true, false);

        LanConnectProtocolException exception = Assert.Throws<LanConnectProtocolException>(() =>
            LanConnectLobbyOverlay.BuildCreateRoomIntentForTests(301, 8, offer).Validate());

        Assert.Equal("ritsulib_sidecar_unavailable", exception.Failure.Code);
    }

    [Fact]
    public void Create_options_follow_local_Ritsu_presence_and_carrier_readiness()
    {
        LanConnectProtocolOffer noRitsu = new(1, 1, "0.6.0-alpha.1", false, false);
        LanConnectProtocolOffer ritsuUnavailable = new(1, 1, "0.6.0-alpha.1", true, false);
        LanConnectProtocolOffer ritsuReady = new(1, 1, "0.6.0-alpha.1", true, true);

        Assert.True(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(301, noRitsu, true));
        Assert.False(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(301, noRitsu, false));
        Assert.False(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(301, ritsuUnavailable, true));
        Assert.True(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(301, ritsuReady, false));
        Assert.False(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(300, ritsuReady, false));
        Assert.True(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(300, noRitsu, false));
    }

    [Fact]
    public void Create_dialog_defaults_to_the_first_protocol_supported_by_the_local_runtime()
    {
        LanConnectProtocolOffer noRitsu = new(1, 1, "0.6.0-alpha.1", false, false);
        LanConnectProtocolOffer ritsuUnavailable = new(1, 1, "0.6.0-alpha.1", true, false);
        LanConnectProtocolOffer ritsuReady = new(1, 1, "0.6.0-alpha.1", true, true);

        Assert.Equal(300, LanConnectLobbyOverlay.GetDefaultCreateProtocolIdForTests(noRitsu, true));
        Assert.Equal(301, LanConnectLobbyOverlay.GetDefaultCreateProtocolIdForTests(ritsuReady, false));
        Assert.Equal(300, LanConnectLobbyOverlay.GetDefaultCreateProtocolIdForTests(ritsuUnavailable, false));
    }
}
