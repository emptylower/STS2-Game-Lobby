using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

/// <summary>
/// Acceptance tests for the slim control strip that replaced the room chat overlay's title bar
/// and tab bar (room chat HUD redesign, design spec §5.3). The strip is:
/// `房间` `频道`(6px unread dot) …spacer… `固定` `收起`, all routed through
/// <see cref="LanConnectHudLegibility.ApplyHudButtonStyle"/> for the first time anything in the
/// overlay actually renders that contract.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomChatShellTests
{
    [TestCase]
    public async Task Title_label_no_longer_exists()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();

        bool anyTitleLabel = fixture.Overlay
            .FindChildren("*", "Label", recursive: true, owned: false)
            .OfType<Label>()
            .Any(label => label.Text == "聊天");

        AssertThat(anyTitleLabel).IsFalse();
    }

    [TestCase]
    public async Task Every_strip_button_has_focus_stylebox_rest_alpha_and_touch_floor()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();

        foreach (string name in StripButtonNames)
        {
            Button button = FindNode<Button>(fixture.Overlay, name);
            AssertThat(button.HasThemeStyleboxOverride("focus")).IsTrue();

            StyleBoxFlat normal = (StyleBoxFlat)button.GetThemeStylebox("normal");
            AssertThat(normal.BgColor.A).IsEqual(LanConnectHudLegibility.RestPlateColor.A);

            AssertThat(button.CustomMinimumSize.X)
                .IsGreaterEqual(LanConnectHudLegibility.MinTouchTargetPixels);
            AssertThat(button.CustomMinimumSize.Y)
                .IsGreaterEqual(LanConnectHudLegibility.MinTouchTargetPixels);
        }
    }

    [TestCase]
    public async Task Activating_the_channel_control_selects_the_server_channel()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        AssertThat(fixture.Overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Room);

        Button serverTab = FindNode<Button>(fixture.Overlay, "ServerChatTab");
        serverTab.EmitSignal(Button.SignalName.Pressed);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.TestState.SelectedChannel).IsEqual(LanConnectChatChannel.Server);
    }

    [TestCase]
    public async Task Unread_dot_shows_for_server_unread_and_hides_once_read()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        Control dot = FindNode<Control>(fixture.Overlay, "ServerUnreadDot");
        AssertThat(dot.Visible).IsFalse();

        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Server, sequence: 5);
        await fixture.Overlay.RefreshForTests();
        AssertThat(fixture.Overlay.TestState.ServerUnread).IsGreater(0);
        AssertThat(dot.Visible).IsTrue();

        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        await fixture.Overlay.RefreshForTests();
        AssertThat(fixture.Overlay.TestState.ServerUnread).IsEqual(0);
        AssertThat(dot.Visible).IsFalse();
    }

    [TestCase]
    public async Task Strip_fades_with_the_panel_while_the_toggle_bubble_stays_opaque()
    {
        // Reuses the EffectiveAlpha helper pattern from LanConnectRoomOverlayFadeTests: the
        // control strip now lives inside _panelFadeContainer (design spec §5.3's explicit
        // requirement), so it must fade with the panel, while the toggle bubble — the only
        // reopen entry point on touch platforms — must never fade.
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => true);
        clock.NowSeconds = 5d;

        fixture.Overlay.RefreshFadeForTests();

        AssertThat(fixture.Overlay.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Faded);
        CanvasItem strip = FindNode<Control>(fixture.Overlay, "RoomChatControlStrip");
        CanvasItem toggle = FindNode<Button>(fixture.Overlay, "RoomChatToggleButton");
        AssertThat(EffectiveAlpha(strip)).IsEqual(0f);
        AssertThat(EffectiveAlpha(toggle)).IsEqual(1f);
    }

    [TestCase]
    public async Task Long_press_on_the_strip_drag_zone_still_moves_the_panel()
    {
        // The header used to host the drag handle; removing it must not remove the touch
        // long-press reposition capability. The handle now lives on the strip's dedicated
        // spacer (RoomChatStripDragZone), the only non-button surface on the strip. Real
        // InputEventMouseButton/Motion events are pushed through the overlay's own viewport
        // (in local coordinates, since the overlay lives inside a SubViewport in this fixture)
        // so both the GuiInput hit-test that starts the drag and the _Input hook that tracks
        // it while held go through exactly the same path production input does.
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        Control dragZone = FindNode<Control>(fixture.Overlay, "RoomChatStripDragZone");
        Viewport viewport = fixture.Overlay.GetViewport();
        Rect2 before = fixture.Overlay.TestState.PanelRect;

        Vector2 pressPosition = dragZone.GetGlobalRect().GetCenter();
        viewport.PushInput(
            new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = pressPosition
            },
            inLocalCoords: true);
        await fixture.Runner.AwaitIdleFrame();

        // Advance real process time past DragHoldSeconds (0.28s) deterministically.
        await fixture.Runner.SimulateFrames(20, 20);

        Vector2 dragPosition = pressPosition + new Vector2(-40f, -30f);
        viewport.PushInput(
            new InputEventMouseMotion
            {
                Position = dragPosition,
                ButtonMask = MouseButtonMask.Left
            },
            inLocalCoords: true);
        await fixture.Runner.AwaitIdleFrame();

        viewport.PushInput(
            new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = dragPosition
            },
            inLocalCoords: true);
        await fixture.Runner.AwaitIdleFrame();

        Rect2 after = fixture.Overlay.TestState.PanelRect;
        AssertThat(after.Position).IsNotEqual(before.Position);
    }

    [TestCase]
    public async Task Hud_panel_frame_is_flat_translucent_with_no_border_and_tight_corners()
    {
        // Room chat HUD redesign spec §5.1: "solid panel with a gold border" -> flat
        // translucent HUD look. Values come from the reference mod (spec §3.2 / §5.1).
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        PanelContainer panelFrame = FindNode<PanelContainer>(fixture.Overlay, "RoomChatPanelFrame");
        StyleBoxFlat style = (StyleBoxFlat)panelFrame.GetThemeStylebox("panel");

        AssertThat(style.BgColor.A).IsEqual(0.75f);
        AssertThat(style.BorderWidthLeft).IsEqual(0);
        AssertThat(style.BorderWidthTop).IsEqual(0);
        AssertThat(style.BorderWidthRight).IsEqual(0);
        AssertThat(style.BorderWidthBottom).IsEqual(0);
        AssertThat(style.CornerRadiusTopLeft).IsEqual(4);
        AssertThat(style.CornerRadiusTopRight).IsEqual(4);
        AssertThat(style.CornerRadiusBottomLeft).IsEqual(4);
        AssertThat(style.CornerRadiusBottomRight).IsEqual(4);
    }

    [TestCase]
    public async Task Hud_messages_and_input_read_as_distinct_plates_with_a_gap_between_them()
    {
        // §5.1: the input area stops being "visually continuous with the messages" and
        // becomes its own plate, separated from the messages plate by a 10px gap. Both
        // plates must be independently identifiable StyleBoxFlats (different BgColors) with
        // non-overlapping rects, or the "gap" is not actually visible on screen.
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        PanelContainer messagesPlate = FindNode<PanelContainer>(fixture.Overlay, "ChatMessagesPlate");
        PanelContainer inputPlate = FindNode<PanelContainer>(fixture.Overlay, "ChatInputPlate");

        StyleBoxFlat messagesStyle = (StyleBoxFlat)messagesPlate.GetThemeStylebox("panel");
        StyleBoxFlat inputStyle = (StyleBoxFlat)inputPlate.GetThemeStylebox("panel");
        AssertThat(inputStyle.BgColor).IsNotEqual(messagesStyle.BgColor);
        AssertThat(inputStyle.BgColor).IsEqual(new Color(0.08f, 0.08f, 0.15f, 0.9f));
        AssertThat(inputStyle.BorderColor).IsEqual(new Color(0.3f, 0.35f, 0.5f, 0.6f));

        Rect2 messagesRect = messagesPlate.GetGlobalRect();
        Rect2 inputRect = inputPlate.GetGlobalRect();
        float gap = inputRect.Position.Y - (messagesRect.Position.Y + messagesRect.Size.Y);
        AssertThat(gap).IsGreater(0f);
    }

    [TestCase]
    public async Task Hud_message_plate_hugs_a_small_history_well_below_the_old_390px_floor()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        AppendRoomMessages(fixture.State.Room, count: 4);
        await fixture.Overlay.RefreshForTests();
        await AwaitStableLayout(fixture.Runner);

        PanelContainer messagesPlate = FindNode<PanelContainer>(fixture.Overlay, "ChatMessagesPlate");
        ScrollContainer messagesScroll = FindNode<ScrollContainer>(
            fixture.Overlay,
            LanConnectConstants.ChatMessagesScrollName);

        // Four short flattened rows fit inside the 140px readability floor. The enclosing
        // plate also contains the channel title and its 8px gap, but must remain comfortably
        // below 300px instead of inheriting the overlay's former fixed 390px minimum.
        AssertThat(messagesScroll.CustomMinimumSize.Y).IsEqual(140f);
        AssertThat(messagesPlate.Size.Y).IsLess(300f);
    }

    [TestCase]
    public async Task Hud_message_plate_caps_a_long_history_at_260px_and_remains_scrollable()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        AppendRoomMessages(fixture.State.Room, count: 40);
        await fixture.Overlay.RefreshForTests();
        await AwaitStableLayout(fixture.Runner);

        ScrollContainer messagesScroll = FindNode<ScrollContainer>(
            fixture.Overlay,
            LanConnectConstants.ChatMessagesScrollName);
        ScrollBar bar = messagesScroll.GetVScrollBar();

        // The reference mod caps at 250px. Our flattened rows carry additional inline
        // delivery metadata, so this HUD deliberately allows 10px more before scrolling.
        AssertThat(messagesScroll.CustomMinimumSize.Y).IsEqual(260f);
        AssertThat(Math.Max(bar.MinValue, bar.MaxValue - bar.Page)).IsGreater(0d);
    }

    [TestCase]
    public async Task Compact_portrait_viewport_keeps_the_message_scroll_at_72px()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport(new Vector2I(720, 1280));
        AppendRoomMessages(fixture.State.Room, count: 20);
        await fixture.Overlay.RefreshForTests();
        await AwaitStableLayout(fixture.Runner);

        PanelContainer messagesPlate = FindNode<PanelContainer>(fixture.Overlay, "ChatMessagesPlate");
        ScrollContainer messagesScroll = FindNode<ScrollContainer>(
            fixture.Overlay,
            LanConnectConstants.ChatMessagesScrollName);

        AssertThat(messagesScroll.CustomMinimumSize.Y).IsEqual(72f);
        AssertThat(messagesPlate.Size.Y).IsLessEqual(140f);
        AssertThat(messagesPlate.GetGlobalRect().End.Y).IsLessEqual(1280f);
    }

    [TestCase]
    public async Task Touch_toggle_is_a_round_56px_icon_bubble()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        Button toggle = FindNode<Button>(fixture.Overlay, "RoomChatToggleButton");

        AssertThat(MathF.Abs(toggle.Size.X - toggle.Size.Y)).IsLessEqual(2f);
        AssertThat(toggle.Size.X).IsGreaterEqual(56f);
        AssertThat(toggle.Size.Y).IsGreaterEqual(56f);
        AssertThat(toggle.Text).IsEmpty();
        AssertThat(toggle.Icon).IsNotNull();

        int expectedRadius = Mathf.RoundToInt(toggle.Size.X / 2f);
        foreach (string styleName in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
        {
            StyleBoxFlat style = (StyleBoxFlat)toggle.GetThemeStylebox(styleName);
            AssertThat(style.CornerRadiusTopLeft).IsEqual(expectedRadius);
            AssertThat(style.CornerRadiusTopRight).IsEqual(expectedRadius);
            AssertThat(style.CornerRadiusBottomLeft).IsEqual(expectedRadius);
            AssertThat(style.CornerRadiusBottomRight).IsEqual(expectedRadius);
        }
    }

    [TestCase]
    public async Task Toggle_unread_badge_still_overlays_the_bubble_and_clears_when_room_is_read()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Room, sequence: 7);
        await fixture.Overlay.RefreshForTests();
        await AwaitStableLayout(fixture.Runner);

        Button toggle = FindNode<Button>(fixture.Overlay, "RoomChatToggleButton");
        Control badge = FindNode<Control>(fixture.Overlay, "ChatToggleUnreadBadge");
        AssertThat(badge.Visible).IsTrue();
        AssertThat(badge.GetGlobalRect().Intersects(toggle.GetGlobalRect())).IsTrue();

        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Room);
        await fixture.Overlay.RefreshForTests();
        AssertThat(badge.Visible).IsFalse();
    }

    [TestCase]
    public async Task Hud_draft_input_placeholder_is_localized_and_not_a_bare_key()
    {
        // The placeholder must come from the localizer (both English and SimplifiedChinese
        // tables carry the key, see LanConnectChatLocalizerTests.ExpectedKeys) and not fall
        // back to the raw key string, which is what LanConnectChatLocalizer.Get returns for a
        // missing entry.
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        TextEdit draftInput = FindNode<TextEdit>(fixture.Overlay, LanConnectConstants.ChatDraftInputName);

        AssertThat(draftInput.PlaceholderText).IsNotEmpty();
        AssertThat(draftInput.PlaceholderText.StartsWith("chat.", StringComparison.Ordinal)).IsFalse();
        AssertThat(draftInput.GetThemeColor("font_placeholder_color"))
            .IsEqual(new Color(0.55f, 0.55f, 0.65f, 1f));

        // ChatInputPlate owns the reference-coloured surface. Individual text runs stay
        // transparent at rest so a second dark rectangle is not nested inside that plate.
        StyleBoxFlat normal = (StyleBoxFlat)draftInput.GetThemeStylebox("normal");
        AssertThat(normal.BgColor).IsEqual(Colors.Transparent);
        AssertThat(normal.BorderWidthLeft).IsEqual(0);
    }

    [TestCase]
    public async Task Lobby_sidebar_shell_keeps_its_inline_surface_and_input_row_unchanged()
    {
        // Guard against the HUD-only messages/input plate split (§5.1) leaking into the lobby
        // sidebar, which is explicitly out of scope (spec §2.2 non-goal 1). Every value here
        // is today's (pre-redesign) behaviour; this must fail if someone later drops the
        // `!UsesLobbyStyle` gate around the plate split.
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(LanConnectChatUiComposition.Icons)
        {
            ChatVisualStyle = LanConnectChatVisualStyle.LobbySidebar
        })!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(
            new LanConnectChatChannelState(LanConnectChatChannel.Server),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        AssertThat(panel.FindChild("ChatMessagesPlate", recursive: true, owned: false)).IsNull();
        AssertThat(panel.FindChild("ChatInputPlate", recursive: true, owned: false)).IsNull();

        ScrollContainer messagesScroll = FindNode<ScrollContainer>(panel, LanConnectConstants.ChatMessagesScrollName);
        AssertThat(messagesScroll.GetParent()).IsSame(panel);
        AssertThat(messagesScroll.CustomMinimumSize).IsEqual(new Vector2(0f, 140f));
        AssertThat(panel.GetThemeConstant("separation")).IsEqual(8);

        Control draftEditor = FindNode<Control>(panel, LanConnectConstants.ChatRichDraftEditorName);
        Control inputRow = (Control)draftEditor.GetParent()!;
        AssertThat(inputRow.GetParent()).IsSame(panel);
        AssertThat(inputRow.GetThemeConstant("separation")).IsEqual(6);

        TextEdit draftInput = FindNode<TextEdit>(panel, LanConnectConstants.ChatDraftInputName);
        AssertThat(draftInput.GetThemeColor("font_placeholder_color"))
            .IsEqual(new Color(0.46f, 0.36f, 0.31f, 1f));
        StyleBoxFlat normal = (StyleBoxFlat)draftInput.GetThemeStylebox("normal");
        AssertThat(normal.BgColor).IsEqual(Colors.Transparent);
        AssertThat(normal.ContentMarginLeft).IsEqual(0f);
        AssertThat(normal.ContentMarginTop).IsEqual(0f);
        AssertThat(normal.ContentMarginRight).IsEqual(0f);
        AssertThat(normal.ContentMarginBottom).IsEqual(0f);
    }

    private static readonly string[] StripButtonNames =
    {
        "RoomChatTab", "ServerChatTab", "ChatPinButton", "ChatCloseButton"
    };

    private static T FindNode<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);

    private static void AppendRoomMessages(LanConnectChatChannelState state, int count)
    {
        for (int index = 0; index < count; index++)
        {
            state.AppendConfirmedForTests(
                $"hud-shell-{index}",
                "A",
                $"message {index}",
                index + 1,
                false);
        }
    }

    private static async Task AwaitStableLayout(ISceneRunner runner)
    {
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
    }

    /// <summary>
    /// Effective (as-rendered) alpha of a CanvasItem: Modulate does not implicitly inherit a
    /// stored value from parent to child, but at render time Godot composes it multiplicatively
    /// with SelfModulate (self only) and every CanvasItem ancestor's Modulate (cascades to
    /// descendants). This walks that real ancestor chain rather than reading a single property.
    /// </summary>
    private static float EffectiveAlpha(CanvasItem node)
    {
        float alpha = node.Modulate.A * node.SelfModulate.A;
        Node? parent = node.GetParent();
        while (parent is CanvasItem canvasParent)
        {
            alpha *= canvasParent.Modulate.A;
            parent = parent.GetParent();
        }
        return alpha;
    }

    private sealed class FakeClock : ILanConnectMonotonicClock
    {
        public double NowSeconds { get; set; }
    }
}
