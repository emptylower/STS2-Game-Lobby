using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectLobbyOverlay : Control
{
    private enum LobbyLayoutMode
    {
        Desktop,
        Compact
    }

    private enum RoomAccessFilter
    {
        All,
        Public,
        Locked
    }

    private const float RoomListWheelStep = 120f;
    private const float RoomListTouchDragThreshold = 12f;
    private const string RefreshFailureSwitchHint = "! 可能服务器拥堵，建议切换服务器";
    private const int FilterPublicId = 100;
    private const int FilterLockedId = 101;
    private const int FilterJoinableId = 102;
    private const int FilterModeStandardId = 200;
    private const int FilterModeDailyId = 201;
    private const int FilterModeCustomId = 202;
    private const double JoinCancelRevealDelaySeconds = 3d;

    // ── Retro pixel-art palette (converted from reference UI oklch values) ──
    private static readonly Color BackdropColor = new(0.97f, 0.95f, 0.89f, 1f);        // #F8F1E3 oklch(0.96,0.02,85)
    private static readonly Color FrameColor = new(0.80f, 0.65f, 0.53f, 1f);           // #CBA688 oklch(0.75,0.06,60)
    private static readonly Color SurfaceColor = new(0.99f, 0.97f, 0.93f, 1f);         // #FDF8ED oklch(0.98,0.015,85)
    private static readonly Color SurfaceMutedColor = new(0.89f, 0.87f, 0.81f, 1f);    // #E4DDCF oklch(0.90,0.02,85)
    private static readonly Color AccentColor = new(0.87f, 0.41f, 0.00f, 1f);          // #DF6900 oklch(0.65,0.18,55)
    private static readonly Color AccentBrightColor = new(0.93f, 0.50f, 0.08f, 1f);    // #ED7F14 brighter hover
    private static readonly Color AccentMutedColor = new(0.87f, 0.41f, 0.00f, 0.10f);  // primary/10 per reference
    private static readonly Color TextStrongColor = new(0.21f, 0.10f, 0.04f, 1f);      // #341A09 oklch(0.25,0.05,50)
    private static readonly Color TextMutedColor = new(0.46f, 0.36f, 0.31f, 1f);       // #775D4F oklch(0.50,0.04,50)
    private static readonly Color SuccessColor = new(0.10f, 0.60f, 0.19f, 1f);         // #189A30 oklch(0.60,0.18,145)
    private static readonly Color DangerColor = new(0.80f, 0.15f, 0.18f, 1f);          // #CC272E oklch(0.55,0.20,25)
    private static readonly Color CardColor = new(0.99f, 0.97f, 0.93f, 1f);            // #FDF8ED oklch(0.98,0.015,85)
    private static readonly Color SecondaryColor = new(0.93f, 0.89f, 0.82f, 1f);       // #ECE4D2 oklch(0.92,0.025,85)
    private static readonly Color InputBgColor = new(0.95f, 0.92f, 0.86f, 1f);         // #F1EADC oklch(0.94,0.02,85)
    private static readonly Color BorderColor = new(0.80f, 0.65f, 0.53f, 1f);          // #CBA688 oklch(0.75,0.06,60)
    private static readonly Color PrimaryFgColor = new(0.15f, 0.05f, 0.00f, 1f);       // #270E01 oklch(0.20,0.05,50)

    private readonly List<LobbyRoomSummary> _rooms = new();
    private readonly List<LobbyAnnouncementItem> _announcements = new();

    private NMultiplayerSubmenu? _submenu;
    private NSubmenuStack? _stack;
    private Control? _loadingOverlay;
    private MarginContainer? _frameMargin;
    private Control? _headerContentHost;
    private HBoxContainer? _headerBrandRow;
    private Control? _mainContentHost;
    private HBoxContainer? _headerToolbar;
    private PanelContainer? _headerHealthPill;
    private Label? _headerTitleLabel;
    private Label? _headerSubtitleLabel;
    private Label? _heroTitleLabel;
    private Label? _heroSubtitleLabel;
    private Control? _roomStagePanel;
    private VBoxContainer? _sidebarContainer;
    private LobbyAnnouncementCarousel? _announcementCarousel;
    private HSeparator? _settingsSeparator;
    private VBoxContainer? _settingsSection;
    private Label? _networkSummaryLabel;
    private LineEdit? _displayNameInput;
    private VBoxContainer? _networkSettingsContainer;
    private Button? _toggleNetworkSettingsButton;
    private Button? _toggleSensitiveNetworkButton;
    private Button? _clearNetworkOverridesButton;
    private Button? _chooseDirectoryServerButton;
    private LineEdit? _serverBaseUrlInput;
    private LineEdit? _lobbyCreateRoomTokenInput;
    private Label? _statusLabel;
    private Label? _healthIndicatorLabel;
    private Label? _healthIndicatorLatencyLabel;
    private Control? _healthIndicatorDot;
    private Label? _statusHealthValueLabel;
    private Control? _statusHealthValueIcon;
    private Label? _statusLatencyValueLabel;
    private Label? _statusRoomCountValueLabel;
    private Label? _roomListSummaryLabel;
    private Label? _pageSummaryLabel;
    private HBoxContainer? _roomPagerRow;
    private ScrollContainer? _roomListScroll;
    private VBoxContainer? _roomListContainer;
    private Label? _roomHintLabel;
    private LineEdit? _roomSearchInput;
    private Button? _roomFilterMenuButton;
    private Control? _filterDialogContainer;
    private Button? _filterPublicButton;
    private Button? _filterLockedButton;
    private Button? _filterJoinableButton;
    private Button? _filterStandardButton;
    private Button? _filterDailyButton;
    private Button? _filterCustomButton;
    private Label? _actionAvailabilityLabel;
    private Label? _actionCreateStatusValue;
    private ColorRect? _actionCreateStatusDot;
    private Label? _actionJoinStatusValue;
    private ColorRect? _actionJoinStatusDot;
    private Label? _actionSyncStatusValue;
    private ColorRect? _actionSyncStatusDot;
    private HBoxContainer? _actionSyncStatusRow;
    private Button? _refreshButton;
    private Button? _createButton;
    private Button? _joinButton;
    private Button? _pagePreviousButton;
    private Button? _pageNextButton;
    private Button? _closeRoomButton;
    private Button? _closeButton;
    private Button? _settingsButton;
    private Button? _repairSaveButton;
    private Button? _copyDebugReportButton;
    private Control? _createDialogContainer;
    private Label? _createDialogErrorLabel;
    private LineEdit? _roomNameInput;
    private OptionButton? _roomTypeOption;
    private LineEdit? _roomPasswordInput;
    private SpinBox? _maxPlayersSpinBox;
    private Label? _maxPlayersHintLabel;
    private Control? _createGuardDialogContainer;
    private Label? _createGuardDialogTitle;
    private Label? _createGuardDialogMessage;
    private Label? _createGuardDialogDetail;
    private Button? _createGuardContinueButton;
    private Control? _joinPasswordDialogContainer;
    private Label? _joinPasswordDialogTitle;
    private Label? _joinPasswordDialogErrorLabel;
    private LineEdit? _joinPasswordInput;
    private LobbyRoomSummary? _pendingPasswordJoinRoom;
    private Control? _progressDialogContainer;
    private Label? _progressDialogTitle;
    private Label? _progressDialogMessage;
    private Label? _progressDialogHint;
    private Button? _progressDialogCancelButton;
    private Control? _resumeSlotDialogContainer;
    private Label? _resumeSlotDialogTitle;
    private Label? _resumeSlotDialogErrorLabel;
    private VBoxContainer? _resumeSlotDialogOptions;
    private Control? _inviteConfirmDialogContainer;
    private Label? _inviteConfirmDialogMessage;
    private LanConnectInvitePayload? _pendingInvitePayload;
    private Control? _directoryServerDialogContainer;
    private Label? _directoryServerDialogTitle;
    private Label? _directoryServerDialogStatusLabel;
    private GridContainer? _directoryServerDialogOptions;
    private bool _directoryServerLoadInFlight;
    private List<(LobbyDirectoryServerEntry Entry, double? RttMs)>? _cachedDirectoryServers;
    private LobbyRoomSummary? _pendingResumeJoinRoom;
    private string? _pendingResumeJoinPassword;
    private bool _networkFieldsRevealed;
    private bool _refreshInFlight;
    private bool _actionInFlight;
    private double _timeUntilAutoRefresh;
    private double _progressDialogTick;
    private double _progressDialogVisibleDuration;
    private int _progressDialogDotCount;
    private int _currentPageIndex;
    private int _consecutiveRefreshFailures;
    private double _lastLobbyRttMs = -1d;
    private string? _selectedRoomId;
    private string _roomSearchQuery = string.Empty;
    private string _lastActionDebugState = string.Empty;
    private string _lastStatusMessage = string.Empty;
    private string _progressDialogBaseMessage = string.Empty;
    private bool _roomListTouchActive;
    private bool _roomListTouchDragging;
    private Vector2 _roomListTouchStartPosition;
    private float _roomListTouchStartScroll;
    private string? _roomListTouchTapRoomId;
    private LobbyLayoutMode _layoutMode = LobbyLayoutMode.Desktop;
    private bool _joinableOnlyFilter;
    private bool _showPublicRooms = true;
    private bool _showLockedRooms = true;
    private bool _showStandardMode = true;
    private bool _showDailyMode = true;
    private bool _showCustomMode = true;
    private bool _progressDialogAllowCancel;
    private double _healthPulseTime;
    private Color _healthIndicatorDotColor = SuccessColor;
    private CancellationTokenSource? _activeJoinCancellationSource;

    public void Initialize(NMultiplayerSubmenu submenu, NSubmenuButton templateButton, NSubmenuStack stack, Control loadingOverlay)
    {
        _ = templateButton;
        _submenu = submenu;
        _stack = stack;
        _loadingOverlay = loadingOverlay;
        BuildUi();
        HideOverlay();
    }

    public override void _Process(double delta)
    {
        AnimateProgressDialog(delta);
        AnimateHealthIndicator(delta);

        if (!Visible || _refreshInFlight)
        {
            return;
        }

        _timeUntilAutoRefresh -= delta;
        if (_timeUntilAutoRefresh <= 0d)
        {
            TaskHelper.RunSafely(RefreshRoomsAsync(userInitiated: false));
        }
    }

    public void ShowOverlay()
    {
        GD.Print("sts2_lan_connect overlay: show requested");
        SetUnderlyingMenuVisible(false);
        Visible = true;
        SyncSettingsInputsFromConfig();
        ApplyResponsiveLayout();
        EnsureAnnouncementFallback();
        RebuildRoomStage();
        UpdateActionButtons();
        CheckClipboardForInviteCode();
        TaskHelper.RunSafely(CheckConnectivityAndRefreshAsync());
    }

    private async Task CheckConnectivityAndRefreshAsync()
    {
        if (!HasAvailableLobbyEndpoint())
        {
            GD.Print("sts2_lan_connect overlay: no lobby endpoint configured, opening server dialog");
            _ = OpenDirectoryServerDialogAsync();
            return;
        }

        // Pre-fetch directory servers in background so cache is warm.
        _ = RefreshDirectoryServersInBackgroundAsync();

        try
        {
            using LobbyApiClient apiClient = LobbyApiClient.CreateConfigured();
            LobbyHealthResponse health = await apiClient.GetHealthAsync();
            if (health.CreateRoomGuardStatus == "block")
            {
                GD.Print("sts2_lan_connect overlay: current server blocks room creation, auto-opening server dialog");
                SetStatus("当前服务器不允许创建房间，建议切换。");
                _ = OpenDirectoryServerDialogAsync();
                return;
            }

            GD.Print("sts2_lan_connect overlay: connectivity check passed, proceeding with refresh");
        }
        catch (Exception ex)
        {
            GD.Print($"sts2_lan_connect overlay: connectivity check failed ({ex.Message}), auto-opening server dialog");
            SetStatus("当前服务器无法连接，建议切换。");
            _ = OpenDirectoryServerDialogAsync();
            return;
        }

        await RefreshRoomsAsync(userInitiated: false);
    }

    private void HideOverlay()
    {
        GD.Print("sts2_lan_connect overlay: hide requested");
        PersistSettings();
        Visible = false;
        ResetRoomListTouchTracking();
        SetUnderlyingMenuVisible(true);

        if (_createDialogContainer != null)
        {
            _createDialogContainer.Visible = false;
        }

        if (_createGuardDialogContainer != null)
        {
            _createGuardDialogContainer.Visible = false;
        }

        if (_joinPasswordDialogContainer != null)
        {
            _joinPasswordDialogContainer.Visible = false;
        }

        if (_resumeSlotDialogContainer != null)
        {
            _resumeSlotDialogContainer.Visible = false;
        }

        if (_directoryServerDialogContainer != null)
        {
            _directoryServerDialogContainer.Visible = false;
        }

        if (_inviteConfirmDialogContainer != null)
        {
            _inviteConfirmDialogContainer.Visible = false;
        }

        HideProgressDialog();
    }

    private void BuildUi()
    {
        Name = LanConnectConstants.LobbyOverlayName;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Connect(Control.SignalName.Resized, Callable.From(ApplyResponsiveLayout));

        ColorRect backdrop = new()
        {
            Color = BackdropColor,
            MouseFilter = MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        _frameMargin = new MarginContainer();
        _frameMargin.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_frameMargin);

        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 16);
        _frameMargin.AddChild(root);

        root.AddChild(BuildHeaderRow());
        _settingsSeparator = new HSeparator
        {
            Visible = false
        };
        root.AddChild(_settingsSeparator);
        _settingsSection = BuildSettingsSection();
        _settingsSection.Visible = false;
        root.AddChild(_settingsSection);
        root.AddChild(BuildAnnouncementSection());
        root.AddChild(BuildMainContent());
        ApplyPassiveMouseFilterRecursive(root);

        AddChild(BuildCreateDialog());
        AddChild(BuildCreateGuardDialog());
        AddChild(BuildJoinPasswordDialog());
        AddChild(BuildProgressDialog());
        AddChild(BuildResumeSlotDialog());
        AddChild(BuildDirectoryServerDialog());
        AddChild(BuildFilterDialog());
        AddChild(BuildInviteConfirmDialog());
        ApplyResponsiveLayout();
    }

    private Control BuildHeaderRow()
    {
        VBoxContainer section = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        section.AddThemeConstantOverride("separation", 10);

        // Wrap entire header in a pixel-border panel
        PanelContainer headerFrame = CreatePixelBorderPanel(background: CardColor, padding: 16);
        headerFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        section.AddChild(headerFrame);

        _headerContentHost = new Control
        {
            CustomMinimumSize = new Vector2(0f, 52f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        headerFrame.AddChild(_headerContentHost);

        _headerBrandRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _headerBrandRow.AddThemeConstantOverride("separation", 14);

        PanelContainer badge = CreatePixelBorderSmPanel(background: AccentColor, padding: 0);
        badge.CustomMinimumSize = new Vector2(48f, 48f);
        _headerBrandRow.AddChild(badge);

        CenterContainer badgeCenter = new();
        badge.AddChild(badgeCenter);

        badgeCenter.AddChild(new GlyphIcon
        {
            Kind = GlyphIconKind.Gamepad,
            GlyphColor = PrimaryFgColor,
            CustomMinimumSize = new Vector2(28f, 28f)
        });

        VBoxContainer titleGroup = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        titleGroup.AddThemeConstantOverride("separation", 3);
        _headerBrandRow.AddChild(titleGroup);

        _headerTitleLabel = CreateTitleLabel("SLAY THE SPIRE 2", 26);
        titleGroup.AddChild(_headerTitleLabel);

        _headerSubtitleLabel = CreateBodyLabel("[ MOD LOBBY ]");
        _headerSubtitleLabel.AddThemeFontSizeOverride("font_size", 14);
        _headerSubtitleLabel.AddThemeColorOverride("font_color", TextMutedColor);
        titleGroup.AddChild(_headerSubtitleLabel);

        _headerToolbar = new HBoxContainer()
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        _headerToolbar.AddThemeConstantOverride("separation", 10);

        _headerHealthPill = CreatePixelBorderSmPanel(background: SecondaryColor, padding: 10);
        _headerHealthPill.CustomMinimumSize = new Vector2(200f, 0f);
        _headerToolbar.AddChild(_headerHealthPill);

        HBoxContainer healthRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        healthRow.AddThemeConstantOverride("separation", 10);
        _headerHealthPill.AddChild(healthRow);

        // Simple colored square — matches reference design (no WiFi icon)
        _healthIndicatorDot = new ColorRect
        {
            Color = SuccessColor,
            CustomMinimumSize = new Vector2(12f, 12f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        healthRow.AddChild(_healthIndicatorDot);

        _healthIndicatorLabel = CreateBodyLabel("ONLINE");
        _healthIndicatorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _healthIndicatorLabel.AddThemeFontSizeOverride("font_size", 15);
        _healthIndicatorLabel.AddThemeColorOverride("font_color", TextStrongColor);
        healthRow.AddChild(_healthIndicatorLabel);

        _healthIndicatorLatencyLabel = CreateBodyLabel("--");
        _healthIndicatorLatencyLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _healthIndicatorLatencyLabel.AddThemeFontSizeOverride("font_size", 15);
        _healthIndicatorLatencyLabel.AddThemeColorOverride("font_color", TextMutedColor);
        healthRow.AddChild(_healthIndicatorLatencyLabel);

        _chooseDirectoryServerButton = CreateToolbarButton("SERVER", "打开公共服务器列表，切换到其他大厅。", () => TaskHelper.RunSafely(OpenDirectoryServerDialogAsync()), GlyphIconKind.Server, accent: true);
        _headerToolbar.AddChild(_chooseDirectoryServerButton);

        _settingsButton = CreateToolbarIconButton("展开或收起设置", ToggleSettingsVisibility, GlyphIconKind.Gear);
        _headerToolbar.AddChild(_settingsButton);

        _closeButton = CreateDestructiveToolbarIconButton("返回上一级菜单", HideOverlay, GlyphIconKind.XClose);
        _headerToolbar.AddChild(_closeButton);

        // Remove the old hero section — the reference design places title in the header itself.
        // We keep hidden hero labels so responsive layout code doesn't null-ref.
        _heroTitleLabel = new Label { Visible = false };
        section.AddChild(_heroTitleLabel);
        _heroSubtitleLabel = new Label { Visible = false };
        section.AddChild(_heroSubtitleLabel);

        RebuildHeaderLayout();
        return section;
    }

    private Control BuildAnnouncementSection()
    {
        _announcementCarousel = new LobbyAnnouncementCarousel
        {
            AutoAdvanceSeconds = 6d,
        };
        return _announcementCarousel;
    }

    private VBoxContainer BuildSettingsSection()
    {
        VBoxContainer section = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };

        PanelContainer card = CreatePixelBorderPanel(background: CardColor, padding: 22);
        section.AddChild(card);

        VBoxContainer body = new();
        body.AddThemeConstantOverride("separation", 14);
        card.AddChild(body);

        body.AddChild(CreateSectionLabel("玩家与网络"));

        Label intro = CreateBodyLabel("普通玩家默认走内置大厅服务。开发网络覆盖仅用于排障或临时切服，默认不会在界面里明文回显。");
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(intro);

        body.AddChild(BuildLabeledInputRow("玩家名", LanConnectConfig.PlayerDisplayName, out _displayNameInput, "留空时自动使用当前系统用户名", showLengthCounter: true, maxLength: LanConnectConfig.MaxPlayerDisplayNameLength));

        _networkSummaryLabel = CreateBodyLabel(string.Empty);
        _networkSummaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_networkSummaryLabel);

        _toggleNetworkSettingsButton = CreateInlineButton("展开开发网络设置", ToggleNetworkSettingsVisibility);
        body.AddChild(_toggleNetworkSettingsButton);

        _networkSettingsContainer = new VBoxContainer
        {
            Visible = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _networkSettingsContainer.AddThemeConstantOverride("separation", 12);
        body.AddChild(_networkSettingsContainer);

        Label networkHint = CreateBodyLabel("这里只保存大厅 HTTP 覆盖地址和建房令牌。中心服务器与读取令牌默认随安装包内置，不对普通玩家开放 UI 编辑。");
        networkHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        networkHint.AddThemeColorOverride("font_color", TextMutedColor);
        _networkSettingsContainer.AddChild(networkHint);

        _networkSettingsContainer.AddChild(BuildLabeledInputRow("HTTP 覆盖", LanConnectConfig.LobbyServerBaseUrlOverride, out _serverBaseUrlInput, "留空则继续使用内置大厅；WS 会自动从 HTTP 地址推导"));
        _networkSettingsContainer.AddChild(BuildLabeledInputRow("建房令牌", LanConnectConfig.LobbyCreateRoomToken, out _lobbyCreateRoomTokenInput, "留空则继续使用安装包内置建房令牌；如管理员临时更换令牌，可在这里覆盖", showLengthCounter: false));

        if (_serverBaseUrlInput != null)
        {
            _serverBaseUrlInput.Secret = true;
        }

        if (_lobbyCreateRoomTokenInput != null)
        {
            _lobbyCreateRoomTokenInput.Secret = true;
        }

        HBoxContainer networkActions = new();
        networkActions.AddThemeConstantOverride("separation", 10);
        _networkSettingsContainer.AddChild(networkActions);

        _toggleSensitiveNetworkButton = CreateInlineButton("显示覆盖地址", ToggleSensitiveNetworkVisibility);
        networkActions.AddChild(_toggleSensitiveNetworkButton);

        _clearNetworkOverridesButton = CreateInlineButton("清空覆盖", ClearNetworkOverrides);
        networkActions.AddChild(_clearNetworkOverridesButton);

        Label repairHint = CreateBodyLabel("如果 Windows / 移动端多人续局出现坏档、读档失败或房间绑定异常，可在这里执行一次带备份的强制修复。");
        repairHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        repairHint.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(repairHint);

        _repairSaveButton = CreateActionButton(
            "强制修复多人存档",
            "先备份当前 modded profile，再按安装脚本同规则执行 vanilla -> modded 单向同步，并重检当前多人存档。",
            () => TaskHelper.RunSafely(RepairMultiplayerSaveAsync()),
            danger: true);
        body.AddChild(_repairSaveButton);

        _copyDebugReportButton = CreateActionButton(
            "复制本地调试报告",
            "收集当前客户端版本、网络配置、存档状态和最近的本地失败日志，并一键复制到剪贴板发给开发者。",
            CopyDebugReportToClipboard);
        body.AddChild(_copyDebugReportButton);
        return section;
    }

    private Control BuildMainContent()
    {
        _mainContentHost = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _roomStagePanel = BuildRoomStagePanel();
        BuildSidebar();
        RebuildMainContentLayout();
        return _mainContentHost;
    }

    private Control BuildRoomStagePanel()
    {
        VBoxContainer outer = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        outer.AddThemeConstantOverride("separation", 14);

        _roomListSummaryLabel = CreateBodyLabel("大厅当前没有房间。");
        _roomListSummaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _roomListSummaryLabel.AddThemeColorOverride("font_color", TextMutedColor);
        _roomListSummaryLabel.Visible = false;
        outer.AddChild(_roomListSummaryLabel);

        // Search/filter bar in a pixel-border panel
        outer.AddChild(BuildRoomFilterRow());

        // Room list in a pixel-border panel — no scroll, fixed 5-slot grid
        PanelContainer listFrame = CreatePixelBorderPanel(background: CardColor, padding: 14);
        listFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        listFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
        outer.AddChild(listFrame);

        // Use a plain Control so we can position cards with anchors (1/5 each)
        _roomListContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _roomListContainer.AddThemeConstantOverride("separation", 8);
        listFrame.AddChild(_roomListContainer);

        // Keep _roomListScroll as null-safe dummy for any code that still references it
        _roomListScroll = new ScrollContainer { Visible = false };
        outer.AddChild(_roomListScroll);

        _roomHintLabel = CreateBodyLabel("[ DOUBLE-CLICK TO JOIN ]  [ LOCKED ROOMS REQUIRE PASSWORD ]");
        _roomHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _roomHintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _roomHintLabel.AddThemeColorOverride("font_color", TextMutedColor);
        _roomHintLabel.AddThemeFontSizeOverride("font_size", 13);
        outer.AddChild(_roomHintLabel);
        return outer;
    }

    private Control BuildRoomFilterRow()
    {
        // All-in-one search + filter + pagination bar inside a pixel-border panel
        PanelContainer barPanel = CreatePixelBorderPanel(background: CardColor, padding: 14);
        barPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        HBoxContainer container = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        container.AddThemeConstantOverride("separation", 10);
        barPanel.AddChild(container);

        // Search icon
        container.AddChild(new GlyphIcon
        {
            Kind = GlyphIconKind.Search,
            GlyphColor = TextMutedColor,
            CustomMinimumSize = new Vector2(18f, 18f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        });

        // Search input (transparent, inset style)
        _roomSearchInput = new LineEdit
        {
            PlaceholderText = UiText("SEARCH ROOM / HOST / VERSION"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectAllOnFocus = true
        };
        ApplySearchInputStyle(_roomSearchInput);
        _roomSearchInput.Connect(LineEdit.SignalName.TextChanged, Callable.From<string>(OnRoomSearchChanged));
        container.AddChild(_roomSearchInput);

        // Filter button — opens a styled dialog instead of a native PopupMenu
        _roomFilterMenuButton = new Button
        {
            Text = UiText("筛选"),
            TooltipText = UiText("打开筛选面板，选择房间类型和游戏模式。"),
            CustomMinimumSize = new Vector2(96f, 44f)
        };
        ApplyInlineButtonStyle(_roomFilterMenuButton, accent: false);
        _roomFilterMenuButton.Connect(BaseButton.SignalName.Pressed, Callable.From(OpenFilterDialog));
        container.AddChild(_roomFilterMenuButton);

        // Clear button (ghost style — no border)
        Button clearButton = new()
        {
            Text = UiText("清空"),
            TooltipText = UiText("清空当前搜索关键词"),
            CustomMinimumSize = new Vector2(76f, 44f)
        };
        // Ghost style normally, green bg + white text + press animation on hover/press
        clearButton.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginLeft = 15, ContentMarginRight = 15, ContentMarginTop = 9, ContentMarginBottom = 9 });
        clearButton.AddThemeStyleboxOverride("hover", CreatePixelPressStyle(SuccessColor, BorderColor, 2, 10, 3, 1));
        clearButton.AddThemeStyleboxOverride("pressed", CreatePixelPressStyle(new Color(SuccessColor, 0.8f), BorderColor, 2, 10, 3, 3));
        clearButton.AddThemeStyleboxOverride("focus", CreatePixelPressStyle(SuccessColor, BorderColor, 2, 10, 3, 1));
        clearButton.AddThemeColorOverride("font_color", TextMutedColor);
        clearButton.AddThemeColorOverride("font_hover_color", CardColor);     // white on green
        clearButton.AddThemeColorOverride("font_pressed_color", CardColor);
        clearButton.AddThemeFontSizeOverride("font_size", 18);
        clearButton.Connect(BaseButton.SignalName.Pressed, Callable.From(ClearRoomFiltersAndSearch));
        container.AddChild(clearButton);

        // Vertical divider
        ColorRect divider = new()
        {
            Color = BorderColor,
            CustomMinimumSize = new Vector2(1f, 24f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };
        container.AddChild(divider);

        // Pagination (merged into the same bar)
        _pageSummaryLabel = CreateBodyLabel("PAGE 1/1");
        _pageSummaryLabel.AddThemeColorOverride("font_color", TextMutedColor);
        _pageSummaryLabel.AddThemeFontSizeOverride("font_size", 16);
        _pageSummaryLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        container.AddChild(_pageSummaryLabel);

        _pagePreviousButton = CreateInlineButton("<", () => ChangePage(-1));
        _pagePreviousButton.CustomMinimumSize = new Vector2(40f, 40f);
        container.AddChild(_pagePreviousButton);

        _pageNextButton = CreateInlineButton(">", () => ChangePage(1));
        _pageNextButton.CustomMinimumSize = new Vector2(40f, 40f);
        container.AddChild(_pageNextButton);

        // Keep _roomPagerRow as a hidden dummy so existing visibility logic doesn't null-ref
        _roomPagerRow = new HBoxContainer { Visible = false };

        UpdateRoomFilterButtons();
        return barPanel;
    }

    private Control BuildRoomPagerRow()
    {
        // Pagination is now merged into BuildRoomFilterRow; this is a no-op stub.
        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false
        };
        _roomPagerRow = row;
        _pageSummaryLabel ??= new Label();
        _pagePreviousButton ??= new Button();
        _pageNextButton ??= new Button();
        return row;
    }

    private Control BuildSidebar()
    {
        _sidebarContainer = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(200f, 0f),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _sidebarContainer.AddThemeConstantOverride("separation", 12);

        _sidebarContainer.AddChild(BuildStatusCard());
        _sidebarContainer.AddChild(BuildActionCard());
        return _sidebarContainer;
    }

    private void RebuildMainContentLayout()
    {
        if (_mainContentHost == null || _roomStagePanel == null || _sidebarContainer == null)
        {
            return;
        }

        foreach (Node child in _mainContentHost.GetChildren())
        {
            _mainContentHost.RemoveChild(child);
            child.QueueFree();
        }

        if (_layoutMode == LobbyLayoutMode.Compact)
        {
            VBoxContainer layout = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            layout.SetAnchorsPreset(LayoutPreset.FullRect);
            layout.AddThemeConstantOverride("separation", 18);
            _mainContentHost.AddChild(layout);

            AttachChild(layout, _roomStagePanel);
            AttachChild(layout, _sidebarContainer);
            _roomStagePanel.SizeFlagsStretchRatio = 1f;
            _sidebarContainer.SizeFlagsStretchRatio = 1f;
        }
        else
        {
            // Anchor-based proportional layout: use a plain Control wrapper
            // so the cleanup loop frees the wrapper, not the panels themselves.
            const float sidebarFraction = 0.25f;    // sidebar = 25% of width (3:1 ratio)
            const float gapPx = 16f;
            float halfGap = gapPx / 2f;

            Control wrapper = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            wrapper.SetAnchorsPreset(LayoutPreset.FullRect);
            _mainContentHost.AddChild(wrapper);

            _roomStagePanel.GetParent()?.RemoveChild(_roomStagePanel);
            _sidebarContainer.GetParent()?.RemoveChild(_sidebarContainer);
            wrapper.AddChild(_roomStagePanel);
            wrapper.AddChild(_sidebarContainer);

            // Room list: left 0% → 75%, full height
            _roomStagePanel.AnchorLeft = 0f;
            _roomStagePanel.AnchorRight = 1f - sidebarFraction;
            _roomStagePanel.AnchorTop = 0f;
            _roomStagePanel.AnchorBottom = 1f;
            _roomStagePanel.OffsetLeft = 0f;
            _roomStagePanel.OffsetRight = -halfGap;
            _roomStagePanel.OffsetTop = 0f;
            _roomStagePanel.OffsetBottom = 0f;

            // Sidebar: right 75% → 100%, full height
            _sidebarContainer.AnchorLeft = 1f - sidebarFraction;
            _sidebarContainer.AnchorRight = 1f;
            _sidebarContainer.AnchorTop = 0f;
            _sidebarContainer.AnchorBottom = 1f;
            _sidebarContainer.OffsetLeft = halfGap;
            _sidebarContainer.OffsetRight = 0f;
            _sidebarContainer.OffsetTop = 0f;
            _sidebarContainer.OffsetBottom = 0f;
        }
    }

    private void RebuildHeaderLayout()
    {
        if (_headerContentHost == null || _headerBrandRow == null || _headerToolbar == null)
        {
            return;
        }

        foreach (Node child in _headerContentHost.GetChildren())
        {
            _headerContentHost.RemoveChild(child);
            child.QueueFree();
        }

        BoxContainer layout = _layoutMode == LobbyLayoutMode.Compact
            ? new VBoxContainer()
            : new HBoxContainer();
        layout.SetAnchorsPreset(LayoutPreset.FullRect);
        layout.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        layout.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        layout.AddThemeConstantOverride("separation", _layoutMode == LobbyLayoutMode.Compact ? 12 : 18);
        _headerContentHost.AddChild(layout);

        AttachChild(layout, _headerBrandRow);
        AttachChild(layout, _headerToolbar);

        _headerBrandRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _headerBrandRow.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        _headerToolbar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _headerToolbar.SizeFlagsHorizontal = _layoutMode == LobbyLayoutMode.Compact
            ? SizeFlags.Fill
            : SizeFlags.ShrinkEnd;
    }

    private static void AttachChild(Container parent, Control child)
    {
        child.GetParent()?.RemoveChild(child);
        parent.AddChild(child);
    }

    private void ApplyResponsiveLayout()
    {
        Vector2 size = GetViewportRect().Size;
        float aspectRatio = size.Y <= 0f ? 1f : size.X / size.Y;
        bool compact = size.X < 1180f || aspectRatio < 1.34f;
        LobbyLayoutMode nextMode = compact ? LobbyLayoutMode.Compact : LobbyLayoutMode.Desktop;
        bool layoutChanged = _layoutMode != nextMode;
        _layoutMode = nextMode;

        if (_frameMargin != null)
        {
            if (compact)
            {
                _frameMargin.OffsetLeft = 14f;
                _frameMargin.OffsetTop = 14f;
                _frameMargin.OffsetRight = -14f;
                _frameMargin.OffsetBottom = -14f;
            }
            else
            {
                _frameMargin.OffsetLeft = 30f;
                _frameMargin.OffsetTop = 26f;
                _frameMargin.OffsetRight = -30f;
                _frameMargin.OffsetBottom = -26f;
            }
        }

        if (_sidebarContainer != null)
        {
            _sidebarContainer.CustomMinimumSize = new Vector2(compact ? 0f : 200f, 0f);
            _sidebarContainer.SizeFlagsHorizontal = compact ? SizeFlags.ExpandFill : SizeFlags.Fill;
        }

        if (_headerContentHost != null)
        {
            _headerContentHost.CustomMinimumSize = new Vector2(0f, compact ? 106f : 64f);
        }

        if (_headerTitleLabel != null)
        {
            _headerTitleLabel.AddThemeFontSizeOverride("font_size", compact ? 22 : 26);
        }

        if (_headerSubtitleLabel != null)
        {
            _headerSubtitleLabel.AddThemeFontSizeOverride("font_size", compact ? 14 : 15);
        }

        if (_heroTitleLabel != null)
        {
            _heroTitleLabel.AddThemeFontSizeOverride("font_size", compact ? 30 : 34);
        }

        if (_heroSubtitleLabel != null)
        {
            _heroSubtitleLabel.AddThemeFontSizeOverride("font_size", compact ? 14 : 15);
        }

        if (_headerToolbar != null)
        {
            _headerToolbar.AddThemeConstantOverride("separation", compact ? 8 : 10);
        }

        if (_headerHealthPill != null)
        {
            _headerHealthPill.CustomMinimumSize = new Vector2(compact ? 148f : 200f, 0f);
        }

        if (_chooseDirectoryServerButton != null)
        {
            _chooseDirectoryServerButton.CustomMinimumSize = new Vector2(compact ? 148f : 170f, compact ? 46f : 50f);
            SetButtonText(_chooseDirectoryServerButton, "SERVER");
        }

        if (_settingsButton != null)
        {
            _settingsButton.CustomMinimumSize = new Vector2(compact ? 46f : 50f, compact ? 46f : 50f);
        }

        if (_closeButton != null)
        {
            _closeButton.CustomMinimumSize = new Vector2(compact ? 46f : 50f, compact ? 46f : 50f);
        }

        if (layoutChanged || _headerContentHost?.GetChildCount() == 0)
        {
            RebuildHeaderLayout();
        }

        if (layoutChanged || _mainContentHost?.GetChildCount() == 0)
        {
            RebuildMainContentLayout();
        }

        _announcementCarousel?.SetCompactMode(compact);
    }

    private Label CreateMetricRow(VBoxContainer parent, string labelText, string valueText)
    {
        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 12);
        parent.AddChild(row);

        Label label = CreateBodyLabel(labelText);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.AddThemeColorOverride("font_color", TextMutedColor);
        label.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(label);

        Label value = CreateBodyLabel(valueText);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.AddThemeFontSizeOverride("font_size", 17);
        row.AddChild(value);
        return value;
    }

    private Control BuildStatusCard()
    {
        PanelContainer card = CreatePixelBorderPanel(background: CardColor, padding: 18);
        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 14);
        card.AddChild(body);

        // Header with bottom border
        HBoxContainer header = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 8);
        body.AddChild(header);
        header.AddChild(new GlyphIcon
        {
            Kind = GlyphIconKind.Globe,
            GlyphColor = AccentColor,
            CustomMinimumSize = new Vector2(18f, 18f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        });
        Label statusTitle = CreateBodyLabel("STATUS");
        statusTitle.AddThemeColorOverride("font_color", TextStrongColor);
        statusTitle.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(statusTitle);

        // Separator line
        ColorRect sep = new()
        {
            Color = BorderColor,
            CustomMinimumSize = new Vector2(0f, 2f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        body.AddChild(sep);

        _statusHealthValueLabel = CreateMetricStatusRow(body, "SERVER", "OK");
        _statusLatencyValueLabel = CreateMetricRow(body, "PING", "--");
        _statusRoomCountValueLabel = CreateMetricRow(body, "ROOMS", "0");

        _statusLabel = CreateBodyLabel(string.Empty);
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _statusLabel.Visible = false;
        body.AddChild(_statusLabel);
        return card;
    }

    private Control BuildActionCard()
    {
        PanelContainer card = CreatePixelBorderPanel(background: CardColor, padding: 16);
        card.SizeFlagsVertical = SizeFlags.ExpandFill;
        VBoxContainer body = new();
        body.AddThemeConstantOverride("separation", 8);
        card.AddChild(body);

        // Header with bottom border
        HBoxContainer header = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 8);
        body.AddChild(header);
        header.AddChild(new GlyphIcon
        {
            Kind = GlyphIconKind.Zap,
            GlyphColor = AccentColor,
            CustomMinimumSize = new Vector2(18f, 18f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        });
        Label actTitle = CreateBodyLabel("ACTIONS");
        actTitle.AddThemeColorOverride("font_color", TextStrongColor);
        actTitle.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(actTitle);

        ColorRect sep = new()
        {
            Color = BorderColor,
            CustomMinimumSize = new Vector2(0f, 2f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        body.AddChild(sep);

        _createButton = CreateActionButton("CREATE", "先在本地起 ENet Host，再把房间发布到大厅。", () => TaskHelper.RunSafely(BeginCreateRoomFlowAsync()), primary: true, iconKind: GlyphIconKind.Plus);
        body.AddChild(_createButton);

        _joinButton = CreateActionButton("JOIN", "加入当前选中的房间，密码房会先弹出输入框。", JoinSelectedRoom, iconKind: GlyphIconKind.JoinArrow);
        body.AddChild(_joinButton);

        _refreshButton = CreateActionButton("REFRESH", "立即抓取最新房间列表，并重置自动刷新计时。", () => TaskHelper.RunSafely(RefreshRoomsAsync(userInitiated: true)), iconKind: GlyphIconKind.Refresh);
        body.AddChild(_refreshButton);

        _closeRoomButton = CreateActionButton("关闭我的房间", "关闭当前托管中的大厅房间，并从房间列表里移除。", () => TaskHelper.RunSafely(CloseMyRoomAsync()), danger: true);
        _closeRoomButton.Visible = false;
        body.AddChild(_closeRoomButton);

        // Spacer before status indicators (reference: mt-3 = 12px)
        Control statusSpacer = new() { CustomMinimumSize = new Vector2(0f, 6f) };
        body.AddChild(statusSpacer);

        // Status box (sunken/inset style) with structured KEY: VALUE rows
        PanelContainer statusBox = new() { ClipContents = true };
        statusBox.AddThemeStyleboxOverride("panel", CreatePixelStyle(SurfaceMutedColor, BorderColor, borderWidth: 2, padding: 8, shadowSize: 0));
        VBoxContainer statusBody = new();
        statusBody.AddThemeConstantOverride("separation", 4);
        statusBox.AddChild(statusBody);

        (_actionCreateStatusDot, _actionCreateStatusValue) = BuildActionStatusRow(statusBody, "CREATE:");
        (_actionJoinStatusDot, _actionJoinStatusValue) = BuildActionStatusRow(statusBody, "JOIN:");
        (_actionSyncStatusDot, _actionSyncStatusValue, _actionSyncStatusRow) = BuildActionStatusRowWithContainer(statusBody, "");

        // Keep legacy label hidden for any code that still references it
        _actionAvailabilityLabel = new Label { Visible = false };
        statusBody.AddChild(_actionAvailabilityLabel);

        body.AddChild(statusBox);
        return card;
    }

    private Control BuildSupportCard()
    {
        PanelContainer card = CreatePixelBorderPanel(background: CardColor, padding: 22);
        VBoxContainer body = new();
        body.AddThemeConstantOverride("separation", 12);
        card.AddChild(body);

        Label title = CreateBodyLabel("手动 LAN/IP 直连仍保留在原 Host/Join 页面，仅作为开发和故障回退入口。");
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(title);
        return card;
    }

    private Control BuildCreateDialog()
    {
        Control shell = CreateDialogShell(out VBoxContainer body);
        _createDialogContainer = shell;

        body.AddChild(CreateSectionLabel("创建房间"));

        body.AddChild(BuildLabeledInputRow("房间名", GetSuggestedRoomName(), out _roomNameInput, "房间列表里展示的名称", showLengthCounter: true, maxLength: LanConnectConfig.MaxRoomNameLength));
        body.AddChild(BuildLabeledOptionRow(
            "房间类型",
            out _roomTypeOption,
            ("标准模式", 0),
            ("多人每日挑战", 1),
            ("自定义模式", 2)));
        body.AddChild(BuildLabeledInputRow("可选密码", string.Empty, out _roomPasswordInput, "留空表示公开房间", showLengthCounter: true, maxLength: LanConnectConfig.MaxRoomPasswordLength));
        body.AddChild(BuildMaxPlayersRow());

        if (_roomNameInput != null)
        {
            _roomNameInput.Connect(LineEdit.SignalName.TextSubmitted, Callable.From<string>(_ => TaskHelper.RunSafely(CreateRoomAsync())));
        }

        if (_roomPasswordInput != null)
        {
            _roomPasswordInput.Connect(LineEdit.SignalName.TextSubmitted, Callable.From<string>(_ => TaskHelper.RunSafely(CreateRoomAsync())));
        }

        _createDialogErrorLabel = CreateBodyLabel(string.Empty);
        _createDialogErrorLabel.Visible = false;
        _createDialogErrorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _createDialogErrorLabel.AddThemeColorOverride("font_color", DangerColor);
        body.AddChild(_createDialogErrorLabel);

        HBoxContainer buttons = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        buttons.AddThemeConstantOverride("separation", 10);
        body.AddChild(buttons);

        Button cancel = CreateActionButton("取消", "返回大厅，不发布房间。", CloseCreateDialog);
        cancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(cancel);

        Button submit = CreateActionButton("发布房间", "创建新的大厅房间，并直接进入现有联机流程。", () => TaskHelper.RunSafely(CreateRoomAsync()), primary: true);
        submit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(submit);
        return shell;
    }

    private Control BuildCreateGuardDialog()
    {
        Control shell = CreateDialogShell(out VBoxContainer body);
        _createGuardDialogContainer = shell;

        _createGuardDialogTitle = CreateSectionLabel("创建房间提示");
        body.AddChild(_createGuardDialogTitle);

        _createGuardDialogMessage = CreateBodyLabel("正在检查当前服务器的可用带宽。");
        _createGuardDialogMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_createGuardDialogMessage);

        _createGuardDialogDetail = CreateBodyLabel(string.Empty);
        _createGuardDialogDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _createGuardDialogDetail.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(_createGuardDialogDetail);

        HBoxContainer buttons = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        buttons.AddThemeConstantOverride("separation", 10);
        body.AddChild(buttons);

        Button cancel = CreateActionButton("取消", "返回大厅，不继续这次建房操作。", CloseCreateGuardDialog);
        cancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(cancel);

        Button switchServer = CreateActionButton(
            "切换服务器",
            "打开公共服务器列表，选择其他可用服务器后再创建房间。",
            () => TaskHelper.RunSafely(SwitchServerFromCreateGuardAsync()),
            primary: true);
        switchServer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(switchServer);

        _createGuardContinueButton = CreateActionButton(
            "继续创建",
            "忽略当前提示并继续打开建房窗口。",
            ContinueCreateAfterGuardDecision);
        _createGuardContinueButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _createGuardContinueButton.Visible = false;
        buttons.AddChild(_createGuardContinueButton);
        return shell;
    }

    private Control BuildJoinPasswordDialog()
    {
        Control shell = CreateDialogShell(out VBoxContainer body);
        _joinPasswordDialogContainer = shell;

        _joinPasswordDialogTitle = CreateSectionLabel("输入房间密码");
        body.AddChild(_joinPasswordDialogTitle);

        Label description = CreateBodyLabel("该房间已启用密码保护。输入正确密码后会直接走现有 JoinFlow。");
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(description);

        body.AddChild(BuildLabeledInputRow("密码", string.Empty, out _joinPasswordInput, "该房间开启了密码保护", showLengthCounter: true, maxLength: LanConnectConfig.MaxRoomPasswordLength));
        if (_joinPasswordInput != null)
        {
            _joinPasswordInput.Secret = true;
            _joinPasswordInput.Connect(LineEdit.SignalName.TextSubmitted, Callable.From<string>(_ => TaskHelper.RunSafely(SubmitJoinPasswordAsync())));
        }

        _joinPasswordDialogErrorLabel = CreateBodyLabel(string.Empty);
        _joinPasswordDialogErrorLabel.Visible = false;
        _joinPasswordDialogErrorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _joinPasswordDialogErrorLabel.AddThemeColorOverride("font_color", DangerColor);
        body.AddChild(_joinPasswordDialogErrorLabel);

        HBoxContainer buttons = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        buttons.AddThemeConstantOverride("separation", 10);
        body.AddChild(buttons);

        Button cancel = CreateActionButton("取消", "返回大厅，不发起加入。", CloseJoinPasswordDialog);
        cancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(cancel);

        Button submit = CreateActionButton("加入房间", "使用当前密码加入房间。", () => TaskHelper.RunSafely(SubmitJoinPasswordAsync()), primary: true);
        submit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(submit);
        return shell;
    }

    private Control BuildResumeSlotDialog()
    {
        Control shell = CreateDialogShell(out VBoxContainer body);
        _resumeSlotDialogContainer = shell;

        _resumeSlotDialogTitle = CreateSectionLabel("选择续局角色");
        body.AddChild(_resumeSlotDialogTitle);

        Label description = CreateBodyLabel("这个房间来自多人续局存档。请选择一个当前没人控制的角色槽位，再进入该续局。");
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(description);

        _resumeSlotDialogOptions = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _resumeSlotDialogOptions.AddThemeConstantOverride("separation", 10);
        body.AddChild(_resumeSlotDialogOptions);

        _resumeSlotDialogErrorLabel = CreateBodyLabel(string.Empty);
        _resumeSlotDialogErrorLabel.Visible = false;
        _resumeSlotDialogErrorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _resumeSlotDialogErrorLabel.AddThemeColorOverride("font_color", DangerColor);
        body.AddChild(_resumeSlotDialogErrorLabel);

        Button cancel = CreateActionButton("取消", "返回大厅，不加入该续局房间。", CloseResumeSlotDialog);
        cancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(cancel);
        return shell;
    }

    private Control BuildProgressDialog()
    {
        Control shell = CreateDialogShell(out VBoxContainer body);
        _progressDialogContainer = shell;

        HBoxContainer header = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        header.AddThemeConstantOverride("separation", 12);
        body.AddChild(header);

        _progressDialogTitle = CreateSectionLabel("正在处理");
        _progressDialogTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_progressDialogTitle);

        _progressDialogCancelButton = CreateDestructiveToolbarIconButton("取消当前连接", CancelActiveJoinRequest, GlyphIconKind.XClose);
        _progressDialogCancelButton.CustomMinimumSize = new Vector2(42f, 42f);
        _progressDialogCancelButton.Visible = false;
        header.AddChild(_progressDialogCancelButton);

        _progressDialogMessage = CreateTitleLabel("正在连接房间", 24);
        _progressDialogMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_progressDialogMessage);

        _progressDialogHint = CreateBodyLabel("连接较慢时请稍候，期间不要重复点击按钮或关闭页面。");
        _progressDialogHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _progressDialogHint.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(_progressDialogHint);
        return shell;
    }

    private Control BuildDirectoryServerDialog()
    {
        Control shell = CreateDialogShell(out VBoxContainer body);
        _directoryServerDialogContainer = shell;

        // Enlarge dialog to cover ~80% of the screen for comfortable browsing.
        if (body.GetParent() is PanelContainer card)
        {
            card.CustomMinimumSize = new Vector2(0f, 0f);
            if (card.GetParent() is MarginContainer margin)
            {
                // Use percentage-based anchors instead of fixed offsets:
                // roughly 10% margin on each side → 80% of screen.
                if (margin.GetParent() is CenterContainer cc)
                {
                    cc.RemoveChild(margin);
                    margin.SetAnchorsPreset(LayoutPreset.FullRect);
                    margin.OffsetLeft = 0f;
                    margin.OffsetTop = 0f;
                    margin.OffsetRight = 0f;
                    margin.OffsetBottom = 0f;
                    margin.AddThemeConstantOverride("margin_left", 80);
                    margin.AddThemeConstantOverride("margin_top", 60);
                    margin.AddThemeConstantOverride("margin_right", 80);
                    margin.AddThemeConstantOverride("margin_bottom", 60);
                    shell.AddChild(margin);
                }
            }
        }

        _directoryServerDialogTitle = CreateSectionLabel("切换大厅服务器");
        _directoryServerDialogTitle.AddThemeFontSizeOverride("font_size", 22);
        body.AddChild(_directoryServerDialogTitle);

        _directoryServerDialogStatusLabel = CreateBodyLabel("选择一个大厅服务器。延迟越低体验越好。");
        _directoryServerDialogStatusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _directoryServerDialogStatusLabel.AddThemeColorOverride("font_color", TextMutedColor);
        _directoryServerDialogStatusLabel.AddThemeFontSizeOverride("font_size", 18);
        body.AddChild(_directoryServerDialogStatusLabel);

        _directoryServerDialogOptions = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _directoryServerDialogOptions.AddThemeConstantOverride("h_separation", 14);
        _directoryServerDialogOptions.AddThemeConstantOverride("v_separation", 14);

        ScrollContainer directoryScroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 300f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        directoryScroll.AddChild(_directoryServerDialogOptions);
        body.AddChild(directoryScroll);

        HBoxContainer actions = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        actions.AddThemeConstantOverride("separation", 10);
        body.AddChild(actions);

        Button refresh = CreateActionButton("刷新列表", "重新从中心服务器拉取可用大厅列表。", () => TaskHelper.RunSafely(RefreshDirectoryServersAsync()), primary: true);
        refresh.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        actions.AddChild(refresh);

        Button cancel = CreateActionButton("关闭", "返回设置页面。", CloseDirectoryServerDialog);
        cancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        actions.AddChild(cancel);
        return shell;
    }

    private Control BuildFilterDialog()
    {
        Control shell = CreateDialogShell(out VBoxContainer body);
        _filterDialogContainer = shell;

        // ── Header row: title (left) + red close button (right) ──
        HBoxContainer header = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 10);
        body.AddChild(header);

        Label title = CreateSectionLabel("筛选条件");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);

        Button closeBtn = new()
        {
            Text = UiText("✕"),
            CustomMinimumSize = new Vector2(44f, 44f),
            TooltipText = UiText("关闭筛选面板")
        };
        // Red close button with press animation
        Color dangerHover = new(0.90f, 0.22f, 0.22f, 1f);
        Color dangerPressed = new(0.65f, 0.12f, 0.12f, 1f);
        closeBtn.AddThemeStyleboxOverride("normal", CreatePixelPressStyle(DangerColor, DangerColor, 2, 10, 3, 0));
        closeBtn.AddThemeStyleboxOverride("hover", CreatePixelPressStyle(dangerHover, DangerColor, 2, 10, 3, 1));
        closeBtn.AddThemeStyleboxOverride("pressed", CreatePixelPressStyle(dangerPressed, DangerColor, 2, 10, 3, 3));
        closeBtn.AddThemeStyleboxOverride("focus", CreatePixelPressStyle(dangerHover, DangerColor, 2, 10, 3, 1));
        closeBtn.AddThemeColorOverride("font_color", CardColor);
        closeBtn.AddThemeColorOverride("font_hover_color", CardColor);
        closeBtn.AddThemeColorOverride("font_pressed_color", CardColor);
        closeBtn.AddThemeFontSizeOverride("font_size", 18);
        closeBtn.Connect(BaseButton.SignalName.Pressed, Callable.From(CloseFilterDialog));
        header.AddChild(closeBtn);

        // ── Separator ──
        body.AddChild(new ColorRect
        {
            Color = BorderColor,
            CustomMinimumSize = new Vector2(0f, 2f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        });

        // ── Section: 房间类型 ──
        body.AddChild(CreateBodyLabel("房间类型"));

        HBoxContainer accessRow = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        accessRow.AddThemeConstantOverride("separation", 10);
        body.AddChild(accessRow);

        _filterPublicButton = CreateFilterToggleButton("公开", () =>
        {
            _showPublicRooms = !_showPublicRooms;
            SyncFilterDialogButtons();
            ApplyRoomFilterState("dialog_public");
        });
        accessRow.AddChild(_filterPublicButton);

        _filterLockedButton = CreateFilterToggleButton("上锁", () =>
        {
            _showLockedRooms = !_showLockedRooms;
            SyncFilterDialogButtons();
            ApplyRoomFilterState("dialog_locked");
        });
        accessRow.AddChild(_filterLockedButton);

        _filterJoinableButton = CreateFilterToggleButton("可加入", () =>
        {
            _joinableOnlyFilter = !_joinableOnlyFilter;
            SyncFilterDialogButtons();
            ApplyRoomFilterState("dialog_joinable");
        });
        accessRow.AddChild(_filterJoinableButton);

        // ── Section: 游戏模式 ──
        body.AddChild(CreateBodyLabel("游戏模式"));

        HBoxContainer modeRow = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        modeRow.AddThemeConstantOverride("separation", 10);
        body.AddChild(modeRow);

        _filterStandardButton = CreateFilterToggleButton("标准模式", () =>
        {
            _showStandardMode = !_showStandardMode;
            SyncFilterDialogButtons();
            ApplyRoomFilterState("dialog_standard");
        });
        modeRow.AddChild(_filterStandardButton);

        _filterDailyButton = CreateFilterToggleButton("多人每日挑战", () =>
        {
            _showDailyMode = !_showDailyMode;
            SyncFilterDialogButtons();
            ApplyRoomFilterState("dialog_daily");
        });
        modeRow.AddChild(_filterDailyButton);

        _filterCustomButton = CreateFilterToggleButton("自定义模式", () =>
        {
            _showCustomMode = !_showCustomMode;
            SyncFilterDialogButtons();
            ApplyRoomFilterState("dialog_custom");
        });
        modeRow.AddChild(_filterCustomButton);

        return shell;
    }

    private Button CreateFilterToggleButton(string text, Action onPressed)
    {
        Button button = new()
        {
            Text = UiText(text),
            CustomMinimumSize = new Vector2(0f, 44f)
        };
        ApplyFilterChipStyle(button, active: true);
        button.Connect(BaseButton.SignalName.Pressed, Callable.From(onPressed));
        return button;
    }

    private void SyncFilterDialogButtons()
    {
        ApplyFilterChipStyle(_filterPublicButton, _showPublicRooms);
        ApplyFilterChipStyle(_filterLockedButton, _showLockedRooms);
        ApplyFilterChipStyle(_filterJoinableButton, _joinableOnlyFilter);
        ApplyFilterChipStyle(_filterStandardButton, _showStandardMode);
        ApplyFilterChipStyle(_filterDailyButton, _showDailyMode);
        ApplyFilterChipStyle(_filterCustomButton, _showCustomMode);
        // Also update the 筛选 button accent state in the search bar
        if (_roomFilterMenuButton != null)
        {
            ApplyInlineButtonStyle(_roomFilterMenuButton, HasRoomSearchOrFilter());
        }
    }

    private void OpenFilterDialog()
    {
        if (_filterDialogContainer == null) return;
        SyncFilterDialogButtons();
        _filterDialogContainer.Visible = true;
        _filterDialogContainer.MoveToFront();
    }

    private void CloseFilterDialog()
    {
        if (_filterDialogContainer != null)
        {
            _filterDialogContainer.Visible = false;
        }
    }

    private Control CreateDialogShell(out VBoxContainer body)
    {
        Control shell = new()
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        shell.SetAnchorsPreset(LayoutPreset.FullRect);

        ColorRect veil = new()
        {
            Color = new Color(0f, 0f, 0f, 0.45f),
            MouseFilter = MouseFilterEnum.Stop
        };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        shell.AddChild(veil);

        CenterContainer center = new();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        shell.AddChild(center);

        MarginContainer margin = new();
        margin.SetAnchorsPreset(LayoutPreset.Center);
        margin.OffsetLeft = -300f;
        margin.OffsetTop = -180f;
        margin.OffsetRight = 300f;
        margin.OffsetBottom = 180f;
        center.AddChild(margin);

        PanelContainer card = CreatePixelBorderPanel(background: CardColor, padding: 22);
        card.CustomMinimumSize = new Vector2(560f, 0f);
        margin.AddChild(card);

        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 14);
        card.AddChild(body);
        return shell;
    }

    private Control BuildLabeledInputRow(string labelText, string initialValue, out LineEdit input, string placeholder, bool showLengthCounter = false, int maxLength = 0)
    {
        VBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 6);

        HBoxContainer header = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        header.AddThemeConstantOverride("separation", 8);
        row.AddChild(header);

        Label label = CreateBodyLabel(labelText);
        label.AddThemeColorOverride("font_color", TextStrongColor);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(label);

        input = new LineEdit
        {
            Text = initialValue,
            PlaceholderText = UiText(placeholder),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectAllOnFocus = true,
            MaxLength = maxLength
        };
        ApplyInputStyle(input);
        if (showLengthCounter)
        {
            LineEdit lengthTrackedInput = input;
            Label counterLabel = CreateBodyLabel(string.Empty);
            counterLabel.HorizontalAlignment = HorizontalAlignment.Right;
            counterLabel.AddThemeColorOverride("font_color", TextMutedColor);
            header.AddChild(counterLabel);

            void UpdateCounter()
            {
                int maxLengthValue = lengthTrackedInput.MaxLength > 0 ? lengthTrackedInput.MaxLength : lengthTrackedInput.Text.Length;
                SetLabelText(counterLabel, $"{lengthTrackedInput.Text.Length}/{maxLengthValue}");
            }

            lengthTrackedInput.Connect(LineEdit.SignalName.TextChanged, Callable.From<string>(_ => UpdateCounter()));
            UpdateCounter();
        }

        input.Connect(LineEdit.SignalName.FocusExited, Callable.From(PersistSettings));
        row.AddChild(input);
        return row;
    }

    private Control BuildLabeledOptionRow(string labelText, out OptionButton option, params (string Label, int Id)[] items)
    {
        VBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 6);

        Label label = CreateBodyLabel(labelText);
        label.AddThemeColorOverride("font_color", TextStrongColor);
        row.AddChild(label);

        option = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FitToLongestItem = false
        };

        foreach ((string itemLabel, int itemId) in items)
        {
            option.AddItem(UiText(itemLabel), itemId);
        }

        ApplyInputStyle(option);
        row.AddChild(option);
        return row;
    }

    private Control BuildMaxPlayersRow()
    {
        VBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 6);

        Label label = CreateBodyLabel("最大人数");
        label.AddThemeColorOverride("font_color", TextStrongColor);
        row.AddChild(label);

        _maxPlayersSpinBox = new SpinBox
        {
            MinValue = LanConnectConstants.MinMaxPlayers,
            MaxValue = 8,
            Value = 8,
            Step = 1,
            AllowGreater = true,
            AllowLesser = false,
            Rounded = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 48f),
            Suffix = "人"
        };
        LineEdit innerLineEdit = _maxPlayersSpinBox.GetLineEdit();
        innerLineEdit.AddThemeColorOverride("font_color", TextStrongColor);
        innerLineEdit.AddThemeFontSizeOverride("font_size", 16);
        innerLineEdit.SelectAllOnFocus = true;
        _maxPlayersSpinBox.AddThemeStyleboxOverride("up_down", new StyleBoxEmpty());
        _maxPlayersSpinBox.AddThemeColorOverride("up_down", AccentColor);
        StyleBoxFlat spinNormal = CreatePixelStyle(InputBgColor, BorderColor, borderWidth: 2, padding: 12, shadowSize: 0);
        StyleBoxFlat spinFocus = CreatePixelStyle(CardColor, AccentColor, borderWidth: 2, padding: 12, shadowSize: 0);
        innerLineEdit.AddThemeStyleboxOverride("normal", spinNormal);
        innerLineEdit.AddThemeStyleboxOverride("focus", spinFocus);
        row.AddChild(_maxPlayersSpinBox);

        _maxPlayersHintLabel = CreateBodyLabel(string.Empty);
        _maxPlayersHintLabel.AddThemeColorOverride("font_color", TextMutedColor);
        row.AddChild(_maxPlayersHintLabel);

        _maxPlayersSpinBox.Connect(Godot.Range.SignalName.ValueChanged, Callable.From<double>(_ => UpdateMaxPlayersHint()));
        UpdateMaxPlayersHint();

        return row;
    }

    private async Task RefreshRoomsAsync(bool userInitiated = false)
    {
        if (_refreshInFlight || _actionInFlight)
        {
            return;
        }

        PersistSettings();
        _refreshInFlight = true;
        GD.Print("sts2_lan_connect overlay: refresh started");
        _timeUntilAutoRefresh = LanConnectConstants.LobbyRefreshIntervalSeconds;
        if (userInitiated || _rooms.Count == 0)
        {
            SetStatus("正在刷新大厅列表...");
        }

        UpdateActionButtons();

        try
        {
            List<LobbyRoomSummary> previousRooms = new(_rooms);
            string? selectedRoomId = GetSelectedRoom()?.RoomId ?? _selectedRoomId;
            using LobbyApiClient apiClient = LobbyApiClient.CreateConfigured();
            Task<double?> probeTask = MeasureLobbyProbeRttSafeAsync(apiClient);
            Task<(bool Success, IReadOnlyList<LobbyAnnouncementItem> Items)> announcementTask = FetchAnnouncementsSafeAsync(apiClient);
            IReadOnlyList<LobbyRoomSummary> rooms = await apiClient.GetRoomsAsync();
            double? measuredProbeRtt = await probeTask;
            (bool announcementSuccess, IReadOnlyList<LobbyAnnouncementItem> announcementItems) = await announcementTask;
            _lastLobbyRttMs = measuredProbeRtt ?? -1d;
            _consecutiveRefreshFailures = 0;
            _rooms.Clear();
            _rooms.AddRange(rooms);
            if (announcementSuccess)
            {
                ApplyAnnouncements(announcementItems);
            }
            else
            {
                EnsureAnnouncementFallback();
            }

            if (!string.IsNullOrWhiteSpace(selectedRoomId) && _rooms.Exists(room => room.RoomId == selectedRoomId))
            {
                _selectedRoomId = selectedRoomId;
            }
            else
            {
                _selectedRoomId = _rooms.Count > 0 ? _rooms[0].RoomId : null;
            }

            bool roomStageChanged = userInitiated
                                    || !AreRoomListsVisuallyEquivalent(previousRooms, _rooms)
                                    || !string.Equals(selectedRoomId, _selectedRoomId, StringComparison.Ordinal);
            if (roomStageChanged)
            {
                RebuildRoomStage();
            }
            else
            {
                UpdateHealthIndicator();
                UpdatePageControls(GetFilteredRooms().Count);
            }

            GD.Print(
                $"sts2_lan_connect overlay: refresh completed with {_rooms.Count} rooms, probeRttMs={(measuredProbeRtt.HasValue ? $"{measuredProbeRtt.Value:0}" : "<unavailable>")}");
            SetStatus(string.Empty);
        }
        catch (LobbyServiceException ex)
        {
            _consecutiveRefreshFailures++;
            _lastLobbyRttMs = -1d;
            GD.Print($"sts2_lan_connect overlay: refresh failed with lobby error {ex.Code} - {ex.Message}");
            EnsureAnnouncementFallback();
            UpdateHealthIndicator();
            if (userInitiated || _rooms.Count == 0)
            {
                SetStatus($"大厅服务不可用：{ex.Message}");
            }
            else
            {
                SetStatus($"大厅刷新失败，已保留上次成功列表：{ex.Message}\n{RefreshFailureSwitchHint}");
            }
        }
        catch (Exception ex)
        {
            _consecutiveRefreshFailures++;
            _lastLobbyRttMs = -1d;
            GD.Print($"sts2_lan_connect overlay: refresh failed with exception {ex.Message}");
            EnsureAnnouncementFallback();
            UpdateHealthIndicator();
            if (userInitiated || _rooms.Count == 0)
            {
                SetStatus($"刷新大厅失败：{ex.Message}");
            }
            else
            {
                SetStatus($"大厅刷新失败，已保留上次成功列表：{ex.Message}\n{RefreshFailureSwitchHint}");
            }
        }
        finally
        {
            _refreshInFlight = false;
            GD.Print("sts2_lan_connect overlay: refresh finished");
            UpdateActionButtons();
        }
    }

    private async Task<(bool Success, IReadOnlyList<LobbyAnnouncementItem> Items)> FetchAnnouncementsSafeAsync(LobbyApiClient apiClient)
    {
        try
        {
            IReadOnlyList<LobbyAnnouncementItem> items = await apiClient.GetAnnouncementsAsync();
            return (true, items);
        }
        catch (Exception ex)
        {
            GD.Print($"sts2_lan_connect overlay: announcement request failed with exception {ex.Message}");
            return (false, Array.Empty<LobbyAnnouncementItem>());
        }
    }

    private void ApplyAnnouncements(IReadOnlyList<LobbyAnnouncementItem> items)
    {
        _announcements.Clear();
        foreach (LobbyAnnouncementItem item in items)
        {
            if (!item.Enabled)
            {
                continue;
            }

            _announcements.Add(item);
        }

        GD.Print($"sts2_lan_connect overlay: announcements applied count={_announcements.Count}");

        EnsureAnnouncementFallback();
        _announcementCarousel?.SetAnnouncements(_announcements);
    }

    private void EnsureAnnouncementFallback()
    {
        if (_announcements.Count == 0)
        {
            _announcements.Clear();
            _announcements.Add(new LobbyAnnouncementItem
            {
                Id = "default-info",
                Type = "info",
                Title = "暂无公告",
                DateLabel = string.Empty,
                Body = HasAvailableLobbyEndpoint()
                    ? "浏览房间列表，或稍后刷新查看最新公告。"
                    : "当前客户端尚未绑定大厅服务。请在设置中填写 HTTP 覆盖地址。",
                Enabled = true,
            });
        }

        _announcementCarousel?.SetAnnouncements(_announcements);
    }

    private void RebuildRoomStage()
    {
        if (_roomListContainer == null || _roomListSummaryLabel == null || _roomHintLabel == null || _pageSummaryLabel == null)
        {
            return;
        }

        foreach (Node child in _roomListContainer.GetChildren())
        {
            child.QueueFree();
        }

        UpdateHealthIndicator();
        List<LobbyRoomSummary> filteredRooms = GetFilteredRooms();
        ClampCurrentPage(filteredRooms.Count);

        if (filteredRooms.Count == 0)
        {
            _selectedRoomId = null;
            bool hasSearchOrFilter = HasRoomSearchOrFilter();
            SetLabelText(
                _roomListSummaryLabel,
                _rooms.Count == 0
                    ? "大厅当前没有房间。"
                    : $"没有匹配结果 · 当前筛选：{DescribeRoomFilterState()}");
            SetLabelText(_pageSummaryLabel, "PAGE 0/0");
            if (_rooms.Count == 0)
            {
                _roomListContainer.AddChild(CreateEmptyRoomCard(
                    "大厅当前没有房间。",
                    "刷新大厅后，你也可以在右侧直接创建一个新的公开房间。"));
                SetLabelText(
                    _actionAvailabilityLabel,
                    HasAvailableLobbyEndpoint()
                        ? "你可以先刷新大厅，或者直接在右侧创建一个新的房间。"
                        : "当前客户端未绑定内置大厅服务。请在设置里填写 HTTP 覆盖地址。");
                _actionAvailabilityLabel?.AddThemeColorOverride("font_color", HasAvailableLobbyEndpoint() ? AccentColor : DangerColor);
            }
            else
            {
                _roomListContainer.AddChild(CreateEmptyRoomCard(
                    "没有匹配结果。",
                    hasSearchOrFilter
                        ? "尝试缩短关键词，或取消部分筛选后重试。"
                        : "可检索字段包括房间名、房主名、版本和状态。"));
                SetLabelText(
                    _actionAvailabilityLabel,
                    hasSearchOrFilter
                        ? "尝试缩短关键词，或取消部分筛选后重新查看完整房间列表。"
                        : "尝试刷新大厅后重新查看完整房间列表。");
                _actionAvailabilityLabel?.AddThemeColorOverride("font_color", AccentColor);
            }

            UpdatePageControls(filteredRooms.Count);
            return;
        }

        List<LobbyRoomSummary> pageRooms = GetVisibleRooms(filteredRooms);
        if (pageRooms.Count == 0)
        {
            _selectedRoomId = null;
            UpdatePageControls(filteredRooms.Count);
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedRoomId) || !pageRooms.Exists(room => room.RoomId == _selectedRoomId))
        {
            _selectedRoomId = pageRooms[0].RoomId;
        }

        LobbyRoomSummary selectedRoom = pageRooms.Find(room => room.RoomId == _selectedRoomId) ?? pageRooms[0];
        bool selectedIsHostRoom = LanConnectLobbyRuntime.Instance?.ActiveRoomId == selectedRoom.RoomId;

        SetLabelText(_roomListSummaryLabel, $"房间 {_rooms.Count} → {filteredRooms.Count} · 筛选：{DescribeRoomFilterState()} · 已选：{FormatRoomName(selectedRoom.RoomName, 24)}");
        UpdatePageControls(filteredRooms.Count);

        foreach (LobbyRoomSummary room in pageRooms)
        {
            bool isSelected = room.RoomId == _selectedRoomId;
            bool isHostRoom = LanConnectLobbyRuntime.Instance?.ActiveRoomId == room.RoomId;
            _roomListContainer.AddChild(CreateRoomCard(room, isSelected, isHostRoom));
        }

        // Pad with invisible spacers so the 5-slot grid is always full.
        // Each card already has ExpandFill + ratio 1; spacers match.
        for (int i = pageRooms.Count; i < LanConnectConstants.LobbyRoomsPerPage; i++)
        {
            Control spacer = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 1f
            };
            _roomListContainer.AddChild(spacer);
        }

        // Dynamic selection status goes to the action availability label (sidebar), not the bottom hint
        if (selectedIsHostRoom)
        {
            SetLabelText(_actionAvailabilityLabel, UiText("当前选中的是你自己托管的房间，无法重复加入。"));
            _actionAvailabilityLabel?.AddThemeColorOverride("font_color", SuccessColor);
        }
        else if (!CanJoinRoom(selectedRoom, out string? joinDisabledReason))
        {
            SetLabelText(_actionAvailabilityLabel, UiText(joinDisabledReason ?? "该房间当前不可加入。"));
            _actionAvailabilityLabel?.AddThemeColorOverride("font_color", DangerColor);
        }
        else if (selectedRoom.SavedRun != null)
        {
            int availableSlots = GetAvailableSavedRunSlots(selectedRoom).Count;
            SetLabelText(
                _actionAvailabilityLabel,
                UiText(availableSlots <= 0
                    ? "该续局房间当前没有可接管角色。"
                    : $"续局房间，可接管 {availableSlots} 个角色槽位。"));
            _actionAvailabilityLabel?.AddThemeColorOverride("font_color", availableSlots <= 0 ? DangerColor : AccentColor);
        }
        else if (selectedRoom.RequiresPassword)
        {
            SetLabelText(_actionAvailabilityLabel, UiText("当前选中房间需要密码。"));
            _actionAvailabilityLabel?.AddThemeColorOverride("font_color", AccentColor);
        }
        else
        {
            SetLabelText(_actionAvailabilityLabel, UiText("当前选中房间为公开房间，可直接加入。"));
            _actionAvailabilityLabel?.AddThemeColorOverride("font_color", SuccessColor);
        }
    }

    private void ConfigureRoomListScroll(ScrollContainer scroll)
    {
        scroll.FollowFocus = false;
        scroll.MouseFilter = MouseFilterEnum.Stop;
        VScrollBar scrollbar = scroll.GetVScrollBar();
        scrollbar.CustomMinimumSize = new Vector2(20f, 0f);
        scrollbar.AddThemeStyleboxOverride("scroll", CreatePixelStyle(SurfaceMutedColor, BorderColor, borderWidth: 1, padding: 4, shadowSize: 0));
        scrollbar.AddThemeStyleboxOverride("grabber", CreatePixelStyle(AccentColor, BorderColor, borderWidth: 0, padding: 8, shadowSize: 0));
        scrollbar.AddThemeStyleboxOverride("grabber_highlight", CreatePanelStyle(new Color(0.64f, 0.24f, 0.13f, 0.94f), new Color(AccentBrightColor, 0.34f), radius: 999, borderWidth: 0, padding: 8));
        scrollbar.AddThemeStyleboxOverride("grabber_pressed", CreatePanelStyle(new Color(0.72f, 0.28f, 0.15f, 0.96f), new Color(AccentBrightColor, 0.38f), radius: 999, borderWidth: 0, padding: 8));
    }

    private bool HandleRoomListPointerInput(InputEvent inputEvent, LobbyRoomSummary? room)
    {
        if (inputEvent is InputEventMouseButton mouseButton &&
            mouseButton.Pressed &&
            (mouseButton.ButtonIndex == MouseButton.WheelUp || mouseButton.ButtonIndex == MouseButton.WheelDown))
        {
            AdjustRoomScroll(mouseButton.ButtonIndex == MouseButton.WheelUp ? -RoomListWheelStep : RoomListWheelStep);
            return true;
        }

        if (inputEvent is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                _roomListTouchActive = true;
                _roomListTouchDragging = false;
                _roomListTouchStartPosition = touch.Position;
                _roomListTouchStartScroll = _roomListScroll?.ScrollVertical ?? 0;
                _roomListTouchTapRoomId = room?.RoomId;
                return true;
            }

            bool shouldSelectTappedRoom = _roomListTouchActive &&
                                          !_roomListTouchDragging &&
                                          room != null &&
                                          !string.IsNullOrWhiteSpace(_roomListTouchTapRoomId) &&
                                          string.Equals(_roomListTouchTapRoomId, room.RoomId, StringComparison.Ordinal);
            ResetRoomListTouchTracking();
            if (shouldSelectTappedRoom)
            {
                SelectRoom(room!);
            }

            return true;
        }

        if (inputEvent is InputEventScreenDrag screenDrag && _roomListTouchActive)
        {
            float dragDistance = screenDrag.Position.DistanceTo(_roomListTouchStartPosition);
            if (!_roomListTouchDragging && dragDistance >= RoomListTouchDragThreshold)
            {
                _roomListTouchDragging = true;
            }

            if (_roomListTouchDragging)
            {
                SetRoomScroll(_roomListTouchStartScroll - (screenDrag.Position.Y - _roomListTouchStartPosition.Y));
            }

            return true;
        }

        return false;
    }

    private void AdjustRoomScroll(float delta)
    {
        if (_roomListScroll == null)
        {
            return;
        }

        SetRoomScroll(_roomListScroll.ScrollVertical + delta);
    }

    private void SetRoomScroll(float value)
    {
        if (_roomListScroll == null)
        {
            return;
        }

        VScrollBar scrollbar = _roomListScroll.GetVScrollBar();
        float maxScroll = Mathf.Max((float)scrollbar.MaxValue - (float)scrollbar.Page, 0f);
        _roomListScroll.ScrollVertical = Mathf.RoundToInt(Mathf.Clamp(value, 0f, maxScroll));
    }

    private void ResetRoomListScroll()
    {
        if (_roomListScroll == null)
        {
            return;
        }

        _roomListScroll.ScrollVertical = 0;
    }

    private void ResetRoomListTouchTracking()
    {
        _roomListTouchActive = false;
        _roomListTouchDragging = false;
        _roomListTouchTapRoomId = null;
        _roomListTouchStartPosition = Vector2.Zero;
        _roomListTouchStartScroll = 0f;
    }

    private Control CreateEmptyRoomCard(string titleText, string descriptionText)
    {
        PanelContainer card = CreateSurfacePanel(SurfaceMutedColor, BorderColor, borderWidth: 2, padding: 22);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.CustomMinimumSize = new Vector2(0f, 120f);

        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 12);
        card.AddChild(body);

        Label title = CreateTitleLabel(titleText, 28);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(title);

        Label description = CreateBodyLabel(descriptionText);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(description);
        ApplyPassiveMouseFilterRecursive(card);
        card.MouseFilter = MouseFilterEnum.Ignore;
        return card;
    }

    private (string Text, Color Border, Color Background) GetRoomPrimaryPill(LobbyRoomSummary room, bool isHostRoom)
    {
        if (isHostRoom)
        {
            return ("你的房间", SuccessColor, new Color(SuccessColor, 0.15f));
        }

        if (string.Equals(room.Status, "starting", StringComparison.OrdinalIgnoreCase))
        {
            return ("已开局", DangerColor, new Color(DangerColor, 0.12f));
        }

        if (string.Equals(room.Status, "full", StringComparison.OrdinalIgnoreCase))
        {
            return ("已满", DangerColor, new Color(DangerColor, 0.12f));
        }

        if (room.SavedRun != null)
        {
            int availableSlots = GetAvailableSavedRunSlots(room).Count;
            return availableSlots > 0
                ? ($"{availableSlots} 可接管", AccentColor, new Color(AccentColor, 0.15f))
                : ("续局已满", DangerColor, new Color(DangerColor, 0.12f));
        }

        if (room.RequiresPassword)
        {
            return ("已上锁", BorderColor, SecondaryColor);
        }

        return ("可加入", SuccessColor, new Color(SuccessColor, 0.15f));
    }

    private static Color GetRoomLockColor(LobbyRoomSummary room)
    {
        return room.RequiresPassword ? DangerColor : SuccessColor;
    }

    private static (string Text, Color Border, Color Background) GetRoomGameModePill(string? gameMode)
    {
        return gameMode?.Trim().ToLowerInvariant() switch
        {
            "daily" => ("挑战", BorderColor, new Color(0.95f, 0.90f, 0.80f, 1f)),
            "custom" => ("自定义", BorderColor, new Color(0.88f, 0.94f, 0.92f, 1f)),
            _ => ("STD", BorderColor, SecondaryColor)
        };
    }

    private string? BuildRoomDetailLine(LobbyRoomSummary room, bool isHostRoom)
    {
        if (isHostRoom)
        {
            return "当前托管中的房间。";
        }

        if (string.Equals(room.Status, "starting", StringComparison.OrdinalIgnoreCase))
        {
            return "房间已经开始游戏，当前不可加入。";
        }

        if (string.Equals(room.Status, "full", StringComparison.OrdinalIgnoreCase))
        {
            return "房间已经满员，请等待空位。";
        }

        if (room.SavedRun != null)
        {
            int availableSlots = GetAvailableSavedRunSlots(room).Count;
            return availableSlots > 0
                ? $"续局房间，可接管 {availableSlots} 个角色槽位。"
                : "续局房间当前没有可接管角色。";
        }

        if (room.RequiresPassword)
        {
            return "输入密码后即可加入。";
        }

        if (string.Equals(room.RelayState, "planned", StringComparison.OrdinalIgnoreCase))
        {
            return "relay 等待房主注册。";
        }

        return null;
    }

    private Color GetRoomDetailColor(LobbyRoomSummary room, bool isHostRoom)
    {
        if (isHostRoom)
        {
            return SuccessColor;
        }

        if (string.Equals(room.Status, "starting", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(room.Status, "full", StringComparison.OrdinalIgnoreCase))
        {
            return DangerColor;
        }

        if (room.SavedRun != null || room.RequiresPassword || string.Equals(room.RelayState, "planned", StringComparison.OrdinalIgnoreCase))
        {
            return AccentColor;
        }

        return TextMutedColor;
    }

    private static Label CreateMetaLabel(string text)
    {
        Label label = CreateBodyLabel(text);
        label.AddThemeColorOverride("font_color", TextMutedColor);
        label.AddThemeFontSizeOverride("font_size", 14);
        return label;
    }

    private static Label CreateMetaSeparator()
    {
        Label label = CreateBodyLabel("·");
        label.AddThemeColorOverride("font_color", new Color(TextMutedColor, 0.72f));
        label.AddThemeFontSizeOverride("font_size", 15);
        return label;
    }

    private Control BuildRoomMetaRow(LobbyRoomSummary room)
    {
        HFlowContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("h_separation", 12);
        row.AddThemeConstantOverride("v_separation", 4);

        row.AddChild(CreateMetaLabel($"HOST: {room.HostPlayerName}"));
        row.AddChild(CreateMetaLabel($"GAME: {room.Version}"));
        row.AddChild(CreateMetaLabel($"MOD: {room.ModVersion}"));

        HBoxContainer playerGroup = new()
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        playerGroup.AddThemeConstantOverride("separation", 4);
        row.AddChild(playerGroup);

        GlyphIcon peopleIcon = new()
        {
            Kind = GlyphIconKind.Users,
            GlyphColor = TextMutedColor,
            CustomMinimumSize = new Vector2(16f, 16f)
        };
        playerGroup.AddChild(peopleIcon);
        playerGroup.AddChild(CreateMetaLabel($"{room.CurrentPlayers}/{room.MaxPlayers}"));
        return row;
    }

    private Control CreateRoomCard(LobbyRoomSummary room, bool isSelected, bool isHostRoom)
    {
        string? detailText = BuildRoomDetailLine(room, isHostRoom);
        // Reference: bg-primary/10 — blend AccentColor at 10% over CardColor
        Color background = isSelected
            ? new Color(
                CardColor.R * 0.9f + AccentColor.R * 0.1f,
                CardColor.G * 0.9f + AccentColor.G * 0.1f,
                CardColor.B * 0.9f + AccentColor.B * 0.1f, 1f)
            : CardColor;
        Color border = isHostRoom
            ? SuccessColor
            : BorderColor;

        // Selected card: thicker border + much larger shadow for emphasis
        int cardBorder = isSelected ? 3 : 2;
        PanelContainer card = new() { ClipContents = false };
        if (isSelected)
        {
            // Selected card shadow — same depth as normal buttons (3px)
            card.AddThemeStyleboxOverride("panel", CreatePixelStyle(background, border, borderWidth: cardBorder, padding: 16, shadowSize: 4));
        }
        else
        {
            card.AddThemeStyleboxOverride("panel", CreatePixelStyle(background, border, borderWidth: cardBorder, padding: 16, shadowSize: 2));
        }
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.SizeFlagsVertical = SizeFlags.ExpandFill;
        card.SizeFlagsStretchRatio = 1f;
        card.MouseFilter = MouseFilterEnum.Stop;
        card.MouseDefaultCursorShape = CursorShape.PointingHand;

        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        body.AddThemeConstantOverride("separation", 6);
        card.AddChild(body);

        // ── Row 1: [name + tags](left) ... [PASS REQ](right) ──
        //
        // Godot HBox gotcha: Label with ClipText=true has minWidth=0,
        // so SizeFlags.Fill collapses it.  Label with ExpandFill pushes
        // everything to the far right.
        //
        // Solution: NO ClipText. Truncate the name string instead.
        // Title uses SizeFlags.Fill → natural text width.
        // leftGroup uses ExpandFill so extra space is absorbed there,
        // keeping PASS REQ at the far right of topRow.
        HBoxContainer topRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        topRow.AddThemeConstantOverride("separation", 10);
        body.AddChild(topRow);

        // Left group: name + tags flow together (expand to absorb extra space)
        HBoxContainer leftGroup = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        leftGroup.AddThemeConstantOverride("separation", 8);
        topRow.AddChild(leftGroup);

        // Title: natural width, no ClipText, truncated at 24 chars
        Label title = CreateTitleLabel(FormatRoomName(room.RoomName, 24), 20);
        title.SizeFlagsHorizontal = SizeFlags.Fill;
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        leftGroup.AddChild(title);

        if (isSelected)
        {
            leftGroup.AddChild(CreateTagPill("SELECT", AccentColor, AccentColor, true));
        }

        if (room.RequiresPassword)
        {
            leftGroup.AddChild(CreateTagPillWithIcon("LOCKED", BorderColor, SecondaryColor));
        }

        (string modeText, Color modeBorder, Color modeBackground) = GetRoomGameModePill(room.GameMode);
        leftGroup.AddChild(CreateTagPill(modeText, modeBorder, modeBackground, false));

        // PASS REQ: right-aligned, separate from tags
        if (room.RequiresPassword)
        {
            Label passReq = CreateBodyLabel("PASS REQ");
            passReq.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            passReq.AddThemeColorOverride("font_color", AccentColor);
            passReq.AddThemeFontSizeOverride("font_size", 13);
            topRow.AddChild(passReq);
        }

        // ── Row 2: META ──
        body.AddChild(BuildRoomMetaRow(room));

        // ── Row 3: optional detail ──
        if (!string.IsNullOrWhiteSpace(detailText))
        {
            Label detailLine = CreateBodyLabel(detailText);
            detailLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            detailLine.AddThemeFontSizeOverride("font_size", 13);
            detailLine.AddThemeColorOverride("font_color", GetRoomDetailColor(room, isHostRoom));
            body.AddChild(detailLine);
        }

        ApplyPassiveMouseFilterRecursive(card);
        card.MouseFilter = MouseFilterEnum.Stop;
        card.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(inputEvent => OnRoomCardGuiInput(room, inputEvent)));
        return card;
    }

    private Control CreateTagPill(string text, Color border, Color background, bool isPrimary)
    {
        PanelContainer pill = CreateSurfacePanel(background, border, borderWidth: 2, padding: 7);

        Label label = CreateBodyLabel(text);
        label.AddThemeColorOverride("font_color", isPrimary ? PrimaryFgColor : TextStrongColor);
        label.AddThemeFontSizeOverride("font_size", 13);
        pill.AddChild(label);
        return pill;
    }

    /// <summary>Tag pill with a lock icon (for LOCKED badge).</summary>
    private Control CreateTagPillWithIcon(string text, Color border, Color background)
    {
        PanelContainer pill = CreateSurfacePanel(background, border, borderWidth: 2, padding: 7);

        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 4);
        pill.AddChild(row);

        row.AddChild(new GlyphIcon
        {
            Kind = GlyphIconKind.Lock,
            GlyphColor = TextStrongColor,
            CustomMinimumSize = new Vector2(12f, 12f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        });

        Label label = CreateBodyLabel(text);
        label.AddThemeColorOverride("font_color", TextStrongColor);
        label.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(label);
        return pill;
    }

    /// <summary>Legacy 3-arg overload for compatibility.</summary>
    private Control CreateTagPill(string text, Color border, Color background)
    {
        return CreateTagPill(text, border, background, false);
    }

    private void OnRoomCardGuiInput(LobbyRoomSummary room, InputEvent inputEvent)
    {
        if (HandleRoomListPointerInput(inputEvent, room))
        {
            return;
        }

        if (inputEvent is not InputEventMouseButton mouseButton || !mouseButton.Pressed || mouseButton.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        SelectRoom(room);
        if (mouseButton.DoubleClick)
        {
            GD.Print($"sts2_lan_connect overlay: room card double clicked -> roomId={room.RoomId}");
            OnJoinRoomPressed(room);
        }
    }

    private void OnRoomListGuiInput(InputEvent inputEvent)
    {
        HandleRoomListPointerInput(inputEvent, null);
    }

    private void SelectRoom(LobbyRoomSummary room)
    {
        if (string.IsNullOrWhiteSpace(room.RoomId))
        {
            return;
        }

        if (_selectedRoomId == room.RoomId)
        {
            return;
        }

        _selectedRoomId = room.RoomId;
        GD.Print($"sts2_lan_connect overlay: selected room roomId={room.RoomId}, roomName='{room.RoomName}'");
        RebuildRoomStage();
        UpdateActionButtons();
    }

    private async Task CreateRoomAsync()
    {
        if (_roomNameInput == null || _createDialogErrorLabel == null || _loadingOverlay == null || _stack == null)
        {
            return;
        }

        if (_actionInFlight)
        {
            return;
        }

        PersistSettings();
        string roomName = _roomNameInput.Text.Trim();
        if (_roomNameInput.Text != roomName)
        {
            _roomNameInput.Text = roomName;
        }
        GameMode gameMode = GetSelectedCreateGameMode();
        string? password = string.IsNullOrWhiteSpace(_roomPasswordInput?.Text) ? null : LanConnectConfig.SanitizeRoomPassword(_roomPasswordInput.Text);
        if (_roomPasswordInput != null && (_roomPasswordInput.Text?.Trim() ?? string.Empty) != (password ?? string.Empty))
        {
            _roomPasswordInput.Text = password ?? string.Empty;
        }
        string gameModeLabel = LanConnectMultiplayerSaveRoomBinding.GetLobbyGameModeLabel(gameMode);
        GD.Print(
            $"sts2_lan_connect overlay: create requested roomName='{roomName}', passwordSet={!string.IsNullOrWhiteSpace(password)}, gameMode={LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode)}, hasRunSave={SaveManager.Instance.HasMultiplayerRunSave}, hasActiveRoom={LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true}, endpointAvailable={HasAvailableLobbyEndpoint()}, matrix={LanConnectCompatibilityMatrix.DescribeCurrentPolicy()}");
        if (string.IsNullOrWhiteSpace(roomName))
        {
            ShowCreateDialogError("请输入房间名。");
            return;
        }

        if (SaveManager.Instance.HasMultiplayerRunSave)
        {
            GD.Print("sts2_lan_connect overlay: create blocked by multiplayer continue save.");
            ShowCreateDialogError("检测到多人续局存档。请先点击官方载入进入该存档，mod 会自动恢复绑定的大厅房间。");
            return;
        }

        if (LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true)
        {
            GD.Print("sts2_lan_connect overlay: create blocked because hosted room is already active.");
            ShowCreateDialogError("你已经有一个大厅房间在托管中，先关闭它再重新建房。");
            return;
        }

        int? selectedMaxPlayers = null;
        if (_maxPlayersSpinBox != null)
        {
            int parsedMaxPlayers = (int)_maxPlayersSpinBox.Value;
            if (parsedMaxPlayers > 8)
            {
                ShowCreateDialogError("房间最大人数不能超过 8 人。");
                return;
            }
            selectedMaxPlayers = parsedMaxPlayers;
        }

        _actionInFlight = true;
        UpdateActionButtons();
        ShowCreateDialogError(string.Empty, visible: false);
        SetStatus($"正在创建{gameModeLabel}房间 {roomName}...");

        try
        {
            bool created = await LanConnectHostFlow.StartLobbyHostAsync(roomName, password, gameMode, _loadingOverlay, _stack, selectedMaxPlayers);
            if (created)
            {
                CloseCreateDialog();
                HideOverlay();
            }
        }
        catch (LobbyServiceException ex) when (string.Equals(ex.Code, "server_bandwidth_near_capacity", StringComparison.Ordinal))
        {
            CloseCreateDialog();
            ShowCreateRoomGuardDialog(
                "当前服务器接近带宽上限",
                "为保证现有连接稳定，当前服务器暂不允许创建新房间。",
                BuildCreateRoomGuardDetail(
                    ex.Details?.CurrentBandwidthMbps,
                    ex.Details?.ResolvedCapacityMbps ?? ex.Details?.BandwidthCapacityMbps,
                    ex.Details?.BandwidthUtilizationRatio,
                    ex.Details?.CapacitySource),
                allowContinue: false);
            SetStatus("当前服务器接近带宽上限，已阻止新建房间。");
        }
        finally
        {
            _actionInFlight = false;
            UpdateActionButtons();
        }
    }

    private async Task<bool> JoinRoomAsync(LobbyRoomSummary room, string? password, string? desiredSavePlayerNetId = null)
    {
        if (_actionInFlight || _stack == null || _loadingOverlay == null)
        {
            return false;
        }

        password = string.IsNullOrWhiteSpace(password) ? null : LanConnectConfig.SanitizeRoomPassword(password);

        PersistSettings();
        GD.Print($"sts2_lan_connect overlay: join requested roomId={room.RoomId}, roomName='{room.RoomName}', requiresPassword={room.RequiresPassword}, desiredSavePlayerNetId={(string.IsNullOrWhiteSpace(desiredSavePlayerNetId) ? "<none>" : desiredSavePlayerNetId)}, roomCompatibility={LanConnectCompatibilityMatrix.DescribeRoomCompatibility(room)}, matrix={LanConnectCompatibilityMatrix.DescribeCurrentPolicy()}");
        _actionInFlight = true;
        UpdateActionButtons();
        SetStatus($"正在请求加入 {FormatRoomName(room.RoomName, 24)}...");
        CancellationTokenSource joinCancellationSource = new();
        ReplaceJoinCancellationSource(joinCancellationSource);
        ShowProgressDialog(
            "正在加入房间",
            $"正在向大厅申请进入 {FormatRoomName(room.RoomName, 24)}",
            "连接较慢时请稍候，期间不要重复点击按钮。",
            allowCancel: true);

        try
        {
            using LobbyApiClient apiClient = LobbyApiClient.CreateConfigured();
            LobbyJoinRoomResponse joinResponse = await apiClient.JoinRoomAsync(room.RoomId, new LobbyJoinRoomRequest
            {
                PlayerName = LanConnectConfig.GetEffectivePlayerDisplayName(),
                Password = string.IsNullOrWhiteSpace(password) ? null : password,
                Version = LanConnectBuildInfo.GetGameVersion(),
                ModVersion = LanConnectBuildInfo.GetModVersion(),
                ModList = LanConnectBuildInfo.GetModList(),
                DesiredSavePlayerNetId = string.IsNullOrWhiteSpace(desiredSavePlayerNetId) ? null : desiredSavePlayerNetId
            }, joinCancellationSource.Token);

            UpdateProgressDialog(
                "正在建立联机连接",
                $"大厅已响应，正在连接 {FormatRoomName(room.RoomName, 24)}",
                "如果房主在外网环境，首次握手通常会比刷新大厅更慢。",
                allowCancel: true);

            LobbyJoinAttemptResult joinResult = await LanConnectLobbyJoinFlow.JoinAsync(
                _stack,
                _loadingOverlay,
                joinResponse,
                desiredSavePlayerNetId,
                joinCancellationSource.Token,
                message => UpdateProgressDialog("正在建立联机连接", message, allowCancel: true));
            if (joinResult.Joined)
            {
                UpdateProgressDialog("正在进入房间", $"已连接 {FormatRoomName(room.RoomName, 24)}，正在切换到联机界面", allowCancel: false);
                SetStatus($"已加入 {FormatRoomName(room.RoomName, 24)}。");
                HideOverlay();
                return true;
            }

            if (joinResult.Canceled)
            {
                LanConnectProtocolProfiles.ResetActiveProfile("join_canceled_overlay");
                SetStatus($"已取消加入 {FormatRoomName(room.RoomName, 24)}。");
                return false;
            }

            string failureMessage = string.IsNullOrWhiteSpace(joinResult.FailureMessage)
                ? "请查看错误弹窗或连接日志。"
                : joinResult.FailureMessage;
            LanConnectProtocolProfiles.ResetActiveProfile("join_failed_overlay");
            SetStatus($"加入 {FormatRoomName(room.RoomName, 24)} 失败：{failureMessage}");
            return false;
        }
        catch (OperationCanceledException) when (joinCancellationSource.IsCancellationRequested)
        {
            LanConnectProtocolProfiles.ResetActiveProfile("join_canceled_request");
            SetStatus($"已取消加入 {FormatRoomName(room.RoomName, 24)}。");
            return false;
        }
        catch (LobbyServiceException ex)
        {
            LanConnectProtocolProfiles.ResetActiveProfile("join_service_exception");
            string message = DescribeJoinFailure(ex);
            if (_resumeSlotDialogContainer != null && _resumeSlotDialogContainer.Visible)
            {
                ShowResumeSlotError(message);
            }
            else if (_joinPasswordDialogContainer != null && _joinPasswordDialogContainer.Visible)
            {
                ShowJoinPasswordError(message);
            }
            else
            {
                SetStatus($"加入房间失败：{message}");
            }

            return false;
        }
        catch (Exception ex)
        {
            LanConnectProtocolProfiles.ResetActiveProfile("join_exception");
            SetStatus($"加入房间失败：{DescribeGenericJoinFailure(ex)}");
            return false;
        }
        finally
        {
            ClearJoinCancellationSource(joinCancellationSource);
            HideProgressDialog();
            _actionInFlight = false;
            UpdateActionButtons();
        }
    }

    private async Task BeginJoinRoomAsync(LobbyRoomSummary room, string? password)
    {
        List<LobbySavedRunSlot> availableSlots = GetAvailableSavedRunSlots(room);
        if (room.SavedRun != null && room.SavedRun.Slots.Count > 0)
        {
            if (availableSlots.Count == 0)
            {
                ReportJoinIssue("该续局房间当前没有可接管角色。");
                return;
            }

            if (availableSlots.Count > 1)
            {
                OpenResumeSlotDialog(room, password, availableSlots);
                return;
            }

            bool joinedSavedRun = await JoinRoomAsync(room, password, availableSlots[0].NetId);
            if (joinedSavedRun)
            {
                CloseJoinPasswordDialog();
            }

            return;
        }

        bool joined = await JoinRoomAsync(room, password);
        if (joined)
        {
            CloseJoinPasswordDialog();
        }
    }

    private async Task CloseMyRoomAsync()
    {
        if (_actionInFlight || LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom != true)
        {
            return;
        }

        _actionInFlight = true;
        UpdateActionButtons();
        SetStatus("正在关闭当前托管房间...");

        try
        {
            await LanConnectLobbyRuntime.Instance.CloseActiveHostedRoomAsync();
            await RefreshRoomsAsync();
            SetStatus("当前托管房间已关闭。");
        }
        catch (Exception ex)
        {
            SetStatus($"关闭房间失败：{ex.Message}");
        }
        finally
        {
            _actionInFlight = false;
            UpdateActionButtons();
        }
    }

    private async Task RepairMultiplayerSaveAsync()
    {
        if (_actionInFlight)
        {
            return;
        }

        _actionInFlight = true;
        UpdateActionButtons();
        SetStatus("正在执行多人存档强制修复...");
        ShowProgressDialog(
            "正在修复多人存档",
            "正在备份当前 modded 存档并执行 vanilla -> modded 同步",
            "修复过程中请不要重复点击按钮。");

        try
        {
            LanConnectSaveRepairResult result = await LanConnectMultiplayerSaveRepair.RepairCurrentProfileAsync();
            SetStatus(result.Success ? "多人存档修复完成。" : "多人存档修复完成，但仍需人工检查。");
            LanConnectPopupUtil.ShowInfo(result.Message);
        }
        catch (Exception ex)
        {
            SetStatus($"多人存档修复失败：{ex.Message}");
            LanConnectPopupUtil.ShowInfo($"多人存档修复失败：{ex.Message}");
        }
        finally
        {
            HideProgressDialog();
            _actionInFlight = false;
            UpdateActionButtons();
        }
    }

    private void OnJoinRoomPressed(LobbyRoomSummary room)
    {
        if (!CanJoinRoom(room, out string? reason))
        {
            ReportJoinIssue(reason ?? "该房间当前不可加入。");
            return;
        }

        if (room.RequiresPassword)
        {
            _pendingPasswordJoinRoom = room;
            SetLabelText(_joinPasswordDialogTitle, $"输入 {FormatRoomName(room.RoomName, 24)} 的房间密码");

            if (_joinPasswordInput != null)
            {
                _joinPasswordInput.Text = string.Empty;
                _joinPasswordInput.GrabFocus();
            }

            ShowJoinPasswordError(string.Empty, visible: false);
            if (_joinPasswordDialogContainer != null)
            {
                _joinPasswordDialogContainer.Visible = true;
            }

            return;
        }

        TaskHelper.RunSafely(BeginJoinRoomAsync(room, null));
    }

    private async Task SubmitJoinPasswordAsync()
    {
        if (_pendingPasswordJoinRoom == null || _joinPasswordInput == null)
        {
            return;
        }

        string password = LanConnectConfig.SanitizeRoomPassword(_joinPasswordInput.Text);
        if (_joinPasswordInput.Text != password)
        {
            _joinPasswordInput.Text = password;
        }
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowJoinPasswordError("请输入密码。");
            return;
        }

        await BeginJoinRoomAsync(_pendingPasswordJoinRoom, password);
    }

    private async Task BeginCreateRoomFlowAsync()
    {
        string? blockReason = GetCreateAvailabilityReasonForDialog();
        if (blockReason != null)
        {
            SetStatus($"当前无法打开建房：{blockReason}");
            LanConnectPopupUtil.ShowInfo($"无法创建房间\n\n{blockReason}");
            return;
        }

        if (_actionInFlight)
        {
            return;
        }

        _actionInFlight = true;
        UpdateActionButtons();
        SetStatus("正在检查当前服务器负载...");
        try
        {
            using LobbyApiClient apiClient = LobbyApiClient.CreateConfigured();
            LobbyHealthResponse health = await apiClient.GetHealthAsync();
            HandleCreateRoomGuardDecision(health);
        }
        catch (LobbyServiceException ex)
        {
            ShowCreateRoomGuardDialog(
                "无法确认服务器负载",
                $"建房前负载检查失败：{ex.Message}",
                "你可以切换到其他公共服务器，或继续打开建房窗口。",
                allowContinue: true);
            SetStatus($"建房前负载检查失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            ShowCreateRoomGuardDialog(
                "无法确认服务器负载",
                $"建房前负载检查失败：{ex.Message}",
                "你可以切换到其他公共服务器，或继续打开建房窗口。",
                allowContinue: true);
            SetStatus($"建房前负载检查失败：{ex.Message}");
        }
        finally
        {
            _actionInFlight = false;
            UpdateActionButtons();
        }
    }

    private void OpenCreateDialogInternal()
    {
        if (_createDialogContainer == null || _roomNameInput == null || _roomPasswordInput == null || _roomTypeOption == null)
        {
            return;
        }

        string? blockReason = GetCreateDisabledReason(
            actionBusy: false,
            SaveManager.Instance.HasMultiplayerRunSave,
            LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true,
            HasAvailableLobbyEndpoint());
        if (blockReason != null)
        {
            SetStatus($"当前无法打开建房：{blockReason}");
            return;
        }

        _roomNameInput.Text = string.IsNullOrWhiteSpace(LanConnectConfig.LastRoomName)
            ? GetSuggestedRoomName()
            : LanConnectConfig.LastRoomName;
        _roomTypeOption.Select(0);
        _roomPasswordInput.Text = string.Empty;
        if (_maxPlayersSpinBox != null)
        {
            int effectiveMax = Math.Clamp(LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers(), LanConnectConstants.MinMaxPlayers, 8);
            _maxPlayersSpinBox.Value = effectiveMax;
            UpdateMaxPlayersHint();
        }
        ShowCreateDialogError(string.Empty, visible: false);
        _createDialogContainer.Visible = true;
        _roomNameInput.GrabFocus();
    }

    private void HandleCreateRoomGuardDecision(LobbyHealthResponse health)
    {
        if (!health.CreateRoomGuardApplies)
        {
            OpenCreateDialogInternal();
            return;
        }

        if (string.Equals(health.CreateRoomGuardStatus, "block", StringComparison.OrdinalIgnoreCase))
        {
            ShowCreateRoomGuardDialog(
                "当前服务器接近带宽上限",
                "为保证现有连接稳定，当前服务器暂不允许创建新房间。",
                BuildCreateRoomGuardDetail(
                    health.CurrentBandwidthMbps,
                    health.ResolvedCapacityMbps ?? health.BandwidthCapacityMbps,
                    health.BandwidthUtilizationRatio,
                    health.CapacitySource),
                allowContinue: false);
            SetStatus("当前服务器接近带宽上限，建议切换服务器。");
            return;
        }

        if (string.Equals(health.CreateRoomGuardStatus, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            ShowCreateRoomGuardDialog(
                "当前服务器负载状态未知",
                "暂时无法确认这台服务器的可用带宽。你可以继续建房，也可以先切换到其他公共服务器。",
                BuildCreateRoomGuardDetail(
                    health.CurrentBandwidthMbps,
                    health.ResolvedCapacityMbps ?? health.BandwidthCapacityMbps,
                    health.BandwidthUtilizationRatio,
                    health.CapacitySource),
                allowContinue: true);
            SetStatus("当前服务器负载状态未知。");
            return;
        }

        OpenCreateDialogInternal();
        SetStatus("当前服务器负载正常，可以继续创建房间。");
    }

    private void JoinSelectedRoom()
    {
        LobbyRoomSummary? selectedRoom = GetSelectedRoom();
        if (selectedRoom == null)
        {
            SetStatus("当前大厅没有可加入的房间。");
            return;
        }

        OnJoinRoomPressed(selectedRoom);
    }

    private GameMode GetSelectedCreateGameMode()
    {
        return _roomTypeOption?.GetSelectedId() switch
        {
            1 => GameMode.Daily,
            2 => GameMode.Custom,
            _ => GameMode.Standard
        };
    }

    private void CloseCreateDialog()
    {
        if (_createDialogContainer != null)
        {
            _createDialogContainer.Visible = false;
        }
    }

    private void CloseCreateGuardDialog()
    {
        if (_createGuardDialogContainer != null)
        {
            _createGuardDialogContainer.Visible = false;
        }
    }

    private void ContinueCreateAfterGuardDecision()
    {
        CloseCreateGuardDialog();
        OpenCreateDialogInternal();
    }

    private async Task SwitchServerFromCreateGuardAsync()
    {
        CloseCreateGuardDialog();
        await OpenDirectoryServerDialogAsync();
    }

    private void ShowCreateRoomGuardDialog(string title, string message, string detail, bool allowContinue)
    {
        if (_createGuardDialogContainer == null)
        {
            return;
        }

        SetLabelText(_createGuardDialogTitle, title);
        SetLabelText(_createGuardDialogMessage, message);
        SetLabelText(_createGuardDialogDetail, detail);
        if (_createGuardContinueButton != null)
        {
            _createGuardContinueButton.Visible = allowContinue;
        }

        _createGuardDialogContainer.Visible = true;
        _createGuardDialogContainer.MoveToFront();
    }

    private void CloseJoinPasswordDialog()
    {
        if (_joinPasswordDialogContainer != null)
        {
            _joinPasswordDialogContainer.Visible = false;
        }

        _pendingPasswordJoinRoom = null;
    }

    private void OpenResumeSlotDialog(LobbyRoomSummary room, string? password, IReadOnlyList<LobbySavedRunSlot> availableSlots)
    {
        if (_resumeSlotDialogContainer == null || _resumeSlotDialogOptions == null)
        {
            ReportJoinIssue("无法打开续局角色选择窗口。");
            return;
        }

        _pendingResumeJoinRoom = room;
        _pendingResumeJoinPassword = password;
        SetLabelText(_resumeSlotDialogTitle, $"选择 {FormatRoomName(room.RoomName, 24)} 的可接管角色");

        foreach (Node child in _resumeSlotDialogOptions.GetChildren())
        {
            _resumeSlotDialogOptions.RemoveChild(child);
            child.QueueFree();
        }

        foreach (LobbySavedRunSlot slot in availableSlots)
        {
            string selectedNetId = slot.NetId;
            Button option = CreateActionButton(
                FormatSlotLabel(slot),
                string.IsNullOrWhiteSpace(slot.PlayerName)
                    ? $"接管该续局角色并使用已保存的玩家槽位加入。存档角色 ID：{slot.CharacterId}"
                    : $"接管 {slot.PlayerName} 的续局角色（{slot.CharacterId}）并使用已保存的玩家槽位加入。",
                () => TaskHelper.RunSafely(SubmitResumeSlotAsync(selectedNetId)),
                primary: _resumeSlotDialogOptions.GetChildCount() == 0);
            option.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _resumeSlotDialogOptions.AddChild(option);
        }

        ShowResumeSlotError(string.Empty, visible: false);
        CloseJoinPasswordDialog();
        _resumeSlotDialogContainer.Visible = true;
    }

    private async Task SubmitResumeSlotAsync(string desiredSavePlayerNetId)
    {
        if (_pendingResumeJoinRoom == null)
        {
            return;
        }

        bool joined = await JoinRoomAsync(_pendingResumeJoinRoom, _pendingResumeJoinPassword, desiredSavePlayerNetId);
        if (joined)
        {
            CloseResumeSlotDialog();
        }
    }

    private void CloseResumeSlotDialog()
    {
        if (_resumeSlotDialogContainer != null)
        {
            _resumeSlotDialogContainer.Visible = false;
        }

        _pendingResumeJoinRoom = null;
        _pendingResumeJoinPassword = null;
    }

    private static string FormatSlotLabel(LobbySavedRunSlot slot)
    {
        string characterLabel = string.IsNullOrWhiteSpace(slot.CharacterName)
            ? slot.CharacterId
            : slot.CharacterName;

        return string.IsNullOrWhiteSpace(slot.PlayerName)
            ? characterLabel
            : $"{characterLabel}（{slot.PlayerName}）";
    }

    private async Task OpenDirectoryServerDialogAsync()
    {
        if (_directoryServerDialogContainer == null)
        {
            return;
        }

        _directoryServerDialogContainer.Visible = true;
        _directoryServerDialogContainer.MoveToFront();

        if (_cachedDirectoryServers != null && _cachedDirectoryServers.Count > 0)
        {
            RebuildDirectoryServerCards(_cachedDirectoryServers);
            SetLabelText(_directoryServerDialogStatusLabel, $"{_cachedDirectoryServers.Count} 个可用大厅。点击即可切换。");
            _ = RefreshDirectoryServersInBackgroundAsync();
        }
        else
        {
            await RefreshDirectoryServersAsync();
        }
    }

    private void CloseDirectoryServerDialog()
    {
        if (_directoryServerDialogContainer != null)
        {
            _directoryServerDialogContainer.Visible = false;
        }
    }

    private void RebuildDirectoryServerCards(List<(LobbyDirectoryServerEntry Entry, double? RttMs)> servers)
    {
        if (_directoryServerDialogOptions == null)
        {
            return;
        }

        foreach (Node child in _directoryServerDialogOptions.GetChildren())
        {
            _directoryServerDialogOptions.RemoveChild(child);
            child.QueueFree();
        }

        foreach ((LobbyDirectoryServerEntry entry, double? rtt) in servers)
        {
            Control card = BuildDirectoryServerCard(entry, rtt);
            _directoryServerDialogOptions.AddChild(card);
        }
    }

    private async Task RefreshDirectoryServersInBackgroundAsync()
    {
        if (_directoryServerLoadInFlight)
        {
            return;
        }

        _directoryServerLoadInFlight = true;
        try
        {
            IReadOnlyList<LobbyDirectoryServerEntry> servers = await LanConnectLobbyDirectoryClient.GetServersAsync();
            if (servers.Count == 0)
            {
                return;
            }

            Task<double?>[] probeTasks = servers.Select(s => MeasureServerProbeRttSafeAsync(s.BaseUrl)).ToArray();
            await Task.WhenAll(probeTasks);

            _cachedDirectoryServers = new List<(LobbyDirectoryServerEntry, double?)>();
            for (int i = 0; i < servers.Count; i++)
            {
                _cachedDirectoryServers.Add((servers[i], probeTasks[i].Result));
            }

            if (_directoryServerDialogContainer is { Visible: true })
            {
                RebuildDirectoryServerCards(_cachedDirectoryServers);
                SetLabelText(_directoryServerDialogStatusLabel, $"{_cachedDirectoryServers.Count} 个可用大厅。点击即可切换。");
            }
        }
        catch (Exception ex)
        {
            GD.Print($"sts2_lan_connect overlay: background directory refresh failed: {ex.Message}");
        }
        finally
        {
            _directoryServerLoadInFlight = false;
        }
    }

    private async Task RefreshDirectoryServersAsync()
    {
        if (_directoryServerLoadInFlight || _directoryServerDialogOptions == null || _directoryServerDialogStatusLabel == null)
        {
            return;
        }

        _directoryServerLoadInFlight = true;
        SetLabelText(_directoryServerDialogStatusLabel, "正在刷新...");
        foreach (Node child in _directoryServerDialogOptions.GetChildren())
        {
            _directoryServerDialogOptions.RemoveChild(child);
            child.QueueFree();
        }

        try
        {
            IReadOnlyList<LobbyDirectoryServerEntry> servers = await LanConnectLobbyDirectoryClient.GetServersAsync();
            if (servers.Count == 0)
            {
                _cachedDirectoryServers = null;
                SetLabelText(_directoryServerDialogStatusLabel, "当前没有可用大厅。");
                return;
            }

            SetLabelText(_directoryServerDialogStatusLabel, $"已找到 {servers.Count} 个大厅，正在测速...");

            Task<double?>[] probeTasks = servers.Select(s => MeasureServerProbeRttSafeAsync(s.BaseUrl)).ToArray();
            await Task.WhenAll(probeTasks);

            _cachedDirectoryServers = new List<(LobbyDirectoryServerEntry, double?)>();
            for (int i = 0; i < servers.Count; i++)
            {
                _cachedDirectoryServers.Add((servers[i], probeTasks[i].Result));
            }

            RebuildDirectoryServerCards(_cachedDirectoryServers);
            SetLabelText(_directoryServerDialogStatusLabel, $"{servers.Count} 个可用大厅。点击即可切换。");
        }
        catch (Exception ex)
        {
            SetLabelText(_directoryServerDialogStatusLabel, $"拉取列表失败：{ex.Message}");
        }
        finally
        {
            _directoryServerLoadInFlight = false;
        }
    }

    private Control BuildDirectoryServerCard(LobbyDirectoryServerEntry entry, double? rttMs)
    {
        string name = string.IsNullOrWhiteSpace(entry.ServerName) ? entry.BaseUrl : entry.ServerName;
        bool blocked = entry.CreateRoomGuardStatus == "block";
        string guardText = FormatCreateRoomGuardStatus(entry.CreateRoomGuardStatus);
        string rttText = rttMs.HasValue ? $"{rttMs.Value:0}ms" : "超时";
        Color rttColor = rttMs.HasValue
            ? rttMs.Value <= 200d ? SuccessColor : rttMs.Value <= 600d ? AccentColor : DangerColor
            : DangerColor;

        string tooltipDetail =
            $"{entry.BaseUrl}\n" +
            $"最后验证：{entry.LastVerifiedAt:yyyy-MM-dd HH:mm:ss}\n" +
            $"利用率：{FormatUtilizationValue(entry.BandwidthUtilizationRatio)}\n" +
            $"带宽：{FormatBandwidthValue(entry.CurrentBandwidthMbps)} / {FormatBandwidthValue(entry.ResolvedCapacityMbps ?? entry.BandwidthCapacityMbps)}";

        PanelContainer card = CreateSurfacePanel(CardColor, BorderColor, borderWidth: 2, padding: 18);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.TooltipText = UiText(tooltipDetail);
        card.MouseFilter = MouseFilterEnum.Stop;
        card.MouseDefaultCursorShape = CursorShape.PointingHand;

        VBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 6);
        content.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(content);

        // Row 1: name + latency
        HBoxContainer topRow = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        topRow.AddThemeConstantOverride("separation", 10);
        topRow.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(topRow);

        Label nameLabel = CreateBodyLabel(name);
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameLabel.AddThemeFontSizeOverride("font_size", 20);
        nameLabel.AddThemeColorOverride("font_color", TextStrongColor);
        nameLabel.ClipText = true;
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        topRow.AddChild(nameLabel);

        Label rttLabel = CreateBodyLabel(rttText);
        rttLabel.HorizontalAlignment = HorizontalAlignment.Right;
        rttLabel.AddThemeFontSizeOverride("font_size", 20);
        rttLabel.AddThemeColorOverride("font_color", rttColor);
        rttLabel.MouseFilter = MouseFilterEnum.Ignore;
        topRow.AddChild(rttLabel);

        // Row 2: room count + create status
        HBoxContainer bottomRow = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bottomRow.AddThemeConstantOverride("separation", 10);
        bottomRow.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(bottomRow);

        Label infoLabel = CreateBodyLabel($"房间 {entry.Rooms}");
        infoLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        infoLabel.AddThemeFontSizeOverride("font_size", 16);
        infoLabel.AddThemeColorOverride("font_color", TextMutedColor);
        infoLabel.MouseFilter = MouseFilterEnum.Ignore;
        bottomRow.AddChild(infoLabel);

        Label guardLabel = CreateBodyLabel(guardText);
        guardLabel.HorizontalAlignment = HorizontalAlignment.Right;
        guardLabel.AddThemeFontSizeOverride("font_size", 16);
        guardLabel.AddThemeColorOverride("font_color", blocked ? DangerColor : SuccessColor);
        guardLabel.MouseFilter = MouseFilterEnum.Ignore;
        bottomRow.AddChild(guardLabel);

        LobbyDirectoryServerEntry capturedEntry = entry;
        card.Connect(Control.SignalName.GuiInput, Callable.From((InputEvent @event) =>
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                GD.Print($"sts2_lan_connect overlay: directory server card '{capturedEntry.ServerName}' clicked");
                ApplyDirectoryServer(capturedEntry);
            }
        }));

        return card;
    }

    private void ApplyDirectoryServer(LobbyDirectoryServerEntry entry)
    {
        if (_serverBaseUrlInput != null)
        {
            _serverBaseUrlInput.Text = entry.BaseUrl;
        }

        PersistSettings();
        UpdateActionButtons();
        SetStatus($"已切换到大厅服务：{entry.ServerName} ({entry.BaseUrl})");
        CloseDirectoryServerDialog();
        TaskHelper.RunSafely(RefreshRoomsAsync(userInitiated: true));
    }

    private void PersistSettings()
    {
        if (_displayNameInput != null)
        {
            string playerDisplayName = LanConnectConfig.SanitizePlayerDisplayName(_displayNameInput.Text);
            if (_displayNameInput.Text != playerDisplayName)
            {
                _displayNameInput.Text = playerDisplayName;
            }

            LanConnectConfig.PlayerDisplayName = playerDisplayName;
        }

        if (_serverBaseUrlInput != null)
        {
            LanConnectConfig.LobbyServerBaseUrl = _serverBaseUrlInput.Text.Trim();
        }

        if (_lobbyCreateRoomTokenInput != null)
        {
            LanConnectConfig.LobbyCreateRoomToken = _lobbyCreateRoomTokenInput.Text.Trim();
        }

        UpdateNetworkSummary();
    }

    private void SyncSettingsInputsFromConfig()
    {
        if (_displayNameInput != null)
        {
            _displayNameInput.Text = LanConnectConfig.PlayerDisplayName;
        }

        if (_serverBaseUrlInput != null)
        {
            _serverBaseUrlInput.Text = LanConnectConfig.LobbyServerBaseUrlOverride;
        }

        if (_lobbyCreateRoomTokenInput != null)
        {
            _lobbyCreateRoomTokenInput.Text = LanConnectConfig.LobbyCreateRoomToken == LanConnectLobbyEndpointDefaults.GetCreateRoomToken()
                ? string.Empty
                : LanConnectConfig.LobbyCreateRoomToken;
        }

        UpdateNetworkSummary();
        UpdateNetworkFieldMasking();
    }

    private void ToggleSensitiveNetworkVisibility()
    {
        _networkFieldsRevealed = !_networkFieldsRevealed;
        UpdateNetworkFieldMasking();
    }

    private void UpdateNetworkFieldMasking()
    {
        if (_serverBaseUrlInput != null)
        {
            _serverBaseUrlInput.Secret = !_networkFieldsRevealed;
        }

        if (_lobbyCreateRoomTokenInput != null)
        {
            _lobbyCreateRoomTokenInput.Secret = !_networkFieldsRevealed;
        }

        if (_toggleSensitiveNetworkButton != null)
        {
            SetButtonText(_toggleSensitiveNetworkButton, _networkFieldsRevealed ? "隐藏覆盖地址" : "显示覆盖地址");
        }
    }

    private void ClearNetworkOverrides()
    {
        if (_serverBaseUrlInput != null)
        {
            _serverBaseUrlInput.Text = string.Empty;
        }

        if (_lobbyCreateRoomTokenInput != null)
        {
            _lobbyCreateRoomTokenInput.Text = string.Empty;
        }

        PersistSettings();
    }

    private void ToggleNetworkSettingsVisibility()
    {
        if (_networkSettingsContainer == null || _toggleNetworkSettingsButton == null || _clearNetworkOverridesButton == null)
        {
            return;
        }

        _networkSettingsContainer.Visible = !_networkSettingsContainer.Visible;
        SetButtonText(
            _toggleNetworkSettingsButton,
            _networkSettingsContainer.Visible
                ? "收起开发网络设置"
                : "展开开发网络设置");
        _clearNetworkOverridesButton.Disabled = !LanConnectConfig.HasLobbyServerOverrides && string.IsNullOrWhiteSpace(_serverBaseUrlInput?.Text);
    }

    private void ToggleSettingsVisibility()
    {
        if (_settingsSection == null || _settingsSeparator == null)
        {
            return;
        }

        bool nextVisible = !_settingsSection.Visible;
        GD.Print($"sts2_lan_connect overlay: toggle settings -> {nextVisible}");
        _settingsSection.Visible = nextVisible;
        _settingsSeparator.Visible = nextVisible;
    }

    private void SetUnderlyingMenuVisible(bool visible)
    {
        if (_submenu == null)
        {
            return;
        }

        Control? buttonContainer = _submenu.GetNodeOrNull<Control>("ButtonContainer");
        if (buttonContainer != null)
        {
            buttonContainer.Visible = visible;
        }
    }

    private void UpdateActionButtons()
    {
        bool refreshBusy = _refreshInFlight;
        bool actionBusy = _actionInFlight;
        bool hasRunSave = SaveManager.Instance.HasMultiplayerRunSave;
        bool hasActiveRoom = LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true;
        bool hasLobbyEndpoint = HasAvailableLobbyEndpoint();
        LobbyRoomSummary? selectedRoom = GetSelectedRoom();
        bool selectedIsHostRoom = selectedRoom != null && LanConnectLobbyRuntime.Instance?.ActiveRoomId == selectedRoom.RoomId;

        string? createDisabledReason = GetCreateDisabledReason(actionBusy, hasRunSave, hasActiveRoom, hasLobbyEndpoint);
        string? createWarning = hasRunSave
            ? "检测到多人续局存档。请先走官方载入入口，进入后会自动恢复绑定的大厅房间。"
            : null;
        string? joinDisabledReason = GetJoinDisabledReason(actionBusy, selectedRoom, selectedIsHostRoom);

        if (_refreshButton != null)
        {
            _refreshButton.Disabled = refreshBusy || actionBusy;
        }

        if (_createButton != null)
        {
            _createButton.Disabled = actionBusy;
        }

        if (_joinButton != null)
        {
            _joinButton.Disabled = joinDisabledReason != null;
        }

        if (_closeRoomButton != null)
        {
            _closeRoomButton.Visible = hasActiveRoom;
            _closeRoomButton.Disabled = actionBusy || !hasActiveRoom;
        }

        if (_closeButton != null)
        {
            _closeButton.Disabled = false;
        }

        if (_settingsButton != null)
        {
            _settingsButton.Disabled = false;
        }

        if (_repairSaveButton != null)
        {
            _repairSaveButton.Disabled = actionBusy;
        }

        if (_copyDebugReportButton != null)
        {
            _copyDebugReportButton.Disabled = false;
        }

        UpdateRoomFilterButtons();

        if (_pagePreviousButton != null)
        {
            _pagePreviousButton.Disabled = _currentPageIndex <= 0;
        }

        if (_pageNextButton != null)
        {
            _pageNextButton.Disabled = _currentPageIndex >= Math.Max(0, GetTotalPages(GetFilteredRooms().Count) - 1);
        }

        if (_clearNetworkOverridesButton != null)
        {
            bool hasOverrideText = !string.IsNullOrWhiteSpace(_serverBaseUrlInput?.Text)
                || !string.IsNullOrWhiteSpace(_lobbyCreateRoomTokenInput?.Text);
            _clearNetworkOverridesButton.Disabled = !(LanConnectConfig.HasLobbyServerOverrides || hasOverrideText);
        }

        // Update structured action status rows
        if (_actionCreateStatusDot != null && _actionCreateStatusValue != null)
        {
            bool createOk = createDisabledReason == null;
            _actionCreateStatusDot.Color = createOk ? SuccessColor : DangerColor;
            SetLabelText(_actionCreateStatusValue, createOk ? "OK" : "BLOCKED");
            _actionCreateStatusValue.AddThemeColorOverride("font_color", createOk ? SuccessColor : DangerColor);
        }

        if (_actionJoinStatusDot != null && _actionJoinStatusValue != null)
        {
            bool joinOk = joinDisabledReason == null;
            _actionJoinStatusDot.Color = joinOk ? SuccessColor : AccentColor;
            SetLabelText(_actionJoinStatusValue, joinOk ? "OK" : "BLOCKED");
            _actionJoinStatusValue.AddThemeColorOverride("font_color", joinOk ? SuccessColor : AccentColor);
        }

        if (_actionSyncStatusRow != null && _actionSyncStatusDot != null && _actionSyncStatusValue != null)
        {
            _actionSyncStatusRow.Visible = refreshBusy;
            _actionSyncStatusDot.Color = AccentColor;
            SetLabelText(_actionSyncStatusValue, "SYNCING...");
            _actionSyncStatusValue.AddThemeColorOverride("font_color", TextMutedColor);
        }

        string actionState = $"refresh={refreshBusy};action={actionBusy};hasRunSave={hasRunSave};hasActiveRoom={hasActiveRoom};hasLobbyEndpoint={hasLobbyEndpoint};rooms={_rooms.Count};selected={(selectedRoom == null ? "<none>" : selectedRoom.RoomId)};create={(createDisabledReason ?? "enabled")};join={(joinDisabledReason ?? "enabled")}";
        if (_lastActionDebugState != actionState)
        {
            GD.Print($"sts2_lan_connect overlay: action state -> {actionState}");
            _lastActionDebugState = actionState;
        }
    }

    private void OnRoomSearchChanged(string value)
    {
        _roomSearchQuery = value.Trim();
        ApplyRoomFilterState("search");
    }

    private void ClearRoomSearch()
    {
        if (_roomSearchInput != null)
        {
            _roomSearchInput.Text = string.Empty;
        }

        OnRoomSearchChanged(string.Empty);
    }

    private void ClearRoomFiltersAndSearch()
    {
        _showPublicRooms = true;
        _showLockedRooms = true;
        _joinableOnlyFilter = false;
        _showStandardMode = true;
        _showDailyMode = true;
        _showCustomMode = true;
        ClearRoomSearch();
        UpdateRoomFilterButtons();
    }

    private void ChangePage(int delta)
    {
        int totalPages = GetTotalPages(GetFilteredRooms().Count);
        if (totalPages <= 0)
        {
            return;
        }

        _currentPageIndex = Math.Clamp(_currentPageIndex + delta, 0, totalPages - 1);
        ResetRoomListTouchTracking();
        ResetRoomListScroll();
        RebuildRoomStage();
        UpdateActionButtons();
    }

    private void UpdatePageControls(int filteredCount)
    {
        int totalPages = GetTotalPages(filteredCount);
        if (_roomPagerRow != null)
        {
            _roomPagerRow.Visible = totalPages > 1;
        }

        SetLabelText(
            _pageSummaryLabel,
            totalPages <= 0
                ? "PAGE 0/0"
                : $"PAGE {_currentPageIndex + 1}/{totalPages}");

        if (_pagePreviousButton != null)
        {
            _pagePreviousButton.Disabled = totalPages <= 1 || _currentPageIndex <= 0;
        }

        if (_pageNextButton != null)
        {
            _pageNextButton.Disabled = totalPages <= 1 || _currentPageIndex >= totalPages - 1;
        }
    }

    private void UpdateHealthIndicator()
    {
        if (_healthIndicatorLabel == null)
        {
            return;
        }

        string statusText;
        string latencyText;
        Color color;
        if (!HasAvailableLobbyEndpoint())
        {
            statusText = "OFFLINE";
            latencyText = "--";
            color = DangerColor;
        }
        else if (_consecutiveRefreshFailures >= 2)
        {
            statusText = "ERROR";
            latencyText = "--";
            color = DangerColor;
        }
        else if (_consecutiveRefreshFailures == 1)
        {
            statusText = "RETRY";
            latencyText = "--";
            color = AccentColor;
        }
        else if (_lastLobbyRttMs < 0d)
        {
            statusText = _rooms.Count > 0 ? "ONLINE" : "CONNECTING";
            latencyText = "--";
            color = AccentColor;
        }
        else if (_lastLobbyRttMs <= 600d)
        {
            statusText = "ONLINE";
            latencyText = $"{_lastLobbyRttMs:0}ms";
            color = SuccessColor;
        }
        else if (_lastLobbyRttMs <= 1500d)
        {
            statusText = "ONLINE";
            latencyText = $"{_lastLobbyRttMs:0}ms";
            color = AccentColor;
        }
        else
        {
            statusText = "ONLINE";
            latencyText = $"{_lastLobbyRttMs:0}ms";
            color = DangerColor;
        }

        SetLabelText(_healthIndicatorLabel, statusText);
        _healthIndicatorLabel.AddThemeColorOverride("font_color", TextStrongColor);
        SetLabelText(_healthIndicatorLatencyLabel, latencyText);
        _healthIndicatorLatencyLabel?.AddThemeColorOverride("font_color", color);
        _healthIndicatorDotColor = color;
        if (_healthIndicatorDot is ColorRect dotRect)
        {
            dotRect.Color = color;
        }
        else if (_healthIndicatorDot != null)
        {
            _healthIndicatorDot.SelfModulate = color;
        }

        if (_statusHealthValueLabel != null)
        {
            string healthText = !HasAvailableLobbyEndpoint()
                ? "N/A"
                : _consecutiveRefreshFailures >= 2
                    ? "FAIL"
                    : _consecutiveRefreshFailures == 1
                        ? "RETRY"
                        : _lastLobbyRttMs < 0d
                            ? "..."
                            : _lastLobbyRttMs <= 600d
                                ? "OK"
                                : _lastLobbyRttMs <= 1500d
                                    ? "SLOW"
                                    : "SLOW";
            SetLabelText(_statusHealthValueLabel, healthText);
            _statusHealthValueLabel.AddThemeColorOverride("font_color", color);
        }

        if (_statusHealthValueIcon is ColorRect statusSquare)
        {
            statusSquare.Color = color;
        }
        else if (_statusHealthValueIcon != null)
        {
            _statusHealthValueIcon.SelfModulate = color;
        }

        if (_statusLatencyValueLabel != null)
        {
            SetLabelText(_statusLatencyValueLabel, _lastLobbyRttMs < 0d ? "--" : $"{_lastLobbyRttMs:0}ms");
            _statusLatencyValueLabel.AddThemeColorOverride("font_color", _lastLobbyRttMs < 0d ? TextMutedColor : color);
        }

        if (_statusRoomCountValueLabel != null)
        {
            SetLabelText(_statusRoomCountValueLabel, _rooms.Count.ToString());
            _statusRoomCountValueLabel.AddThemeColorOverride("font_color", _rooms.Count > 0 ? TextStrongColor : TextMutedColor);
        }
    }

    private static async Task<double?> MeasureLobbyProbeRttSafeAsync(LobbyApiClient apiClient)
    {
        try
        {
            return await apiClient.MeasureProbeRttAsync();
        }
        catch (Exception ex)
        {
            GD.Print($"sts2_lan_connect overlay: probe request failed with exception {ex.Message}");
            return null;
        }
    }

    private static async Task<double?> MeasureServerProbeRttSafeAsync(string baseUrl)
    {
        try
        {
            string normalized = baseUrl.TrimEnd('/');
            Uri probeUri = new($"{normalized}/probe");
            using System.Net.Http.HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5d) };
            Stopwatch sw = Stopwatch.StartNew();
            using HttpResponseMessage response = await client.GetAsync(probeUri);
            sw.Stop();
            return response.IsSuccessStatusCode ? sw.Elapsed.TotalMilliseconds : null;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateNetworkSummary()
    {
        if (_networkSummaryLabel == null)
        {
            return;
        }

        string summary;
        Color color;
        if (LanConnectConfig.HasLobbyServerOverrides)
        {
            summary = "当前网络：已启用手动大厅覆盖地址。中心服务器与读取令牌继续使用打包内置值；建房令牌默认随包内置，也可在此页临时覆盖。";
            color = AccentColor;
        }
        else if (LanConnectLobbyEndpointDefaults.HasBundledDefaults())
        {
            summary = "当前网络：使用打包内置大厅、中心服务器与访问令牌。默认值仅在运行时读取，不会写进 config.json，也不会在这里明文显示。";
            color = SuccessColor;
        }
        else
        {
            summary = "当前网络：未找到打包内置大厅服务。若需要联机，请在开发网络设置里填写覆盖地址。";
            color = DangerColor;
        }

        SetLabelText(_networkSummaryLabel, summary);
        _networkSummaryLabel.AddThemeColorOverride("font_color", color);
    }

    private void SetStatus(string message)
    {
        SetLabelText(_statusLabel, message);
        if (_statusLabel != null)
        {
            _statusLabel.Visible = !string.IsNullOrWhiteSpace(message);
        }

        if (_lastStatusMessage != message)
        {
            GD.Print($"sts2_lan_connect overlay: status -> {message}");
            _lastStatusMessage = message;
        }
    }

    private void CopyDebugReportToClipboard()
    {
        try
        {
            LanConnectDebugOverlayState overlayState = new(
                _lastStatusMessage,
                _lastLobbyRttMs,
                _rooms.Count,
                _selectedRoomId,
                _consecutiveRefreshFailures,
                GetSelectedRoom());
            string report = LanConnectDebugReport.Build(overlayState);
            DisplayServer.ClipboardSet(report);
            GD.Print($"sts2_lan_connect overlay: copied debug report to clipboard, length={report.Length}");
            LanConnectPopupUtil.ShowInfo("已把本地调试报告复制到剪贴板。\n直接粘贴给开发者即可。报告不会包含房间密码。");
        }
        catch (Exception ex)
        {
            GD.Print($"sts2_lan_connect overlay: failed to copy debug report -> {ex}");
            LanConnectPopupUtil.ShowInfo($"复制本地调试报告失败：{ex.Message}");
        }
    }

    private void CheckClipboardForInviteCode()
    {
        try
        {
            string clipboardText = DisplayServer.ClipboardGet();
            if (!LanConnectInviteCode.TryDecode(clipboardText, out LanConnectInvitePayload? payload) || payload == null)
            {
                return;
            }

            GD.Print($"sts2_lan_connect overlay: detected invite code in clipboard, server={payload.S}, roomId={payload.R}");

            if (_actionInFlight)
            {
                return;
            }

            if (LanConnectLobbyRuntime.Instance?.ActiveRoomId == payload.R)
            {
                GD.Print("sts2_lan_connect overlay: invite code targets own hosted room, ignoring");
                return;
            }

            ShowInviteConfirmDialog(payload);
        }
        catch (Exception ex)
        {
            GD.Print($"sts2_lan_connect overlay: clipboard invite check failed -> {ex.Message}");
        }
    }

    private void ShowInviteConfirmDialog(LanConnectInvitePayload payload)
    {
        _pendingInvitePayload = payload;

        bool serverDiffers = !string.Equals(
            payload.S.TrimEnd('/'),
            LanConnectConfig.LobbyServerBaseUrl.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

        string roomIdPreview = payload.R.Length > 8 ? payload.R[..8] + "..." : payload.R;
        string message = $"剪贴板中包含一个房间邀请码。\n房间ID：{roomIdPreview}";
        if (serverDiffers)
        {
            message += $"\n\n注意：该邀请指向不同的服务器（{payload.S}），加入时会临时切换。";
        }
        if (!string.IsNullOrWhiteSpace(payload.P))
        {
            message += "\n邀请码中已包含房间密码，无需手动输入。";
        }

        SetLabelText(_inviteConfirmDialogMessage, message);

        if (_inviteConfirmDialogContainer != null)
        {
            _inviteConfirmDialogContainer.Visible = true;
            _inviteConfirmDialogContainer.MoveToFront();
        }
    }

    private void CloseInviteConfirmDialog()
    {
        if (_inviteConfirmDialogContainer != null)
        {
            _inviteConfirmDialogContainer.Visible = false;
        }
        _pendingInvitePayload = null;
    }

    private async Task AcceptInviteAsync()
    {
        LanConnectInvitePayload? payload = _pendingInvitePayload;
        CloseInviteConfirmDialog();

        if (payload == null) return;

        bool serverDiffers = !string.Equals(
            payload.S.TrimEnd('/'),
            LanConnectConfig.LobbyServerBaseUrl.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

        string originalServerUrl = LanConnectConfig.LobbyServerBaseUrl;
        string? originalInputText = _serverBaseUrlInput?.Text;

        if (serverDiffers)
        {
            // Update both config AND the settings input field.
            // PersistSettings() (called by JoinRoomAsync) reads from the input field
            // and writes it back to config — if we only set the config, the input field
            // still holds the old URL and PersistSettings() would revert our switch.
            LanConnectConfig.LobbyServerBaseUrl = payload.S;
            if (_serverBaseUrlInput != null)
            {
                _serverBaseUrlInput.Text = payload.S;
            }
            GD.Print($"sts2_lan_connect overlay: switched server for invite join: {originalServerUrl} -> {payload.S}");
        }

        try
        {
            using LobbyApiClient apiClient = LobbyApiClient.CreateConfigured();
            IReadOnlyList<LobbyRoomSummary> rooms = await apiClient.GetRoomsAsync();
            LobbyRoomSummary? targetRoom = null;
            foreach (LobbyRoomSummary r in rooms)
            {
                if (string.Equals(r.RoomId, payload.R, StringComparison.Ordinal))
                {
                    targetRoom = r;
                    break;
                }
            }

            if (targetRoom == null)
            {
                SetStatus("邀请码对应的房间不存在或已关闭。");
                LanConnectPopupUtil.ShowInfo("邀请码对应的房间不存在或已关闭，可能房主已经关闭了房间。");
                RevertInviteServerSwitch(serverDiffers, originalServerUrl, originalInputText);
                return;
            }

            await BeginJoinRoomAsync(targetRoom, payload.P);
        }
        catch (Exception ex)
        {
            GD.Print($"sts2_lan_connect overlay: invite join failed -> {ex}");
            SetStatus($"通过邀请码加入失败：{ex.Message}");
            RevertInviteServerSwitch(serverDiffers, originalServerUrl, originalInputText);
        }
    }

    private void RevertInviteServerSwitch(bool serverDiffers, string originalServerUrl, string? originalInputText)
    {
        if (!serverDiffers) return;
        LanConnectConfig.LobbyServerBaseUrl = originalServerUrl;
        if (_serverBaseUrlInput != null && originalInputText != null)
        {
            _serverBaseUrlInput.Text = originalInputText;
        }
    }

    private Control BuildInviteConfirmDialog()
    {
        Control shell = CreateDialogShell(out VBoxContainer body);
        _inviteConfirmDialogContainer = shell;

        body.AddChild(CreateSectionLabel("检测到邀请码"));

        _inviteConfirmDialogMessage = CreateBodyLabel("");
        _inviteConfirmDialogMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _inviteConfirmDialogMessage.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(_inviteConfirmDialogMessage);

        HBoxContainer buttons = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        buttons.AddThemeConstantOverride("separation", 10);
        body.AddChild(buttons);

        Button cancel = CreateActionButton("忽略", "忽略该邀请码，继续浏览大厅。", CloseInviteConfirmDialog);
        cancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(cancel);

        Button accept = CreateActionButton("加入房间", "根据邀请码自动加入该房间。", () => TaskHelper.RunSafely(AcceptInviteAsync()), primary: true);
        accept.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(accept);

        return shell;
    }

    private void ShowProgressDialog(string title, string message, string? hint = null, bool allowCancel = false)
    {
        if (_progressDialogContainer == null)
        {
            return;
        }

        _progressDialogVisibleDuration = 0d;
        ConfigureProgressDialogCancel(allowCancel);
        SetProgressDialogContent(title, message, hint);
        _progressDialogContainer.Visible = true;
        _progressDialogContainer.MoveToFront();
        GD.Print($"sts2_lan_connect overlay: progress dialog shown -> {title} | {message}");
    }

    private void UpdateProgressDialog(string title, string message, string? hint = null, bool? allowCancel = null)
    {
        if (_progressDialogContainer == null || !_progressDialogContainer.Visible)
        {
            return;
        }

        if (allowCancel.HasValue)
        {
            ConfigureProgressDialogCancel(allowCancel.Value);
        }

        SetProgressDialogContent(title, message, hint);
        GD.Print($"sts2_lan_connect overlay: progress dialog updated -> {title} | {message}");
    }

    private void HideProgressDialog()
    {
        if (_progressDialogContainer != null)
        {
            _progressDialogContainer.Visible = false;
        }

        _progressDialogBaseMessage = string.Empty;
        _progressDialogTick = 0d;
        _progressDialogDotCount = 0;
        _progressDialogVisibleDuration = 0d;
        ConfigureProgressDialogCancel(false);
    }

    private void SetProgressDialogContent(string title, string message, string? hint)
    {
        SetLabelText(_progressDialogTitle, title);

        SetLabelText(
            _progressDialogHint,
            string.IsNullOrWhiteSpace(hint)
                ? "连接较慢时请稍候，期间不要重复点击按钮或关闭页面。"
                : hint);

        _progressDialogBaseMessage = message.Trim();
        _progressDialogTick = 0d;
        _progressDialogDotCount = 0;
        RefreshProgressDialogMessage();
    }

    private void AnimateProgressDialog(double delta)
    {
        if (_progressDialogContainer == null || !_progressDialogContainer.Visible || string.IsNullOrWhiteSpace(_progressDialogBaseMessage))
        {
            return;
        }

        _progressDialogVisibleDuration += delta;
        RefreshProgressDialogCancelButton();
        _progressDialogTick += delta;
        if (_progressDialogTick < 0.45d)
        {
            return;
        }

        _progressDialogTick = 0d;
        _progressDialogDotCount = (_progressDialogDotCount + 1) % 4;
        RefreshProgressDialogMessage();
    }

    private void RefreshProgressDialogMessage()
    {
        if (_progressDialogMessage == null)
        {
            return;
        }

        string suffix = _progressDialogDotCount == 0 ? string.Empty : new string('.', _progressDialogDotCount);
        SetLabelText(_progressDialogMessage, _progressDialogBaseMessage + suffix);
    }

    private void UpdateMaxPlayersHint()
    {
        if (_maxPlayersHintLabel == null || _maxPlayersSpinBox == null)
        {
            return;
        }

        int selectedMaxPlayers = (int)_maxPlayersSpinBox.Value;
        string message = selectedMaxPlayers <= LanConnectConstants.MinMaxPlayers
            ? "4 人房会自动启用 0.2.2 兼容协议，可与 0.2.2 玩家共同游玩。"
            : "5-8 人房仅支持 0.2.3+ 客户端，不兼容 0.2.2。";
        SetLabelText(_maxPlayersHintLabel, message);
    }

    private void ReplaceJoinCancellationSource(CancellationTokenSource cancellationTokenSource)
    {
        _activeJoinCancellationSource?.Dispose();
        _activeJoinCancellationSource = cancellationTokenSource;
        RefreshProgressDialogCancelButton();
    }

    private void ClearJoinCancellationSource(CancellationTokenSource cancellationTokenSource)
    {
        if (!ReferenceEquals(_activeJoinCancellationSource, cancellationTokenSource))
        {
            cancellationTokenSource.Dispose();
            return;
        }

        _activeJoinCancellationSource?.Dispose();
        _activeJoinCancellationSource = null;
        RefreshProgressDialogCancelButton();
    }

    private void CancelActiveJoinRequest()
    {
        if (_activeJoinCancellationSource == null || _activeJoinCancellationSource.IsCancellationRequested)
        {
            return;
        }

        _activeJoinCancellationSource.Cancel();
        ConfigureProgressDialogCancel(false);
        UpdateProgressDialog(
            "正在取消连接",
            "正在取消当前联机连接",
            "正在通知房主停止本次连接申请，请稍候。",
            allowCancel: false);
    }

    private void ConfigureProgressDialogCancel(bool allowCancel)
    {
        _progressDialogAllowCancel = allowCancel;
        RefreshProgressDialogCancelButton();
    }

    private void RefreshProgressDialogCancelButton()
    {
        if (_progressDialogCancelButton == null)
        {
            return;
        }

        bool hasCancelableJoin = _progressDialogAllowCancel
            && _activeJoinCancellationSource != null
            && !_activeJoinCancellationSource.IsCancellationRequested
            && _progressDialogVisibleDuration >= JoinCancelRevealDelaySeconds;
        _progressDialogCancelButton.Visible = hasCancelableJoin;
    }

    private void ShowCreateDialogError(string message, bool visible = true)
    {
        if (_createDialogErrorLabel == null)
        {
            return;
        }

        SetLabelText(_createDialogErrorLabel, message);
        _createDialogErrorLabel.Visible = visible;
    }

    private void ShowJoinPasswordError(string message, bool visible = true)
    {
        if (_joinPasswordDialogErrorLabel == null)
        {
            return;
        }

        SetLabelText(_joinPasswordDialogErrorLabel, message);
        _joinPasswordDialogErrorLabel.Visible = visible;
    }

    private void ShowResumeSlotError(string message, bool visible = true)
    {
        if (_resumeSlotDialogErrorLabel == null)
        {
            return;
        }

        SetLabelText(_resumeSlotDialogErrorLabel, message);
        _resumeSlotDialogErrorLabel.Visible = visible;
    }

    private void ReportJoinIssue(string message)
    {
        if (_resumeSlotDialogContainer != null && _resumeSlotDialogContainer.Visible)
        {
            ShowResumeSlotError(message);
            return;
        }

        if (_joinPasswordDialogContainer != null && _joinPasswordDialogContainer.Visible)
        {
            ShowJoinPasswordError(message);
            return;
        }

        SetStatus(message);
    }

    private static bool HasAvailableLobbyEndpoint()
    {
        return LanConnectConfig.HasLobbyServerOverrides || LanConnectLobbyEndpointDefaults.HasBundledDefaults();
    }

    private static List<LobbySavedRunSlot> GetAvailableSavedRunSlots(LobbyRoomSummary room)
    {
        if (room.SavedRun == null)
        {
            return new List<LobbySavedRunSlot>();
        }

        return room.SavedRun.Slots
            .FindAll(static slot => !slot.IsConnected);
    }

    private LobbyRoomSummary? GetSelectedRoom()
    {
        List<LobbyRoomSummary> filteredRooms = GetFilteredRooms();
        if (filteredRooms.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_selectedRoomId))
        {
            LobbyRoomSummary? selected = filteredRooms.Find(room => room.RoomId == _selectedRoomId);
            if (selected != null)
            {
                return selected;
            }
        }

        _selectedRoomId = filteredRooms[0].RoomId;
        return filteredRooms[0];
    }

    private static string? GetCreateDisabledReason(bool actionBusy, bool hasRunSave, bool hasActiveRoom, bool hasLobbyEndpoint)
    {
        if (actionBusy)
        {
            return "当前已有房间操作在进行。";
        }

        if (hasRunSave)
        {
            return "检测到多人续局存档，请先点击官方载入。";
        }

        if (hasActiveRoom)
        {
            return "你已经托管了一个房间。";
        }

        if (!hasLobbyEndpoint)
        {
            return "当前客户端尚未绑定大厅服务。";
        }

        return null;
    }

    private string? GetCreateAvailabilityReasonForDialog()
    {
        return GetCreateDisabledReason(
            _actionInFlight,
            SaveManager.Instance.HasMultiplayerRunSave,
            LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true,
            HasAvailableLobbyEndpoint());
    }

    private static string? GetJoinDisabledReason(bool actionBusy, LobbyRoomSummary? selectedRoom, bool selectedIsHostRoom)
    {
        if (actionBusy)
        {
            return "当前已有房间操作在进行。";
        }

        if (selectedRoom == null)
        {
            return "当前没有可加入的房间。";
        }

        if (selectedIsHostRoom)
        {
            return "当前选中的是你自己托管的房间。";
        }

        return CanJoinRoom(selectedRoom, out string? reason) ? null : reason;
    }

    private static bool CanJoinRoom(LobbyRoomSummary room, out string? reason)
    {
        if (string.Equals(room.Status, "starting", StringComparison.OrdinalIgnoreCase))
        {
            reason = "该房间已经开始游戏。";
            return false;
        }

        if (string.Equals(room.Status, "full", StringComparison.OrdinalIgnoreCase))
        {
            reason = "该房间已经满员。";
            return false;
        }

        if (string.Equals(room.Status, "closed", StringComparison.OrdinalIgnoreCase))
        {
            reason = "该房间已经关闭。";
            return false;
        }

        if (room.SavedRun != null && GetAvailableSavedRunSlots(room).Count == 0)
        {
            reason = "该续局房间当前没有可接管角色。";
            return false;
        }

        if (string.Equals(LanConnectLobbyEndpointDefaults.GetConnectionStrategy(), "relay-only", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(room.RelayState, "ready", StringComparison.OrdinalIgnoreCase))
        {
            reason = "房主 relay 尚未注册完成，请稍后刷新后再试。";
            return false;
        }

        reason = null;
        return true;
    }

    private List<LobbyRoomSummary> GetFilteredRooms()
    {
        List<LobbyRoomSummary> filtered = new();
        string query = _roomSearchQuery.Trim();
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        foreach (LobbyRoomSummary room in _rooms)
        {
            if (!RoomMatchesVisibilityFilter(room))
            {
                continue;
            }

            if (!RoomMatchesGameModeFilter(room))
            {
                continue;
            }

            if (hasQuery && !RoomMatchesSearch(room, query))
            {
                continue;
            }

            filtered.Add(room);
        }

        return filtered;
    }

    private static bool ContainsIgnoreCase(string source, string value)
    {
        return !string.IsNullOrWhiteSpace(source) && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool RoomMatchesVisibilityFilter(LobbyRoomSummary room)
    {
        bool matchesAccess = room.RequiresPassword ? _showLockedRooms : _showPublicRooms;
        if (!matchesAccess)
        {
            return false;
        }

        return !_joinableOnlyFilter || CanDisplayAsJoinable(room);
    }

    private bool RoomMatchesGameModeFilter(LobbyRoomSummary room)
    {
        return room.GameMode.Trim().ToLowerInvariant() switch
        {
            "daily" => _showDailyMode,
            "custom" => _showCustomMode,
            _ => _showStandardMode
        };
    }

    private bool CanDisplayAsJoinable(LobbyRoomSummary room)
    {
        if (string.Equals(LanConnectLobbyRuntime.Instance?.ActiveRoomId, room.RoomId, StringComparison.Ordinal))
        {
            return false;
        }

        return CanJoinRoom(room, out _);
    }

    private static bool RoomMatchesSearch(LobbyRoomSummary room, string query)
    {
        return ContainsIgnoreCase(room.RoomName, query)
               || ContainsIgnoreCase(room.HostPlayerName, query)
               || ContainsIgnoreCase(room.Version, query)
               || ContainsIgnoreCase(room.ModVersion, query)
               || ContainsIgnoreCase(FormatStatus(room.Status), query)
               || ContainsIgnoreCase(LanConnectMultiplayerSaveRoomBinding.GetLobbyGameModeLabel(room.GameMode), query);
    }

    private static bool AreRoomListsVisuallyEquivalent(IReadOnlyList<LobbyRoomSummary> previousRooms, IReadOnlyList<LobbyRoomSummary> currentRooms)
    {
        if (previousRooms.Count != currentRooms.Count)
        {
            return false;
        }

        for (int index = 0; index < previousRooms.Count; index++)
        {
            if (!AreRoomCardsVisuallyEquivalent(previousRooms[index], currentRooms[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreRoomCardsVisuallyEquivalent(LobbyRoomSummary left, LobbyRoomSummary right)
    {
        return string.Equals(left.RoomId, right.RoomId, StringComparison.Ordinal)
               && string.Equals(left.RoomName, right.RoomName, StringComparison.Ordinal)
               && string.Equals(left.HostPlayerName, right.HostPlayerName, StringComparison.Ordinal)
               && left.RequiresPassword == right.RequiresPassword
               && string.Equals(left.Status, right.Status, StringComparison.Ordinal)
               && string.Equals(left.GameMode, right.GameMode, StringComparison.Ordinal)
               && left.CurrentPlayers == right.CurrentPlayers
               && left.MaxPlayers == right.MaxPlayers
               && string.Equals(left.Version, right.Version, StringComparison.Ordinal)
               && string.Equals(left.ModVersion, right.ModVersion, StringComparison.Ordinal)
               && string.Equals(left.ProtocolProfile, right.ProtocolProfile, StringComparison.Ordinal)
               && string.Equals(left.RelayState, right.RelayState, StringComparison.Ordinal)
               && AreSavedRunsEquivalent(left.SavedRun, right.SavedRun);
    }

    private static bool AreSavedRunsEquivalent(LobbySavedRunInfo? left, LobbySavedRunInfo? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (!string.Equals(left.SaveKey, right.SaveKey, StringComparison.Ordinal) ||
            left.ConnectedPlayerNetIds.Count != right.ConnectedPlayerNetIds.Count ||
            left.Slots.Count != right.Slots.Count)
        {
            return false;
        }

        for (int index = 0; index < left.ConnectedPlayerNetIds.Count; index++)
        {
            if (!string.Equals(left.ConnectedPlayerNetIds[index], right.ConnectedPlayerNetIds[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (int index = 0; index < left.Slots.Count; index++)
        {
            LobbySavedRunSlot leftSlot = left.Slots[index];
            LobbySavedRunSlot rightSlot = right.Slots[index];
            if (!string.Equals(leftSlot.NetId, rightSlot.NetId, StringComparison.Ordinal) ||
                !string.Equals(leftSlot.CharacterId, rightSlot.CharacterId, StringComparison.Ordinal) ||
                !string.Equals(leftSlot.CharacterName, rightSlot.CharacterName, StringComparison.Ordinal) ||
                !string.Equals(leftSlot.PlayerName, rightSlot.PlayerName, StringComparison.Ordinal) ||
                leftSlot.IsHost != rightSlot.IsHost ||
                leftSlot.IsConnected != rightSlot.IsConnected)
            {
                return false;
            }
        }

        return true;
    }

    private List<LobbyRoomSummary> GetVisibleRooms(List<LobbyRoomSummary> filteredRooms)
    {
        ClampCurrentPage(filteredRooms.Count);
        int startIndex = _currentPageIndex * LanConnectConstants.LobbyRoomsPerPage;
        int count = Math.Min(LanConnectConstants.LobbyRoomsPerPage, Math.Max(0, filteredRooms.Count - startIndex));
        if (count <= 0)
        {
            return new List<LobbyRoomSummary>();
        }

        return filteredRooms.GetRange(startIndex, count);
    }

    private void ClampCurrentPage(int filteredCount)
    {
        int totalPages = GetTotalPages(filteredCount);
        _currentPageIndex = totalPages <= 0
            ? 0
            : Math.Clamp(_currentPageIndex, 0, totalPages - 1);
    }

    private static int GetTotalPages(int itemCount)
    {
        return itemCount <= 0
            ? 0
            : (itemCount + LanConnectConstants.LobbyRoomsPerPage - 1) / LanConnectConstants.LobbyRoomsPerPage;
    }

    private string DescribeJoinFailure(LobbyServiceException ex)
    {
        return ex.Code switch
        {
            "version_mismatch" => ex.Message,
            "mod_mismatch" => LanConnectLobbyModMismatchFormatter.FormatFromDetails(ex.Details, ex.Message),
            "mod_version_mismatch" => LanConnectLobbyModMismatchFormatter.FormatFromDetails(ex.Details, ex.Message),
            _ => LanConnectCompatibilityMatrix.DescribeJoinFailureCode(ex.Code, ex.Message)
        };
    }

    private static string DescribeGenericJoinFailure(Exception ex)
    {
        return ex.Message;
    }

    private static string BuildCreateRoomGuardDetail(double? currentBandwidthMbps, double? resolvedCapacityMbps, double? utilizationRatio, string? capacitySource)
    {
        List<string> parts = new();
        parts.Add($"当前带宽：{FormatBandwidthValue(currentBandwidthMbps)}");
        parts.Add($"有效容量：{FormatBandwidthValue(resolvedCapacityMbps)}");
        parts.Add($"当前利用率：{FormatUtilizationValue(utilizationRatio)}");
        parts.Add($"容量来源：{FormatCapacitySource(capacitySource)}");
        return string.Join("\n", parts);
    }

    private static string FormatStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "open" => "开放中",
            "starting" => "已开局",
            "full" => "已满",
            "closed" => "已关闭",
            _ => status
        };
    }

    private void OnRoomFilterMenuIdPressed(long id)
    {
        switch ((int)id)
        {
            case FilterPublicId:
                _showPublicRooms = !_showPublicRooms;
                break;
            case FilterLockedId:
                _showLockedRooms = !_showLockedRooms;
                break;
            case FilterJoinableId:
                _joinableOnlyFilter = !_joinableOnlyFilter;
                break;
            case FilterModeStandardId:
                _showStandardMode = !_showStandardMode;
                break;
            case FilterModeDailyId:
                _showDailyMode = !_showDailyMode;
                break;
            case FilterModeCustomId:
                _showCustomMode = !_showCustomMode;
                break;
            default:
                return;
        }

        UpdateRoomFilterButtons();
        ApplyRoomFilterState($"menu_{id}");
    }

    private void ApplyRoomFilterState(string source)
    {
        GD.Print(
            $"sts2_lan_connect overlay: room filters -> source={source};public={_showPublicRooms};locked={_showLockedRooms};joinableOnly={_joinableOnlyFilter};standard={_showStandardMode};daily={_showDailyMode};custom={_showCustomMode};query='{_roomSearchQuery}'");
        _currentPageIndex = 0;
        ResetRoomListTouchTracking();
        ResetRoomListScroll();
        RebuildRoomStage();
        UpdateActionButtons();
    }

    private bool HasRoomSearchOrFilter()
    {
        return !string.IsNullOrWhiteSpace(_roomSearchQuery)
               || !_showPublicRooms
               || !_showLockedRooms
               || _joinableOnlyFilter
               || !_showStandardMode
               || !_showDailyMode
               || !_showCustomMode;
    }

    private string DescribeRoomFilterState()
    {
        List<string> filters = new();

        if (_showPublicRooms && !_showLockedRooms)
        {
            filters.Add("公开");
        }
        else if (!_showPublicRooms && _showLockedRooms)
        {
            filters.Add("上锁");
        }
        else if (!_showPublicRooms && !_showLockedRooms)
        {
            filters.Add("无房间类型");
        }

        if (_joinableOnlyFilter)
        {
            filters.Add("可加入");
        }

        if (_showStandardMode && !_showDailyMode && !_showCustomMode)
        {
            filters.Add("标准模式");
        }
        else if (!_showStandardMode && _showDailyMode && !_showCustomMode)
        {
            filters.Add("多人每日挑战");
        }
        else if (!_showStandardMode && !_showDailyMode && _showCustomMode)
        {
            filters.Add("自定义模式");
        }
        else if (!_showStandardMode || !_showDailyMode || !_showCustomMode)
        {
            List<string> modes = new();
            if (_showStandardMode)
            {
                modes.Add("标准");
            }

            if (_showDailyMode)
            {
                modes.Add("挑战");
            }

            if (_showCustomMode)
            {
                modes.Add("自定义");
            }

            filters.Add(modes.Count == 0 ? "无游戏模式" : $"模式：{string.Join("/", modes)}");
        }

        if (!string.IsNullOrWhiteSpace(_roomSearchQuery))
        {
            filters.Add($"搜索：{_roomSearchQuery}");
        }

        return filters.Count == 0 ? "全部" : string.Join(" · ", filters);
    }

    private static string FormatBandwidthValue(double? value)
    {
        return value.HasValue ? $"{value.Value:0.##} Mbps" : "未知";
    }

    private static string FormatUtilizationValue(double? value)
    {
        return value.HasValue ? $"{value.Value * 100:0.#}%" : "未知";
    }

    private static string FormatCapacitySource(string? value)
    {
        return value switch
        {
            "manual" => "手动配置",
            "probe_peak_7d" => "近 7 天探针峰值",
            _ => "未知"
        };
    }

    private static string FormatCreateRoomGuardStatus(string? value)
    {
        return value switch
        {
            "block" => "禁止新建",
            "unknown" => "状态未知",
            _ => "允许创建"
        };
    }

    private Button CreateActionButton(string text, string tooltip, Action onPressed, bool primary = false, bool danger = false, GlyphIconKind iconKind = GlyphIconKind.None)
    {
        Button button = new()
        {
            Text = iconKind == GlyphIconKind.None ? UiText(text) : string.Empty,
            TooltipText = UiText(tooltip),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 42f),
            Alignment = HorizontalAlignment.Center
        };
        ApplyButtonStyle(button, primary, danger);
        if (iconKind != GlyphIconKind.None)
        {
            AttachActionButtonContent(button, iconKind, text, primary
                ? PrimaryFgColor
                : TextStrongColor);
        }

        button.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            GD.Print($"sts2_lan_connect overlay: action button '{text}' pressed");
            onPressed();
        }));
        return button;
    }

    private Button CreateToolbarButton(string text, string tooltip, Action onPressed, GlyphIconKind iconKind, bool accent = false)
    {
        Button button = new()
        {
            Text = string.Empty,
            TooltipText = UiText(tooltip),
            CustomMinimumSize = new Vector2(170f, 50f),
            Alignment = HorizontalAlignment.Center
        };
        ApplyToolbarButtonStyle(button, accent, iconOnly: false);
        AttachToolbarButtonContent(button, iconKind, text);
        button.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            GD.Print($"sts2_lan_connect overlay: toolbar button '{text}' pressed");
            onPressed();
        }));
        return button;
    }

    private Button CreateToolbarIconButton(string tooltip, Action onPressed, GlyphIconKind iconKind, bool accent = false)
    {
        Button button = new()
        {
            Text = string.Empty,
            TooltipText = UiText(tooltip),
            CustomMinimumSize = new Vector2(50f, 50f),
            Alignment = HorizontalAlignment.Center
        };
        ApplyToolbarButtonStyle(button, accent, iconOnly: true);
        AttachToolbarIconContent(button, iconKind);
        button.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            GD.Print($"sts2_lan_connect overlay: toolbar icon button pressed");
            onPressed();
        }));
        return button;
    }

    private Button CreateDestructiveToolbarIconButton(string tooltip, Action onPressed, GlyphIconKind iconKind)
    {
        Button button = new()
        {
            Text = string.Empty,
            TooltipText = UiText(tooltip),
            CustomMinimumSize = new Vector2(50f, 50f),
            Alignment = HorizontalAlignment.Center
        };
        ApplyDestructiveToolbarButtonStyle(button);
        AttachToolbarIconContent(button, iconKind, CardColor);
        button.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            GD.Print($"sts2_lan_connect overlay: destructive toolbar icon button pressed");
            onPressed();
        }));
        return button;
    }

    private Button CreateInlineButton(string text, Action onPressed, bool accent = false)
    {
        Button button = new()
        {
            Text = UiText(text),
            CustomMinimumSize = new Vector2(0f, 40f)
        };
        ApplyInlineButtonStyle(button, accent);
        button.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            GD.Print($"sts2_lan_connect overlay: inline button '{text}' pressed");
            onPressed();
        }));
        return button;
    }

    private Button CreateFilterChipButton(string text, string tooltip, Action onPressed)
    {
        Button button = new()
        {
            Text = UiText(text),
            TooltipText = UiText(tooltip),
            CustomMinimumSize = new Vector2(0f, 38f)
        };
        ApplyFilterChipStyle(button, active: false);
        button.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            GD.Print($"sts2_lan_connect overlay: filter chip '{text}' pressed");
            onPressed();
        }));
        return button;
    }

    private static string UiText(string text) => LanConnectUiText.NormalizeForDisplay(text);

    private static void SetButtonText(Button? button, string text)
    {
        if (button != null)
        {
            if (button.FindChild("ButtonLabel", recursive: true, owned: false) is Label label)
            {
                label.Text = UiText(text);
                button.Text = string.Empty;
            }
            else
            {
                button.Text = UiText(text);
            }
        }
    }

    private static void SetLabelText(Label? label, string text)
    {
        if (label != null)
        {
            label.Text = UiText(text);
        }
    }

    private static void AttachToolbarButtonContent(Button button, GlyphIconKind iconKind, string text)
    {
        CenterContainer center = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        button.AddChild(center);
        SetupPressShift(button, center, 3);

        HBoxContainer row = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        row.AddThemeConstantOverride("separation", 10);
        center.AddChild(row);

        row.AddChild(new GlyphIcon
        {
            Kind = iconKind,
            GlyphColor = TextStrongColor,
            CustomMinimumSize = new Vector2(20f, 20f)
        });

        Label label = CreateBodyLabel(text);
        label.Name = "ButtonLabel";
        label.AddThemeFontSizeOverride("font_size", 17);
        row.AddChild(label);
    }

    private static void AttachActionButtonContent(Button button, GlyphIconKind iconKind, string text, Color iconColor)
    {
        CenterContainer center = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        button.AddChild(center);
        SetupPressShift(button, center, 3);

        HBoxContainer row = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        row.AddThemeConstantOverride("separation", 10);
        center.AddChild(row);

        row.AddChild(new GlyphIcon
        {
            Kind = iconKind,
            GlyphColor = iconColor,
            CustomMinimumSize = new Vector2(18f, 18f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        });

        Label label = CreateBodyLabel(text);
        label.Name = "ButtonLabel";
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", iconColor);
        row.AddChild(label);
    }

    private static void AttachToolbarIconContent(Button button, GlyphIconKind iconKind, Color? color = null)
    {
        CenterContainer center = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        button.AddChild(center);
        SetupPressShift(button, center, 3);
        center.AddChild(new GlyphIcon
        {
            Kind = iconKind,
            GlyphColor = color ?? TextStrongColor,
            CustomMinimumSize = new Vector2(19f, 19f)
        });
    }

    // ── Pixel-art panel factories ──

    private static PanelContainer CreateGlassPanel(int padding = 18, int radius = 0)
    {
        return CreatePixelBorderPanel(padding: padding);
    }

    /// <summary>Main pixel-border container (3px border, 4px hard offset shadow).</summary>
    private static PanelContainer CreatePixelBorderPanel(Color? background = null, int padding = 18)
    {
        return new PixelBorderPanel
        {
            BgColor = background ?? CardColor,
            PixelBorderColor = BorderColor,
            PixelBorderWidth = 3,
            ShadowPixelOffset = 4,
            DrawInsetBevel = true,
            Padding = padding
        };
    }

    /// <summary>Compact pixel-border container (2px border, 2px hard offset shadow).</summary>
    private static PanelContainer CreatePixelBorderSmPanel(Color? background = null, int padding = 10)
    {
        return new PixelBorderPanel
        {
            BgColor = background ?? CardColor,
            PixelBorderColor = BorderColor,
            PixelBorderWidth = 2,
            ShadowPixelOffset = 2,
            DrawInsetBevel = true,
            Padding = padding
        };
    }

    /// <summary>Legacy compatibility — routes to pixel-border.</summary>
    private static PanelContainer CreateSurfacePanel(Color background, Color border, int radius = 0, int borderWidth = 2, int padding = 18)
    {
        PanelContainer panel = new() { ClipContents = true };
        panel.AddThemeStyleboxOverride("panel", CreatePixelStyle(background, border, borderWidth: borderWidth, padding: padding, shadowSize: borderWidth >= 3 ? 4 : 2));
        return panel;
    }

    private static StyleBoxFlat CreatePixelStyle(Color background, Color border, int borderWidth = 3, int padding = 18, int shadowSize = 4)
    {
        StyleBoxFlat style = new()
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
            ContentMarginBottom = padding
        };
        style.ShadowColor = new Color(border, 0.55f);
        style.ShadowSize = shadowSize;
        style.ShadowOffset = new Vector2(shadowSize, shadowSize);
        return style;
    }

    /// <summary>
    /// Creates a pixel-art button style with physical press animation.
    ///
    /// The trick: ExpandMargin shifts the ENTIRE drawn area (border + bg)
    /// while keeping the total visual footprint constant.
    ///
    /// depth=0 → normal: button at rest, full shadow visible.
    /// depth=maxShadow → fully pressed: button moved into shadow position, no shadow.
    ///
    /// Visual:  Normal:  [BUTTON]░░░   (shadow on right/bottom)
    ///          Pressed: ░░░[BUTTON]   (button slid into shadow, shadow gone)
    ///          The shadow's outer edge stays fixed — the button moves toward it.
    /// </summary>
    private static StyleBoxFlat CreatePixelPressStyle(Color background, Color border, int borderWidth, int padding, int maxShadow, int depth)
    {
        int shadow = Math.Max(0, maxShadow - depth);
        return new StyleBoxFlat
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
            // ExpandMargin shifts the drawn rect (bg + border).
            // Negative left/top → draws starting further right/down.
            // Positive right/bottom → extends draw area to keep total footprint constant.
            // Total footprint = control_rect + maxShadow (constant across all depths).
            ExpandMarginLeft = -depth,
            ExpandMarginTop = -depth,
            ExpandMarginRight = maxShadow + depth,
            ExpandMarginBottom = maxShadow + depth,
            ShadowColor = new Color(0.15f, 0.10f, 0.08f, 0.45f),
            ShadowSize = shadow,
            ShadowOffset = new Vector2(shadow, shadow)
        };
    }

    /// <summary>
    /// Connects mouse_entered/mouse_exited on a Button to tint all child
    /// Labels and GlyphIcons between normal and hover colors.
    /// Fixes the issue where Button.font_hover_color only affects Button.Text
    /// but not custom child nodes.
    /// </summary>
    private static void SetupChildHoverTint(Button button, Color normalColor, Color hoverColor)
    {
        button.Connect(Control.SignalName.MouseEntered, Callable.From(() =>
        {
            if (!button.Disabled)
                TintButtonChildrenRecursive(button, hoverColor);
        }));
        button.Connect(Control.SignalName.MouseExited, Callable.From(() =>
            TintButtonChildrenRecursive(button, normalColor)));
    }

    /// <summary>
    /// Shifts a FullRect-anchored content host to follow the CreatePixelPressStyle
    /// expand-margin animation on hover (depth=1) and press (depth=maxShadow).
    /// Without this, child icons/labels stay fixed while the background sinks.
    /// </summary>
    private static void SetupPressShift(Button button, Control contentHost, int maxShadow)
    {
        bool hovering = false;
        button.Connect(Control.SignalName.MouseEntered, Callable.From(() =>
        {
            hovering = true;
            if (!button.ButtonPressed)
                ApplyContentShift(contentHost, 1);
        }));
        button.Connect(Control.SignalName.MouseExited, Callable.From(() =>
        {
            hovering = false;
            if (!button.ButtonPressed)
                ApplyContentShift(contentHost, 0);
        }));
        button.Connect(BaseButton.SignalName.ButtonDown, Callable.From(() =>
            ApplyContentShift(contentHost, maxShadow)));
        button.Connect(BaseButton.SignalName.ButtonUp, Callable.From(() =>
            ApplyContentShift(contentHost, hovering ? 1 : 0)));
    }

    private static void ApplyContentShift(Control host, int depth)
    {
        host.OffsetLeft = depth;
        host.OffsetTop = depth;
        host.OffsetRight = depth;
        host.OffsetBottom = depth;
    }

    private static void TintButtonChildrenRecursive(Node node, Color color)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Label label)
            {
                label.AddThemeColorOverride("font_color", color);
            }
            else if (child is GlyphIcon icon)
            {
                // GlyphColor is init-only; use SelfModulate to tint at runtime.
                // GlyphIcon draws a white texture tinted by GlyphColor in _Draw().
                // SelfModulate multiplies on top, so we compute the ratio.
                Color original = icon.GlyphColor;
                icon.SelfModulate = new Color(
                    original.R > 0.01f ? color.R / original.R : color.R,
                    original.G > 0.01f ? color.G / original.G : color.G,
                    original.B > 0.01f ? color.B / original.B : color.B,
                    1f);
            }

            TintButtonChildrenRecursive(child, color);
        }
    }

    /// <summary>Legacy compatibility shim.</summary>
    private static StyleBoxFlat CreatePanelStyle(Color background, Color border, int radius, int borderWidth, int padding, int shadowSize = 0, Color? shadowColor = null)
    {
        return CreatePixelStyle(background, border, borderWidth: borderWidth, padding: padding, shadowSize: shadowSize);
    }

    private static Label CreateTitleLabel(string text, int size)
    {
        Label label = new()
        {
            Text = UiText(text),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        label.AddThemeColorOverride("font_color", TextStrongColor);
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static Label CreateSectionLabel(string text)
    {
        Label label = new()
        {
            Text = UiText(text)
        };
        label.AddThemeColorOverride("font_color", AccentColor);
        label.AddThemeFontSizeOverride("font_size", 18);
        return label;
    }

    private static Control CreateSectionHeader(string text, GlyphIconKind iconKind)
    {
        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 8);

        row.AddChild(new GlyphIcon
        {
            Kind = iconKind,
            GlyphColor = AccentColor,
            CustomMinimumSize = new Vector2(18f, 18f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        });

        row.AddChild(CreateSectionLabel(text));
        return row;
    }

    private static Label CreateBodyLabel(string text)
    {
        Label label = new()
        {
            Text = UiText(text)
        };
        label.AddThemeColorOverride("font_color", TextStrongColor);
        label.AddThemeFontSizeOverride("font_size", 16);
        return label;
    }

    private static void ApplyButtonStyle(Button button, bool primary, bool danger)
    {
        Color normalBg = primary ? AccentColor
            : danger ? new Color(0.63f, 0.24f, 0.24f, 0.9f)
            : CardColor;
        Color hoverBg = primary ? AccentBrightColor
            : danger ? new Color(0.73f, 0.30f, 0.30f, 1f)
            : SuccessColor;
        Color pressedBg = primary ? new Color(AccentColor, 0.85f)
            : danger ? new Color(0.55f, 0.20f, 0.20f, 1f)
            : new Color(SuccessColor, 0.8f);
        Color border = danger ? DangerColor : BorderColor;

        // Physical press: content shifts toward shadow → depth 0/1/3
        button.AddThemeStyleboxOverride("normal", CreatePixelPressStyle(normalBg, border, 2, 14, 3, 0));
        button.AddThemeStyleboxOverride("hover", CreatePixelPressStyle(hoverBg, border, 2, 14, 3, 1));
        button.AddThemeStyleboxOverride("pressed", CreatePixelPressStyle(pressedBg, border, 2, 14, 3, 3));
        button.AddThemeStyleboxOverride("disabled", CreatePixelStyle(WithAlpha(normalBg, 0.45f), WithAlpha(border, 0.4f), borderWidth: 2, padding: 14, shadowSize: 0));
        button.AddThemeStyleboxOverride("focus", CreatePixelPressStyle(hoverBg, border, 2, 14, 3, 1));
        // Primary (CREATE) keeps dark text; all others → white on hover/press
        Color normalText = primary ? PrimaryFgColor : (danger ? CardColor : TextStrongColor);
        Color activeText = primary ? PrimaryFgColor : CardColor;
        button.AddThemeColorOverride("font_color", normalText);
        button.AddThemeColorOverride("font_hover_color", activeText);
        button.AddThemeColorOverride("font_pressed_color", activeText);
        button.AddThemeColorOverride("font_focus_color", activeText);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 16);
        // Tint custom child icons/labels on hover (Button.font_hover_color only affects Button.Text)
        if (!primary)
        {
            SetupChildHoverTint(button, normalText, activeText);
        }
    }

    private static void ApplyToolbarButtonStyle(Button button, bool accent, bool iconOnly)
    {
        int pad = iconOnly ? 10 : 14;
        Color bg = SecondaryColor;
        Color hoverBg = SuccessColor;
        Color pressedBg = new Color(SuccessColor, 0.8f);

        // Physical press animation
        button.AddThemeStyleboxOverride("normal", CreatePixelPressStyle(bg, BorderColor, 2, pad, 3, 0));
        button.AddThemeStyleboxOverride("hover", CreatePixelPressStyle(hoverBg, BorderColor, 2, pad, 3, 1));
        button.AddThemeStyleboxOverride("pressed", CreatePixelPressStyle(pressedBg, BorderColor, 2, pad, 3, 3));
        button.AddThemeStyleboxOverride("disabled", CreatePixelStyle(WithAlpha(bg, 0.45f), WithAlpha(BorderColor, 0.4f), borderWidth: 2, padding: pad, shadowSize: 0));
        button.AddThemeStyleboxOverride("focus", CreatePixelPressStyle(hoverBg, BorderColor, 2, pad, 3, 1));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        button.AddThemeColorOverride("font_hover_color", CardColor);
        button.AddThemeColorOverride("font_pressed_color", CardColor);
        button.AddThemeColorOverride("font_focus_color", CardColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", iconOnly ? 18 : 15);
        // Tint child icons/labels white on hover
        SetupChildHoverTint(button, TextStrongColor, CardColor);
    }

    private static void ApplyDestructiveToolbarButtonStyle(Button button)
    {
        int pad = 10;
        Color bg = DangerColor;
        Color hoverBg = new Color(0.65f, 0.10f, 0.12f, 1f);   // darker red on hover
        Color pressedBg = new Color(0.55f, 0.08f, 0.10f, 1f);  // even darker on press

        button.AddThemeStyleboxOverride("normal", CreatePixelPressStyle(bg, new Color(0.60f, 0.10f, 0.12f, 1f), 2, pad, 3, 0));
        button.AddThemeStyleboxOverride("hover", CreatePixelPressStyle(hoverBg, new Color(0.50f, 0.08f, 0.10f, 1f), 2, pad, 3, 1));
        button.AddThemeStyleboxOverride("pressed", CreatePixelPressStyle(pressedBg, new Color(0.45f, 0.06f, 0.08f, 1f), 2, pad, 3, 3));
        button.AddThemeStyleboxOverride("disabled", CreatePixelStyle(WithAlpha(bg, 0.45f), WithAlpha(BorderColor, 0.4f), borderWidth: 2, padding: pad, shadowSize: 0));
        button.AddThemeStyleboxOverride("focus", CreatePixelPressStyle(hoverBg, new Color(0.50f, 0.08f, 0.10f, 1f), 2, pad, 3, 1));
        button.AddThemeColorOverride("font_color", CardColor);
        button.AddThemeColorOverride("font_hover_color", CardColor);
        button.AddThemeColorOverride("font_pressed_color", CardColor);
        button.AddThemeColorOverride("font_focus_color", CardColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(CardColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 18);
        // Icon starts white on red bg — stays white on hover
        SetupChildHoverTint(button, CardColor, CardColor);
    }

    private static void ApplyInlineButtonStyle(Button button, bool accent)
    {
        Color bg = accent ? SecondaryColor : CardColor;
        Color hoverBg = SuccessColor;
        Color pressedBg = new Color(SuccessColor, 0.8f);

        // Physical press animation
        button.AddThemeStyleboxOverride("normal", CreatePixelPressStyle(bg, BorderColor, 2, 12, 3, 0));
        button.AddThemeStyleboxOverride("hover", CreatePixelPressStyle(hoverBg, BorderColor, 2, 12, 3, 1));
        button.AddThemeStyleboxOverride("pressed", CreatePixelPressStyle(pressedBg, BorderColor, 2, 12, 3, 3));
        button.AddThemeStyleboxOverride("disabled", CreatePixelStyle(WithAlpha(bg, 0.45f), WithAlpha(BorderColor, 0.4f), borderWidth: 2, padding: 12, shadowSize: 0));
        button.AddThemeStyleboxOverride("focus", CreatePixelPressStyle(hoverBg, BorderColor, 2, 12, 3, 1));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        button.AddThemeColorOverride("font_hover_color", CardColor);
        button.AddThemeColorOverride("font_pressed_color", CardColor);
        button.AddThemeColorOverride("font_focus_color", CardColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 17);
    }

    private void UpdateRoomFilterButtons()
    {
        if (_roomFilterMenuButton != null)
        {
            ApplyInlineButtonStyle(_roomFilterMenuButton, HasRoomSearchOrFilter());
            _roomFilterMenuButton.TooltipText = UiText($"当前筛选：{DescribeRoomFilterState()}");
        }

        // Sync toggle buttons in the filter dialog if it's open
        if (_filterDialogContainer is { Visible: true })
        {
            SyncFilterDialogButtons();
        }
    }

    private static void SyncPopupCheckState(PopupMenu popup, int id, bool isChecked)
    {
        int itemIndex = popup.GetItemIndex(id);
        if (itemIndex >= 0)
        {
            popup.SetItemChecked(itemIndex, isChecked);
        }
    }

    private static void ApplySearchInputStyle(LineEdit input)
    {
        input.CustomMinimumSize = new Vector2(0f, 22f);
        input.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        input.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_placeholder_color", new Color(TextMutedColor, 0.6f));
        input.AddThemeColorOverride("caret_color", AccentColor);
        input.AddThemeFontSizeOverride("font_size", 15);
    }

    private static void ApplyFilterChipStyle(Button button, bool active)
    {
        Color bg = active ? AccentColor : SecondaryColor;
        Color hoverBg = active ? AccentBrightColor : SuccessColor;
        Color pressedBg = active ? new Color(AccentColor, 0.85f) : new Color(SuccessColor, 0.8f);

        // Physical press animation
        button.AddThemeStyleboxOverride("normal", CreatePixelPressStyle(bg, BorderColor, 2, 10, 3, 0));
        button.AddThemeStyleboxOverride("hover", CreatePixelPressStyle(hoverBg, BorderColor, 2, 10, 3, 1));
        button.AddThemeStyleboxOverride("pressed", CreatePixelPressStyle(pressedBg, BorderColor, 2, 10, 3, 3));
        button.AddThemeStyleboxOverride("disabled", CreatePixelStyle(WithAlpha(bg, 0.45f), WithAlpha(BorderColor, 0.4f), borderWidth: 2, padding: 10, shadowSize: 0));
        button.AddThemeStyleboxOverride("focus", CreatePixelPressStyle(hoverBg, BorderColor, 2, 10, 3, 1));
        Color normalText = active ? PrimaryFgColor : TextStrongColor;
        button.AddThemeColorOverride("font_color", normalText);
        button.AddThemeColorOverride("font_hover_color", CardColor);
        button.AddThemeColorOverride("font_pressed_color", CardColor);
        button.AddThemeColorOverride("font_focus_color", CardColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 15);
    }

    private static void ApplyInputStyle(LineEdit input)
    {
        input.CustomMinimumSize = new Vector2(0f, 48f);
        input.AddThemeStyleboxOverride("normal", CreatePixelStyle(InputBgColor, BorderColor, borderWidth: 2, padding: 12, shadowSize: 0));
        input.AddThemeStyleboxOverride("focus", CreatePixelStyle(CardColor, AccentColor, borderWidth: 2, padding: 12, shadowSize: 0));
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_placeholder_color", new Color(TextMutedColor, 0.6f));
        input.AddThemeColorOverride("caret_color", AccentColor);
        input.AddThemeFontSizeOverride("font_size", 16);
    }

    private static void ApplyInputStyle(OptionButton input)
    {
        input.CustomMinimumSize = new Vector2(0f, 48f);
        input.AddThemeStyleboxOverride("normal", CreatePixelStyle(InputBgColor, BorderColor, borderWidth: 2, padding: 12, shadowSize: 0));
        input.AddThemeStyleboxOverride("hover", CreatePixelStyle(CardColor, AccentColor, borderWidth: 2, padding: 12, shadowSize: 0));
        input.AddThemeStyleboxOverride("pressed", CreatePixelStyle(CardColor, AccentColor, borderWidth: 2, padding: 12, shadowSize: 0));
        input.AddThemeStyleboxOverride("focus", CreatePixelStyle(CardColor, AccentColor, borderWidth: 2, padding: 12, shadowSize: 0));
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_hover_color", TextStrongColor);
        input.AddThemeColorOverride("font_pressed_color", TextStrongColor);
        input.AddThemeColorOverride("font_focus_color", TextStrongColor);
        input.AddThemeColorOverride("modulate_arrow", AccentColor);
        input.AddThemeFontSizeOverride("font_size", 16);
    }

    private static (ColorRect Dot, Label Value) BuildActionStatusRow(VBoxContainer parent, string keyText)
    {
        HBoxContainer row = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);

        ColorRect dot = new()
        {
            Color = SuccessColor,
            CustomMinimumSize = new Vector2(6f, 6f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        row.AddChild(dot);

        Label key = CreateBodyLabel(keyText);
        key.AddThemeColorOverride("font_color", TextMutedColor);
        key.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(key);

        Label value = CreateBodyLabel("OK");
        value.AddThemeColorOverride("font_color", SuccessColor);
        value.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(value);

        return (dot, value);
    }

    private static (ColorRect Dot, Label Value, HBoxContainer Row) BuildActionStatusRowWithContainer(VBoxContainer parent, string keyText)
    {
        HBoxContainer row = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);

        ColorRect dot = new()
        {
            Color = AccentColor,
            CustomMinimumSize = new Vector2(6f, 6f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        row.AddChild(dot);

        Label value = CreateBodyLabel("SYNCING...");
        value.AddThemeColorOverride("font_color", TextMutedColor);
        value.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(value);

        return (dot, value, row);
    }

    private Label CreateMetricStatusRow(VBoxContainer parent, string labelText, string valueText)
    {
        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 12);
        parent.AddChild(row);

        Label label = CreateBodyLabel(labelText);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.AddThemeColorOverride("font_color", TextMutedColor);
        label.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(label);

        HBoxContainer valueRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        valueRow.AddThemeConstantOverride("separation", 6);
        row.AddChild(valueRow);

        _statusHealthValueIcon = new ColorRect
        {
            Color = SuccessColor,
            CustomMinimumSize = new Vector2(12f, 12f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        valueRow.AddChild(_statusHealthValueIcon);

        Label value = CreateBodyLabel(valueText);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.AddThemeFontSizeOverride("font_size", 18);
        valueRow.AddChild(value);
        return value;
    }

    private static void ApplyPassiveMouseFilterRecursive(Node node)
    {
        if (node is LobbyAnnouncementCarousel)
        {
            return;
        }

        if (node is Control control && node is not Button && node is not LineEdit && node is not ColorRect)
        {
            control.MouseFilter = MouseFilterEnum.Ignore;
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyPassiveMouseFilterRecursive(child);
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.R, color.G, color.B, alpha);
    }

    private static void AddPanelChrome(PanelContainer panel, Color border)
    {
        Control chrome = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true
        };
        chrome.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddChild(chrome);

        ColorRect topLine = new()
        {
            Color = WithAlpha(AccentBrightColor, 0.025f),
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0f, 1f)
        };
        topLine.SetAnchorsPreset(LayoutPreset.TopWide);
        chrome.AddChild(topLine);
    }

    private enum GlyphIconKind
    {
        None,
        Wifi,
        Server,
        Gear,
        Back,
        Person,
        Search,
        Nodes,
        InfoCircle,
        Plus,
        JoinArrow,
        Refresh,
        Gamepad,
        Lock,
        Globe,
        Zap,
        Users,
        Share2,
        XClose
    }

    private sealed partial class GlyphIcon : Control
    {
        // ── SVG texture cache (shared across all GlyphIcon instances) ──
        // Keyed by GlyphIconKind — all icons render white strokes; color is applied via DrawTexture modulate.
        private static readonly Dictionary<GlyphIconKind, ImageTexture> SvgTextureCache = new();

        // Render resolution: 4x the 24px viewbox for crisp rendering at all display sizes.
        private const int SvgRenderSize = 96;

        public GlyphIconKind Kind { get; init; }

        public Color GlyphColor { get; init; } = Colors.White;

        public float StrokeWidth { get; init; } = 2f;

        public GlyphIcon()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            if (Kind == GlyphIconKind.None)
            {
                return;
            }

            ImageTexture? texture = GetOrCreateTexture(Kind);
            if (texture != null)
            {
                DrawTextureRect(texture, new Rect2(Vector2.Zero, Size), false, GlyphColor);
            }
        }

        private static ImageTexture? GetOrCreateTexture(GlyphIconKind kind)
        {
            if (SvgTextureCache.TryGetValue(kind, out ImageTexture? cached))
            {
                return cached;
            }

            string? svgData = GetSvgForKind(kind);
            if (svgData == null)
            {
                return null;
            }

            // Replace stroke color with white so modulate tinting works correctly.
            // White (#FFFFFF) * modulate color = desired color.
            svgData = svgData.Replace("stroke=\"currentColor\"", "stroke=\"#FFFFFF\"");

            Image image = new();
            float scale = SvgRenderSize / 24f;
            Error err = image.LoadSvgFromString(svgData, scale);
            if (err != Error.Ok)
            {
                GD.PrintErr($"sts2_lan_connect: failed to load SVG for icon {kind}: {err}");
                return null;
            }

            ImageTexture texture = ImageTexture.CreateFromImage(image);
            SvgTextureCache[kind] = texture;
            return texture;
        }

        private static string? GetSvgForKind(GlyphIconKind kind) => kind switch
        {
            GlyphIconKind.Wifi => SvgWifi,
            GlyphIconKind.Server => SvgServer,
            GlyphIconKind.Gear => SvgGear,
            GlyphIconKind.Back => SvgBack,
            GlyphIconKind.Person => SvgPerson,
            GlyphIconKind.Search => SvgSearch,
            GlyphIconKind.Nodes => SvgNodes,
            GlyphIconKind.InfoCircle => SvgInfoCircle,
            GlyphIconKind.Plus => SvgPlus,
            GlyphIconKind.JoinArrow => SvgJoinArrow,
            GlyphIconKind.Refresh => SvgRefresh,
            GlyphIconKind.Gamepad => SvgGamepad,
            GlyphIconKind.Lock => SvgLock,
            GlyphIconKind.Globe => SvgGlobe,
            GlyphIconKind.Zap => SvgZap,
            GlyphIconKind.Users => SvgUsers,
            GlyphIconKind.Share2 => SvgShare2,
            GlyphIconKind.XClose => SvgXClose,
            _ => null,
        };

        // ── Lucide icon SVG strings (24×24 viewbox, stroke-based) ──

        private const string SvgWifi = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M12 20h.01"/>
              <path d="M2 8.82a15 15 0 0 1 20 0"/>
              <path d="M5 12.859a10 10 0 0 1 14 0"/>
              <path d="M8.5 16.429a5 5 0 0 1 7 0"/>
            </svg>
            """;

        private const string SvgServer = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect width="20" height="8" x="2" y="2" rx="2" ry="2"/>
              <rect width="20" height="8" x="2" y="14" rx="2" ry="2"/>
              <line x1="6" x2="6.01" y1="6" y2="6"/>
              <line x1="6" x2="6.01" y1="18" y2="18"/>
            </svg>
            """;

        private const string SvgGear = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915"/>
              <circle cx="12" cy="12" r="3"/>
            </svg>
            """;

        private const string SvgBack = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="m12 19-7-7 7-7"/>
              <path d="M19 12H5"/>
            </svg>
            """;

        private const string SvgPerson = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/>
              <circle cx="12" cy="7" r="4"/>
            </svg>
            """;

        private const string SvgSearch = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="m21 21-4.34-4.34"/>
              <circle cx="11" cy="11" r="8"/>
            </svg>
            """;

        private const string SvgNodes = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect x="16" y="16" width="6" height="6" rx="1"/>
              <rect x="2" y="16" width="6" height="6" rx="1"/>
              <rect x="9" y="2" width="6" height="6" rx="1"/>
              <path d="M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3"/>
              <path d="M12 12V8"/>
            </svg>
            """;

        private const string SvgInfoCircle = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="10"/>
              <path d="M12 16v-4"/>
              <path d="M12 8h.01"/>
            </svg>
            """;

        private const string SvgPlus = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M5 12h14"/>
              <path d="M12 5v14"/>
            </svg>
            """;

        private const string SvgJoinArrow = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="m10 17 5-5-5-5"/>
              <path d="M15 12H3"/>
              <path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/>
            </svg>
            """;

        private const string SvgRefresh = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/>
              <path d="M21 3v5h-5"/>
              <path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/>
              <path d="M8 16H3v5"/>
            </svg>
            """;

        private const string SvgGamepad = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <line x1="6" x2="10" y1="11" y2="11"/>
              <line x1="8" x2="8" y1="9" y2="13"/>
              <line x1="15" x2="15.01" y1="12" y2="12"/>
              <line x1="18" x2="18.01" y1="10" y2="10"/>
              <path d="M17.32 5H6.68a4 4 0 0 0-3.978 3.59c-.006.052-.01.101-.017.152C2.604 9.416 2 14.456 2 16a3 3 0 0 0 3 3c1 0 1.5-.5 2-1l1.414-1.414A2 2 0 0 1 9.828 16h4.344a2 2 0 0 1 1.414.586L17 18c.5.5 1 1 2 1a3 3 0 0 0 3-3c0-1.545-.604-6.584-.685-7.258-.007-.05-.011-.1-.017-.151A4 4 0 0 0 17.32 5z"/>
            </svg>
            """;

        private const string SvgLock = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect width="18" height="11" x="3" y="11" rx="2" ry="2"/>
              <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
            </svg>
            """;

        private const string SvgGlobe = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="10"/>
              <path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20"/>
              <path d="M2 12h20"/>
            </svg>
            """;

        private const string SvgZap = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z"/>
            </svg>
            """;

        private const string SvgUsers = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/>
              <path d="M16 3.128a4 4 0 0 1 0 7.744"/>
              <path d="M22 21v-2a4 4 0 0 0-3-3.87"/>
              <circle cx="9" cy="7" r="4"/>
            </svg>
            """;

        private const string SvgShare2 = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="18" cy="5" r="3"/>
              <circle cx="6" cy="12" r="3"/>
              <circle cx="18" cy="19" r="3"/>
              <line x1="8.59" x2="15.42" y1="13.51" y2="17.49"/>
              <line x1="15.41" x2="8.59" y1="6.51" y2="10.49"/>
            </svg>
            """;

        private const string SvgXClose = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
              <path d="M18 6 6 18"/>
              <path d="m6 6 12 12"/>
            </svg>
            """;
    }

    private sealed partial class RoomStateGlyph : Control
    {
        private static ImageTexture? _lockedTexture;
        private static ImageTexture? _unlockedTexture;
        private const int RenderSize = 96;

        public Color GlyphColor { get; init; } = Colors.White;

        public bool Unlocked { get; init; } = true;

        public RoomStateGlyph()
        {
            CustomMinimumSize = new Vector2(30f, 30f);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            ImageTexture? texture = GetOrCreateTexture(Unlocked);
            if (texture != null)
            {
                DrawTextureRect(texture, new Rect2(Vector2.Zero, Size), false, GlyphColor);
            }
        }

        private static ImageTexture? GetOrCreateTexture(bool unlocked)
        {
            ref ImageTexture? cached = ref (unlocked ? ref _unlockedTexture : ref _lockedTexture);
            if (cached != null)
            {
                return cached;
            }

            string svgData = (unlocked ? SvgLockOpen : SvgLockClosed).Replace("stroke=\"currentColor\"", "stroke=\"#FFFFFF\"");
            Image image = new();
            float scale = RenderSize / 24f;
            Error err = image.LoadSvgFromString(svgData, scale);
            if (err != Error.Ok)
            {
                return null;
            }

            cached = ImageTexture.CreateFromImage(image);
            return cached;
        }

        private const string SvgLockClosed = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect width="18" height="11" x="3" y="11" rx="2" ry="2"/>
              <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
            </svg>
            """;

        private const string SvgLockOpen = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect width="18" height="11" x="3" y="11" rx="2" ry="2"/>
              <path d="M7 11V7a5 5 0 0 1 9.9-1"/>
            </svg>
            """;
    }

    /// <summary>
    /// Custom PanelContainer that draws pixel-art borders with hard-edged offset shadows,
    /// matching CSS box-shadow: Npx Npx 0 0 (no blur).
    /// Replaces StyleBoxFlat which only supports blurred shadows.
    /// </summary>
    private sealed partial class PixelBorderPanel : PanelContainer
    {
        public Color BgColor { get; set; } = new(1f, 0.99f, 0.98f, 1f); // CardColor
        public Color PixelBorderColor { get; set; } = new(0.77f, 0.70f, 0.62f, 1f); // BorderColor
        public int PixelBorderWidth { get; set; } = 3;
        public int ShadowPixelOffset { get; set; } = 4;
        public bool DrawInsetBevel { get; set; } = true;
        public int Padding { get; set; } = 18;

        public PixelBorderPanel()
        {
            ClipContents = false;
        }

        public override void _Ready()
        {
            base._Ready();
            // Use a transparent StyleBox with padding only — all visuals come from _Draw()
            StyleBoxEmpty padStyle = new()
            {
                ContentMarginLeft = Padding,
                ContentMarginTop = Padding,
                ContentMarginRight = Padding,
                ContentMarginBottom = Padding
            };
            AddThemeStyleboxOverride("panel", padStyle);
        }

        public override void _Draw()
        {
            float w = Size.X;
            float h = Size.Y;
            int bw = PixelBorderWidth;
            int so = ShadowPixelOffset;

            // 1) Hard-edged offset shadow (drawn first, behind everything)
            if (so > 0)
            {
                DrawRect(new Rect2(so, so, w, h), PixelBorderColor);
            }

            // 2) Background fill
            DrawRect(new Rect2(0, 0, w, h), BgColor);

            // 3) Border (draw inward from edges)
            for (int i = 0; i < bw; i++)
            {
                DrawRect(new Rect2(i, i, w - 2 * i, h - 2 * i), PixelBorderColor, false, 1.0f);
            }

            // 4) Inner bevel highlights (CSS inset shadows)
            if (DrawInsetBevel)
            {
                float innerLeft = bw;
                float innerTop = bw;
                float innerRight = w - bw;
                float innerBottom = h - bw;
                Color highlight = new(1f, 1f, 1f, 0.3f);
                Color shadow = new(0f, 0f, 0f, 0.1f);

                // Top-left highlight
                DrawLine(new Vector2(innerLeft, innerTop + 1), new Vector2(innerRight, innerTop + 1), highlight, 2f);
                DrawLine(new Vector2(innerLeft + 1, innerTop), new Vector2(innerLeft + 1, innerBottom), highlight, 2f);

                // Bottom-right shadow
                DrawLine(new Vector2(innerRight - 1, innerTop), new Vector2(innerRight - 1, innerBottom), shadow, 2f);
                DrawLine(new Vector2(innerLeft, innerBottom - 1), new Vector2(innerRight, innerBottom - 1), shadow, 2f);
            }
        }
    }

    private void AnimateHealthIndicator(double delta)
    {
        if (!Visible || _healthIndicatorDot == null)
        {
            return;
        }

        _healthPulseTime = (_healthPulseTime + delta) % 2d;
        float phase = (float)(_healthPulseTime / 2d * Math.Tau);
        float wave = 0.5f * (1f + Mathf.Cos(phase));
        float opacity = 0.6f + 0.4f * wave;
        // ColorRect uses Color property directly for pulsing
        if (_healthIndicatorDot is ColorRect dotRect)
        {
            dotRect.Color = WithAlpha(_healthIndicatorDotColor, opacity);
        }
        else
        {
            _healthIndicatorDot.SelfModulate = WithAlpha(_healthIndicatorDotColor, opacity);
        }
    }

    private static string GetSuggestedRoomName()
    {
        return string.IsNullOrWhiteSpace(LanConnectConfig.LastRoomName)
            ? "新的联机房间"
            : LanConnectConfig.LastRoomName;
    }

    private static string FormatRoomName(string? roomName, int maxLength)
    {
        string value = string.IsNullOrWhiteSpace(roomName) ? "未命名房间" : roomName.Trim();
        if (maxLength < 4 || value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 3)] + "...";
    }
}
