using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

// The dialog is built 100% programmatically — no .tscn resource is loaded at
// runtime. Mod hosting environments do not always resolve `res://Scripts/.../X.cs`
// references inside packed scenes (the .cs files live in the mod DLL, not in
// the .pck), which would silently make GD.Load<PackedScene> return null and
// suppress the picker. Constructing nodes in C# avoids that path entirely.
public partial class LanConnectServerSelectionDialog : Window
{
    private VBoxContainer? _list;
    private LineEdit? _manualInput;
    private CheckBox? _autoConnectCheck;
    private Label? _statusLabel;

    private System.Collections.Generic.List<ServerListEntry> _entries = new();

    public event Action<string>? ServerChosen;

    public LanConnectServerSelectionDialog()
    {
        Title = "选择服务器";
        Size = new Vector2I(640, 480);
        InitialPosition = WindowInitialPosition.CenterPrimaryScreen;
        Exclusive = false;
        Unresizable = false;
        BuildUi();
    }

    private void BuildUi()
    {
        var margin = new MarginContainer
        {
            AnchorRight = 1f,
            AnchorBottom = 1f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        var header = new Label { Text = "选择服务器" };
        vbox.AddChild(header);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        vbox.AddChild(scroll);

        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list);

        var manualRow = new HBoxContainer();
        manualRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(manualRow);

        _manualInput = new LineEdit
        {
            PlaceholderText = "手动输入 https://...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        manualRow.AddChild(_manualInput);

        var manualButton = new Button { Text = "连接" };
        manualButton.Pressed += OnManualConnect;
        manualRow.AddChild(manualButton);

        var actionsRow = new HBoxContainer();
        actionsRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(actionsRow);

        _autoConnectCheck = new CheckBox
        {
            Text = "自动连接上次使用",
            ButtonPressed = LanConnectConfig.AutoConnectLastServer,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _autoConnectCheck.Toggled += (bool pressed) =>
        {
            LanConnectConfig.AutoConnectLastServer = pressed;
        };
        actionsRow.AddChild(_autoConnectCheck);

        var refreshButton = new Button { Text = "刷新" };
        refreshButton.Pressed += () => _ = RefreshAsync();
        actionsRow.AddChild(refreshButton);

        var resetButton = new Button { Text = "重置本地列表" };
        resetButton.Pressed += () =>
        {
            LanConnectKnownPeersCache.Reset();
            _ = RefreshAsync();
        };
        actionsRow.AddChild(resetButton);

        _statusLabel = new Label { Text = "" };
        vbox.AddChild(_statusLabel);

        CloseRequested += () => QueueFree();
    }

    public override void _Ready()
    {
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_statusLabel != null) _statusLabel.Text = "刷新中...";
        _entries = await LanConnectServerListBootstrap.GatherAsync();
        await LanConnectServerListBootstrap.PingAllAsync(_entries);
        Render();
        if (_statusLabel != null) _statusLabel.Text = $"共 {_entries.Count} 个，刷新完成";
    }

    private void Render()
    {
        if (_list == null) return;
        foreach (var child in _list.GetChildren()) child.QueueFree();

        var ordered = _entries
            .OrderByDescending(e => e.LastSuccessConnect ?? DateTime.MinValue)
            .ThenBy(e => e.Bucket)
            .ThenBy(e => e.Address);
        foreach (var e in ordered)
        {
            var btn = new Button
            {
                Text = $"[{(e.PingMs.HasValue ? e.PingMs.Value.ToString() + "ms" : "—")}] {e.DisplayName ?? e.Address}",
                CustomMinimumSize = new Vector2(560, 32),
                TooltipText = $"address: {e.Address}\nsource: {e.Source}",
            };
            string addr = e.Address;
            btn.Pressed += () => { ServerChosen?.Invoke(addr); QueueFree(); };
            _list.AddChild(btn);
        }
    }

    private void OnManualConnect()
    {
        string addr = (_manualInput?.Text ?? "").Trim();
        if (!string.IsNullOrEmpty(addr)) { ServerChosen?.Invoke(addr); QueueFree(); }
    }
}
