using System;
using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectBasicChatPanelTestState(
    int MessageCount,
    int PendingCount,
    int FailedCount,
    int DeliveryUnknownCount,
    int RetryButtonCount,
    double ScrollOffset,
    bool IsAtBottom,
    int NewMessagesBelowCount,
    bool InputEditable,
    string FocusOwnerName);

internal sealed partial class LanConnectBasicChatPanel : VBoxContainer
{
    private static readonly Color TextStrongColor = new(0.94f, 0.91f, 0.84f, 1f);
    private static readonly Color TextMutedColor = new(0.68f, 0.65f, 0.6f, 1f);
    private static readonly Color AccentColor = new(0.88f, 0.58f, 0.17f, 1f);
    private static readonly Color DangerColor = new(0.94f, 0.38f, 0.34f, 1f);
    private static readonly Color WarningColor = new(0.94f, 0.7f, 0.26f, 1f);

    private LanConnectChatChannelState? _state;
    private Func<string, Task>? _send;
    private Func<string, Task>? _retry;
    private ScrollContainer? _messagesScroll;
    private VBoxContainer? _messagesList;
    private Button? _newMessagesButton;
    private LineEdit? _draftInput;
    private Button? _sendButton;
    private Label? _statusLabel;
    private ConfirmationDialog? _unknownConfirmation;
    private string? _confirmedUnknownText;
    private long _renderedRevision = -1;
    private bool _suppressScrollChange;
    private bool _busy;
    private string _operationStatus = string.Empty;

    internal LanConnectChatChannelState? State => _state;

    internal LanConnectBasicChatPanelTestState TestState
    {
        get
        {
            int pending = 0;
            int failed = 0;
            int unknown = 0;
            int retries = 0;
            IReadOnlyList<ServerChatMessageState> messages = _state?.Messages ?? Array.Empty<ServerChatMessageState>();
            foreach (ServerChatMessageState message in messages)
            {
                switch (message.Delivery)
                {
                    case ServerChatDeliveryState.Pending:
                        pending++;
                        break;
                    case ServerChatDeliveryState.Failed:
                        failed++;
                        retries++;
                        break;
                    case ServerChatDeliveryState.DeliveryUnknown:
                        unknown++;
                        retries++;
                        break;
                }
            }

            string focusOwnerName = string.Empty;
            if (IsInsideTree())
            {
                focusOwnerName = GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? string.Empty;
            }

            return new LanConnectBasicChatPanelTestState(
                messages.Count,
                pending,
                failed,
                unknown,
                retries,
                _state?.ScrollOffset ?? 0,
                _state?.IsAtBottom ?? true,
                _state?.NewMessagesBelowCount ?? 0,
                _draftInput?.Editable == true,
                focusOwnerName);
        }
    }

    public override void _Ready()
    {
        BuildControls();
        SetProcess(true);
        if (_state != null)
        {
            ApplyBoundState();
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (_state != null && _state.Revision != _renderedRevision)
        {
            Refresh();
        }
    }

    public override void _ExitTree()
    {
        _state?.SetVisible(false);
    }

    internal void Bind(
        LanConnectChatChannelState state,
        Func<string, Task> send,
        Func<string, Task> retry)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(retry);

        if (_state != null && !ReferenceEquals(_state, state))
        {
            CaptureCurrentViewState();
            _state.SetVisible(false);
        }

        _state = state;
        _send = send;
        _retry = retry;
        _renderedRevision = -1;
        _operationStatus = string.Empty;
        _confirmedUnknownText = null;
        _unknownConfirmation?.Hide();
        if (_draftInput != null)
        {
            ApplyBoundState();
        }
    }

    internal void SetScrollForTests(double offset, bool atBottom)
    {
        if (_state == null)
        {
            return;
        }

        _suppressScrollChange = true;
        if (_messagesScroll?.GetVScrollBar() is ScrollBar bar)
        {
            bar.Value = Math.Max(0, offset);
        }
        _suppressScrollChange = false;
        _state.SetVisible(true);
        _state.SetScrollState(offset, atBottom);
        Refresh();
    }

    internal Task RefreshForTests()
    {
        Refresh();
        return Task.CompletedTask;
    }

    private void BuildControls()
    {
        AddThemeConstantOverride("separation", 8);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        HBoxContainer titleRow = new();
        titleRow.AddThemeConstantOverride("separation", 10);
        AddChild(titleRow);

        Label title = CreateLabel("频道聊天", 16, TextStrongColor);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleRow.AddChild(title);

        _statusLabel = CreateLabel(string.Empty, 12, TextMutedColor);
        _statusLabel.Name = LanConnectConstants.ChatStatusLabelName;
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Right;
        titleRow.AddChild(_statusLabel);

        _messagesScroll = new ScrollContainer
        {
            Name = LanConnectConstants.ChatMessagesScrollName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, 140),
            FocusMode = FocusModeEnum.All
        };
        AddChild(_messagesScroll);

