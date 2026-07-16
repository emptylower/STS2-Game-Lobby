using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectChatResolutionTests
{
    private static readonly (Vector2I Size, float Scale)[] Cases =
    {
        (new Vector2I(1280, 720), 1.0f),
        (new Vector2I(1280, 720), 1.5f),
        (new Vector2I(1920, 1080), 1.0f),
        (new Vector2I(1920, 1080), 1.5f),
        (new Vector2I(2560, 1440), 1.0f),
        (new Vector2I(2560, 1440), 1.5f),
        (new Vector2I(3840, 2160), 1.0f),
        (new Vector2I(3840, 2160), 1.5f)
    };

    [TestCase]
    public async Task Lobby_and_room_chat_remain_in_bounds_for_supported_matrix()
    {
        foreach ((Vector2I size, float scale) in Cases)
        {
            using ChatUiFixture fixture = await ChatUiFixture.Create(size, scale);
            string context = $"{size.X}x{size.Y}@{scale:0.0}";

            AssertRectInside(fixture.Lobby.TestState.SidebarRect, fixture.ViewportRect, $"{context} lobby sidebar");
            AssertRectInside(fixture.Lobby.TestState.ServerChatRect, fixture.ViewportRect, $"{context} server chat");
            AssertRectInside(fixture.Room.TestState.PanelRect, fixture.ViewportRect, $"{context} room chat");
            AssertRectInside(fixture.Room.TestState.DraftRect, fixture.ViewportRect, $"{context} room draft");
            AssertRectInside(fixture.Room.TestState.SendRect, fixture.ViewportRect, $"{context} room send");
            AssertRectInside(fixture.Lobby.TestState.ServerChatPanelState.DraftRect, fixture.ViewportRect, $"{context} lobby draft");
            AssertRectInside(fixture.Lobby.TestState.ServerChatPanelState.SendRect, fixture.ViewportRect, $"{context} lobby send");
            AssertThat(fixture.Room.TestState.TabRects.Count).IsEqual(2);
            foreach (Rect2 tabRect in fixture.Room.TestState.TabRects)
            {
                AssertRectInside(tabRect, fixture.ViewportRect, $"{context} room tab");
            }
            AssertThat(fixture.Room.TestState.RetryRects.Count).IsEqual(2);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.RetryRects.Count).IsEqual(2);
            AssertThat(fixture.Room.TestState.NewMessagesBelowCount).IsGreater(0);
            AssertRectInside(fixture.Room.TestState.NewMessagesRect, fixture.ViewportRect, $"{context} new messages");
            AssertRectInside(
                fixture.Lobby.TestState.ServerChatPanelState.NewMessagesRect,
                fixture.ViewportRect,
                $"{context} lobby new messages");
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.MessageCount).IsEqual(50);
            AssertThat(fixture.Room.ChatPanelForTests.TestState.MessageCount).IsEqual(50);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.PendingCount).IsEqual(1);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.FailedCount).IsEqual(1);
            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.DeliveryUnknownCount).IsEqual(1);
            AssertThat(fixture.Room.TestState.RoomUnread).IsGreaterEqual(10);
            AssertThat(fixture.Room.TestState.ServerUnread).IsGreaterEqual(10);
            AssertThat(fixture.RoomUnreadBadgeText.Length).IsGreaterEqual(2);
            AssertThat(fixture.ServerUnreadBadgeText.Length).IsGreaterEqual(2);
            AssertThat(fixture.ToggleUnreadBadgeText)
                .IsEqual((fixture.Room.TestState.RoomUnread + fixture.Room.TestState.ServerUnread).ToString());
            IReadOnlyList<string> overlaps = fixture.OverlappingNamedControls();
            if (overlaps.Count > 0)
            {
                throw new InvalidOperationException($"{context} overlapping controls: {string.Join(", ", overlaps)}");
            }

            await fixture.ShowRichMatrix();
            try
            {
                Control[] messageRuns = fixture.RichMessageRuns();
                AssertThat(messageRuns.Length).IsEqual(13);
                AssertThat(messageRuns.Count(run => run is not Label)).IsEqual(12);
                foreach (Control run in messageRuns)
                {
                    AssertRectInside(run.GetGlobalRect(), fixture.ViewportRect, $"{context} rich message run");
                    if (run is not Label)
                    {
                        AssertThat(run.AccessibilityName.ToString().Length).IsGreater(0);
                    }
                }
                AssertNoPairwiseOverlap(messageRuns, $"{context} rich message runs");
                AssertThat(fixture.RichResolvedItemCount).IsGreaterEqual(5);
                AssertThat(fixture.RichUnknownItemCount).IsGreaterEqual(3);
                AssertThat(fixture.RichVisibleText.Contains("PrivateMod.", StringComparison.Ordinal)).IsFalse();

                Control[] editorRuns = fixture.RichEditorRuns();
                AssertThat(editorRuns.Length).IsGreaterEqual(4);
                AssertThat(editorRuns.Count(run =>
                    run.Name.ToString().StartsWith(
                        LanConnectConstants.ChatEntityChipPrefix,
                        StringComparison.Ordinal))).IsEqual(3);
                foreach (Control run in editorRuns)
                {
                    AssertRectInside(run.GetGlobalRect(), fixture.ViewportRect, $"{context} editor run");
                }
                AssertNoPairwiseOverlap(editorRuns, $"{context} editor runs");

                Button[] pickerButtons = await fixture.OpenRichEmojiPicker();
                AssertThat(pickerButtons.Length).IsEqual(18);
                foreach (Button button in pickerButtons)
                {
                    AssertRectInside(button.GetGlobalRect(), fixture.ViewportRect, $"{context} picker {button.Name}");
                    AssertThat(button.TooltipText.Length).IsGreater(0);
                    AssertThat(button.AccessibilityName.ToString().Length).IsGreater(0);
                }
                AssertNoPairwiseOverlap(pickerButtons, $"{context} picker buttons");
                await fixture.CloseRichEmojiPicker();

                foreach (string itemType in new[] { "card", "relic", "potion" })
                {
                    LanConnectItemPreviewTestState preview = await fixture.ShowPreview(itemType);
                    AssertThat(preview.Visible).IsTrue();
                    AssertThat(preview.ItemType).IsEqual(itemType);
                    AssertRectInside(preview.Bounds, fixture.ViewportRect, $"{context} {itemType} preview");
                    await fixture.ClosePreview();
                }

                foreach (Control control in fixture.AccessibilityStateControls())
                {
                    bool hasNonColorState =
                        (control is Button button && !string.IsNullOrWhiteSpace(button.Text)) ||
                        !string.IsNullOrWhiteSpace(control.AccessibilityName.ToString()) ||
                        !string.IsNullOrWhiteSpace(control.TooltipText);
                    AssertThat(hasNonColorState).IsTrue();
                }
                foreach (Label longLabel in fixture.LongNameLabels())
                {
                    AssertThat(longLabel.ClipText ||
                               longLabel.AutowrapMode != TextServer.AutowrapMode.Off).IsTrue();
                    AssertThat(longLabel.Size.X).IsLessEqual(fixture.ViewportRect.Size.X);
                    AssertThat(longLabel.Size.Y).IsGreater(0f);
                }
            }
            finally
            {
                await fixture.CloseRichSurfaces();
            }

            await fixture.AssertRealTabFocusOrder(context);
        }
    }

    [TestCase]
    public async Task Every_retry_focus_target_scrolls_fully_into_its_messages_viewport()
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(1280, 720), 1f);

        foreach (string retryName in fixture.RoomRetryNames())
        {
            Control retry = fixture.FindRoomControl(retryName);
            retry.GrabFocus();
            await fixture.AwaitTwoFrames();
            AssertThat(fixture.Room.TestState.FocusOwnerName).IsEqual(retryName);
            AssertRectInside(retry.GetGlobalRect(), fixture.Room.TestState.MessagesRect, $"room focused retry {retryName}");
            AssertRectInside(retry.GetGlobalRect(), fixture.ViewportRect, $"room viewport retry {retryName}");
        }

        foreach (string retryName in fixture.LobbyRetryNames())
        {
            Control retry = fixture.FindLobbyControl(retryName);
            retry.GrabFocus();
            await fixture.AwaitTwoFrames();
            AssertThat(fixture.Lobby.ServerChatPanelForTests.TestState.FocusOwnerName).IsEqual(retryName);
            AssertRectInside(
                retry.GetGlobalRect(),
                fixture.Lobby.TestState.ServerChatPanelState.MessagesRect,
                $"lobby focused retry {retryName}");
            AssertRectInside(retry.GetGlobalRect(), fixture.ViewportRect, $"lobby viewport retry {retryName}");
        }
    }

    [TestCase]
    public async Task Production_window_scale_enters_compact_layout_without_matrix_override()
    {
        Window window = ((SceneTree)Engine.GetMainLoop()).Root;
        float originalScale = window.ContentScaleFactor;
        try
        {
            window.ContentScaleFactor = 1.5f;
            using ChatUiFixture fixture = await ChatUiFixture.Create(
                new Vector2I(2560, 1440),
                1.5f,
                useUiScaleOverride: false);
            await fixture.TriggerRealResize(new Vector2I(2560, 1440), 1.5f);

            AssertThat(fixture.Lobby.TestState.CompactSidebarScrollVisible).IsTrue();
        }
        finally
        {
            window.ContentScaleFactor = originalScale;
        }
    }

    [TestCase]
    public async Task Every_lobby_named_focus_target_can_be_scrolled_into_the_viewport()
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(1280, 720), 1f);
        IReadOnlyList<LanConnectNamedControlRect> targets = fixture.Lobby.TestState.FocusTargetRects;
        AssertThat(targets.Count).IsGreater(0);

        for (int index = 0; index < targets.Count; index++)
        {
            string expectedName = targets[index].Name;
            fixture.Lobby.FocusLobbyTargetForTests(index);
            await fixture.AwaitTwoFrames();

            AssertThat(fixture.Lobby.TestState.ServerChatPanelState.FocusOwnerName).IsEqual(expectedName);
            AssertRectInside(
                fixture.Lobby.FocusedControlRectForTests(),
                fixture.ViewportRect,
                $"lobby focused target {expectedName}");
        }
    }

    [TestCase]
    public async Task Lobby_draft_focus_and_state_survive_real_breakpoint_resize()
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(1920, 1080), 1f);
        const string draft = "lobby resize draft\nsecond line";
        fixture.ServerState.SetDraft(draft);
        await fixture.Lobby.ServerChatPanelForTests.RefreshForTests();
        fixture.Lobby.ServerChatPanelForTests.SetScrollForTests(120, atBottom: false);
        fixture.Lobby.ServerChatPanelForTests.FocusDraft();
        await fixture.AwaitTwoFrames();
        LanConnectBasicChatPanel panelBefore = fixture.Lobby.ServerChatPanelForTests;
        LanConnectChatChannelState stateBefore = fixture.ServerState;

        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(3840, 2160) })
        {
            await fixture.Resize(size, 1f);
            LanConnectBasicChatPanelTestState state = fixture.Lobby.TestState.ServerChatPanelState;

            AssertThat(fixture.Lobby.ServerChatPanelForTests.DraftHasFocus).IsTrue();
            AssertThat(fixture.ServerState.Draft).IsEqual(draft);
            AssertThat(fixture.ServerState.ScrollOffset).IsEqual(120d);
            AssertThat(state.RenderedScrollOffset).IsEqual(120d);
            AssertThat(ReferenceEquals(panelBefore, fixture.Lobby.ServerChatPanelForTests)).IsTrue();
            AssertThat(ReferenceEquals(stateBefore, fixture.ServerState)).IsTrue();
            AssertThat(fixture.Lobby.TestState.FocusTargetRects.Count).IsGreater(0);
            foreach (Rect2 rect in fixture.Lobby.TestState.VisibleFocusTargetRects)
            {
                AssertRectInside(rect, fixture.ViewportRect, $"{size.X}x{size.Y} lobby focus target");
            }
            AssertThat(state.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
        }
    }

    [TestCase]
    public async Task Resize_preserves_room_chat_focus_channel_draft_scroll_and_focus_bounds()
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(1920, 1080), 1f);
        fixture.Room.SelectChannelForTests(LanConnectChatChannel.Server);
        fixture.Room.SetDraftForTests("resize keeps this draft\nwith a second line");
        fixture.Room.SetScrollForTests(120, atBottom: false);
        fixture.Room.FocusDraftForTests();
        await fixture.AwaitTwoFrames();
        LanConnectBasicChatPanel roomPanelBefore = fixture.Room.ChatPanelForTests;
        LanConnectChatChannelState channelStateBefore = fixture.Room.SelectedChannelStateForTests;
        LanConnectBasicChatPanel lobbyPanelBefore = fixture.Lobby.ServerChatPanelForTests;

        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(3840, 2160) })
        {
            await fixture.Resize(size, 1f);
            LanConnectRoomChatOverlayTestState state = fixture.Room.TestState;

            AssertThat(state.FocusOwnerName).IsEqual(LanConnectConstants.ChatDraftInputName);
            AssertThat(state.SelectedChannel).IsEqual(LanConnectChatChannel.Server);
            AssertThat(state.Draft).IsEqual("resize keeps this draft\nwith a second line");
            AssertThat(state.ScrollOffset).IsEqual(120d);
            AssertThat(state.RenderedScrollOffset).IsEqual(120d);
            AssertThat(ReferenceEquals(roomPanelBefore, fixture.Room.ChatPanelForTests)).IsTrue();
            AssertThat(ReferenceEquals(channelStateBefore, fixture.Room.SelectedChannelStateForTests)).IsTrue();
            AssertThat(ReferenceEquals(lobbyPanelBefore, fixture.Lobby.ServerChatPanelForTests)).IsTrue();
            foreach (LanConnectNamedControlRect target in fixture.Room.TestState.FocusTargetRects
                         .Where(target => !target.Name.StartsWith(
                             LanConnectConstants.ChatRetryButtonPrefix,
                             StringComparison.Ordinal)))
            {
                AssertRectInside(target.Rect, fixture.ViewportRect, $"{size.X}x{size.Y} focus target {target.Name}");
            }
            AssertThat(fixture.Lobby.TestState.FocusTargetRects.Count).IsGreater(0);
            foreach (Rect2 target in fixture.Lobby.TestState.VisibleFocusTargetRects)
            {
                AssertRectInside(target, fixture.ViewportRect, $"{size.X}x{size.Y} lobby focus target");
            }
        }
    }

    private static void AssertRectInside(Rect2 rect, Rect2 viewport, string name)
    {
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f ||
            rect.Position.X < viewport.Position.X || rect.Position.Y < viewport.Position.Y ||
            rect.End.X > viewport.End.X || rect.End.Y > viewport.End.Y)
        {
            throw new InvalidOperationException($"{name}: rect {rect} is outside viewport {viewport}");
        }
    }

    private static void AssertNoPairwiseOverlap(
        IReadOnlyList<Control> controls,
        string context)
    {
        for (int first = 0; first < controls.Count; first++)
        {
            for (int second = first + 1; second < controls.Count; second++)
            {
                Rect2 left = controls[first].GetGlobalRect();
                Rect2 right = controls[second].GetGlobalRect();
                if (left.Intersects(right, includeBorders: false))
                {
                    throw new InvalidOperationException(
                        $"{context}: {controls[first].Name}/{controls[second].Name} overlap");
                }
            }
        }
    }
}

