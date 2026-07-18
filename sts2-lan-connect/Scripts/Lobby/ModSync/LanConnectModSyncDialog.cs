using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectModSyncDialogTestState(
    Rect2 PanelRect,
    Rect2 ScrollRect,
    Rect2 PrimaryButtonRect,
    Rect2 CancelButtonRect,
    int RowCount,
    IReadOnlyList<string> SelectedIds,
    bool ScrollVisible,
    bool AllVisibleTextContained,
    LanConnectModSyncAction PrimaryAction,
    bool PrimaryButtonDisabled,
    bool RelaxedButtonVisible,
    string RelaxedButtonAccessibilityName,
    string FocusOwnerName);

internal sealed partial class LanConnectModSyncDialog : Control
{
    private static readonly Color PageColor = new(0.94f, 0.92f, 0.87f, 1f);
    private static readonly Color CardColor = new(0.99f, 0.97f, 0.93f, 1f);
    private static readonly Color SurfaceMutedColor = new(0.89f, 0.87f, 0.81f, 1f);
    private static readonly Color BorderColor = new(0.28f, 0.16f, 0.08f, 1f);
    private static readonly Color TextStrongColor = new(0.21f, 0.10f, 0.04f, 1f);
    private static readonly Color TextMutedColor = new(0.42f, 0.34f, 0.25f, 1f);
    private static readonly Color AccentColor = new(0.67f, 0.24f, 0.12f, 1f);
    private static readonly Color SuccessColor = new(0.16f, 0.42f, 0.23f, 1f);

    private LanConnectModSyncViewState _state = LanConnectModSyncViewState.Checking();
    private PanelContainer? _panel;
    private ScrollContainer? _scroll;
    private VBoxContainer? _rows;
    private GridContainer? _actions;
    private Label? _title;
    private Label? _message;
    private Button? _primaryButton;
    private Button? _cancelButton;
    private Button? _relaxedButton;
    private readonly List<LanConnectModSyncRow> _rowControls = [];
    private bool _busy;

    internal event Action<LanConnectModSyncAction>? ActionRequested;

    internal IReadOnlyList<string> SelectedExtraIds => _rowControls
        .Where(row => row.Selected)
        .Select(row => row.Descriptor.Id)
        .ToArray();

