using Godot;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectLobbyDialogChoice(
    int Id,
    string Label,
    string Description,
    bool Enabled = true,
    bool Primary = false,
    bool Danger = false);

internal partial class LanConnectLobbyChoiceDialog : Control
{
    private static readonly Color CardColor = new(0.99f, 0.97f, 0.93f, 1f);
    private static readonly Color SecondaryColor = new(0.93f, 0.89f, 0.82f, 1f);
    private static readonly Color BorderColor = new(0.80f, 0.65f, 0.53f, 1f);
    private static readonly Color AccentColor = new(0.87f, 0.41f, 0.00f, 1f);
    private static readonly Color AccentBrightColor = new(0.93f, 0.50f, 0.08f, 1f);
    private static readonly Color TextStrongColor = new(0.21f, 0.10f, 0.04f, 1f);
    private static readonly Color TextMutedColor = new(0.46f, 0.36f, 0.31f, 1f);
    private static readonly Color DangerColor = new(0.80f, 0.15f, 0.18f, 1f);
    private static readonly Color SuccessColor = new(0.10f, 0.60f, 0.19f, 1f);

    private readonly List<Button> _choiceButtons = [];
    private IReadOnlyList<LanConnectLobbyDialogChoice> _choices = [];
    private string _titleText = string.Empty;
    private string _messageText = string.Empty;
    private string _cancelText = "取消";
    private PanelContainer? _panel;
    private VBoxContainer? _choiceList;
    private Label? _title;
    private Label? _message;
    private Button? _cancelButton;
    private bool _built;

    internal event Action<int>? ChoiceSelected;
    internal event Action? Canceled;

    internal int ChoiceCountForTests => _choices.Count;

    internal IReadOnlyList<string> ChoiceLabelsForTests => _choices.Select(static choice => choice.Label).ToArray();

    internal IReadOnlyList<bool> ChoiceDisabledStatesForTests => _choiceButtons.Select(static button => button.Disabled).ToArray();

    internal int FocusedChoiceForTests => _choiceButtons.FindIndex(static button => button.HasFocus());

    internal Rect2 PanelRectForTests => _panel?.GetGlobalRect() ?? new Rect2();

