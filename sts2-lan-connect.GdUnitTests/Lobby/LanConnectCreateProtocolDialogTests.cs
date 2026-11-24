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
    public async Task Host_mod_checklist_overlays_the_create_form_and_dismisses_independently()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(960, 540),
            LanConnectServerChatPresentation.Ready);

        fixture.Overlay.OpenCreateDialogForTests();
        await fixture.Runner.AwaitIdleFrame();
        LineEdit roomName = (LineEdit)fixture.Overlay.FindChild("CreateRoomNameInput", owned: false);
        roomName.Text = "Room name draft";

        List<LobbyModDescriptor> mods = Enumerable.Range(1, 12).Select(index => new LobbyModDescriptor
        {
            Id = $"gameplay-mod-{index}",
            Version = "2.3.4",
            Role = LanConnectModRoles.Gameplay
        }).ToList();
        mods.Add(new LobbyModDescriptor
        {
            Id = "required-library",
            Version = "1.2.3",
            Role = LanConnectModRoles.Dependency
        });
        fixture.Overlay.ShowCreateGameplayModsDialog(mods);
        await fixture.Runner.AwaitIdleFrame();

        LanConnectModSyncDialog preview = (LanConnectModSyncDialog)fixture.Overlay.FindChild(
            "LanConnectModSyncDialog", owned: false);
        AssertThat(fixture.Overlay.CreateDialogVisibleForTests).IsTrue();
        AssertThat(preview.IsVisibleInTree()).IsTrue();
        AssertThat(fixture.Overlay.CreateDialogCardRectForTests.Encloses(preview.TestState.PanelRect)).IsTrue();
        AssertThat(preview.TestState.RowCount).IsEqual(13);
        Label dependency = (Label)preview.FindChild("ModSyncRow_required-library", owned: false)
            .FindChild("ModSyncRowDetail", owned: false);
        AssertThat(dependency.Text).Contains("版本 1.2.3").Contains("依赖");

        ScrollContainer list = (ScrollContainer)preview.FindChild("ModSyncRowsScroll", owned: false);
        AssertThat(list.GetVScrollBar().Visible).IsTrue();
        AssertThat(list.FindChildren("*", "CheckBox", owned: false).Count).IsEqual(0);
        Control actions = (Control)preview.FindChild("ModSyncActions", owned: false);
        AssertThat(actions.IsVisibleInTree()).IsFalse();

        Button close = (Button)preview.FindChild("HostModPreviewCloseButton", owned: false);
        AssertThat(close.IsVisibleInTree()).IsTrue();
        AssertThat(close.HasFocus()).IsTrue();
        close.EmitSignal(Button.SignalName.Pressed);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.FindChild("LanConnectModSyncDialog", owned: false) == null).IsTrue();
        AssertThat(fixture.Overlay.CreateDialogVisibleForTests).IsTrue();
        AssertThat(roomName.HasFocus()).IsTrue();
        AssertThat(roomName.Text).IsEqual("Room name draft");

        IReadOnlyList<LobbyModDescriptor> cosmeticOnly = LanConnectModInventoryBuilder.Build([
            new LanConnectRuntimeMod("cosmetic-only", "1.0.0", AffectsGameplay: false, IsLoaded: true,
                Source: LanConnectModSources.ModsDirectory, WorkshopFileId: null, Dependencies: [])
        ]);
        fixture.Overlay.ShowCreateGameplayModsDialog(cosmeticOnly);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.FindChild("LanConnectModSyncDialog", owned: false) == null).IsTrue();
        AssertThat(fixture.Overlay.CreateDialogVisibleForTests).IsTrue();
        AssertThat(roomName.HasFocus()).IsTrue();

        fixture.Overlay.ShowCreateGameplayModsDialog(mods);
        await fixture.Runner.AwaitIdleFrame();
        fixture.Overlay._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.FindChild("LanConnectModSyncDialog", owned: false) == null).IsTrue();
        AssertThat(fixture.Overlay.CreateDialogVisibleForTests).IsTrue();
    }

    [TestCase(LanConnectLobbyThemes.ArcadeRetroId)]
    [TestCase(LanConnectLobbyThemes.MidnightGlassId)]
    [TestCase(LanConnectLobbyThemes.SaasCleanId)]
    public async Task Host_mod_checklist_uses_the_active_lobby_theme(string themeId)
    {
        LanConnectLobbyTheme theme = LanConnectLobbyThemes.All.Single(candidate => candidate.Id == themeId);
        LanConnectLobbyTheme previousTheme = LanConnectLobbyThemes.Current;
        LanConnectLobbyThemes.SetForTests(theme);
        try
        {
            using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
                new Vector2I(960, 540),
                LanConnectServerChatPresentation.Ready);
            fixture.Overlay.OpenCreateDialogForTests();
            fixture.Overlay.ShowCreateGameplayModsDialog([
                new LobbyModDescriptor { Id = "example", Version = "1.0", Role = LanConnectModRoles.Gameplay }
            ]);
            await fixture.Runner.AwaitIdleFrame();

            Node preview = fixture.Overlay.FindChild("LanConnectModSyncDialog", owned: false);
            Label title = (Label)preview.FindChild("ModSyncTitle", owned: false);
            Label detail = (Label)preview.FindChild("ModSyncRowDetail", owned: false);
            PanelContainer row = (PanelContainer)preview.FindChild("ModSyncRow_example", owned: false);
            StyleBoxFlat rowStyle = (StyleBoxFlat)row.GetThemeStylebox("panel");
            ScrollContainer scroll = (ScrollContainer)preview.FindChild("ModSyncRowsScroll", owned: false);
            StyleBoxFlat thumb = (StyleBoxFlat)scroll.GetVScrollBar().GetThemeStylebox("grabber");
            PanelContainer panel = (PanelContainer)preview.FindChild("ModSyncPanel", owned: false);

            AssertThat(title.GetThemeColor("font_color")).IsEqual(theme.Palette.Accent);
            AssertThat(detail.GetThemeColor("font_color")).IsEqual(theme.Palette.TextMuted);
            AssertThat(rowStyle.BgColor).IsEqual(theme.Palette.Card);
            AssertThat(rowStyle.BorderColor).IsEqual(theme.Palette.Border);
            AssertThat(thumb.BgColor).IsEqual(theme.Palette.Accent);
            AssertThat(panel.Material is ShaderMaterial).IsEqual(theme.Shape.Glass);
        }
        finally
        {
            LanConnectLobbyThemes.SetForTests(previousTheme);
        }
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
    public async Task Create_dialog_defaults_to_tail_when_available_and_allows_manual_switch_to_compat()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1280, 720),
            LanConnectServerChatPresentation.Ready);

        fixture.Overlay.OpenCreateDialogForTests();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.SelectedCreateProtocolIdForTests).IsEqual(301);
        AssertThat(fixture.Overlay.CreateDialogSubmitEnabledForTests).IsTrue();
        AssertThat(fixture.Overlay.CreateProtocolOptionLabelsForTests())
            .Contains("新协议")
            .Contains("兼容旧版 Mod");
        AssertThat(fixture.Overlay.CreateProtocolOptionDisabledStatesForTests())
            .IsEqual(new[] { false, false });
        AssertThat(fixture.Overlay.CreateProtocolChoiceRectsForTests()
            .All(static rect => rect.Size.Y >= 76f)).IsTrue();
        AssertThat(fixture.Overlay.CreateProtocolDescriptionForTests)
            .IsEqual("通过官方 Mod 消息注册通道传输，需 0.6.2 及以上客户端；与双方是否安装 RitsuLib、Mod 列表是否相同均无关");

        fixture.Overlay.PressCreateProtocolForTests(300);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.SelectedCreateProtocolIdForTests).IsEqual(300);
        AssertThat(fixture.Overlay.CreateProtocolDescriptionForTests)
            .IsEqual("沿用旧版联机协议，可与 0.3–0.5 旧版客户端同房；不支持 RitsuLib");
    }

    [TestCase]
    public async Task Create_dialog_disables_submit_and_explains_when_neither_protocol_is_available()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1280, 720),
            LanConnectServerChatPresentation.Ready);

        fixture.Overlay.OpenCreateDialogForTests();
        fixture.Overlay.SetCreateProtocolOfferOverrideForTests(
            new LanConnectProtocolOffer(0, 0, "0.6.5", true, false),
            standaloneTailRuntimeAvailable: true);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.SelectedCreateProtocolIdForTests).IsNull();
        AssertThat(fixture.Overlay.CreateDialogSubmitEnabledForTests).IsFalse();
        AssertThat(fixture.Overlay.CreateProtocolOptionDisabledStatesForTests())
            .IsEqual(new[] { true, true });
        AssertThat(fixture.Overlay.CreateProtocolDescriptionForTests)
            .Contains("两种联机协议均不可用")
            .Contains("0.111+")
            .Contains("RitsuLib");
    }

    [TestCase]
    public async Task Room_cards_and_details_expose_protocol_carrier_and_Ritsu_presence()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1280, 720),
            LanConnectServerChatPresentation.Ready);

        IReadOnlyList<string> labels = fixture.Overlay.VisibleLabelTextsForTests();

        AssertThat(labels.Any(static text => text.Contains("兼容旧版 Mod", StringComparison.Ordinal))).IsTrue();
        AssertThat(labels.Any(static text => text.Contains("旧版协议", StringComparison.Ordinal))).IsTrue();
        AssertThat(labels.Any(static text => text.Contains("不支持 RitsuLib", StringComparison.Ordinal))).IsTrue();

        fixture.Overlay.SelectRoomForTests("room-b");
        await fixture.Runner.AwaitIdleFrame();
        labels = fixture.Overlay.VisibleLabelTextsForTests();

        AssertThat(labels.Any(static text => text.Contains("新协议", StringComparison.Ordinal))).IsTrue();
        AssertThat(labels.Any(static text => text.Contains("旧载体（需房主升级）", StringComparison.Ordinal))).IsTrue();
        AssertThat(labels.All(static text =>
            !text.Contains("新协议", StringComparison.Ordinal)
            || !text.Contains("RitsuLib", StringComparison.Ordinal))).IsTrue();
    }
}
