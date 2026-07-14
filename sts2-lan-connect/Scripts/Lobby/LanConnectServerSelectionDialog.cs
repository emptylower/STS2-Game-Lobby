using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

// Server picker — full-screen Control overlay with the lobby's retro pixel-art
// look. Built 100% in C# (no .tscn) so script resolution can never fail inside
// mod hosts. Palette and pixel-border treatment mirror LanConnectLobbyOverlay.
public partial class LanConnectServerSelectionDialog : Control
{
    private const float ServerListWheelStep = 120f;
    private const float ServerListTouchDragThreshold = 20f;
    private const float ServerListTouchTapMovementThreshold = ServerListTouchDragThreshold;

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
    private ScrollContainer? _listScroll;
    private LineEdit? _manualInput;
    private Label? _statusLabel;
    private CancellationTokenSource? _refreshCts;
    private int _refreshGeneration;

    private System.Collections.Generic.List<ServerListEntry> _entries = new();
    private bool _serverListTouchActive;
    private bool _serverListTouchDragging;
    private Vector2 _serverListTouchStartPosition;
    private float _serverListTouchStartScroll;
    private float _serverListTouchMaxDistance;
    private string? _serverListTouchTapAddress;
    private int _serverListTouchIndex = -1;

    public event Action<string>? ServerChosen;
    public event Action? Cancelled;

    public LanConnectServerSelectionDialog()
    {
        Name = "LanConnectServerSelectionDialog";
        MouseFilter = MouseFilterEnum.Stop;
        LanConnectBlockingModal.Register(this);
        // Full-rect anchored — it covers the whole screen and dims the
        // background, which makes the centered panel feel modal.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUi();
    }

    public override void _Ready()
    {
        _ = RefreshAsync();
    }

