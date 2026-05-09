using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

// Server picker — full-screen Control overlay with the lobby's retro pixel-art
// look. Built 100% in C# (no .tscn) so script resolution can never fail inside
// mod hosts. Palette and pixel-border treatment mirror LanConnectLobbyOverlay.
public partial class LanConnectServerSelectionDialog : Control
{
    // Mirror of the palette used by LanConnectLobbyOverlay so the picker
    // matches the lobby style without extracting shared theme infra.
    private static readonly Color BackdropDimColor = new(0.10f, 0.06f, 0.03f, 0.55f);
    private static readonly Color SurfaceColor = new(0.99f, 0.97f, 0.93f, 1f);
    private static readonly Color CardColor = new(0.99f, 0.97f, 0.93f, 1f);
    private static readonly Color SecondaryColor = new(0.93f, 0.89f, 0.82f, 1f);
    private static readonly Color SurfaceMutedColor = new(0.89f, 0.87f, 0.81f, 1f);
    private static readonly Color InputBgColor = new(0.95f, 0.92f, 0.86f, 1f);
    private static readonly Color BorderColor = new(0.80f, 0.65f, 0.53f, 1f);
    private static readonly Color AccentColor = new(0.87f, 0.41f, 0.00f, 1f);
    private static readonly Color AccentBrightColor = new(0.93f, 0.50f, 0.08f, 1f);
    private static readonly Color TextStrongColor = new(0.21f, 0.10f, 0.04f, 1f);
    private static readonly Color TextMutedColor = new(0.46f, 0.36f, 0.31f, 1f);
    private static readonly Color SuccessColor = new(0.10f, 0.60f, 0.19f, 1f);
    private static readonly Color DangerColor = new(0.80f, 0.15f, 0.18f, 1f);
    private static readonly Color PrimaryFgColor = new(0.15f, 0.05f, 0.00f, 1f);

    private VBoxContainer? _list;
    private LineEdit? _manualInput;
    private Label? _statusLabel;

    private System.Collections.Generic.List<ServerListEntry> _entries = new();

    public event Action<string>? ServerChosen;
    public event Action? Cancelled;

    public LanConnectServerSelectionDialog()
    {
        Name = "LanConnectServerSelectionDialog";
        MouseFilter = MouseFilterEnum.Stop;
        // Full-rect anchored — it covers the whole screen and dims the
        // background, which makes the centered panel feel modal.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUi();
    }

