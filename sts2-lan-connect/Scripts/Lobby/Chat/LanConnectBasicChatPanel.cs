using System;
using System.Text;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;

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
    private const int MaxDraftScalars = 500;

    private readonly record struct ChatBinding(
        long Generation,
        long ContextGeneration,
        long DraftGeneration,
        LanConnectChatChannelState State,
        Func<string, Task> Send,
        Func<string, Task> Retry);

    private readonly record struct PendingUnknownConfirmation(
        ChatBinding Binding,
        string ClientMessageId,
        string Text);

    private readonly record struct SendOperationKey(
        LanConnectChatChannelState State,
        long ContextGeneration);

    private readonly record struct RetryOperationKey(
        LanConnectChatChannelState State,
        long ContextGeneration,
        string StableKey);

    private static readonly Color TextStrongColor = new(0.94f, 0.91f, 0.84f, 1f);
    private static readonly Color TextMutedColor = new(0.68f, 0.65f, 0.6f, 1f);
    private static readonly Color AccentColor = new(0.88f, 0.58f, 0.17f, 1f);
    private static readonly Color DangerColor = new(0.94f, 0.38f, 0.34f, 1f);
    private static readonly Color WarningColor = new(0.94f, 0.7f, 0.26f, 1f);

    private LanConnectChatChannelState? _state;
    private Func<string, Task>? _send;
    private Func<string, Task>? _retry;
    private Label? _titleLabel;
    private ScrollContainer? _messagesScroll;
    private VBoxContainer? _messagesList;
    private Button? _newMessagesButton;
    private TextEdit? _draftInput;
    private Button? _sendButton;
    private Label? _statusLabel;
    private ConfirmationDialog? _unknownConfirmation;
    private PendingUnknownConfirmation? _pendingUnknownConfirmation;
    private readonly HashSet<SendOperationKey> _sendInFlight = new();
    private readonly HashSet<RetryOperationKey> _retryInFlight = new();
    private long _renderedRevision = -1;
    private long _bindingGeneration;
    private long _boundContextGeneration;
    private long _scrollInteractionGeneration;
    private bool _suppressScrollChange;
    private bool _suppressDraftTextChanged;
    private string _acceptedDraftText = string.Empty;
    private string _operationStatus = string.Empty;

    internal LanConnectChatChannelState? State => _state;

    internal bool DraftHasFocus =>
        _draftInput != null &&
        GodotObject.IsInstanceValid(_draftInput) &&
        IsInsideTree() &&
        ReferenceEquals(GetViewport().GuiGetFocusOwner(), _draftInput);

    internal bool PopupVisible =>
        _unknownConfirmation != null &&
        GodotObject.IsInstanceValid(_unknownConfirmation) &&
        _unknownConfirmation.Visible;

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
            if (GodotObject.IsInstanceValid(this) && IsInsideTree())
            {
                focusOwnerName = GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? string.Empty;
            }

            bool inputEditable = _draftInput != null &&
                                 GodotObject.IsInstanceValid(_draftInput) &&
                                 _draftInput.Editable;

            return new LanConnectBasicChatPanelTestState(
                messages.Count,
                pending,
                failed,
                unknown,
                retries,
                _state?.ScrollOffset ?? 0,
                _state?.IsAtBottom ?? true,
                _state?.NewMessagesBelowCount ?? 0,
                inputEditable,
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
        if (_state != null && _state.ContextGeneration != _boundContextGeneration)
        {
            ResetForContextChange();
        }
        if (_state != null && _state.Revision != _renderedRevision)
        {
            Refresh();
        }
    }

    public override void _ExitTree()
    {
        _bindingGeneration++;
        _pendingUnknownConfirmation = null;
        _sendInFlight.Clear();
        _retryInFlight.Clear();
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

        _bindingGeneration++;
        _state = state;
        _boundContextGeneration = state.ContextGeneration;
        _send = send;
        _retry = retry;
        _renderedRevision = -1;
        _operationStatus = string.Empty;
        _pendingUnknownConfirmation = null;
        if (_unknownConfirmation != null && GodotObject.IsInstanceValid(_unknownConfirmation))
        {
            _unknownConfirmation.Hide();
        }
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

    internal void FocusDraft()
    {
        if (_draftInput != null && GodotObject.IsInstanceValid(_draftInput) && _draftInput.Editable)
        {
            _draftInput.GrabFocus();
        }
    }

    internal void ReleaseDraftFocus()
    {
        if (DraftHasFocus)
        {
            _draftInput!.ReleaseFocus();
        }
    }

    internal void ClosePopup()
    {
        if (PopupVisible)
        {
            _unknownConfirmation!.Hide();
            _pendingUnknownConfirmation = null;
        }
    }

    internal void HandleDraftEnter(bool shiftPressed)
    {
        if (_draftInput == null || !GodotObject.IsInstanceValid(_draftInput) || !_draftInput.Editable)
        {
            return;
        }

        if (shiftPressed)
        {
            _draftInput.InsertTextAtCaret("\n");
            UpdateDraftHeight();
            return;
        }

        _ = SendDraftAsync();
    }

    internal IReadOnlyList<Control> GetFocusChainControls()
    {
        List<Control> controls = new();
        AddFocusable(controls, _messagesScroll);
        if (_newMessagesButton?.Visible == true)
        {
            AddFocusable(controls, _newMessagesButton);
        }
        AddFocusable(controls, _draftInput);
        AddFocusable(controls, _sendButton);
        if (_messagesList != null && GodotObject.IsInstanceValid(_messagesList))
        {
            foreach (Node node in _messagesList.FindChildren(
                         LanConnectConstants.ChatRetryButtonPrefix + "*",
                         "Button",
                         recursive: true,
                         owned: false))
            {
                AddFocusable(controls, node as Button);
            }
        }
        return controls;
    }

    private void BuildControls()
    {
        AddThemeConstantOverride("separation", 8);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        HBoxContainer titleRow = new();
        titleRow.AddThemeConstantOverride("separation", 10);
        AddChild(titleRow);

        _titleLabel = CreateLabel("频道聊天", 16, TextStrongColor);
        _titleLabel.Name = "ChatChannelTitle";
        _titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleRow.AddChild(_titleLabel);

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

        _draftInput = new TextEdit
        {
            Name = LanConnectConstants.ChatDraftInputName,
            PlaceholderText = "输入消息",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 38),
            FocusMode = FocusModeEnum.All,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ScrollFitContentHeight = false,
            TabInputMode = false
        };
        ApplyInputStyle(_draftInput);
        _draftInput.Connect(TextEdit.SignalName.TextChanged, Callable.From(OnDraftChanged));
        _draftInput.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(OnDraftGuiInput));
        _draftInput.Connect(Control.SignalName.Resized, Callable.From(UpdateDraftHeight));
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
        if (_state == null ||
            _draftInput == null ||
            !GodotObject.IsInstanceValid(_draftInput))
        {
            return;
        }

        SetDraftTextFromState(_state.Draft, moveCaretToEnd: true);
        UpdateDraftHeight();
        _state.SetVisible(IsInsideTree() && IsVisibleInTree());
        Refresh();
    }

    private void ResetForContextChange()
    {
        if (_state == null)
        {
            return;
        }

        _bindingGeneration++;
        long currentContext = _state.ContextGeneration;
        _sendInFlight.RemoveWhere(key =>
            ReferenceEquals(key.State, _state) && key.ContextGeneration != currentContext);
        _retryInFlight.RemoveWhere(key =>
            ReferenceEquals(key.State, _state) && key.ContextGeneration != currentContext);
        _boundContextGeneration = currentContext;
        _renderedRevision = -1;
        _operationStatus = string.Empty;
        _pendingUnknownConfirmation = null;
        if (_unknownConfirmation != null && GodotObject.IsInstanceValid(_unknownConfirmation))
        {
            _unknownConfirmation.Hide();
        }
        ApplyBoundState();
    }

    private void CaptureCurrentViewState()
    {
        if (_state == null)
        {
            return;
        }

        if (_draftInput != null && GodotObject.IsInstanceValid(_draftInput))
        {
            _state.SetDraft(_draftInput.Text);
        }

        if (_messagesScroll != null &&
            GodotObject.IsInstanceValid(_messagesScroll) &&
            _messagesScroll.GetVScrollBar() is ScrollBar bar)
        {
            _state.SetScrollState(bar.Value, IsNearBottom(bar));
        }
    }

    private void Refresh()
    {
        if (!TryCaptureBinding(out ChatBinding binding) ||
            _messagesList == null || !GodotObject.IsInstanceValid(_messagesList) ||
            _draftInput == null || !GodotObject.IsInstanceValid(_draftInput) ||
            _sendButton == null || !GodotObject.IsInstanceValid(_sendButton))
        {
            return;
        }

        LanConnectChatChannelState state = binding.State;
        long renderedRevision = state.Revision;
        IReadOnlyList<ServerChatMessageState> messages = state.Messages;
        bool atBottom = state.IsAtBottom;
        double scrollOffset = state.ScrollOffset;
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
                _messagesList.AddChild(BuildMessageRow(state, messages[index], index));
            }
        }

        if (_messagesScroll?.GetVScrollBar() is ScrollBar bar)
        {
            bar.Value = atBottom ? BottomValue(bar) : scrollOffset;
        }
        _suppressScrollChange = false;

        if (!IsBindingCurrent(binding))
        {
            return;
        }

        if (_draftInput.Text != state.Draft)
        {
            SetDraftTextFromState(state.Draft, moveCaretToEnd: true);
            UpdateDraftHeight();
        }
        UpdateAvailability();
        UpdateNewMessagesButton();
        _renderedRevision = renderedRevision;
        if (atBottom)
        {
            Callable.From(() => ScrollToBottomWithoutConsumingState(binding.Generation)).CallDeferred();
        }
        else if (scrollOffset > 0)
        {
            long interactionGeneration = _scrollInteractionGeneration;
            TaskHelper.RunSafely(RestoreSavedScrollOffsetAfterLayoutAsync(
                binding,
                scrollOffset,
                interactionGeneration));
        }
    }

    private Control BuildMessageRow(
        LanConnectChatChannelState state,
        ServerChatMessageState message,
        int index)
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
                string stableKey = RetryStableKey(message, index);
                Button retryButton = CreateButton("重试", accent: false);
                retryButton.Name = LanConnectConstants.ChatRetryButtonPrefix + RetryNodeSuffix(message, index);
                retryButton.FocusMode = FocusModeEnum.All;
                retryButton.Disabled = _retryInFlight.Contains(
                    new RetryOperationKey(state, state.ContextGeneration, stableKey));
                retryButton.Connect(
                    Button.SignalName.Pressed,
                    Callable.From(() => _ = RetryMessageAsync(message, stableKey, retryButton)));
                deliveryRow.AddChild(retryButton);
            }
        }

        return row;
    }

    private async Task SendDraftAsync()
    {
        if (!TryCaptureBinding(out ChatBinding binding) ||
            !CanTouchControls(binding) ||
            _draftInput == null || !GodotObject.IsInstanceValid(_draftInput) ||
            _sendButton == null || !GodotObject.IsInstanceValid(_sendButton))
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
        if (!binding.State.ChatEnabled)
        {
            _operationStatus = "聊天暂不可用";
            UpdateAvailability();
            return;
        }

        SendOperationKey operationKey = new(binding.State, binding.ContextGeneration);
        if (!_sendInFlight.Add(operationKey))
        {
            return;
        }
        UpdateAvailability();
        try
        {
            await binding.Send(text);
            binding.State.ClearDraftIfMatches(
                binding.ContextGeneration,
                binding.DraftGeneration,
                text);
            if (!CanTouchControls(binding) || _draftInput == null || !GodotObject.IsInstanceValid(_draftInput))
            {
                return;
            }

            SetDraftTextFromState(binding.State.Draft, moveCaretToEnd: true);
            UpdateDraftHeight();
            _operationStatus = "已提交";
            _draftInput.GrabFocus();
        }
        catch (Exception ex)
        {
            if (CanTouchControls(binding))
            {
                _operationStatus = $"发送失败：{ex.Message}";
            }
        }
        finally
        {
            _sendInFlight.Remove(operationKey);
            if (CanTouchSameContext(binding))
            {
                UpdateAvailability();
            }
        }
    }

    private async Task RetryMessageAsync(
        ServerChatMessageState message,
        string stableKey,
        Button button)
    {
        if (!TryCaptureBinding(out ChatBinding binding) ||
            !CanTouchControls(binding) ||
            !GodotObject.IsInstanceValid(button) ||
            button.Disabled)
        {
            return;
        }

        if (message.Delivery == ServerChatDeliveryState.DeliveryUnknown && message.DisconnectedAfterUnknown)
        {
            if (string.IsNullOrEmpty(message.ClientMessageId))
            {
                return;
            }

            _pendingUnknownConfirmation = new PendingUnknownConfirmation(
                binding,
                message.ClientMessageId,
                message.Text);
            if (_unknownConfirmation != null && GodotObject.IsInstanceValid(_unknownConfirmation))
            {
                _unknownConfirmation.PopupCentered(new Vector2I(420, 180));
            }
            return;
        }

        RetryOperationKey operationKey = new(
            binding.State,
            binding.ContextGeneration,
            stableKey);
        if (!_retryInFlight.Add(operationKey))
        {
            return;
        }

        string buttonName = button.Name.ToString();
        button.Disabled = true;
        try
        {
            if (message.Delivery == ServerChatDeliveryState.Failed)
            {
                await binding.Send(message.Text);
            }
            else if (message.Delivery == ServerChatDeliveryState.DeliveryUnknown &&
                     !string.IsNullOrEmpty(message.ClientMessageId))
            {
                await binding.Retry(message.ClientMessageId);
            }

            if (CanTouchControls(binding))
            {
                _operationStatus = "已重试";
            }
        }
        catch (Exception ex)
        {
            if (CanTouchControls(binding))
            {
                _operationStatus = $"重试失败：{ex.Message}";
            }
        }
        finally
        {
            _retryInFlight.Remove(operationKey);
            if (CanTouchSameContext(binding))
            {
                Button? currentButton = FindCurrentRetryButton(buttonName);
                if (currentButton != null)
                {
                    currentButton.Disabled = false;
                }
            }
            if (CanTouchSameContext(binding))
            {
                UpdateAvailability();
            }
        }
    }

    private async Task SendConfirmedUnknownAsync()
    {
        PendingUnknownConfirmation? pending = _pendingUnknownConfirmation;
        _pendingUnknownConfirmation = null;
        if (!pending.HasValue || !IsBindingCurrent(pending.Value.Binding))
        {
            return;
        }

        PendingUnknownConfirmation confirmation = pending.Value;
        ServerChatMessageState? current = FindMessage(
            confirmation.Binding.State,
            confirmation.ClientMessageId);
        if (current == null ||
            current.Delivery != ServerChatDeliveryState.DeliveryUnknown ||
            !current.DisconnectedAfterUnknown ||
            !string.Equals(current.Text, confirmation.Text, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await confirmation.Binding.Send(confirmation.Text);
            if (CanTouchControls(confirmation.Binding))
            {
                _operationStatus = "已作为新消息发送";
            }
        }
        catch (Exception ex)
        {
            if (CanTouchControls(confirmation.Binding))
            {
                _operationStatus = $"重发失败：{ex.Message}";
            }
        }
        if (CanTouchControls(confirmation.Binding))
        {
            UpdateAvailability();
        }
    }

    private void OnDraftChanged()
    {
        if (_draftInput == null || !GodotObject.IsInstanceValid(_draftInput))
        {
            return;
        }
        if (_suppressDraftTextChanged)
        {
            return;
        }

        LanConnectChatDraftLimitResult limited = LanConnectChatDraftLimiter.Limit(
            _acceptedDraftText,
            _draftInput.Text,
            GetDraftCaretUtf16Offset(),
            MaxDraftScalars);
        if (!string.Equals(_draftInput.Text, limited.Text, StringComparison.Ordinal))
        {
            _suppressDraftTextChanged = true;
            _draftInput.Text = limited.Text;
            _suppressDraftTextChanged = false;
            SetDraftCaretUtf16Offset(limited.CaretUtf16Offset);
        }
        _acceptedDraftText = limited.Text;
        _state?.SetDraft(limited.Text);
        UpdateDraftHeight();
    }

    private void OnDraftGuiInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey
            {
                Pressed: true,
                Echo: false,
                Keycode: Key.Enter or Key.KpEnter
            } keyEvent)
        {
            return;
        }

        LanConnectChatInputAction action = LanConnectChatInputRouter.RouteEnter(
            inputFocused: true,
            shiftPressed: keyEvent.ShiftPressed,
            blockingModalVisible: PopupVisible);
        switch (action)
        {
            case LanConnectChatInputAction.Send:
                HandleDraftEnter(shiftPressed: false);
                _draftInput?.AcceptEvent();
                break;
            case LanConnectChatInputAction.InsertNewline:
                HandleDraftEnter(shiftPressed: true);
                _draftInput?.AcceptEvent();
                break;
        }
    }

    private void UpdateDraftHeight()
    {
        if (_draftInput == null || !GodotObject.IsInstanceValid(_draftInput))
        {
            return;
        }

        int visibleLines = Math.Clamp(_draftInput.GetTotalVisibleLineCount(), 1, 3);
        float lineHeight = Math.Max(18, _draftInput.GetLineHeight());
        _draftInput.CustomMinimumSize = new Vector2(0, 20 + visibleLines * lineHeight);
    }

    private void MoveDraftCaretToEnd()
    {
        if (_draftInput == null || !GodotObject.IsInstanceValid(_draftInput))
        {
            return;
        }

        int lastLine = Math.Max(0, _draftInput.GetLineCount() - 1);
        _draftInput.SetCaretLine(lastLine);
        _draftInput.SetCaretColumn(CountRunes(_draftInput.GetLine(lastLine)));
    }

    private void SetDraftTextFromState(string text, bool moveCaretToEnd)
    {
        if (_draftInput == null || !GodotObject.IsInstanceValid(_draftInput))
        {
            return;
        }

        LanConnectChatDraftLimitResult limited = LanConnectChatDraftLimiter.Limit(
            string.Empty,
            text ?? string.Empty,
            (text ?? string.Empty).Length,
            MaxDraftScalars);
        _suppressDraftTextChanged = true;
        _draftInput.Text = limited.Text;
        _suppressDraftTextChanged = false;
        _acceptedDraftText = limited.Text;
        if (_state != null && !string.Equals(_state.Draft, limited.Text, StringComparison.Ordinal))
        {
            _state.SetDraft(limited.Text);
        }
        if (moveCaretToEnd)
        {
            MoveDraftCaretToEnd();
        }
        else
        {
            SetDraftCaretUtf16Offset(limited.CaretUtf16Offset);
        }
    }

    private int GetDraftCaretUtf16Offset()
    {
        if (_draftInput == null || !GodotObject.IsInstanceValid(_draftInput))
        {
            return 0;
        }

        int caretLine = Math.Clamp(_draftInput.GetCaretLine(), 0, _draftInput.GetLineCount() - 1);
        int offset = 0;
        for (int line = 0; line < caretLine; line++)
        {
            offset += _draftInput.GetLine(line).Length + 1;
        }
        return offset + Utf16OffsetAtRuneIndex(
            _draftInput.GetLine(caretLine),
            _draftInput.GetCaretColumn());
    }

    private void SetDraftCaretUtf16Offset(int utf16Offset)
    {
        if (_draftInput == null || !GodotObject.IsInstanceValid(_draftInput))
        {
            return;
        }

        int remaining = Math.Clamp(utf16Offset, 0, _draftInput.Text.Length);
        int lastLine = Math.Max(0, _draftInput.GetLineCount() - 1);
        for (int line = 0; line <= lastLine; line++)
        {
            string lineText = _draftInput.GetLine(line);
            if (remaining <= lineText.Length || line == lastLine)
            {
                _draftInput.SetCaretLine(line);
                _draftInput.SetCaretColumn(RuneIndexAtUtf16Offset(lineText, remaining));
                return;
            }
            remaining -= lineText.Length + 1;
        }
    }

    private static int CountRunes(string text)
    {
        int count = 0;
        foreach (System.Text.Rune _ in text.EnumerateRunes())
        {
            count++;
        }
        return count;
    }

    private static int RuneIndexAtUtf16Offset(string text, int utf16Offset)
    {
        int clamped = Math.Clamp(utf16Offset, 0, text.Length);
        int consumed = 0;
        int count = 0;
        foreach (System.Text.Rune rune in text.EnumerateRunes())
        {
            int next = consumed + rune.Utf16SequenceLength;
            if (next > clamped)
            {
                break;
            }
            consumed = next;
            count++;
        }
        return count;
    }

    private static int Utf16OffsetAtRuneIndex(string text, int runeIndex)
    {
        int target = Math.Max(0, runeIndex);
        int offset = 0;
        int count = 0;
        foreach (System.Text.Rune rune in text.EnumerateRunes())
        {
            if (count++ >= target)
            {
                break;
            }
            offset += rune.Utf16SequenceLength;
        }
        return offset;
    }

    private void OnScrollChanged(double value)
    {
        if (_suppressScrollChange || _state == null || _messagesScroll == null)
        {
            return;
        }

        _scrollInteractionGeneration++;
        ScrollBar bar = _messagesScroll.GetVScrollBar();
        _state.SetScrollState(value, IsNearBottom(bar));
    }

    private void ScrollToBottom()
    {
        if (!TryCaptureBinding(out ChatBinding binding) || !CanTouchControls(binding))
        {
            return;
        }

        ScrollToBottomWithoutConsumingState();
        double value = _messagesScroll?.GetVScrollBar().Value ?? 0;
        binding.State.SetScrollState(value, atBottom: true);
        if (IsBindingCurrent(binding))
        {
            UpdateNewMessagesButton();
        }
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

    private void ScrollToBottomWithoutConsumingState(long generation)
    {
        if (generation != _bindingGeneration ||
            _state?.IsAtBottom != true ||
            !GodotObject.IsInstanceValid(this) ||
            IsQueuedForDeletion())
        {
            return;
        }

        ScrollToBottomWithoutConsumingState();
    }

    private async Task RestoreSavedScrollOffsetAfterLayoutAsync(
        ChatBinding binding,
        double offset,
        long interactionGeneration)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!CanTouchControls(binding) ||
            _scrollInteractionGeneration != interactionGeneration ||
            binding.State.IsAtBottom ||
            binding.State.ScrollOffset != offset ||
            _messagesScroll?.GetVScrollBar() is not ScrollBar bar)
        {
            return;
        }

        _suppressScrollChange = true;
        bar.Value = offset;
        _suppressScrollChange = false;
    }

    private void UpdateAvailability()
    {
        if (_state == null ||
            _draftInput == null || !GodotObject.IsInstanceValid(_draftInput) ||
            _sendButton == null || !GodotObject.IsInstanceValid(_sendButton) ||
            _statusLabel == null || !GodotObject.IsInstanceValid(_statusLabel))
        {
            return;
        }

        bool ready = _state.Presentation == LanConnectServerChatPresentation.Ready && _state.ChatEnabled;
        bool editable = ready && !_sendInFlight.Contains(
            new SendOperationKey(_state, _state.ContextGeneration));
        if (_titleLabel != null && GodotObject.IsInstanceValid(_titleLabel))
        {
            _titleLabel.Text = _state.Channel == LanConnectChatChannel.Room
                ? "房间聊天"
                : "频道聊天";
        }
        _draftInput.Editable = editable;
        _sendButton.Disabled = !editable;
        _statusLabel.Text = ready && !string.IsNullOrEmpty(_operationStatus)
            ? _operationStatus
            : PresentationText(_state);
        _statusLabel.AddThemeColorOverride(
            "font_color",
            ready ? TextMutedColor : WarningColor);
    }

    private void UpdateNewMessagesButton()
    {
        if (_newMessagesButton == null ||
            !GodotObject.IsInstanceValid(_newMessagesButton) ||
            _state == null)
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

    private bool TryCaptureBinding(out ChatBinding binding)
    {
        if (_state == null || _send == null || _retry == null)
        {
            binding = default;
            return false;
        }

        binding = new ChatBinding(
            _bindingGeneration,
            _boundContextGeneration,
            _state.DraftGeneration,
            _state,
            _send,
            _retry);
        return true;
    }

    private bool IsBindingCurrent(ChatBinding binding) =>
        binding.Generation == _bindingGeneration &&
        binding.ContextGeneration == _boundContextGeneration &&
        ReferenceEquals(binding.State, _state) &&
        binding.State.ContextGeneration == binding.ContextGeneration;

    private bool CanTouchControls(ChatBinding binding) =>
        IsBindingCurrent(binding) &&
        GodotObject.IsInstanceValid(this) &&
        IsInsideTree() &&
        !IsQueuedForDeletion();

    private bool CanTouchSameContext(ChatBinding binding) =>
        binding.ContextGeneration == _boundContextGeneration &&
        ReferenceEquals(binding.State, _state) &&
        binding.State.ContextGeneration == binding.ContextGeneration &&
        GodotObject.IsInstanceValid(this) &&
        IsInsideTree() &&
        !IsQueuedForDeletion();

    private static ServerChatMessageState? FindMessage(
        LanConnectChatChannelState state,
        string clientMessageId)
    {
        foreach (ServerChatMessageState message in state.Messages)
        {
            if (string.Equals(message.ClientMessageId, clientMessageId, StringComparison.Ordinal))
            {
                return message;
            }
        }

        return null;
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

    private static string PresentationText(LanConnectChatChannelState state) =>
        state.Presentation switch
        {
            LanConnectServerChatPresentation.Unsupported => "当前服务器不支持频道聊天",
            LanConnectServerChatPresentation.Connecting => "正在连接频道...",
            LanConnectServerChatPresentation.Reconnecting => "频道连接中断，正在重连...",
            LanConnectServerChatPresentation.Ready when state.Channel == LanConnectChatChannel.Room =>
                state.ChatEnabled ? "房间聊天可用" : "房间聊天暂不可用",
            LanConnectServerChatPresentation.Ready => state.ChatEnabled ? "频道可用" : "聊天暂不可用",
            LanConnectServerChatPresentation.Disabled => "频道已由服务器停用",
            LanConnectServerChatPresentation.TransportFailure when !string.IsNullOrWhiteSpace(state.PresentationDetail) =>
                $"频道连接失败：{state.PresentationDetail}",
            LanConnectServerChatPresentation.TransportFailure => "频道连接失败",
            _ => "聊天暂不可用"
        };

    private static string RetryNodeSuffix(ServerChatMessageState message, int index)
    {
        string value = string.IsNullOrWhiteSpace(message.ClientMessageId)
            ? index.ToString()
            : message.ClientMessageId;
        return value.Replace("/", "_").Replace(":", "_").Replace("@", "_");
    }

    private static string RetryStableKey(ServerChatMessageState message, int index) =>
        string.IsNullOrEmpty(message.ClientMessageId)
            ? $"index:{index}"
            : $"client:{message.ClientMessageId}";

    private Button? FindCurrentRetryButton(string buttonName)
    {
        if (_messagesList == null || !GodotObject.IsInstanceValid(_messagesList))
        {
            return null;
        }

        Button? button = _messagesList.FindChild(buttonName, recursive: true, owned: false) as Button;
        return button != null && GodotObject.IsInstanceValid(button) ? button : null;
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

    private static void ApplyInputStyle(TextEdit input)
    {
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_placeholder_color", TextMutedColor);
        input.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.11f, 0.11f, 0.13f, 0.98f), new Color(0.34f, 0.32f, 0.29f, 1f)));
        input.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.13f, 0.13f, 0.15f, 1f), AccentColor));
        input.AddThemeStyleboxOverride("read_only", CreateButtonStyle(new Color(0.09f, 0.09f, 0.1f, 0.9f), new Color(0.28f, 0.27f, 0.25f, 1f)));
    }

    private static void AddFocusable(List<Control> controls, Control? control)
    {
        if (control != null &&
            GodotObject.IsInstanceValid(control) &&
            control.Visible &&
            control.FocusMode != FocusModeEnum.None &&
            (control is not BaseButton button || !button.Disabled))
        {
            controls.Add(control);
        }
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