internal sealed class ChatUiFixture : IDisposable
{
    private static readonly string LongNickname = new('N', 32);
    private readonly SubViewport _root;
    private readonly ISceneRunner _runner;
    private readonly Control _richSurface;
    private float _uiScale;

    private ChatUiFixture(
        SubViewport root,
        ISceneRunner runner,
        LanConnectLobbyOverlay lobby,
        LanConnectRoomChatOverlay room,
        LanConnectChatChannelState lobbyServerState,
        LanConnectChatChannelState roomServerState,
        Control richSurface,
        LanConnectBasicChatPanel richPanel,
        LanConnectChatChannelState richState,
        LanConnectItemPreview matrixPreview,
        float uiScale)
    {
        _root = root;
        _runner = runner;
        _richSurface = richSurface;
        Lobby = lobby;
        Room = room;
        ServerState = lobbyServerState;
        RoomServerState = roomServerState;
        RichPanel = richPanel;
        RichState = richState;
        MatrixPreview = matrixPreview;
        _uiScale = uiScale;
    }

    internal LanConnectLobbyOverlay Lobby { get; }

    internal LanConnectRoomChatOverlay Room { get; }

    internal LanConnectChatChannelState ServerState { get; }

    internal LanConnectChatChannelState RoomServerState { get; }