    internal IReadOnlyList<Rect2> ChoiceRectsForTests =>
        _choiceButtons.Select(static button => button.GetGlobalRect()).ToArray();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        LanConnectBlockingModal.Register(this);
        BuildChrome();
        Resized += UpdateResponsiveLayout;
        ApplyConfiguration();
        Callable.From(UpdateResponsiveLayout).CallDeferred();
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            Cancel();
            AcceptEvent();
        }
    }

    public override void _ExitTree()
    {
        if (Visible)
        {
            Visible = false;
            Canceled?.Invoke();
        }
    }

    internal void Configure(
        string title,
        string message,
        IReadOnlyList<LanConnectLobbyDialogChoice> choices,
        string cancelText = "取消")
    {
        _titleText = title ?? string.Empty;
        _messageText = message ?? string.Empty;
        _choices = choices ?? throw new ArgumentNullException(nameof(choices));
        _cancelText = cancelText ?? "取消";
        ApplyConfiguration();
    }

    internal void Open()
    {
        if (!IsInsideTree())
        {
            return;
        }

        Visible = true;
        MoveToFront();
        UpdateResponsiveLayout();
        Callable.From(FocusSafestAction).CallDeferred();
    }

    internal void ActivateChoiceForTests(int id)
    {
        int index = _choices.ToList().FindIndex(choice => choice.Id == id);
        if (index >= 0 && index < _choiceButtons.Count)
        {
            _choiceButtons[index].EmitSignal(Button.SignalName.Pressed);
        }
    }

    internal static async Task<int?> ShowAsync(
        Node parent,
        string title,
        string message,
        IReadOnlyList<LanConnectLobbyDialogChoice> choices,
        string cancelText = "取消")
    {
        ArgumentNullException.ThrowIfNull(parent);
        LanConnectLobbyChoiceDialog dialog = new()
        {
            Name = "LanConnectLobbyChoiceDialog"
        };
        dialog.Configure(title, message, choices, cancelText);
        TaskCompletionSource<int?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.ChoiceSelected += id => completion.TrySetResult(id);
        dialog.Canceled += () => completion.TrySetResult(null);
        parent.AddChild(dialog);
        dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        try
        {
            dialog.Open();
            return await completion.Task;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(dialog))
            {
                dialog.QueueFree();
            }
        }
    }

    private void BuildChrome()
    {
        if (_built)
        {
            return;
        }

        _built = true;
        ColorRect veil = new()
        {
            Color = new Color(0f, 0f, 0f, 0.52f),
            MouseFilter = MouseFilterEnum.Stop
        };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(veil);

        _panel = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Stop
        };
        _panel.AddThemeStyleboxOverride("panel", CreatePixelStyle(CardColor, BorderColor, 3, 26, 5));
        AddChild(_panel);

        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 14);
        _panel.AddChild(body);

        _title = CreateLabel(string.Empty, 27, AccentColor);
        body.AddChild(_title);

        _message = CreateLabel(string.Empty, 17, TextMutedColor);
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_message);

        HSeparator separator = new()
        {
            CustomMinimumSize = new Vector2(0f, 2f)
        };
        separator.AddThemeStyleboxOverride("separator", CreatePixelStyle(BorderColor, BorderColor, 0, 0, 0));
        body.AddChild(separator);

        ScrollContainer scroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            FollowFocus = true
        };
        body.AddChild(scroll);

        _choiceList = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _choiceList.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_choiceList);

        _cancelButton = new Button
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 58f),
            FocusMode = FocusModeEnum.All
        };
        ApplyButtonStyle(_cancelButton, primary: false, danger: false);
        _cancelButton.Pressed += Cancel;
        body.AddChild(_cancelButton);
    }

    private void ApplyConfiguration()
    {
        if (!_built || _title == null || _message == null || _choiceList == null || _cancelButton == null)
        {
            return;
        }

        _title.Text = _titleText;
        _title.AccessibilityName = _titleText;
        _message.Text = _messageText;
        _message.AccessibilityName = _messageText;
        _message.Visible = !string.IsNullOrWhiteSpace(_messageText);
        _cancelButton.Text = _cancelText;
        _cancelButton.AccessibilityName = _cancelText;

        foreach (Node child in _choiceList.GetChildren())
        {
            _choiceList.RemoveChild(child);
            child.QueueFree();
        }
        _choiceButtons.Clear();

        foreach (LanConnectLobbyDialogChoice choice in _choices)
        {
            Button button = CreateChoiceButton(choice);
            _choiceButtons.Add(button);
            _choiceList.AddChild(button);
        }

        UpdateResponsiveLayout();
        if (Visible)
        {
            Callable.From(FocusSafestAction).CallDeferred();
        }
    }

    private Button CreateChoiceButton(LanConnectLobbyDialogChoice choice)
    {
        Button button = new()
        {
            Name = $"LobbyChoice{choice.Id}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 88f),
            FocusMode = FocusModeEnum.All,
            Disabled = !choice.Enabled,
            AccessibilityName = choice.Label,
            TooltipText = choice.Description
        };
        ApplyButtonStyle(button, choice.Primary, choice.Danger);

        VBoxContainer text = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        text.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 12);
        text.AddThemeConstantOverride("separation", 4);
        button.AddChild(text);

        Label title = CreateLabel(choice.Label, 20, choice.Primary || choice.Danger ? CardColor : TextStrongColor);
        title.MouseFilter = MouseFilterEnum.Ignore;
        text.AddChild(title);
        if (!string.IsNullOrWhiteSpace(choice.Description))
        {
            Label description = CreateLabel(
                choice.Description,
                15,
                choice.Primary || choice.Danger ? new Color(CardColor, 0.86f) : TextMutedColor);
            description.MouseFilter = MouseFilterEnum.Ignore;
            description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.AddChild(description);
        }

        button.Pressed += () => SelectChoice(choice.Id);
        return button;
    }

    private void SelectChoice(int id)
    {
        if (!Visible)
        {
            return;
        }

        int index = _choices.ToList().FindIndex(choice => choice.Id == id && choice.Enabled);
        if (index < 0 || index >= _choiceButtons.Count || _choiceButtons[index].Disabled)
        {
            return;
        }

        Visible = false;
        ChoiceSelected?.Invoke(id);
    }

    private void Cancel()
    {
        if (!Visible)
        {
            return;
        }

        Visible = false;
        Canceled?.Invoke();
    }

    private void FocusSafestAction()
    {
        if (!Visible)
        {
            return;
        }

        if (_cancelButton?.IsVisibleInTree() == true)
        {
            _cancelButton.GrabFocus();
            return;
        }

        _choiceButtons.FirstOrDefault(static button => !button.Disabled)?.GrabFocus();
    }

    private void UpdateResponsiveLayout()
    {
        if (_panel == null)
        {
            return;
        }

        Vector2 viewport = Size;
        if (viewport.X <= 0f || viewport.Y <= 0f)
        {
            viewport = GetViewportRect().Size;
        }

        float margin = Math.Clamp(Math.Min(viewport.X, viewport.Y) * 0.04f, 14f, 48f);
        float panelWidth = Math.Min(920f, Math.Max(1f, viewport.X - margin * 2f));
        float desiredHeight = 230f + _choices.Count * 100f;
        float panelHeight = Math.Min(desiredHeight, Math.Max(1f, viewport.Y - margin * 2f));
        _panel.Position = new Vector2(
            MathF.Round((viewport.X - panelWidth) * 0.5f),
            MathF.Round((viewport.Y - panelHeight) * 0.5f));
        _panel.Size = new Vector2(MathF.Round(panelWidth), MathF.Round(panelHeight));
    }

    private static Label CreateLabel(string text, int fontSize, Color color)
    {
        Label label = new()
        {
            Text = text,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static void ApplyButtonStyle(Button button, bool primary, bool danger)
    {
        Color background = danger ? DangerColor : primary ? AccentColor : SecondaryColor;
        Color foreground = primary || danger ? CardColor : TextStrongColor;
        Color hover = danger ? new Color(0.67f, 0.10f, 0.13f, 1f) : primary ? AccentBrightColor : SuccessColor;
        button.AddThemeStyleboxOverride("normal", CreatePixelStyle(background, danger ? DangerColor : BorderColor, 2, 12, 3));
        button.AddThemeStyleboxOverride("hover", CreatePixelStyle(hover, BorderColor, 2, 12, 2));
        button.AddThemeStyleboxOverride("pressed", CreatePixelStyle(new Color(hover, 0.82f), AccentColor, 2, 12, 0));
        button.AddThemeStyleboxOverride("focus", CreatePixelStyle(background, AccentColor, 3, 11, 2));
        button.AddThemeStyleboxOverride("disabled", CreatePixelStyle(new Color(background, 0.42f), new Color(BorderColor, 0.45f), 2, 12, 0));
        button.AddThemeColorOverride("font_color", foreground);
        button.AddThemeColorOverride("font_hover_color", CardColor);
        button.AddThemeColorOverride("font_pressed_color", CardColor);
        button.AddThemeColorOverride("font_focus_color", foreground);
        button.AddThemeColorOverride("font_disabled_color", new Color(foreground, 0.62f));
    }

    private static StyleBoxFlat CreatePixelStyle(
        Color background,
        Color border,
        int borderWidth,
        int padding,
        int shadowSize)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            ContentMarginLeft = padding,
            ContentMarginTop = padding,
            ContentMarginRight = padding,
            ContentMarginBottom = padding,
            ShadowColor = new Color(border, 0.72f),
            ShadowSize = shadowSize,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0
        };
    }
}
