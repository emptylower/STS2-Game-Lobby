using Godot;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectEmojiPickerTestState(
    int Columns,
    int Rows,
    IReadOnlyList<string> EmojiIds,
    int FocusedIndex,
    bool Available,
    bool Visible,
    Rect2 Bounds,
    IReadOnlyList<LanConnectNamedControlRect> ButtonRects);

internal sealed partial class LanConnectEmojiPicker : PopupPanel
{
    internal const string PickerName = "ChatEmojiPicker";
    internal const string GridName = "ChatEmojiGrid";
    internal const string ToggleButtonName = "ChatEmojiButton";
    internal const string ButtonPrefix = "ChatEmoji_";
    internal const int Columns = 6;

    private static readonly Color LobbySurfaceColor = new(0.99f, 0.97f, 0.93f, 1f);
    private static readonly Color LobbySecondaryColor = new(0.93f, 0.89f, 0.82f, 1f);
    private static readonly Color LobbyAccentColor = new(0.87f, 0.41f, 0.00f, 1f);
    private static readonly Color LobbyBorderColor = new(0.80f, 0.65f, 0.53f, 1f);

    private readonly List<Button> _buttons = [];
    private LanConnectRichDraftEditor? _editor;
    private IReadOnlyList<LanConnectEmojiDescriptor> _emojis = Array.Empty<LanConnectEmojiDescriptor>();
    private Func<string, Texture2D>? _icon;
    private Func<string, string>? _localize;
    private GridContainer? _grid;
    private Control? _previousFocus;
    private bool _available = true;
    private bool _openIntent;
    private long _bindingGeneration;
    private long _focusIntentGeneration;
    private bool _explicitHideSettling;
    private bool _explicitHideCompletionScheduled;
    private bool _openQueuedAfterExplicitHide;
    private long _explicitHideGeneration;
    private int _lastFocusedIndex;

    internal LanConnectChatVisualStyle ChatVisualStyle { get; init; } =
        LanConnectChatVisualStyle.DarkOverlay;

    private bool UsesLobbyStyle => ChatVisualStyle == LanConnectChatVisualStyle.LobbySidebar;

    internal event Action<string>? Inserted;

    internal event Action<bool>? FocusExitRequested;

    internal LanConnectEmojiPickerTestState TestState
    {
        get
        {
            int focusedIndex = -1;
            if (IsInsideTree() && GetViewport().GuiGetFocusOwner() is Control focused &&
                focused.HasMeta("emoji_index"))
            {
                focusedIndex = focused.GetMeta("emoji_index").AsInt32();
            }
            return new LanConnectEmojiPickerTestState(
                Columns,
                (_emojis.Count + Columns - 1) / Columns,
                _emojis.Select(emoji => emoji.Id).ToArray(),
                focusedIndex,
                _available,
                Visible,
                Visible ? new Rect2(Position, Size) : default,
                _buttons
                    .Where(button => button.Visible && button.IsInsideTree())
                    .Select(button => new LanConnectNamedControlRect(
                        button.Name.ToString(),
                        button.GetGlobalRect()))
                    .ToArray());
        }
    }

    internal void Bind(
        LanConnectRichDraftEditor editor,
        IReadOnlyList<LanConnectEmojiDescriptor> emojis,
        Func<string, Texture2D> icon,
        Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(emojis);
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(localize);
        bool restoreFocus = InvalidateBinding();
        _editor = editor;
        _emojis = emojis.ToArray();
        _icon = icon;
        _localize = localize;
        RefreshLocalization();
        if (restoreFocus && editor.IsInsideTree())
        {
            editor.FocusEditor();
        }
    }

    internal void Bind(
        LanConnectRichDraftEditor editor,
        IReadOnlyList<LanConnectEmojiDescriptor> emojis,
        LanConnectLucideIconLoader icons,
        Color iconColor,
        Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(icons);
        Bind(editor, emojis, name => icons.Get(name, 20, iconColor), localize);
    }

    internal void SetAvailable(bool available)
    {
        if (_available == available)
        {
            return;
        }
        _available = available;
        if (!available)
        {
            if (_openIntent || Visible || PickerOwnsFocus())
            {
                ClosePicker();
            }
            else
            {
                InvalidateOpenIntent();
                HideExplicitly();
            }
        }
    }