    internal LanConnectBasicChatPanel RichPanel { get; }

    internal LanConnectChatChannelState RichState { get; }

    internal LanConnectItemPreview MatrixPreview { get; }

    internal Rect2 ViewportRect => new(Vector2.Zero, _root.Size2DOverride);

    internal Vector2I PhysicalSize => _root.Size;

    internal Vector2I LogicalViewportSize => _root.Size2DOverride;

    internal float UiScale => _uiScale;

    internal int RichResolvedItemCount => RichMessageRuns()
        .Count(run => run.HasMeta("lan_connect_resolved_item"));

    internal int RichUnknownItemCount => RichMessageRuns()
        .Count(run => run is PanelContainer && !run.HasMeta("lan_connect_resolved_item"));

    internal string RichVisibleText => string.Join(
        "\n",
        RichPanel.FindChildren("*", "Label", recursive: true, owned: false)
            .OfType<Label>()
            .Select(label => label.Text));

    internal string RoomUnreadBadgeText => Find<Label>(Room, "RoomUnreadBadge").Text;

    internal string ServerUnreadBadgeText => Find<Label>(Room, "ServerUnreadBadge").Text;

    internal string ToggleUnreadBadgeText =>
        Find<Control>(Room, "ChatToggleUnreadBadge").GetChildOrNull<Label>(0) is Label label
            ? label.Text
            : string.Empty;

