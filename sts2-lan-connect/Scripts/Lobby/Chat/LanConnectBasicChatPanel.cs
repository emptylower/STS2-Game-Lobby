using System;
using System.Text;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectNamedControlRect(string Name, Rect2 Rect);

internal readonly record struct LanConnectBasicChatPanelTestState(
    int MessageCount,
    int PendingCount,
    int FailedCount,
    int DeliveryUnknownCount,
    int RetryButtonCount,
    double ScrollOffset,
    bool IsAtBottom,
    double RenderedScrollOffset,
    int NewMessagesBelowCount,
    bool InputEditable,
    string FocusOwnerName,
    Rect2 PanelRect,
    Rect2 MessagesRect,
    Rect2 DraftRect,
    Rect2 SendRect,
    IReadOnlyList<Rect2> RetryRects,
    Rect2 NewMessagesRect,
    IReadOnlyList<LanConnectNamedControlRect> FocusTargetRects);

internal sealed partial class LanConnectBasicChatPanel : VBoxContainer
{
    private readonly record struct ChatBinding(
        long Generation,
        long ContextGeneration,
        long DraftGeneration,
        LanConnectChatChannelState State,
        Func<string, Task>? SendText,
        Func<LanConnectChatContent, string, Task>? SendContent,
        Func<string, Task> Retry);

    private readonly record struct PendingUnknownConfirmation(
        ChatBinding Binding,
        string ClientMessageId,
        string Text,
        LanConnectChatContent Content);

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

    private readonly LanConnectLucideIconLoader _icons;
    private readonly Func<LanConnectItemRun, LanConnectResolvedItem> _resolveItem;
    private readonly Func<LanConnectItemResolverContext>? _resolverContextProvider;
    private LanConnectItemResolverContext _resolverContext;
    private bool _resolverContextInitialized;
    private LanConnectChatChannelState? _state;
    private Func<string, Task>? _send;
    private Func<LanConnectChatContent, string, Task>? _sendContent;
    private Func<string, Task>? _retry;
    private Func<bool> _blockingModalVisible = NeverBlocking;
    private Label? _titleLabel;
    private ScrollContainer? _messagesScroll;
    private VBoxContainer? _messagesList;
    private Button? _newMessagesButton;
    private LanConnectRichDraftEditor? _draftEditor;
    private Button? _emojiButton;
    private LanConnectEmojiPicker? _emojiPicker;
    private Button? _sendButton;
    private Label? _statusLabel;
    private LanConnectItemPreview? _itemPreview;
    private ConfirmationDialog? _unknownConfirmation;
    private PendingUnknownConfirmation? _pendingUnknownConfirmation;
    private readonly HashSet<SendOperationKey> _sendInFlight = new();
    private readonly HashSet<RetryOperationKey> _retryInFlight = new();
    private long _renderedRevision = -1;
    private string? _renderedMessageFingerprint;
    private long _bindingGeneration;
    private long _boundContextGeneration;
    private long _scrollInteractionGeneration;
    private long _messageFocusRestoreGeneration;
    private bool _suppressScrollChange;
    private bool _compactLayout;
    private string _operationStatus = string.Empty;
    private Action? _itemLinkPostInsertForTests;
    private int _visibilityPublishSuppressionDepth;
    private bool _publishVisibilityWhenSuppressionEnds;

    internal sealed class VisibilityPublishScope(LanConnectBasicChatPanel owner) : IDisposable
    {
        private LanConnectBasicChatPanel? _owner = owner;
        private bool _completed;

        internal void Complete() => _completed = true;

        public void Dispose()
        {
            LanConnectBasicChatPanel? current = Interlocked.Exchange(ref _owner, null);
            current?.EndVisibilityPublishSuppression(_completed);
        }
    }

    internal LanConnectBasicChatPanel()
        : this(
            LanConnectChatUiComposition.Icons,
            new LanConnectItemModelResolver(),
            CreateProductionResolverContextProvider())
    {
    }

    internal LanConnectBasicChatPanel(LanConnectLucideIconLoader icons)
        : this(
            icons,
            new LanConnectItemModelResolver(),
            CreateProductionResolverContextProvider())
    {
    }

