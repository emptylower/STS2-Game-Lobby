using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectProtocolUiMessagesTests
{
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
    [InlineData("lan_legacy_carrier_unsupported")]
    [InlineData("lan_registry_fingerprint_required")]
    [InlineData("lan_registry_fingerprint_mismatch")]
    [InlineData("lan_client_version_too_old")]
    public void Known_protocol_failures_have_stable_user_messages(string code)
    {
        string message = LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure(
            code,
            code is "client_update_required" or "lan_client_version_too_old" ? "0.6.0-alpha.1" : null,
            code == "ritsulib_presence_mismatch"));

        Assert.NotEmpty(message);
        Assert.DoesNotContain(code, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lan_native_frame_invalid")]
    [InlineData("lan_type_id_mismatch")]
    [InlineData("lan_extension_missing")]
    public void Native_bus_frame_failures_embed_the_code_for_diagnostics(string code)
    {
        string message = LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure(code));

        Assert.Contains("新协议通信帧校验失败", message, StringComparison.Ordinal);
        Assert.Contains(code, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ritsu_and_legacy_carrier_messages_match_the_new_protocol_wording()
    {
        Assert.Equal(
            "“兼容旧版 Mod”房间不能启用 RitsuLib。请关闭 RitsuLib 后重试，或改用新协议房间。",
            LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure("ritsulib_not_allowed_in_compat_mode")));
        Assert.Equal(
            "该房间是旧版本创建的，要求所有玩家启用 RitsuLib；新协议房间不再有此限制。",
            LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure(
                "ritsulib_presence_mismatch", RequiredRitsuLibPresent: true)));
        Assert.Equal(
            "该房间是旧版本创建的，要求所有玩家关闭 RitsuLib；新协议房间不再有此限制。",
            LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure(
                "ritsulib_presence_mismatch", RequiredRitsuLibPresent: false)));
        Assert.Equal(
            "该房间使用已停用的旧版 RitsuLib 通道，请房主升级 LAN Connect 后重新建房。",
            LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure("ritsulib_sidecar_unavailable")));
        Assert.Equal(
            "该房间由旧版 LAN Connect 创建（旧载体），请房主升级后重新建房。",
            LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure("lan_legacy_carrier_unsupported")));
        Assert.Equal(
            "双方的联机消息注册表不一致（通常是 Mod 列表不同），无法使用新协议加入。",
            LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure("lan_registry_fingerprint_required")));
        Assert.Equal(
            "双方的联机消息注册表不一致（通常是 Mod 列表不同），无法使用新协议加入。",
            LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure("lan_registry_fingerprint_mismatch")));
        Assert.Equal(
            "客户端版本过旧，请更新到 0.6.1 或更高版本。",
            LanConnectProtocolUiMessages.Describe(new LanConnectProtocolFailure(
                "lan_client_version_too_old", RequiredClientVersion: "0.6.1")));
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
            "沿用旧版联机协议，可与 0.3–0.5 旧版客户端同房；不支持 RitsuLib",
            LanConnectLobbyOverlay.CreateProtocolDescriptionForTestsStatic(300));
        Assert.Equal(
            "通过官方 Mod 消息注册通道传输，需 0.6.1 及以上客户端；与是否安装 RitsuLib 无关",
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
    public void Tail_with_Ritsu_tolerates_an_unavailable_public_sidecar()
    {
        // 0.5.18 事故正面回归：present 但 sidecar 不可用照常创建 tail 房间（native 载体）。
        LanConnectProtocolOffer offer = new(1, 1, "0.6.1-alpha.1", true, false);

        Assert.Equal(
            LanConnectProtocolProfile.TailV1,
            LanConnectLobbyOverlay.BuildCreateRoomIntentForTests(301, 8, offer).Validate().Profile);
    }

    [Fact]
    public void Create_options_follow_local_Ritsu_presence_and_carrier_readiness()
    {
        LanConnectProtocolOffer noRitsu = new(1, 1, "0.6.1-alpha.1", false, false);
        LanConnectProtocolOffer ritsuUnavailable = new(1, 1, "0.6.1-alpha.1", true, false);

        // tail 可选性只取决于运行时支持，不再消费 sidecar 可用性。
        Assert.True(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(301, noRitsu, true));
        Assert.False(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(301, noRitsu, false));
        Assert.True(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(301, ritsuUnavailable, true));
        Assert.False(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(300, ritsuUnavailable, false));
        Assert.True(LanConnectLobbyOverlay.IsCreateProtocolSelectableForTests(300, noRitsu, false));
    }

    [Fact]
    public void Create_dialog_defaults_to_the_first_protocol_supported_by_the_local_runtime()
    {
        LanConnectProtocolOffer noRitsu = new(1, 1, "0.6.1-alpha.1", false, false);
        LanConnectProtocolOffer ritsuUnavailable = new(1, 1, "0.6.1-alpha.1", true, false);

        Assert.Equal(300, LanConnectLobbyOverlay.GetDefaultCreateProtocolIdForTests(noRitsu, true));
        Assert.Equal(301, LanConnectLobbyOverlay.GetDefaultCreateProtocolIdForTests(ritsuUnavailable, true));
    }
}