    internal static async Task<ChatUiFixture> Create(
        Vector2I physicalSize,
        float uiScale,
        bool useUiScaleOverride = true)
    {
        Vector2I logicalSize = LogicalSize(physicalSize, uiScale);
        LanConnectChatChannelState lobbyServer = PopulatedState(LanConnectChatChannel.Server, "lobby-server", 46);
        LanConnectChatChannelState roomServer = PopulatedState(LanConnectChatChannel.Server, "room-server", 36);
        LanConnectDualChatState dual = new(roomServer);
        dual.EnterRoom("resolution-room");
        Populate(dual.Room, "room", 46);

        SubViewport root = AutoFree(new SubViewport
        {
            Size = physicalSize,
            Size2DOverride = logicalSize,
            Size2DOverrideStretch = true,
            Disable3D = true,
            GuiEmbedSubwindows = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
            Snap2DTransformsToPixel = true,
            Snap2DVerticesToPixel = true
        })!;
        Theme fixedTheme = LoadFixedTestTheme();
        LanConnectLobbyOverlay lobby = new() { Visible = false };
        lobby.SetProcess(false);
        root.AddChild(lobby);
        LanConnectRoomChatOverlay room = new();
        root.AddChild(room);
        Control richSurface = new()
        {
            Name = "RichCompatibilitySurface",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Theme = fixedTheme
        };
        root.AddChild(richSurface);
        richSurface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        LanConnectChatChannelState richState = RichMatrixState();
        LanConnectBasicChatPanel richPanel = new(
            LanConnectChatUiComposition.Icons,
            ResolveMatrixItem)
        {
            Name = "RichCompatibilityPanel",
            ClipContents = true
        };
        richSurface.AddChild(richPanel);
        LanConnectItemPreview matrixPreview = new(new MatrixCardVisualFactory());
        richSurface.AddChild(matrixPreview);
        SizeRichPanel(richPanel, logicalSize);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.AwaitIdleFrame();

        Find<Control>(room, "RoomChatOverlayRoot").Theme = fixedTheme;
        richPanel.Theme = fixedTheme;
        richPanel.EmojiPickerForTests.Theme = fixedTheme;
        richPanel.ItemPreviewForTests.Theme = fixedTheme;
        matrixPreview.Theme = fixedTheme;

        lobby.ConfigureForTests(
            logicalSize,
            lobbyServer,
            Rooms(),
            send: _ => Task.CompletedTask,
            retry: _ => Task.CompletedTask,
            uiScale: useUiScaleOverride ? uiScale : null);
        room.ConfigureForTests(
            dual,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);
        richPanel.BindStructured(
            richState,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask);
        MatrixCombatContext matrixCombatContext = new();
        LanConnectRoomCombatReferenceResolver matrixCombatResolver = new(matrixCombatContext);
        richPanel.ConfigureCombatRendering(() => new LanConnectRoomCombatRenderContext(
            matrixCombatResolver,
            TranslationServer.GetLocale(),
            "fixed-mod-fingerprint",
            matrixCombatContext.ActiveRoomSessionId,
            "fixed-peer-directory",
            FreshReady: true));
        await room.OpenForTests();
        room.SelectChannelForTests(LanConnectChatChannel.Server);

        // Produce new-messages while visible, then use normal hidden-channel mechanics for both unread badges.
        room.SetScrollForTests(120, atBottom: false);
        roomServer.AppendConfirmedForTests("room-server-below", LongNickname, "new message below", 2000, false);
        roomServer.SetVisible(false);
        for (int index = 0; index < 10; index++)
        {
            roomServer.AppendConfirmedForTests($"server-unread-{index}", LongNickname, $"server unread {index}", 2100 + index, false);
        }
        for (int index = 0; index < 12; index++)
        {
            dual.Room.AppendConfirmedForTests($"room-unread-{index}", LongNickname, $"unread {index}", 1000 + index, false);
        }
        lobbyServer.SetScrollState(120, atBottom: false);
        lobbyServer.AppendConfirmedForTests("lobby-server-below", LongNickname, "lobby new message below", 3000, false);
        await room.RefreshForTests();
        await lobby.RefreshLayoutForTests(logicalSize);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        lobby.ScrollCompactSidebarToChatForTests();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        return new ChatUiFixture(
            root,
            runner,
            lobby,
            room,
            lobbyServer,
            roomServer,
            richSurface,
            richPanel,
            richState,
            matrixPreview,
            uiScale);
    }

