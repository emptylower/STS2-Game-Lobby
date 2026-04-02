using System;
using System.Collections.Generic;
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

    private static readonly Color BackdropColor = new(0.051f, 0.043f, 0.035f, 0.96f);
    private static readonly Color FrameColor = new(0.051f, 0.043f, 0.035f, 0.98f);
    private static readonly Color SurfaceColor = new(0.059f, 0.047f, 0.039f, 0.76f);
    private static readonly Color SurfaceMutedColor = new(0.059f, 0.047f, 0.039f, 0.68f);
    private static readonly Color AccentColor = new(0.954f, 0.431f, 0.203f, 1f);
    private static readonly Color AccentBrightColor = new(0.996f, 0.58f, 0.274f, 1f);
    private static readonly Color AccentMutedColor = new(0.954f, 0.431f, 0.203f, 0.24f);
    private static readonly Color TextStrongColor = new(0.91f, 0.878f, 0.835f, 1f);
    private static readonly Color TextMutedColor = new(0.72f, 0.69f, 0.64f, 1f);
    private static readonly Color SuccessColor = new(0.25f, 0.9f, 0.46f, 1f);
    private static readonly Color DangerColor = new(0.94f, 0.38f, 0.33f, 1f);

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
    private LineEdit? _registryBaseUrlInput;
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
    private MenuButton? _roomFilterMenuButton;
    private Label? _actionAvailabilityLabel;
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
    private Control? _resumeSlotDialogContainer;
    private Label? _resumeSlotDialogTitle;
    private Label? _resumeSlotDialogErrorLabel;
    private VBoxContainer? _resumeSlotDialogOptions;
    private Control? _directoryServerDialogContainer;
    private Label? _directoryServerDialogTitle;
    private Label? _directoryServerDialogStatusLabel;
    private VBoxContainer? _directoryServerDialogOptions;
    private bool _directoryServerLoadInFlight;
    private LobbyRoomSummary? _pendingResumeJoinRoom;
    private string? _pendingResumeJoinPassword;
    private bool _networkFieldsRevealed;
    private bool _refreshInFlight;
    private bool _actionInFlight;
    private double _timeUntilAutoRefresh;
    private double _progressDialogTick;
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
    private double _healthPulseTime;
    private Color _healthIndicatorDotColor = SuccessColor;

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
        TaskHelper.RunSafely(RefreshRoomsAsync(userInitiated: false));
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
        root.AddThemeConstantOverride("separation", 18);
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
        ApplyResponsiveLayout();
    }

    private Control BuildHeaderRow()
    {
        VBoxContainer section = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        section.AddThemeConstantOverride("separation", 10);

        _headerContentHost = new Control
        {
            CustomMinimumSize = new Vector2(0f, 72f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        section.AddChild(_headerContentHost);

        _headerBrandRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _headerBrandRow.AddThemeConstantOverride("separation", 14);

        PanelContainer badge = CreateSurfacePanel(new Color(0.059f, 0.047f, 0.039f, 0.82f), new Color(AccentColor, 0.76f), radius: 18, borderWidth: 1, padding: 0);
        badge.CustomMinimumSize = new Vector2(56f, 56f);
        _headerBrandRow.AddChild(badge);

        CenterContainer badgeCenter = new();
        badge.AddChild(badgeCenter);

        Label badgeLabel = CreateTitleLabel("S", 26);
        badgeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        badgeLabel.VerticalAlignment = VerticalAlignment.Center;
        badgeLabel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        badgeLabel.AddThemeColorOverride("font_color", AccentColor);
        badgeCenter.AddChild(badgeLabel);

        VBoxContainer titleGroup = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        titleGroup.AddThemeConstantOverride("separation", 3);
        _headerBrandRow.AddChild(titleGroup);

        _headerTitleLabel = CreateTitleLabel("Slay the Spire 2", 26);
        titleGroup.AddChild(_headerTitleLabel);

        _headerSubtitleLabel = CreateBodyLabel("Mod 联机大厅");
        _headerSubtitleLabel.AddThemeFontSizeOverride("font_size", 15);
        _headerSubtitleLabel.AddThemeColorOverride("font_color", TextMutedColor);
        titleGroup.AddChild(_headerSubtitleLabel);

        _headerToolbar = new HBoxContainer()
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        _headerToolbar.AddThemeConstantOverride("separation", 10);

        _headerHealthPill = CreateSurfacePanel(new Color(0.059f, 0.047f, 0.039f, 0.82f), AccentMutedColor, radius: 18, borderWidth: 1, padding: 12);
        _headerHealthPill.CustomMinimumSize = new Vector2(194f, 0f);
        _headerToolbar.AddChild(_headerHealthPill);

        HBoxContainer healthRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        healthRow.AddThemeConstantOverride("separation", 10);
        _headerHealthPill.AddChild(healthRow);

        _healthIndicatorDot = new GlyphIcon
        {
            Kind = GlyphIconKind.Wifi,
            GlyphColor = SuccessColor,
            CustomMinimumSize = new Vector2(20f, 20f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        _healthIndicatorDot.PivotOffset = new Vector2(10f, 10f);
        healthRow.AddChild(_healthIndicatorDot);

        _healthIndicatorLabel = CreateBodyLabel("服务状态加载中");
        _healthIndicatorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _healthIndicatorLabel.AddThemeFontSizeOverride("font_size", 17);
        _healthIndicatorLabel.AddThemeColorOverride("font_color", TextStrongColor);
        healthRow.AddChild(_healthIndicatorLabel);

        _healthIndicatorLatencyLabel = CreateBodyLabel("--");
        _healthIndicatorLatencyLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _healthIndicatorLatencyLabel.AddThemeFontSizeOverride("font_size", 17);
        _healthIndicatorLatencyLabel.AddThemeColorOverride("font_color", SuccessColor);
        healthRow.AddChild(_healthIndicatorLatencyLabel);

        _chooseDirectoryServerButton = CreateToolbarButton("切换服务器", "打开公共服务器列表，切换到其他大厅。", () => TaskHelper.RunSafely(OpenDirectoryServerDialogAsync()), GlyphIconKind.Server, accent: true);
        _headerToolbar.AddChild(_chooseDirectoryServerButton);

        _settingsButton = CreateToolbarIconButton("展开或收起设置", ToggleSettingsVisibility, GlyphIconKind.Gear);
        _headerToolbar.AddChild(_settingsButton);

        _closeButton = CreateToolbarIconButton("返回上一级菜单", HideOverlay, GlyphIconKind.Back, accent: true);
        _headerToolbar.AddChild(_closeButton);

        ColorRect divider = new()
        {
            Color = new Color(AccentColor, 0.14f),
            CustomMinimumSize = new Vector2(0f, 1f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        section.AddChild(divider);

        VBoxContainer hero = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        hero.AddThemeConstantOverride("separation", 4);
        section.AddChild(hero);

        _heroTitleLabel = CreateTitleLabel("游戏大厅", 34);
        hero.AddChild(_heroTitleLabel);

        _heroSubtitleLabel = CreateBodyLabel("浏览房间、搜索筛选、查看状态，并按服务端策略走直连或 relay 加入联机流程。");
        _heroSubtitleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _heroSubtitleLabel.AddThemeFontSizeOverride("font_size", 15);
        _heroSubtitleLabel.AddThemeColorOverride("font_color", TextMutedColor);
        hero.AddChild(_heroSubtitleLabel);

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

        PanelContainer card = CreateSurfacePanel(SurfaceColor, AccentMutedColor, padding: 22);
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

        Label networkHint = CreateBodyLabel("这些字段只保存自定义覆盖值。留空表示继续使用打包时附带的默认大厅，不会把默认地址写入 config.json。");
        networkHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        networkHint.AddThemeColorOverride("font_color", TextMutedColor);
        _networkSettingsContainer.AddChild(networkHint);

        _networkSettingsContainer.AddChild(BuildLabeledInputRow("HTTP 覆盖", LanConnectConfig.LobbyServerBaseUrlOverride, out _serverBaseUrlInput, "留空则继续使用内置大厅；WS 会自动从 HTTP 地址推导"));
        _networkSettingsContainer.AddChild(BuildLabeledInputRow("中心服务器覆盖", LanConnectConfig.LobbyRegistryBaseUrlOverride, out _registryBaseUrlInput, "留空则继续使用默认中心服务器；用于拉取公共大厅列表"));

        if (_serverBaseUrlInput != null)
        {
            _serverBaseUrlInput.Secret = true;
        }

        if (_registryBaseUrlInput != null)
        {
            _registryBaseUrlInput.Secret = true;
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
        PanelContainer card = CreateGlassPanel(padding: 20);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.SizeFlagsVertical = SizeFlags.ExpandFill;

        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 14);
        card.AddChild(body);

        body.AddChild(CreateSectionLabel("房间目录"));

        Label summary = CreateBodyLabel("搜索、筛选房间，双击卡片可快速加入。");
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summary.AddThemeFontSizeOverride("font_size", 15);
        summary.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(summary);

        _roomListSummaryLabel = CreateBodyLabel("大厅当前没有房间。");
        _roomListSummaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _roomListSummaryLabel.AddThemeColorOverride("font_color", TextStrongColor);
        _roomListSummaryLabel.Visible = false;
        body.AddChild(_roomListSummaryLabel);

        body.AddChild(BuildRoomFilterRow());
        body.AddChild(BuildRoomPagerRow());

        PanelContainer listFrame = CreateSurfacePanel(new Color(0.059f, 0.047f, 0.039f, 0.68f), new Color(AccentColor, 0.12f), radius: 18, borderWidth: 1, padding: 10);
        listFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        listFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
        body.AddChild(listFrame);

        ScrollContainer scroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _roomListScroll = scroll;
        scroll.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(OnRoomListGuiInput));
        listFrame.AddChild(scroll);
        ConfigureRoomListScroll(scroll);

        _roomListContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _roomListContainer.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_roomListContainer);

        _roomHintLabel = CreateBodyLabel("刷新大厅后即可更新这里的内容。");
        _roomHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _roomHintLabel.AddThemeColorOverride("font_color", AccentColor);
        body.AddChild(_roomHintLabel);
        return card;
    }

    private Control BuildRoomFilterRow()
    {
        HBoxContainer container = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        container.AddThemeConstantOverride("separation", 8);

        PanelContainer searchShell = CreateSurfacePanel(new Color(0.059f, 0.047f, 0.039f, 0.82f), AccentMutedColor, radius: 12, borderWidth: 1, padding: 10);
        searchShell.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        container.AddChild(searchShell);

        HBoxContainer searchRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        searchRow.AddThemeConstantOverride("separation", 8);
        searchShell.AddChild(searchRow);

        searchRow.AddChild(new GlyphIcon
        {
            Kind = GlyphIconKind.Search,
            GlyphColor = TextMutedColor,
            CustomMinimumSize = new Vector2(18f, 18f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        });

        _roomSearchInput = new LineEdit
        {
            PlaceholderText = UiText("搜索房间 / 房主 / 版本 / 状态"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectAllOnFocus = true
        };
        ApplySearchInputStyle(_roomSearchInput);
        _roomSearchInput.Connect(LineEdit.SignalName.TextChanged, Callable.From<string>(OnRoomSearchChanged));
        searchRow.AddChild(_roomSearchInput);

        _roomFilterMenuButton = new MenuButton
        {
            Text = UiText("筛选"),
            TooltipText = UiText("展开筛选菜单，选择公开状态、可加入状态和游戏模式。"),
            CustomMinimumSize = new Vector2(92f, 44f)
        };
        ApplyInlineButtonStyle(_roomFilterMenuButton, accent: false);
        PopupMenu filterPopup = _roomFilterMenuButton.GetPopup();
        filterPopup.AddCheckItem(UiText("公开"), FilterPublicId);
        filterPopup.AddCheckItem(UiText("上锁"), FilterLockedId);
        filterPopup.AddCheckItem(UiText("可加入"), FilterJoinableId);
        filterPopup.AddSeparator();
        filterPopup.AddCheckItem(UiText("标准模式"), FilterModeStandardId);
        filterPopup.AddCheckItem(UiText("多人每日挑战"), FilterModeDailyId);
        filterPopup.AddCheckItem(UiText("自定义模式"), FilterModeCustomId);
        filterPopup.Connect(PopupMenu.SignalName.IdPressed, Callable.From<long>(OnRoomFilterMenuIdPressed));
        container.AddChild(_roomFilterMenuButton);

        Button clearButton = CreateInlineButton("清空", ClearRoomFiltersAndSearch);
        clearButton.TooltipText = UiText("清空当前搜索关键词");
        container.AddChild(clearButton);
        UpdateRoomFilterButtons();
        return container;
    }

    private Control BuildRoomPagerRow()
    {
        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 10);
        row.Visible = false;
        _roomPagerRow = row;

        _pageSummaryLabel = CreateBodyLabel("第 1 / 1 页");
        _pageSummaryLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _pageSummaryLabel.AddThemeColorOverride("font_color", TextMutedColor);
        row.AddChild(_pageSummaryLabel);

        _pagePreviousButton = CreateInlineButton("上一页", () => ChangePage(-1));
        row.AddChild(_pagePreviousButton);

        _pageNextButton = CreateInlineButton("下一页", () => ChangePage(1));
        row.AddChild(_pageNextButton);
        return row;
    }

    private Control BuildSidebar()
    {
        _sidebarContainer = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(278f, 0f),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _sidebarContainer.AddThemeConstantOverride("separation", 16);

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
            HBoxContainer layout = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            layout.SetAnchorsPreset(LayoutPreset.FullRect);
            layout.AddThemeConstantOverride("separation", 22);
            _mainContentHost.AddChild(layout);

            AttachChild(layout, _roomStagePanel);
            AttachChild(layout, _sidebarContainer);
            _roomStagePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _roomStagePanel.SizeFlagsVertical = SizeFlags.ExpandFill;
            _roomStagePanel.SizeFlagsStretchRatio = 1f;
            _sidebarContainer.SizeFlagsHorizontal = SizeFlags.Fill;
            _sidebarContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
            _sidebarContainer.SizeFlagsStretchRatio = 0f;
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
                _frameMargin.OffsetLeft = 18f;
                _frameMargin.OffsetTop = 18f;
                _frameMargin.OffsetRight = -18f;
                _frameMargin.OffsetBottom = -18f;
            }
            else
            {
                _frameMargin.OffsetLeft = 28f;
                _frameMargin.OffsetTop = 24f;
                _frameMargin.OffsetRight = -28f;
                _frameMargin.OffsetBottom = -24f;
            }
        }

        if (_sidebarContainer != null)
        {
            _sidebarContainer.CustomMinimumSize = new Vector2(compact ? 0f : 278f, 0f);
            _sidebarContainer.SizeFlagsHorizontal = compact ? SizeFlags.ExpandFill : SizeFlags.Fill;
        }

        if (_headerContentHost != null)
        {
            _headerContentHost.CustomMinimumSize = new Vector2(0f, compact ? 106f : 72f);
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
            _headerHealthPill.CustomMinimumSize = new Vector2(compact ? 148f : 206f, 0f);
        }

        if (_chooseDirectoryServerButton != null)
        {
            _chooseDirectoryServerButton.CustomMinimumSize = new Vector2(compact ? 154f : 174f, compact ? 50f : 54f);
            SetButtonText(_chooseDirectoryServerButton, "切换服务器");
        }

        if (_settingsButton != null)
        {
            _settingsButton.CustomMinimumSize = new Vector2(compact ? 50f : 54f, compact ? 50f : 54f);
        }

        if (_closeButton != null)
        {
            _closeButton.CustomMinimumSize = new Vector2(compact ? 50f : 54f, compact ? 50f : 54f);
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
        PanelContainer card = CreateGlassPanel(padding: 20);
        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 14);
        card.AddChild(body);

        body.AddChild(CreateSectionHeader("大厅状态", GlyphIconKind.Nodes));

        _statusHealthValueLabel = CreateMetricStatusRow(body, "服务状态", "等待探测");
        _statusLatencyValueLabel = CreateMetricRow(body, "延迟", "--");
        _statusRoomCountValueLabel = CreateMetricRow(body, "公开房间", "0");

        _statusLabel = CreateBodyLabel(string.Empty);
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _statusLabel.Visible = false;
        body.AddChild(_statusLabel);
        return card;
    }

    private Control BuildActionCard()
    {
        PanelContainer card = CreateGlassPanel(padding: 20);
        VBoxContainer body = new();
        body.AddThemeConstantOverride("separation", 12);
        card.AddChild(body);

        body.AddChild(CreateSectionHeader("快速操作", GlyphIconKind.InfoCircle));

        _createButton = CreateActionButton("创建房间", "先在本地起 ENet Host，再把房间发布到大厅。", () => TaskHelper.RunSafely(BeginCreateRoomFlowAsync()), primary: true, iconKind: GlyphIconKind.Plus);
        body.AddChild(_createButton);

        _joinButton = CreateActionButton("加入选中房间", "加入当前选中的房间，密码房会先弹出输入框。", JoinSelectedRoom, iconKind: GlyphIconKind.JoinArrow);
        body.AddChild(_joinButton);

        _refreshButton = CreateActionButton("刷新大厅", "立即抓取最新房间列表，并重置自动刷新计时。", () => TaskHelper.RunSafely(RefreshRoomsAsync(userInitiated: true)), iconKind: GlyphIconKind.Refresh);
        body.AddChild(_refreshButton);

        _closeRoomButton = CreateActionButton("关闭我的房间", "关闭当前托管中的大厅房间，并从房间列表里移除。", () => TaskHelper.RunSafely(CloseMyRoomAsync()), danger: true);
        _closeRoomButton.Visible = false;
        body.AddChild(_closeRoomButton);

        _actionAvailabilityLabel = CreateBodyLabel("操作状态会显示在这里。");
        _actionAvailabilityLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _actionAvailabilityLabel.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(_actionAvailabilityLabel);
        return card;
    }

    private Control BuildSupportCard()
    {
        PanelContainer card = CreateGlassPanel(padding: 22);
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

        Label description = CreateBodyLabel("房间会先在本地起 ENet Host，再向大厅注册。你可以在这里直接选择标准模式、多人每日挑战或自定义模式。当前入口只做房间目录与连接编排，不重写原生联机逻辑。");
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(description);

        body.AddChild(BuildLabeledInputRow("房间名", GetSuggestedRoomName(), out _roomNameInput, "房间列表里展示的名称", showLengthCounter: true, maxLength: LanConnectConfig.MaxRoomNameLength));
        body.AddChild(BuildLabeledOptionRow(
            "房间类型",
            out _roomTypeOption,
            ("标准模式", 0),
            ("多人每日挑战", 1),
            ("自定义模式", 2)));
        body.AddChild(BuildLabeledInputRow("可选密码", string.Empty, out _roomPasswordInput, "留空表示公开房间", showLengthCounter: true, maxLength: LanConnectConfig.MaxRoomPasswordLength));

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

        _progressDialogTitle = CreateSectionLabel("正在处理");
        body.AddChild(_progressDialogTitle);

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
        Control shell = CreateDialogShell(out VBoxContainer body, halfWidth: 420f, halfHeight: 280f);
        _directoryServerDialogContainer = shell;
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.SizeFlagsVertical = SizeFlags.ExpandFill;

        _directoryServerDialogTitle = CreateSectionLabel("选择中心服务器中的大厅");
        body.AddChild(_directoryServerDialogTitle);

        Label description = CreateBodyLabel("展示中心服务器返回的大厅。左右两列可滚动，点击即可切换。 ");
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(description);

        _directoryServerDialogStatusLabel = CreateBodyLabel("正在等待加载。 ");
        _directoryServerDialogStatusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _directoryServerDialogStatusLabel.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(_directoryServerDialogStatusLabel);

        ScrollContainer optionsScroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 280f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        ConfigureRoomListScroll(optionsScroll);
        body.AddChild(optionsScroll);

        _directoryServerDialogOptions = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _directoryServerDialogOptions.AddThemeConstantOverride("separation", 10);
        optionsScroll.AddChild(_directoryServerDialogOptions);

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

    private Control CreateDialogShell(out VBoxContainer body, float halfWidth = 300f, float halfHeight = 180f)
    {
        Control shell = new()
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        shell.SetAnchorsPreset(LayoutPreset.FullRect);

        ColorRect veil = new()
        {
            Color = new Color(0f, 0f, 0f, 0.55f),
            MouseFilter = MouseFilterEnum.Stop
        };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        shell.AddChild(veil);

        CenterContainer center = new();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        shell.AddChild(center);

        MarginContainer margin = new();
        margin.SetAnchorsPreset(LayoutPreset.Center);
        margin.OffsetLeft = -halfWidth;
        margin.OffsetTop = -halfHeight;
        margin.OffsetRight = halfWidth;
        margin.OffsetBottom = halfHeight;
        center.AddChild(margin);

        PanelContainer card = CreateSurfacePanel(FrameColor, AccentColor, radius: 20, padding: 22);
        card.CustomMinimumSize = new Vector2(Mathf.Max((halfWidth * 2f) - 40f, 560f), 0f);
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
            SetLabelText(_pageSummaryLabel, "第 0 / 0 页");
            if (_rooms.Count == 0)
            {
                _roomListContainer.AddChild(CreateEmptyRoomCard(
                    "大厅当前没有房间。",
                    "刷新大厅后，你也可以在右侧直接创建一个新的公开房间。"));
                SetLabelText(
                    _roomHintLabel,
                    HasAvailableLobbyEndpoint()
                        ? "你可以先刷新大厅，或者直接在右侧创建一个新的房间。"
                        : "当前客户端未绑定内置大厅服务。请在设置里填写 HTTP 覆盖地址。");
                _roomHintLabel.AddThemeColorOverride("font_color", HasAvailableLobbyEndpoint() ? AccentColor : DangerColor);
            }
            else
            {
                _roomListContainer.AddChild(CreateEmptyRoomCard(
                    "没有匹配结果。",
                    hasSearchOrFilter
                        ? "尝试缩短关键词，或取消部分筛选后重试。"
                        : "可检索字段包括房间名、房主名、版本和状态。"));
                SetLabelText(
                    _roomHintLabel,
                    hasSearchOrFilter
                        ? "尝试缩短关键词，或取消部分筛选后重新查看完整房间列表。"
                        : "尝试刷新大厅后重新查看完整房间列表。");
                _roomHintLabel.AddThemeColorOverride("font_color", AccentColor);
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

        if (selectedIsHostRoom)
        {
            SetLabelText(_roomHintLabel, "当前选中的是你自己托管的房间，无法重复加入；如需重开，请先关闭它。");
            _roomHintLabel.AddThemeColorOverride("font_color", SuccessColor);
        }
        else if (!CanJoinRoom(selectedRoom, out string? joinDisabledReason))
        {
            SetLabelText(_roomHintLabel, joinDisabledReason ?? "该房间当前不可加入。");
            _roomHintLabel.AddThemeColorOverride("font_color", DangerColor);
        }
        else if (selectedRoom.SavedRun != null)
        {
            int availableSlots = GetAvailableSavedRunSlots(selectedRoom).Count;
            SetLabelText(
                _roomHintLabel,
                availableSlots <= 0
                    ? "当前选中的是续局房间，但暂时没有可接管的角色槽位。"
                    : $"当前选中的是续局房间。可接管角色数：{availableSlots}；加入前会先让你选择角色槽位。");
            _roomHintLabel.AddThemeColorOverride("font_color", availableSlots <= 0 ? DangerColor : AccentColor);
        }
        else if (selectedRoom.RequiresPassword)
        {
            SetLabelText(_roomHintLabel, "当前选中房间需要密码。双击卡片或点击右侧“加入选中房间”后会先弹出密码输入框。");
            _roomHintLabel.AddThemeColorOverride("font_color", AccentColor);
        }
        else
        {
            SetLabelText(_roomHintLabel, "当前选中房间为公开房间。双击卡片可直接加入。");
            _roomHintLabel.AddThemeColorOverride("font_color", SuccessColor);
        }
    }

    private void ConfigureRoomListScroll(ScrollContainer scroll)
    {
        scroll.FollowFocus = false;
        scroll.MouseFilter = MouseFilterEnum.Stop;
        VScrollBar scrollbar = scroll.GetVScrollBar();
        scrollbar.CustomMinimumSize = new Vector2(20f, 0f);
        scrollbar.AddThemeStyleboxOverride("scroll", CreatePanelStyle(new Color(0.07f, 0.07f, 0.08f, 0.98f), new Color(AccentColor, 0.14f), radius: 999, borderWidth: 1, padding: 4));
        scrollbar.AddThemeStyleboxOverride("grabber", CreatePanelStyle(new Color(0.54f, 0.19f, 0.11f, 0.92f), new Color(AccentColor, 0.3f), radius: 999, borderWidth: 0, padding: 8));
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
        PanelContainer card = CreateSurfacePanel(new Color(0.059f, 0.047f, 0.039f, 0.72f), AccentMutedColor, radius: 18, borderWidth: 1, padding: 22);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.CustomMinimumSize = new Vector2(0f, 156f);

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
            return ("你的房间", SuccessColor, new Color(0.06f, 0.11f, 0.07f, 0.96f));
        }

        if (string.Equals(room.Status, "starting", StringComparison.OrdinalIgnoreCase))
        {
            return ("已开局", DangerColor, new Color(0.18f, 0.09f, 0.09f, 0.96f));
        }

        if (string.Equals(room.Status, "full", StringComparison.OrdinalIgnoreCase))
        {
            return ("已满", DangerColor, new Color(0.18f, 0.09f, 0.09f, 0.96f));
        }

        if (room.SavedRun != null)
        {
            int availableSlots = GetAvailableSavedRunSlots(room).Count;
            return availableSlots > 0
                ? ($"{availableSlots} 可接管", AccentColor, new Color(0.16f, 0.08f, 0.07f, 0.96f))
                : ("续局已满", DangerColor, new Color(0.18f, 0.09f, 0.09f, 0.96f));
        }

        if (room.RequiresPassword)
        {
            return ("已上锁", DangerColor, new Color(0.18f, 0.09f, 0.09f, 0.96f));
        }

        return ("可加入", SuccessColor, new Color(0.06f, 0.11f, 0.07f, 0.96f));
    }

    private static Color GetRoomLockColor(LobbyRoomSummary room)
    {
        return room.RequiresPassword ? DangerColor : SuccessColor;
    }

    private static (string Text, Color Border, Color Background) GetRoomGameModePill(string? gameMode)
    {
        return gameMode?.Trim().ToLowerInvariant() switch
        {
            "daily" => ("挑战", new Color(0.97f, 0.76f, 0.34f, 1f), new Color(0.15f, 0.10f, 0.05f, 0.96f)),
            "custom" => ("自定义", new Color(0.42f, 0.88f, 0.79f, 1f), new Color(0.06f, 0.11f, 0.10f, 0.96f)),
            _ => ("标准", new Color(0.58f, 0.77f, 0.97f, 1f), new Color(0.08f, 0.10f, 0.14f, 0.96f))
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
        label.AddThemeFontSizeOverride("font_size", 15);
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
        row.AddThemeConstantOverride("h_separation", 8);
        row.AddThemeConstantOverride("v_separation", 4);

        row.AddChild(CreateMetaLabel($"房主:{room.HostPlayerName}"));
        row.AddChild(CreateMetaSeparator());
        row.AddChild(CreateMetaLabel($"游戏:{room.Version}"));
        row.AddChild(CreateMetaSeparator());
        row.AddChild(CreateMetaLabel($"MOD:{room.ModVersion}"));
        row.AddChild(CreateMetaSeparator());

        HBoxContainer playerGroup = new()
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        playerGroup.AddThemeConstantOverride("separation", 4);
        row.AddChild(playerGroup);

        GlyphIcon peopleIcon = new()
        {
            Kind = GlyphIconKind.Person,
            GlyphColor = TextMutedColor,
            CustomMinimumSize = new Vector2(16f, 16f)
        };
        playerGroup.AddChild(peopleIcon);
        playerGroup.AddChild(CreateMetaLabel($"{room.CurrentPlayers}/{room.MaxPlayers}"));
        return row;
    }

    private Control CreateRoomCard(LobbyRoomSummary room, bool isSelected, bool isHostRoom)
    {
        (string pillText, Color pillBorder, Color pillBackground) = GetRoomPrimaryPill(room, isHostRoom);
        string? detailText = BuildRoomDetailLine(room, isHostRoom);
        Color background = isSelected
            ? new Color(0.108f, 0.052f, 0.043f, 0.88f)
            : new Color(0.055f, 0.043f, 0.037f, 0.76f);
        Color border = isSelected
            ? new Color(AccentBrightColor, 0.34f)
            : isHostRoom
                ? SuccessColor
                : AccentMutedColor;

        PanelContainer card = CreateSurfacePanel(background, border, radius: 18, borderWidth: 1, padding: 20);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.CustomMinimumSize = new Vector2(0f, string.IsNullOrWhiteSpace(detailText) ? 118f : 142f);
        card.MouseFilter = MouseFilterEnum.Stop;
        card.MouseDefaultCursorShape = CursorShape.PointingHand;

        HBoxContainer shell = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        shell.AddThemeConstantOverride("separation", 16);
        card.AddChild(shell);

        CenterContainer iconHost = new()
        {
            CustomMinimumSize = new Vector2(42f, 0f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        shell.AddChild(iconHost);

        RoomStateGlyph icon = new()
        {
            GlyphColor = GetRoomLockColor(room),
            Unlocked = !room.RequiresPassword
        };
        iconHost.AddChild(icon);

        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        body.AddThemeConstantOverride("separation", 9);
        shell.AddChild(body);

        HBoxContainer topRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        topRow.AddThemeConstantOverride("separation", 12);
        body.AddChild(topRow);

        Label title = CreateTitleLabel(FormatRoomName(room.RoomName, 40), 24);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.ClipText = true;
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        topRow.AddChild(title);

        HFlowContainer tagRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        tagRow.AddThemeConstantOverride("h_separation", 8);
        tagRow.AddThemeConstantOverride("v_separation", 6);
        body.AddChild(tagRow);

        if (isSelected)
        {
            tagRow.AddChild(CreateTagPill("已选中", AccentColor, new Color(0.18f, 0.08f, 0.06f, 0.94f)));
        }

        tagRow.AddChild(CreateTagPill(pillText, pillBorder, pillBackground));
        (string modeText, Color modeBorder, Color modeBackground) = GetRoomGameModePill(room.GameMode);
        tagRow.AddChild(CreateTagPill(modeText, modeBorder, modeBackground));

        body.AddChild(BuildRoomMetaRow(room));

        if (!string.IsNullOrWhiteSpace(detailText))
        {
            Label detailLine = CreateBodyLabel(detailText);
            detailLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            detailLine.AddThemeFontSizeOverride("font_size", 16);
            detailLine.AddThemeColorOverride("font_color", GetRoomDetailColor(room, isHostRoom));
            body.AddChild(detailLine);
        }

        ApplyPassiveMouseFilterRecursive(card);
        card.MouseFilter = MouseFilterEnum.Stop;
        card.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(inputEvent => OnRoomCardGuiInput(room, inputEvent)));
        return card;
    }

    private Control CreateTagPill(string text, Color border, Color background)
    {
        PanelContainer pill = CreateSurfacePanel(background, border, radius: 999, borderWidth: 1, padding: 8);

        Label label = CreateBodyLabel(text);
        label.AddThemeColorOverride("font_color", border == TextMutedColor ? TextStrongColor : border);
        label.AddThemeFontSizeOverride("font_size", 13);
        pill.AddChild(label);
        return pill;
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
            $"sts2_lan_connect overlay: create requested roomName='{roomName}', passwordSet={!string.IsNullOrWhiteSpace(password)}, gameMode={LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode)}, hasRunSave={SaveManager.Instance.HasMultiplayerRunSave}, hasActiveRoom={LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true}, endpointAvailable={HasAvailableLobbyEndpoint()}");
        if (string.IsNullOrWhiteSpace(roomName))
        {
            ShowCreateDialogError("请输入房间名。");
            return;
        }

        if (SaveManager.Instance.HasMultiplayerRunSave)
        {
            GD.Print("sts2_lan_connect overlay: create blocked by multiplayer continue save.");
            ShowCreateDialogError("检测到多人续局存档。请先点击官方“载入”进入该存档，mod 会自动恢复绑定的大厅房间。");
            return;
        }

        if (LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true)
        {
            GD.Print("sts2_lan_connect overlay: create blocked because hosted room is already active.");
            ShowCreateDialogError("你已经有一个大厅房间在托管中，先关闭它再重新建房。");
            return;
        }

        _actionInFlight = true;
        UpdateActionButtons();
        ShowCreateDialogError(string.Empty, visible: false);
        SetStatus($"正在创建{gameModeLabel}房间“{roomName}”...");

        try
        {
            bool created = await LanConnectHostFlow.StartLobbyHostAsync(roomName, password, gameMode, _loadingOverlay, _stack);
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
        _actionInFlight = true;
        UpdateActionButtons();
        SetStatus($"正在请求加入“{FormatRoomName(room.RoomName, 24)}”...");
        ShowProgressDialog(
            "正在加入房间",
            $"正在向大厅申请进入“{FormatRoomName(room.RoomName, 24)}”",
            "连接较慢时请稍候，期间不要重复点击按钮。");

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
            });

            UpdateProgressDialog(
                "正在建立联机连接",
                $"大厅已响应，正在连接“{FormatRoomName(room.RoomName, 24)}”",
                "如果房主在外网环境，首次握手通常会比刷新大厅更慢。");

            LobbyJoinAttemptResult joinResult = await LanConnectLobbyJoinFlow.JoinAsync(
                _stack,
                _loadingOverlay,
                joinResponse,
                desiredSavePlayerNetId,
                message => UpdateProgressDialog("正在建立联机连接", message));
            if (joinResult.Joined)
            {
                UpdateProgressDialog("正在进入房间", $"已连接“{FormatRoomName(room.RoomName, 24)}”，正在切换到联机界面");
                SetStatus($"已加入“{FormatRoomName(room.RoomName, 24)}”。");
                HideOverlay();
                return true;
            }

            string failureMessage = string.IsNullOrWhiteSpace(joinResult.FailureMessage)
                ? "请查看错误弹窗或连接日志。"
                : joinResult.FailureMessage;
            SetStatus($"加入“{FormatRoomName(room.RoomName, 24)}”失败：{failureMessage}");
            return false;
        }
        catch (LobbyServiceException ex)
        {
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
            SetStatus($"加入房间失败：{DescribeGenericJoinFailure(ex)}");
            return false;
        }
        finally
        {
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
            SetLabelText(_joinPasswordDialogTitle, $"输入“{FormatRoomName(room.RoomName, 24)}”的房间密码");

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
        SetLabelText(_resumeSlotDialogTitle, $"选择“{FormatRoomName(room.RoomName, 24)}”的可接管角色");

        foreach (Node child in _resumeSlotDialogOptions.GetChildren())
        {
            _resumeSlotDialogOptions.RemoveChild(child);
            child.QueueFree();
        }

        foreach (LobbySavedRunSlot slot in availableSlots)
        {
            string selectedNetId = slot.NetId;
            Button option = CreateActionButton(
                string.IsNullOrWhiteSpace(slot.CharacterName) ? slot.CharacterId : slot.CharacterName,
                $"接管该续局角色并使用已保存的玩家槽位加入。存档角色 ID：{slot.CharacterId}",
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

    private async Task OpenDirectoryServerDialogAsync()
    {
        if (_directoryServerDialogContainer == null)
        {
            return;
        }

        _directoryServerDialogContainer.Visible = true;
        _directoryServerDialogContainer.MoveToFront();
        await RefreshDirectoryServersAsync();
    }

    private void CloseDirectoryServerDialog()
    {
        if (_directoryServerDialogContainer != null)
        {
            _directoryServerDialogContainer.Visible = false;
        }
    }

    private async Task RefreshDirectoryServersAsync()
    {
        if (_directoryServerLoadInFlight || _directoryServerDialogOptions == null || _directoryServerDialogStatusLabel == null)
        {
            return;
        }

        _directoryServerLoadInFlight = true;
        SetLabelText(_directoryServerDialogStatusLabel, "正在从中心服务器拉取大厅列表...");
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
                SetLabelText(_directoryServerDialogStatusLabel, "中心服务器当前没有可用大厅。可稍后重试，或继续手动填写 HTTP 覆盖地址。");
                return;
            }

            SetLabelText(_directoryServerDialogStatusLabel, $"已找到 {servers.Count} 个可用大厅。左右两列可滚动，点击条目即可切换。 ");
            HBoxContainer? row = null;
            int rowColumnIndex = 0;
            foreach (LobbyDirectoryServerEntry entry in servers)
            {
                string guardStatus = FormatCreateRoomGuardStatus(entry.CreateRoomGuardStatus);
                string utilization = FormatUtilizationValue(entry.BandwidthUtilizationRatio);
                string title = entry.ServerName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title))
                {
                    if (Uri.TryCreate(entry.BaseUrl, UriKind.Absolute, out Uri? parsedUri) && !string.IsNullOrWhiteSpace(parsedUri.Host))
                    {
                        title = parsedUri.Host;
                    }
                    else
                    {
                        title = entry.BaseUrl;
                    }
                }

                string brief = $"房间 {entry.Rooms}  ·  新建 {guardStatus}  ·  负载 {utilization}";
                string tooltip =
                    $"{title}\n" +
                    $"{entry.BaseUrl}\n" +
                    $"{brief}\n" +
                    $"实时带宽：{FormatBandwidthValue(entry.CurrentBandwidthMbps)}  ·  有效容量：{FormatBandwidthValue(entry.ResolvedCapacityMbps ?? entry.BandwidthCapacityMbps)}\n" +
                    $"最后验证：{entry.LastVerifiedAt:yyyy-MM-dd HH:mm:ss}";

                if (row == null || rowColumnIndex == 0)
                {
                    row = new HBoxContainer
                    {
                        SizeFlagsHorizontal = SizeFlags.ExpandFill
                    };
                    row.AddThemeConstantOverride("separation", 10);
                    _directoryServerDialogOptions.AddChild(row);
                    rowColumnIndex = 0;
                }

                Button option = CreateActionButton(title, tooltip, () => ApplyDirectoryServer(entry), primary: false);
                option.Text = UiText($"{title}  ·  {brief}");
                option.Alignment = HorizontalAlignment.Left;
                option.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                option.CustomMinimumSize = new Vector2(0f, 50f);
                row.AddChild(option);

                rowColumnIndex++;
                if (rowColumnIndex >= 2)
                {
                    rowColumnIndex = 0;
                }
            }

            if (row != null && rowColumnIndex == 1)
            {
                Control spacer = new()
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                row.AddChild(spacer);
            }
        }
        catch (Exception ex)
        {
            SetLabelText(_directoryServerDialogStatusLabel, $"拉取中心服务器列表失败：{ex.Message}");
        }
        finally
        {
            _directoryServerLoadInFlight = false;
        }
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

        if (_registryBaseUrlInput != null)
        {
            LanConnectConfig.LobbyRegistryBaseUrl = _registryBaseUrlInput.Text.Trim();
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

        if (_registryBaseUrlInput != null)
        {
            _registryBaseUrlInput.Text = LanConnectConfig.LobbyRegistryBaseUrlOverride;
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

        if (_registryBaseUrlInput != null)
        {
            _registryBaseUrlInput.Secret = !_networkFieldsRevealed;
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

        if (_registryBaseUrlInput != null)
        {
            _registryBaseUrlInput.Text = string.Empty;
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
            ? "检测到多人续局存档。请先走官方“载入”入口，进入后会自动恢复绑定的大厅房间。"
            : null;
        string? joinDisabledReason = GetJoinDisabledReason(actionBusy, selectedRoom, selectedIsHostRoom);

        if (_refreshButton != null)
        {
            _refreshButton.Disabled = refreshBusy || actionBusy;
        }

        if (_createButton != null)
        {
            _createButton.Disabled = createDisabledReason != null;
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
                || !string.IsNullOrWhiteSpace(_registryBaseUrlInput?.Text);
            _clearNetworkOverridesButton.Disabled = !(LanConnectConfig.HasLobbyServerOverrides || hasOverrideText);
        }

        if (_actionAvailabilityLabel != null)
        {
            List<string> lines = new();
            lines.Add(createDisabledReason == null ? "创建房间：可用。" : $"创建房间：不可用，{createDisabledReason}");
            if (createWarning != null)
            {
                lines.Add(createWarning);
            }

            lines.Add(joinDisabledReason == null ? "加入选中房间：可用。" : $"加入选中房间：不可用，{joinDisabledReason}");
            if (refreshBusy)
            {
                lines.Add("大厅列表刷新中；刷新不会再单独锁死建房按钮。");
            }

            SetLabelText(_actionAvailabilityLabel, string.Join("\n", lines));
            _actionAvailabilityLabel.AddThemeColorOverride(
                "font_color",
                createDisabledReason != null || joinDisabledReason != null
                    ? DangerColor
                    : createWarning != null || refreshBusy
                        ? AccentColor
                        : SuccessColor);
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
                ? "第 0 / 0 页"
                : $"第 {_currentPageIndex + 1} / {totalPages} 页");

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
            statusText = "未绑定大厅";
            latencyText = "--";
            color = DangerColor;
        }
        else if (_consecutiveRefreshFailures >= 2)
        {
            statusText = "连接异常";
            latencyText = "切服";
            color = DangerColor;
        }
        else if (_consecutiveRefreshFailures == 1)
        {
            statusText = "最近失败";
            latencyText = "重试";
            color = AccentColor;
        }
        else if (_lastLobbyRttMs < 0d)
        {
            statusText = _rooms.Count > 0 ? "等待探测" : "连接检查中";
            latencyText = "--";
            color = AccentColor;
        }
        else if (_lastLobbyRttMs <= 600d)
        {
            statusText = "服务正常";
            latencyText = $"{_lastLobbyRttMs:0}ms";
            color = SuccessColor;
        }
        else if (_lastLobbyRttMs <= 1500d)
        {
            statusText = "延迟偏高";
            latencyText = $"{_lastLobbyRttMs:0}ms";
            color = AccentColor;
        }
        else
        {
            statusText = "延迟过高";
            latencyText = $"{_lastLobbyRttMs:0}ms";
            color = DangerColor;
        }

        SetLabelText(_healthIndicatorLabel, statusText);
        _healthIndicatorLabel.AddThemeColorOverride("font_color", TextStrongColor);
        SetLabelText(_healthIndicatorLatencyLabel, latencyText);
        _healthIndicatorLatencyLabel?.AddThemeColorOverride("font_color", color);
        _healthIndicatorDotColor = color;
        if (_healthIndicatorDot != null)
        {
            _healthIndicatorDot.SelfModulate = color;
        }

        if (_statusHealthValueLabel != null)
        {
            string healthText = !HasAvailableLobbyEndpoint()
                ? "未绑定"
                : _consecutiveRefreshFailures >= 2
                    ? "连接异常"
                    : _consecutiveRefreshFailures == 1
                        ? "最近失败"
                        : _lastLobbyRttMs < 0d
                            ? "等待探测"
                            : _lastLobbyRttMs <= 600d
                                ? "服务正常"
                                : _lastLobbyRttMs <= 1500d
                                    ? "延迟偏高"
                                    : "延迟过高";
            SetLabelText(_statusHealthValueLabel, healthText);
            _statusHealthValueLabel.AddThemeColorOverride("font_color", color);
        }

        if (_statusHealthValueIcon != null)
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
            summary = "当前网络：已启用手动覆盖地址。大厅与中心服务器覆盖值默认遮罩显示，不会回显打包默认地址。";
            color = AccentColor;
        }
        else if (LanConnectLobbyEndpointDefaults.HasBundledDefaults())
        {
            summary = "当前网络：使用打包内置大厅服务和默认中心服务器。默认地址仅在运行时读取，不会写进 config.json，也不会在这里明文显示。";
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

    private void ShowProgressDialog(string title, string message, string? hint = null)
    {
        if (_progressDialogContainer == null)
        {
            return;
        }

        SetProgressDialogContent(title, message, hint);
        _progressDialogContainer.Visible = true;
        _progressDialogContainer.MoveToFront();
        GD.Print($"sts2_lan_connect overlay: progress dialog shown -> {title} | {message}");
    }

    private void UpdateProgressDialog(string title, string message, string? hint = null)
    {
        if (_progressDialogContainer == null || !_progressDialogContainer.Visible)
        {
            return;
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
            return "检测到多人续局存档，请先点击官方“载入”。";
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
            "room_started" => "房间已经开始游戏，不能再加入。",
            "room_full" => "房间已经满员。",
            "room_closed" => "房间已经关闭。",
            "relay_host_not_ready" => "房主 relay 尚未注册完成，请稍后刷新后再试。",
            "version_mismatch" => ex.Message,
            "mod_mismatch" => LanConnectLobbyModMismatchFormatter.FormatFromDetails(ex.Details, ex.Message),
            "mod_version_mismatch" => LanConnectLobbyModMismatchFormatter.FormatFromDetails(ex.Details, ex.Message),
            "invalid_password" => "房间密码错误。",
            "save_slot_required" => "这是续局房间，需要先选择一个可接管角色。",
            "save_slot_invalid" => "所选续局角色不存在。",
            "save_slot_unavailable" => "所选续局角色已被其他玩家接管，或当前没有可接管角色。",
            _ => ex.Message
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
            CustomMinimumSize = new Vector2(0f, 50f),
            Alignment = HorizontalAlignment.Center
        };
        ApplyButtonStyle(button, primary, danger);
        if (iconKind != GlyphIconKind.None)
        {
            AttachActionButtonContent(button, iconKind, text, primary
                ? new Color(0.1f, 0.08f, 0.06f, 1f)
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
            CustomMinimumSize = new Vector2(174f, 54f),
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
            CustomMinimumSize = new Vector2(54f, 54f),
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

    private static void AttachToolbarIconContent(Button button, GlyphIconKind iconKind)
    {
        CenterContainer center = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        button.AddChild(center);
        center.AddChild(new GlyphIcon
        {
            Kind = iconKind,
            GlyphColor = TextStrongColor,
            CustomMinimumSize = new Vector2(19f, 19f)
        });
    }

    private static PanelContainer CreateGlassPanel(int padding = 18, int radius = 20)
    {
        return CreateSurfacePanel(new Color(0.059f, 0.047f, 0.039f, 0.72f), new Color(AccentColor, 0.14f), radius: radius, borderWidth: 1, padding: padding);
    }

    private static PanelContainer CreateSurfacePanel(Color background, Color border, int radius = 18, int borderWidth = 1, int padding = 18)
    {
        PanelContainer panel = new()
        {
            ClipContents = true
        };
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, border, radius, borderWidth, padding, shadowSize: 6, shadowColor: WithAlpha(border, 0.03f)));
        AddPanelChrome(panel, border);
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
        style.ShadowOffset = Vector2.Zero;
        return style;
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
        Color normal = primary
            ? new Color(0.953f, 0.631f, 0.227f, 1f)
            : danger
                ? new Color(0.34f, 0.1f, 0.1f, 0.98f)
                : new Color(0.059f, 0.047f, 0.039f, 0.22f);
        Color hover = primary
            ? new Color(0.99f, 0.69f, 0.29f, 1f)
            : danger
                ? new Color(0.46f, 0.14f, 0.14f, 1f)
                : new Color(0.059f, 0.047f, 0.039f, 0.34f);
        Color pressed = primary
            ? new Color(0.88f, 0.54f, 0.16f, 1f)
            : danger
                ? new Color(0.28f, 0.09f, 0.09f, 1f)
                : new Color(0.059f, 0.047f, 0.039f, 0.42f);
        Color border = danger ? new Color(DangerColor, 0.32f) : new Color(AccentColor, primary ? 0.3f : 0.18f);

        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(normal, border, radius: 14, borderWidth: 1, padding: 14, shadowSize: primary ? 10 : 0, shadowColor: primary ? WithAlpha(AccentColor, 0.06f) : null));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(hover, danger ? new Color(DangerColor, 0.4f) : new Color(AccentBrightColor, 0.36f), radius: 14, borderWidth: 1, padding: 14, shadowSize: primary ? 10 : 0, shadowColor: primary ? WithAlpha(AccentBrightColor, 0.07f) : null));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(pressed, danger ? new Color(DangerColor, 0.42f) : new Color(AccentColor, 0.34f), radius: 14, borderWidth: 1, padding: 14, shadowSize: primary ? 8 : 0, shadowColor: primary ? WithAlpha(AccentColor, 0.05f) : null));
        button.AddThemeStyleboxOverride("disabled", CreatePanelStyle(WithAlpha(normal, 0.45f), WithAlpha(border, 0.24f), radius: 14, borderWidth: 1, padding: 14));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(hover, new Color(AccentBrightColor, 0.36f), radius: 14, borderWidth: 1, padding: 14, shadowSize: primary ? 10 : 0, shadowColor: primary ? WithAlpha(AccentBrightColor, 0.07f) : null));
        button.AddThemeColorOverride("font_color", primary ? new Color(0.1f, 0.08f, 0.06f, 1f) : (danger ? TextStrongColor : AccentColor));
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 16);
    }

    private static void ApplyToolbarButtonStyle(Button button, bool accent, bool iconOnly)
    {
        Color border = accent ? new Color(AccentBrightColor, 0.26f) : AccentMutedColor;
        Color normal = accent
            ? new Color(0.059f, 0.047f, 0.039f, 0.9f)
            : new Color(0.059f, 0.047f, 0.039f, 0.82f);
        Color hover = accent
            ? new Color(0.059f, 0.047f, 0.039f, 0.96f)
            : new Color(0.059f, 0.047f, 0.039f, 0.92f);

        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(normal, border, radius: 16, borderWidth: 1, padding: iconOnly ? 10 : 14));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(hover, new Color(AccentBrightColor, 0.32f), radius: 16, borderWidth: 1, padding: iconOnly ? 10 : 14, shadowSize: 6, shadowColor: WithAlpha(AccentColor, 0.05f)));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(normal, new Color(AccentBrightColor, 0.32f), radius: 16, borderWidth: 1, padding: iconOnly ? 10 : 14));
        button.AddThemeStyleboxOverride("disabled", CreatePanelStyle(WithAlpha(normal, 0.45f), WithAlpha(border, 0.2f), radius: 16, borderWidth: 1, padding: iconOnly ? 10 : 14));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(hover, new Color(AccentBrightColor, 0.32f), radius: 16, borderWidth: 1, padding: iconOnly ? 10 : 14));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", iconOnly ? 18 : 15);
    }

    private static void ApplyInlineButtonStyle(Button button, bool accent)
    {
        Color border = accent ? new Color(AccentColor, 0.24f) : AccentMutedColor;
        Color normal = accent
            ? new Color(0.059f, 0.047f, 0.039f, 0.9f)
            : new Color(0.059f, 0.047f, 0.039f, 0.82f);
        Color hover = accent
            ? new Color(0.059f, 0.047f, 0.039f, 0.96f)
            : new Color(0.059f, 0.047f, 0.039f, 0.92f);

        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(normal, border, radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(hover, new Color(AccentBrightColor, 0.28f), radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(normal, new Color(AccentColor, 0.28f), radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("disabled", CreatePanelStyle(WithAlpha(normal, 0.45f), WithAlpha(border, 0.2f), radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(hover, new Color(AccentBrightColor, 0.28f), radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 15);
    }

    private void UpdateRoomFilterButtons()
    {
        if (_roomFilterMenuButton != null)
        {
            ApplyInlineButtonStyle(_roomFilterMenuButton, HasRoomSearchOrFilter());
            _roomFilterMenuButton.TooltipText = UiText($"当前筛选：{DescribeRoomFilterState()}");
            PopupMenu popup = _roomFilterMenuButton.GetPopup();
            SyncPopupCheckState(popup, FilterPublicId, _showPublicRooms);
            SyncPopupCheckState(popup, FilterLockedId, _showLockedRooms);
            SyncPopupCheckState(popup, FilterJoinableId, _joinableOnlyFilter);
            SyncPopupCheckState(popup, FilterModeStandardId, _showStandardMode);
            SyncPopupCheckState(popup, FilterModeDailyId, _showDailyMode);
            SyncPopupCheckState(popup, FilterModeCustomId, _showCustomMode);
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
        input.AddThemeColorOverride("font_placeholder_color", new Color(TextMutedColor, 0.72f));
        input.AddThemeColorOverride("caret_color", AccentColor);
        input.AddThemeFontSizeOverride("font_size", 15);
    }

    private static void ApplyFilterChipStyle(Button button, bool active)
    {
        Color normal = active
            ? new Color(0.24f, 0.09f, 0.06f, 0.98f)
            : new Color(0.059f, 0.047f, 0.039f, 0.78f);
        Color hover = active
            ? new Color(0.3f, 0.12f, 0.07f, 1f)
            : new Color(0.059f, 0.047f, 0.039f, 0.9f);
        Color border = active ? new Color(AccentColor, 0.28f) : AccentMutedColor;

        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(normal, border, radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(hover, new Color(AccentBrightColor, 0.3f), radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(normal, new Color(AccentColor, 0.3f), radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("disabled", CreatePanelStyle(WithAlpha(normal, 0.45f), WithAlpha(border, 0.2f), radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(hover, new Color(AccentBrightColor, 0.3f), radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeColorOverride("font_color", active ? TextStrongColor : TextMutedColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 15);
    }

    private static void ApplyInputStyle(LineEdit input)
    {
        input.CustomMinimumSize = new Vector2(0f, 48f);
        input.AddThemeStyleboxOverride("normal", CreatePanelStyle(new Color(0.059f, 0.047f, 0.039f, 0.82f), AccentMutedColor, radius: 12, borderWidth: 1, padding: 12));
        input.AddThemeStyleboxOverride("focus", CreatePanelStyle(new Color(0.059f, 0.047f, 0.039f, 0.94f), new Color(AccentBrightColor, 0.28f), radius: 12, borderWidth: 1, padding: 12));
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_placeholder_color", new Color(TextMutedColor, 0.72f));
        input.AddThemeColorOverride("caret_color", AccentColor);
        input.AddThemeFontSizeOverride("font_size", 16);
    }

    private static void ApplyInputStyle(OptionButton input)
    {
        input.CustomMinimumSize = new Vector2(0f, 48f);
        input.AddThemeStyleboxOverride("normal", CreatePanelStyle(new Color(0.059f, 0.047f, 0.039f, 0.82f), AccentMutedColor, radius: 12, borderWidth: 1, padding: 12));
        input.AddThemeStyleboxOverride("hover", CreatePanelStyle(new Color(0.059f, 0.047f, 0.039f, 0.94f), new Color(AccentBrightColor, 0.28f), radius: 12, borderWidth: 1, padding: 12));
        input.AddThemeStyleboxOverride("pressed", CreatePanelStyle(new Color(0.059f, 0.047f, 0.039f, 0.94f), new Color(AccentBrightColor, 0.28f), radius: 12, borderWidth: 1, padding: 12));
        input.AddThemeStyleboxOverride("focus", CreatePanelStyle(new Color(0.059f, 0.047f, 0.039f, 0.94f), new Color(AccentBrightColor, 0.28f), radius: 12, borderWidth: 1, padding: 12));
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_hover_color", TextStrongColor);
        input.AddThemeColorOverride("font_pressed_color", TextStrongColor);
        input.AddThemeColorOverride("font_focus_color", TextStrongColor);
        input.AddThemeColorOverride("modulate_arrow", AccentColor);
        input.AddThemeFontSizeOverride("font_size", 16);
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

        _statusHealthValueIcon = new GlyphIcon
        {
            Kind = GlyphIconKind.Wifi,
            GlyphColor = SuccessColor,
            CustomMinimumSize = new Vector2(16f, 16f),
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
        Refresh
    }

    private sealed partial class GlyphIcon : Control
    {
        public GlyphIconKind Kind { get; init; }

        public Color GlyphColor { get; init; } = Colors.White;

        public float StrokeWidth { get; init; } = 2f;

        public GlyphIcon()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            switch (Kind)
            {
                case GlyphIconKind.None:
                    break;
                case GlyphIconKind.Wifi:
                    DrawWifi();
                    break;
                case GlyphIconKind.Server:
                    DrawServer();
                    break;
                case GlyphIconKind.Gear:
                    DrawGear();
                    break;
                case GlyphIconKind.Back:
                    DrawBackArrow();
                    break;
                case GlyphIconKind.Person:
                    DrawPerson();
                    break;
                case GlyphIconKind.Search:
                    DrawSearch();
                    break;
                case GlyphIconKind.Nodes:
                    DrawNodes();
                    break;
                case GlyphIconKind.InfoCircle:
                    DrawInfoCircle();
                    break;
                case GlyphIconKind.Plus:
                    DrawPlus();
                    break;
                case GlyphIconKind.JoinArrow:
                    DrawJoinArrow();
                    break;
                case GlyphIconKind.Refresh:
                    DrawRefresh();
                    break;
            }
        }

        private void DrawWifi()
        {
            Vector2 center = new(Size.X * 0.5f, Size.Y * 0.72f);
            DrawArc(center, Size.X * 0.38f, Mathf.Pi * 1.12f, Mathf.Pi * 1.88f, 18, GlyphColor, StrokeWidth, true);
            DrawArc(center, Size.X * 0.26f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 16, GlyphColor, StrokeWidth, true);
            DrawArc(center, Size.X * 0.14f, Mathf.Pi * 1.18f, Mathf.Pi * 1.82f, 12, GlyphColor, StrokeWidth, true);
            DrawCircle(new Vector2(Size.X * 0.5f, Size.Y * 0.78f), 1.8f, GlyphColor);
        }

        private void DrawServer()
        {
            float left = Size.X * 0.14f;
            float width = Size.X * 0.72f;
            float height = Size.Y * 0.22f;
            Rect2 top = new(new Vector2(left, Size.Y * 0.2f), new Vector2(width, height));
            Rect2 bottom = new(new Vector2(left, Size.Y * 0.58f), new Vector2(width, height));
            DrawRect(top, Colors.Transparent, false, StrokeWidth, true);
            DrawRect(bottom, Colors.Transparent, false, StrokeWidth, true);
            DrawCircle(new Vector2(top.Position.X + width * 0.2f, top.GetCenter().Y), 1.3f, GlyphColor);
            DrawCircle(new Vector2(bottom.Position.X + width * 0.2f, bottom.GetCenter().Y), 1.3f, GlyphColor);
            DrawLine(new Vector2(top.Position.X + width * 0.38f, top.GetCenter().Y), new Vector2(top.End.X - 3f, top.GetCenter().Y), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(bottom.Position.X + width * 0.38f, bottom.GetCenter().Y), new Vector2(bottom.End.X - 3f, bottom.GetCenter().Y), GlyphColor, StrokeWidth, true);
        }

        private void DrawGear()
        {
            Vector2 center = Size * 0.5f;
            float outerRadius = Size.X * 0.28f;
            float innerRadius = Size.X * 0.13f;
            for (int index = 0; index < 8; index++)
            {
                float angle = index / 8f * Mathf.Tau;
                Vector2 start = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * outerRadius;
                Vector2 end = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (outerRadius + 3f);
                DrawLine(start, end, GlyphColor, StrokeWidth, true);
            }

            DrawArc(center, outerRadius, 0f, Mathf.Tau, 28, GlyphColor, StrokeWidth, true);
            DrawArc(center, innerRadius, 0f, Mathf.Tau, 24, GlyphColor, StrokeWidth, true);
        }

        private void DrawBackArrow()
        {
            Vector2 center = new(Size.X * 0.55f, Size.Y * 0.5f);
            DrawLine(new Vector2(Size.X * 0.72f, center.Y), new Vector2(Size.X * 0.28f, center.Y), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.28f, center.Y), new Vector2(Size.X * 0.46f, Size.Y * 0.32f), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.28f, center.Y), new Vector2(Size.X * 0.46f, Size.Y * 0.68f), GlyphColor, StrokeWidth, true);
        }

        private void DrawPerson()
        {
            Vector2 headCenter = new(Size.X * 0.5f, Size.Y * 0.32f);
            DrawArc(headCenter, Size.X * 0.16f, 0f, Mathf.Tau, 18, GlyphColor, StrokeWidth, true);
            DrawArc(new Vector2(Size.X * 0.5f, Size.Y * 0.84f), Size.X * 0.28f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 18, GlyphColor, StrokeWidth, true);
        }

        private void DrawSearch()
        {
            Vector2 center = new(Size.X * 0.42f, Size.Y * 0.42f);
            float radius = Size.X * 0.22f;
            DrawArc(center, radius, 0f, Mathf.Tau, 20, GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(center.X + radius * 0.7f, center.Y + radius * 0.7f), new Vector2(Size.X * 0.84f, Size.Y * 0.84f), GlyphColor, StrokeWidth, true);
        }

        private void DrawNodes()
        {
            Rect2 top = new(new Vector2(Size.X * 0.38f, Size.Y * 0.08f), new Vector2(Size.X * 0.24f, Size.Y * 0.24f));
            Rect2 left = new(new Vector2(Size.X * 0.12f, Size.Y * 0.62f), new Vector2(Size.X * 0.24f, Size.Y * 0.24f));
            Rect2 right = new(new Vector2(Size.X * 0.64f, Size.Y * 0.62f), new Vector2(Size.X * 0.24f, Size.Y * 0.24f));
            DrawRect(top, Colors.Transparent, false, StrokeWidth, true);
            DrawRect(left, Colors.Transparent, false, StrokeWidth, true);
            DrawRect(right, Colors.Transparent, false, StrokeWidth, true);
            DrawLine(top.GetCenter() + new Vector2(0f, top.Size.Y * 0.5f), left.GetCenter() - new Vector2(0f, left.Size.Y * 0.5f), GlyphColor, StrokeWidth, true);
            DrawLine(top.GetCenter() + new Vector2(0f, top.Size.Y * 0.5f), right.GetCenter() - new Vector2(0f, right.Size.Y * 0.5f), GlyphColor, StrokeWidth, true);
        }

        private void DrawInfoCircle()
        {
            Vector2 center = Size * 0.5f;
            float radius = Size.X * 0.34f;
            DrawArc(center, radius, 0f, Mathf.Tau, 24, GlyphColor, StrokeWidth, true);
            DrawCircle(new Vector2(center.X, Size.Y * 0.3f), 1.5f, GlyphColor);
            DrawLine(new Vector2(center.X, Size.Y * 0.42f), new Vector2(center.X, Size.Y * 0.68f), GlyphColor, StrokeWidth, true);
        }

        private void DrawPlus()
        {
            Vector2 center = Size * 0.5f;
            DrawLine(new Vector2(center.X, Size.Y * 0.22f), new Vector2(center.X, Size.Y * 0.78f), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.22f, center.Y), new Vector2(Size.X * 0.78f, center.Y), GlyphColor, StrokeWidth, true);
        }

        private void DrawJoinArrow()
        {
            float midY = Size.Y * 0.52f;
            DrawLine(new Vector2(Size.X * 0.16f, midY), new Vector2(Size.X * 0.72f, midY), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.56f, Size.Y * 0.28f), new Vector2(Size.X * 0.72f, midY), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.56f, Size.Y * 0.76f), new Vector2(Size.X * 0.72f, midY), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.18f, Size.Y * 0.24f), new Vector2(Size.X * 0.18f, Size.Y * 0.44f), GlyphColor, StrokeWidth, true);
        }

        private void DrawRefresh()
        {
            Vector2 center = Size * 0.5f;
            float radius = Size.X * 0.28f;
            DrawArc(center, radius, Mathf.Pi * 0.22f, Mathf.Pi * 1.4f, 18, GlyphColor, StrokeWidth, true);
            DrawArc(center, radius, Mathf.Pi * 1.22f, Mathf.Pi * 2.38f, 18, GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.73f, Size.Y * 0.18f), new Vector2(Size.X * 0.83f, Size.Y * 0.18f), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.83f, Size.Y * 0.18f), new Vector2(Size.X * 0.8f, Size.Y * 0.31f), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.27f, Size.Y * 0.82f), new Vector2(Size.X * 0.17f, Size.Y * 0.82f), GlyphColor, StrokeWidth, true);
            DrawLine(new Vector2(Size.X * 0.17f, Size.Y * 0.82f), new Vector2(Size.X * 0.2f, Size.Y * 0.69f), GlyphColor, StrokeWidth, true);
        }
    }

    private sealed partial class RoomStateGlyph : Control
    {
        public Color GlyphColor { get; init; } = Colors.White;

        public bool Unlocked { get; init; } = true;

        public RoomStateGlyph()
        {
            CustomMinimumSize = new Vector2(30f, 30f);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            const float strokeWidth = 2.6f;
            Rect2 bodyRect = new(new Vector2(7f, 14f), new Vector2(16f, 11f));
            DrawRect(bodyRect, Colors.Transparent, false, strokeWidth, true);

            if (Unlocked)
            {
                DrawLine(new Vector2(9f, 14f), new Vector2(9f, 11.4f), GlyphColor, strokeWidth, true);
                DrawArc(new Vector2(14f, 13.6f), 5.8f, Mathf.Pi * 1.04f, Mathf.Pi * 1.6f, 20, GlyphColor, strokeWidth, true);
                DrawLine(new Vector2(17.8f, 9.4f), new Vector2(21.8f, 6.2f), GlyphColor, strokeWidth, true);
            }
            else
            {
                DrawArc(new Vector2(15f, 14f), 5.8f, Mathf.Pi, Mathf.Tau, 22, GlyphColor, strokeWidth, true);
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
        float scale = 1f + 0.1f * (1f - wave);
        _healthIndicatorDot.SelfModulate = WithAlpha(_healthIndicatorDotColor, opacity);
        _healthIndicatorDot.Scale = new Vector2(scale, scale);
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
