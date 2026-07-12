using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// 检测 AutoModSubscriber 并注册 UI 接管回调。
/// 当 AMS 检测到 ModMismatch 时，由本类构建大厅风格的弹窗，
/// 核心订阅/禁用逻辑复用 AMS 的 public API。
/// </summary>
internal static class LanConnectAutoModSubscriberCompat
{
    private const string AmsPatchTypeName = "AutoModSubscriber.UI.ClientModMismatchInterceptPatch";
    private const string AmsExternalHandlerFieldName = "ExternalDialogHandler";

    private static bool _registered;

    public static void Initialize()
    {
        if (_registered) return;

        try
        {
            Type? patchType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(static a => a.GetType(AmsPatchTypeName, throwOnError: false))
                .FirstOrDefault(static t => t != null);

            if (patchType == null)
            {
                Log.Info("sts2_lan_connect: AutoModSubscriber not detected, skipping compat registration.");
                return;
            }

            FieldInfo? field = patchType.GetField(
                AmsExternalHandlerFieldName,
                BindingFlags.Public | BindingFlags.Static);

            if (field == null)
            {
                Log.Warn("sts2_lan_connect: AutoModSubscriber detected but ExternalDialogHandler field not found.");
                return;
            }

            field.SetValue(null, (Func<ConnectionFailureExtraInfo, bool>)HandleModMismatch);
            _registered = true;
            Log.Info("sts2_lan_connect: AutoModSubscriber compat registered, UI takeover enabled.");
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect: Failed to register AutoModSubscriber compat: {ex.Message}");
        }
    }

    // ── AMS public API 反射缓存 ──────────────────────────────────────

    private static Type? _amsWorkshopSubscriberType;
    private static Type? _amsModDisableApplierType;
    private static Type? _amsModWorkshopMapType;
    private static Type? _amsSubscribeJobStateType;
    private static object? _amsWorkshopSubscriberInstance;
    private static MethodInfo? _amsSubmitMethod;
    private static MethodInfo? _amsPollMethod;
    private static MethodInfo? _amsGetMethod;
    private static MethodInfo? _amsWaitAllMethod;
    private static MethodInfo? _amsApplyDisableMethod;
    private static MethodInfo? _amsMapTryGetMethod;
    private static PropertyInfo? _amsMapHostHasModProp;
    private static PropertyInfo? _amsJobStateProperty;
    private static PropertyInfo? _amsJobIsTerminalProperty;
    private static PropertyInfo? _amsJobBytesDownloadedProperty;
    private static PropertyInfo? _amsJobBytesTotalProperty;

    private static void EnsureAmsApiCached()
    {
        if (_amsWorkshopSubscriberType != null) return;

        _amsWorkshopSubscriberType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(static a => a.GetType("AutoModSubscriber.Subscribe.WorkshopSubscriber", throwOnError: false))
            .FirstOrDefault(static t => t != null);
        _amsModDisableApplierType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(static a => a.GetType("AutoModSubscriber.Disable.ModDisableApplier", throwOnError: false))
            .FirstOrDefault(static t => t != null);
        _amsModWorkshopMapType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(static a => a.GetType("AutoModSubscriber.Protocol.ModWorkshopMap", throwOnError: false))
            .FirstOrDefault(static t => t != null);
        _amsSubscribeJobStateType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(static a => a.GetType("AutoModSubscriber.Subscribe.SubscribeJobState", throwOnError: false))
            .FirstOrDefault(static t => t != null);

        if (_amsWorkshopSubscriberType != null)
        {
            _amsWorkshopSubscriberInstance = _amsWorkshopSubscriberType
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?
                .GetValue(null);

            _amsSubmitMethod = _amsWorkshopSubscriberType.GetMethod("Submit");
            _amsPollMethod = _amsWorkshopSubscriberType.GetMethod("Poll");
            _amsGetMethod = _amsWorkshopSubscriberType.GetMethod("Get");
            _amsWaitAllMethod = _amsWorkshopSubscriberType.GetMethod("WaitAll");
        }

        if (_amsModDisableApplierType != null)
        {
            _amsApplyDisableMethod = _amsModDisableApplierType.GetMethod("Apply");
        }

        if (_amsModWorkshopMapType != null)
        {
            _amsMapTryGetMethod = _amsModWorkshopMapType.GetMethod("TryGet", new[] { typeof(string) });
            _amsMapHostHasModProp = _amsModWorkshopMapType.GetProperty("HostHasMod");
        }

        if (_amsSubscribeJobStateType != null)
        {
            // Job 的属性通过运行时反射
        }
    }