    internal async Task Resize(Vector2I physicalSize, float uiScale)
    {
        Vector2I logicalSize = LogicalSize(physicalSize, uiScale);
        _root.Size = physicalSize;
        _root.Size2DOverride = logicalSize;
        _uiScale = uiScale;
        SizeRichPanel(RichPanel, logicalSize);
        await AwaitTwoFrames();
    }

    internal async Task TriggerRealResize(Vector2I physicalSize, float uiScale)
    {
        Vector2I target = LogicalSize(physicalSize, uiScale);
        _root.Size = physicalSize + Vector2I.One;
        _root.Size2DOverride = target + Vector2I.One;
        SizeRichPanel(RichPanel, target + Vector2I.One);
        await AwaitTwoFrames();
        _root.Size = physicalSize;
        _root.Size2DOverride = target;
        _uiScale = uiScale;
        SizeRichPanel(RichPanel, target);
        await AwaitTwoFrames();
    }

    internal async Task ShowRichMatrix()
    {
        _richSurface.Visible = true;
        await RichPanel.RefreshForTests();
        await AwaitTwoFrames();
    }

    internal async Task ShowRichMatrixOnly()
    {
        Lobby.Visible = false;
        Room.Visible = false;
        await ShowRichMatrix();
    }

    internal async Task ShowRoomMatrixOnly()
    {
        Lobby.Visible = false;
        _richSurface.Visible = false;
        Room.Visible = true;
        await Room.RefreshForTests();
        await AwaitTwoFrames();
    }