    internal static async Task<LanConnectModPreflightDecision> ShowAsync(
        Node parent,
        LobbyRoomSummary room,
        LobbyModPreflightResponse response,
        string? desiredSavePlayerNetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        using LanConnectModSyncWorkflow workflow = new(
            LanConnectSteamWorkshopSyncProvider.CreateNative(),
            new LanConnectModDisableApplier(new LanConnectGameModDisableSettings()),
            LanConnectPendingModSyncJoinStore.CreateDefault(),
            response);
        LanConnectModSyncDialog dialog = new();
        TaskCompletionSource<LanConnectModPreflightDecision> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        parent.AddChild(dialog);
        dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        workflow.ProgressChanged += state => dialog.ApplyState(state);
        dialog.ActionRequested += action => TaskHelper.RunSafely(HandleActionAsync(action));
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            workflow.Cancel();
            completion.TrySetCanceled(cancellationToken);
        });
        try
        {
            dialog.ApplyState(LanConnectModSyncViewState.Checking());
            dialog.ApplyState(await workflow.PrepareAsync(cancellationToken));
            return await completion.Task;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(dialog))
            {
                dialog.QueueFree();
            }
        }

        async Task HandleActionAsync(LanConnectModSyncAction action)
        {
            if (completion.Task.IsCompleted ||
                dialog._busy && action != LanConnectModSyncAction.Cancel)
            {
                return;
            }
            switch (action)
            {
                case LanConnectModSyncAction.Cancel:
                    workflow.Cancel();
                    if (!dialog._busy)
                    {
                        completion.TrySetResult(LanConnectModPreflightDecision.Cancel);
                    }
                    return;
                case LanConnectModSyncAction.ContinueRelaxed when response.CanContinueRelaxed:
                    completion.TrySetResult(LanConnectModPreflightDecision.ContinueRelaxed);
                    return;
                case LanConnectModSyncAction.Restart:
                    completion.TrySetResult(LanConnectModPreflightDecision.RestartScheduled);
                    Callable.From(() => parent.GetTree()?.Quit()).CallDeferred();
                    return;
                case LanConnectModSyncAction.ApplyChanges:
                case LanConnectModSyncAction.Retry:
                    break;
                default:
                    return;
            }

            IReadOnlyList<string> selected = dialog.SelectedExtraIds;
            bool confirmed = selected.Count == 0 || await ShowDisableConfirmationAsync(
                dialog,
                selected,
                cancellationToken);
            if (!confirmed)
            {
                return;
            }

            dialog.SetBusy(true);
            try
            {
                LanConnectModSyncWorkflowResult result = action == LanConnectModSyncAction.Retry
                    ? await workflow.RetryAsync(cancellationToken)
                    : await workflow.ApplyAsync(
                        room,
                        LanConnectConfig.LobbyServerBaseUrl,
                        desiredSavePlayerNetId,
                        selected,
                        disableConfirmed: confirmed,
                        cancellationToken);
                dialog.ApplyState(result.State);
                if (result.Status == LanConnectModSyncWorkflowStatus.Canceled)
                {
                    completion.TrySetResult(LanConnectModPreflightDecision.Cancel);
                }
            }
            finally
            {
                dialog.SetBusy(false);
            }
        }
    }

    internal LanConnectModSyncDialogTestState TestState
    {
        get
        {
            Rect2 panelRect = Rect(_panel);
            bool textContained = true;
            if (_panel != null)
            {
                foreach (Label label in _panel.FindChildren("*", "Label", recursive: true, owned: false).OfType<Label>())
                {
                    if (!label.IsVisibleInTree())
                    {
                        continue;
                    }
                    Rect2 rect = label.GetGlobalRect();
                    textContained &= rect.Size.X > 0f &&
                                     rect.Size.Y > 0f &&
                                     (HasScrollAncestor(label) || panelRect.Encloses(rect));
                }
            }
            return new LanConnectModSyncDialogTestState(
                panelRect,
                Rect(_scroll),
                Rect(_primaryButton),
                Rect(_cancelButton),
                _rowControls.Count,
                _rowControls.Where(row => row.Selected).Select(row => row.Descriptor.Id).ToArray(),
                _scroll?.Visible == true,
                textContained,
                _state.PrimaryAction,
                _primaryButton?.Disabled ?? true,
                _relaxedButton?.Visible == true,
                _relaxedButton?.AccessibilityName.ToString() ?? string.Empty,
                GetViewport()?.GuiGetFocusOwner()?.Name.ToString() ?? string.Empty);
        }
    }

    public override void _Ready()
    {
        Name = "LanConnectModSyncDialog";
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        LanConnectBlockingModal.Register(this);
        BuildChrome();
        Resized += UpdateResponsiveLayout;
        ApplyState(_state);
        Callable.From(UpdateResponsiveLayout).CallDeferred();
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            Request(LanConnectModSyncAction.Cancel);
            AcceptEvent();
        }
    }

    internal void ConfigureForTests(LanConnectModSyncViewState state) => ApplyState(state);

    internal void ApplyState(LanConnectModSyncViewState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        if (!IsNodeReady() || _title == null || _message == null || _rows == null)
        {
            return;
        }

        _title.Text = state.Title;
        _title.AccessibilityName = state.Title;
        _message.Text = state.Message;
        _message.AccessibilityName = state.Message;
        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        _rowControls.Clear();
        foreach (LanConnectModSyncRowState rowState in state.Rows)
        {
            LanConnectModSyncRow row = new(rowState);
            row.SelectionChanged += OnSelectionChanged;
            _rowControls.Add(row);
            _rows.AddChild(row);
        }
        _scroll!.Visible = state.Rows.Count > 0;
        ConfigureActions();
        UpdateResponsiveLayout();
        Callable.From(FocusSafestAction).CallDeferred();
    }

    internal void SetRowSelectedForTests(string id, bool selected)
    {
        LanConnectModSyncRow row = _rowControls.First(candidate =>
            string.Equals(candidate.Descriptor.Id, id, StringComparison.Ordinal));
        row.SetSelectedForTests(selected);
    }

    internal void RouteKeyForTests(Key key) => _GuiInput(new InputEventKey
    {
        Keycode = key,
        Pressed = true
    });

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ConfigureActions();
    }

    private void BuildChrome()
    {
        ColorRect veil = new()
        {
            Name = "ModSyncVeil",
            Color = new Color(0f, 0f, 0f, 0.48f),
            MouseFilter = MouseFilterEnum.Stop
        };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(veil);

        _panel = new PanelContainer
        {
            Name = "ModSyncPanel",
            ClipContents = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        _panel.AddThemeStyleboxOverride("panel", LanConnectModSyncRow.PixelStyle(
            CardColor,
            BorderColor,
            borderWidth: 3,
            padding: 20,
            shadowSize: 5));
        AddChild(_panel);

        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(body);

        HBoxContainer header = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        header.AddThemeConstantOverride("separation", 12);
        body.AddChild(header);

        TextureRect icon = new()
        {
            Name = "ModSyncShieldIcon",
            Texture = LanConnectChatUiComposition.Icons.Get("shield", 28, AccentColor),
            CustomMinimumSize = new Vector2(34f, 34f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            AccessibilityName = "MOD 兼容性保护"
        };
        header.AddChild(icon);

        VBoxContainer heading = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeConstantOverride("separation", 4);
        header.AddChild(heading);
        _title = CreateLabel("", 25, TextStrongColor, maxLines: 2);
        _title.Name = "ModSyncTitle";
        heading.AddChild(_title);
        _message = CreateLabel("", 15, TextMutedColor, maxLines: 3);
        _message.Name = "ModSyncMessage";
        heading.AddChild(_message);

        HSeparator separator = new();
        separator.AddThemeStyleboxOverride("separator", LanConnectModSyncRow.PixelStyle(
            BorderColor,
            BorderColor,
            borderWidth: 0,
            padding: 0));
        separator.CustomMinimumSize = new Vector2(0f, 2f);
        body.AddChild(separator);

        _scroll = new ScrollContainer
        {
            Name = "ModSyncRowsScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            FollowFocus = true,
            CustomMinimumSize = new Vector2(0f, 120f),
            ClipContents = true,
            AccessibilityName = "MOD 差异列表"
        };
        body.AddChild(_scroll);
        _rows = new VBoxContainer
        {
            Name = "ModSyncRows",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _rows.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_rows);

        PanelContainer safety = new();
        safety.AddThemeStyleboxOverride("panel", LanConnectModSyncRow.PixelStyle(
            SurfaceMutedColor,
            BorderColor,
            borderWidth: 1,
            padding: 8));
        body.AddChild(safety);
        Label safetyText = CreateLabel(
            "只会使用 Steam Workshop。\n只在确认后禁用所选 gameplay MOD；任何改动都需要重启。",
            13,
            TextMutedColor,
            maxLines: 2);
        safetyText.Name = "ModSyncSafetyBoundary";
        safetyText.AccessibilityName = safetyText.Text;
        safety.AddChild(safetyText);

        _actions = new GridContainer
        {
            Name = "ModSyncActions",
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _actions.AddThemeConstantOverride("h_separation", 10);
        _actions.AddThemeConstantOverride("v_separation", 8);
        body.AddChild(_actions);

        _relaxedButton = CreateButton("ModSyncRelaxedButton", LanConnectModSyncAction.ContinueRelaxed, primary: false);
        _cancelButton = CreateButton("ModSyncCancelButton", LanConnectModSyncAction.Cancel, primary: false);
        _primaryButton = CreateButton("ModSyncPrimaryButton", LanConnectModSyncAction.ApplyChanges, primary: true);
        _actions.AddChild(_relaxedButton);
        _actions.AddChild(_cancelButton);
        _actions.AddChild(_primaryButton);
    }

    private void ConfigureActions()
    {
        if (_primaryButton == null || _cancelButton == null || _relaxedButton == null)
        {
            return;
        }
        _primaryButton.Text = LanConnectModSyncLocalizer.Action(_state.PrimaryAction);
        _primaryButton.AccessibilityName = _primaryButton.Text;
        ApplyActionIcon(_primaryButton, _state.PrimaryAction);
        _primaryButton.SetMeta("action", (int)_state.PrimaryAction);
        _primaryButton.Visible = _state.PrimaryAction != LanConnectModSyncAction.None;
        _primaryButton.Disabled = _state.PrimaryAction == LanConnectModSyncAction.ApplyChanges &&
                                  _state.Kind == LanConnectModSyncViewKind.ExtraGameplaySelection &&
                                  _rowControls.All(row => !row.Selected);
        _primaryButton.Disabled |= _busy;
        _cancelButton.Disabled = false;
        _cancelButton.Text = LanConnectModSyncLocalizer.Action(LanConnectModSyncAction.Cancel);
        _cancelButton.AccessibilityName = _cancelButton.Text;
        ApplyActionIcon(_cancelButton, LanConnectModSyncAction.Cancel);
        _relaxedButton.Text = LanConnectModSyncLocalizer.Action(LanConnectModSyncAction.ContinueRelaxed);
        _relaxedButton.AccessibilityName = _relaxedButton.Text;
        ApplyActionIcon(_relaxedButton, LanConnectModSyncAction.ContinueRelaxed);
        _relaxedButton.Visible = _state.CanContinueRelaxed &&
                                 _state.PrimaryAction != LanConnectModSyncAction.Join;
        _relaxedButton.Disabled = _busy;
    }

    private Button CreateButton(
        string name,
        LanConnectModSyncAction action,
        bool primary)
    {
        Button button = new()
        {
            Name = name,
            Text = LanConnectModSyncLocalizer.Action(action),
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 48f),
            AccessibilityName = LanConnectModSyncLocalizer.Action(action),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        button.SetMeta("action", (int)action);
        Color background = primary ? AccentColor : CardColor;
        Color foreground = primary ? CardColor : TextStrongColor;
        button.AddThemeStyleboxOverride("normal", LanConnectModSyncRow.PixelStyle(background, BorderColor, 2, 10, 2));
        button.AddThemeStyleboxOverride("hover", LanConnectModSyncRow.PixelStyle(primary ? SuccessColor : PageColor, AccentColor, 2, 10, 1));
        button.AddThemeStyleboxOverride("pressed", LanConnectModSyncRow.PixelStyle(SurfaceMutedColor, AccentColor, 2, 10));
        button.AddThemeStyleboxOverride("focus", LanConnectModSyncRow.PixelStyle(background, AccentColor, 3, 9));
        button.AddThemeStyleboxOverride("disabled", LanConnectModSyncRow.PixelStyle(new Color(background, 0.45f), new Color(BorderColor, 0.45f), 2, 10));
        button.AddThemeColorOverride("font_color", foreground);
        button.AddThemeColorOverride("font_hover_color", primary ? CardColor : TextStrongColor);
        button.AddThemeColorOverride("font_pressed_color", TextStrongColor);
        button.AddThemeColorOverride("font_focus_color", foreground);
        button.AddThemeColorOverride("font_disabled_color", new Color(foreground, 0.72f));
        button.Pressed += () =>
        {
            LanConnectModSyncAction requested = button == _primaryButton
                ? _state.PrimaryAction
                : action;
            Request(requested);
        };
        return button;
    }

    private void OnSelectionChanged(LanConnectModSyncRow _, bool __) => ConfigureActions();

    private void Request(LanConnectModSyncAction action)
    {
        if (action != LanConnectModSyncAction.None)
        {
            ActionRequested?.Invoke(action);
        }
    }

    private void FocusSafestAction()
    {
        if (_cancelButton?.IsVisibleInTree() == true)
        {
            _cancelButton.GrabFocus();
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (_panel == null || _actions == null)
        {
            return;
        }
        Vector2 viewport = Size;
        if (viewport.X <= 0f || viewport.Y <= 0f)
        {
            viewport = GetViewportRect().Size;
        }
        float margin = Math.Clamp(Math.Min(viewport.X, viewport.Y) * 0.035f, 14f, 40f);
        float panelWidth = Math.Min(980f, Math.Max(1f, viewport.X - margin * 2f));
        float panelHeight = Math.Min(900f, Math.Max(1f, viewport.Y - margin * 2f));
        _panel.Position = new Vector2(
            MathF.Round((viewport.X - panelWidth) * 0.5f),
            MathF.Round((viewport.Y - panelHeight) * 0.5f));
        _panel.Size = new Vector2(MathF.Round(panelWidth), MathF.Round(panelHeight));
        _actions.Columns = panelWidth < 700f ? 1 : 3;
    }

    private static Label CreateLabel(string text, int fontSize, Color color, int maxLines)
    {
        int visibleLines = Math.Clamp(text.Count(character => character == '\n') + 1, 1, maxLines);
        Label label = new()
        {
            Text = text,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, (fontSize + 6f) * visibleLines),
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static void ApplyActionIcon(Button button, LanConnectModSyncAction action)
    {
        string iconName = action switch
        {
            LanConnectModSyncAction.Cancel => "x",
            LanConnectModSyncAction.Retry or LanConnectModSyncAction.Restart => "refresh-cw",
            LanConnectModSyncAction.ContinueRelaxed => "shield",
            _ => "check"
        };
        button.Icon = LanConnectChatUiComposition.Icons.Get(iconName, 18, TextStrongColor);
        button.AddThemeConstantOverride("icon_max_width", 18);
        button.ExpandIcon = false;
    }

    private static async Task<bool> ShowDisableConfirmationAsync(
        Node parent,
        IReadOnlyList<string> selectedIds,
        CancellationToken cancellationToken)
    {
        ConfirmationDialog confirmation = new()
        {
            Name = "LanConnectModDisableConfirmation",
            Title = "确认禁用所选 gameplay MOD",
            DialogText =
                "将只禁用以下已勾选项目，并保存一次设置：\n\n" +
                string.Join("\n", selectedIds.Select(id => $"- {id}")) +
                "\n\n普通非联机 MOD、LAN Connect 和必要依赖不会被禁用。之后必须重启游戏。",
            OkButtonText = "确认禁用",
            CancelButtonText = "返回检查",
            Exclusive = true,
            Unresizable = false,
            MinSize = new Vector2I(520, 320)
        };
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        confirmation.Confirmed += () => completion.TrySetResult(true);
        confirmation.Canceled += () => completion.TrySetResult(false);
        confirmation.CloseRequested += () => completion.TrySetResult(false);
        parent.AddChild(confirmation);
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        try
        {
            confirmation.PopupCenteredClamped(new Vector2I(680, 460), 0.9f);
            confirmation.GetCancelButton().GrabFocus();
            return await completion.Task;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(confirmation))
            {
                confirmation.QueueFree();
            }
        }
    }

    private static Rect2 Rect(Control? control) =>
        control != null && GodotObject.IsInstanceValid(control) && control.IsInsideTree()
            ? control.GetGlobalRect()
            : new Rect2();

    private static bool HasScrollAncestor(Node node)
    {
        Node? current = node.GetParent();
        while (current != null)
        {
            if (current is ScrollContainer)
            {
                return true;
            }
            current = current.GetParent();
        }
        return false;
    }
}
