using System;
using System.Threading;
using Godot;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectRichDraftEditorTestState(
    IReadOnlyList<string> RunKinds,
    int TextEditorCount,
    int EntityChipCount,
    int SegmentCount,
    LanConnectDraftSelection Selection,
    LanConnectDraftMeasure Budget,
    bool ChildControlsMutable,
    bool Editable,
    string FocusOwnerName,
    int BudgetComputationCount);

internal sealed partial class LanConnectRichDraftEditor : Control
{
    private readonly record struct BudgetSnapshot(
        long DraftRevision,
        LanConnectChatFeatureVersions Enabled,
        string SenderName,
        LanConnectDraftMeasure Measure,
        bool IsBlank,
        string BlockingReason,
        string BudgetText);

    private const float MinimumEditorWidth = 220f;
    private const float MinimumTextRunWidth = 72f;
    private const float MaximumTextRunWidth = 260f;
    private const float EntityChipWidth = 112f;
    private const float EntityChipHeight = 34f;
    private const float TextLineHeight = 22f;
    private static readonly Color TextStrongColor = new(0.94f, 0.91f, 0.84f, 1f);
    private static readonly Color TextMutedColor = new(0.68f, 0.65f, 0.6f, 1f);
    private static readonly Color AccentColor = new(0.88f, 0.58f, 0.17f, 1f);

    private readonly object _bindingSync = new();
    private LanConnectRichDraft? _draft;
    private Action<long>? _draftContentChangedHandler;
    private bool _draftContentChangedSubscribed;
    private long _bindingGeneration;
    private LanConnectChatFeatureVersions _enabled = new();
    private string _senderName = string.Empty;
    private Func<LanConnectDraftRun, string> _accessibleLabel = DefaultAccessibleLabel;
    private VBoxContainer? _stack;
    private HFlowContainer? _flow;
    private Label? _budgetLabel;
    private bool _reconciling;
    private bool _handlingChildChange;
    private bool _syncingSelection;
    private long _selectionSyncGeneration;
    private long _retiredControlGeneration;
    private bool _editable = true;
    private bool _restoreFocus;
    private LanConnectDraftPosition? _pointerAnchor;
    private bool _pointerDragging;
    private long _focusRestoreGeneration;
    private long _renderedDraftRevision = -1;
    private long _pendingDraftRevision = -1;
    private long _lastNotifiedDraftRevision = -1;
    private int _pendingDraftChange;
    private BudgetSnapshot _budgetSnapshot;
    private bool _budgetSnapshotValid;
    private int _budgetComputationCount;

    internal event Action? SubmitRequested;

    internal event Action? DraftChanged;

    internal bool Editable
    {
        get => _editable;
        set
        {
            if (_editable == value)
            {
                return;
            }
            _editable = value;
            ApplyEditableState();
        }
    }

    internal bool HasEditorFocus
    {
        get
        {
            if (!IsInsideTree())
            {
                return false;
            }
            Control? focusOwner = GetViewport().GuiGetFocusOwner();
            return focusOwner != null && (ReferenceEquals(focusOwner, this) || IsAncestorOf(focusOwner));
        }
    }

    internal Control? FocusTarget =>
        _draft == null
            ? null
            : FindRunControl(_draft.Selection.Active.RunIndex) ??
              _flow?.GetChildren().OfType<Control>().FirstOrDefault(control => !control.IsQueuedForDeletion());

    internal LanConnectDraftMeasure Budget => GetBudgetSnapshot().Measure;

    internal bool CanSubmit => Editable && GetBudgetSnapshot().Measure.CanSubmit;

    internal bool IsBlank => GetBudgetSnapshot().IsBlank;

    internal string BlockingReason => GetBudgetSnapshot().BlockingReason;

    internal string BudgetText => GetBudgetSnapshot().BudgetText;

    internal LanConnectRichDraftEditorTestState TestState
    {
        get
        {
            BudgetSnapshot budget = GetBudgetSnapshot();
            IReadOnlyList<LanConnectDraftRun> runs = _draft?.Runs ?? Array.Empty<LanConnectDraftRun>();
            string[] kinds = runs.Select(RunKind).ToArray();
            string focusOwnerName = IsInsideTree()
                ? GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? string.Empty
                : string.Empty;
            return new LanConnectRichDraftEditorTestState(
                kinds,
                runs.Count(run => run is LanConnectTextRun),
                runs.Count(run => run is not LanConnectTextRun),
                budget.Measure.SegmentCount,
                _draft?.Selection ?? default,
                budget.Measure,
                ChildControlsMutable: false,
                Editable,
                focusOwnerName,
                _budgetComputationCount);
        }
    }

