using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class LanConnectItemLinkCaptureUiTests
{
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public async Task Runtime_input_route_precedes_gui_and_only_success_suppresses_normal_action(
        bool captureSucceeds,
        int expectedNormalActions)
    {
        SubViewport viewport = AutoFree(new SubViewport
        {
            Size = new Vector2I(320, 180),
            Disable3D = true
        })!;
        ObservableInputHolder holder = new()
        {
            Position = new Vector2(20, 20),
            Size = new Vector2(120, 80),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        viewport.AddChild(holder);
        ViewportRoutePorts ports = new(holder, captureSucceeds);
        LanConnectLobbyRuntime runtime = new();
        runtime.ConfigureItemLinkCaptureRouteForTests(ports);
        viewport.AddChild(runtime);
        using ISceneRunner runner = ISceneRunner.Load(viewport, autoFree: true);
        await runner.AwaitIdleFrame();

        Vector2 pointer = new(40, 40);
        viewport.PushInput(new InputEventMouseMotion
        {
            Position = pointer,
            GlobalPosition = pointer
        });
        await runner.AwaitInputProcessed();
        viewport.PushInput(AltLeftPress(pointer));
        await runner.AwaitInputProcessed();

        AssertThat(ports.InsertCalls).IsEqual(captureSucceeds ? 1 : 0);
        AssertThat(holder.NormalActions).IsEqual(expectedNormalActions);
    }

    [TestCase("card", "MegaCrit.Strike", 2)]
    [TestCase("relic", "MegaCrit.Anchor", null)]
    [TestCase("potion", "MegaCrit.FirePotion", null)]
    public async Task Successful_capture_inserts_at_current_caret_focuses_and_marks_handled(
        string itemType,
        string modelId,
        int? upgradeLevel)
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("ab");
        draft.SetCaret(new LanConnectDraftPosition(0, 1));
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", _ => "Item");
        Control root = AutoFree(new Control())!;
        root.AddChild(editor);
        TestItemHolder holder = new(itemType, new LanConnectItemRun(itemType, modelId, upgradeLevel));
        Control hitbox = new();
        holder.AddChild(hitbox);
        root.AddChild(holder);
        using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.AwaitIdleFrame();

        TestCapturePorts ports = new(editor)
        {
            Hovered = hitbox,
            SelectedChannel = LanConnectChatChannel.Room
        };
        ports.Drafts[LanConnectChatChannel.Room] = draft;
        bool handled = false;
        bool consumed = LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(),
            new LanConnectItemLinkCapture(ports),
            () => handled = true);
        await runner.AwaitIdleFrame();

        AssertThat(consumed).IsTrue();
        AssertThat(handled).IsTrue();
        AssertThat(draft.Runs).ContainsExactly(
            new LanConnectTextRun("a"),
            new LanConnectItemRun(itemType, modelId, upgradeLevel),
            new LanConnectTextRun("b"));
        AssertThat(editor.HasEditorFocus).IsTrue();
        AssertThat(ports.SendCalls).IsEqual(0);
    }

    [TestCase]
    public async Task Successful_power_capture_inserts_room_entity_focuses_and_marks_handled()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("ab");
        draft.SetCaret(new LanConnectDraftPosition(0, 1));
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 1), "Ironclad", _ => "Entity");
        Control root = AutoFree(new Control())!;
        root.AddChild(editor);
        LanConnectCombatRun power = new(new LanConnectPowerStateSegment(
            "MegaCrit.Strength",
            -2,
            "session-1",
            "net:owner"));
        TestCombatHolder holder = new("power", power);
        Control hitbox = new();
        holder.AddChild(hitbox);
        root.AddChild(holder);
        using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.AwaitIdleFrame();

        TestCapturePorts ports = new(editor)
        {
            Hovered = hitbox,
            SelectedChannel = LanConnectChatChannel.Room
        };
        ports.Drafts[LanConnectChatChannel.Room] = draft;
        bool handled = false;
        bool consumed = LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(),
            new LanConnectItemLinkCapture(ports),
            () => handled = true);
        await runner.AwaitIdleFrame();

        AssertThat(consumed).IsTrue();
        AssertThat(handled).IsTrue();
        AssertThat(draft.Runs).ContainsExactly(
            new LanConnectTextRun("a"),
            power,
            new LanConnectTextRun("b"));
        AssertThat(editor.HasEditorFocus).IsTrue();
        AssertThat(ports.SendCalls).IsEqual(0);
    }

    [TestCase]
    public async Task Production_room_overlay_inserts_into_selected_server_and_room_drafts()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        LanConnectRichDraft serverDraft = fixture.State.Server.RichDraft;
        LanConnectRichDraft roomDraft = fixture.State.Room.RichDraft;
        fixture.State.Room.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 1));
        serverDraft.ReplaceAllWithText("S");
        roomDraft.ReplaceAllWithText("R");

        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Server);
        await fixture.Overlay.RefreshForTests();
        AssertThat(LanConnectGodotItemLinkCapturePorts.TryInsertAndFocus(
            roomActive: true,
            fixture.State.Server,
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            fixture.Overlay,
            lobbyOverlay: null)).IsTrue();
        fixture.Overlay.ChatPanelForTests.ReleaseDraftFocus();
        fixture.Overlay.SelectChannelForTests(LanConnectChatChannel.Room);
        await fixture.Overlay.RefreshForTests();
        AssertThat(LanConnectGodotItemLinkCapturePorts.TryInsertAndFocus(
            roomActive: true,
            fixture.State.Room,
            new LanConnectItemRun("potion", "MegaCrit.FirePotion"),
            fixture.Overlay,
            lobbyOverlay: null)).IsTrue();
        fixture.Overlay.ChatPanelForTests.ReleaseDraftFocus();
        LanConnectCombatRun power = new(new LanConnectPowerStateSegment(
            "MegaCrit.Strength",
            -2,
            "session-1"));
        AssertThat(fixture.Overlay.TryInsertCombatReferenceAndFocus(
            fixture.State.Room,
            power)).IsTrue();

        AssertThat(serverDraft.Runs).ContainsExactly(
            new LanConnectTextRun("S"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1));
        AssertThat(roomDraft.Runs).ContainsExactly(
            new LanConnectTextRun("R"),
            new LanConnectItemRun("potion", "MegaCrit.FirePotion"),
            power);
    }

    [TestCase]
    public async Task Server_combat_candidate_shows_warning_without_marking_input_handled()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("server");
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        editor.Bind(draft, new(1, 1, 1, 1), "Ironclad", _ => "Entity");
        Control root = AutoFree(new Control())!;
        root.AddChild(editor);
        TestCombatHolder holder = new(
            "power",
            new LanConnectCombatRun(new LanConnectPowerStateSegment(
                "MegaCrit.Strength",
                2,
                "session-1")));
        root.AddChild(holder);
        using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.AwaitIdleFrame();
        TestCapturePorts ports = new(editor)
        {
            Hovered = holder,
            SelectedChannel = LanConnectChatChannel.Server
        };
        ports.Drafts[LanConnectChatChannel.Server] = draft;
        bool handled = false;

        bool consumed = LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(),
            new LanConnectItemLinkCapture(ports),
            () => handled = true);

        AssertThat(consumed).IsFalse();
        AssertThat(handled).IsFalse();
        AssertThat(ports.RoomOnlyWarnings).IsEqual(1);
        AssertThat(draft.IsExactlyText("server")).IsTrue();
    }

    [TestCase]
    public async Task Throwing_draft_subscriber_keeps_capture_committed_handled_and_notifies_later_subscriber()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        fixture.Overlay.ChatPanelForTests.ReleaseDraftFocus();
        LanConnectRichDraft draft = fixture.State.Room.RichDraft;
        long beforeRevision = draft.ContentRevision;
        int laterSubscriberCalls = 0;
        draft.ContentChanged += _ => throw new InvalidOperationException("observer failed");
        draft.ContentChanged += _ => laterSubscriberCalls++;
        TestItemHolder holder = AutoFree(new TestItemHolder(
            "card",
            new LanConnectItemRun("card", "MegaCrit.Strike", 1)))!;
        ProductionInsertCapturePorts ports = new(
            holder,
            run => LanConnectGodotItemLinkCapturePorts.TryInsertAndFocus(
                roomActive: true,
                fixture.State.Room,
                run,
                fixture.Overlay,
                lobbyOverlay: null));
        bool handled = false;

        bool consumed = LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(),
            new LanConnectItemLinkCapture(ports),
            () => handled = true);

        AssertThat(consumed).IsTrue();
        AssertThat(handled).IsTrue();
        AssertThat(draft.ContentRevision).IsEqual(beforeRevision + 1);
        AssertThat(draft.Runs.OfType<LanConnectItemRun>().Count()).IsEqual(1);
        AssertThat(laterSubscriberCalls).IsEqual(1);
    }

    [TestCase("blocked")]
    [TestCase("uneditable")]
    [TestCase("binding_mismatch")]
    [TestCase("post_insert_throw")]
    public async Task Production_panel_failure_keeps_runs_and_selection_unchanged(string scenario)
    {
        using RoomChatFixture fixture = await RoomChatFixture.CreateNeverOpenedWithServerSupport();
        LanConnectChatChannelState bound = fixture.State.Room;
        bound.RichDraft.ReplaceAllWithText("ab");
        bound.RichDraft.SetCaret(new LanConnectDraftPosition(0, 1));
        fixture.Overlay.InjectRemoteForTests(LanConnectChatChannel.Server, sequence: 20);
        await fixture.Overlay.RefreshForTests();
        LanConnectBasicChatPanel panel = fixture.Overlay.ChatPanelForTests;
        LanConnectChatChannel selectedBefore = fixture.State.SelectedChannel;
        Button previousFocus = new() { Name = "OutsideItemCaptureFocus" };
        fixture.Overlay.GetViewport().AddChild(previousFocus);
        previousFocus.GrabFocus();
        await fixture.Runner.AwaitIdleFrame();
        Control? modal = null;
        if (scenario == "blocked")
        {
            modal = new Control { Visible = true };
            modal.AddToGroup(LanConnectConstants.BlockingModalGroupName);
            fixture.Overlay.GetViewport().AddChild(modal);
            await fixture.Runner.AwaitIdleFrame();
        }
        if (scenario == "uneditable")
        {
            bound.SetChatEnabled(false);
            await fixture.Overlay.RefreshForTests();
        }
        if (scenario == "post_insert_throw")
        {
            panel.SetItemLinkPostInsertForTests(() => throw new InvalidOperationException("boom"));
        }
        LanConnectChatChannelState expected = scenario == "binding_mismatch"
            ? EnabledState(LanConnectChatChannel.Server)
            : bound;
        LanConnectRichDraftSnapshot before = bound.RichDraft.CaptureSnapshot();
        int roomUnreadBefore = fixture.State.Room.UnreadCount;
        int serverUnreadBefore = fixture.State.Server.UnreadCount;
        long? roomFirstUnreadBefore = fixture.State.Room.FirstUnreadSequence;
        long? serverFirstUnreadBefore = fixture.State.Server.FirstUnreadSequence;
        long roomRevisionBefore = fixture.State.Room.Revision;
        long serverRevisionBefore = fixture.State.Server.Revision;

        bool inserted = LanConnectGodotItemLinkCapturePorts.TryInsertAndFocus(
            roomActive: true,
            expected,
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            fixture.Overlay,
            lobbyOverlay: null);

        AssertThat(inserted).IsFalse();
        AssertThat(bound.RichDraft.Runs).ContainsExactly(before.Runs.ToArray());
        AssertThat(bound.RichDraft.Selection).IsEqual(before.Selection);
        AssertThat(bound.RichDraft.ContentRevision).IsEqual(before.ContentRevision);
        AssertThat(fixture.Overlay.TestState.PanelOpen).IsFalse();
        AssertThat(fixture.State.SelectedChannel).IsEqual(selectedBefore);
        AssertThat(fixture.State.Room.UnreadCount).IsEqual(roomUnreadBefore);
        AssertThat(fixture.State.Server.UnreadCount).IsEqual(serverUnreadBefore);
        AssertThat(fixture.State.Room.FirstUnreadSequence == roomFirstUnreadBefore).IsTrue();
        AssertThat(fixture.State.Server.FirstUnreadSequence == serverFirstUnreadBefore).IsTrue();
        AssertThat(fixture.State.Room.Revision == roomRevisionBefore).IsTrue();
        AssertThat(fixture.State.Server.Revision == serverRevisionBefore).IsTrue();
        AssertThat(ReferenceEquals(
            fixture.Overlay.GetViewport().GuiGetFocusOwner(),
            previousFocus)).IsTrue();
        modal?.QueueFree();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Overlay.OpenForTests();
        AssertThat(fixture.State.SelectedChannel).IsEqual(LanConnectChatChannel.Room);
    }

    [TestCase]
    public void Unbuilt_production_panel_fails_without_mutating_draft()
    {
        LanConnectChatChannelState state = EnabledState(LanConnectChatChannel.Server);
        state.RichDraft.ReplaceAllWithText("keep");
        LanConnectRoomChatOverlay overlay = AutoFree(new LanConnectRoomChatOverlay { Visible = false })!;
        LanConnectRichDraftSnapshot before = state.RichDraft.CaptureSnapshot();

        bool inserted = LanConnectGodotItemLinkCapturePorts.TryInsertAndFocus(
            roomActive: true,
            state,
            new LanConnectItemRun("relic", "MegaCrit.Anchor"),
            overlay,
            lobbyOverlay: null);

        AssertThat(inserted).IsFalse();
        AssertThat(state.RichDraft.Runs).ContainsExactly(before.Runs.ToArray());
        AssertThat(state.RichDraft.Selection).IsEqual(before.Selection);
        AssertThat(state.RichDraft.ContentRevision).IsEqual(before.ContentRevision);
        AssertThat(overlay.Visible).IsFalse();
    }

    [TestCase("uneditable")]
    [TestCase("binding_mismatch")]
    [TestCase("post_insert_throw")]
    public async Task Hidden_lobby_failure_restores_visibility_focus_and_picker_state(string scenario)
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        fixture.ServerState.RichDraft.ReplaceAllWithText("lobby");
        fixture.Overlay.HideForTests();
        fixture.ServerState.AppendConfirmedForTests(
            $"hidden-{scenario}",
            "Remote",
            "unread",
            sequence: 90,
            isLocal: false);
        LanConnectBasicChatPanel panel = fixture.Overlay.ServerChatPanelForTests;
        Button previousFocus = new() { Name = "OutsideLobbyItemCaptureFocus" };
        fixture.Overlay.GetViewport().AddChild(previousFocus);
        previousFocus.GrabFocus();
        await fixture.Runner.AwaitIdleFrame();
        if (scenario == "uneditable")
        {
            fixture.ServerState.SetChatEnabled(false);
            await fixture.Overlay.RefreshLayoutForTests(new Vector2I(1920, 1080));
        }
        if (scenario == "post_insert_throw")
        {
            panel.SetItemLinkPostInsertForTests(() => throw new InvalidOperationException("boom"));
        }
        LanConnectChatChannelState expected = scenario == "binding_mismatch"
            ? EnabledState(LanConnectChatChannel.Server)
            : fixture.ServerState;
        bool pickerBefore = fixture.Overlay.ServerPickerOpenForTests;
        bool dialogBefore = fixture.Overlay.AnyDialogVisibleForTests;
        bool pendingInviteBefore = fixture.Overlay.HasPendingInviteForTests;
        LanConnectRichDraftSnapshot before = fixture.ServerState.RichDraft.CaptureSnapshot();
        int unreadBefore = fixture.ServerState.UnreadCount;
        long? firstUnreadBefore = fixture.ServerState.FirstUnreadSequence;
        long channelRevisionBefore = fixture.ServerState.Revision;

        bool inserted = LanConnectGodotItemLinkCapturePorts.TryInsertAndFocus(
            roomActive: false,
            expected,
            new LanConnectItemRun("relic", "MegaCrit.Anchor"),
            roomOverlay: null,
            fixture.Overlay);
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(inserted).IsFalse();
        AssertThat(fixture.ServerState.RichDraft.Runs).ContainsExactly(before.Runs.ToArray());
        AssertThat(fixture.ServerState.RichDraft.Selection).IsEqual(before.Selection);
        AssertThat(fixture.ServerState.RichDraft.ContentRevision).IsEqual(before.ContentRevision);
        AssertThat(fixture.ServerState.UnreadCount).IsEqual(unreadBefore);
        AssertThat(fixture.ServerState.FirstUnreadSequence == firstUnreadBefore).IsTrue();
        AssertThat(fixture.ServerState.Revision == channelRevisionBefore).IsTrue();
        AssertThat(fixture.Overlay.Visible).IsFalse();
        AssertThat(fixture.Overlay.ServerPickerOpenForTests).IsEqual(pickerBefore);
        AssertThat(fixture.Overlay.AnyDialogVisibleForTests).IsEqual(dialogBefore);
        AssertThat(fixture.Overlay.HasPendingInviteForTests).IsEqual(pendingInviteBefore);
        AssertThat(ReferenceEquals(
            fixture.Overlay.GetViewport().GuiGetFocusOwner(),
            previousFocus)).IsTrue();
    }

    [TestCase]
    public async Task Hidden_lobby_success_ignores_clipboard_and_missing_endpoint_and_keeps_draft_focus()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        fixture.Overlay.HideForTests();
        fixture.Overlay.SetEndpointAvailableForTests(false);
        string previousClipboard = DisplayServer.ClipboardGet();
        DisplayServer.ClipboardSet(LanConnectInviteCode.Encode(
            "https://invite.example",
            "capture-room",
            password: null));
        try
        {
            bool inserted = LanConnectGodotItemLinkCapturePorts.TryInsertAndFocus(
                roomActive: false,
                fixture.ServerState,
                new LanConnectItemRun("card", "MegaCrit.Strike", 1),
                roomOverlay: null,
                fixture.Overlay);

            AssertThat(inserted).IsTrue();
            AssertThat(fixture.Overlay.Visible).IsTrue();
            AssertThat(fixture.Overlay.ServerChatPanelForTests.DraftHasFocus).IsTrue();
            for (int frame = 0; frame < 4; frame++)
            {
                await fixture.Runner.AwaitIdleFrame();
            }
            AssertThat(fixture.Overlay.ServerChatPanelForTests.DraftHasFocus).IsTrue();
            AssertThat(fixture.Overlay.AnyDialogVisibleForTests).IsFalse();
            AssertThat(fixture.Overlay.HasPendingInviteForTests).IsFalse();
            AssertThat(fixture.Overlay.ServerPickerOpenForTests).IsFalse();
        }
        finally
        {
            DisplayServer.ClipboardSet(previousClipboard);
        }
    }

    [TestCase]
    public async Task Focused_real_entity_chip_blocks_capture_without_mutating_draft()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        LanConnectRichDraft draft = fixture.State.Room.RichDraft;
        draft.InsertEntity(new LanConnectItemRun("relic", "MegaCrit.Anchor"));
        await fixture.Overlay.RefreshForTests();
        await fixture.Runner.AwaitIdleFrame();
        Button chip = fixture.Overlay.FindChildren(
                LanConnectConstants.ChatEntityChipPrefix + "*",
                "Button",
                recursive: true,
                owned: false)
            .OfType<Button>()
            .Single();
        chip.GrabFocus();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Overlay.ItemLinkCaptureBlocked).IsTrue();
        LanConnectRichDraftSnapshot before = draft.CaptureSnapshot();
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        TestCapturePorts ports = new(editor)
        {
            Hovered = AutoFree(new TestItemHolder(
                "card",
                new LanConnectItemRun("card", "MegaCrit.Strike", 1)))!,
            SelectedChannel = LanConnectChatChannel.Room,
            IsChatInteractionBlocking = fixture.Overlay.ItemLinkCaptureBlocked
        };
        ports.Drafts[LanConnectChatChannel.Room] = draft;
        bool handled = false;

        bool consumed = LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(),
            new LanConnectItemLinkCapture(ports),
            () => handled = true);

        AssertThat(consumed).IsFalse();
        AssertThat(handled).IsFalse();
        AssertThat(draft.Runs).ContainsExactly(before.Runs.ToArray());
        AssertThat(draft.Selection).IsEqual(before.Selection);
        AssertThat(draft.ContentRevision).IsEqual(before.ContentRevision);
    }

    [TestCase]
    public async Task Focused_lobby_entity_chip_marks_homepage_capture_blocked()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        fixture.ServerState.RichDraft.InsertEntity(
            new LanConnectItemRun("card", "MegaCrit.Strike", 1));
        await fixture.Overlay.RefreshLayoutForTests(new Vector2I(1920, 1080));
        await fixture.Runner.AwaitIdleFrame();
        Button chip = fixture.Overlay.FindChildren(
                LanConnectConstants.ChatEntityChipPrefix + "*",
                "Button",
                recursive: true,
                owned: false)
            .OfType<Button>()
            .Single();

        chip.GrabFocus();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.ServerChatPanelForTests.DraftHasFocus).IsTrue();
        AssertThat(fixture.Overlay.ItemLinkCaptureBlocked).IsTrue();
    }

    [TestCase]
    public async Task Unsupported_button_and_preview_boundary_are_not_handled_and_normal_click_survives()
    {
        Button button = AutoFree(new Button { Text = "normal" })!;
        using ISceneRunner runner = ISceneRunner.Load(button, autoFree: true);
        await runner.AwaitIdleFrame();
        int clicks = 0;
        button.Pressed += () => clicks++;
        LanConnectRichDraftEditor editor = AutoFree(new LanConnectRichDraftEditor())!;
        TestCapturePorts ports = new(editor) { Hovered = button };
        LanConnectItemLinkCapture capture = new(ports);
        bool handled = false;

        bool consumed = LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(), capture, () => handled = true);
        button.EmitSignal(Button.SignalName.Pressed);

        AssertThat(consumed).IsFalse();
        AssertThat(handled).IsFalse();
        AssertThat(clicks).IsEqual(1);
        AssertThat(ports.OpenAndFocusChannels).IsEmpty();

        TestItemHolder preview = AutoFree(new TestItemHolder(
            "card_preview",
            new LanConnectItemRun("card", "MegaCrit.Strike", 1)))!;
        ports.Hovered = preview;
        AssertThat(LanConnectItemLinkCaptureInputRoute.TryRoute(
            AltLeftPress(), capture, () => handled = true)).IsFalse();
        AssertThat(handled).IsFalse();
    }

    private static InputEventMouseButton AltLeftPress(Vector2? position = null) => new()
    {
        AltPressed = true,
        ButtonIndex = MouseButton.Left,
        Pressed = true,
        Position = position ?? Vector2.Zero,
        GlobalPosition = position ?? Vector2.Zero
    };

    private static LanConnectChatChannelState EnabledState(LanConnectChatChannel channel)
    {
        LanConnectChatChannelState state = new(channel);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            Channel = channel,
            ServerChatVersion = 1,
            InstanceId = "item-link-capture-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures
            {
                RichContentVersion = 1,
                ItemRefVersion = 1
            }
        });
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }

    private sealed partial class ObservableInputHolder : Control
    {
        internal int NormalActions { get; private set; }

        public override void _GuiInput(InputEvent inputEvent)
        {
            if (inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                NormalActions++;
            }
        }
    }

    private sealed partial class TestItemHolder : Control
    {
        internal TestItemHolder(string kind, LanConnectItemRun item)
        {
            Kind = kind;
            Item = item;
        }

        internal string Kind { get; }

        internal LanConnectItemRun Item { get; }
    }

    private sealed partial class TestCombatHolder(
        string kind,
        LanConnectCombatRun combat) : Control
    {
        internal string Kind { get; } = kind;

        internal LanConnectCombatRun Combat { get; } = combat;
    }

    private sealed class TestCapturePorts : ILanConnectItemLinkCapturePorts
    {
        private readonly LanConnectRichDraftEditor _editor;

        internal TestCapturePorts(LanConnectRichDraftEditor editor)
        {
            _editor = editor;
        }

        internal Control? Hovered { get; set; }

        internal LanConnectChatChannel SelectedChannel { get; set; }

        internal Dictionary<LanConnectChatChannel, LanConnectRichDraft> Drafts { get; } = new();

        internal List<LanConnectChatChannel> OpenAndFocusChannels { get; } = new();

        internal int SendCalls { get; private set; }

        internal int RoomOnlyWarnings { get; private set; }

        public bool IsChatInteractionBlocking { get; set; }

        public bool ItemRefsEnabledForSelectedChannel { get; set; } = true;

        public bool CombatRefsEnabledForSelectedChannel { get; set; } = true;

        public bool IsRoomChannelSelected => SelectedChannel == LanConnectChatChannel.Room;

        public object? GuiGetHoveredControl() => Hovered;

        public object? GetParent(object node) => ((Node)node).GetParent();

        public bool IsCaptureBoundary(object node) =>
            node is TestItemHolder { Kind: "card_preview" };

        public bool IsSupportedHolder(object node) =>
            node is TestItemHolder { Kind: "card" or "relic" or "potion" };

        public bool IsPowerHolder(object node) => node is TestCombatHolder { Kind: "power" };

        public bool IsPlayerHolder(object node) => node is TestCombatHolder { Kind: "player" };

        public bool TryResolveCard(object node, out LanConnectItemRun run) =>
            TryResolve("card", node, out run);

        public bool TryResolveRelic(object node, out LanConnectItemRun run) =>
            TryResolve("relic", node, out run);

        public bool TryResolvePotion(object node, out LanConnectItemRun run) =>
            TryResolve("potion", node, out run);

        public bool TryResolvePower(object node, out LanConnectCombatRun run) =>
            TryResolveCombat("power", node, out run);

        public bool TryResolvePlayerTarget(object node, out LanConnectCombatRun run) =>
            TryResolveCombat("player", node, out run);

        public bool InsertAndFocus(LanConnectItemRun run)
        {
            if (!Drafts.TryGetValue(SelectedChannel, out LanConnectRichDraft? draft))
            {
                return false;
            }
            draft.InsertEntity(run);
            _editor.Bind(draft, new(1, 1, 1, 0), "Ironclad", _ => "Item");
            _editor.RefreshFromDraft();
            _editor.FocusEditor();
            OpenAndFocusChannels.Add(SelectedChannel);
            return true;
        }

        public bool InsertCombatAndFocus(LanConnectCombatRun run)
        {
            if (!Drafts.TryGetValue(SelectedChannel, out LanConnectRichDraft? draft))
            {
                return false;
            }
            draft.InsertEntity(run);
            _editor.Bind(draft, new(1, 1, 1, 1), "Ironclad", _ => "Entity");
            _editor.RefreshFromDraft();
            _editor.FocusEditor();
            OpenAndFocusChannels.Add(SelectedChannel);
            return true;
        }

        public void ShowCombatRoomOnlyWarning()
        {
            RoomOnlyWarnings++;
        }

        private static bool TryResolve(string kind, object node, out LanConnectItemRun run)
        {
            if (node is TestItemHolder holder && holder.Kind == kind)
            {
                run = holder.Item;
                return true;
            }
            run = null!;
            return false;
        }

        private static bool TryResolveCombat(
            string kind,
            object node,
            out LanConnectCombatRun run)
        {
            if (node is TestCombatHolder holder && holder.Kind == kind)
            {
                run = holder.Combat;
                return true;
            }
            run = null!;
            return false;
        }
    }

    private sealed class ViewportRoutePorts(
        ObservableInputHolder holder,
        bool captureSucceeds) : ILanConnectItemLinkCapturePorts
    {
        internal int InsertCalls { get; private set; }

        public bool IsChatInteractionBlocking => false;

        public bool ItemRefsEnabledForSelectedChannel => true;

        public bool CombatRefsEnabledForSelectedChannel => false;

        public bool IsRoomChannelSelected => false;

        public object? GuiGetHoveredControl() => holder;

        public object? GetParent(object node) => (node as Node)?.GetParent();

        public bool IsCaptureBoundary(object node) => false;

        public bool IsSupportedHolder(object node) => ReferenceEquals(node, holder);

        public bool IsPowerHolder(object node) => false;

        public bool IsPlayerHolder(object node) => false;

        public bool TryResolveCard(object node, out LanConnectItemRun run)
        {
            if (captureSucceeds && ReferenceEquals(node, holder))
            {
                run = new LanConnectItemRun("card", "MegaCrit.Strike", 1);
                return true;
            }
            run = null!;
            return false;
        }

        public bool TryResolveRelic(object node, out LanConnectItemRun run)
        {
            run = null!;
            return false;
        }

        public bool TryResolvePotion(object node, out LanConnectItemRun run)
        {
            run = null!;
            return false;
        }

        public bool TryResolvePower(object node, out LanConnectCombatRun run)
        {
            run = null!;
            return false;
        }

        public bool TryResolvePlayerTarget(object node, out LanConnectCombatRun run)
        {
            run = null!;
            return false;
        }

        public bool InsertAndFocus(LanConnectItemRun run)
        {
            InsertCalls++;
            return true;
        }

        public bool InsertCombatAndFocus(LanConnectCombatRun run) => false;

        public void ShowCombatRoomOnlyWarning()
        {
        }
    }

    private sealed class ProductionInsertCapturePorts(
        TestItemHolder holder,
        Func<LanConnectItemRun, bool> insert) : ILanConnectItemLinkCapturePorts
    {
        public bool IsChatInteractionBlocking => false;

        public bool ItemRefsEnabledForSelectedChannel => true;

        public bool CombatRefsEnabledForSelectedChannel => false;

        public bool IsRoomChannelSelected => false;

        public object? GuiGetHoveredControl() => holder;

        public object? GetParent(object node) => (node as Node)?.GetParent();

        public bool IsCaptureBoundary(object node) => false;

        public bool IsSupportedHolder(object node) => ReferenceEquals(node, holder);

        public bool IsPowerHolder(object node) => false;

        public bool IsPlayerHolder(object node) => false;

        public bool TryResolveCard(object node, out LanConnectItemRun run)
        {
            if (ReferenceEquals(node, holder))
            {
                run = holder.Item;
                return true;
            }
            run = null!;
            return false;
        }

        public bool TryResolveRelic(object node, out LanConnectItemRun run)
        {
            run = null!;
            return false;
        }

        public bool TryResolvePotion(object node, out LanConnectItemRun run)
        {
            run = null!;
            return false;
        }

        public bool TryResolvePower(object node, out LanConnectCombatRun run)
        {
            run = null!;
            return false;
        }

        public bool TryResolvePlayerTarget(object node, out LanConnectCombatRun run)
        {
            run = null!;
            return false;
        }

        public bool InsertAndFocus(LanConnectItemRun run) => insert(run);

        public bool InsertCombatAndFocus(LanConnectCombatRun run) => false;

        public void ShowCombatRoomOnlyWarning()
        {
        }
    }
}