    public override void _ExitTree()
    {
        CancelRefresh();
        base._ExitTree();
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
        CancelRefresh();
        ResetServerListTouchTracking();
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
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                || @event is InputEventScreenTouch { Pressed: true })
            {
                AcceptEvent();
                Cancel();
            }
        };
        AddChild(backdrop);

        // Anchored panel — fills ~92% of the viewport with a small margin on
        // each side. Scales naturally on phone screens (where 720x520 was
        // unreadable in portrait) and on big desktops (where 720x520 was
        // dwarfed by the surrounding game). Letting the backdrop peek through
        // the 4% margin keeps the modal feel.
        var panel = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
        };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AnchorLeft = 0.04f;
        panel.AnchorTop = 0.04f;
        panel.AnchorRight = 0.96f;
        panel.AnchorBottom = 0.96f;
        panel.OffsetLeft = 0f;
        panel.OffsetTop = 0f;
        panel.OffsetRight = 0f;
        panel.OffsetBottom = 0f;
        panel.AddThemeStyleboxOverride("panel", BuildPixelStyle(SurfaceColor, BorderColor, borderWidth: 3, padding: 20, shadowSize: 4));
        AddChild(panel);

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

        _listScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _listScroll.GuiInput += OnServerListGuiInput;
        scrollPanel.AddChild(_listScroll);

        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _list.AddThemeConstantOverride("separation", 6);
        _listScroll.AddChild(_list);

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
        var (refreshGeneration, refreshToken) = BeginRefresh();

        try
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = "正在加载本地候选服务器...";
            }

            _entries = LanConnectServerListBootstrap.GatherInitialCandidates();
            if (!CanApplyRefresh(refreshGeneration, refreshToken))
            {
                return;
            }

            Render();
            SetRefreshStatus(enrichmentPending: true);

            Task cfDiscoveryTask = EnrichWithCloudflareResultsAsync(refreshGeneration, refreshToken);
            Task pingTask = EnrichVisibleEntriesAsync(refreshGeneration, refreshToken, _entries.ToList());

            await cfDiscoveryTask;
            await pingTask;

            if (!CanApplyRefresh(refreshGeneration, refreshToken))
            {
                return;
            }

            Render();
            SetRefreshStatus(enrichmentPending: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!CanApplyRefresh(refreshGeneration, refreshToken))
            {
                return;
            }

            Render();
            if (_statusLabel != null)
            {
                _statusLabel.Text = _entries.Count > 0
                    ? $"已显示 {_entries.Count} 个候选服务器，后台刷新失败。"
                    : "刷新失败，请稍后重试。";
            }
        }
    }

    private async Task EnrichWithCloudflareResultsAsync(int refreshGeneration, CancellationToken refreshToken)
    {
        List<ServerListEntry> cfEntries;
        try
        {
            cfEntries = await LanConnectServerListBootstrap.GatherCloudflareCandidatesAsync(refreshToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return;
        }

        if (!CanApplyRefresh(refreshGeneration, refreshToken))
        {
            return;
        }

        var mergeResult = LanConnectServerListBootstrap.MergeDiscoveredEntries(_entries, cfEntries);
        if (!mergeResult.Changed)
        {
            return;
        }

        Render();
        SetRefreshStatus(enrichmentPending: true);

        if (mergeResult.AddedEntries.Count > 0)
        {
            await EnrichVisibleEntriesAsync(refreshGeneration, refreshToken, mergeResult.AddedEntries);
        }
    }

    private async Task EnrichVisibleEntriesAsync(int refreshGeneration, CancellationToken refreshToken, System.Collections.Generic.IEnumerable<ServerListEntry> entries)
    {
        await LanConnectServerListBootstrap.PingAllAsync(entries, refreshToken);
        if (!CanApplyRefresh(refreshGeneration, refreshToken))
        {
            return;
        }

        Render();
        SetRefreshStatus(enrichmentPending: true);
    }

    private (int RefreshGeneration, CancellationToken RefreshToken) BeginRefresh()
    {
        CancelRefresh();
        _refreshCts = new CancellationTokenSource();
        _refreshGeneration += 1;
        return (_refreshGeneration, _refreshCts.Token);
    }

    private void CancelRefresh()
    {
        if (_refreshCts == null)
        {
            return;
        }

        try
        {
            _refreshCts.Cancel();
        }
        catch
        {
        }

        _refreshCts.Dispose();
        _refreshCts = null;
    }

    private bool CanApplyRefresh(int refreshGeneration, CancellationToken refreshToken)
    {
        return refreshGeneration == _refreshGeneration &&
               !refreshToken.IsCancellationRequested &&
               IsInstanceValid(this) &&
               !IsQueuedForDeletion();
    }

    private void SetRefreshStatus(bool enrichmentPending)
    {
        if (_statusLabel == null)
        {
            return;
        }

        _statusLabel.Text = enrichmentPending
            ? $"已显示 {_entries.Count} 个候选服务器，正在补充延迟和房间信息..."
            : $"共 {_entries.Count} 个候选服务器";
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
        // Card layout mirrors the in-lobby overlay's directory dialog
        // (LanConnectLobbyOverlay.BuildDirectoryServerCard) so the picker
        // and the in-overlay switch button feel like one component.
        // Two rows: name + latency on top, room count + build/create-room
        // guard on the bottom. Tooltip exposes the full address plus the
        // live bandwidth/utilization snapshot.
        bool blocked = string.Equals(e.CreateRoomGuardStatus, "block", StringComparison.OrdinalIgnoreCase);
        string guardText = e.CreateRoomGuardApplies
            ? blocked ? "建房暂停" : "可以建房"
            : "—";
        string rttText = e.PingMs.HasValue ? $"{e.PingMs.Value} ms" : "—";
        Color rttColor = !e.PingMs.HasValue
            ? TextMutedColor
            : e.PingMs.Value < 80 ? SuccessColor
            : e.PingMs.Value < 250 ? AccentColor
            : DangerColor;
        string roomsText = e.Rooms.HasValue ? $"房间 {e.Rooms.Value}" : "房间 —";

        string utilization = e.BandwidthUtilizationRatio.HasValue
            ? $"{(e.BandwidthUtilizationRatio.Value * 100):0.0}%"
            : "未计算";
        string bandwidth = e.CurrentBandwidthMbps.HasValue || e.ResolvedCapacityMbps.HasValue || e.BandwidthCapacityMbps.HasValue
            ? $"{FormatMbps(e.CurrentBandwidthMbps)} / {FormatMbps(e.ResolvedCapacityMbps ?? e.BandwidthCapacityMbps)}"
            : "未上报";

        var card = new Button
        {
            CustomMinimumSize = new Vector2(0f, 80f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = $"{e.Address}\n来源：{e.Source}\n利用率：{utilization}\n带宽：{bandwidth}",
            Text = string.Empty,
        };
        ApplyServerCardStyle(card);
        string addr = e.Address;
        card.Pressed += () =>
        {
            if (!_serverListTouchActive)
            {
                ChooseServer(addr);
            }
        };
        card.GuiInput += inputEvent => OnServerRowGuiInput(addr, inputEvent);

        var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        content.AddThemeConstantOverride("separation", 6);
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        content.OffsetLeft = 14;
        content.OffsetRight = -14;
        content.OffsetTop = 8;
        content.OffsetBottom = -8;
        card.AddChild(content);

        var topRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        topRow.AddThemeConstantOverride("separation", 10);
        content.AddChild(topRow);

        var nameLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(e.DisplayName) ? e.Address : e.DisplayName!,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = true,
        };
        nameLabel.AddThemeColorOverride("font_color", TextStrongColor);
        nameLabel.AddThemeFontSizeOverride("font_size", 20);
        topRow.AddChild(nameLabel);

        var rttLabel = new Label
        {
            Text = rttText,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(80f, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rttLabel.AddThemeColorOverride("font_color", rttColor);
        rttLabel.AddThemeFontSizeOverride("font_size", 20);
        topRow.AddChild(rttLabel);

        var bottomRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bottomRow.AddThemeConstantOverride("separation", 10);
        content.AddChild(bottomRow);

        var roomsLabel = new Label
        {
            Text = roomsText,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        roomsLabel.AddThemeColorOverride("font_color", TextMutedColor);
        roomsLabel.AddThemeFontSizeOverride("font_size", 14);
        bottomRow.AddChild(roomsLabel);

        var guardLabel = new Label
        {
            Text = guardText,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Color guardColor = !e.CreateRoomGuardApplies ? TextMutedColor : blocked ? DangerColor : SuccessColor;
        guardLabel.AddThemeColorOverride("font_color", guardColor);
        guardLabel.AddThemeFontSizeOverride("font_size", 14);
        bottomRow.AddChild(guardLabel);

        return card;
    }

    private void OnServerRowGuiInput(string address, InputEvent inputEvent)
    {
        HandleServerListPointerInput(inputEvent, address);
    }

    private void OnServerListGuiInput(InputEvent inputEvent)
    {
        HandleServerListPointerInput(inputEvent, null);
    }

    private bool HandleServerListPointerInput(InputEvent inputEvent, string? address)
    {
        if (inputEvent is InputEventMouseButton mouseButton &&
            mouseButton.Pressed &&
            (mouseButton.ButtonIndex == MouseButton.WheelUp || mouseButton.ButtonIndex == MouseButton.WheelDown))
        {
            AdjustServerListScroll(mouseButton.ButtonIndex == MouseButton.WheelUp ? -ServerListWheelStep : ServerListWheelStep);
            AcceptEvent();
            return true;
        }

        if (inputEvent is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                _serverListTouchActive = true;
                _serverListTouchDragging = false;
                _serverListTouchStartPosition = touch.Position;
                _serverListTouchStartScroll = _listScroll?.ScrollVertical ?? 0;
                _serverListTouchMaxDistance = 0f;
                _serverListTouchTapAddress = address;
                _serverListTouchIndex = touch.Index;
                AcceptEvent();
                return true;
            }

            if (!_serverListTouchActive || touch.Index != _serverListTouchIndex)
            {
                return false;
            }

            bool shouldChooseTappedServer = !_serverListTouchDragging &&
                                            _serverListTouchMaxDistance <= ServerListTouchTapMovementThreshold &&
                                            !string.IsNullOrWhiteSpace(address) &&
                                            !string.IsNullOrWhiteSpace(_serverListTouchTapAddress) &&
                                            string.Equals(_serverListTouchTapAddress, address, StringComparison.Ordinal);
            ResetServerListTouchTracking();
            if (shouldChooseTappedServer)
            {
                ChooseServer(address!);
            }

            AcceptEvent();
            return true;
        }

        if (inputEvent is InputEventScreenDrag screenDrag && _serverListTouchActive)
        {
            if (screenDrag.Index != _serverListTouchIndex)
            {
                return false;
            }

            float dragDistance = screenDrag.Position.DistanceTo(_serverListTouchStartPosition);
            _serverListTouchMaxDistance = Mathf.Max(_serverListTouchMaxDistance, dragDistance);
            if (!_serverListTouchDragging && dragDistance >= ServerListTouchDragThreshold)
            {
                _serverListTouchDragging = true;
            }

            if (_serverListTouchDragging)
            {
                SetServerListScroll(_serverListTouchStartScroll - (screenDrag.Position.Y - _serverListTouchStartPosition.Y));
            }

            AcceptEvent();
            return true;
        }

        return false;
    }

    private void AdjustServerListScroll(float delta)
    {
        if (_listScroll == null)
        {
            return;
        }

        SetServerListScroll(_listScroll.ScrollVertical + delta);
    }

    private void SetServerListScroll(float value)
    {
        if (_listScroll == null)
        {
            return;
        }

        VScrollBar scrollbar = _listScroll.GetVScrollBar();
        float maxScroll = Mathf.Max((float)scrollbar.MaxValue - (float)scrollbar.Page, 0f);
        _listScroll.ScrollVertical = Mathf.RoundToInt(Mathf.Clamp(value, 0f, maxScroll));
    }

    private void ResetServerListTouchTracking()
    {
        _serverListTouchActive = false;
        _serverListTouchDragging = false;
        _serverListTouchTapAddress = null;
        _serverListTouchIndex = -1;
        _serverListTouchStartPosition = Vector2.Zero;
        _serverListTouchStartScroll = 0f;
        _serverListTouchMaxDistance = 0f;
    }

    private static string FormatMbps(double? value)
    {
        if (!value.HasValue) return "未设置";
        if (value.Value <= 0) return "0.00 Mbps";
        if (value.Value < 0.01) return "< 0.01 Mbps";
        return $"{value.Value:0.00} Mbps";
    }

    private void OnManualConnect()
    {
        string addr = (_manualInput?.Text ?? "").Trim();
        if (!string.IsNullOrEmpty(addr))
        {
            ChooseServer(addr);
        }
    }

    private void ChooseServer(string address)
    {
        ResetServerListTouchTracking();
        ServerChosen?.Invoke(address);
        QueueFree();
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