    internal void RefreshLocalization()
    {
        if (_localize == null)
        {
            return;
        }
        int focusedIndex = TestState.FocusedIndex >= 0 ? TestState.FocusedIndex : _lastFocusedIndex;
        bool restorePickerFocus = Visible && _openIntent && focusedIndex >= 0;
        Title = _localize("chat.emoji.picker_title");
        if (_grid != null)
        {
            bool canUpdateInPlace = _buttons.Count == _emojis.Count &&
                                    _buttons.Select(button => button.Name.ToString()).SequenceEqual(
                                        _emojis.Select(emoji => ButtonPrefix + emoji.Id),
                                        StringComparer.Ordinal);
            if (canUpdateInPlace)
            {
                for (int index = 0; index < _buttons.Count; index++)
                {
                    string label = _localize(_emojis[index].LabelKey);
                    _buttons[index].TooltipText = label;
                    _buttons[index].AccessibilityName = label;
                }
            }
            else
            {
                RebuildButtons();
            }
        }
        if (restorePickerFocus && focusedIndex < _buttons.Count)
        {
            Button focusTarget = _buttons[focusedIndex];
            Callable.From(() =>
            {
                if (Visible && _openIntent &&
                    GodotObject.IsInstanceValid(focusTarget) &&
                    focusTarget.IsInsideTree())
                {
                    focusTarget.GrabFocus();
                }
            }).CallDeferred();
        }
    }

    internal bool InvalidateBinding()
    {
        bool wasActive = _openIntent || Visible || PickerOwnsFocus();
        _bindingGeneration++;
        InvalidateOpenIntent();
        HideExplicitly();
        return wasActive;
    }

    internal void OpenPicker()
    {
        if (!_available || _editor == null || _buttons.Count == 0 || !IsInsideTree())
        {
            return;
        }
        if (_explicitHideSettling)
        {
            _openQueuedAfterExplicitHide = true;
            return;
        }
        Control? current = GetViewport().GuiGetFocusOwner();
        _previousFocus = current != null && _editor.IsAncestorOf(current)
            ? current
            : _editor.FocusTarget;
        Control? focusAtOpen = current;
        LanConnectRichDraftEditor editor = _editor;
        long bindingGeneration = _bindingGeneration;
        long focusIntentGeneration = ++_focusIntentGeneration;
        _openIntent = true;
        PopupCentered(new Vector2I(Columns * 42 + 20, 3 * 42 + 20));
        _ = CompleteOpenIntentAfterFrameAsync(
            editor,
            focusAtOpen,
            bindingGeneration,
            focusIntentGeneration);
    }