    internal LanConnectBasicChatPanel(
        LanConnectLucideIconLoader icons,
        Func<LanConnectItemRun, LanConnectResolvedItem> resolveItem)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _resolveItem = resolveItem ?? throw new ArgumentNullException(nameof(resolveItem));
    }

    internal LanConnectBasicChatPanel(
        LanConnectLucideIconLoader icons,
        LanConnectItemModelResolver resolver,
        Func<LanConnectItemResolverContext> resolverContextProvider)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        ArgumentNullException.ThrowIfNull(resolver);
        _resolverContextProvider = resolverContextProvider ??
            throw new ArgumentNullException(nameof(resolverContextProvider));
        _resolveItem = run => resolver.Resolve(
            run,
            _resolverContext.Locale,
            _resolverContext.ModFingerprint);
    }

    internal LanConnectChatChannelState? State => _state;

    internal LanConnectItemPreview ItemPreviewForTests => _itemPreview ??
        throw new InvalidOperationException("The item preview is not ready.");

    internal bool DraftHasFocus =>
        _draftEditor != null &&
        GodotObject.IsInstanceValid(_draftEditor) &&
        _draftEditor.HasEditorFocus;

    internal bool PopupVisible =>
        EmojiPickerVisible || ConfirmationPopupVisible;

    internal bool EmojiPickerVisible =>
        _emojiPicker != null &&
        GodotObject.IsInstanceValid(_emojiPicker) &&
        _emojiPicker.Visible;

    internal bool ConfirmationPopupVisible =>
        _unknownConfirmation != null &&
        GodotObject.IsInstanceValid(_unknownConfirmation) &&
        _unknownConfirmation.Visible;

    private bool InteractionBlocked => PopupVisible || _blockingModalVisible();

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

            bool inputEditable = _draftEditor != null &&
                                 GodotObject.IsInstanceValid(_draftEditor) &&
                                 _draftEditor.Editable;

            return new LanConnectBasicChatPanelTestState(
                messages.Count,
                pending,
                failed,
                unknown,
                retries,
                _state?.ScrollOffset ?? 0,
                _state?.IsAtBottom ?? true,
                RenderedScrollOffsetForTests(),
                _state?.NewMessagesBelowCount ?? 0,
                inputEditable,
                focusOwnerName,
                RectForTests(this),
                RectForTests(_messagesScroll),
                RectForTests(_draftEditor),
                RectForTests(_sendButton),
                RetryRectsForTests(),
                RectForTests(_newMessagesButton),
                FocusTargetRectsForTests());
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
        if (InteractionBlocked)
        {
            ReleaseDraftFocus();
        }
        if (_state != null && _state.ContextGeneration != _boundContextGeneration)
        {
            ResetForContextChange();
        }
        bool resolverContextChanged = UpdateResolverContext();
        if (_state != null &&
            (resolverContextChanged || _state.Revision != _renderedRevision))
        {
            Refresh();
        }
    }

    public override void _ExitTree()
    {
        _itemPreview?.Invalidate(LanConnectItemPreviewInvalidation.ContextCleared);
        _bindingGeneration++;
        _messageFocusRestoreGeneration++;
        _pendingUnknownConfirmation = null;
        _sendInFlight.Clear();
        _retryInFlight.Clear();
        _state?.SetVisible(false);
    }

    internal void Bind(
        LanConnectChatChannelState state,
        Func<string, Task> send,
        Func<string, Task> retry,
        Func<bool>? blockingModalVisible = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(retry);

        bool restoreDraftFocus = _emojiPicker?.InvalidateBinding() == true;

        if (_state != null && !ReferenceEquals(_state, state))
        {
            _itemPreview?.Invalidate(LanConnectItemPreviewInvalidation.TabSwitched);
            CaptureCurrentViewState();
            if (_visibilityPublishSuppressionDepth == 0)
            {
                _state.SetVisible(false);
            }
        }

        _bindingGeneration++;
        _messageFocusRestoreGeneration++;
        _state = state;
        _boundContextGeneration = state.ContextGeneration;
        _send = send;
        _sendContent = null;
        _retry = retry;
        _blockingModalVisible = blockingModalVisible ?? NeverBlocking;
        _renderedRevision = -1;
        _renderedMessageFingerprint = null;
        _operationStatus = string.Empty;
        _pendingUnknownConfirmation = null;
        if (_unknownConfirmation != null && GodotObject.IsInstanceValid(_unknownConfirmation))
        {
            _unknownConfirmation.Hide();
        }
        if (_draftEditor != null)
        {
            ApplyBoundState();
            if (restoreDraftFocus)
            {
                _draftEditor.FocusEditor();
            }
        }
    }

    internal void BindStructured(
        LanConnectChatChannelState state,
        Func<LanConnectChatContent, string, Task> send,
        Func<string, Task> retry,
        Func<bool>? blockingModalVisible = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(retry);
        Bind(state, _ => throw new InvalidOperationException("Structured chat binding requires content."), retry,
            blockingModalVisible);
        _send = null;
        _sendContent = send;
        if (_draftEditor != null)
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
        if (!InteractionBlocked &&
            _draftEditor != null &&
            GodotObject.IsInstanceValid(_draftEditor) &&
            _draftEditor.Editable)
        {
            _draftEditor.FocusEditor();
        }
    }

    internal bool TryInsertItemAndFocus(
        LanConnectChatChannelState expectedState,
        LanConnectItemRun run)
    {
        ArgumentNullException.ThrowIfNull(expectedState);
        ArgumentNullException.ThrowIfNull(run);
        if (!CanInsertItem(expectedState))
        {
            return false;
        }

        return _draftEditor!.InsertItem(run, _itemLinkPostInsertForTests);
    }

    internal bool CanInsertItem(LanConnectChatChannelState expectedState)
    {
        ArgumentNullException.ThrowIfNull(expectedState);
        return IsInsideTree() &&
               ReferenceEquals(_state, expectedState) &&
               expectedState.Presentation == LanConnectServerChatPresentation.Ready &&
               expectedState.ChatEnabled &&
               !InteractionBlocked &&
               _draftEditor != null &&
               GodotObject.IsInstanceValid(_draftEditor) &&
               _draftEditor.Editable;
    }

    internal VisibilityPublishScope SuppressVisibilityPublishing()
    {
        _visibilityPublishSuppressionDepth++;
        return new VisibilityPublishScope(this);
    }

    internal void SetItemLinkPostInsertForTests(Action? callback) =>
        _itemLinkPostInsertForTests = callback;

    internal void SetCompactLayout(bool compact)
    {
        _compactLayout = compact;
        if (_messagesScroll != null && GodotObject.IsInstanceValid(_messagesScroll))
        {
            _messagesScroll.CustomMinimumSize = new Vector2(0, compact ? 72 : 140);
        }
        _draftEditor?.SetCompactLayout(compact);
    }

    internal void ReleaseDraftFocus()
    {
        if (DraftHasFocus)
        {
            _draftEditor!.ReleaseEditorFocus();
        }
    }

    internal void ClosePopup()
    {
        if (EmojiPickerVisible)
        {
            CloseEmojiPicker();
        }
        else if (ConfirmationPopupVisible)
        {
            _unknownConfirmation!.Hide();
            _pendingUnknownConfirmation = null;
        }
    }

    internal void CloseEmojiPicker(bool restoreDraftFocus = true)
    {
        if (EmojiPickerVisible)
        {
            _emojiPicker!.ClosePicker(restoreDraftFocus);
        }
    }

    internal void HandleDraftEnter(bool shiftPressed)
    {
        if (InteractionBlocked ||
            _draftEditor == null ||
            !GodotObject.IsInstanceValid(_draftEditor) ||
            !_draftEditor.Editable)
        {
            return;
        }

        if (shiftPressed)
        {
            _draftEditor.InsertNewline();
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
        AddFocusable(controls, _draftEditor?.FocusTarget);
        AddFocusable(controls, _emojiButton);
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
        _statusLabel.ClipText = true;
        _statusLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _statusLabel.CustomMinimumSize = Vector2.Zero;
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        titleRow.AddChild(_statusLabel);

        _messagesScroll = new ScrollContainer
        {
            Name = LanConnectConstants.ChatMessagesScrollName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
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
        _newMessagesButton.CustomMinimumSize = new Vector2(0, 34);
        _newMessagesButton.Visible = false;
        _newMessagesButton.Connect(Button.SignalName.Pressed, Callable.From(ScrollToBottom));
        AddChild(_newMessagesButton);

        HBoxContainer inputRow = new();
        inputRow.AddThemeConstantOverride("separation", 8);
        AddChild(inputRow);

        _draftEditor = new LanConnectRichDraftEditor
        {
            Name = LanConnectConstants.ChatRichDraftEditorName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _draftEditor.SubmitRequested += () => HandleDraftEnter(shiftPressed: false);
        _draftEditor.DraftChanged += OnRichDraftChanged;
        inputRow.AddChild(_draftEditor);

        _emojiButton = CreateButton(string.Empty, accent: false);
        _emojiButton.Name = LanConnectEmojiPicker.ToggleButtonName;
        _emojiButton.Icon = _icons.Get("smile", 20, AccentColor);
        _emojiButton.ExpandIcon = true;
        _emojiButton.TooltipText = "选择表情";
        _emojiButton.AccessibilityName = "选择表情";
        _emojiButton.CustomMinimumSize = new Vector2(38, 38);
        _emojiButton.Visible = false;
        _emojiButton.Connect(
            Button.SignalName.Pressed,
            Callable.From(() => _emojiPicker?.OpenPicker()));
        inputRow.AddChild(_emojiButton);

        _sendButton = CreateButton("发送", accent: true);
        _sendButton.Name = LanConnectConstants.ChatSendButtonName;
        _sendButton.Icon = _icons.Get("send", 18, TextStrongColor);
        _sendButton.ExpandIcon = true;
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

        _emojiPicker = new LanConnectEmojiPicker();
        _emojiPicker.Bind(
            _draftEditor,
            LanConnectChatEmojiSet.Version1,
            _icons,
            AccentColor,
            TemporaryEmojiLabel);
        _emojiPicker.FocusExitRequested += backwards =>
        {
            if (backwards)
            {
                _draftEditor?.FocusEditor();
            }
            else
            {
                _sendButton?.GrabFocus();
            }
        };
        AddChild(_emojiPicker);

        _itemPreview = new LanConnectItemPreview();
        AddChild(_itemPreview);

        Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(UpdateStateVisibility));
    }

    private void ApplyBoundState()
    {
        if (_state == null ||
            _draftEditor == null ||
            !GodotObject.IsInstanceValid(_draftEditor))
        {
            return;
        }

        UpdateResolverContext();

        _draftEditor.Bind(
            _state.RichDraft,
            _state.EnabledRichFeatures,
            LanConnectConfig.GetEffectivePlayerDisplayName(),
            AccessibleDraftLabel);
        _draftEditor.SetCompactLayout(_compactLayout);
        PublishStateVisibility();
        Refresh();
    }

    private void ResetForContextChange()
    {
        if (_state == null)
        {
            return;
        }

        bool restoreDraftFocus = _emojiPicker?.InvalidateBinding() == true;
        _itemPreview?.Invalidate(LanConnectItemPreviewInvalidation.ContextCleared);

        _bindingGeneration++;
        _messageFocusRestoreGeneration++;
        long currentContext = _state.ContextGeneration;
        _sendInFlight.RemoveWhere(key =>
            ReferenceEquals(key.State, _state) && key.ContextGeneration != currentContext);
        _retryInFlight.RemoveWhere(key =>
            ReferenceEquals(key.State, _state) && key.ContextGeneration != currentContext);
        _boundContextGeneration = currentContext;
        _renderedRevision = -1;
        _renderedMessageFingerprint = null;
        _operationStatus = string.Empty;
        _pendingUnknownConfirmation = null;
        if (_unknownConfirmation != null && GodotObject.IsInstanceValid(_unknownConfirmation))
        {
            _unknownConfirmation.Hide();
        }
        ApplyBoundState();
        if (restoreDraftFocus)
        {
            _draftEditor?.FocusEditor();
        }
    }

    private void CaptureCurrentViewState()
    {
        if (_state == null)
        {
            return;
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
            _draftEditor == null || !GodotObject.IsInstanceValid(_draftEditor) ||
            _sendButton == null || !GodotObject.IsInstanceValid(_sendButton))
        {
            return;
        }

        LanConnectChatChannelState state = binding.State;
        long renderedRevision = state.Revision;
        IReadOnlyList<ServerChatMessageState> messages = state.Messages;
        bool atBottom = state.IsAtBottom;
        double scrollOffset = state.ScrollOffset;
        string focusedMessageControlName = FocusedDescendantName(_messagesList);
        string messageFingerprint = BuildMessageFingerprint(state, messages);
        bool rebuildMessages = !string.Equals(
            _renderedMessageFingerprint,
            messageFingerprint,
            StringComparison.Ordinal);
        _suppressScrollChange = true;
        if (rebuildMessages)
        {
            _itemPreview?.Invalidate(LanConnectItemPreviewInvalidation.MessageRemoved);
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
            _renderedMessageFingerprint = messageFingerprint;
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

        _draftEditor.UpdateBudgetContext(
            state.EnabledRichFeatures,
            LanConnectConfig.GetEffectivePlayerDisplayName());
        RestoreMessageFocus(binding, focusedMessageControlName);
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
        sender.ClipText = true;
        sender.CustomMinimumSize = Vector2.Zero;
        sender.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        metadata.AddChild(sender);

        Label timestamp = CreateLabel(message.SentAt.ToLocalTime().ToString("HH:mm"), 11, TextMutedColor);
        metadata.AddChild(timestamp);

        body.AddChild(BuildMessageContent(message));

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
                retryButton.Icon = _icons.Get("refresh-cw", 16, TextStrongColor);
                retryButton.ExpandIcon = true;
                retryButton.FocusMode = FocusModeEnum.All;
                retryButton.CustomMinimumSize = new Vector2(64, 34);
                retryButton.Disabled = _retryInFlight.Contains(
                    new RetryOperationKey(state, state.ContextGeneration, stableKey));
                retryButton.Connect(
                    Control.SignalName.FocusEntered,
                    Callable.From(() => _messageFocusRestoreGeneration++));
                retryButton.Connect(
                    Button.SignalName.Pressed,
                    Callable.From(() => _ = RetryMessageAsync(message, stableKey, retryButton)));
                deliveryRow.AddChild(retryButton);
            }
        }

        return row;
    }

    private Control BuildMessageContent(ServerChatMessageState message)
    {
        HFlowContainer runs = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        runs.AddThemeConstantOverride("h_separation", 2);
        runs.AddThemeConstantOverride("v_separation", 4);
        IReadOnlyList<LanConnectChatSegment> segments = message.Content?.Segments ??
            Array.Empty<LanConnectChatSegment>();
        if (segments.Count == 0)
        {
            segments = [new LanConnectTextSegment(message.Text)];
        }
        for (int index = 0; index < segments.Count; index++)
        {
            Control run = BuildMessageRun(segments[index]);
            run.Name = $"ChatMessageRun{index}";
            runs.AddChild(run);
        }
        return runs;
    }

    private Control BuildMessageRun(LanConnectChatSegment segment)
    {
        switch (segment)
        {
            case LanConnectTextSegment text:
                Label label = CreateLabel(text.Text, 14, TextStrongColor);
                label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                label.MouseFilter = MouseFilterEnum.Ignore;
                return label;
            case LanConnectEmojiSegment emoji:
                if (!LanConnectChatEmojiSet.TryGet(emoji.EmojiId, out LanConnectEmojiDescriptor descriptor))
                {
                    return UnknownItemChip("未知表情");
                }
                return new TextureRect
                {
                    Texture = _icons.Get(descriptor.LucideIcon, 20, AccentColor),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(22, 22),
                    TooltipText = descriptor.LabelKey,
                    AccessibilityName = descriptor.LabelKey,
                    MouseFilter = MouseFilterEnum.Ignore
                };
            case LanConnectItemRefSegment item:
                return BuildItemRun(item);
            default:
                return UnknownItemChip("未知内容");
        }
    }

    private Control BuildItemRun(LanConnectItemRefSegment item)
    {
        LanConnectResolvedItem resolved;
        try
        {
            resolved = _resolveItem(new LanConnectItemRun(item.ItemType, item.ModelId, item.UpgradeLevel));
        }
        catch
        {
            resolved = UnknownResolvedItem(item.ItemType);
        }
        if (resolved.Status != LanConnectResolvedItemStatus.Resolved ||
            string.IsNullOrWhiteSpace(resolved.LocalizedTitle) ||
            resolved.Preview == null)
        {
            return UnknownItemChip(UnknownItemLabel(resolved));
        }

        PanelContainer chip = ItemChip(resolved.LocalizedTitle);
        chip.SetMeta("lan_connect_resolved_item", true);
        chip.AccessibilityName = resolved.AccessibleText;
        chip.MouseFilter = MouseFilterEnum.Stop;
        chip.MouseEntered += () => ShowItemPreview(chip, resolved);
        chip.MouseExited += () => _itemPreview?.Invalidate(LanConnectItemPreviewInvalidation.PointerExited);
        return chip;
    }

    private void ShowItemPreview(Control owner, LanConnectResolvedItem item)
    {
        if (_itemPreview == null ||
            !GodotObject.IsInstanceValid(_itemPreview) ||
            !GodotObject.IsInstanceValid(owner) ||
            !owner.IsInsideTree())
        {
            return;
        }
        _itemPreview.ShowResolved(item, owner.GetGlobalRect(), GetViewport().GetVisibleRect());
    }

    private static PanelContainer UnknownItemChip(string label)
    {
        PanelContainer chip = ItemChip(label);
        chip.MouseFilter = MouseFilterEnum.Ignore;
        chip.AccessibilityName = label;
        return chip;
    }

    private static PanelContainer ItemChip(string label)
    {
        PanelContainer chip = new();
        StyleBoxFlat style = new()
        {
            BgColor = new Color(0.18f, 0.17f, 0.15f, 1f),
            BorderColor = AccentColor,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 2,
            ContentMarginBottom = 2
        };
        chip.AddThemeStyleboxOverride("panel", style);
        chip.AddChild(CreateLabel(label, 13, TextStrongColor));
        return chip;
    }

    private static string UnknownItemLabel(LanConnectResolvedItem item) =>
        !string.IsNullOrWhiteSpace(item.AccessibleText) &&
        !item.AccessibleText.StartsWith("chat.unknown_", StringComparison.Ordinal)
            ? item.AccessibleText
            : item.ItemType switch
            {
                "card" => "未知卡牌",
                "relic" => "未知遗物",
                "potion" => "未知药水",
                _ => "未知物品"
            };

    private static LanConnectResolvedItem UnknownResolvedItem(string itemType) => new(
        LanConnectResolvedItemStatus.Unknown,
        itemType,
        "chat.unknown_item",
        null,
        itemType switch
        {
            "card" => "未知卡牌",
            "relic" => "未知遗物",
            "potion" => "未知药水",
            _ => "未知物品"
        },
        null);

    private async Task SendDraftAsync()
    {
        if (InteractionBlocked ||
            !TryCaptureBinding(out ChatBinding binding) ||
            !CanTouchControls(binding) ||
            _draftEditor == null || !GodotObject.IsInstanceValid(_draftEditor) ||
            _sendButton == null || !GodotObject.IsInstanceValid(_sendButton))
        {
            return;
        }

        string text = binding.State.Draft;
        LanConnectChatContent content = binding.State.RichDraft.ToContent();
        if (!CanSendCurrentDraft())
        {
            _operationStatus = CurrentDraftBlockingReason();
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
        bool restoreFocus = false;
        try
        {
            if (binding.SendContent != null)
            {
                LanConnectChatContent canonical = LanConnectServerChatProtocol.Canonicalize(
                    content,
                    binding.State.EnabledRichFeatures);
                LanConnectServerChatProtocol.AssertInboundBudget(
                    canonical,
                    LanConnectConfig.GetEffectivePlayerDisplayName());
                await binding.SendContent(canonical, Guid.NewGuid().ToString("D"));
            }
            else if (binding.SendText != null)
            {
                await binding.SendText(text);
            }
            binding.State.ClearDraftIfMatches(
                binding.ContextGeneration,
                binding.DraftGeneration,
                text);
            if (!CanTouchControls(binding) || _draftEditor == null || !GodotObject.IsInstanceValid(_draftEditor))
            {
                return;
            }

            _operationStatus = "已提交";
            _draftEditor.RefreshFromDraft(preserveFocus: false);
            restoreFocus = true;
        }
        catch (Exception ex)
        {
            if (CanTouchControls(binding))
            {
                _operationStatus = $"发送失败：{ex.Message}";
                _draftEditor.FocusEditor();
            }
        }
        finally
        {
            _sendInFlight.Remove(operationKey);
            if (CanTouchSameContext(binding))
            {
                UpdateAvailability();
                if (restoreFocus && _draftEditor != null && GodotObject.IsInstanceValid(_draftEditor))
                {
                    _draftEditor.FocusEditor();
                }
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
                message.Text,
                message.Content);
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
                if (binding.SendContent != null)
                {
                    await binding.SendContent(message.Content, Guid.NewGuid().ToString("D"));
                }
                else if (binding.SendText != null)
                {
                    await binding.SendText(message.Text);
                }
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
            if (confirmation.Binding.SendContent != null)
            {
                await confirmation.Binding.SendContent(
                    confirmation.Content,
                    Guid.NewGuid().ToString("D"));
            }
            else if (confirmation.Binding.SendText != null)
            {
                await confirmation.Binding.SendText(confirmation.Text);
            }
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

    private void OnRichDraftChanged()
    {
        _operationStatus = string.Empty;
        UpdateAvailability();
    }

    private static Func<LanConnectItemResolverContext> CreateProductionResolverContextProvider()
    {
        LanConnectProductionItemResolverContextProvider provider = new();
        return () => provider.Current;
    }

    private bool UpdateResolverContext()
    {
        if (_resolverContextProvider == null)
        {
            return false;
        }

        LanConnectItemResolverContext next = _resolverContextProvider();
        if (_resolverContextInitialized && next == _resolverContext)
        {
            return false;
        }

        bool changed = _resolverContextInitialized;
        _resolverContext = next;
        _resolverContextInitialized = true;
        if (changed)
        {
            _itemPreview?.Invalidate(LanConnectItemPreviewInvalidation.ContextCleared);
            _renderedRevision = -1;
            _renderedMessageFingerprint = null;
        }
        return changed;
    }

    private static bool NeverBlocking() => false;

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
            _draftEditor == null || !GodotObject.IsInstanceValid(_draftEditor) ||
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
        _draftEditor.Editable = editable;
        LanConnectChatFeatureVersions features = _state.EnabledRichFeatures;
        bool emojiAvailable = features.RichContentVersion == 1 &&
                              features.EmojiSetVersion == LanConnectChatEmojiSet.Version;
        if (_emojiButton != null && GodotObject.IsInstanceValid(_emojiButton))
        {
            _emojiButton.Visible = emojiAvailable;
            _emojiButton.Disabled = !editable;
        }
        _emojiPicker?.SetAvailable(emojiAvailable && editable);
        _sendButton.Disabled = !editable ||
            (!CanSendCurrentDraft() && !IsBlankDraft());
        _statusLabel.Text = ready && !string.IsNullOrEmpty(_operationStatus)
            ? _operationStatus
            : ready && !CanSendCurrentDraft()
                ? CurrentDraftBlockingReason()
            : PresentationText(_state);
        _statusLabel.AddThemeColorOverride(
            "font_color",
            ready && CanSendCurrentDraft() ? TextMutedColor : WarningColor);
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

    private bool CanSendCurrentDraft() =>
        _draftEditor != null &&
        _draftEditor.Budget.CanSubmit &&
        (_sendContent != null || _draftEditor.Budget.EntityCount == 0);

    private bool IsBlankDraft() =>
        _draftEditor?.IsBlank == true;

    private string FocusedDescendantName(Control root)
    {
        if (!IsInsideTree() || GetViewport().GuiGetFocusOwner() is not Control focusOwner)
        {
            return string.Empty;
        }
        return root.IsAncestorOf(focusOwner) ? focusOwner.Name.ToString() : string.Empty;
    }

    private void RestoreMessageFocus(ChatBinding binding, string controlName)
    {
        long restoreGeneration = ++_messageFocusRestoreGeneration;
        if (string.IsNullOrEmpty(controlName) || _messagesList == null)
        {
            return;
        }
        if (_messagesList.FindChild(
                controlName,
                recursive: true,
                owned: false) is not Control replacement)
        {
            return;
        }
        Callable.From(() =>
        {
            if (restoreGeneration != _messageFocusRestoreGeneration ||
                !IsBindingCurrent(binding) ||
                !GodotObject.IsInstanceValid(replacement) ||
                !replacement.IsInsideTree() ||
                _messagesList == null ||
                !GodotObject.IsInstanceValid(_messagesList))
            {
                return;
            }
            Control? focusOwner = GetViewport().GuiGetFocusOwner();
            if (focusOwner != null &&
                GodotObject.IsInstanceValid(focusOwner) &&
                !_messagesList.IsAncestorOf(focusOwner))
            {
                return;
            }
            if (focusOwner != null &&
                GodotObject.IsInstanceValid(focusOwner) &&
                _messagesList.IsAncestorOf(focusOwner) &&
                !focusOwner.IsQueuedForDeletion() &&
                !ReferenceEquals(focusOwner, replacement))
            {
                return;
            }
            replacement.GrabFocus();
        }).CallDeferred();
    }

    private string CurrentDraftBlockingReason()
    {
        if (_draftEditor == null)
        {
            return "聊天暂不可用";
        }
        if (!string.IsNullOrEmpty(_draftEditor.BlockingReason))
        {
            return _draftEditor.BlockingReason;
        }
        return _sendContent == null && _draftEditor.Budget.EntityCount > 0
            ? "当前发送通道尚未启用富内容发送"
            : string.Empty;
    }

    private void UpdateStateVisibility()
    {
        PublishStateVisibility();
    }

    private void PublishStateVisibility()
    {
        if (_visibilityPublishSuppressionDepth == 0 && _state != null)
        {
            _state.SetVisible(IsInsideTree() && IsVisibleInTree());
        }
    }

    private void EndVisibilityPublishSuppression(bool publishCurrentVisibility)
    {
        if (_visibilityPublishSuppressionDepth <= 0)
        {
            return;
        }
        _publishVisibilityWhenSuppressionEnds |= publishCurrentVisibility;
        _visibilityPublishSuppressionDepth--;
        if (_visibilityPublishSuppressionDepth == 0)
        {
            bool publish = _publishVisibilityWhenSuppressionEnds;
            _publishVisibilityWhenSuppressionEnds = false;
            if (publish)
            {
                PublishStateVisibility();
            }
        }
    }

    private bool TryCaptureBinding(out ChatBinding binding)
    {
        if (_state == null || (_send == null && _sendContent == null) || _retry == null)
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
            _sendContent,
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

    private static string AccessibleDraftLabel(LanConnectDraftRun run) => run switch
    {
        LanConnectEmojiRun => "Emoji",
        LanConnectItemRun { ItemType: "card" } => "卡牌",
        LanConnectItemRun { ItemType: "relic" } => "遗物",
        LanConnectItemRun { ItemType: "potion" } => "药水",
        _ => "消息实体"
    };

    private string BuildMessageFingerprint(
        LanConnectChatChannelState state,
        IReadOnlyList<ServerChatMessageState> messages)
    {
        StringBuilder builder = new();
        AppendFingerprint(builder, _resolverContext.Locale);
        AppendFingerprint(builder, _resolverContext.ModFingerprint);
        for (int index = 0; index < messages.Count; index++)
        {
            ServerChatMessageState message = messages[index];
            AppendFingerprint(builder, message.MessageId);
            AppendFingerprint(builder, message.ClientMessageId);
            AppendFingerprint(builder, message.SenderName);
            AppendFingerprint(builder, message.Text);
            AppendFingerprint(
                builder,
                LanConnectServerChatProtocol.DeterministicContentJson(message.Content));
            AppendFingerprint(builder, message.ErrorCode);
            AppendFingerprint(builder, message.ErrorMessage);
            builder.Append('|').Append((int)message.Delivery)
                .Append('|').Append(message.IsLocal)
                .Append('|').Append(message.DisconnectedAfterUnknown)
                .Append('|').Append(message.Sequence)
                .Append('|').Append(message.SentAt.UtcTicks);
            string stableKey = RetryStableKey(message, index);
            builder.Append('|').Append(_retryInFlight.Contains(
                new RetryOperationKey(state, state.ContextGeneration, stableKey)));
        }
        return builder.ToString();
    }

    private static void AppendFingerprint(StringBuilder builder, string? value)
    {
        string text = value ?? string.Empty;
        builder.Append('|').Append(text.Length).Append(':').Append(text);
    }

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

    private static string TemporaryEmojiLabel(string key) => key switch
    {
        "chat.emoji.smile" => "微笑",
        "chat.emoji.laugh" => "大笑",
        "chat.emoji.heart" => "爱心",
        "chat.emoji.thumbs-up" => "赞同",
        "chat.emoji.thumbs-down" => "反对",
        "chat.emoji.sparkles" => "闪光",
        "chat.emoji.flame" => "火焰",
        "chat.emoji.zap" => "闪电",
        "chat.emoji.shield" => "护盾",
        "chat.emoji.swords" => "双剑",
        "chat.emoji.target" => "目标",
        "chat.emoji.crown" => "皇冠",
        "chat.emoji.skull" => "骷髅",
        "chat.emoji.ghost" => "幽灵",
        "chat.emoji.eye" => "眼睛",
        "chat.emoji.message-circle" => "消息",
        "chat.emoji.check" => "确认",
        "chat.emoji.x" => "取消",
        _ => "表情"
    };

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

    private IReadOnlyList<Rect2> RetryRectsForTests()
    {
        if (_messagesList == null || !GodotObject.IsInstanceValid(_messagesList))
        {
            return Array.Empty<Rect2>();
        }

        List<Rect2> rects = new();
        foreach (Node node in _messagesList.FindChildren(
                     LanConnectConstants.ChatRetryButtonPrefix + "*",
                     "Button",
                     recursive: true,
                     owned: false))
        {
            if (node is Control { Visible: true } control)
            {
                rects.Add(RectForTests(control));
            }
        }
        return rects;
    }

    private IReadOnlyList<LanConnectNamedControlRect> FocusTargetRectsForTests()
    {
        List<LanConnectNamedControlRect> rects = new();
        foreach (Control control in GetFocusChainControls())
        {
            Rect2 rect = RectForTests(control);
            if (rect.Size.X > 0f && rect.Size.Y > 0f)
            {
                rects.Add(new LanConnectNamedControlRect(control.Name.ToString(), rect));
            }
        }
        return rects;
    }

    private double RenderedScrollOffsetForTests() =>
        _messagesScroll?.GetVScrollBar() is ScrollBar bar ? bar.Value : 0d;

    private static Rect2 RectForTests(Control? control) =>
        control != null && GodotObject.IsInstanceValid(control) && control.IsInsideTree() && control.Visible
            ? control.GetGlobalRect()
            : default;

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