    public override void _Ready()
    {
        _ = RefreshAsync();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            AcceptEvent();
            Cancel();
        }
    }

    private void Cancel()
    {
        Cancelled?.Invoke();
        QueueFree();
    }

    private void BuildUi()
    {
        // Dimming backdrop — clicking outside the panel cancels.
        var backdrop = new ColorRect
        {
            Color = BackdropDimColor,
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Cancel();
            }
        };
        AddChild(backdrop);

        // Centered panel — pixel-border surface, fixed size, doesn't fill the
        // whole screen so the lobby feel of an inline modal panel comes through.
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        center.MouseFilter = MouseFilterEnum.Pass;
        AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(720f, 520f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", BuildPixelStyle(SurfaceColor, BorderColor, borderWidth: 3, padding: 20, shadowSize: 4));
        center.AddChild(panel);

        var inner = new VBoxContainer();
        inner.AddThemeConstantOverride("separation", 14);
        panel.AddChild(inner);

        // Header row: title + small subtitle on accent-bordered strip
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 12);
        inner.AddChild(headerRow);

        var titleLabel = new Label
        {
            Text = "选择服务器",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        titleLabel.AddThemeColorOverride("font_color", TextStrongColor);
        titleLabel.AddThemeFontSizeOverride("font_size", 26);
        headerRow.AddChild(titleLabel);

        var closeBtn = new Button { Text = "✕" };
        closeBtn.CustomMinimumSize = new Vector2(40f, 40f);
        ApplyButtonStyle(closeBtn, primary: false, danger: true);
        closeBtn.Pressed += Cancel;
        headerRow.AddChild(closeBtn);

        var subtitleLabel = new Label
        {
            Text = "[ MOD LOBBY  ·  PUBLIC SERVER LIST ]",
        };
        subtitleLabel.AddThemeColorOverride("font_color", AccentColor);
        subtitleLabel.AddThemeFontSizeOverride("font_size", 14);
        inner.AddChild(subtitleLabel);

        var divider = new HSeparator();
        inner.AddChild(divider);

        // Status pill
        _statusLabel = new Label { Text = "刷新中..." };
        _statusLabel.AddThemeColorOverride("font_color", TextMutedColor);
        _statusLabel.AddThemeFontSizeOverride("font_size", 14);
        inner.AddChild(_statusLabel);

        // Server list (scrollable)
        var scrollPanel = new PanelContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scrollPanel.AddThemeStyleboxOverride("panel", BuildPixelStyle(SecondaryColor, BorderColor, borderWidth: 2, padding: 6, shadowSize: 0));
        inner.AddChild(scrollPanel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scrollPanel.AddChild(scroll);

        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_list);

        // Manual entry row
        var manualSection = new VBoxContainer();
        manualSection.AddThemeConstantOverride("separation", 6);
        inner.AddChild(manualSection);

        var manualHeader = new Label { Text = "手动输入" };
        manualHeader.AddThemeColorOverride("font_color", AccentColor);
        manualHeader.AddThemeFontSizeOverride("font_size", 14);
        manualSection.AddChild(manualHeader);

        var manualRow = new HBoxContainer();
        manualRow.AddThemeConstantOverride("separation", 8);
        manualSection.AddChild(manualRow);

        _manualInput = new LineEdit
        {
            PlaceholderText = "https://lobby.example.com",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 38f),
        };
        ApplyLineEditStyle(_manualInput);
        manualRow.AddChild(_manualInput);

        var manualButton = new Button { Text = "连接" };
        manualButton.CustomMinimumSize = new Vector2(110f, 38f);
        ApplyButtonStyle(manualButton, primary: true, danger: false);
        manualButton.Pressed += OnManualConnect;
        manualRow.AddChild(manualButton);

        // Actions row — verification-phase policy is "always show picker", so
        // the auto-connect-on-next-launch checkbox would be misleading and
        // has been removed. The row keeps refresh + reset.
        var actionsRow = new HBoxContainer();
        actionsRow.AddThemeConstantOverride("separation", 12);
        inner.AddChild(actionsRow);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        actionsRow.AddChild(spacer);

        var refreshButton = new Button { Text = "刷新" };
        refreshButton.CustomMinimumSize = new Vector2(110f, 36f);
        ApplyButtonStyle(refreshButton, primary: false, danger: false);
        refreshButton.Pressed += () => _ = RefreshAsync();
        actionsRow.AddChild(refreshButton);

        var resetButton = new Button { Text = "重置本地列表" };
        resetButton.CustomMinimumSize = new Vector2(140f, 36f);
        ApplyButtonStyle(resetButton, primary: false, danger: false);
        resetButton.Pressed += () =>
        {
            LanConnectKnownPeersCache.Reset();
            _ = RefreshAsync();
        };
        actionsRow.AddChild(resetButton);
    }

    private async Task RefreshAsync()
    {
        if (_statusLabel != null) _statusLabel.Text = "刷新中...";
        _entries = await LanConnectServerListBootstrap.GatherAsync();
        await LanConnectServerListBootstrap.PingAllAsync(_entries);
        Render();
        if (_statusLabel != null) _statusLabel.Text = $"共 {_entries.Count} 个候选服务器";
    }

    private void Render()
    {
        if (_list == null) return;
        foreach (var child in _list.GetChildren()) child.QueueFree();

        var ordered = _entries
            .OrderByDescending(e => e.LastSuccessConnect ?? DateTime.MinValue)
            .ThenBy(e => e.Bucket)
            .ThenBy(e => e.Address);

        bool any = false;
        foreach (var e in ordered)
        {
            any = true;
            _list.AddChild(BuildServerRow(e));
        }

        if (!any)
        {
            var empty = new Label { Text = "暂无可用服务器，可手动输入或重置缓存重试。" };
            empty.AddThemeColorOverride("font_color", TextMutedColor);
            empty.AddThemeFontSizeOverride("font_size", 14);
            empty.CustomMinimumSize = new Vector2(0f, 60f);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            _list.AddChild(empty);
        }
    }

    private Control BuildServerRow(ServerListEntry e)
    {
        // Whole row is a clickable button styled as a pixel card.
        var btn = new Button
        {
            CustomMinimumSize = new Vector2(0f, 56f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = $"address: {e.Address}\nsource: {e.Source}",
            Text = string.Empty, // we draw our own content
        };
        ApplyServerCardStyle(btn);
        string addr = e.Address;
        btn.Pressed += () => { ServerChosen?.Invoke(addr); QueueFree(); };

        // Layered content: HBox with name + ping
        var row = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 14;
        row.OffsetRight = -14;
        row.AddThemeConstantOverride("separation", 12);
        btn.AddChild(row);

        var nameVbox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        nameVbox.AddThemeConstantOverride("separation", 2);
        row.AddChild(nameVbox);

        var nameLabel = new Label
        {
            Text = e.DisplayName ?? e.Address,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        nameLabel.AddThemeColorOverride("font_color", TextStrongColor);
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        nameVbox.AddChild(nameLabel);

        var subLabel = new Label
        {
            Text = $"{e.Address}  ·  {e.Source}",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        subLabel.AddThemeColorOverride("font_color", TextMutedColor);
        subLabel.AddThemeFontSizeOverride("font_size", 12);
        nameVbox.AddChild(subLabel);

        var pingLabel = new Label
        {
            Text = e.PingMs.HasValue ? $"{e.PingMs.Value} ms" : "—",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(80f, 0f),
        };
        Color pingColor = !e.PingMs.HasValue
            ? TextMutedColor
            : e.PingMs.Value < 80 ? SuccessColor
            : e.PingMs.Value < 250 ? AccentColor
            : DangerColor;
        pingLabel.AddThemeColorOverride("font_color", pingColor);
        pingLabel.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(pingLabel);

        return btn;
    }

    private void OnManualConnect()
    {
        string addr = (_manualInput?.Text ?? "").Trim();
        if (!string.IsNullOrEmpty(addr))
        {
            ServerChosen?.Invoke(addr);
            QueueFree();
        }
    }

    // ── Pixel-art style helpers (local copies of the lobby palette) ──

    private static StyleBoxFlat BuildPixelStyle(Color background, Color border, int borderWidth, int padding, int shadowSize)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
            CornerRadiusBottomRight = 0,
            CornerRadiusBottomLeft = 0,
            ContentMarginLeft = padding,
            ContentMarginTop = padding,
            ContentMarginRight = padding,
            ContentMarginBottom = padding,
            ShadowColor = new Color(border, 0.55f),
            ShadowSize = shadowSize,
            ShadowOffset = new Vector2(shadowSize, shadowSize),
        };
        return style;
    }

    private static void ApplyButtonStyle(Button button, bool primary, bool danger)
    {
        Color normalBg = primary ? AccentColor : danger ? new Color(0.63f, 0.24f, 0.24f, 0.9f) : CardColor;
        Color hoverBg = primary ? AccentBrightColor : danger ? new Color(0.73f, 0.28f, 0.28f, 1f) : SurfaceMutedColor;
        Color textColor = primary || danger ? PrimaryFgColor : TextStrongColor;

        button.AddThemeStyleboxOverride("normal", BuildPixelStyle(normalBg, BorderColor, 2, 8, 2));
        button.AddThemeStyleboxOverride("hover", BuildPixelStyle(hoverBg, BorderColor, 2, 8, 2));
        button.AddThemeStyleboxOverride("pressed", BuildPixelStyle(normalBg, BorderColor, 2, 8, 0));
        button.AddThemeStyleboxOverride("focus", BuildPixelStyle(normalBg, AccentColor, 2, 8, 0));
        button.AddThemeStyleboxOverride("disabled", BuildPixelStyle(SurfaceMutedColor, BorderColor, 2, 8, 0));
        button.AddThemeColorOverride("font_color", textColor);
        button.AddThemeColorOverride("font_hover_color", textColor);
        button.AddThemeColorOverride("font_pressed_color", textColor);
        button.AddThemeFontSizeOverride("font_size", 14);
    }

    private static void ApplyServerCardStyle(Button button)
    {
        button.AddThemeStyleboxOverride("normal", BuildPixelStyle(CardColor, BorderColor, 2, 6, 2));
        button.AddThemeStyleboxOverride("hover", BuildPixelStyle(SurfaceMutedColor, AccentColor, 2, 6, 2));
        button.AddThemeStyleboxOverride("pressed", BuildPixelStyle(SurfaceMutedColor, AccentColor, 2, 6, 0));
        button.AddThemeStyleboxOverride("focus", BuildPixelStyle(CardColor, AccentColor, 2, 6, 0));
    }

    private static void ApplyLineEditStyle(LineEdit input)
    {
        input.AddThemeStyleboxOverride("normal", BuildPixelStyle(InputBgColor, BorderColor, 2, 8, 0));
        input.AddThemeStyleboxOverride("focus", BuildPixelStyle(InputBgColor, AccentColor, 2, 8, 0));
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_placeholder_color", TextMutedColor);
        input.AddThemeFontSizeOverride("font_size", 14);
    }
}