    internal void Bind(
        LanConnectRichDraft draft,
        LanConnectChatFeatureVersions enabled,
        string senderName,
        Func<LanConnectDraftRun, string> accessibleLabel)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(senderName);
        ArgumentNullException.ThrowIfNull(accessibleLabel);
        lock (_bindingSync)
        {
            UnsubscribeFromDraftLocked();
            _draft = draft;
            if (IsInsideTree())
            {
                SubscribeToDraftLocked();
            }
            long baselineRevision = _draft.ContentRevision;
            Interlocked.Exchange(ref _pendingDraftRevision, baselineRevision);
            Interlocked.Exchange(ref _lastNotifiedDraftRevision, baselineRevision);
            Interlocked.Exchange(ref _pendingDraftChange, 0);
        }
        _enabled = enabled;
        _senderName = senderName;
        _accessibleLabel = accessibleLabel;
        _pointerAnchor = null;
        _pointerDragging = false;
        _budgetSnapshotValid = false;
        CancelDeferredFocusRestore();
        ReconcileControls(preserveFocus: HasEditorFocus);
    }

    internal void UpdateBudgetContext(
        LanConnectChatFeatureVersions enabled,
        string senderName)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(senderName);
        if (Equals(_enabled, enabled) &&
            string.Equals(_senderName, senderName, StringComparison.Ordinal))
        {
            return;
        }
        _enabled = enabled;
        _senderName = senderName;
        _budgetSnapshotValid = false;
        if (_budgetLabel != null && GodotObject.IsInstanceValid(_budgetLabel))
        {
            _budgetLabel.Text = BudgetText;
            _budgetLabel.TooltipText = BlockingReason;
        }
    }

    internal void SetCompactLayout(bool compact)
    {
        CustomMinimumSize = new Vector2(MinimumEditorWidth, compact ? 48f : 54f);
    }

    internal void RefreshFromDraft(bool preserveFocus = true)
    {
        ReconcileControls(preserveFocus);
    }

    internal void FocusEditor()
    {
        if (!_editable || _draft == null)
        {
            return;
        }
        CancelDeferredFocusRestore();
        FocusSelection(_draft.Selection);
    }

    internal void ReleaseEditorFocus()
    {
        CancelDeferredFocusRestore();
        if (HasEditorFocus)
        {
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        }
    }

    internal void InsertNewline()
    {
        if (!_editable || _draft == null)
        {
            return;
        }
        MutateDocument(() => _draft.InsertText("\n"));
    }

    internal void CopySelectionToClipboard()
    {
        if (_draft == null)
        {
            return;
        }
        DisplayServer.ClipboardSet(_draft.CopySelection(_accessibleLabel));
    }

    internal void PastePlainText(string text)
    {
        if (!_editable || _draft == null)
        {
            return;
        }
        MutateDocument(() => _draft.Paste(text ?? string.Empty));
    }

    public override void _Ready()
    {
        BuildControls();
        ReconcileControls(preserveFocus: false);
        SetProcess(true);
    }

    public override void _EnterTree()
    {
        lock (_bindingSync)
        {
            SubscribeToDraftLocked();
            if (_stack != null && _draft != null && _draft.ContentRevision > _renderedDraftRevision)
            {
                Interlocked.Exchange(ref _pendingDraftRevision, _draft.ContentRevision);
                Interlocked.Exchange(ref _pendingDraftChange, 1);
            }
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (Interlocked.Exchange(ref _pendingDraftChange, 0) == 0)
        {
            return;
        }

        long pendingRevision = Interlocked.Read(ref _pendingDraftRevision);
        long lastNotified = Interlocked.Read(ref _lastNotifiedDraftRevision);
        if (pendingRevision <= lastNotified)
        {
            return;
        }
        Interlocked.Exchange(ref _lastNotifiedDraftRevision, pendingRevision);
        _ = GetBudgetSnapshot();
        DraftChanged?.Invoke();
        if (_draft != null && pendingRevision > _renderedDraftRevision)
        {
            ReconcileControls(preserveFocus: HasEditorFocus);
        }
    }

    public override void _ExitTree()
    {
        CancelDeferredFocusRestore();
        lock (_bindingSync)
        {
            UnsubscribeFromDraftLocked();
        }
    }

    private void BuildControls()
    {
        if (_stack != null)
        {
            return;
        }
        Name = LanConnectConstants.ChatRichDraftEditorName;
        CustomMinimumSize = new Vector2(MinimumEditorWidth, 54f);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Pass;

        _stack = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _stack.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _stack.AddThemeConstantOverride("separation", 3);
        AddChild(_stack);

        _flow = new HFlowContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(MinimumEditorWidth, 38f),
            Alignment = FlowContainer.AlignmentMode.Begin
        };
        _flow.AddThemeConstantOverride("h_separation", 4);
        _flow.AddThemeConstantOverride("v_separation", 4);
        _stack.AddChild(_flow);

        _budgetLabel = new Label
        {
            Name = LanConnectConstants.ChatDraftBudgetName,
            Text = BudgetText,
            ClipText = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
            AccessibilityName = "消息预算"
        };
        _budgetLabel.AddThemeFontSizeOverride("font_size", 11);
        _stack.AddChild(_budgetLabel);
    }

    private void ReconcileControls(bool preserveFocus)
    {
        if (_draft == null || _flow == null || !GodotObject.IsInstanceValid(_flow) || _reconciling)
        {
            return;
        }

        _reconciling = true;
        SuppressSelectionSignalsThroughIdle();
        try
        {
            bool shouldRestoreFocus = preserveFocus || _restoreFocus || HasEditorFocus;
            _restoreFocus = false;
            long retiredGeneration = ++_retiredControlGeneration;
            int retiredIndex = 0;
            foreach (Node child in _flow.GetChildren())
            {
                if (child.IsQueuedForDeletion())
                {
                    continue;
                }
                child.Name = $"RetiredDraftControl_{retiredGeneration}_{retiredIndex++}";
                child.QueueFree();
            }

            IReadOnlyList<LanConnectDraftRun> runs = _draft.Runs;
            int textIndex = 0;
            for (int runIndex = 0; runIndex < runs.Count; runIndex++)
            {
                LanConnectDraftRun run = runs[runIndex];
                Control child = run switch
                {
                    LanConnectTextRun text => BuildTextRun(text, runIndex, textIndex++),
                    _ => BuildEntityChip(run, runIndex)
                };
                _flow.AddChild(child);
            }
            if (_budgetLabel != null && GodotObject.IsInstanceValid(_budgetLabel))
            {
                _budgetLabel.Text = BudgetText;
                _budgetLabel.TooltipText = BlockingReason;
            }
            ApplyEditableState();
            if (shouldRestoreFocus)
            {
                ScheduleFocusRestore(_draft.Selection);
            }
            _renderedDraftRevision = _draft.ContentRevision;
        }
        finally
        {
            _reconciling = false;
        }
    }

    private TextEdit BuildTextRun(LanConnectTextRun run, int runIndex, int textIndex)
    {
        int lines = Math.Clamp(run.Text.Count(character => character == '\n') + 1, 1, 3);
        float estimatedWidth = Math.Clamp(44f + run.Text.Length * 7f, MinimumTextRunWidth, MaximumTextRunWidth);
        TextEdit editor = new()
        {
            Name = textIndex == 0
                ? LanConnectConstants.ChatDraftInputName
                : LanConnectConstants.ChatDraftRunPrefix + runIndex,
            Text = run.Text,
            CustomMinimumSize = new Vector2(estimatedWidth, 12f + lines * TextLineHeight),
            FocusMode = _editable ? FocusModeEnum.All : FocusModeEnum.None,
            Editable = _editable,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ScrollFitContentHeight = false,
            TabInputMode = false,
            ContextMenuEnabled = false,
            AccessibilityName = "消息文字"
        };
        editor.SetMeta("run_index", runIndex);
        ApplyTextRunStyle(editor);
        editor.Connect(TextEdit.SignalName.TextChanged, Callable.From(() => OnTextRunChanged(editor, runIndex, run.Text)));
        editor.Connect(TextEdit.SignalName.CaretChanged, Callable.From(() => OnTextCaretChanged(editor, runIndex)));
        editor.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(input => OnTextGuiInput(editor, runIndex, input)));
        return editor;
    }

    private Button BuildEntityChip(LanConnectDraftRun run, int runIndex)
    {
        string label = _accessibleLabel(run) ?? DefaultAccessibleLabel(run);
        Button chip = new()
        {
            Name = LanConnectConstants.ChatEntityChipPrefix + runIndex,
            Text = label,
            TooltipText = label,
            AccessibilityName = label,
            ClipText = true,
            CustomMinimumSize = new Vector2(EntityChipWidth, EntityChipHeight),
            FocusMode = _editable ? FocusModeEnum.All : FocusModeEnum.None,
            Disabled = !_editable,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        chip.SetMeta("run_index", runIndex);
        ApplyEntityChipStyle(chip);
        chip.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(input => OnEntityGuiInput(runIndex, input)));
        return chip;
    }

    private void OnTextRunChanged(TextEdit editor, int runIndex, string previousText)
    {
        if (_draft == null || _reconciling || _handlingChildChange)
        {
            return;
        }
        string nextText = editor.Text;
        int prefix = CommonPrefix(previousText, nextText);
        int suffix = CommonSuffix(previousText, nextText, prefix);
        _handlingChildChange = true;
        try
        {
            _restoreFocus = editor.HasFocus();
            _draft.SetSelection(new LanConnectDraftSelection(
                new LanConnectDraftPosition(runIndex, prefix),
                new LanConnectDraftPosition(runIndex, previousText.Length - suffix)));
            _draft.ReplaceSelectionWithText(nextText[prefix..(nextText.Length - suffix)]);
        }
        finally
        {
            _handlingChildChange = false;
        }
        ReconcileControls(preserveFocus: _restoreFocus);
    }

    private void OnTextCaretChanged(TextEdit editor, int runIndex)
    {
        if (_draft == null || _reconciling || _handlingChildChange || _syncingSelection ||
            !editor.HasFocus())
        {
            return;
        }
        int active = Utf16OffsetFromLineColumn(editor, editor.GetCaretLine(), editor.GetCaretColumn());
        if (editor.HasSelection())
        {
            int anchor = Utf16OffsetFromLineColumn(
                editor,
                editor.GetSelectionFromLine(),
                editor.GetSelectionFromColumn());
            int selectionEnd = Utf16OffsetFromLineColumn(
                editor,
                editor.GetSelectionToLine(),
                editor.GetSelectionToColumn());
            int directedAnchor = active == anchor ? selectionEnd : anchor;
            _draft.SetSelection(new LanConnectDraftSelection(
                new LanConnectDraftPosition(runIndex, directedAnchor),
                new LanConnectDraftPosition(runIndex, active)));
        }
        else
        {
            _draft.SetCaret(new LanConnectDraftPosition(runIndex, active));
        }
    }

    private void OnTextGuiInput(TextEdit editor, int runIndex, InputEvent inputEvent)
    {
        if (_draft == null)
        {
            return;
        }
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
        {
            if (mouseButton.Pressed)
            {
                BeginPointerSelection(
                    DocumentPositionAt(editor, runIndex, mouseButton.Position),
                    mouseButton.ShiftPressed);
            }
            else
            {
                _pointerDragging = false;
            }
            return;
        }
        if (inputEvent is InputEventMouseMotion motion &&
            motion.ButtonMask.HasFlag(MouseButtonMask.Left))
        {
            ExtendPointerSelection(DocumentPositionAt(editor, runIndex, motion.Position));
            return;
        }
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        bool command = key.CtrlPressed || key.MetaPressed;
        if (command && key.Keycode == Key.C)
        {
            OnTextCaretChanged(editor, runIndex);
            CopySelectionToClipboard();
            editor.AcceptEvent();
            return;
        }
        if (command && key.Keycode == Key.V)
        {
            OnTextCaretChanged(editor, runIndex);
            PastePlainText(DisplayServer.ClipboardGet());
            editor.AcceptEvent();
            return;
        }
        if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            if (key.ShiftPressed)
            {
                OnTextCaretChanged(editor, runIndex);
                InsertNewline();
            }
            else
            {
                SubmitRequested?.Invoke();
            }
            editor.AcceptEvent();
            return;
        }
        if (!_editable)
        {
            editor.AcceptEvent();
            return;
        }

        int caret = Utf16OffsetFromLineColumn(editor, editor.GetCaretLine(), editor.GetCaretColumn());
        int textLength = editor.Text.Length;
        bool boundaryCommand = key.Keycode switch
        {
            Key.Left => caret == 0,
            Key.Right => caret == textLength,
            Key.Backspace => caret == 0 && !editor.HasSelection(),
            Key.Delete => caret == textLength && !editor.HasSelection(),
            _ => false
        };
        if (!boundaryCommand)
        {
            return;
        }

        _draft.SetCaret(new LanConnectDraftPosition(runIndex, caret));
        MutateDocument(() =>
        {
            switch (key.Keycode)
            {
                case Key.Left:
                    _draft.MoveLeft(key.ShiftPressed);
                    break;
                case Key.Right:
                    _draft.MoveRight(key.ShiftPressed);
                    break;
                case Key.Backspace:
                    _draft.Backspace();
                    break;
                case Key.Delete:
                    _draft.Delete();
                    break;
            }
        });
        editor.AcceptEvent();
    }

    private void OnEntityGuiInput(int runIndex, InputEvent inputEvent)
    {
        if (_draft == null)
        {
            return;
        }
        switch (inputEvent)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse:
                BeginPointerEntitySelection(runIndex, mouse.ShiftPressed);
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                _pointerDragging = false;
                break;
            case InputEventMouseMotion motion when motion.ButtonMask.HasFlag(MouseButtonMask.Left):
                ExtendPointerSelection(new LanConnectDraftPosition(runIndex, 1));
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleEntityKey(key);
                break;
        }
    }

    private void HandleEntityKey(InputEventKey key)
    {
        if (_draft == null)
        {
            return;
        }
        if ((key.CtrlPressed || key.MetaPressed) && key.Keycode == Key.C)
        {
            CopySelectionToClipboard();
            AcceptEvent();
            return;
        }
        if ((key.CtrlPressed || key.MetaPressed) && key.Keycode == Key.V)
        {
            PastePlainText(DisplayServer.ClipboardGet());
            AcceptEvent();
            return;
        }
        if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            if (key.ShiftPressed)
            {
                InsertNewline();
            }
            else
            {
                SubmitRequested?.Invoke();
            }
            AcceptEvent();
            return;
        }
        if (!_editable)
        {
            return;
        }
        bool handled = true;
        switch (key.Keycode)
        {
            case Key.Left:
                MutateDocument(() => _draft.MoveLeft(key.ShiftPressed));
                break;
            case Key.Right:
                MutateDocument(() => _draft.MoveRight(key.ShiftPressed));
                break;
            case Key.Backspace:
                MutateDocument(_draft.Backspace);
                break;
            case Key.Delete:
                MutateDocument(_draft.Delete);
                break;
            default:
                handled = TryGetLiteralText(key, out string literal);
                if (handled)
                {
                    MutateDocument(() => _draft.InsertText(literal));
                }
                break;
        }
        if (handled)
        {
            AcceptEvent();
        }
    }

    private void BeginPointerEntitySelection(int runIndex, bool extend)
    {
        if (_draft == null)
        {
            return;
        }
        LanConnectDraftPosition before = new(runIndex, 0);
        LanConnectDraftPosition after = new(runIndex, 1);
        _pointerAnchor = extend ? _draft.Selection.Anchor : before;
        _pointerDragging = true;
        SuppressSelectionSignalsThroughIdle();
        _draft.SetSelection(new LanConnectDraftSelection(_pointerAnchor.Value, after));
        FindRunControl(runIndex)?.GrabFocus();
    }

    private void BeginPointerSelection(LanConnectDraftPosition position, bool extend)
    {
        if (_draft == null)
        {
            return;
        }
        _pointerAnchor = extend ? _draft.Selection.Anchor : position;
        _pointerDragging = true;
        SuppressSelectionSignalsThroughIdle();
        _draft.SetSelection(new LanConnectDraftSelection(_pointerAnchor.Value, position));
    }

    private void ExtendPointerSelection(LanConnectDraftPosition position)
    {
        if (_draft == null || !_pointerDragging || _pointerAnchor == null)
        {
            return;
        }
        SuppressSelectionSignalsThroughIdle();
        _draft.SetSelection(new LanConnectDraftSelection(_pointerAnchor.Value, position));
    }

    private static LanConnectDraftPosition DocumentPositionAt(
        TextEdit editor,
        int runIndex,
        Vector2 localPosition)
    {
        Vector2I lineColumn = editor.GetLineColumnAtPos(
            new Vector2I((int)localPosition.X, (int)localPosition.Y),
            allowOutOfBounds: true);
        int offset = Utf16OffsetFromLineColumn(editor, lineColumn.Y, lineColumn.X);
        return new LanConnectDraftPosition(runIndex, offset);
    }

    private static bool TryGetLiteralText(InputEventKey key, out string literal)
    {
        literal = string.Empty;
        if (key.CtrlPressed || key.MetaPressed || key.AltPressed ||
            key.Keycode is Key.Tab or Key.Escape or Key.Enter or Key.KpEnter ||
            !System.Text.Rune.IsValid((int)key.Unicode) ||
            key.Unicode < 0x20 || key.Unicode == 0x7f)
        {
            return false;
        }
        literal = new System.Text.Rune((int)key.Unicode).ToString();
        return true;
    }

    private void MutateDocument(Action mutation)
    {
        long contentRevisionBefore = _draft?.ContentRevision ?? -1;
        _handlingChildChange = true;
        try
        {
            _restoreFocus = HasEditorFocus;
            mutation();
        }
        finally
        {
            _handlingChildChange = false;
        }
        if (_draft != null && _draft.ContentRevision == contentRevisionBefore)
        {
            if (_restoreFocus)
            {
                FocusSelection(_draft.Selection);
            }
        }
        else
        {
            ReconcileControls(preserveFocus: _restoreFocus);
        }
    }

    private void OnDraftContentChanged(long bindingGeneration, long revision)
    {
        lock (_bindingSync)
        {
            if (bindingGeneration != _bindingGeneration)
            {
                return;
            }
            long observed = Interlocked.Read(ref _pendingDraftRevision);
            while (revision > observed)
            {
                long previous = Interlocked.CompareExchange(
                    ref _pendingDraftRevision,
                    revision,
                    observed);
                if (previous == observed)
                {
                    break;
                }
                observed = previous;
            }
            Interlocked.Exchange(ref _pendingDraftChange, 1);
        }
    }

    private void SubscribeToDraftLocked()
    {
        if (_draft == null || _draftContentChangedSubscribed)
        {
            return;
        }
        long generation = ++_bindingGeneration;
        _draftContentChangedHandler = revision => OnDraftContentChanged(generation, revision);
        _draft.ContentChanged += _draftContentChangedHandler;
        _draftContentChangedSubscribed = true;
    }

    private void UnsubscribeFromDraftLocked()
    {
        _bindingGeneration++;
        if (_draft != null && _draftContentChangedHandler != null && _draftContentChangedSubscribed)
        {
            _draft.ContentChanged -= _draftContentChangedHandler;
        }
        _draftContentChangedHandler = null;
        _draftContentChangedSubscribed = false;
    }

    private void ApplyEditableState()
    {
        if (_flow == null || !GodotObject.IsInstanceValid(_flow))
        {
            return;
        }
        foreach (Node child in _flow.GetChildren())
        {
            switch (child)
            {
                case TextEdit text:
                    text.Editable = _editable;
                    text.FocusMode = _editable ? FocusModeEnum.All : FocusModeEnum.None;
                    break;
                case Button button:
                    button.Disabled = !_editable;
                    button.FocusMode = _editable ? FocusModeEnum.All : FocusModeEnum.None;
                    break;
            }
        }
    }

    private void FocusSelection(LanConnectDraftSelection selection)
    {
        if (!_editable || _flow == null || !GodotObject.IsInstanceValid(_flow))
        {
            return;
        }
        CancelDeferredFocusRestore();
        _syncingSelection = true;
        long syncGeneration = ++_selectionSyncGeneration;
        try
        {
            int runIndex = selection.Active.RunIndex;
            Control? control = FindRunControl(runIndex);
            if (control == null)
            {
                control = _flow.GetChildren().OfType<Control>()
                    .FirstOrDefault(candidate => !candidate.IsQueuedForDeletion());
            }
            if (control is TextEdit text)
            {
                text.GrabFocus();
                SetCaretUtf16Offset(text, selection.Active.TextOffset);
                if (selection.Anchor.RunIndex == selection.Active.RunIndex &&
                    selection.Anchor.TextOffset != selection.Active.TextOffset)
                {
                    (int fromLine, int fromColumn) = LineColumnAtUtf16Offset(text, selection.Anchor.TextOffset);
                    (int toLine, int toColumn) = LineColumnAtUtf16Offset(text, selection.Active.TextOffset);
                    text.Select(fromLine, fromColumn, toLine, toColumn);
                }
            }
            else
            {
                control?.GrabFocus();
            }
        }
        finally
        {
            if (IsInsideTree())
            {
                Callable.From(() =>
                {
                    if (syncGeneration == _selectionSyncGeneration)
                    {
                        _syncingSelection = false;
                    }
                }).CallDeferred();
            }
            else
            {
                _syncingSelection = false;
            }
        }
    }

    private void ScheduleFocusRestore(LanConnectDraftSelection selection)
    {
        long focusGeneration = ++_focusRestoreGeneration;
        long bindingGeneration;
        lock (_bindingSync)
        {
            bindingGeneration = _bindingGeneration;
        }
        Callable.From(() =>
        {
            if (focusGeneration != _focusRestoreGeneration ||
                bindingGeneration != _bindingGeneration ||
                !_editable || _draft == null || !IsInsideTree())
            {
                return;
            }
            Control? focusOwner = GetViewport().GuiGetFocusOwner();
            if (focusOwner != null &&
                GodotObject.IsInstanceValid(focusOwner) &&
                !ReferenceEquals(focusOwner, this) &&
                !IsAncestorOf(focusOwner))
            {
                return;
            }
            FocusSelection(selection);
        }).CallDeferred();
    }

    private void CancelDeferredFocusRestore()
    {
        _focusRestoreGeneration++;
    }

    private void SuppressSelectionSignalsThroughIdle()
    {
        _syncingSelection = true;
        long syncGeneration = ++_selectionSyncGeneration;
        if (IsInsideTree())
        {
            Callable.From(() =>
            {
                if (syncGeneration == _selectionSyncGeneration)
                {
                    _syncingSelection = false;
                }
            }).CallDeferred();
        }
    }

    private Control? FindRunControl(int runIndex)
    {
        if (_flow == null)
        {
            return null;
        }
        foreach (Node child in _flow.GetChildren())
        {
            if (child is Control control &&
                !control.IsQueuedForDeletion() &&
                control.HasMeta("run_index") &&
                control.GetMeta("run_index").AsInt32() == runIndex)
            {
                return control;
            }
        }
        return null;
    }

    private static int CommonPrefix(string left, string right)
    {
        int length = Math.Min(left.Length, right.Length);
        int index = 0;
        while (index < length && left[index] == right[index])
        {
            index++;
        }
        return index;
    }

    private static int CommonSuffix(string left, string right, int prefix)
    {
        int maximum = Math.Min(left.Length, right.Length) - prefix;
        int count = 0;
        while (count < maximum && left[^(count + 1)] == right[^(count + 1)])
        {
            count++;
        }
        return count;
    }

    private static int Utf16OffsetFromLineColumn(TextEdit editor, int line, int runeColumn)
    {
        int clampedLine = Math.Clamp(line, 0, Math.Max(0, editor.GetLineCount() - 1));
        int offset = 0;
        for (int index = 0; index < clampedLine; index++)
        {
            offset += editor.GetLine(index).Length + 1;
        }
        return offset + Utf16OffsetAtRuneIndex(editor.GetLine(clampedLine), runeColumn);
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

    private static int RuneIndexAtUtf16Offset(string text, int utf16Offset)
    {
        int clamped = Math.Clamp(utf16Offset, 0, text.Length);
        int consumed = 0;
        int count = 0;
        foreach (System.Text.Rune rune in text.EnumerateRunes())
        {
            if (consumed + rune.Utf16SequenceLength > clamped)
            {
                break;
            }
            consumed += rune.Utf16SequenceLength;
            count++;
        }
        return count;
    }

    private static (int Line, int Column) LineColumnAtUtf16Offset(TextEdit editor, int utf16Offset)
    {
        int remaining = Math.Clamp(utf16Offset, 0, editor.Text.Length);
        int lastLine = Math.Max(0, editor.GetLineCount() - 1);
        for (int line = 0; line <= lastLine; line++)
        {
            string text = editor.GetLine(line);
            if (remaining <= text.Length || line == lastLine)
            {
                return (line, RuneIndexAtUtf16Offset(text, remaining));
            }
            remaining -= text.Length + 1;
        }
        return (lastLine, 0);
    }

    private static void SetCaretUtf16Offset(TextEdit editor, int utf16Offset)
    {
        (int line, int column) = LineColumnAtUtf16Offset(editor, utf16Offset);
        editor.SetCaretLine(line);
        editor.SetCaretColumn(column);
    }

    private BudgetSnapshot GetBudgetSnapshot()
    {
        if (_draft == null)
        {
            return new BudgetSnapshot(
                -1,
                _enabled,
                _senderName,
                default,
                IsBlank: true,
                "请输入消息",
                FormatBudget(default));
        }

        long revision = _draft.ContentRevision;
        if (_budgetSnapshotValid &&
            _budgetSnapshot.DraftRevision == revision &&
            Equals(_budgetSnapshot.Enabled, _enabled) &&
            string.Equals(_budgetSnapshot.SenderName, _senderName, StringComparison.Ordinal))
        {
            return _budgetSnapshot;
        }

        LanConnectDraftMeasure measure;
        string compatibilityText;
        long measuredRevision;
        do
        {
            measuredRevision = _draft.ContentRevision;
            measure = _draft.Measure(_enabled, _senderName);
            compatibilityText = _draft.ToCompatibilityText();
        }
        while (measuredRevision != _draft.ContentRevision);

        bool isBlank = measure.EntityCount == 0 && string.IsNullOrWhiteSpace(compatibilityText);
        _budgetSnapshot = new BudgetSnapshot(
            measuredRevision,
            _enabled,
            _senderName,
            measure,
            isBlank,
            BlockingReasonFor(measure, isBlank),
            FormatBudget(measure));
        _budgetSnapshotValid = true;
        _budgetComputationCount++;
        return _budgetSnapshot;
    }

    private static string BlockingReasonFor(LanConnectDraftMeasure measure, bool isBlank)
    {
        if (isBlank)
        {
            return "请输入消息";
        }
        if (measure.TextScalars > LanConnectServerChatProtocol.MaxTextScalars)
        {
            return "消息文字超过 300 字符";
        }
        if (measure.SegmentCount > LanConnectServerChatProtocol.MaxSegments)
        {
            return "消息分段超过 32 段";
        }
        if (measure.EntityCount > LanConnectServerChatProtocol.MaxEntities)
        {
            return "消息实体超过 12 个";
        }
        if (measure.ContentValid &&
            measure.WorstCaseInboundBytes > LanConnectServerChatProtocol.MaxPayloadBytes)
        {
            return "消息传输大小超过 8192 字节";
        }
        if (!measure.ContentValid)
        {
            return "消息内容无效";
        }
        if (!measure.FeaturesSupported)
        {
            return "当前频道不支持草稿中的富内容";
        }
        return string.Empty;
    }

    private static string FormatBudget(LanConnectDraftMeasure measure) =>
        $"字符 {measure.TextScalars}/300 · 分段 {measure.SegmentCount}/32 · " +
        $"实体 {measure.EntityCount}/12 · 字节 {measure.WorstCaseInboundBytes}/8192";

    private static string RunKind(LanConnectDraftRun run) => run switch
    {
        LanConnectTextRun => "text",
        LanConnectEmojiRun => "emoji",
        LanConnectItemRun item => $"item_ref:{item.ItemType}",
        _ => "unknown"
    };

    private static string DefaultAccessibleLabel(LanConnectDraftRun run) => run switch
    {
        LanConnectEmojiRun => "Emoji",
        LanConnectItemRun { ItemType: "card" } => "卡牌",
        LanConnectItemRun { ItemType: "relic" } => "遗物",
        LanConnectItemRun { ItemType: "potion" } => "药水",
        _ => "实体"
    };

    private static void ApplyTextRunStyle(TextEdit input)
    {
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_placeholder_color", TextMutedColor);
        input.AddThemeStyleboxOverride(
            "normal",
            CreateStyle(new Color(0.11f, 0.11f, 0.13f, 0.98f), new Color(0.34f, 0.32f, 0.29f, 1f)));
        input.AddThemeStyleboxOverride(
            "focus",
            CreateStyle(new Color(0.13f, 0.13f, 0.15f, 1f), AccentColor));
        input.AddThemeStyleboxOverride(
            "read_only",
            CreateStyle(new Color(0.09f, 0.09f, 0.1f, 0.9f), new Color(0.28f, 0.27f, 0.25f, 1f)));
    }

    private static void ApplyEntityChipStyle(Button chip)
    {
        Color background = new(0.18f, 0.16f, 0.14f, 0.98f);
        Color border = new(0.42f, 0.36f, 0.27f, 1f);
        chip.AddThemeColorOverride("font_color", TextStrongColor);
        chip.AddThemeColorOverride("font_disabled_color", TextMutedColor);
        chip.AddThemeStyleboxOverride("normal", CreateStyle(background, border));
        chip.AddThemeStyleboxOverride("hover", CreateStyle(background.Lightened(0.08f), AccentColor));
        chip.AddThemeStyleboxOverride("pressed", CreateStyle(background.Darkened(0.05f), AccentColor));
        chip.AddThemeStyleboxOverride("focus", CreateStyle(background, AccentColor, borderWidth: 2));
        chip.AddThemeStyleboxOverride(
            "disabled",
            CreateStyle(new Color(0.1f, 0.1f, 0.11f, 0.9f), new Color(0.28f, 0.27f, 0.25f, 1f)));
    }

    private static StyleBoxFlat CreateStyle(Color background, Color border, int borderWidth = 1) => new()
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
        ContentMarginLeft = 8,
        ContentMarginRight = 8,
        ContentMarginTop = 5,
        ContentMarginBottom = 5
    };
}