    private async Task CompleteOpenIntentAfterFrameAsync(
        LanConnectRichDraftEditor editor,
        Control? focusAtOpen,
        long bindingGeneration,
        long focusIntentGeneration)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() ||
            focusIntentGeneration != _focusIntentGeneration ||
            bindingGeneration != _bindingGeneration ||
            !ReferenceEquals(_editor, editor) ||
            !_openIntent || !_available ||
            _buttons.Count == 0 || !GodotObject.IsInstanceValid(_buttons[0]))
        {
            return;
        }
        Control? currentFocus = GetViewport().GuiGetFocusOwner();
        if (currentFocus != null &&
            !ReferenceEquals(currentFocus, focusAtOpen) &&
            !IsPickerControl(currentFocus))
        {
            return;
        }
        if (!Visible)
        {
            PopupCentered(new Vector2I(Columns * 42 + 20, 3 * 42 + 20));
        }
        if (Visible)
        {
            _buttons[0].GrabFocus();
        }
    }

    internal void ClosePicker(bool restoreDraftFocus = true)
    {
        LanConnectRichDraftEditor? editor = _editor;
        Control? restoreFocus = _previousFocus;
        InvalidateOpenIntent();
        HideExplicitly();
        if (!restoreDraftFocus || editor == null ||
            !GodotObject.IsInstanceValid(editor) ||
            editor.IsQueuedForDeletion() || !editor.IsInsideTree())
        {
            return;
        }
        if (restoreFocus != null &&
            GodotObject.IsInstanceValid(restoreFocus) &&
            !restoreFocus.IsQueuedForDeletion() &&
            restoreFocus.IsInsideTree() &&
            editor.IsAncestorOf(restoreFocus))
        {
            restoreFocus.GrabFocus();
        }
        else
        {
            editor.FocusEditor();
        }
    }

    public override void _Ready()
    {
        Name = PickerName;
        Unresizable = true;
        if (UsesLobbyStyle)
        {
            AddThemeStyleboxOverride("panel", CreatePickerPanelStyle());
        }
        BuildGrid();
        Hide();
        PopupHide += OnPopupHide;
    }

    public override void _ExitTree()
    {
        HideExplicitly();
        InvalidateOpenIntent();
        _explicitHideGeneration++;
        _explicitHideSettling = false;
        _explicitHideCompletionScheduled = false;
    }

    private void BuildGrid()
    {
        _grid = new GridContainer
        {
            Name = GridName,
            Columns = Columns
        };
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        AddChild(_grid);
        RebuildButtons();
    }

    private void RebuildButtons()
    {
        if (_grid == null || _icon == null || _localize == null)
        {
            return;
        }
        foreach (Button button in _buttons)
        {
            if (GodotObject.IsInstanceValid(button))
            {
                if (ReferenceEquals(button.GetParent(), _grid))
                {
                    _grid.RemoveChild(button);
                }
                button.Free();
            }
        }
        _buttons.Clear();
        for (int index = 0; index < _emojis.Count; index++)
        {
            int capturedIndex = index;
            LanConnectEmojiDescriptor emoji = _emojis[index];
            string label = _localize(emoji.LabelKey);
            Button button = new()
            {
                Name = ButtonPrefix + emoji.Id,
                Text = string.Empty,
                Icon = _icon(emoji.LucideIcon),
                ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Center,
                TooltipText = label,
                AccessibilityName = label,
                CustomMinimumSize = new Vector2(38, 38),
                FocusMode = Control.FocusModeEnum.All,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand
            };
            button.SetMeta("emoji_index", index);
            if (UsesLobbyStyle)
            {
                ApplyLobbyButtonStyle(button);
            }
            button.Connect(
                Control.SignalName.FocusEntered,
                Callable.From(() => _lastFocusedIndex = capturedIndex));
            button.Connect(Button.SignalName.Pressed, Callable.From(() => Insert(capturedIndex)));
            button.Connect(
                Control.SignalName.GuiInput,
                Callable.From<InputEvent>(input => OnButtonGuiInput(button, capturedIndex, input)));
            _grid.AddChild(button);
            _buttons.Add(button);
        }
    }

    private void OnButtonGuiInput(Button button, int index, InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey key)
        {
            if (!key.Pressed || key.Echo)
            {
                return;
            }
            switch (key.Keycode)
            {
                case Key.Left:
                    FocusWrapped(index, rowDelta: 0, columnDelta: -1);
                    button.AcceptEvent();
                    return;
                case Key.Right:
                    FocusWrapped(index, rowDelta: 0, columnDelta: 1);
                    button.AcceptEvent();
                    return;
                case Key.Up:
                    FocusWrapped(index, rowDelta: -1, columnDelta: 0);
                    button.AcceptEvent();
                    return;
                case Key.Down:
                    FocusWrapped(index, rowDelta: 1, columnDelta: 0);
                    button.AcceptEvent();
                    return;
                case Key.Enter:
                case Key.KpEnter:
                case Key.Space:
                    Insert(index);
                    button.AcceptEvent();
                    return;
                case Key.Escape:
                    ClosePicker();
                    button.AcceptEvent();
                    return;
                case Key.Tab:
                    ExitForTab(key.ShiftPressed);
                    button.AcceptEvent();
                    return;
            }
        }
        if (inputEvent.IsActionPressed("ui_left"))
        {
            FocusWrapped(index, rowDelta: 0, columnDelta: -1);
            button.AcceptEvent();
            return;
        }
        if (inputEvent.IsActionPressed("ui_right"))
        {
            FocusWrapped(index, rowDelta: 0, columnDelta: 1);
            button.AcceptEvent();
            return;
        }
        if (inputEvent.IsActionPressed("ui_up"))
        {
            FocusWrapped(index, rowDelta: -1, columnDelta: 0);
            button.AcceptEvent();
            return;
        }
        if (inputEvent.IsActionPressed("ui_down"))
        {
            FocusWrapped(index, rowDelta: 1, columnDelta: 0);
            button.AcceptEvent();
            return;
        }
        if (inputEvent.IsActionPressed("ui_accept"))
        {
            Insert(index);
            button.AcceptEvent();
            return;
        }
        if (inputEvent.IsActionPressed("ui_cancel"))
        {
            ClosePicker();
            button.AcceptEvent();
        }
    }

    private void FocusWrapped(int index, int rowDelta, int columnDelta)
    {
        if (_buttons.Count == 0)
        {
            return;
        }
        int rows = (_buttons.Count + Columns - 1) / Columns;
        int row = index / Columns;
        int column = index % Columns;
        int nextRow = (row + rowDelta + rows) % rows;
        int nextColumn = (column + columnDelta + Columns) % Columns;
        int next = nextRow * Columns + nextColumn;
        while (next >= _buttons.Count)
        {
            nextColumn = (nextColumn - 1 + Columns) % Columns;
            next = nextRow * Columns + nextColumn;
        }
        _buttons[next].GrabFocus();
    }

    private void Insert(int index)
    {
        if (!_available || _editor == null || index < 0 || index >= _emojis.Count)
        {
            return;
        }
        string id = _emojis[index].Id;
        if (_editor.InsertEmoji(id))
        {
            Inserted?.Invoke(id);
        }
        if (index < _buttons.Count && GodotObject.IsInstanceValid(_buttons[index]))
        {
            _buttons[index].GrabFocus();
        }
    }

    private void ExitForTab(bool backwards)
    {
        InvalidateOpenIntent();
        HideExplicitly();
        if (FocusExitRequested != null)
        {
            FocusExitRequested.Invoke(backwards);
        }
        else
        {
            _editor?.FocusEditor();
        }
    }

    private bool PickerOwnsFocus() =>
        IsInsideTree() && IsPickerControl(GetViewport().GuiGetFocusOwner());

    private bool IsPickerControl(Control? control) =>
        control != null && _buttons.Any(button => ReferenceEquals(button, control));

    private void OnPopupHide()
    {
        if (_explicitHideSettling)
        {
            if (!_explicitHideCompletionScheduled)
            {
                _explicitHideCompletionScheduled = true;
                long generation = _explicitHideGeneration;
                Callable.From(() => CompleteExplicitHide(generation)).CallDeferred();
            }
            return;
        }
        if (Visible && _openIntent)
        {
            return;
        }
        InvalidateOpenIntent();
    }

    private void HideExplicitly()
    {
        if (Visible)
        {
            _explicitHideGeneration++;
            _explicitHideSettling = true;
            _explicitHideCompletionScheduled = false;
        }
        Hide();
    }

    private void CompleteExplicitHide(long generation)
    {
        if (generation != _explicitHideGeneration)
        {
            return;
        }
        _explicitHideSettling = false;
        _explicitHideCompletionScheduled = false;
        if (!_openQueuedAfterExplicitHide)
        {
            return;
        }
        _openQueuedAfterExplicitHide = false;
        OpenPicker();
    }

    private void InvalidateOpenIntent()
    {
        _focusIntentGeneration++;
        _openIntent = false;
        _previousFocus = null;
        _openQueuedAfterExplicitHide = false;
    }

    private static void ApplyLobbyButtonStyle(Button button)
    {
        button.AddThemeStyleboxOverride(
            "normal",
            CreatePickerButtonStyle(LobbySurfaceColor, new Color(LobbyBorderColor, 0.72f), 1));
        button.AddThemeStyleboxOverride(
            "hover",
            CreatePickerButtonStyle(LobbySecondaryColor, LobbyAccentColor, 2));
        button.AddThemeStyleboxOverride(
            "pressed",
            CreatePickerButtonStyle(LobbySecondaryColor.Darkened(0.05f), LobbyAccentColor, 2));
        button.AddThemeStyleboxOverride(
            "focus",
            CreatePickerButtonStyle(LobbySurfaceColor, LobbyAccentColor, 2));
    }

    private static StyleBoxFlat CreatePickerPanelStyle() => new()
    {
        BgColor = LobbySurfaceColor,
        BorderColor = LobbyBorderColor,
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        ContentMarginLeft = 10,
        ContentMarginTop = 10,
        ContentMarginRight = 10,
        ContentMarginBottom = 10
    };

    private static StyleBoxFlat CreatePickerButtonStyle(
        Color background,
        Color border,
        int borderWidth) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = borderWidth,
        BorderWidthTop = borderWidth,
        BorderWidthRight = borderWidth,
        BorderWidthBottom = borderWidth,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        ContentMarginLeft = 6,
        ContentMarginTop = 6,
        ContentMarginRight = 6,
        ContentMarginBottom = 6
    };
}
