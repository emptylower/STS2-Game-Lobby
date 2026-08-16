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
    public async Task Create_dialog_wraps_its_content_instead_of_filling_the_viewport()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);

        fixture.Overlay.OpenCreateDialogForTests();
        await fixture.Runner.AwaitIdleFrame();

        Rect2 card = fixture.Overlay.CreateDialogCardRectForTests;
        Rect2 body = fixture.Overlay.CreateDialogBodyRectForTests;
        AssertThat(card.Size.X).IsLessEqual(760f);
        AssertThat(card.Size.Y).IsLessEqual(820f);
        AssertThat(body.Size.X).IsGreaterEqual(700f);
        AssertThat(card.Size.Y - body.Size.Y).IsLessEqual(100f);
    }

    [TestCase]
    public async Task Create_dialog_stays_inside_a_small_landscape_viewport()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(960, 540),
            LanConnectServerChatPresentation.Ready);

        fixture.Overlay.OpenCreateDialogForTests();
        await fixture.Runner.AwaitIdleFrame();

        Rect2 card = fixture.Overlay.CreateDialogCardRectForTests;
        AssertThat(card.Position.X).IsGreaterEqual(0f);
        AssertThat(card.Position.Y).IsGreaterEqual(0f);
        AssertThat(card.End.X).IsLessEqual(960f);
        AssertThat(card.End.Y).IsLessEqual(540f);
        AssertThat(fixture.Overlay.CreateProtocolChoiceRectsForTests()
            .All(static rect => rect.Size.Y >= 76f)).IsTrue();
    }

    [TestCase]
    public async Task Locked_create_modes_show_a_prompt_and_keep_the_last_valid_selection()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1280, 720),
            LanConnectServerChatPresentation.Ready);
        fixture.Overlay.SetCreateGameModeAvailabilityForTests(new(
            Standard: true,
            Daily: false,
            Custom: false));
        string popupTitle = string.Empty;
        string popupMessage = string.Empty;
        fixture.Overlay.SetCreateGameModePopupForTests((title, message) =>
        {
            popupTitle = title;
            popupMessage = message;
        });
        fixture.Overlay.OpenCreateDialogForTests();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.CreateGameModeOptionLabelsForTests())
            .Contains("多人每日挑战（未解锁）")
            .Contains("自定义模式（未解锁）");

        fixture.Overlay.SelectCreateGameModeForTests(1);

        AssertThat(fixture.Overlay.SelectedCreateGameModeIdForTests).IsEqual(0);
        AssertThat(popupTitle).IsEqual("模式尚未解锁");
        AssertThat(popupMessage).Contains("多人每日挑战尚未解锁");

        fixture.Overlay.SelectCreateGameModeForTests(2);

        AssertThat(fixture.Overlay.SelectedCreateGameModeIdForTests).IsEqual(0);
        AssertThat(popupTitle).IsEqual("模式尚未解锁");
        AssertThat(popupMessage).Contains("自定义模式尚未解锁");
    }

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
        AssertThat(fixture.Overlay.CreateProtocolChoiceRectsForTests()
            .All(static rect => rect.Size.Y >= 76f)).IsTrue();
        AssertThat(fixture.Overlay.CreateProtocolDescriptionForTests)
            .IsEqual("支持 0.3-0.5，不支持 RitsuLib");

        fixture.Overlay.PressCreateProtocolForTests(301);
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
