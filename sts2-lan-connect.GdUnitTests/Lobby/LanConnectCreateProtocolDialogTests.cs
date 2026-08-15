using GdUnit4;
using Godot;
using Sts2LanConnect.GdUnitTests.Chat;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Lobby;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectCreateProtocolDialogTests
{
    [TestCase]
    public async Task Create_dialog_defaults_to_compat_without_Ritsu_and_exposes_tail_warning()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1280, 720),
            LanConnectServerChatPresentation.Ready);

        fixture.Overlay.OpenCreateDialogForTests();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.SelectedCreateProtocolIdForTests).IsEqual(300);
        AssertThat(fixture.Overlay.CreateProtocolOptionLabelsForTests())
            .Contains("兼容旧版客户端")
            .Contains("0.6 新协议（RitsuLib 状态必须一致）");
        AssertThat(fixture.Overlay.CreateProtocolOptionDisabledStatesForTests())
            .IsEqual(new[] { false, false });
        AssertThat(fixture.Overlay.CreateProtocolDescriptionForTests)
            .IsEqual("支持 0.3-0.5，不支持 RitsuLib");

        fixture.Overlay.SelectCreateProtocolForTests(301);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.CreateProtocolDescriptionForTests)
            .IsEqual("仅支持 0.6+；RitsuLib 状态必须一致");
    }

    [TestCase]
    public async Task Room_cards_and_details_expose_protocol_carrier_and_Ritsu_presence()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1280, 720),
            LanConnectServerChatPresentation.Ready);

        IReadOnlyList<string> labels = fixture.Overlay.VisibleLabelTextsForTests();

        AssertThat(labels.Any(static text => text.Contains("compat_4_5_v1", StringComparison.Ordinal))).IsTrue();
        AssertThat(labels.Any(static text => text.Contains("none", StringComparison.Ordinal))).IsTrue();
        AssertThat(labels.Any(static text => text.Contains("无 RitsuLib", StringComparison.Ordinal))).IsTrue();

        fixture.Overlay.SelectRoomForTests("room-b");
        await fixture.Runner.AwaitIdleFrame();
        labels = fixture.Overlay.VisibleLabelTextsForTests();

        AssertThat(labels.Any(static text => text.Contains("tail_v1", StringComparison.Ordinal))).IsTrue();
        AssertThat(labels.Any(static text => text.Contains("ritsulib_sidecar_v1", StringComparison.Ordinal))).IsTrue();
        AssertThat(labels.Any(static text => text.Contains("需要 RitsuLib", StringComparison.Ordinal))).IsTrue();
    }
}