        _messagesList = new VBoxContainer
        {
            Name = LanConnectConstants.ChatMessagesListName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _messagesList.AddThemeConstantOverride("separation", 6);
        _messagesScroll.AddChild(_messagesList);
        _messagesScroll.GetVScrollBar().Connect(
            Godot.Range.SignalName.ValueChanged,
            Callable.From<double>(OnScrollChanged));

        _newMessagesButton = CreateButton(string.Empty, accent: false);
        _newMessagesButton.Name = LanConnectConstants.ChatNewMessagesButtonName;
        _newMessagesButton.Visible = false;
        _newMessagesButton.Connect(Button.SignalName.Pressed, Callable.From(ScrollToBottom));
        AddChild(_newMessagesButton);

        HBoxContainer inputRow = new();
        inputRow.AddThemeConstantOverride("separation", 8);
        AddChild(inputRow);

        _draftInput = new LineEdit
        {
            Name = LanConnectConstants.ChatDraftInputName,
            PlaceholderText = "输入消息",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MaxLength = 500,
            FocusMode = FocusModeEnum.All
        };
        ApplyInputStyle(_draftInput);
        _draftInput.Connect(LineEdit.SignalName.TextChanged, Callable.From<string>(OnDraftChanged));
        _draftInput.Connect(
            LineEdit.SignalName.TextSubmitted,
            Callable.From<string>(_submittedText => _ = SendDraftAsync()));
        inputRow.AddChild(_draftInput);

        _sendButton = CreateButton("发送", accent: true);
        _sendButton.Name = LanConnectConstants.ChatSendButtonName;
        _sendButton.CustomMinimumSize = new Vector2(74, 38);
        _sendButton.Connect(Button.SignalName.Pressed, Callable.From(() => _ = SendDraftAsync()));
        inputRow.AddChild(_sendButton);

        _unknownConfirmation = new ConfirmationDialog
        {
            Name = "DisconnectedUnknownConfirmation",
            Title = "确认重新发送",
            DialogText = "这条消息可能已经发送。是否以新消息重新发送？",
            OkButtonText = "重新发送",
            CancelButtonText = "取消"
        };
        _unknownConfirmation.Connect(
            ConfirmationDialog.SignalName.Confirmed,
            Callable.From(() => _ = SendConfirmedUnknownAsync()));
        AddChild(_unknownConfirmation);

        Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(UpdateStateVisibility));
    }

    private void ApplyBoundState()
    {
        if (_state == null || _draftInput == null)
        {
            return;
        }

        _draftInput.Text = _state.Draft;
        _state.SetVisible(IsInsideTree() && IsVisibleInTree());
        Refresh();
    }

    private void CaptureCurrentViewState()
    {
        if (_state == null)
        {
            return;
        }

        if (_draftInput != null)
        {
            _state.SetDraft(_draftInput.Text);
        }

        if (_messagesScroll?.GetVScrollBar() is ScrollBar bar)
        {
            _state.SetScrollState(bar.Value, IsNearBottom(bar));
        }
    }

    private void Refresh()
    {
        if (_state == null || _messagesList == null || _draftInput == null || _sendButton == null)
        {
            return;
        }

        IReadOnlyList<ServerChatMessageState> messages = _state.Messages;
        bool atBottom = _state.IsAtBottom;
        double scrollOffset = _state.ScrollOffset;
        _suppressScrollChange = true;
        foreach (Node child in _messagesList.GetChildren())
        {
            _messagesList.RemoveChild(child);
            child.Free();
        }

        if (messages.Count == 0)
        {
            Label empty = CreateLabel("还没有消息", 13, TextMutedColor);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            _messagesList.AddChild(empty);
        }
        else
        {
            for (int index = 0; index < messages.Count; index++)
            {
                _messagesList.AddChild(BuildMessageRow(messages[index], index));
            }
        }

        if (_messagesScroll?.GetVScrollBar() is ScrollBar bar)
        {
            bar.Value = atBottom ? BottomValue(bar) : scrollOffset;
        }
        _suppressScrollChange = false;

        if (_draftInput.Text != _state.Draft)
        {
            _draftInput.Text = _state.Draft;
        }
        UpdateAvailability();
        UpdateNewMessagesButton();
        _renderedRevision = _state.Revision;
        if (atBottom)
        {
            Callable.From(ScrollToBottomWithoutConsumingState).CallDeferred();
        }
    }

    private Control BuildMessageRow(ServerChatMessageState message, int index)
    {
        PanelContainer row = new();
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddThemeStyleboxOverride("panel", CreatePanelStyle(message.IsLocal));

        VBoxContainer body = new();
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 3);
        row.AddChild(body);

        HBoxContainer metadata = new();
        metadata.AddThemeConstantOverride("separation", 8);
        body.AddChild(metadata);

        Label sender = CreateLabel(
            string.IsNullOrWhiteSpace(message.SenderName) ? "未知玩家" : message.SenderName,
            12,
            message.IsLocal ? AccentColor : TextStrongColor);
        sender.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sender.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        metadata.AddChild(sender);

        Label timestamp = CreateLabel(message.SentAt.ToLocalTime().ToString("HH:mm"), 11, TextMutedColor);
        metadata.AddChild(timestamp);

        Label content = CreateLabel(message.Text, 14, TextStrongColor);
        content.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(content);

        string deliveryText = DeliveryText(message);
        if (!string.IsNullOrEmpty(deliveryText))
        {
            HBoxContainer deliveryRow = new();
            deliveryRow.AddThemeConstantOverride("separation", 8);
            body.AddChild(deliveryRow);

            Label delivery = CreateLabel(
                deliveryText,
                12,
                message.Delivery == ServerChatDeliveryState.Failed ? DangerColor : WarningColor);
            delivery.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            delivery.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            deliveryRow.AddChild(delivery);

            if (message.Delivery is ServerChatDeliveryState.Failed or ServerChatDeliveryState.DeliveryUnknown)
            {
                Button retryButton = CreateButton("重试", accent: false);
                retryButton.Name = LanConnectConstants.ChatRetryButtonPrefix + RetryNodeSuffix(message, index);
                retryButton.FocusMode = FocusModeEnum.All;
                retryButton.Connect(
                    Button.SignalName.Pressed,
                    Callable.From(() => _ = RetryMessageAsync(message, retryButton)));
                deliveryRow.AddChild(retryButton);
            }
        }

        return row;
    }

    private async Task SendDraftAsync()
    {
        if (_state == null || _send == null || _draftInput == null || _sendButton == null || _busy)
        {
            return;
        }

        string text = _draftInput.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _operationStatus = "请输入消息";
            UpdateAvailability();
            return;
        }
        if (!_state.ChatEnabled)
        {
            _operationStatus = "聊天暂不可用";
            UpdateAvailability();
            return;
        }

        _busy = true;
        UpdateAvailability();
        try
        {
            await _send(text);
            _draftInput.Text = string.Empty;
            _state.SetDraft(string.Empty);
            _operationStatus = "已提交";
            _draftInput.GrabFocus();
        }
        catch (Exception ex)
        {
            _operationStatus = $"发送失败：{ex.Message}";
        }
        finally
        {
            _busy = false;
            UpdateAvailability();
            _renderedRevision = _state.Revision;
        }
    }

    private async Task RetryMessageAsync(ServerChatMessageState message, Button button)
    {
        if (_send == null || _retry == null || button.Disabled)
        {
            return;
        }

        if (message.Delivery == ServerChatDeliveryState.DeliveryUnknown && message.DisconnectedAfterUnknown)
        {
            _confirmedUnknownText = message.Text;
            _unknownConfirmation?.PopupCentered(new Vector2I(420, 180));
            return;
        }

        button.Disabled = true;
        try
        {
            if (message.Delivery == ServerChatDeliveryState.Failed)
            {
                await _send(message.Text);
            }
            else if (message.Delivery == ServerChatDeliveryState.DeliveryUnknown &&
                     !string.IsNullOrEmpty(message.ClientMessageId))
            {
                await _retry(message.ClientMessageId);
            }
            _operationStatus = "已重试";
        }
        catch (Exception ex)
        {
            _operationStatus = $"重试失败：{ex.Message}";
        }
        finally
        {
            if (GodotObject.IsInstanceValid(button))
            {
                button.Disabled = false;
            }
            UpdateAvailability();
        }
    }

    private async Task SendConfirmedUnknownAsync()
    {
        string? text = _confirmedUnknownText;
        _confirmedUnknownText = null;
        if (_send == null || string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            await _send(text);
            _operationStatus = "已作为新消息发送";
        }
        catch (Exception ex)
        {
            _operationStatus = $"重发失败：{ex.Message}";
        }
        UpdateAvailability();
    }

    private void OnDraftChanged(string text)
    {
        _state?.SetDraft(text);
    }

    private void OnScrollChanged(double value)
    {
        if (_suppressScrollChange || _state == null || _messagesScroll == null)
        {
            return;
        }

        ScrollBar bar = _messagesScroll.GetVScrollBar();
        _state.SetScrollState(value, IsNearBottom(bar));
    }

    private void ScrollToBottom()
    {
        if (_state == null)
        {
            return;
        }

        ScrollToBottomWithoutConsumingState();
        double value = _messagesScroll?.GetVScrollBar().Value ?? 0;
        _state.SetScrollState(value, atBottom: true);
        UpdateNewMessagesButton();
        _renderedRevision = _state.Revision;
    }

    private void ScrollToBottomWithoutConsumingState()
    {
        if (_messagesScroll?.GetVScrollBar() is not ScrollBar bar)
        {
            return;
        }

        _suppressScrollChange = true;
        bar.Value = BottomValue(bar);
        _suppressScrollChange = false;
    }

    private void UpdateAvailability()
    {
        if (_state == null || _draftInput == null || _sendButton == null || _statusLabel == null)
        {
            return;
        }

        bool editable = _state.ChatEnabled && !_busy;
        _draftInput.Editable = editable;
        _sendButton.Disabled = !editable;
        _statusLabel.Text = !string.IsNullOrEmpty(_operationStatus)
            ? _operationStatus
            : _state.ChatEnabled ? "频道可用" : "聊天暂不可用";
        _statusLabel.AddThemeColorOverride(
            "font_color",
            _state.ChatEnabled ? TextMutedColor : WarningColor);
    }

    private void UpdateNewMessagesButton()
    {
        if (_newMessagesButton == null || _state == null)
        {
            return;
        }

        int count = _state.NewMessagesBelowCount;
        _newMessagesButton.Visible = count > 0;
        _newMessagesButton.Text = count > 0 ? $"有 {count} 条新消息" : string.Empty;
    }

    private void UpdateStateVisibility()
    {
        if (_state != null)
        {
            _state.SetVisible(IsInsideTree() && IsVisibleInTree());
        }
    }

    private static string DeliveryText(ServerChatMessageState message) =>
        message.Delivery switch
        {
            ServerChatDeliveryState.Pending => "发送中",
            ServerChatDeliveryState.Failed => $"发送失败：{message.ErrorMessage}",
            ServerChatDeliveryState.DeliveryUnknown when message.DisconnectedAfterUnknown => "可能已发送，确认后重发",
            ServerChatDeliveryState.DeliveryUnknown => "投递状态未知",
            _ => string.Empty
        };

    private static string RetryNodeSuffix(ServerChatMessageState message, int index)
    {
        string value = string.IsNullOrWhiteSpace(message.ClientMessageId)
            ? index.ToString()
            : message.ClientMessageId;
        return value.Replace("/", "_").Replace(":", "_").Replace("@", "_");
    }

    private static bool IsNearBottom(ScrollBar bar) =>
        BottomValue(bar) - bar.Value <= 8;

    private static double BottomValue(ScrollBar bar) =>
        Math.Max(bar.MinValue, bar.MaxValue - bar.Page);

    private static Label CreateLabel(string text, int size, Color color)
    {
        Label label = new() { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Button CreateButton(string text, bool accent)
    {
        Button button = new()
        {
            Text = text,
            FocusMode = FocusModeEnum.All,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        Color background = accent ? new Color(0.34f, 0.23f, 0.08f, 0.98f) : new Color(0.14f, 0.14f, 0.16f, 0.96f);
        Color border = accent ? AccentColor : new Color(0.36f, 0.34f, 0.31f, 1f);
        button.AddThemeStyleboxOverride("normal", CreateButtonStyle(background, border));
        button.AddThemeStyleboxOverride("hover", CreateButtonStyle(background.Lightened(0.08f), AccentColor));
        button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(background.Darkened(0.06f), AccentColor));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        return button;
    }

    private static void ApplyInputStyle(LineEdit input)
    {
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_placeholder_color", TextMutedColor);
        input.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.11f, 0.11f, 0.13f, 0.98f), new Color(0.34f, 0.32f, 0.29f, 1f)));
        input.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.13f, 0.13f, 0.15f, 1f), AccentColor));
        input.AddThemeStyleboxOverride("read_only", CreateButtonStyle(new Color(0.09f, 0.09f, 0.1f, 0.9f), new Color(0.28f, 0.27f, 0.25f, 1f)));
    }

    private static StyleBoxFlat CreateButtonStyle(Color background, Color border)
    {
        StyleBoxFlat style = new()
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
        return style;
    }

    private static StyleBoxFlat CreatePanelStyle(bool local)
    {
        StyleBoxFlat style = new()
        {
            BgColor = local ? new Color(0.19f, 0.16f, 0.1f, 0.96f) : new Color(0.12f, 0.12f, 0.14f, 0.96f),
            BorderColor = local ? new Color(0.46f, 0.34f, 0.14f, 1f) : new Color(0.27f, 0.27f, 0.29f, 1f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 10,
            ContentMarginTop = 8,
            ContentMarginRight = 10,
            ContentMarginBottom = 8
        };
        return style;
    }
}
