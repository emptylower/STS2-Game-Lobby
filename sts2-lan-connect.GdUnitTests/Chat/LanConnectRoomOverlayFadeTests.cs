using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomOverlayFadeTests
{
    [TestCase]
    public async Task Exact_idle_edge_starts_normal_tween_without_changing_layout_or_visibility()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock);
        LanConnectRoomChatOverlayTestState before = fixture.Overlay.TestState;
        float chatPanelAlpha = fixture.Overlay.ChatPanelForTests.Modulate.A;

        clock.NowSeconds = 4.999d;
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(1f);
        AssertThat(fixture.Overlay.TestState.TweenActive).IsFalse();

        clock.NowSeconds = 5d;
        fixture.Overlay.RefreshFadeForTests();
        LanConnectRoomChatOverlayTestState fading = fixture.Overlay.TestState;

        AssertThat(fading.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Fading);
        AssertThat(fading.TweenActive).IsTrue();
        AssertThat(fading.PanelVisible).IsTrue();
        AssertThat(fading.PanelRect).IsEqual(before.PanelRect);
        AssertThat(fixture.Overlay.ChatPanelForTests.Modulate.A).IsEqual(chatPanelAlpha);
        AssertThat(fading.HintAlpha).IsEqual(1f);
        AssertThat(fading.HintText).Contains("0");

        await AwaitTweenStopped(fixture);
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqualApprox(0f, 0.02f);
        AssertThat(fixture.Overlay.TestState.PanelVisible).IsTrue();
        AssertThat(fixture.Overlay.TestState.PanelRect).IsEqual(before.PanelRect);
    }

    [TestCase]
    public async Task Reduced_motion_fades_immediately_without_creating_a_tween()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => true);
        clock.NowSeconds = 5d;

        fixture.Overlay.RefreshFadeForTests();
        LanConnectRoomChatOverlayTestState state = fixture.Overlay.TestState;

        AssertThat(state.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Faded);
        AssertThat(state.PanelAlpha).IsEqual(0f);
        AssertThat(state.TweenActive).IsFalse();
        AssertThat(state.HintAlpha).IsEqual(1f);
        AssertThat(FindNode<Label>(fixture.Overlay, "RoomChatFadeHint").Visible).IsTrue();
        AssertThat(FindNode<Label>(fixture.Overlay, "RoomChatFadeHint").AccessibilityName)
            .IsEqual(state.HintText);
    }

    [TestCase]
    public async Task Preference_failure_falls_back_to_reduced_motion()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => throw new InvalidOperationException("prefs unavailable"));
        clock.NowSeconds = 5d;

        fixture.Overlay.RefreshFadeForTests();

        AssertThat(fixture.Overlay.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Faded);
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(0f);
        AssertThat(fixture.Overlay.TestState.TweenActive).IsFalse();
    }

    [TestCase]
    public async Task Toggle_focus_counts_as_overlay_focus_and_blocks_fade()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => true);
        fixture.Overlay.FocusToggleForTests();
        await fixture.Runner.AwaitIdleFrame();
        clock.NowSeconds = 5d;

        fixture.Overlay.RefreshFadeForTests();

        AssertThat(fixture.Overlay.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Awake);
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(1f);
        AssertThat(fixture.Overlay.TestState.TweenActive).IsFalse();
    }

    [TestCase]
    public async Task Activity_root_hover_and_selected_bottom_incoming_each_wake_and_kill_fade()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => true);
        clock.NowSeconds = 5d;
        fixture.Overlay.RefreshFadeForTests();

        fixture.Overlay.SignalFadeActivityForTests();
        AssertVisibleWaiting(fixture.Overlay.TestState);

        clock.NowSeconds = 10d;
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(0f);
        FindNode<Control>(fixture.Overlay, "RoomChatOverlayRoot")
            .EmitSignal(Control.SignalName.MouseEntered);
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(1f);
        AssertThat(fixture.Overlay.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Awake);
        FindNode<Control>(fixture.Overlay, "RoomChatOverlayRoot")
            .EmitSignal(Control.SignalName.MouseExited);

        clock.NowSeconds = 15d;
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(0f);
        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Room, sequence: 6732);
        fixture.Overlay.RefreshFadeForTests();

        AssertVisibleWaiting(fixture.Overlay.TestState);
        AssertThat(fixture.Overlay.TestState.RoomUnread).IsEqual(0);
        AssertThat(fixture.Overlay.TestState.NewMessagesBelowCount).IsEqual(0);
    }

    [TestCase]
    public async Task F8_and_enter_attempts_kill_an_active_normal_tween_same_frame()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock);
        clock.NowSeconds = 5d;
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.TweenActive).IsTrue();

        fixture.Overlay.RouteKeyForTests(Key.F8, blockingModalVisible: true);
        AssertVisibleWaiting(fixture.Overlay.TestState);
        await AwaitFrames(fixture.Runner, 60);
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(1f);

        clock.NowSeconds = 10d;
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.TweenActive).IsTrue();
        fixture.Overlay.RouteKeyForTests(Key.Enter, blockingModalVisible: true);
        AssertVisibleWaiting(fixture.Overlay.TestState);
        await AwaitFrames(fixture.Runner, 60);
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(1f);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Empty_state_server_remote_sequence_6732_does_not_wake_room_overlay(
        bool serverSupported)
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        if (!serverSupported)
        {
            fixture.State.Server.SetPresentationForTests(LanConnectServerChatPresentation.Unsupported);
            await fixture.Overlay.RefreshForTests();
        }
        fixture.State.Server.SetVisible(true);
        fixture.State.Server.SetScrollState(0, atBottom: true);
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => true);
        clock.NowSeconds = 5d;
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(0f);

        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Server, sequence: 6732);
        fixture.Overlay.RefreshFadeForTests();

        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(0f);
        AssertThat(fixture.Overlay.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Faded);
        AssertThat(fixture.State.Server.UnreadCount).IsEqual(0);
        AssertThat(fixture.State.Server.NewMessagesBelowCount).IsEqual(0);
        AssertThat(fixture.State.Server.RemoteArrivalRevision).IsEqual(1);
    }

    [TestCase]
    public async Task Room_new_below_wakes_and_hint_excludes_server_unread()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => true);
        clock.NowSeconds = 5d;
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.HintText).Contains("0");

        fixture.State.Room.SetScrollState(0, atBottom: false);
        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Room, sequence: 101);
        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Server, sequence: 102);
        fixture.Overlay.RefreshFadeForTests();
        LanConnectRoomChatOverlayTestState state = fixture.Overlay.TestState;

        AssertThat(state.PanelAlpha).IsEqual(1f);
        AssertThat(state.HintAlpha).IsEqual(0f);
        AssertThat(state.NewMessagesBelowCount).IsEqual(1);
        AssertThat(state.ServerUnread).IsEqual(1);
        AssertThat(state.HintText).Contains("1");
    }

    [TestCase("pending")]
    [TestCase("failed")]
    [TestCase("delivery-unknown")]
    public async Task Each_delivery_blocker_wakes_and_kills_an_active_fade(string delivery)
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock);
        clock.NowSeconds = 5d;
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.TweenActive).IsTrue();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        fixture.State.Room.BeginPendingText("fade-blocker", "Me", delivery, queuedAt: now - TimeSpan.FromSeconds(20));
        if (delivery == "failed")
        {
            fixture.State.Room.MarkFailed("fade-blocker", "failed", "offline");
        }
        else if (delivery == "delivery-unknown")
        {
            fixture.State.Room.MarkTimedOut(now);
        }
        fixture.Overlay.RefreshFadeForTests();

        AssertThat(fixture.Overlay.TestState.PanelAlpha).IsEqual(1f);
        AssertThat(fixture.Overlay.TestState.TweenActive).IsFalse();
        AssertThat(fixture.Overlay.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Awake);
    }

    [TestCase]
    public async Task Picker_preview_mouse_signals_and_real_drag_each_wake_and_block_fade()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        EnableRichEmoji(fixture.State.Room);
        await fixture.Overlay.RefreshForTests();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock, () => true);
        clock.NowSeconds = 5d;
        fixture.Overlay.RefreshFadeForTests();

        FindNode<Button>(fixture.Overlay, LanConnectEmojiPicker.ToggleButtonName)
            .EmitSignal(Button.SignalName.Pressed);
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.ChatPanelForTests.EmojiPickerVisible).IsTrue();
        AssertAwakeVisible(fixture.Overlay.TestState);
        fixture.Overlay.ChatPanelForTests.CloseTransientUi(restoreFocus: false);
        await AwaitFrames(fixture.Runner, 2);
        fixture.Overlay.RefreshFadeForTests();

        clock.NowSeconds = 10d;
        fixture.Overlay.RefreshFadeForTests();
        ShowPreview(fixture.Overlay.ChatPanelForTests);
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.ChatPanelForTests.PreviewVisible).IsTrue();
        AssertAwakeVisible(fixture.Overlay.TestState);
        fixture.Overlay.ChatPanelForTests.CloseTransientUi(restoreFocus: false);
        await AwaitFrames(fixture.Runner, 2);
        fixture.Overlay.RefreshFadeForTests();

        clock.NowSeconds = 15d;
        fixture.Overlay.RefreshFadeForTests();
        Control root = FindNode<Control>(fixture.Overlay, "RoomChatOverlayRoot");
        root.EmitSignal(Control.SignalName.MouseEntered);
        AssertAwakeVisible(fixture.Overlay.TestState);
        root.EmitSignal(Control.SignalName.MouseExited);

        clock.NowSeconds = 20d;
        fixture.Overlay.RefreshFadeForTests();
        Button toggle = FindNode<Button>(fixture.Overlay, "RoomChatToggleButton");
        toggle.EmitSignal(
            Control.SignalName.GuiInput,
            new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = toggle.GetGlobalRect().GetCenter()
            });
        fixture.Overlay.RefreshFadeForTests();
        AssertAwakeVisible(fixture.Overlay.TestState);
        toggle.EmitSignal(
            Control.SignalName.GuiInput,
            new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = toggle.GetGlobalRect().GetCenter()
            });
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Waiting);
    }

    [TestCase("close")]
    [TestCase("leave")]
    public async Task Close_or_leave_clears_preview_and_delivery_confirmation_before_reopen(
        string lifecycle)
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        int sends = 0;
        fixture.Overlay.ConfigureForTests(
            fixture.State,
            (_, _) =>
            {
                sends++;
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask);
        ConfirmationDialog confirmation = await ShowConfirmationAndPreview(fixture);

        if (lifecycle == "close")
        {
            await fixture.Overlay.CloseForTests();
            await fixture.Overlay.OpenForTests();
        }
        else
        {
            await fixture.Overlay.LeaveRoomForTests();
            fixture.State.EnterRoom("room-after-leave");
            await fixture.Overlay.OpenForTests();
        }

        AssertThat(fixture.Overlay.ChatPanelForTests.PreviewVisible).IsFalse();
        AssertThat(fixture.Overlay.ChatPanelForTests.ConfirmationPopupVisible).IsFalse();
        confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(sends).IsEqual(0);
    }

    [TestCase]
    public async Task Exit_tree_closes_preview_and_confirmation_and_invalidates_pending_action()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        int sends = 0;
        fixture.Overlay.ConfigureForTests(
            fixture.State,
            (_, _) =>
            {
                sends++;
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask);
        ConfirmationDialog confirmation = await ShowConfirmationAndPreview(fixture);
        LanConnectBasicChatPanel panel = fixture.Overlay.ChatPanelForTests;

        fixture.Overlay.GetParent().RemoveChild(fixture.Overlay);

        AssertThat(panel.PreviewVisible).IsFalse();
        AssertThat(panel.ConfirmationPopupVisible).IsFalse();
        confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        AssertThat(sends).IsEqual(0);
        fixture.Overlay.Free();
        await AwaitFrames(fixture.Runner, 2);
    }

    [TestCase("close")]
    [TestCase("leave")]
    [TestCase("exit")]
    public async Task Lifecycle_kills_normal_tween_and_it_cannot_write_back_later(string lifecycle)
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        fixture.Overlay.ConfigureFadeForTests(clock);
        clock.NowSeconds = 5d;
        fixture.Overlay.RefreshFadeForTests();
        AssertThat(fixture.Overlay.TestState.TweenActive).IsTrue();
        PanelContainer frame = FindNode<PanelContainer>(fixture.Overlay, "RoomChatPanelFrame");
        if (lifecycle == "close")
        {
            await fixture.Overlay.CloseForTests();
        }
        else if (lifecycle == "leave")
        {
            await fixture.Overlay.LeaveRoomForTests();
        }
        else
        {
            fixture.Overlay.GetParent().RemoveChild(fixture.Overlay);
        }
        float alphaAfterLifecycle = frame.Modulate.A;
        await AwaitFrames(fixture.Runner, 60);

        AssertThat(frame.Modulate.A).IsEqual(alphaAfterLifecycle);
        if (lifecycle != "exit")
        {
            AssertThat(frame.Modulate.A).IsEqual(1f);
            AssertThat(fixture.Overlay.TestState.TweenActive).IsFalse();
        }
        else
        {
            fixture.Overlay.Free();
            await AwaitFrames(fixture.Runner, 2);
        }
    }

    [TestCase]
    public async Task Localized_three_digit_hint_stays_inside_compact_scaled_layout_without_toggle_overlap()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport(
            new Vector2I(1280, 720),
            uiScale: 1.5f);
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        for (int index = 0; index < 100; index++)
        {
            fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Room, sequence: index + 1);
        }
        string originalLocale = TranslationServer.GetLocale();
        try
        {
            TranslationServer.SetLocale("en");
            fixture.Overlay.RefreshFadeForTests();
            AssertThat(fixture.Overlay.TestState.HintText).IsEqual("Chat · 100 unread");
            AssertHintLayout(fixture.Overlay.TestState);

            TranslationServer.SetLocale("zh_CN");
            fixture.Overlay.RefreshFadeForTests();
            AssertThat(fixture.Overlay.TestState.HintText).IsEqual("聊天 · 100 条未读");
            AssertHintLayout(fixture.Overlay.TestState);
        }
        finally
        {
            TranslationServer.SetLocale(originalLocale);
        }
    }

    [TestCase]
    public async Task Room_fade_does_not_change_homepage_server_panel_opacity()
    {
        using LobbyOverlayFixture lobby = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        float serverAlpha = lobby.Overlay.ServerChatPanelForTests.Modulate.A;
        using RoomChatFixture room = await RoomChatFixture.OpenWithServerSupport();
        FakeClock clock = new();
        room.Overlay.ConfigureFadeForTests(clock, () => true);
        clock.NowSeconds = 5d;

        room.Overlay.RefreshFadeForTests();

        AssertThat(room.Overlay.TestState.PanelAlpha).IsEqual(0f);
        AssertThat(lobby.Overlay.ServerChatPanelForTests.Modulate.A).IsEqual(serverAlpha);
    }

    private static void AssertVisibleWaiting(LanConnectRoomChatOverlayTestState state)
    {
        AssertThat(state.PanelAlpha).IsEqual(1f);
        AssertThat(state.TweenActive).IsFalse();
        AssertThat(state.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Waiting);
        AssertThat(state.HintAlpha).IsEqual(0f);
    }

    private static void AssertAwakeVisible(LanConnectRoomChatOverlayTestState state)
    {
        AssertThat(state.PanelAlpha).IsEqual(1f);
        AssertThat(state.TweenActive).IsFalse();
        AssertThat(state.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Awake);
        AssertThat(state.HintAlpha).IsEqual(0f);
    }

    private static async Task<ConfirmationDialog> ShowConfirmationAndPreview(RoomChatFixture fixture)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        fixture.State.Room.BeginPendingText(
            "transient-confirmation",
            "Me",
            "possibly sent",
            queuedAt: now - TimeSpan.FromSeconds(20));
        fixture.State.Room.MarkTimedOut(now);
        fixture.State.Room.MarkDisconnected();
        await fixture.Overlay.RefreshForTests();
        await fixture.Runner.AwaitIdleFrame();
        FindNode<Button>(
                fixture.Overlay,
                LanConnectConstants.ChatRetryButtonPrefix + "transient-confirmation")
            .EmitSignal(Button.SignalName.Pressed);
        await fixture.Runner.AwaitIdleFrame();
        ConfirmationDialog confirmation = FindNode<ConfirmationDialog>(
            fixture.Overlay,
            "DisconnectedUnknownConfirmation");
        ShowPreview(fixture.Overlay.ChatPanelForTests);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(confirmation.Visible).IsTrue();
        AssertThat(fixture.Overlay.ChatPanelForTests.PreviewVisible).IsTrue();
        return confirmation;
    }

    private static void ShowPreview(LanConnectBasicChatPanel panel)
    {
        panel.ItemPreviewForTests.ShowResolved(
            new LanConnectResolvedItem(
                LanConnectResolvedItemStatus.Resolved,
                "relic",
                "chat.item.relic",
                "Test relic",
                "Test relic",
                new LanConnectHoverTipPreviewData(
                    "relic",
                    "Test relic",
                    "Preview description",
                    null)),
            new Rect2(new Vector2(100, 100), new Vector2(20, 20)),
            panel.GetViewport().GetVisibleRect());
    }

    private static void EnableRichEmoji(LanConnectChatChannelState state)
    {
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            Channel = state.Channel,
            ServerChatVersion = 1,
            InstanceId = "fade-picker-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures
            {
                RichContentVersion = 1,
                EmojiSetVersion = 1
            }
        });
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
    }

    private static async Task AwaitTweenStopped(RoomChatFixture fixture)
    {
        const int maxFrames = 180;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (!fixture.Overlay.TestState.TweenActive)
            {
                return;
            }
            await fixture.Runner.AwaitIdleFrame();
        }
        throw new TimeoutException($"Fade tween was still active after {maxFrames} frames.");
    }

    private static async Task AwaitFrames(ISceneRunner runner, int count)
    {
        for (int frame = 0; frame < count; frame++)
        {
            await runner.AwaitIdleFrame();
        }
    }

    private static void AssertHintLayout(LanConnectRoomChatOverlayTestState state)
    {
        AssertRectInside(state.HintRect, state.RootRect);
        AssertRectInside(state.ToggleRect, state.RootRect);
        AssertRectInside(state.HintRect, state.ViewportRect);
        AssertRectInside(state.ToggleRect, state.ViewportRect);
        AssertThat(state.HintRect.Intersects(state.ToggleRect, includeBorders: false)).IsFalse();
    }

    private static void AssertRectInside(Rect2 rect, Rect2 bounds)
    {
        AssertThat(rect.Size.X > 0f && rect.Size.Y > 0f).IsTrue();
        AssertThat(rect.Position.X >= bounds.Position.X).IsTrue();
        AssertThat(rect.Position.Y >= bounds.Position.Y).IsTrue();
        AssertThat(rect.End.X <= bounds.End.X).IsTrue();
        AssertThat(rect.End.Y <= bounds.End.Y).IsTrue();
    }

    private static T FindNode<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);

    private sealed class FakeClock : ILanConnectMonotonicClock
    {
        public double NowSeconds { get; set; }
    }
}
