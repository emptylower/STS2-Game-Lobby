using System;
using System.Collections.Generic;
using Godot;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LobbyAnnouncementCarousel : Control
{
    // ── Retro pixel-art palette (converted from reference UI oklch values) ──
    private static readonly Color CardColor = new(0.99f, 0.97f, 0.93f, 1f);                 // #FDF8ED oklch(0.98,0.015,85)
    private static readonly Color BaseBackgroundColor = new(0.97f, 0.95f, 0.89f, 1f);       // #F8F1E3 oklch(0.96,0.02,85)
    private static readonly Color TextStrongColor = new(0.21f, 0.10f, 0.04f, 1f);          // #341A09 oklch(0.25,0.05,50)
    private static readonly Color TextMutedColor = new(0.46f, 0.36f, 0.31f, 1f);           // #775D4F oklch(0.50,0.04,50)
    private static readonly Color AccentColor = new(0.87f, 0.41f, 0.00f, 1f);              // #DF6900 oklch(0.65,0.18,55)
    private static readonly Color AccentBrightColor = new(0.93f, 0.50f, 0.08f, 1f);        // #ED7F14 brighter hover
    private static readonly Color BorderColor = new(0.80f, 0.65f, 0.53f, 1f);              // #CBA688 oklch(0.75,0.06,60)

    private readonly List<LobbyAnnouncementItem> _announcements = new();

    private PanelContainer? _frame;
    private SmoothGradientControl? _backgroundGradient;
    private ColorRect? _topGlow;
    private PanelContainer? _iconOrb;
    private Label? _iconLabel;
    private Label? _titleLabel;
    private Label? _dateLabel;
    private Label? _bodyLabel;
    private Button? _previousButton;
    private Button? _nextButton;
    private HBoxContainer? _indicatorContainer;
    private Label? _counterLabel;
    private Control? _progressTrack;
    private ColorRect? _progressFill;

    private bool _compactMode;
    private bool _paused;
    private int _currentIndex;
    private double _elapsed;

    public LobbyAnnouncementCarousel()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(0f, 68f);
        BuildUi();
    }

    public double AutoAdvanceSeconds { get; set; } = 6d;

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _announcements.Count <= 1)
        {
            UpdateProgress();
            return;
        }

        if (_paused)
        {
            UpdateProgress();
            return;
        }

        _elapsed += delta;
        if (_elapsed >= AutoAdvanceSeconds)
        {
            Advance(1);
            return;
        }

        UpdateProgress();
    }

    public void SetAnnouncements(IReadOnlyList<LobbyAnnouncementItem> announcements)
    {
        if (AreAnnouncementsEquivalent(announcements))
        {
            RefreshCurrentAnnouncement();
            return;
        }

        _announcements.Clear();
        foreach (LobbyAnnouncementItem announcement in announcements)
        {
            _announcements.Add(new LobbyAnnouncementItem
            {
                Id = announcement.Id,
                Type = announcement.Type,
                Title = announcement.Title,
                DateLabel = announcement.DateLabel,
                Body = announcement.Body,
                Enabled = announcement.Enabled,
            });
        }

        _currentIndex = _announcements.Count == 0
            ? 0
            : Math.Clamp(_currentIndex, 0, _announcements.Count - 1);
        _elapsed = 0d;
        RebuildIndicators();
        RefreshCurrentAnnouncement();
    }

    public void SetCompactMode(bool compactMode)
    {
        if (_compactMode == compactMode)
        {
            return;
        }

        _compactMode = compactMode;
        UpdateControlVisibility();
        RefreshCurrentAnnouncement();
    }

    private void BuildUi()
    {
        // Frame panel — padding is applied via StyleBox content margins,
        // which is the ONLY way to add inner spacing inside a PanelContainer.
        // Anchor offsets and MarginContainer offsets are ignored by Container parents.
        StyleBoxFlat frameStyle = CreatePanelStyle(BaseBackgroundColor, BorderColor, radius: 0, borderWidth: 2, padding: 0, shadowSize: 2, shadowColor: new Color(BorderColor, 0.7f));
        frameStyle.ContentMarginLeft = 24;
        frameStyle.ContentMarginRight = 24;
        frameStyle.ContentMarginTop = 14;
        frameStyle.ContentMarginBottom = 10;
        _frame = new PanelContainer { ClipContents = true };
        _frame.AddThemeStyleboxOverride("panel", frameStyle);
        _frame.SetAnchorsPreset(LayoutPreset.FullRect);
        _frame.MouseFilter = MouseFilterEnum.Stop;
        _frame.Connect(Control.SignalName.MouseEntered, Callable.From(() => _paused = true));
        _frame.Connect(Control.SignalName.MouseExited, Callable.From(() => _paused = false));
        AddChild(_frame);

        // Background layers (gradient is disabled in flat mode but kept for structure)
        _backgroundGradient = new SmoothGradientControl
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        _frame.AddChild(_backgroundGradient);
        _topGlow = new ColorRect { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _frame.AddChild(_topGlow);

        // Single-line layout: [TYPE pill] [title] [date] [dot indicators]
        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 2);
        _frame.AddChild(root);

        HBoxContainer mainRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        mainRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(mainRow);

        // Nav buttons hidden by default — use dot indicators + auto-advance
        _previousButton = CreateNavButton("‹", () => Advance(-1));
        _previousButton.Visible = false;
        mainRow.AddChild(_previousButton);

        // Type badge pill
        _iconOrb = CreatePanel(AccentColor, BorderColor, radius: 0, borderWidth: 2, padding: 0, shadowSize: 2, shadowColor: new Color(BorderColor, 0.7f));
        _iconOrb.CustomMinimumSize = new Vector2(42f, 28f);
        _iconOrb.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        mainRow.AddChild(_iconOrb);

        CenterContainer iconCenter = new();
        _iconOrb.AddChild(iconCenter);

        _iconLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _iconLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
        _iconLabel.AddThemeFontSizeOverride("font_size", 13);
        iconCenter.AddChild(_iconLabel);

        // Title — single line, truncated
        _titleLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true
        };
        _titleLabel.AddThemeColorOverride("font_color", TextStrongColor);
        _titleLabel.AddThemeFontSizeOverride("font_size", 16);
        mainRow.AddChild(_titleLabel);

        // Date label
        _dateLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        _dateLabel.AddThemeColorOverride("font_color", TextMutedColor);
        _dateLabel.AddThemeFontSizeOverride("font_size", 14);
        mainRow.AddChild(_dateLabel);

        // Dot indicators
        _indicatorContainer = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        _indicatorContainer.AddThemeConstantOverride("separation", 6);
        mainRow.AddChild(_indicatorContainer);

        // Counter label (compact mode only)
        _counterLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        _counterLabel.AddThemeColorOverride("font_color", AccentColor);
        _counterLabel.AddThemeFontSizeOverride("font_size", 14);
        mainRow.AddChild(_counterLabel);

        _nextButton = CreateNavButton("›", () => Advance(1));
        _nextButton.Visible = false;
        mainRow.AddChild(_nextButton);

        // Body label kept hidden (content available via tooltip)
        _bodyLabel = new Label { Visible = false };
        root.AddChild(_bodyLabel);

        // Progress bar
        _progressTrack = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 3f),
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _progressTrack.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(BorderColor, 0.3f), Colors.Transparent, radius: 0, borderWidth: 0, padding: 0));
        _progressTrack.Connect(Control.SignalName.Resized, Callable.From(UpdateProgress));
        root.AddChild(_progressTrack);

        _progressFill = new ColorRect
        {
            Color = new Color(AccentColor, 0.92f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _progressFill.SetAnchorsPreset(LayoutPreset.LeftWide);
        _progressTrack.AddChild(_progressFill);

        UpdateControlVisibility();
    }

    private void Advance(int direction)
    {
        if (_announcements.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + direction + _announcements.Count) % _announcements.Count;
        _elapsed = 0d;
        RefreshCurrentAnnouncement();
    }

    private void JumpToAnnouncement(int index)
    {
        if (index < 0 || index >= _announcements.Count || index == _currentIndex)
        {
            return;
        }

        _currentIndex = index;
        _elapsed = 0d;
        RefreshCurrentAnnouncement();
    }

    private void RefreshCurrentAnnouncement()
    {
        LobbyAnnouncementItem current = GetCurrentAnnouncement();
        AnnouncementVisualStyle style = GetVisualStyle(current.Type);

        if (_frame != null)
        {
            StyleBoxFlat refreshStyle = CreatePanelStyle(BaseBackgroundColor, BorderColor, radius: 0, borderWidth: 2, padding: 0, shadowSize: 2, shadowColor: new Color(BorderColor, 0.7f));
            refreshStyle.ContentMarginLeft = 24;
            refreshStyle.ContentMarginRight = 24;
            refreshStyle.ContentMarginTop = 14;
            refreshStyle.ContentMarginBottom = 10;
            _frame.AddThemeStyleboxOverride("panel", refreshStyle);
        }

        if (_iconOrb != null)
        {
            _iconOrb.AddThemeStyleboxOverride("panel", CreatePanelStyle(AccentColor, BorderColor, radius: 0, borderWidth: 2, padding: 0, shadowSize: 2, shadowColor: new Color(BorderColor, 0.7f)));
        }

        if (_iconLabel != null)
        {
            _iconLabel.Text = style.IconText;
            // White text on orange pill for contrast
            _iconLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
        }

        if (_titleLabel != null)
        {
            // Single-line: show body text as main content (title is shown via the type pill)
            string displayText = !string.IsNullOrWhiteSpace(current.Body)
                ? current.Body.Trim()
                : !string.IsNullOrWhiteSpace(current.Title)
                    ? current.Title.Trim()
                    : "暂无公告";
            _titleLabel.Text = NormalizeText(displayText);
            // Full text available via tooltip
            string fullText = $"{current.Title?.Trim()}\n{current.Body?.Trim()}".Trim();
            _titleLabel.TooltipText = NormalizeText(fullText);
        }

        if (_dateLabel != null)
        {
            _dateLabel.Text = NormalizeText(current.DateLabel?.Trim() ?? string.Empty);
            _dateLabel.Visible = !string.IsNullOrWhiteSpace(_dateLabel.Text);
        }

        if (_bodyLabel != null)
        {
            _bodyLabel.Visible = false;
        }

        if (_counterLabel != null)
        {
            int total = Math.Max(_announcements.Count, 1);
            _counterLabel.Text = $"{Math.Min(_currentIndex + 1, total)}/{total}";
        }

        UpdateIndicatorSelection(style);
        UpdateControlVisibility();
        UpdateProgress();
    }

    private void RebuildIndicators()
    {
        if (_indicatorContainer == null)
        {
            return;
        }

        foreach (Node child in _indicatorContainer.GetChildren())
        {
            child.QueueFree();
        }

        for (int index = 0; index < _announcements.Count; index++)
        {
            int selectedIndex = index;
            Button dot = new()
            {
                Text = string.Empty,
                CustomMinimumSize = new Vector2(8f, 8f),
                FocusMode = FocusModeEnum.None,
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            ApplyIndicatorStyle(dot, new Color(BorderColor, 0.4f), active: false);
            dot.Connect(Button.SignalName.Pressed, Callable.From(() => JumpToAnnouncement(selectedIndex)));
            _indicatorContainer.AddChild(dot);
        }
    }

    private void UpdateIndicatorSelection(AnnouncementVisualStyle style)
    {
        if (_indicatorContainer == null)
        {
            return;
        }

        for (int index = 0; index < _indicatorContainer.GetChildCount(); index++)
        {
            if (_indicatorContainer.GetChild(index) is not Button button)
            {
                continue;
            }

            bool active = index == _currentIndex;
            button.CustomMinimumSize = new Vector2(8f, 8f);
            ApplyIndicatorStyle(button, active ? style.Accent : new Color(BorderColor, 0.4f), active);
        }
    }

    private void UpdateControlVisibility()
    {
        bool hasMultiple = _announcements.Count > 1;

        // Nav buttons always hidden — use dot indicators + auto-advance
        if (_previousButton != null)
        {
            _previousButton.Visible = false;
        }

        if (_nextButton != null)
        {
            _nextButton.Visible = false;
        }

        if (_indicatorContainer != null)
        {
            _indicatorContainer.Visible = hasMultiple;
        }

        if (_counterLabel != null)
        {
            _counterLabel.Visible = _compactMode && hasMultiple;
        }
    }

    private void UpdateProgress()
    {
        if (_progressTrack == null || _progressFill == null)
        {
            return;
        }

        float width = _progressTrack.Size.X;
        float progressRatio = _announcements.Count <= 1 || AutoAdvanceSeconds <= 0d
            ? 1f
            : Mathf.Clamp((float)(_elapsed / AutoAdvanceSeconds), 0f, 1f);
        _progressFill.Size = new Vector2(width * progressRatio, Math.Max(_progressTrack.Size.Y, 4f));
    }

    private bool AreAnnouncementsEquivalent(IReadOnlyList<LobbyAnnouncementItem> announcements)
    {
        if (_announcements.Count != announcements.Count)
        {
            return false;
        }

        for (int index = 0; index < announcements.Count; index++)
        {
            LobbyAnnouncementItem current = _announcements[index];
            LobbyAnnouncementItem next = announcements[index];
            if (!string.Equals(current.Id, next.Id, StringComparison.Ordinal) ||
                !string.Equals(current.Type, next.Type, StringComparison.Ordinal) ||
                !string.Equals(current.Title, next.Title, StringComparison.Ordinal) ||
                !string.Equals(current.DateLabel, next.DateLabel, StringComparison.Ordinal) ||
                !string.Equals(current.Body, next.Body, StringComparison.Ordinal) ||
                current.Enabled != next.Enabled)
            {
                return false;
            }
        }

        return true;
    }

    private static void ApplyIndicatorStyle(Button button, Color baseColor, bool active)
    {
        Color hoverColor = active ? baseColor.Lightened(0.08f) : new Color(BorderColor, 0.6f);
        Color pressedColor = active ? baseColor.Darkened(0.08f) : new Color(BorderColor, 0.7f);
        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(baseColor, Colors.Transparent, radius: 0, borderWidth: 0, padding: 0));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(hoverColor, Colors.Transparent, radius: 0, borderWidth: 0, padding: 0));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(pressedColor, Colors.Transparent, radius: 0, borderWidth: 0, padding: 0));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(hoverColor, Colors.Transparent, radius: 0, borderWidth: 0, padding: 0));
    }

    private LobbyAnnouncementItem GetCurrentAnnouncement()
    {
        if (_announcements.Count == 0)
        {
            return new LobbyAnnouncementItem
            {
                Id = "default",
                Type = "info",
                Title = "暂无公告",
                Body = "浏览房间列表，或稍后刷新查看最新公告。",
                Enabled = true
            };
        }

        return _announcements[Math.Clamp(_currentIndex, 0, _announcements.Count - 1)];
    }

    private static Button CreateNavButton(string text, Action onPressed)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(38f, 38f),
            FocusMode = FocusModeEnum.None,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(new Color(BorderColor, 0.2f), Colors.Transparent, radius: 0, borderWidth: 0, padding: 8));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(new Color(BorderColor, 0.4f), Colors.Transparent, radius: 0, borderWidth: 0, padding: 8));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(new Color(BorderColor, 0.5f), Colors.Transparent, radius: 0, borderWidth: 0, padding: 8));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(new Color(BorderColor, 0.4f), Colors.Transparent, radius: 0, borderWidth: 0, padding: 8));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        button.AddThemeFontSizeOverride("font_size", 22);
        button.Connect(Button.SignalName.Pressed, Callable.From(onPressed));
        return button;
    }

    private static PanelContainer CreatePanel(Color background, Color border, int radius, int borderWidth, int padding, int shadowSize = 0, Color? shadowColor = null)
    {
        PanelContainer panel = new()
        {
            ClipContents = true
        };
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, border, radius, borderWidth, padding, shadowSize, shadowColor));
        return panel;
    }

    private static StyleBoxFlat CreatePanelStyle(Color background, Color border, int radius, int borderWidth, int padding, int shadowSize = 0, Color? shadowColor = null)
    {
        StyleBoxFlat style = new()
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomRight = radius,
            CornerRadiusBottomLeft = radius,
            ContentMarginLeft = padding,
            ContentMarginTop = padding,
            ContentMarginRight = padding,
            ContentMarginBottom = padding
        };
        style.ShadowColor = shadowColor ?? new Color(0f, 0f, 0f, 0f);
        style.ShadowSize = shadowSize;
        style.ShadowOffset = shadowSize > 0 ? new Vector2(shadowSize, shadowSize) : Vector2.Zero;
        return style;
    }

    private static string NormalizeText(string text)
    {
        return LanConnectUiText.NormalizeForDisplay(text);
    }

    private static AnnouncementVisualStyle GetVisualStyle(string? type)
    {
        // All types use AccentColor for the pill — gradient colors are unused in flat mode
        Color transparent = Colors.Transparent;
        return type switch
        {
            "update" => new AnnouncementVisualStyle("NEW", AccentColor, transparent, transparent, transparent, transparent, transparent),
            "event" => new AnnouncementVisualStyle("HOT", AccentColor, transparent, transparent, transparent, transparent, transparent),
            "warning" => new AnnouncementVisualStyle("INFO", AccentColor, transparent, transparent, transparent, transparent, transparent),
            _ => new AnnouncementVisualStyle("TIP", AccentColor, transparent, transparent, transparent, transparent, transparent),
        };
    }

    private readonly record struct AnnouncementVisualStyle(
        string IconText,
        Color Accent,
        Color LeftGlow,
        Color CenterGlow,
        Color RightGlow,
        Color TopGlow,
        Color Border);

    private sealed partial class SmoothGradientControl : Control
    {
        private Color _leftColor = Colors.Transparent;
        private Color _centerColor = Colors.Transparent;
        private Color _rightColor = Colors.Transparent;

        public SmoothGradientControl()
        {
            ClipContents = true;
        }

        public void SetColors(Color leftColor, Color centerColor, Color rightColor)
        {
            _leftColor = leftColor;
            _centerColor = centerColor;
            _rightColor = rightColor;
            QueueRedraw();
        }

        public override void _Draw()
        {
            int width = Math.Max(Mathf.RoundToInt(Size.X), 1);
            float height = Math.Max(Size.Y, 1f);
            for (int x = 0; x < width; x++)
            {
                float t = width <= 1 ? 0f : x / (float)(width - 1);
                Color color = SampleGradient(t);
                DrawLine(new Vector2(x, 0f), new Vector2(x, height), color, 1f, true);
            }
        }

        private Color SampleGradient(float t)
        {
            const float leftSpan = 0.42f;
            if (t <= leftSpan)
            {
                float local = Mathf.SmoothStep(0f, 1f, t / leftSpan);
                return _leftColor.Lerp(_centerColor, local);
            }

            float tail = Mathf.Clamp((t - leftSpan) / (1f - leftSpan), 0f, 1f);
            float smoothTail = Mathf.SmoothStep(0f, 1f, tail);
            return _centerColor.Lerp(_rightColor, smoothTail);
        }
    }
}
