using Godot;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectEmojiPickerTestState(
    int Columns,
    int Rows,
    IReadOnlyList<string> EmojiIds,
    int FocusedIndex,
    bool Available,
    bool Visible);

internal sealed partial class LanConnectEmojiPicker : PopupPanel
{
    internal const string PickerName = "ChatEmojiPicker";
    internal const string GridName = "ChatEmojiGrid";
    internal const string ToggleButtonName = "ChatEmojiButton";
    internal const string ButtonPrefix = "ChatEmoji_";
    internal const int Columns = 6;

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
                Visible);
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
        if (_grid != null)
        {
            RebuildButtons();
        }
        if (restoreFocus && editor.IsInsideTree())
        {
            editor.FocusEditor();
        }
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
                _focusIntentGeneration++;
                Hide();
            }
        }
    }

    internal bool InvalidateBinding()
    {
        bool wasActive = _openIntent || Visible || PickerOwnsFocus();
        _bindingGeneration++;
        _focusIntentGeneration++;
        _openIntent = false;
        _previousFocus = null;
        Hide();
        return wasActive;
    }

    internal void OpenPicker()
    {
        if (!_available || _editor == null || _buttons.Count == 0 || !IsInsideTree())
        {
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
        _focusIntentGeneration++;
        _openIntent = false;
        _previousFocus = null;
        Hide();
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
        BuildGrid();
        Hide();
    }

    public override void _ExitTree()
    {
        _focusIntentGeneration++;
        _openIntent = false;
        _previousFocus = null;
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
                TooltipText = label,
                AccessibilityName = label,
                CustomMinimumSize = new Vector2(38, 38),
                FocusMode = Control.FocusModeEnum.All,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand
            };
            button.SetMeta("emoji_index", index);
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
        _focusIntentGeneration++;
        _openIntent = false;
        _previousFocus = null;
        Hide();
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
}