    // ── ModMismatch 弹窗 ─────────────────────────────────────────────

    private static bool HandleModMismatch(ConnectionFailureExtraInfo extra)
    {
        try
        {
            EnsureAmsApiCached();
            if (_amsWorkshopSubscriberInstance == null)
            {
                Log.Warn("sts2_lan_connect: AMS API not available, falling back to AMS default UI.");
                return false;
            }

            GD.Print("sts2_lan_connect: AutoModSubscriber ModMismatch intercepted, showing lobby-style dialog.");

            var dialog = new LobbyModMismatchDialog(extra);
            ShowDialogDeferred(dialog);
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"sts2_lan_connect: HandleModMismatch failed: {ex}");
            return false;
        }
    }

    private static void ShowDialogDeferred(Control dialog)
    {
        Callable.From(() =>
        {
            try
            {
                var tree = Engine.GetMainLoop() as SceneTree;
                if (tree?.Root == null) return;
                tree.Root.AddChild(dialog);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"sts2_lan_connect: ShowDialogDeferred failed: {ex}");
            }
        }).CallDeferred();
    }

    // ── 辅助：解析 "ModId-1.2.3" ───────────────────────────────────

    private static void ParseIdVersion(string raw, out string id, out string version)
    {
        if (string.IsNullOrEmpty(raw)) { id = ""; version = ""; return; }
        int dash = raw.LastIndexOf('-');
        if (dash <= 0 || dash == raw.Length - 1) { id = raw; version = ""; }
        else { id = raw.Substring(0, dash); version = raw.Substring(dash + 1); }
    }

    private static ulong TryGetWorkshopId(string manifestId)
    {
        if (_amsMapTryGetMethod == null) return 0;
        var result = _amsMapTryGetMethod.Invoke(null, new object[] { manifestId });
        if (result is bool found && found)
        {
            // TryGet 返回 (bool, ulong) — 需要通过 out 参数获取
            // 实际上 TryGet(string, out ulong) 在反射调用时需要特殊处理
        }
        return 0;
    }

    // ── 大厅风格弹窗 ─────────────────────────────────────────────────

    /// <summary>
    /// 简单的大厅风格 ModMismatch 弹窗。
    /// 使用 Godot 原生 Control 组件 + 大厅配色方案。
    /// </summary>
    private sealed partial class LobbyModMismatchDialog : Control
    {
        private static readonly Color BgColor = new("0D1117");
        private static readonly Color PanelColor = new("1A2A3A");
        private static readonly Color AccentColor = new("3B82F6");
        private static readonly Color TextColor = new("D1D5DB");
        private static readonly Color WarningColor = new("F5A623");
        private static readonly Color SuccessColor = new("22C55E");

        private readonly ConnectionFailureExtraInfo _extra;
        private readonly List<(string Id, ulong FileId, Label StatusLabel, ProgressBar Bar, Button SubBtn)> _subscribeRows = new();
        private readonly List<(string Id, CheckBox Cb)> _disableRows = new();
        private Label _hintLabel = null!;
        private Godot.Timer? _pollTimer;
        private bool _subscribeInProgress;

        public LobbyModMismatchDialog(ConnectionFailureExtraInfo extra)
        {
            _extra = extra;
            Name = "LobbyModMismatchDialog";
            MouseFilter = MouseFilterEnum.Stop;
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            BuildTree();
        }

        private void BuildTree()
        {
            // 遮罩
            var dim = new ColorRect { Color = new Color(0, 0, 0, 0.7f), MouseFilter = MouseFilterEnum.Stop };
            dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(dim);

            // 主面板
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 520) };
            panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            panel.OffsetLeft = -360; panel.OffsetTop = -260;
            panel.OffsetRight = 360; panel.OffsetBottom = 260;
            var panelStyle = new StyleBoxFlat { BgColor = PanelColor, BorderWidthBottom = 2, BorderWidthTop = 0, BorderWidthLeft = 0, BorderWidthRight = 0, BorderColor = AccentColor };
            panel.AddThemeStyleboxOverride("panel", panelStyle);
            AddChild(panel);

            var root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
            panel.AddChild(root);

            // 标题
            var title = new Label { Text = "联机 Mod 不匹配", HorizontalAlignment = HorizontalAlignment.Center };
            title.AddThemeColorOverride("font_color", AccentColor);
            title.AddThemeFontSizeOverride("font_size", 20);
            root.AddChild(title);
            root.AddChild(new HSeparator());

            // 区块 1：缺失的 mod
            var section1 = new Label { Text = "房主有但你没有的 Mod" };
            section1.AddThemeColorOverride("font_color", WarningColor);
            root.AddChild(section1);

            var scroll1 = new ScrollContainer { CustomMinimumSize = new Vector2(0, 140), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var vbox1 = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll1.AddChild(vbox1);
            root.AddChild(scroll1);

            if (_extra.missingModsOnLocal != null && _extra.missingModsOnLocal.Count > 0)
            {
                foreach (var name in _extra.missingModsOnLocal)
                {
                    ParseIdVersion(name, out var id, out _);
                    ulong fileId = TryGetWorkshopIdForRow(id);
                    var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 28) };

                    var nameLabel = new Label { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
                    nameLabel.AddThemeColorOverride("font_color", TextColor);
                    row.AddChild(nameLabel);

                    var bar = new ProgressBar { CustomMinimumSize = new Vector2(100, 16), Visible = false, ShowPercentage = false };
                    row.AddChild(bar);

                    var statusLabel = new Label { CustomMinimumSize = new Vector2(90, 0), HorizontalAlignment = HorizontalAlignment.Right };
                    statusLabel.AddThemeColorOverride("font_color", TextColor);
                    row.AddChild(statusLabel);

                    var subBtn = new Button { Text = "订阅", Visible = fileId != 0 };
                    subBtn.AddThemeColorOverride("font_color", TextColor);
                    row.AddChild(subBtn);

                    var openBtn = new Button { Text = "工坊" };
                    openBtn.Pressed += () => OS.ShellOpen($"https://steamcommunity.com/workshop/browse/?appid=2868840&searchtext={Uri.EscapeDataString(id)}");
                    row.AddChild(openBtn);

                    vbox1.AddChild(row);
                    _subscribeRows.Add((id, fileId, statusLabel, bar, subBtn));

                    if (fileId != 0)
                    {
                        subBtn.Pressed += () => OnSingleSubscribe(id, fileId);
                    }
                    else
                    {
                        statusLabel.Text = "无 workshopId";
                    }
                }
            }
            else
            {
                vbox1.AddChild(new Label { Text = "（无）" });
            }

            // 全部订阅按钮
            var btnRow1 = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var subAllBtn = new Button { Text = "全部自动订阅" };
            subAllBtn.Disabled = _subscribeRows.Count == 0 || _subscribeRows.All(r => r.FileId == 0);
            subAllBtn.Pressed += OnSubscribeAll;
            btnRow1.AddChild(subAllBtn);
            root.AddChild(btnRow1);

            // 区块 2：多余的 mod
            var section2 = new Label { Text = "你有但房主没有的 Mod" };
            section2.AddThemeColorOverride("font_color", WarningColor);
            root.AddChild(section2);

            var scroll2 = new ScrollContainer { CustomMinimumSize = new Vector2(0, 120), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var vbox2 = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll2.AddChild(vbox2);
            root.AddChild(scroll2);

            if (_extra.missingModsOnHost != null && _extra.missingModsOnHost.Count > 0)
            {
                foreach (var name in _extra.missingModsOnHost)
                {
                    ParseIdVersion(name, out var id, out _);
                    var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 24) };
                    var cb = new CheckBox { ButtonPressed = true };
                    row.AddChild(cb);
                    var label = new Label { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
                    label.AddThemeColorOverride("font_color", TextColor);
                    row.AddChild(label);
                    vbox2.AddChild(row);
                    _disableRows.Add((id, cb));
                }
            }
            else
            {
                vbox2.AddChild(new Label { Text = "（无）" });
            }

            var btnRow2 = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var disableBtn = new Button { Text = "禁用勾选项" };
            disableBtn.Disabled = _disableRows.Count == 0;
            disableBtn.Pressed += OnDisableSelected;
            btnRow2.AddChild(disableBtn);
            root.AddChild(btnRow2);

            // 提示区
            _hintLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 40) };
            _hintLabel.AddThemeColorOverride("font_color", TextColor);
            root.AddChild(_hintLabel);

            // 关闭按钮
            var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var closeBtn = new Button { Text = "关闭" };
            closeBtn.Pressed += QueueFree;
            footer.AddChild(closeBtn);
            root.AddChild(footer);

            // 轮询定时器
            _pollTimer = new Godot.Timer { WaitTime = 0.25f, OneShot = false, Autostart = false };
            _pollTimer.Timeout += OnPoll;
            AddChild(_pollTimer);
        }

        private ulong TryGetWorkshopIdForRow(string manifestId)
        {
            if (_amsMapTryGetMethod == null) return 0;
            // TryGet(string, out ulong) via reflection
            var parameters = new object[] { manifestId, 0UL };
            var result = _amsMapTryGetMethod.Invoke(null, parameters);
            if (result is bool found && found)
            {
                return (ulong)parameters[1];
            }
            return 0;
        }

        private void OnSingleSubscribe(string id, ulong fileId)
        {
            if (_amsSubmitMethod == null || _amsWorkshopSubscriberInstance == null) return;
            var items = Array.CreateInstance(
                typeof(ValueTuple<string, ulong>), 1);
            items.SetValue(ValueTuple.Create(id, fileId), 0);
            _amsSubmitMethod.Invoke(_amsWorkshopSubscriberInstance, new[] { items });
            _pollTimer?.Start();
        }

        private void OnSubscribeAll()
        {
            if (_subscribeInProgress) return;
            if (_amsSubmitMethod == null || _amsWorkshopSubscriberInstance == null) return;

            var valid = _subscribeRows.Where(r => r.FileId != 0).ToList();
            if (valid.Count == 0) return;

            var items = Array.CreateInstance(typeof(ValueTuple<string, ulong>), valid.Count);
            for (int i = 0; i < valid.Count; i++)
                items.SetValue(ValueTuple.Create(valid[i].Id, valid[i].FileId), i);

            _subscribeInProgress = true;
            _amsSubmitMethod.Invoke(_amsWorkshopSubscriberInstance, new[] { items });
            _pollTimer?.Start();

            _ = WaitForCompletionAsync();
        }

        private async System.Threading.Tasks.Task WaitForCompletionAsync()
        {
            try
            {
                // 等待几秒后停止轮询并更新 UI
                await System.Threading.Tasks.Task.Delay(5000);
            }
            catch { }

            Callable.From(() =>
            {
                _pollTimer?.Stop();
                _subscribeInProgress = false;
                RefreshRows();
                _hintLabel.Text = "订阅已提交，请关闭并重启游戏后再尝试加入房间。\n如需重新启用被禁用的 Mod：标题界面 → 设置 → 模组设置 → 勾选 → 重启。";
            }).CallDeferred();
        }

        private void OnPoll()
        {
            try
            {
                if (_amsPollMethod != null && _amsWorkshopSubscriberInstance != null)
                    _amsPollMethod.Invoke(_amsWorkshopSubscriberInstance, null);
            }
            catch { }
            RefreshRows();
        }

        private void RefreshRows()
        {
            foreach (var row in _subscribeRows)
            {
                if (row.FileId == 0) continue;
                if (_amsGetMethod == null || _amsWorkshopSubscriberInstance == null) continue;

                var job = _amsGetMethod.Invoke(_amsWorkshopSubscriberInstance, new object[] { row.FileId });
                if (job == null) continue;

                var jobType = job.GetType();
                var stateProp = jobType.GetProperty("State");
                if (stateProp != null)
                {
                    var stateVal = stateProp.GetValue(job);
                    var stateStr = stateVal?.ToString() ?? "Pending";
                    row.StatusLabel.Text = stateStr;

                    if (stateStr == "Installed")
                    {
                        row.Bar.Visible = true; row.Bar.MaxValue = 100; row.Bar.Value = 100;
                        row.SubBtn.Visible = false;
                    }
                    else if (stateStr == "Downloading" || stateStr == "WaitingInstall")
                    {
                        row.Bar.Visible = true;
                        var doneProp = jobType.GetProperty("BytesDownloaded");
                        var totalProp = jobType.GetProperty("BytesTotal");
                        if (doneProp != null && totalProp != null)
                        {
                            var done = (ulong)(doneProp.GetValue(job) ?? 0);
                            var total = (ulong)(totalProp.GetValue(job) ?? 0);
                            if (total > 0) { row.Bar.MaxValue = total; row.Bar.Value = Math.Min(done, total); }
                        }
                    }
                    else if (stateStr == "Failed" || stateStr == "TimedOut")
                    {
                        row.Bar.Visible = false;
                        row.SubBtn.Visible = true;
                    }
                }
            }
        }

        private void OnDisableSelected()
        {
            var ids = _disableRows.Where(r => r.Cb.ButtonPressed).Select(r => r.Id).ToList();
            if (ids.Count == 0) return;

            if (_amsApplyDisableMethod != null)
            {
                var idsArray = ids.ToArray();
                _amsApplyDisableMethod.Invoke(null, new[] { idsArray });
            }

            _hintLabel.Text = $"已禁用 {ids.Count} 个 Mod。重启游戏后生效。\n如需重新启用：标题界面 → 设置 → 模组设置 → 勾选 → 重启。";
        }
    }
}