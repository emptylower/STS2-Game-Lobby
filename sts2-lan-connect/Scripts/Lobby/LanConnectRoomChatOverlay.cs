using System;
using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectRoomChatOverlayTestState(
    bool PanelOpen,
    bool Pinned,
    LanConnectChatChannel SelectedChannel,
    int RoomUnread,
    int ServerUnread,
    string Draft,
    double ScrollOffset,
    double RenderedScrollOffset,
    int NewMessagesBelowCount,
    string FocusOwnerName,
    Rect2 PanelRect,
    Rect2 MessagesRect,
    Rect2 DraftRect,
    Rect2 SendRect,
    IReadOnlyList<Rect2> TabRects,
    IReadOnlyList<Rect2> RetryRects,
    Rect2 NewMessagesRect,
    Rect2 PinRect,
    IReadOnlyList<LanConnectNamedControlRect> FocusTargetRects);

internal sealed partial class LanConnectRoomChatOverlay : CanvasLayer
{
    private const float DefaultRightMargin = 148f;
    private const float DefaultTopMargin = 96f;
    private const float PanelWidth = 430f;
    private const float PanelHeight = 520f;
    private const float TabWidth = 142f;
    private const float DragHoldSeconds = 0.28f;

    private static readonly Color PanelColor = new(0.09f, 0.09f, 0.11f, 0.95f);
    private static readonly Color BorderColor = new(0.56f, 0.44f, 0.2f, 1f);
    private static readonly Color AccentColor = new(0.86f, 0.69f, 0.33f, 1f);
    private static readonly Color TextStrongColor = new(0.96f, 0.94f, 0.88f, 1f);
    private static readonly Color TextMutedColor = new(0.76f, 0.74f, 0.69f, 1f);
    private static ImageTexture? _pinIconTexture;

