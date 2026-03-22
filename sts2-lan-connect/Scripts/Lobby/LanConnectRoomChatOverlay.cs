using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectRoomChatOverlay : CanvasLayer
{
    private const float DefaultRightMargin = 148f;
    private const float DefaultTopMargin = 96f;
    private const float PanelWidth = 402f;
    private const float PanelHeight = 420f;
    private const float MessageContentWidth = 348f;
    private const float DragHoldSeconds = 0.28f;

    private static readonly Color PanelColor = new(0.09f, 0.09f, 0.11f, 0.95f);
    private static readonly Color BorderColor = new(0.56f, 0.44f, 0.2f, 1f);
    private static readonly Color AccentColor = new(0.86f, 0.69f, 0.33f, 1f);
    private static readonly Color AccentMutedColor = new(0.38f, 0.29f, 0.12f, 1f);
    private static readonly Color TextStrongColor = new(0.96f, 0.94f, 0.88f, 1f);
    private static readonly Color TextMutedColor = new(0.76f, 0.74f, 0.69f, 1f);
    private static readonly Color LocalBubbleColor = new(0.2f, 0.17f, 0.08f, 0.96f);
    private static readonly Color RemoteBubbleColor = new(0.14f, 0.14f, 0.16f, 0.96f);

    private MarginContainer? _root;
    private Control? _toggleBadge;
    private Label? _toggleBadgeLabel;
    private Button? _toggleButton;
    private PanelContainer? _panel;
    private HBoxContainer? _header;
    private Label? _roomLabel;
    private Label? _emptyStateLabel;
    private ScrollContainer? _messagesScroll;
    private VBoxContainer? _messagesList;
    private LineEdit? _input;
    private Button? _sendButton;
    private Label? _statusLabel;
    private bool _panelOpen;
    private int _lastChatRevision = -1;
    private int _lastMessageCount;
    private int _unreadCount;
    private string? _lastRoomId;
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

    internal static void Install()
    {
        Callable.From(InstallDeferred).CallDeferred();
    }

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
    }

    public override void _Process(double delta)
    {
        UpdateDragState(delta);
        RefreshVisibility();
        RefreshMessagesIfNeeded();
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
                BeginTouchDrag(touch.Position, touch.Index);
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
                UpdateDragPointer(GetViewportPointerPosition(mouseMotion.Position), mouseMotion.ButtonMask.HasFlag(MouseButtonMask.Left));
                break;
            case InputEventMouseButton mouseButton when !_dragUsesTouch && mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed:
                EndDrag();
                break;
        }
    }

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

        LanConnectRoomChatOverlay overlay = new()
        {
            Name = LanConnectConstants.RoomChatOverlayName
        };
        tree.Root.AddChild(overlay);
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

        HBoxContainer topRow = new();
        topRow.Alignment = BoxContainer.AlignmentMode.End;
        stack.AddChild(topRow);

        Control toggleWrap = new()
        {
            CustomMinimumSize = new Vector2(132f, 44f),
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        topRow.AddChild(toggleWrap);

        _toggleButton = CreateButton("房间聊天", true, TogglePanel);
        _toggleButton.CustomMinimumSize = new Vector2(132f, 44f);
        _toggleButton.TooltipText = "点击打开聊天，长按可拖动位置";
        _toggleButton.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(OnDragHandleGuiInput));
        toggleWrap.AddChild(_toggleButton);

        _toggleBadge = CreateUnreadBadge(out _toggleBadgeLabel);
        _toggleBadge.Visible = false;
        toggleWrap.AddChild(_toggleBadge);

        _panel = CreatePanel(PanelColor, BorderColor, 18, 18);
        _panel.Visible = false;
        _panel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        stack.AddChild(_panel);

        VBoxContainer body = new();
        body.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(body);

        _header = new HBoxContainer();
        _header.MouseDefaultCursorShape = Control.CursorShape.Drag;
        _header.TooltipText = "长按标题栏可拖动聊天区";
        _header.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(OnDragHandleGuiInput));
        body.AddChild(_header);

        VBoxContainer titleBox = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleBox.AddThemeConstantOverride("separation", 3);
        _header.AddChild(titleBox);

        Label title = CreateLabel("房间聊天", 20, TextStrongColor);
        titleBox.AddChild(title);

        _roomLabel = CreateLabel("", 13, TextMutedColor);
        titleBox.AddChild(_roomLabel);

        Button closeButton = CreateButton("收起", false, TogglePanel);
        closeButton.CustomMinimumSize = new Vector2(76f, 36f);
        _header.AddChild(closeButton);

        _messagesScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0f, 260f)
        };
        ApplyScrollStyle(_messagesScroll);
        body.AddChild(_messagesScroll);

        _messagesList = new VBoxContainer();
        _messagesList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _messagesList.CustomMinimumSize = new Vector2(MessageContentWidth, 0f);
        _messagesList.AddThemeConstantOverride("separation", 8);
        _messagesScroll.AddChild(_messagesList);

        _emptyStateLabel = CreateLabel("加入房间后即可和同房间玩家聊天。", 14, TextMutedColor);
        _emptyStateLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_emptyStateLabel);

        _statusLabel = CreateLabel("", 12, TextMutedColor);
        body.AddChild(_statusLabel);

        HBoxContainer inputRow = new();
        inputRow.AddThemeConstantOverride("separation", 10);
        body.AddChild(inputRow);

        _input = new LineEdit
        {
            PlaceholderText = "输入聊天内容，按 Enter 发送",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = 60
        };
        ApplyInputStyle(_input);
        _input.Connect(LineEdit.SignalName.TextSubmitted, Callable.From<string>(_ => TaskHelper.RunSafely(SendCurrentMessageAsync())));
        inputRow.AddChild(_input);

        _sendButton = CreateButton("发送", true, () => TaskHelper.RunSafely(SendCurrentMessageAsync()));
        _sendButton.CustomMinimumSize = new Vector2(82f, 42f);
        inputRow.AddChild(_sendButton);
    }

    private void RefreshVisibility()
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        bool hasSession = runtime?.HasActiveRoomSession == true;
        if (_root != null)
        {
            _root.Visible = hasSession;
        }

        if (_toggleButton != null)
        {
            _toggleButton.Visible = hasSession;
        }

        if (_panel != null)
        {
            _panel.Visible = hasSession && _panelOpen;
        }

        if (!hasSession)
        {
            _panelOpen = false;
            _lastRoomId = null;
            _lastChatRevision = -1;
            _lastMessageCount = 0;
            _unreadCount = 0;
            if (_input != null)
            {
                _input.Text = string.Empty;
            }

            if (_statusLabel != null)
            {
                _statusLabel.Text = string.Empty;
            }

            RefreshUnreadBadge();
            CancelDrag();
        }
    }

    private void RefreshMessagesIfNeeded()
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null || !runtime.HasActiveRoomSession)
        {
            return;
        }

        IReadOnlyList<LobbyRoomChatEntry> snapshot = runtime.GetChatMessagesSnapshot();
        string? roomId = runtime.ActiveRoomId;
        if (string.IsNullOrWhiteSpace(roomId) && snapshot.Count > 0)
        {
            roomId = snapshot[snapshot.Count - 1].RoomId;
        }

        if (!string.Equals(_lastRoomId, roomId, StringComparison.Ordinal) || _lastChatRevision != runtime.ChatRevision)
        {
            UpdateUnreadState(snapshot, roomId);
            _lastRoomId = roomId;
            _lastChatRevision = runtime.ChatRevision;
            _lastMessageCount = snapshot.Count;
            RebuildMessages(snapshot, roomId);
        }
    }

    private void UpdateUnreadState(IReadOnlyList<LobbyRoomChatEntry> messages, string? roomId)
    {
        if (!string.Equals(_lastRoomId, roomId, StringComparison.Ordinal))
        {
            _unreadCount = 0;
            RefreshUnreadBadge();
            return;
        }

        if (_panelOpen)
        {
            _unreadCount = 0;
            RefreshUnreadBadge();
            return;
        }

        for (int index = _lastMessageCount; index < messages.Count; index++)
        {
            if (!messages[index].IsLocal)
            {
                _unreadCount++;
            }
        }

        RefreshUnreadBadge();
    }

    private void RebuildMessages(IReadOnlyList<LobbyRoomChatEntry> messages, string? roomId)
    {
        if (_messagesList == null || _emptyStateLabel == null || _roomLabel == null)
        {
            return;
        }

        foreach (Node child in _messagesList.GetChildren())
        {
            child.QueueFree();
        }

        _roomLabel.Text = string.IsNullOrWhiteSpace(roomId) ? "当前未识别房间" : $"房间 ID: {roomId}";
        _emptyStateLabel.Visible = messages.Count == 0;
        _emptyStateLabel.Text = messages.Count == 0 ? "现在还没有聊天消息。" : string.Empty;

        foreach (LobbyRoomChatEntry message in messages)
        {
            _messagesList.AddChild(BuildMessageBubble(message));
        }

        if (_panelOpen)
        {
            CallDeferred(nameof(ScrollMessagesToBottom));
        }
    }

    private Control BuildMessageBubble(LobbyRoomChatEntry message)
    {
        PanelContainer bubble = CreatePanel(message.IsLocal ? LocalBubbleColor : RemoteBubbleColor, message.IsLocal ? AccentColor : AccentMutedColor, 14, 12);
        bubble.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bubble.CustomMinimumSize = new Vector2(MessageContentWidth, 0f);

        VBoxContainer body = new();
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 4);
        bubble.AddChild(body);

        HBoxContainer metaRow = new();
        body.AddChild(metaRow);

        Label sender = CreateLabel(message.SenderName, 13, message.IsLocal ? AccentColor : TextStrongColor);
        sender.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        metaRow.AddChild(sender);

        Label time = CreateLabel(message.SentAt.ToLocalTime().ToString("HH:mm"), 12, TextMutedColor);
        metaRow.AddChild(time);

        Label text = CreateLabel(message.MessageText, 14, TextStrongColor);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(text);
        return bubble;
    }

    private async Task SendCurrentMessageAsync()
    {
        if (_input == null || _sendButton == null || _statusLabel == null)
        {
            return;
        }

        string messageText = _input.Text;
        if (string.IsNullOrWhiteSpace(messageText))
        {
            _statusLabel.Text = "请输入聊天内容。";
            return;
        }

        _sendButton.Disabled = true;
        _input.Editable = false;
        try
        {
            LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
            if (runtime == null || !runtime.HasActiveRoomSession)
            {
                _statusLabel.Text = "当前没有已连接的房间。";
                return;
            }

            await runtime.SendRoomChatMessageAsync(messageText);
            _input.Text = string.Empty;
            _statusLabel.Text = "已发送。";
            _input.GrabFocus();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "发送失败，请稍后重试。";
            Log.Warn($"sts2_lan_connect room chat send failed: {ex.Message}");
        }
        finally
        {
            _sendButton.Disabled = false;
            _input.Editable = true;
        }
    }

    private void TogglePanel()
    {
        if (_suppressNextToggle)
        {
            _suppressNextToggle = false;
            return;
        }

        _panelOpen = !_panelOpen;
        if (_panel != null)
        {
            _panel.Visible = _panelOpen;
        }

        if (_panelOpen)
        {
            _unreadCount = 0;
            RefreshUnreadBadge();
            _input?.GrabFocus();
            CallDeferred(nameof(ScrollMessagesToBottom));
        }
    }

    private void ScrollMessagesToBottom()
    {
        if (_messagesScroll?.GetVScrollBar() is ScrollBar scrollBar)
        {
            scrollBar.Value = scrollBar.MaxValue;
        }
    }

    private void OnDragHandleGuiInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton mouseButton when mouseButton.ButtonIndex == MouseButton.Left:
                if (mouseButton.Pressed)
                {
                    BeginMouseDrag(GetViewportPointerPosition(mouseButton.Position));
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

    private void BeginMouseDrag(Vector2 pointerPosition)
    {
        BeginDrag(pointerPosition, usesTouch: false, touchIndex: -1);
    }

    private void BeginTouchDrag(Vector2 pointerPosition, int touchIndex)
    {
        BeginDrag(pointerPosition, usesTouch: true, touchIndex);
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
        _dragHeldSeconds = 0d;
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
        Vector2 delta = pointerPosition - _dragPointerStart;
        ApplyOverlayPosition(ClampOverlayOffset(_dragRootStart + delta));
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
            if (_statusLabel != null)
            {
                _statusLabel.Text = "拖动中，松开即可保存位置。";
            }
        }
    }

    private void EndDrag()
    {
        if (_dragging && _root != null)
        {
            LanConnectConfig.RoomChatOverlayOffset = new Vector2(_root.OffsetRight, _root.OffsetTop);
            _suppressNextToggle = true;
            if (_statusLabel != null)
            {
                _statusLabel.Text = "已保存聊天区位置。";
            }
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
        _dragHeldSeconds = 0d;
    }

    private void ApplyOverlayPosition(Vector2 offset)
    {
        if (_root == null)
        {
            return;
        }

        _root.OffsetRight = offset.X;
        _root.OffsetTop = offset.Y;
        _root.OffsetLeft = offset.X - PanelWidth;
        _root.OffsetBottom = offset.Y;
    }

    private Vector2 GetViewportPointerPosition(Vector2 fallbackPosition)
    {
        return GetViewport()?.GetMousePosition() ?? fallbackPosition;
    }

    private Vector2 ClampOverlayOffset(Vector2 offset)
    {
        Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920f, 1080f);
        float minRight = -viewportSize.X + PanelWidth + 18f;
        float maxRight = -18f;
        float minTop = 18f;
        float maxTop = MathF.Max(minTop, viewportSize.Y - PanelHeight + 18f);
        return new Vector2(Mathf.Clamp(offset.X, minRight, maxRight), Mathf.Clamp(offset.Y, minTop, maxTop));
    }

    private static Vector2 GetDefaultOverlayOffset()
    {
        return new Vector2(-DefaultRightMargin, DefaultTopMargin);
    }

    private static PanelContainer CreatePanel(Color background, Color border, int radius, int padding)
    {
        PanelContainer panel = new();
        StyleBoxFlat style = new();
        style.BgColor = background;
        style.BorderColor = border;
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.CornerRadiusTopLeft = radius;
        style.CornerRadiusTopRight = radius;
        style.CornerRadiusBottomLeft = radius;
        style.CornerRadiusBottomRight = radius;
        style.ContentMarginLeft = padding;
        style.ContentMarginTop = padding;
        style.ContentMarginRight = padding;
        style.ContentMarginBottom = padding;
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static Control CreateUnreadBadge(out Label label)
    {
        PanelContainer badge = new();
        badge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        badge.OffsetLeft = -30f;
        badge.OffsetTop = -6f;
        badge.OffsetRight = -2f;
        badge.OffsetBottom = 22f;
        badge.MouseFilter = Control.MouseFilterEnum.Ignore;

        StyleBoxFlat style = new();
        style.BgColor = new Color(0.82f, 0.12f, 0.16f, 1f);
        style.BorderColor = new Color(1f, 0.88f, 0.9f, 0.95f);
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.CornerRadiusTopLeft = 999;
        style.CornerRadiusTopRight = 999;
        style.CornerRadiusBottomLeft = 999;
        style.CornerRadiusBottomRight = 999;
        style.ContentMarginLeft = 6;
        style.ContentMarginTop = 1;
        style.ContentMarginRight = 6;
        style.ContentMarginBottom = 1;
        badge.AddThemeStyleboxOverride("panel", style);

        label = CreateLabel("1", 11, new Color(1f, 0.98f, 0.94f, 1f));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        badge.AddChild(label);
        return badge;
    }

    private void RefreshUnreadBadge()
    {
        if (_toggleBadge == null || _toggleBadgeLabel == null)
        {
            return;
        }

        _toggleBadge.Visible = _unreadCount > 0;
        _toggleBadgeLabel.Text = _unreadCount > 99 ? "99+" : _unreadCount.ToString();
    }

    private static Label CreateLabel(string text, int size, Color color)
    {
        Label label = new();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Button CreateButton(string text, bool accent, Action onPressed)
    {
        Button button = new();
        button.Text = text;
        button.Flat = false;
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        button.AddThemeStyleboxOverride("normal", CreateButtonStyle(accent ? new Color(0.28f, 0.21f, 0.08f, 0.96f) : new Color(0.14f, 0.14f, 0.16f, 0.96f), accent ? AccentColor : BorderColor));
        button.AddThemeStyleboxOverride("hover", CreateButtonStyle(accent ? new Color(0.37f, 0.28f, 0.11f, 0.98f) : new Color(0.19f, 0.19f, 0.21f, 0.98f), AccentColor));
        button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(accent ? new Color(0.47f, 0.34f, 0.12f, 1f) : new Color(0.24f, 0.24f, 0.26f, 1f), AccentColor));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        button.Connect(Button.SignalName.Pressed, Callable.From(onPressed));
        return button;
    }

    private static StyleBoxFlat CreateButtonStyle(Color background, Color border)
    {
        StyleBoxFlat style = new();
        style.BgColor = background;
        style.BorderColor = border;
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.CornerRadiusTopLeft = 14;
        style.CornerRadiusTopRight = 14;
        style.CornerRadiusBottomLeft = 14;
        style.CornerRadiusBottomRight = 14;
        style.ContentMarginLeft = 14;
        style.ContentMarginTop = 8;
        style.ContentMarginRight = 14;
        style.ContentMarginBottom = 8;
        return style;
    }

    private static void ApplyInputStyle(LineEdit input)
    {
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_placeholder_color", TextMutedColor);
        input.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.12f, 0.12f, 0.14f, 0.98f), AccentMutedColor));
        input.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.14f, 0.14f, 0.17f, 1f), AccentColor));
        input.AddThemeStyleboxOverride("read_only", CreateButtonStyle(new Color(0.09f, 0.09f, 0.11f, 0.9f), AccentMutedColor));
    }

    private static void ApplyScrollStyle(ScrollContainer scroll)
    {
        if (scroll.GetVScrollBar() is ScrollBar scrollbar)
        {
            scrollbar.AddThemeStyleboxOverride("scroll", CreateButtonStyle(new Color(0.08f, 0.08f, 0.1f, 0.96f), AccentMutedColor));
            scrollbar.AddThemeStyleboxOverride("grabber", CreateButtonStyle(new Color(0.31f, 0.24f, 0.1f, 0.98f), AccentColor));
            scrollbar.AddThemeStyleboxOverride("grabber_highlight", CreateButtonStyle(new Color(0.4f, 0.3f, 0.12f, 1f), AccentColor));
            scrollbar.AddThemeStyleboxOverride("grabber_pressed", CreateButtonStyle(new Color(0.47f, 0.34f, 0.12f, 1f), AccentColor));
        }
    }
}