    internal async Task ShowReducedMotionFadeOverRich()
    {
        Lobby.Visible = false;
        LanConnectChatChannelState fadeServer = new(LanConnectChatChannel.Server);
        fadeServer.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            Channel = LanConnectChatChannel.Server,
            ServerChatVersion = 1,
            InstanceId = "screenshot-fade",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures()
        });
        fadeServer.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        LanConnectDualChatState fade = new(fadeServer);
        fade.EnterRoom("screenshot-fade-room");
        Room.ConfigureForTests(
            fade,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);
        await Room.OpenForTests();
        ScreenshotClock clock = new();
        Room.ConfigureFadeForTests(clock, () => true);
        clock.NowSeconds = LanConnectRoomOverlayFadeController.IdleDelaySeconds;
        Room.RefreshFadeForTests();
        _richSurface.Visible = true;
        Room.Visible = true;
        await RichPanel.RefreshForTests();
        await AwaitTwoFrames();
    }

    internal void DisableCaretBlink()
    {
        foreach (LineEdit input in _root.FindChildren("*", "LineEdit", recursive: true, owned: false)
                     .OfType<LineEdit>())
        {
            input.CaretBlink = false;
        }
        foreach (TextEdit input in _root.FindChildren("*", "TextEdit", recursive: true, owned: false)
                     .OfType<TextEdit>())
        {
            input.CaretBlink = false;
        }
        FreezeDynamicProcessing(RichPanel);
        FreezeDynamicProcessing(Room);
        FreezeDynamicProcessing(MatrixPreview);
        FreezeDynamicProcessing(RichPanel.EmojiPickerForTests);
    }

    internal Control[] RichMessageRuns() => RichPanel
        .FindChildren("ChatMessageRun*", "Control", recursive: true, owned: false)
        .OfType<Control>()
        .ToArray();

    internal Control[] RichEditorRuns() => RichPanel
        .FindChildren("*", "Control", recursive: true, owned: false)
        .OfType<Control>()
        .Where(control =>
            control.Name == LanConnectConstants.ChatDraftInputName ||
            control.Name.ToString().StartsWith(LanConnectConstants.ChatDraftRunPrefix, StringComparison.Ordinal) ||
            control.Name.ToString().StartsWith(LanConnectConstants.ChatEntityChipPrefix, StringComparison.Ordinal))
        .ToArray();

    internal async Task<Button[]> OpenRichEmojiPicker()
    {
        RichPanel.FocusDraft();
        await AwaitTwoFrames();
        Find<Button>(RichPanel, LanConnectEmojiPicker.ToggleButtonName)
            .EmitSignal(Button.SignalName.Pressed);
        await AwaitTwoFrames();
        return RichPanel.FindChildren(
                LanConnectEmojiPicker.ButtonPrefix + "*",
                "Button",
                recursive: true,
                owned: false)
            .OfType<Button>()
            .ToArray();
    }

    internal async Task CloseRichEmojiPicker()
    {
        RichPanel.CloseEmojiPicker(restoreDraftFocus: false);
        await AwaitTwoFrames();
    }

    internal async Task<LanConnectItemPreviewTestState> ShowPreview(string itemType)
    {
        LanConnectItemPreviewData preview = itemType == "card"
            ? new LanConnectCardPreviewData(new object(), 1)
            : new LanConnectHoverTipPreviewData(
                itemType,
                itemType == "relic" ? "Anchor" : "Fire Potion",
                "A deliberately long preview description that must wrap inside the viewport.",
                null);
        MatrixPreview.ShowResolved(
            new LanConnectResolvedItem(
                LanConnectResolvedItemStatus.Resolved,
                itemType,
                "chat.item." + itemType,
                itemType,
                itemType,
                preview),
            new Rect2(ViewportRect.End - new Vector2(36, 36), new Vector2(24, 24)),
            ViewportRect);
        await AwaitTwoFrames();
        return MatrixPreview.TestState;
    }

    internal async Task ClosePreview()
    {
        MatrixPreview.ClosePreview();
        await AwaitTwoFrames();
    }

    internal IReadOnlyList<Control> AccessibilityStateControls()
    {
        List<Control> controls = RichMessageRuns()
            .Where(run => run is not Label)
            .ToList();
        controls.AddRange(RichPanel.FindChildren("*", "Button", recursive: true, owned: false)
            .OfType<Button>()
            .Where(button => button.Disabled));
        controls.AddRange(Room.FindChildren("RoomChatTab", "Button", recursive: true, owned: false)
            .OfType<Button>());
        return controls;
    }

    internal Label[] LongNameLabels() => new Node[]
        {
            Lobby.ServerChatPanelForTests,
            Room.ChatPanelForTests,
            RichPanel
        }
        .SelectMany(root => root.FindChildren("*", "Label", recursive: true, owned: false)
            .OfType<Label>())
        .Where(label => label.Text.Contains(LongNickname, StringComparison.Ordinal))
        .ToArray();

    internal async Task CloseRichSurfaces()
    {
        RichPanel.CloseEmojiPicker(restoreDraftFocus: false);
        MatrixPreview.ClosePreview();
        _richSurface.Visible = false;
        Room.Visible = true;
        await AwaitTwoFrames();
    }

    internal async Task AssertRealTabFocusOrder(string context)
    {
        bool lobbyVisible = Lobby.Visible;
        Lobby.Visible = false;
        try
        {
            LanConnectChatChannelState focusServer = PopulatedState(
                LanConnectChatChannel.Server,
                "focus-server",
                confirmedCount: 0);
            LanConnectDualChatState focus = new(focusServer);
            focus.EnterRoom("focus-room");
            focus.Room.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 0));
            focus.Room.SetDraft("focus message");
            focus.Room.BeginPendingText("focus-retry", LongNickname, "failed focus message");
            focus.Room.MarkFailed("focus-retry", "send_failed", "offline");
            Room.ConfigureForTests(
                focus,
                (_, _) => Task.CompletedTask,
                (_, _) => Task.CompletedTask);
            await Room.OpenForTests();
            Room.SelectChannelForTests(LanConnectChatChannel.Room);
            await AwaitTwoFrames();
            Room.FocusFirstForTests();
            await AwaitTwoFrames();
            Control roomTab = Find<Control>(Room, "RoomChatTab");
            Control focusNext = roomTab.GetNodeOrNull<Control>(roomTab.FocusNext) ??
                                throw new InvalidOperationException(
                                    $"{context} RoomChatTab.FocusNext does not resolve: {roomTab.FocusNext}");
            if (focusNext.Name != "ServerChatTab")
            {
                throw new InvalidOperationException(
                    $"{context} RoomChatTab.FocusNext resolved to {focusNext.GetPath()}");
            }

            string[] expected =
            {
                "RoomChatTab",
                "ServerChatTab",
                LanConnectConstants.ChatMessagesScrollName,
                LanConnectConstants.ChatDraftInputName,
                LanConnectEmojiPicker.ToggleButtonName,
                LanConnectConstants.ChatSendButtonName,
                LanConnectConstants.ChatRetryButtonPrefix + "focus-retry",
                "ChatPinButton"
            };
            foreach (string name in expected)
            {
                string actual = Room.TestState.FocusOwnerName;
                if (!string.Equals(actual, name, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{context} tab order expected {name} but focused {actual} " +
                        $"at {_root.GuiGetFocusOwner()?.GetPath()}");
                }
                Control focused = _root.GuiGetFocusOwner() as Control ??
                                  throw new InvalidOperationException($"{context} has no focus owner");
                Rect2 rectBefore = focused.GetGlobalRect();
                AssertRectInsideMatrix(rectBefore, ViewportRect, $"{context} focus {name}");
                PushTab(_root);
                await AwaitTwoFrames();
            }
        }
        finally
        {
            Lobby.Visible = lobbyVisible;
            await AwaitTwoFrames();
        }
    }

    internal async Task AwaitTwoFrames()
    {
        await _runner.AwaitIdleFrame();
        await _runner.AwaitIdleFrame();
    }

    internal async Task<Image> CaptureImage()
    {
        await _runner.AwaitIdleFrame();
        await _runner.AwaitIdleFrame();
        TaskCompletionSource frameDrawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFramePostDraw() => frameDrawn.TrySetResult();
        RenderingServer.FramePostDraw += OnFramePostDraw;
        try
        {
            RenderingServer.ForceDraw();
            await frameDrawn.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            RenderingServer.FramePostDraw -= OnFramePostDraw;
        }
        Image image = _root.GetTexture().GetImage();
        if (image.GetWidth() != PhysicalSize.X || image.GetHeight() != PhysicalSize.Y)
        {
            throw new InvalidOperationException(
                $"capture size {image.GetWidth()}x{image.GetHeight()} != {PhysicalSize.X}x{PhysicalSize.Y}");
        }
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }
        return image;
    }

    internal IReadOnlyList<string> OverlappingNamedControls()
    {
        List<string> overlaps = new();
        AddPairwiseOverlaps(
            overlaps,
            "room",
            Room.TestState.FocusTargetRects,
            Room.TestState.MessagesRect);
        AddPairwiseOverlaps(
            overlaps,
            "lobby",
            Lobby.TestState.ServerChatPanelState.FocusTargetRects,
            Lobby.TestState.ServerChatPanelState.MessagesRect);
        return overlaps;
    }

    internal IReadOnlyList<string> RoomRetryNames() => RetryNames(Room);

    internal IReadOnlyList<string> LobbyRetryNames() => RetryNames(Lobby.ServerChatPanelForTests);

    internal IReadOnlyList<string> VisibleControlsAt(Vector2 logicalPoint) => _root
        .FindChildren("*", "Control", recursive: true, owned: false)
        .OfType<Control>()
        .Where(control => control.IsVisibleInTree() && control.GetGlobalRect().HasPoint(logicalPoint))
        .Select(control => control.GetPath().ToString())
        .ToArray();

    internal Control FindRoomControl(string name) => Find<Control>(Room, name);

    internal Control FindLobbyControl(string name) => Find<Control>(Lobby.ServerChatPanelForTests, name);

    public void Dispose() => _runner.Dispose();

    private static LanConnectChatChannelState RichMatrixState()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        state.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 1));
        state.RichDraft.ReplaceAllWithText("multiline draft\nsecond line");
        state.RichDraft.InsertEntity(new LanConnectEmojiRun("heart"));
        state.RichDraft.InsertEntity(new LanConnectItemRun("card", "MegaCrit.Strike", 1));
        state.RichDraft.InsertEntity(new LanConnectItemRun("relic", "MegaCrit.Anchor"));
        state.AppendConfirmedContentForTests(
            "rich-matrix-message",
            LongNickname,
            new LanConnectChatContent(1,
            [
                new LanConnectTextSegment("mixed entities\nwrapped line "),
                new LanConnectEmojiSegment("smile"),
                new LanConnectItemRefSegment("card", "MegaCrit.Strike", 1),
                new LanConnectItemRefSegment("relic", "PrivateMod.MissingRelic"),
                new LanConnectItemRefSegment("relic", "MegaCrit.Anchor"),
                new LanConnectItemRefSegment("potion", "MegaCrit.FirePotion"),
                new LanConnectPowerStateSegment("MegaCrit.Strength", 2, "generation-a"),
                new LanConnectItemRefSegment("card", "PrivateMod.MissingCard"),
                new LanConnectItemRefSegment("potion", "PrivateMod.MissingPotion"),
                new LanConnectTargetRefSegment("player", "net:watcher", "generation-a"),
                new LanConnectItemRefSegment("card", "MegaCrit.Defend", 0),
                new LanConnectItemRefSegment("relic", "MegaCrit.BagOfPreparation"),
                new LanConnectTargetRefSegment("monster", "prototype:stale", "generation-b")
            ]),
            sequence: 1,
            isLocal: false);
        return state;
    }

    private static LanConnectResolvedItem ResolveMatrixItem(LanConnectItemRun run)
    {
        if (run.ModelId.StartsWith("PrivateMod.", StringComparison.Ordinal))
        {
            string text = run.ItemType switch
            {
                "card" => "Unknown card",
                "relic" => "Unknown relic",
                "potion" => "Unknown potion",
                _ => "Unknown item"
            };
            return new LanConnectResolvedItem(
                LanConnectResolvedItemStatus.Unknown,
                run.ItemType,
                "chat.unknown_" + run.ItemType,
                null,
                text,
                null);
        }

        string title = run.ItemType switch
        {
            "card" => run.UpgradeLevel > 0 ? "Strike+1" : "Defend",
            "relic" => run.ModelId.EndsWith("Anchor", StringComparison.Ordinal)
                ? "Anchor"
                : "Bag of Preparation",
            "potion" => run.ModelId.EndsWith("FirePotion", StringComparison.Ordinal)
                ? "Fire Potion"
                : "Block Potion",
            _ => "Item"
        };
        LanConnectItemPreviewData preview = run.ItemType == "card"
            ? new LanConnectCardPreviewData(new object(), run.UpgradeLevel ?? 0)
            : new LanConnectHoverTipPreviewData(run.ItemType, title, "Preview description", null);
        return new LanConnectResolvedItem(
            LanConnectResolvedItemStatus.Resolved,
            run.ItemType,
            "chat.item." + run.ItemType,
            title,
            title,
            preview);
    }

    private static void SizeRichPanel(LanConnectBasicChatPanel panel, Vector2I viewport)
    {
        panel.Position = new Vector2(8, 8);
        panel.Size = new Vector2(
            Math.Max(1, viewport.X - 16),
            Math.Max(1, viewport.Y - 16));
    }

    private static void AssertRectInsideMatrix(Rect2 rect, Rect2 viewport, string name)
    {
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f ||
            rect.Position.X < viewport.Position.X || rect.Position.Y < viewport.Position.Y ||
            rect.End.X > viewport.End.X || rect.End.Y > viewport.End.Y)
        {
            throw new InvalidOperationException($"{name}: rect {rect} is outside viewport {viewport}");
        }
    }

    private static void PushTab(Viewport viewport)
    {
        viewport.PushInput(new InputEventKey
        {
            Keycode = Key.Tab,
            PhysicalKeycode = Key.Tab,
            Pressed = true,
            Echo = false
        });
        viewport.PushInput(new InputEventKey
        {
            Keycode = Key.Tab,
            PhysicalKeycode = Key.Tab,
            Pressed = false,
            Echo = false
        });
    }

    private static LanConnectChatChannelState PopulatedState(
        LanConnectChatChannel channel,
        string prefix,
        int confirmedCount)
    {
        LanConnectChatChannelState state = new(channel);
        if (channel == LanConnectChatChannel.Server)
        {
            state.Apply(new ServerChatInboundEnvelope
            {
                Type = "chat_ready",
                Channel = channel,
                ServerChatVersion = 1,
                InstanceId = "resolution-tests",
                HistoryEpoch = 1,
                ChatEnabled = true,
                EnabledFeatures = new ServerChatEnabledFeatures()
            });
            state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        }
        Populate(state, prefix, confirmedCount);
        return state;
    }

    private static void Populate(LanConnectChatChannelState state, string prefix, int confirmedCount)
    {
        DateTimeOffset now = new(2026, 7, 16, 8, 0, 0, TimeSpan.Zero);
        state.BeginPendingText($"{prefix}-pending", LongNickname, "pending message\nwith two lines", queuedAt: now);
        state.BeginPendingText($"{prefix}-failed", LongNickname, "failed message", queuedAt: now);
        state.MarkFailed($"{prefix}-failed", "send_failed", "offline");
        state.BeginPendingText($"{prefix}-unknown", LongNickname, "unknown message", queuedAt: now - TimeSpan.FromSeconds(20));
        state.MarkTimedOut(now);

        for (int index = 0; index < confirmedCount; index++)
        {
            state.AppendConfirmedForTests(
                $"{prefix}-confirmed-{index}",
                index == 0 ? LongNickname : $"Player {index}",
                index == 1 ? "first line\nsecond line with wrapped content" : $"message {index}",
                index + 1,
                false);
        }
    }

    private static IReadOnlyList<LobbyRoomSummary> Rooms() =>
    [
        new LobbyRoomSummary
        {
            RoomId = "resolution-room",
            RoomName = "Resolution Fixture Room",
            HostPlayerName = LongNickname,
            CurrentPlayers = 3,
            MaxPlayers = 4,
            GameMode = "standard",
            Version = "1.0",
            ModVersion = "0.4.0",
            Status = "waiting"
        }
    ];

    private static Vector2I LogicalSize(Vector2I physicalSize, float uiScale) => new(
        Mathf.Max(1, Mathf.FloorToInt(physicalSize.X / uiScale)),
        Mathf.Max(1, Mathf.FloorToInt(physicalSize.Y / uiScale)));

    private static T Find<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);

    private static IReadOnlyList<string> RetryNames(Node root) => root
        .FindChildren(LanConnectConstants.ChatRetryButtonPrefix + "*", "Button", recursive: true, owned: false)
        .Select(node => node.Name.ToString())
        .ToArray();

    private static void AddPairwiseOverlaps(
        List<string> overlaps,
        string surface,
        IReadOnlyList<LanConnectNamedControlRect> controls,
        Rect2 messagesRect)
    {
        LanConnectNamedControlRect[] peers = controls
            .Select(control => control.Name.StartsWith(
                    LanConnectConstants.ChatRetryButtonPrefix,
                    StringComparison.Ordinal)
                ? control with { Rect = control.Rect.Intersection(messagesRect) }
                : control)
            .ToArray();
        for (int first = 0; first < peers.Length; first++)
        {
            for (int second = first + 1; second < peers.Length; second++)
            {
                bool legalMessagesRetryContainment =
                    (peers[first].Name == LanConnectConstants.ChatMessagesScrollName &&
                     peers[second].Name.StartsWith(LanConnectConstants.ChatRetryButtonPrefix, StringComparison.Ordinal) ||
                     peers[second].Name == LanConnectConstants.ChatMessagesScrollName &&
                     peers[first].Name.StartsWith(LanConnectConstants.ChatRetryButtonPrefix, StringComparison.Ordinal));
                if (!legalMessagesRetryContainment &&
                    peers[first].Rect.Intersects(peers[second].Rect, includeBorders: false))
                {
                    overlaps.Add($"{surface}: {peers[first].Name}/{peers[second].Name}");
                }
            }
        }
    }

    private sealed class MatrixCardVisualFactory : ILanConnectCardPreviewVisualFactory
    {
        public Control Create(object card) => new PanelContainer
        {
            CustomMinimumSize = new Vector2(180, 250),
            AccessibilityName = "Card preview"
        };
    }

    private sealed class MatrixCombatContext : ILanConnectRoomCombatContext
    {
        public string ActiveRoomSessionId => "generation-a";

        public bool IsCurrentPeer(string playerNetId) => playerNetId == "net:watcher";

        public bool TryGetCurrentPeerName(string playerNetId, out string name)
        {
            name = playerNetId == "net:watcher" ? "Watcher" : string.Empty;
            return name.Length > 0;
        }

        public bool TryResolveLocalPower(string modelId, out LanConnectLocalPowerReference power)
        {
            power = new LanConnectLocalPowerReference("Strength", "Gain attack damage.");
            return modelId == "MegaCrit.Strength";
        }
    }

    private sealed class ScreenshotClock : ILanConnectMonotonicClock
    {
        public double NowSeconds { get; set; }
    }

    private static Theme LoadFixedTestTheme()
    {
        FontFile font = GD.Load<FontFile>(
            "res://TestAssets/Fonts/ark-pixel-10px-proportional-zh_cn.otf") ??
            throw new InvalidOperationException("Fixed Ark Pixel screenshot font failed to load.");
        return new Theme { DefaultFont = font };
    }

    private static void FreezeDynamicProcessing(Node node)
    {
        node.SetProcess(false);
        node.SetPhysicsProcess(false);
        foreach (Node child in node.GetChildren())
        {
            FreezeDynamicProcessing(child);
        }
    }
}