    private const string PinIconSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#FFFFFF" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M12 17v5"/><path d="M5 17h14"/><path d="M17 3.1c-.5-.1-1-.1-1.5-.1h-7c-.5 0-1 0-1.5.1"/><path d="m6 8 1 1v4.5L5 17h14l-2-3.5V9l1-1"/><path d="M6 3h12"/>
        </svg>
        """;

    private MarginContainer? _root;
    private Control? _toggleBadge;
    private Label? _toggleBadgeLabel;
    private Button? _toggleButton;
    private PanelContainer? _panelFrame;
    private Button? _roomTab;
    private Button? _serverTab;
    private Label? _roomUnreadBadge;
    private Label? _serverUnreadBadge;
    private Button? _pinButton;
    private LanConnectBasicChatPanel? _chatPanel;
    private LanConnectDualChatState? _boundChat;
    private LanConnectChatChannelState? _boundChannelState;
    private LanConnectDualChatState? _testChat;
    private Func<LanConnectChatChannel, string, Task>? _testSend;
    private Func<LanConnectChatChannel, string, Task>? _testRetry;
    private bool _pinned;
    private long _testMessageId;
    private bool _dragPointerDown;
    private bool _dragTriggered;
    private bool _dragging;
    private bool _suppressNextToggle;
    private bool _dragUsesTouch;
    private int _dragTouchIndex = -1;
    private int _pendingTouchBeginIndex = -1;
    private double _dragHeldSeconds;
    private Vector2 _dragPointerStart;
    private Vector2 _dragRootStart;
    private float _currentPanelWidth = PanelWidth;
    private float _currentPanelHeight = PanelHeight;

    internal LanConnectRoomChatOverlayTestState TestState
    {
        get
        {
            LanConnectDualChatState? chat = ResolveChat();
            LanConnectChatChannel selected = chat?.SelectedChannel ?? LanConnectChatChannel.Room;
            LanConnectChatChannelState? selectedState = chat == null
                ? null
                : selected == LanConnectChatChannel.Room ? chat.Room : chat.Server;
            LanConnectBasicChatPanelTestState panelState = _chatPanel?.TestState ?? default;
            return new LanConnectRoomChatOverlayTestState(
                chat?.RoomOverlayOpen == true,
                _pinned,
                selected,
                chat?.Room.UnreadCount ?? 0,
                chat?.Server.UnreadCount ?? 0,
                selectedState?.Draft ?? string.Empty,
                selectedState?.ScrollOffset ?? 0,
                panelState.RenderedScrollOffset,
                selectedState?.NewMessagesBelowCount ?? 0,
                panelState.FocusOwnerName,
                RectForTests(_panelFrame),
                panelState.MessagesRect,
                panelState.DraftRect,
                panelState.SendRect,
                VisibleRectsForTests(_roomTab, _serverTab),
                panelState.RetryRects,
                panelState.NewMessagesRect,
                RectForTests(_pinButton),
                FocusTargetRectsForTests(panelState));
        }
    }

    internal LanConnectBasicChatPanel ChatPanelForTests =>
        _chatPanel ?? throw new InvalidOperationException("The room chat panel has not been built.");

    internal LanConnectChatChannelState SelectedChannelStateForTests =>
        SelectedState(ResolveChat()) ?? throw new InvalidOperationException("The room chat state is unavailable.");

    internal static void Install()
    {
        Callable.From(InstallDeferred).CallDeferred();
    }

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        GetViewport().Connect(Viewport.SignalName.SizeChanged, Callable.From(ApplyViewportBounds));
        ApplyViewportBounds();
        RefreshFromSource();
    }

    public override void _Process(double delta)
    {
        UpdateDragState(delta);
        RefreshFromSource();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            return;
        }

        HandleUnhandledKey(
            keyEvent.Keycode,
            keyEvent.ShiftPressed,
            HasBlockingModalOutsideOverlay(),
            markHandled: true);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!_dragPointerDown)
        {
            return;
        }

        switch (inputEvent)
        {
            case InputEventScreenDrag screenDrag when _dragUsesTouch && screenDrag.Index == _dragTouchIndex:
                UpdateDragPointer(screenDrag.Position, true);
                break;
            case InputEventScreenTouch touch when _pendingTouchBeginIndex == touch.Index && touch.Pressed:
                _pendingTouchBeginIndex = -1;
                BeginDrag(touch.Position, usesTouch: true, touch.Index);
                break;
            case InputEventScreenTouch touch when _dragUsesTouch && touch.Index == _dragTouchIndex:
                if (touch.Pressed)
                {
                    UpdateDragPointer(touch.Position, true);
                }
                else
                {
                    EndDrag();
                }
                break;
            case InputEventMouseMotion mouseMotion when !_dragUsesTouch:
                UpdateDragPointer(
                    GetViewportPointerPosition(mouseMotion.Position),
                    mouseMotion.ButtonMask.HasFlag(MouseButtonMask.Left));
                break;
            case InputEventMouseButton mouseButton when
                !_dragUsesTouch && mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed:
                EndDrag();
                break;
        }
    }

    internal void ConfigureForTests(
        LanConnectDualChatState chat,
        Func<LanConnectChatChannel, string, Task> send,
        Func<LanConnectChatChannel, string, Task> retry)
    {
        _testChat = chat ?? throw new ArgumentNullException(nameof(chat));
        _testSend = send ?? throw new ArgumentNullException(nameof(send));
        _testRetry = retry ?? throw new ArgumentNullException(nameof(retry));
        _boundChat = null;
        _boundChannelState = null;
        RefreshFromSource();
    }

    internal async Task OpenForTests()
    {
        OpenPanel();
        await RefreshForTests();
    }

    internal async Task CloseForTests()
    {
        ClosePanel();
        await RefreshForTests();
    }

    internal void SelectChannelForTests(LanConnectChatChannel channel)
    {
        SelectChannel(channel);
    }

    internal void SetDraftForTests(string draft)
    {
        LanConnectChatChannelState? state = SelectedState(ResolveChat());
        state?.SetDraft(draft);
        _chatPanel?.RefreshForTests().GetAwaiter().GetResult();
    }

    internal void SetScrollForTests(double offset, bool atBottom)
    {
        _chatPanel?.SetScrollForTests(offset, atBottom);
    }

    internal void InjectRemoteForTests(LanConnectChatChannel channel, long sequence)
    {
        LanConnectDualChatState chat = ResolveChat() ??
            throw new InvalidOperationException("Chat test state is unavailable.");
        LanConnectChatChannelState state = channel == LanConnectChatChannel.Room ? chat.Room : chat.Server;
        long id = ++_testMessageId;
        state.AppendConfirmedForTests($"test-{channel}-{id}", "Remote", $"message {id}", sequence, false);
    }

    internal async Task LeaveRoomForTests()
    {
        LanConnectDualChatState chat = ResolveChat() ??
            throw new InvalidOperationException("Chat test state is unavailable.");
        chat.LeaveRoom();
        await RefreshForTests();
    }

    internal async Task RefreshForTests()
    {
        RefreshFromSource();
        if (_chatPanel != null)
        {
            await _chatPanel.RefreshForTests();
        }
    }

    internal void RouteKeyForTests(Key key, bool shiftPressed = false, bool blockingModalVisible = false)
    {
        HandleUnhandledKey(key, shiftPressed, blockingModalVisible, markHandled: false);
    }

    internal void FocusDraftForTests() => _chatPanel?.FocusDraft();

    internal void FocusFirstForTests() => _roomTab?.GrabFocus();

    private static void InstallDeferred()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            Callable.From(InstallDeferred).CallDeferred();
            return;
        }
        if (tree.Root.GetNodeOrNull<Node>(LanConnectConstants.RoomChatOverlayName) != null)
        {
            return;
        }

        tree.Root.AddChild(new LanConnectRoomChatOverlay
        {
            Name = LanConnectConstants.RoomChatOverlayName
        });
    }

    private void BuildUi()
    {
        _root = new MarginContainer();
        _root.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        AddChild(_root);
        ApplyOverlayPosition(LanConnectConfig.RoomChatOverlayOffset ?? GetDefaultOverlayOffset());

        VBoxContainer stack = new();
        stack.MouseFilter = Control.MouseFilterEnum.Stop;
        stack.AddThemeConstantOverride("separation", 10);
        _root.AddChild(stack);

        HBoxContainer topRow = new() { Alignment = BoxContainer.AlignmentMode.End };
        stack.AddChild(topRow);

        Control toggleWrap = new()
        {
            CustomMinimumSize = new Vector2(132f, 44f),
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        topRow.AddChild(toggleWrap);

        _toggleButton = CreateButton("聊天", accent: true, TogglePanel);
        _toggleButton.CustomMinimumSize = new Vector2(132f, 44f);
        _toggleButton.TooltipText = "点击打开聊天，长按可拖动位置";
        _toggleButton.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(OnDragHandleGuiInput));
        toggleWrap.AddChild(_toggleButton);

        _toggleBadge = CreateUnreadBadge("ChatToggleUnreadBadge", out _toggleBadgeLabel);
        toggleWrap.AddChild(_toggleBadge);

        _panelFrame = CreatePanel(PanelColor, BorderColor, 10, 14);
        _panelFrame.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        stack.AddChild(_panelFrame);

        VBoxContainer body = new();
        body.AddThemeConstantOverride("separation", 10);
        _panelFrame.AddChild(body);

        HBoxContainer header = new();
        header.MouseDefaultCursorShape = Control.CursorShape.Drag;
        header.TooltipText = "长按标题栏可拖动聊天区";
        header.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(OnDragHandleGuiInput));
        body.AddChild(header);

        Label title = CreateLabel("聊天", 18, TextStrongColor);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);

        _pinButton = CreateButton("固定", accent: false, TogglePinned);
        _pinButton.Name = "ChatPinButton";
        _pinButton.CustomMinimumSize = new Vector2(104, 36);
        _pinButton.TooltipText = "固定聊天浮层";
        _pinButton.Icon = GetPinIconTexture();
        _pinButton.ExpandIcon = true;
        header.AddChild(_pinButton);

        Button closeButton = CreateButton("收起", accent: false, ClosePanel);
        closeButton.CustomMinimumSize = new Vector2(68, 36);
        header.AddChild(closeButton);

        HBoxContainer tabs = new();
        tabs.AddThemeConstantOverride("separation", 8);
        body.AddChild(tabs);

        _roomTab = CreateTab("RoomChatTab", "房间聊天", "RoomUnreadBadge", out _roomUnreadBadge);
        _roomTab.Connect(Button.SignalName.Pressed, Callable.From(() => SelectChannel(LanConnectChatChannel.Room)));
        tabs.AddChild(_roomTab);

        _serverTab = CreateTab("ServerChatTab", "频道聊天", "ServerUnreadBadge", out _serverUnreadBadge);
        _serverTab.Connect(Button.SignalName.Pressed, Callable.From(() => SelectChannel(LanConnectChatChannel.Server)));
        tabs.AddChild(_serverTab);

        _chatPanel = new LanConnectBasicChatPanel
        {
            Name = "RoomChatPanel",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 390)
        };
        body.AddChild(_chatPanel);
        ApplyViewportBounds();
    }

    private void RefreshFromSource()
    {
        LanConnectDualChatState? chat = ResolveChat();
        bool hasRoom = chat?.ActiveRoomId != null;
        if (_root != null)
        {
            _root.Visible = hasRoom;
        }
        if (_toggleButton != null)
        {
            _toggleButton.Visible = hasRoom;
        }
        if (!hasRoom || chat == null)
        {
            _chatPanel?.ReleaseDraftFocus();
            if (_panelFrame != null)
            {
                _panelFrame.Visible = false;
            }
            CancelDrag();
            RefreshBadges(chat);
            return;
        }

        bool serverSupported = chat.Server.Presentation != LanConnectServerChatPresentation.Unsupported;
        if (_serverTab != null)
        {
            _serverTab.Visible = serverSupported;
        }
        if (!serverSupported && chat.SelectedChannel == LanConnectChatChannel.Server)
        {
            CaptureCurrentViewState(chat);
            chat.Select(LanConnectChatChannel.Room);
        }

        if (_panelFrame != null)
        {
            _panelFrame.Visible = chat.RoomOverlayOpen;
        }
        if (!chat.RoomOverlayOpen)
        {
            _chatPanel?.ReleaseDraftFocus();
        }
        BindSelectedChannel(chat);
        RefreshTabStyles(chat);
        RefreshBadges(chat);
        ConfigureFocusChain();
    }

    private LanConnectDualChatState? ResolveChat()
    {
        if (_testChat != null)
        {
            return _testChat;
        }

        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null)
        {
            return null;
        }
        try
        {
            return runtime.Chat;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void BindSelectedChannel(LanConnectDualChatState chat)
    {
        if (_chatPanel == null)
        {
            return;
        }

        LanConnectChatChannel channel = chat.SelectedChannel;
        LanConnectChatChannelState state = channel == LanConnectChatChannel.Room ? chat.Room : chat.Server;
        if (ReferenceEquals(_boundChat, chat) && ReferenceEquals(_boundChannelState, state))
        {
            return;
        }

        _chatPanel.Bind(
            state,
            text => SendAsync(channel, text),
            clientMessageId => RetryAsync(channel, clientMessageId),
            HasBlockingModalOutsideOverlay);
        _boundChat = chat;
        _boundChannelState = state;
        ConfigureFocusChain();
    }

    private void TogglePanel()
    {
        if (_suppressNextToggle)
        {
            _suppressNextToggle = false;
            return;
        }

        LanConnectDualChatState? chat = ResolveChat();
        if (chat?.RoomOverlayOpen == true)
        {
            ClosePanel();
        }
        else
        {
            OpenPanel();
        }
    }

    private void OpenPanel()
    {
        LanConnectDualChatState? chat = ResolveChat();
        if (chat?.ActiveRoomId == null)
        {
            return;
        }

        bool serverSelectable = chat.Server.Presentation != LanConnectServerChatPresentation.Unsupported;
        chat.OpenRoomOverlay(serverSelectable);
        RefreshFromSource();
    }

    private void ShowPanelPreservingSelection()
    {
        LanConnectDualChatState? chat = ResolveChat();
        if (chat?.ActiveRoomId == null)
        {
            return;
        }

        bool serverSelectable = chat.Server.Presentation != LanConnectServerChatPresentation.Unsupported;
        chat.ShowRoomOverlayPreservingSelection(serverSelectable);
        RefreshFromSource();
    }

    private void ClosePanel()
    {
        LanConnectDualChatState? chat = ResolveChat();
        if (chat?.RoomOverlayOpen != true)
        {
            return;
        }

        CaptureCurrentViewState(chat);
        _chatPanel?.ReleaseDraftFocus();
        chat.CloseRoomOverlay();
        RefreshFromSource();
    }

    private void SelectChannel(LanConnectChatChannel channel)
    {
        LanConnectDualChatState? chat = ResolveChat();
        if (chat == null ||
            (channel == LanConnectChatChannel.Server &&
             chat.Server.Presentation == LanConnectServerChatPresentation.Unsupported))
        {
            return;
        }

        CaptureCurrentViewState(chat);
        chat.Select(channel);
        BindSelectedChannel(chat);
        RefreshTabStyles(chat);
        RefreshBadges(chat);
    }

    private Task SendAsync(LanConnectChatChannel channel, string text)
    {
        if (_testSend != null)
        {
            return _testSend(channel, text);
        }

        LanConnectLobbyRuntime runtime = LanConnectLobbyRuntime.Instance ??
            throw new InvalidOperationException("Lobby runtime is unavailable.");
        return runtime.SendChatTextAsync(channel, text);
    }

    private Task RetryAsync(LanConnectChatChannel channel, string clientMessageId)
    {
        if (_testRetry != null)
        {
            return _testRetry(channel, clientMessageId);
        }
        if (channel == LanConnectChatChannel.Room)
        {
            return Task.CompletedTask;
        }

        LanConnectLobbyRuntime runtime = LanConnectLobbyRuntime.Instance ??
            throw new InvalidOperationException("Lobby runtime is unavailable.");
        return runtime.RetryServerChatAsync(clientMessageId);
    }

    private void TogglePinned()
    {
        SetPinned(!_pinned);
    }

    private void SetPinned(bool value)
    {
        _pinned = value;
        if (_pinButton != null)
        {
            _pinButton.Text = _pinned ? "取消固定" : "固定";
        }
    }

    private void CaptureCurrentViewState(LanConnectDualChatState chat)
    {
        LanConnectChatChannelState state = chat.SelectedChannel == LanConnectChatChannel.Room
            ? chat.Room
            : chat.Server;
        if (_chatPanel?.FindChild(
                LanConnectConstants.ChatMessagesScrollName,
                recursive: true,
                owned: false) is ScrollContainer scroll)
        {
            ScrollBar bar = scroll.GetVScrollBar();
            double bottom = Math.Max(bar.MinValue, bar.MaxValue - bar.Page);
            state.SetScrollState(bar.Value, bottom - bar.Value <= 8);
        }
    }

    private void RefreshTabStyles(LanConnectDualChatState chat)
    {
        ApplyTabStyle(_roomTab, chat.SelectedChannel == LanConnectChatChannel.Room);
        ApplyTabStyle(_serverTab, chat.SelectedChannel == LanConnectChatChannel.Server);
    }

    private void RefreshBadges(LanConnectDualChatState? chat)
    {
        int roomUnread = chat?.Room.UnreadCount ?? 0;
        int serverUnread = chat?.Server.UnreadCount ?? 0;
        SetBadge(_roomUnreadBadge, roomUnread);
        SetBadge(_serverUnreadBadge, serverUnread);
        bool serverSelectable = chat?.Server.Presentation != LanConnectServerChatPresentation.Unsupported;
        int total = roomUnread + (serverSelectable ? serverUnread : 0);
        if (_toggleBadge != null)
        {
            _toggleBadge.Visible = total > 0;
        }
        if (_toggleBadgeLabel != null)
        {
            _toggleBadgeLabel.Text = BadgeText(total);
        }
    }

    private static LanConnectChatChannelState? SelectedState(LanConnectDualChatState? chat)
    {
        if (chat == null)
        {
            return null;
        }
        return chat.SelectedChannel == LanConnectChatChannel.Room ? chat.Room : chat.Server;
    }

    private void HandleUnhandledKey(
        Key key,
        bool shiftPressed,
        bool blockingModalVisible,
        bool markHandled)
    {
        LanConnectDualChatState? chat = ResolveChat();
        bool overlayOpen = chat?.RoomOverlayOpen == true;
        bool modalVisible = blockingModalVisible || _chatPanel?.PopupVisible == true;
        LanConnectChatInputAction action;

        if (key is Key.Enter or Key.KpEnter)
        {
            Control? focusOwner = GetViewport().GuiGetFocusOwner();
            if (_chatPanel?.DraftHasFocus != true && LanConnectAccessibilityKeyboard.IsTextInput(focusOwner))
            {
                return;
            }
            action = LanConnectChatInputRouter.RouteEnter(
                _chatPanel?.DraftHasFocus == true,
                shiftPressed,
                modalVisible);
        }
        else if (key == Key.Escape)
        {
            if (blockingModalVisible)
            {
                return;
            }
            action = LanConnectChatInputRouter.RouteEscape(
                popupVisible: _chatPanel?.PopupVisible == true,
                previewVisible: false,
                inputFocused: _chatPanel?.DraftHasFocus == true,
                overlayOpen);
        }
        else if (key == Key.F8)
        {
            LanConnectAccessibilityHotkeyRoute hotkeyRoute = LanConnectAccessibilityHotkeyRouter.Route(
                LanConnectAccessibilityHotkey.F8Chat,
                new LanConnectAccessibilityHotkeyContext(
                    TextInputHasFocus: LanConnectAccessibilityKeyboard.IsTextInput(GetViewport().GuiGetFocusOwner()),
                    InviteDialogVisible: false,
                    ClipboardHasInvite: false,
                    ChatAvailable: _toggleButton?.Visible == true,
                    BlockingModalVisible: modalVisible));
            if (hotkeyRoute.Action != LanConnectAccessibilityHotkeyAction.ToggleChat)
            {
                return;
            }
            action = LanConnectChatInputRouter.RouteF8(overlayOpen, _pinned, modalVisible);
        }
        else
        {
            return;
        }

        if (ApplyInputAction(action, overlayOpen) && markHandled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private bool ApplyInputAction(LanConnectChatInputAction action, bool overlayOpen)
    {
        switch (action)
        {
            case LanConnectChatInputAction.OpenAndFocus:
                if (!overlayOpen)
                {
                    OpenPanel();
                }
                if (ResolveChat()?.RoomOverlayOpen == true)
                {
                    _chatPanel?.FocusDraft();
                    return true;
                }
                return false;
            case LanConnectChatInputAction.Send:
                _chatPanel?.HandleDraftEnter(shiftPressed: false);
                return true;
            case LanConnectChatInputAction.InsertNewline:
                _chatPanel?.HandleDraftEnter(shiftPressed: true);
                return true;
            case LanConnectChatInputAction.ClosePopup:
                _chatPanel?.ClosePopup();
                return true;
            case LanConnectChatInputAction.ReleaseInputFocus:
                _chatPanel?.ReleaseDraftFocus();
                return true;
            case LanConnectChatInputAction.CloseOverlay:
                ClosePanel();
                return true;
            case LanConnectChatInputAction.ShowOverlay:
                ShowPanelPreservingSelection();
                return ResolveChat()?.RoomOverlayOpen == true;
            case LanConnectChatInputAction.PinOverlay:
                SetPinned(true);
                return true;
            case LanConnectChatInputAction.HideOverlay:
                SetPinned(false);
                ClosePanel();
                return true;
            default:
                return false;
        }
    }

    private bool HasBlockingModalOutsideOverlay()
    {
        return IsInsideTree() && LanConnectBlockingModal.IsAnyVisible(GetTree(), this);
    }

    private void ConfigureFocusChain()
    {
        if (_roomTab == null || _pinButton == null || _chatPanel == null)
        {
            return;
        }

        List<Control> chain = new() { _roomTab };
        if (_serverTab?.Visible == true)
        {
            chain.Add(_serverTab);
        }
        chain.AddRange(_chatPanel.GetFocusChainControls());
        chain.Add(_pinButton);

        for (int index = 0; index < chain.Count; index++)
        {
            Control current = chain[index];
            Control previous = chain[(index - 1 + chain.Count) % chain.Count];
            Control next = chain[(index + 1) % chain.Count];
            current.FocusPrevious = current.GetPathTo(previous);
            current.FocusNext = current.GetPathTo(next);
        }
    }

    private static void SetBadge(Label? badge, int count)
    {
        if (badge == null)
        {
            return;
        }
        badge.Visible = count > 0;
        badge.Text = BadgeText(count);
    }

    private static string BadgeText(int count) => count > 99 ? "99+" : count.ToString();

    private static void ApplyTabStyle(Button? tab, bool selected)
    {
        if (tab == null)
        {
            return;
        }
        tab.ButtonPressed = selected;
        tab.AddThemeColorOverride("font_color", selected ? TextStrongColor : TextMutedColor);
    }

    private void OnDragHandleGuiInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton mouseButton when mouseButton.ButtonIndex == MouseButton.Left:
                if (mouseButton.Pressed)
                {
                    BeginDrag(GetViewportPointerPosition(mouseButton.Position), usesTouch: false, touchIndex: -1);
                }
                else
                {
                    EndDrag();
                }
                break;
            case InputEventScreenTouch touch:
                if (touch.Pressed)
                {
                    _pendingTouchBeginIndex = touch.Index;
                }
                else
                {
                    EndDrag();
                }
                break;
        }
    }

    private void BeginDrag(Vector2 pointerPosition, bool usesTouch, int touchIndex)
    {
        if (_root == null)
        {
            return;
        }
        _dragPointerDown = true;
        _dragTriggered = false;
        _dragging = false;
        _dragUsesTouch = usesTouch;
        _dragTouchIndex = touchIndex;
        _dragHeldSeconds = 0;
        _dragPointerStart = pointerPosition;
        _dragRootStart = new Vector2(_root.OffsetRight, _root.OffsetTop);
    }

    private void UpdateDragPointer(Vector2 pointerPosition, bool stillPressed)
    {
        if (!_dragPointerDown)
        {
            return;
        }
        if (!stillPressed)
        {
            EndDrag();
            return;
        }
        if (!_dragTriggered)
        {
            return;
        }

        _dragging = true;
        ApplyOverlayPosition(ClampOverlayOffset(_dragRootStart + pointerPosition - _dragPointerStart));
    }

    private void UpdateDragState(double delta)
    {
        if (!_dragPointerDown || _dragTriggered)
        {
            return;
        }
        _dragHeldSeconds += delta;
        if (_dragHeldSeconds >= DragHoldSeconds)
        {
            _dragTriggered = true;
        }
    }

    private void EndDrag()
    {
        if (_dragging && _root != null)
        {
            LanConnectConfig.RoomChatOverlayOffset = new Vector2(_root.OffsetRight, _root.OffsetTop);
            _suppressNextToggle = true;
        }
        CancelDrag();
    }

    private void CancelDrag()
    {
        _dragPointerDown = false;
        _dragTriggered = false;
        _dragging = false;
        _dragUsesTouch = false;
        _dragTouchIndex = -1;
        _pendingTouchBeginIndex = -1;
        _dragHeldSeconds = 0;
    }

    private void ApplyOverlayPosition(Vector2 offset)
    {
        if (_root == null)
        {
            return;
        }
        _root.OffsetRight = offset.X;
        _root.OffsetTop = offset.Y;
        _root.OffsetLeft = offset.X - _currentPanelWidth;
        _root.OffsetBottom = offset.Y;
    }

    private void ApplyViewportBounds()
    {
        if (_root == null || !GodotObject.IsInstanceValid(_root))
        {
            return;
        }

        Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920f, 1080f);
        bool compact = viewportSize.X <= 1280f || viewportSize.Y <= 720f;
        _currentPanelWidth = MathF.Min(PanelWidth, MathF.Max(300f, viewportSize.X - 36f));
        float availablePanelHeight = MathF.Max(270f, viewportSize.Y - 90f);
        _currentPanelHeight = MathF.Min(PanelHeight, availablePanelHeight);

        if (_panelFrame != null && GodotObject.IsInstanceValid(_panelFrame))
        {
            _panelFrame.CustomMinimumSize = new Vector2(_currentPanelWidth, _currentPanelHeight);
        }
        if (_chatPanel != null && GodotObject.IsInstanceValid(_chatPanel))
        {
            _chatPanel.CustomMinimumSize = new Vector2(0, compact ? 180 : 390);
            _chatPanel.SetCompactLayout(compact);
        }

        Vector2 currentOffset = new(_root.OffsetRight, _root.OffsetTop);
        ApplyOverlayPosition(ClampOverlayOffset(currentOffset));
    }

    private Vector2 ClampOverlayOffset(Vector2 offset)
    {
        Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920f, 1080f);
        float minRight = -viewportSize.X + _currentPanelWidth + 18f;
        float maxRight = -18f;
        float minTop = 18f;
        float stackHeight = 44f + 10f + _currentPanelHeight;
        float maxTop = MathF.Max(minTop, viewportSize.Y - stackHeight - 18f);
        return new Vector2(
            Mathf.Clamp(offset.X, minRight, maxRight),
            Mathf.Clamp(offset.Y, minTop, maxTop));
    }

    private Vector2 GetViewportPointerPosition(Vector2 fallbackPosition) =>
        GetViewport()?.GetMousePosition() ?? fallbackPosition;

    private static Vector2 GetDefaultOverlayOffset() =>
        new(-DefaultRightMargin, DefaultTopMargin);

    private static Rect2 RectForTests(Control? control) =>
        control != null && GodotObject.IsInstanceValid(control) && control.IsInsideTree() && control.Visible
            ? control.GetGlobalRect()
            : default;

    private static IReadOnlyList<Rect2> VisibleRectsForTests(params Control?[] controls)
    {
        List<Rect2> rects = new();
        foreach (Control? control in controls)
        {
            Rect2 rect = RectForTests(control);
            if (rect.Size.X > 0f && rect.Size.Y > 0f)
            {
                rects.Add(rect);
            }
        }
        return rects;
    }

    private IReadOnlyList<LanConnectNamedControlRect> FocusTargetRectsForTests(
        LanConnectBasicChatPanelTestState panelState)
    {
        List<LanConnectNamedControlRect> rects = new();
        AddNamedRect(rects, _roomTab);
        AddNamedRect(rects, _serverTab);
        rects.AddRange(panelState.FocusTargetRects);
        AddNamedRect(rects, _pinButton);
        return rects;
    }

    private static void AddNamedRect(List<LanConnectNamedControlRect> rects, Control? control)
    {
        Rect2 rect = RectForTests(control);
        if (rect.Size.X > 0f && rect.Size.Y > 0f)
        {
            rects.Add(new LanConnectNamedControlRect(control!.Name.ToString(), rect));
        }
    }

    private static Button CreateTab(string name, string text, string badgeName, out Label badge)
    {
        Button tab = CreateButton(text, accent: false, static () => { });
        tab.Name = name;
        tab.ToggleMode = true;
        tab.ActionMode = BaseButton.ActionModeEnum.Release;
        tab.CustomMinimumSize = new Vector2(TabWidth, 40);
        tab.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;

        badge = CreateLabel(string.Empty, 11, Colors.White);
        badge.Name = badgeName;
        badge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        badge.OffsetLeft = -34;
        badge.OffsetTop = 2;
        badge.OffsetRight = -6;
        badge.OffsetBottom = 22;
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.AddThemeStyleboxOverride("normal", CreateBadgeStyle());
        badge.Visible = false;
        tab.AddChild(badge);
        return tab;
    }

    private static Control CreateUnreadBadge(string name, out Label label)
    {
        PanelContainer badge = new()
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        badge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        badge.OffsetLeft = -34;
        badge.OffsetTop = -6;
        badge.OffsetRight = -2;
        badge.OffsetBottom = 22;
        badge.AddThemeStyleboxOverride("panel", CreateBadgeStyle());

        label = CreateLabel(string.Empty, 11, Colors.White);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        badge.AddChild(label);
        return badge;
    }

    private static StyleBoxFlat CreateBadgeStyle() => new()
    {
        BgColor = new Color(0.82f, 0.12f, 0.16f, 1f),
        BorderColor = new Color(1f, 0.88f, 0.9f, 0.95f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 8,
        CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8,
        CornerRadiusBottomRight = 8,
        ContentMarginLeft = 5,
        ContentMarginTop = 1,
        ContentMarginRight = 5,
        ContentMarginBottom = 1
    };

    private static PanelContainer CreatePanel(Color background, Color border, int radius, int padding)
    {
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = padding,
            ContentMarginTop = padding,
            ContentMarginRight = padding,
            ContentMarginBottom = padding
        });
        return panel;
    }

    private static Label CreateLabel(string text, int size, Color color)
    {
        Label label = new() { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Button CreateButton(string text, bool accent, Action onPressed)
    {
        Button button = new()
        {
            Text = text,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            FocusMode = Control.FocusModeEnum.All
        };
        Color background = accent
            ? new Color(0.28f, 0.21f, 0.08f, 0.96f)
            : new Color(0.14f, 0.14f, 0.16f, 0.96f);
        Color border = accent ? AccentColor : BorderColor;
        button.AddThemeStyleboxOverride("normal", CreateButtonStyle(background, border));
        button.AddThemeStyleboxOverride("hover", CreateButtonStyle(background.Lightened(0.08f), AccentColor));
        button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(background.Darkened(0.06f), AccentColor));
        button.AddThemeStyleboxOverride("hover_pressed", CreateButtonStyle(background.Lightened(0.04f), AccentColor));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        button.Connect(Button.SignalName.Pressed, Callable.From(onPressed));
        return button;
    }

    private static StyleBoxFlat CreateButtonStyle(Color background, Color border) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 10,
        ContentMarginTop = 7,
        ContentMarginRight = 10,
        ContentMarginBottom = 7
    };

    private static ImageTexture? GetPinIconTexture()
    {
        if (_pinIconTexture != null && GodotObject.IsInstanceValid(_pinIconTexture))
        {
            return _pinIconTexture;
        }

        Image image = new();
        Error error = image.LoadSvgFromString(PinIconSvg, scale: 2f);
        if (error != Error.Ok)
        {
            GD.PrintErr($"sts2_lan_connect: failed to load room chat pin icon: {error}");
            return null;
        }

        _pinIconTexture = ImageTexture.CreateFromImage(image);
        return _pinIconTexture;
    }
}
