using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectLobbyOverlay : Control
{
    private enum RoomAccessFilter
    {
        All,
        Public,
        Locked
    }

    private const float RoomListWheelStep = 120f;
    private const float RoomListTouchDragThreshold = 12f;

    private static readonly Color BackdropColor = new(0.01f, 0.01f, 0.02f, 0.94f);
    private static readonly Color FrameColor = new(0.07f, 0.07f, 0.08f, 0.96f);
    private static readonly Color SurfaceColor = new(0.1f, 0.1f, 0.12f, 0.94f);
    private static readonly Color SurfaceMutedColor = new(0.08f, 0.08f, 0.09f, 0.94f);
    private static readonly Color AccentColor = new(0.86f, 0.69f, 0.33f, 1f);
    private static readonly Color AccentMutedColor = new(0.46f, 0.36f, 0.17f, 1f);
    private static readonly Color TextStrongColor = new(0.96f, 0.94f, 0.88f, 1f);
    private static readonly Color TextMutedColor = new(0.76f, 0.74f, 0.69f, 1f);
    private static readonly Color SuccessColor = new(0.63f, 0.83f, 0.58f, 1f);
    private static readonly Color DangerColor = new(0.94f, 0.46f, 0.43f, 1f);

    private readonly List<LobbyRoomSummary> _rooms = new();

    private NMultiplayerSubmenu? _submenu;
    private NSubmenuStack? _stack;
    private Control? _loadingOverlay;
    private HSeparator? _settingsSeparator;
    private VBoxContainer? _settingsSection;
    private Label? _networkSummaryLabel;
    private LineEdit? _displayNameInput;
    private VBoxContainer? _networkSettingsContainer;
    private Button? _toggleNetworkSettingsButton;
    private Button? _toggleSensitiveNetworkButton;
    private Button? _clearNetworkOverridesButton;
    private LineEdit? _serverBaseUrlInput;
    private LineEdit? _serverWsUrlInput;
    private Label? _statusLabel;
    private Label? _healthIndicatorLabel;
    private Label? _roomListSummaryLabel;
    private Label? _pageSummaryLabel;
    private ScrollContainer? _roomListScroll;
    private VBoxContainer? _roomListContainer;
    private Label? _roomHintLabel;
    private LineEdit? _roomSearchInput;
    private Label? _actionAvailabilityLabel;
    private Button? _refreshButton;
    private Button? _createButton;
    private Button? _joinButton;
    private Button? _pagePreviousButton;
    private Button? _pageNextButton;
    private Button? _roomFilterPublicButton;
    private Button? _roomFilterLockedButton;
    private Button? _roomFilterJoinableButton;
    private Button? _closeRoomButton;
    private Button? _closeButton;
    private Button? _settingsButton;
    private Button? _repairSaveButton;
    private Button? _copyDebugReportButton;
    private Control? _createDialogContainer;
    private Label? _createDialogErrorLabel;
    private LineEdit? _roomNameInput;
    private LineEdit? _roomPasswordInput;
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
    private RoomAccessFilter _roomAccessFilter = RoomAccessFilter.All;
    private bool _joinableOnlyFilter;

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

        if (_joinPasswordDialogContainer != null)
        {
            _joinPasswordDialogContainer.Visible = false;
        }

        if (_resumeSlotDialogContainer != null)
        {
            _resumeSlotDialogContainer.Visible = false;
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

        ColorRect backdrop = new()
        {
            Color = BackdropColor,
            MouseFilter = MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        MarginContainer frameMargin = new();
        frameMargin.SetAnchorsPreset(LayoutPreset.FullRect);
        frameMargin.OffsetLeft = 44f;
        frameMargin.OffsetTop = 36f;
        frameMargin.OffsetRight = -44f;
        frameMargin.OffsetBottom = -36f;
        AddChild(frameMargin);

        PanelContainer frame = CreateSurfacePanel(FrameColor, AccentMutedColor, padding: 28);
        frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        frame.SizeFlagsVertical = SizeFlags.ExpandFill;
        frameMargin.AddChild(frame);

        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 18);
        frame.AddChild(root);

        root.AddChild(BuildHeaderRow());
        _settingsSeparator = new HSeparator
        {
            Visible = false
        };
        root.AddChild(_settingsSeparator);
        _settingsSection = BuildSettingsSection();
        _settingsSection.Visible = false;
        root.AddChild(_settingsSection);
        root.AddChild(BuildMainContent());
        ApplyPassiveMouseFilterRecursive(frame);

        AddChild(BuildCreateDialog());
        AddChild(BuildJoinPasswordDialog());
        AddChild(BuildProgressDialog());
        AddChild(BuildResumeSlotDialog());
    }

    private Control BuildHeaderRow()
    {
        HBoxContainer row = new()
        {
            CustomMinimumSize = new Vector2(0f, 72f)
        };
        row.AddThemeConstantOverride("separation", 16);

        VBoxContainer titleGroup = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        titleGroup.AddThemeConstantOverride("separation", 6);
        row.AddChild(titleGroup);

        Label title = CreateTitleLabel("游戏大厅", 28);
        titleGroup.AddChild(title);

        Label subtitle = CreateBodyLabel("浏览房间、搜索筛选、查看状态，并按服务端策略走直连或 relay 加入联机流程。");
        subtitle.AddThemeColorOverride("font_color", TextMutedColor);
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titleGroup.AddChild(subtitle);

        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 10);
        row.AddChild(actions);

        _healthIndicatorLabel = CreateBodyLabel("● 未连接");
        _healthIndicatorLabel.AddThemeColorOverride("font_color", DangerColor);
        actions.AddChild(_healthIndicatorLabel);

        _settingsButton = CreateInlineButton("设置", ToggleSettingsVisibility);
        actions.AddChild(_settingsButton);

        _closeButton = CreateInlineButton("返回", HideOverlay, accent: true);
        actions.AddChild(_closeButton);
        return row;
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

        body.AddChild(BuildLabeledInputRow("玩家名", LanConnectConfig.PlayerDisplayName, out _displayNameInput, "留空时自动使用当前系统用户名"));

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

        _networkSettingsContainer.AddChild(BuildLabeledInputRow("HTTP 覆盖", LanConnectConfig.LobbyServerBaseUrlOverride, out _serverBaseUrlInput, "留空则继续使用内置大厅"));
        _networkSettingsContainer.AddChild(BuildLabeledInputRow("WS 覆盖", LanConnectConfig.LobbyServerWsUrlOverride, out _serverWsUrlInput, "留空则按 HTTP 自动推导 /control"));

        if (_serverBaseUrlInput != null)
        {
            _serverBaseUrlInput.Secret = true;
        }

        if (_serverWsUrlInput != null)
        {
            _serverWsUrlInput.Secret = true;
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
        HBoxContainer content = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 18);

        content.AddChild(BuildRoomStagePanel());
        content.AddChild(BuildSidebar());
        return content;
    }

    private Control BuildRoomStagePanel()
    {
        PanelContainer card = CreateSurfacePanel(SurfaceColor, AccentMutedColor, padding: 24);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.SizeFlagsVertical = SizeFlags.ExpandFill;

        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 18);
        card.AddChild(body);

        body.AddChild(CreateSectionLabel("房间目录"));

        Label summary = CreateBodyLabel("搜索、筛选并翻页浏览房间，双击卡片可快速加入。");
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summary.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(summary);

        _roomListSummaryLabel = CreateBodyLabel("大厅当前没有房间。");
        _roomListSummaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _roomListSummaryLabel.AddThemeColorOverride("font_color", TextStrongColor);
        body.AddChild(_roomListSummaryLabel);

        body.AddChild(BuildRoomFilterRow());
        body.AddChild(BuildRoomPagerRow());

        PanelContainer listFrame = CreateSurfacePanel(SurfaceMutedColor, AccentMutedColor, padding: 12);
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
        VBoxContainer container = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        container.AddThemeConstantOverride("separation", 8);

        HBoxContainer searchRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        searchRow.AddThemeConstantOverride("separation", 10);

        _roomSearchInput = new LineEdit
        {
            PlaceholderText = UiText("搜索房间 / 房主 / 版本 / 状态"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectAllOnFocus = true
        };
        ApplyInputStyle(_roomSearchInput);
        _roomSearchInput.Connect(LineEdit.SignalName.TextChanged, Callable.From<string>(OnRoomSearchChanged));
        searchRow.AddChild(_roomSearchInput);

        Button clearButton = CreateInlineButton("清空", ClearRoomSearch);
        clearButton.TooltipText = UiText("清空当前搜索关键词");
        searchRow.AddChild(clearButton);
        container.AddChild(searchRow);

        HFlowContainer filterRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        filterRow.AddThemeConstantOverride("h_separation", 8);
        filterRow.AddThemeConstantOverride("v_separation", 8);

        _roomFilterPublicButton = CreateFilterChipButton("公开", "只显示公开房间，再次点击取消。", () => ToggleRoomAccessFilter(RoomAccessFilter.Public));
        filterRow.AddChild(_roomFilterPublicButton);

        _roomFilterLockedButton = CreateFilterChipButton("上锁", "只显示密码房，再次点击取消。", () => ToggleRoomAccessFilter(RoomAccessFilter.Locked));
        filterRow.AddChild(_roomFilterLockedButton);

        _roomFilterJoinableButton = CreateFilterChipButton("可加入", "隐藏当前不可加入的房间。", ToggleJoinableOnlyFilter);
        filterRow.AddChild(_roomFilterJoinableButton);

        container.AddChild(filterRow);
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
        VBoxContainer sidebar = new()
        {
            CustomMinimumSize = new Vector2(340f, 0f),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        sidebar.AddThemeConstantOverride("separation", 18);

        sidebar.AddChild(BuildStatusCard());
        sidebar.AddChild(BuildActionCard());
        return sidebar;
    }

    private Control BuildStatusCard()
    {
        PanelContainer card = CreateSurfacePanel(SurfaceColor, AccentMutedColor, padding: 20);
        VBoxContainer body = new();
        body.AddThemeConstantOverride("separation", 12);
        card.AddChild(body);

        body.AddChild(CreateSectionLabel("大厅状态"));

        _statusLabel = CreateBodyLabel("大厅就绪。");
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_statusLabel);

        Label help = CreateBodyLabel("普通玩家通常只需要刷新大厅和加入目标房间。房主创建后会自动注册、保活，并在退出时主动关房。");
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        help.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(help);
        return card;
    }

    private Control BuildActionCard()
    {
        PanelContainer card = CreateSurfacePanel(SurfaceColor, AccentMutedColor, padding: 20);
        VBoxContainer body = new();
        body.AddThemeConstantOverride("separation", 12);
        card.AddChild(body);

        body.AddChild(CreateSectionLabel("快速操作"));

        _createButton = CreateActionButton("创建房间", "先在本地起 ENet Host，再把房间发布到大厅。", OpenCreateDialog, primary: true);
        body.AddChild(_createButton);

        _joinButton = CreateActionButton("加入选中房间", "加入当前选中的房间，密码房会先弹出输入框。", JoinSelectedRoom);
        body.AddChild(_joinButton);

        _refreshButton = CreateActionButton("刷新大厅", "立即抓取最新房间列表，并重置自动刷新计时。", () => TaskHelper.RunSafely(RefreshRoomsAsync(userInitiated: true)));
        body.AddChild(_refreshButton);

        _closeRoomButton = CreateActionButton("关闭我的房间", "关闭当前托管中的大厅房间，并从房间列表里移除。", () => TaskHelper.RunSafely(CloseMyRoomAsync()), danger: true);
        _closeRoomButton.Visible = false;
        body.AddChild(_closeRoomButton);

        _actionAvailabilityLabel = CreateBodyLabel("操作状态会显示在这里。");
        _actionAvailabilityLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _actionAvailabilityLabel.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(_actionAvailabilityLabel);

        Label fallback = CreateBodyLabel("手动 LAN/IP 直连仍保留在原 Host/Join 页面，仅作为开发和故障回退入口。");
        fallback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        fallback.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(fallback);
        return card;
    }

    private Control BuildCreateDialog()
    {
        Control shell = CreateDialogShell(out VBoxContainer body);
        _createDialogContainer = shell;

        body.AddChild(CreateSectionLabel("创建房间"));

        Label description = CreateBodyLabel("房间会先在本地起标准 ENet Host，再向大厅注册。当前入口只做房间目录与连接编排，不重写原生联机逻辑。");
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(description);

        body.AddChild(BuildLabeledInputRow("房间名", GetSuggestedRoomName(), out _roomNameInput, "房间列表里展示的名称"));
        body.AddChild(BuildLabeledInputRow("可选密码", string.Empty, out _roomPasswordInput, "留空表示公开房间"));

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

        body.AddChild(BuildLabeledInputRow("密码", string.Empty, out _joinPasswordInput, "该房间开启了密码保护"));
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
        margin.OffsetLeft = -300f;
        margin.OffsetTop = -180f;
        margin.OffsetRight = 300f;
        margin.OffsetBottom = 180f;
        center.AddChild(margin);

        PanelContainer card = CreateSurfacePanel(FrameColor, AccentColor, radius: 20, padding: 22);
        card.CustomMinimumSize = new Vector2(560f, 0f);
        margin.AddChild(card);

        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 14);
        card.AddChild(body);
        return shell;
    }

    private Control BuildLabeledInputRow(string labelText, string initialValue, out LineEdit input, string placeholder)
    {
        VBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 6);

        Label label = CreateBodyLabel(labelText);
        label.AddThemeColorOverride("font_color", TextStrongColor);
        row.AddChild(label);

        input = new LineEdit
        {
            Text = initialValue,
            PlaceholderText = UiText(placeholder),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectAllOnFocus = true
        };
        ApplyInputStyle(input);
        input.Connect(LineEdit.SignalName.FocusExited, Callable.From(PersistSettings));
        row.AddChild(input);
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
            IReadOnlyList<LobbyRoomSummary> rooms = await apiClient.GetRoomsAsync();
            double? measuredProbeRtt = await probeTask;
            _lastLobbyRttMs = measuredProbeRtt ?? -1d;
            _consecutiveRefreshFailures = 0;
            _rooms.Clear();
            _rooms.AddRange(rooms);
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
            if (userInitiated || previousRooms.Count == 0)
            {
                if (_rooms.Count == 0)
                {
                    SetStatus(measuredProbeRtt.HasValue
                        ? $"当前没有公开房间。服务延迟约 {measuredProbeRtt.Value:0}ms。"
                        : "当前没有公开房间。");
                }
                else
                {
                    SetStatus(measuredProbeRtt.HasValue
                        ? $"已加载 {_rooms.Count} 个房间。服务延迟约 {measuredProbeRtt.Value:0}ms。"
                        : $"已加载 {_rooms.Count} 个房间。延迟探测暂不可用。");
                }
            }
        }
        catch (LobbyServiceException ex)
        {
            _consecutiveRefreshFailures++;
            _lastLobbyRttMs = -1d;
            GD.Print($"sts2_lan_connect overlay: refresh failed with lobby error {ex.Code} - {ex.Message}");
            UpdateHealthIndicator();
            if (userInitiated || _rooms.Count == 0)
            {
                SetStatus($"大厅服务不可用：{ex.Message}");
            }
            else
            {
                SetStatus($"大厅刷新失败，已保留上次成功列表：{ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _consecutiveRefreshFailures++;
            _lastLobbyRttMs = -1d;
            GD.Print($"sts2_lan_connect overlay: refresh failed with exception {ex.Message}");
            UpdateHealthIndicator();
            if (userInitiated || _rooms.Count == 0)
            {
                SetStatus($"刷新大厅失败：{ex.Message}");
            }
            else
            {
                SetStatus($"大厅刷新失败，已保留上次成功列表：{ex.Message}");
            }
        }
        finally
        {
            _refreshInFlight = false;
            GD.Print("sts2_lan_connect overlay: refresh finished");
            UpdateActionButtons();
        }
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
                        : "当前客户端未绑定内置大厅服务。请在设置里填写 HTTP/WS 覆盖地址。");
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

        SetLabelText(_roomListSummaryLabel, $"房间 {_rooms.Count} → {filteredRooms.Count} · 筛选：{DescribeRoomFilterState()} · 已选：{selectedRoom.RoomName}");
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
        scrollbar.AddThemeStyleboxOverride("scroll", CreatePanelStyle(new Color(0.07f, 0.07f, 0.08f, 0.98f), AccentMutedColor, radius: 999, borderWidth: 1, padding: 4));
        scrollbar.AddThemeStyleboxOverride("grabber", CreatePanelStyle(new Color(0.33f, 0.26f, 0.12f, 0.96f), AccentColor, radius: 999, borderWidth: 1, padding: 8));
        scrollbar.AddThemeStyleboxOverride("grabber_highlight", CreatePanelStyle(new Color(0.42f, 0.33f, 0.15f, 0.98f), AccentColor, radius: 999, borderWidth: 1, padding: 8));
        scrollbar.AddThemeStyleboxOverride("grabber_pressed", CreatePanelStyle(new Color(0.52f, 0.39f, 0.17f, 1f), AccentColor, radius: 999, borderWidth: 1, padding: 8));
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
        PanelContainer card = CreateSurfacePanel(new Color(0.06f, 0.06f, 0.07f, 0.96f), AccentMutedColor, radius: 16, borderWidth: 1, padding: 22);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.CustomMinimumSize = new Vector2(0f, 140f);

        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 10);
        card.AddChild(body);

        Label title = CreateTitleLabel(titleText, 26);
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

    private Control CreateRoomCard(LobbyRoomSummary room, bool isSelected, bool isHostRoom)
    {
        Color background = isSelected
            ? new Color(0.17f, 0.13f, 0.08f, 0.98f)
            : new Color(0.06f, 0.06f, 0.07f, 0.96f);
        Color border = isSelected
            ? AccentColor
            : isHostRoom
                ? SuccessColor
                : AccentMutedColor;

        PanelContainer card = CreateSurfacePanel(background, border, radius: 16, borderWidth: isSelected ? 2 : 1, padding: 18);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.CustomMinimumSize = new Vector2(0f, 118f);
        card.MouseFilter = MouseFilterEnum.Stop;
        card.MouseDefaultCursorShape = CursorShape.PointingHand;

        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 10);
        card.AddChild(body);

        HBoxContainer topRow = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        topRow.AddThemeConstantOverride("separation", 10);
        body.AddChild(topRow);

        Label title = CreateTitleLabel(room.RequiresPassword ? $"[锁] {room.RoomName}" : room.RoomName, 22);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        topRow.AddChild(title);

        HBoxContainer tags = new();
        tags.AddThemeConstantOverride("separation", 8);
        topRow.AddChild(tags);

        if (isSelected)
        {
            tags.AddChild(CreateTagPill("已选中", AccentColor, FrameColor));
        }

        if (isHostRoom)
        {
            tags.AddChild(CreateTagPill("你的房间", SuccessColor, new Color(0.06f, 0.11f, 0.07f, 0.96f)));
        }
        else if (string.Equals(room.Status, "starting", StringComparison.OrdinalIgnoreCase))
        {
            tags.AddChild(CreateTagPill("已开局 / 不可加入", DangerColor, new Color(0.18f, 0.09f, 0.09f, 0.96f)));
        }
        else if (string.Equals(room.Status, "full", StringComparison.OrdinalIgnoreCase))
        {
            tags.AddChild(CreateTagPill("已满", DangerColor, new Color(0.18f, 0.09f, 0.09f, 0.96f)));
        }
        else if (room.SavedRun != null)
        {
            int availableSlots = GetAvailableSavedRunSlots(room).Count;
            tags.AddChild(CreateTagPill($"续局 {availableSlots} 可接管", AccentColor, new Color(0.15f, 0.12f, 0.08f, 0.96f)));
        }
        else if (room.RequiresPassword)
        {
            tags.AddChild(CreateTagPill("密码房", AccentColor, new Color(0.19f, 0.14f, 0.08f, 0.96f)));
        }
        else
        {
            tags.AddChild(CreateTagPill("公开房", TextMutedColor, new Color(0.11f, 0.11f, 0.13f, 0.96f)));
        }

        tags.AddChild(CreateTagPill(
            room.RelayState switch
            {
                "ready" => "relay 就绪",
                "planned" => "relay 等待房主注册",
                _ => "relay 未启用"
            },
            room.RelayState == "ready" ? SuccessColor : room.RelayState == "planned" ? AccentColor : TextMutedColor,
            room.RelayState == "ready"
                ? new Color(0.06f, 0.11f, 0.07f, 0.96f)
                : room.RelayState == "planned"
                    ? new Color(0.16f, 0.13f, 0.08f, 0.96f)
                    : new Color(0.11f, 0.11f, 0.13f, 0.96f)));

        Label hostLine = CreateBodyLabel($"房主：{room.HostPlayerName}  ·  席位：{room.CurrentPlayers}/{room.MaxPlayers}  ·  状态：{FormatStatus(room.Status)}");
        hostLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hostLine.AddThemeColorOverride("font_color", TextStrongColor);
        body.AddChild(hostLine);

        Label metaLine = CreateBodyLabel($"模式：{room.GameMode}  ·  游戏 {room.Version}  ·  MOD {room.ModVersion}");
        metaLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        metaLine.AddThemeColorOverride("font_color", TextMutedColor);
        body.AddChild(metaLine);

        Label hintLine = CreateBodyLabel(
            isHostRoom
                ? "这是你当前托管的房间。"
                : string.Equals(room.Status, "starting", StringComparison.OrdinalIgnoreCase)
                    ? "该房间已经开始游戏。大厅会明确阻止继续加入，避免误报成连接超时。"
                : string.Equals(room.Status, "full", StringComparison.OrdinalIgnoreCase)
                    ? "该房间已经满员。请等待空位或选择其他房间。"
                : room.SavedRun != null
                    ? "这是一个多人续局房间。加入前需要接管一个当前无人控制的角色槽位。"
                : room.RequiresPassword
                    ? "需要密码。双击卡片或点击右侧按钮后输入密码即可加入。"
                    : "双击卡片即可直接加入。");
        hintLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hintLine.AddThemeColorOverride(
            "font_color",
            isHostRoom
                ? SuccessColor
                : string.Equals(room.Status, "starting", StringComparison.OrdinalIgnoreCase) || string.Equals(room.Status, "full", StringComparison.OrdinalIgnoreCase)
                    ? DangerColor
                : room.SavedRun != null
                    ? AccentColor
                    : room.RequiresPassword
                        ? AccentColor
                        : TextMutedColor);
        body.AddChild(hintLine);

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
        string? password = _roomPasswordInput?.Text.Trim();
        GD.Print(
            $"sts2_lan_connect overlay: create requested roomName='{roomName}', passwordSet={!string.IsNullOrWhiteSpace(password)}, hasRunSave={SaveManager.Instance.HasMultiplayerRunSave}, hasActiveRoom={LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true}, endpointAvailable={HasAvailableLobbyEndpoint()}");
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
        SetStatus($"正在创建房间“{roomName}”...");

        try
        {
            bool created = await LanConnectHostFlow.StartLobbyHostAsync(roomName, password, _loadingOverlay, _stack);
            if (created)
            {
                CloseCreateDialog();
                HideOverlay();
            }
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

        PersistSettings();
        _actionInFlight = true;
        UpdateActionButtons();
        SetStatus($"正在请求加入“{room.RoomName}”...");
        ShowProgressDialog(
            "正在加入房间",
            $"正在向大厅申请进入“{room.RoomName}”",
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
                $"大厅已响应，正在连接“{room.RoomName}”",
                "如果房主在外网环境，首次握手通常会比刷新大厅更慢。");

            LobbyJoinAttemptResult joinResult = await LanConnectLobbyJoinFlow.JoinAsync(
                _stack,
                _loadingOverlay,
                joinResponse,
                desiredSavePlayerNetId,
                message => UpdateProgressDialog("正在建立联机连接", message));
            if (joinResult.Joined)
            {
                UpdateProgressDialog("正在进入房间", $"已连接“{room.RoomName}”，正在切换到联机界面");
                SetStatus($"已加入“{room.RoomName}”。");
                HideOverlay();
                return true;
            }

            string failureMessage = string.IsNullOrWhiteSpace(joinResult.FailureMessage)
                ? "请查看错误弹窗或连接日志。"
                : joinResult.FailureMessage;
            SetStatus($"加入“{room.RoomName}”失败：{failureMessage}");
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
            SetLabelText(_joinPasswordDialogTitle, $"输入“{room.RoomName}”的房间密码");

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

        string password = _joinPasswordInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowJoinPasswordError("请输入密码。");
            return;
        }

        await BeginJoinRoomAsync(_pendingPasswordJoinRoom, password);
    }

    private void OpenCreateDialog()
    {
        if (_createDialogContainer == null || _roomNameInput == null || _roomPasswordInput == null)
        {
            return;
        }

        string? blockReason = GetCreateAvailabilityReasonForDialog();
        if (blockReason != null)
        {
            SetStatus($"当前无法打开建房：{blockReason}");
            return;
        }

        _roomNameInput.Text = string.IsNullOrWhiteSpace(LanConnectConfig.LastRoomName)
            ? GetSuggestedRoomName()
            : LanConnectConfig.LastRoomName;
        _roomPasswordInput.Text = string.Empty;
        ShowCreateDialogError(string.Empty, visible: false);
        _createDialogContainer.Visible = true;
        _roomNameInput.GrabFocus();
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

    private void CloseCreateDialog()
    {
        if (_createDialogContainer != null)
        {
            _createDialogContainer.Visible = false;
        }
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
        SetLabelText(_resumeSlotDialogTitle, $"选择“{room.RoomName}”的可接管角色");

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

    private void PersistSettings()
    {
        if (_displayNameInput != null)
        {
            LanConnectConfig.PlayerDisplayName = _displayNameInput.Text.Trim();
        }

        if (_serverBaseUrlInput != null)
        {
            LanConnectConfig.LobbyServerBaseUrl = _serverBaseUrlInput.Text.Trim();
        }

        if (_serverWsUrlInput != null)
        {
            LanConnectConfig.LobbyServerWsUrl = _serverWsUrlInput.Text.Trim();
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

        if (_serverWsUrlInput != null)
        {
            _serverWsUrlInput.Text = LanConnectConfig.LobbyServerWsUrlOverride;
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

        if (_serverWsUrlInput != null)
        {
            _serverWsUrlInput.Secret = !_networkFieldsRevealed;
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

        if (_serverWsUrlInput != null)
        {
            _serverWsUrlInput.Text = string.Empty;
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
        _clearNetworkOverridesButton.Disabled = !LanConnectConfig.HasLobbyServerOverrides && string.IsNullOrWhiteSpace(_serverBaseUrlInput?.Text) && string.IsNullOrWhiteSpace(_serverWsUrlInput?.Text);
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
        SetButtonText(_settingsButton, nextVisible ? "收起设置" : "设置");
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
            bool hasOverrideText = !string.IsNullOrWhiteSpace(_serverBaseUrlInput?.Text) || !string.IsNullOrWhiteSpace(_serverWsUrlInput?.Text);
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

        string text;
        Color color;
        if (!HasAvailableLobbyEndpoint())
        {
            text = "● 未绑定大厅";
            color = DangerColor;
        }
        else if (_consecutiveRefreshFailures >= 2)
        {
            text = "● 连接异常";
            color = DangerColor;
        }
        else if (_consecutiveRefreshFailures == 1)
        {
            text = "● 最近刷新失败";
            color = AccentColor;
        }
        else if (_lastLobbyRttMs < 0d)
        {
            text = _rooms.Count > 0 ? "● 延迟探测暂不可用" : "● 等待首次探测";
            color = AccentColor;
        }
        else if (_lastLobbyRttMs <= 600d)
        {
            text = $"● 服务正常 {_lastLobbyRttMs:0}ms";
            color = SuccessColor;
        }
        else if (_lastLobbyRttMs <= 1500d)
        {
            text = $"● 延迟偏高 {_lastLobbyRttMs:0}ms";
            color = AccentColor;
        }
        else
        {
            text = $"● 延迟过高 {_lastLobbyRttMs:0}ms";
            color = DangerColor;
        }

        SetLabelText(_healthIndicatorLabel, text);
        _healthIndicatorLabel.AddThemeColorOverride("font_color", color);
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
            summary = "当前网络：已启用手动覆盖地址。覆盖值默认遮罩显示，不会回显打包默认地址。";
            color = AccentColor;
        }
        else if (LanConnectLobbyEndpointDefaults.HasBundledDefaults())
        {
            summary = "当前网络：使用打包内置大厅服务。默认地址仅在运行时读取，不会写进 config.json，也不会在这里明文显示。";
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
            if (!RoomMatchesAccessFilter(room))
            {
                continue;
            }

            if (_joinableOnlyFilter && !CanDisplayAsJoinable(room))
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

    private bool RoomMatchesAccessFilter(LobbyRoomSummary room)
    {
        return _roomAccessFilter switch
        {
            RoomAccessFilter.Public => !room.RequiresPassword,
            RoomAccessFilter.Locked => room.RequiresPassword,
            _ => true
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
               || ContainsIgnoreCase(FormatStatus(room.Status), query);
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

    private void ToggleRoomAccessFilter(RoomAccessFilter filter)
    {
        _roomAccessFilter = _roomAccessFilter == filter ? RoomAccessFilter.All : filter;
        ApplyRoomFilterState("access_filter");
    }

    private void ToggleJoinableOnlyFilter()
    {
        _joinableOnlyFilter = !_joinableOnlyFilter;
        ApplyRoomFilterState("joinable_filter");
    }

    private void ApplyRoomFilterState(string source)
    {
        GD.Print($"sts2_lan_connect overlay: room filters -> source={source};access={_roomAccessFilter};joinableOnly={_joinableOnlyFilter};query='{_roomSearchQuery}'");
        _currentPageIndex = 0;
        ResetRoomListTouchTracking();
        ResetRoomListScroll();
        RebuildRoomStage();
        UpdateActionButtons();
    }

    private bool HasRoomSearchOrFilter()
    {
        return !string.IsNullOrWhiteSpace(_roomSearchQuery)
               || _roomAccessFilter != RoomAccessFilter.All
               || _joinableOnlyFilter;
    }

    private string DescribeRoomFilterState()
    {
        List<string> filters = new();
        switch (_roomAccessFilter)
        {
            case RoomAccessFilter.Public:
                filters.Add("公开");
                break;
            case RoomAccessFilter.Locked:
                filters.Add("上锁");
                break;
        }

        if (_joinableOnlyFilter)
        {
            filters.Add("可加入");
        }

        if (!string.IsNullOrWhiteSpace(_roomSearchQuery))
        {
            filters.Add($"搜索：{_roomSearchQuery}");
        }

        return filters.Count == 0 ? "全部" : string.Join(" · ", filters);
    }

    private Button CreateActionButton(string text, string tooltip, Action onPressed, bool primary = false, bool danger = false)
    {
        Button button = new()
        {
            Text = UiText(text),
            TooltipText = UiText(tooltip),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 54f),
            Alignment = HorizontalAlignment.Center
        };
        ApplyButtonStyle(button, primary, danger);
        button.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            GD.Print($"sts2_lan_connect overlay: action button '{text}' pressed");
            onPressed();
        }));
        return button;
    }

    private Button CreateInlineButton(string text, Action onPressed, bool accent = false)
    {
        Button button = new()
        {
            Text = UiText(text),
            CustomMinimumSize = new Vector2(0f, 42f)
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
            button.Text = UiText(text);
        }
    }

    private static void SetLabelText(Label? label, string text)
    {
        if (label != null)
        {
            label.Text = UiText(text);
        }
    }

    private static PanelContainer CreateSurfacePanel(Color background, Color border, int radius = 18, int borderWidth = 1, int padding = 18)
    {
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, border, radius, borderWidth, padding));
        return panel;
    }

    private static StyleBoxFlat CreatePanelStyle(Color background, Color border, int radius, int borderWidth, int padding)
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

    private static Label CreateBodyLabel(string text)
    {
        Label label = new()
        {
            Text = UiText(text)
        };
        label.AddThemeColorOverride("font_color", TextStrongColor);
        label.AddThemeFontSizeOverride("font_size", 15);
        return label;
    }

    private static void ApplyButtonStyle(Button button, bool primary, bool danger)
    {
        Color normal = primary
            ? new Color(0.39f, 0.29f, 0.11f, 0.98f)
            : danger
                ? new Color(0.3f, 0.12f, 0.12f, 0.98f)
                : new Color(0.15f, 0.15f, 0.17f, 0.98f);
        Color hover = primary
            ? new Color(0.49f, 0.37f, 0.13f, 1f)
            : danger
                ? new Color(0.42f, 0.16f, 0.16f, 1f)
                : new Color(0.21f, 0.21f, 0.24f, 1f);
        Color pressed = primary
            ? new Color(0.29f, 0.22f, 0.09f, 1f)
            : danger
                ? new Color(0.24f, 0.1f, 0.1f, 1f)
                : new Color(0.11f, 0.11f, 0.13f, 1f);
        Color border = danger ? DangerColor : AccentMutedColor;

        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(normal, border, radius: 14, borderWidth: 1, padding: 14));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(hover, danger ? DangerColor : AccentColor, radius: 14, borderWidth: 1, padding: 14));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(pressed, danger ? DangerColor : AccentColor, radius: 14, borderWidth: 1, padding: 14));
        button.AddThemeStyleboxOverride("disabled", CreatePanelStyle(WithAlpha(normal, 0.45f), WithAlpha(border, 0.35f), radius: 14, borderWidth: 1, padding: 14));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(hover, AccentColor, radius: 14, borderWidth: 1, padding: 14));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 16);
    }

    private static void ApplyInlineButtonStyle(Button button, bool accent)
    {
        Color border = accent ? AccentColor : AccentMutedColor;
        Color normal = accent
            ? new Color(0.22f, 0.17f, 0.08f, 0.98f)
            : new Color(0.12f, 0.12f, 0.14f, 0.98f);
        Color hover = accent
            ? new Color(0.3f, 0.22f, 0.1f, 1f)
            : new Color(0.18f, 0.18f, 0.2f, 1f);

        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(normal, border, radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(hover, AccentColor, radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(normal, AccentColor, radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("disabled", CreatePanelStyle(WithAlpha(normal, 0.45f), WithAlpha(border, 0.35f), radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(hover, AccentColor, radius: 12, borderWidth: 1, padding: 10));
        button.AddThemeColorOverride("font_color", TextStrongColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 14);
    }

    private void UpdateRoomFilterButtons()
    {
        if (_roomFilterPublicButton != null)
        {
            ApplyFilterChipStyle(_roomFilterPublicButton, _roomAccessFilter == RoomAccessFilter.Public);
        }

        if (_roomFilterLockedButton != null)
        {
            ApplyFilterChipStyle(_roomFilterLockedButton, _roomAccessFilter == RoomAccessFilter.Locked);
        }

        if (_roomFilterJoinableButton != null)
        {
            ApplyFilterChipStyle(_roomFilterJoinableButton, _joinableOnlyFilter);
        }
    }

    private static void ApplyFilterChipStyle(Button button, bool active)
    {
        Color normal = active
            ? new Color(0.34f, 0.26f, 0.1f, 0.98f)
            : new Color(0.09f, 0.09f, 0.11f, 0.98f);
        Color hover = active
            ? new Color(0.43f, 0.32f, 0.12f, 1f)
            : new Color(0.15f, 0.15f, 0.18f, 1f);
        Color border = active ? AccentColor : AccentMutedColor;

        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(normal, border, radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(hover, AccentColor, radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(normal, AccentColor, radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("disabled", CreatePanelStyle(WithAlpha(normal, 0.45f), WithAlpha(border, 0.35f), radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(hover, AccentColor, radius: 14, borderWidth: 1, padding: 10));
        button.AddThemeColorOverride("font_color", active ? TextStrongColor : TextMutedColor);
        button.AddThemeColorOverride("font_disabled_color", WithAlpha(TextMutedColor, 0.65f));
        button.AddThemeFontSizeOverride("font_size", 14);
    }

    private static void ApplyInputStyle(LineEdit input)
    {
        input.CustomMinimumSize = new Vector2(0f, 44f);
        input.AddThemeStyleboxOverride("normal", CreatePanelStyle(new Color(0.06f, 0.06f, 0.07f, 0.98f), AccentMutedColor, radius: 12, borderWidth: 1, padding: 12));
        input.AddThemeStyleboxOverride("focus", CreatePanelStyle(new Color(0.08f, 0.08f, 0.09f, 1f), AccentColor, radius: 12, borderWidth: 1, padding: 12));
        input.AddThemeColorOverride("font_color", TextStrongColor);
        input.AddThemeColorOverride("font_placeholder_color", new Color(TextMutedColor, 0.72f));
        input.AddThemeColorOverride("caret_color", AccentColor);
        input.AddThemeFontSizeOverride("font_size", 15);
    }

    private static void ApplyPassiveMouseFilterRecursive(Node node)
    {
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

    private static string GetSuggestedRoomName()
    {
        return string.IsNullOrWhiteSpace(LanConnectConfig.LastRoomName)
            ? "新的联机房间"
            : LanConnectConfig.LastRoomName;
    }
}
