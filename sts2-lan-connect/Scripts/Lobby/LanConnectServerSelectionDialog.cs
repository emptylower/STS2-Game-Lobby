using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

public partial class LanConnectServerSelectionDialog : Window
{
    private VBoxContainer? _list;
    private Button? _refreshButton;
    private Button? _manualButton;
    private LineEdit? _manualInput;
    private CheckBox? _autoConnectCheck;
    private Label? _statusLabel;

    private System.Collections.Generic.List<ServerListEntry> _entries = new();

    public event Action<string>? ServerChosen;

    public override void _Ready()
    {
        Title = "选择服务器";
        _list = GetNode<VBoxContainer>("%ServerList");
        _refreshButton = GetNode<Button>("%RefreshButton");
        _manualButton = GetNode<Button>("%ManualButton");
        _manualInput = GetNode<LineEdit>("%ManualInput");
        _autoConnectCheck = GetNode<CheckBox>("%AutoConnectCheck");
        _statusLabel = GetNode<Label>("%StatusLabel");

        _refreshButton.Pressed += () => _ = RefreshAsync();
        _manualButton.Pressed += OnManualConnect;
        CloseRequested += () => QueueFree();

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
                ToolTipText = $"address: {e.Address}\nsource: {e.Source}",
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
