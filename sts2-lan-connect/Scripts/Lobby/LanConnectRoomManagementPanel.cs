using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectRoomManagementPanel : CanvasLayer
{
    // Lobby warm color palette (matches LanConnectLobbyOverlay)
    private static readonly Color CardColor = new(0.99f, 0.97f, 0.93f, 1f);
    private static readonly Color BorderColor = new(0.80f, 0.65f, 0.53f, 1f);
    private static readonly Color AccentColor = new(0.87f, 0.41f, 0.00f, 1f);
    private static readonly Color TextStrongColor = new(0.21f, 0.10f, 0.04f, 1f);
    private static readonly Color TextMutedColor = new(0.46f, 0.36f, 0.31f, 1f);
    private static readonly Color DangerColor = new(0.80f, 0.15f, 0.18f, 1f);
    private static readonly Color DangerHoverColor = new(0.90f, 0.25f, 0.20f, 1f);
    private static readonly Color SecondaryColor = new(0.93f, 0.89f, 0.82f, 1f);
    private static readonly Color SurfaceMutedColor = new(0.89f, 0.87f, 0.81f, 1f);
    private static readonly Color SuccessColor = new(0.10f, 0.60f, 0.19f, 1f);

    private static LanConnectRoomManagementPanel? _instance;

    private VBoxContainer? _playerList;
    private CheckButton? _chatToggle;
    private Label? _statusLabel;
    private Button? _restartButton;
    private bool _restartInFlight;
    private int _lastChatEnabledRevision = -1;
    private int _lastPlayerListHash = -1;

    internal static void ShowPanel(Node parent)
    {
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
        {
            _instance.RefreshContent();
            _instance.Visible = true;
            return;
        }

        LanConnectRoomManagementPanel panel = new();
        panel.Name = LanConnectConstants.RoomManagementPanelName;
        parent.GetTree().Root.AddChild(panel);
        _instance = panel;
    }

    public override void _Ready()
    {
        Layer = 120;
        BuildUI();
        RefreshContent();
    }

    public override void _Process(double delta)
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null || !runtime.HasActiveRoomSession)
        {
            ClosePanel();
            return;
        }

        if (runtime.ChatEnabledRevision != _lastChatEnabledRevision)
        {
            _lastChatEnabledRevision = runtime.ChatEnabledRevision;
            if (_chatToggle != null)
            {
                _chatToggle.SetPressedNoSignal(runtime.ChatEnabled);
            }
        }

        RefreshPlayerList();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            ClosePanel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildUI()
    {
        // Full-screen dark veil
        Control shell = new();
        shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shell);

        ColorRect veil = new()
        {
            Color = new Color(0f, 0f, 0f, 0.45f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        veil.GuiInput += OnVeilInput;
        shell.AddChild(veil);

        // Centered dialog
        CenterContainer center = new();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shell.AddChild(center);

        // Pixel-art style card (sharp corners, border shadow — matching lobby)
        PanelContainer card = new()
        {
            CustomMinimumSize = new Vector2(620f, 0f),
        };
        StyleBoxFlat cardStyle = new()
        {
            BgColor = CardColor,
            BorderColor = BorderColor,
            BorderWidthLeft = 3,
            BorderWidthTop = 3,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
            CornerRadiusBottomRight = 0,
            CornerRadiusBottomLeft = 0,
            ContentMarginLeft = 28,
            ContentMarginTop = 24,
            ContentMarginRight = 28,
            ContentMarginBottom = 24,
            ShadowSize = 4,
        };
        cardStyle.ShadowColor = new Color(BorderColor, 0.55f);
        card.AddThemeStyleboxOverride("panel", cardStyle);
        center.AddChild(card);

        VBoxContainer body = new();
        body.AddThemeConstantOverride("separation", 16);
        card.AddChild(body);

        // Title
        Label title = new()
        {
            Text = "房间管理",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", AccentColor);
        title.AddThemeFontSizeOverride("font_size", 24);
        body.AddChild(title);

        body.AddChild(CreateSeparator());

        // Section: 房间设置
        Label chatSection = CreateSectionLabel("房间设置");
        body.AddChild(chatSection);

        HBoxContainer chatRow = new();
        chatRow.AddThemeConstantOverride("separation", 16);
        body.AddChild(chatRow);

        Label chatLabel = new()
        {
            Text = "房间聊天",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        chatLabel.AddThemeColorOverride("font_color", TextStrongColor);
        chatLabel.AddThemeFontSizeOverride("font_size", 18);
        chatRow.AddChild(chatLabel);

        bool isHost = LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true;

        _chatToggle = new CheckButton
        {
            ButtonPressed = LanConnectLobbyRuntime.Instance?.ChatEnabled ?? true,
            Disabled = !isHost,
            Text = "",
        };
        _chatToggle.Toggled += OnChatToggled;
        chatRow.AddChild(_chatToggle);

        if (!isHost)
        {
            Label hint = new()
            {
                Text = "仅房主可修改设置",
            };
            hint.AddThemeColorOverride("font_color", TextMutedColor);
            hint.AddThemeFontSizeOverride("font_size", 14);
            body.AddChild(hint);
        }

        body.AddChild(CreateSeparator());

        // Section: 在线玩家
        Label playerSection = CreateSectionLabel("在线玩家");
        body.AddChild(playerSection);

        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(0, 240),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddChild(scroll);

        _playerList = new VBoxContainer();
        _playerList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_playerList);

        // Status
        _statusLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _statusLabel.AddThemeColorOverride("font_color", TextMutedColor);
        _statusLabel.AddThemeFontSizeOverride("font_size", 14);
        body.AddChild(_statusLabel);

        body.AddChild(CreateSeparator());

        if (isHost)
        {
            _restartButton = CreatePixelButton("重开一局", AccentColor, DangerHoverColor, CardColor);
            _restartButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _restartButton.Pressed += OnRestartPressed;
            body.AddChild(_restartButton);
        }

        // Close button
        Button closeButton = CreatePixelButton("关闭", SecondaryColor, SurfaceMutedColor, TextStrongColor);
        closeButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        closeButton.Pressed += ClosePanel;
        body.AddChild(closeButton);
    }

    private void OnVeilInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            ClosePanel();
        }
    }

    private void RefreshContent()
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null)
        {
            return;
        }

        if (_chatToggle != null)
        {
            _chatToggle.SetPressedNoSignal(runtime.ChatEnabled);
            _chatToggle.Disabled = !runtime.HasActiveHostedRoom;
            _lastChatEnabledRevision = runtime.ChatEnabledRevision;
        }

        _lastPlayerListHash = -1;
        RefreshPlayerList();
    }

    private void RefreshPlayerList()
    {
        if (_playerList == null)
        {
            return;
        }

        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null)
        {
            return;
        }

        string? roomId = runtime.ActiveRoomId;
        if (roomId == null)
        {
            return;
        }

        // Only show currently connected players (host + connected peers)
        List<LobbyPlayerNameEntry> allPlayers = LanConnectLobbyPlayerNameDirectory.BuildSnapshot(roomId);
        bool isHost = runtime.HasActiveHostedRoom;
        ulong hostNetId = GetHostNetId();
        IReadOnlyCollection<ulong> connectedPeerIds = runtime.GetHostedRoomPeerIds();

        // Filter: keep host + currently connected peers only
        List<LobbyPlayerNameEntry> players = new();
        HashSet<string> seenNames = new();
        foreach (LobbyPlayerNameEntry entry in allPlayers)
        {
            if (!ulong.TryParse(entry.PlayerNetId, out ulong netId))
            {
                continue;
            }

            bool isCurrentlyConnected = netId == hostNetId || connectedPeerIds.Contains(netId);
            if (!isCurrentlyConnected)
            {
                continue;
            }

            // Deduplicate by netId (same player reconnecting gets different netId each time,
            // but only the active one is in connectedPeerIds)
            string dedupeKey = entry.PlayerNetId;
            if (!seenNames.Add(dedupeKey))
            {
                continue;
            }

            players.Add(entry);
        }

        int hash = ComputePlayerListHash(players);
        if (hash == _lastPlayerListHash)
        {
            return;
        }

        _lastPlayerListHash = hash;

        foreach (Node child in _playerList.GetChildren())
        {
            child.QueueFree();
        }

        foreach (LobbyPlayerNameEntry entry in players)
        {
            bool isPlayerHost = ulong.TryParse(entry.PlayerNetId, out ulong netId) && netId == hostNetId;

            PanelContainer rowBg = new();
            StyleBoxFlat rowStyle = new()
            {
                BgColor = SecondaryColor,
                BorderColor = BorderColor,
                BorderWidthLeft = 2,
                BorderWidthTop = 2,
                BorderWidthRight = 2,
                BorderWidthBottom = 2,
                ContentMarginLeft = 16,
                ContentMarginRight = 16,
                ContentMarginTop = 14,
                ContentMarginBottom = 14,
                ShadowSize = 2,
            };
            rowStyle.ShadowColor = new Color(BorderColor, 0.3f);
            rowBg.AddThemeStyleboxOverride("panel", rowStyle);
            _playerList.AddChild(rowBg);

            HBoxContainer rowContent = new();
            rowContent.AddThemeConstantOverride("separation", 12);
            rowBg.AddChild(rowContent);

            Label nameLabel = new()
            {
                Text = entry.PlayerName,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            nameLabel.AddThemeColorOverride("font_color", TextStrongColor);
            nameLabel.AddThemeFontSizeOverride("font_size", 18);
            rowContent.AddChild(nameLabel);

            if (isPlayerHost)
            {
                Label hostBadge = new()
                {
                    Text = "(房主)",
                };
                hostBadge.AddThemeColorOverride("font_color", AccentColor);
                hostBadge.AddThemeFontSizeOverride("font_size", 16);
                rowContent.AddChild(hostBadge);
            }
            else if (isHost)
            {
                // Larger button for mobile — easy to tap
                Button kickButton = CreatePixelButton("移出", DangerColor, DangerHoverColor, CardColor);
                kickButton.CustomMinimumSize = new Vector2(100, 48);
                string capturedNetId = entry.PlayerNetId;
                string capturedName = entry.PlayerName;
                kickButton.Pressed += () => OnKickPressed(capturedNetId, capturedName);
                rowContent.AddChild(kickButton);
            }
        }

        if (_statusLabel != null)
        {
            _statusLabel.Text = $"当前 {players.Count} 位玩家在线";
        }
    }

    private void OnChatToggled(bool pressed)
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null || !runtime.HasActiveHostedRoom)
        {
            return;
        }

        TaskHelper.RunSafely(runtime.SendRoomSettingsAsync(pressed));
        Log.Info($"sts2_lan_connect room_mgmt: chat toggled to {pressed}");
        if (_statusLabel != null)
        {
            _statusLabel.Text = pressed ? "聊天已启用" : "聊天已禁用";
        }
    }

    private void OnKickPressed(string playerNetId, string playerName)
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null || !runtime.HasActiveHostedRoom)
        {
            Log.Warn($"sts2_lan_connect room_mgmt: kick ignored, no active hosted room");
            return;
        }

        Log.Info($"sts2_lan_connect room_mgmt: kicking playerNetId={playerNetId} playerName={playerName}");

        // 1. Send kick through control channel (server-side enforcement)
        TaskHelper.RunSafely(runtime.SendKickPlayerAsync(playerNetId, playerName));

        // 2. Delayed ENet disconnect — give WebSocket kicked message 1.5s to arrive first
        if (ulong.TryParse(playerNetId, out ulong netId))
        {
            LanConnectRemoteLobbyPlayerPatches.ScheduleDelayedDisconnect(runtime, netId);
        }

        if (_statusLabel != null)
        {
            _statusLabel.Text = $"已将 {playerName} 移出房间";
        }

        _lastPlayerListHash = -1;
    }

    private void OnRestartPressed()
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null || !runtime.HasActiveHostedRoom)
        {
            return;
        }

        if (_restartInFlight)
        {
            return;
        }

        _restartInFlight = true;
        if (_restartButton != null)
        {
            _restartButton.Disabled = true;
        }

        if (_statusLabel != null)
        {
            _statusLabel.Text = "正在准备重开并通知队友...";
        }

        TaskHelper.RunSafely(StartRestartFlowAsync(runtime));
    }

    private async Task StartRestartFlowAsync(LanConnectLobbyRuntime runtime)
    {
        try
        {
            bool started = await runtime.StartHostedRunRestartAsync();
            if (!started && _statusLabel != null)
            {
                _statusLabel.Text = "重开启动失败，请查看提示信息。";
            }
        }
        finally
        {
            _restartInFlight = false;
            if (_restartButton != null && GodotObject.IsInstanceValid(_restartButton))
            {
                _restartButton.Disabled = false;
            }
        }
    }

    private void ClosePanel()
    {
        if (GodotObject.IsInstanceValid(this))
        {
            QueueFree();
        }

        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private static ulong GetHostNetId()
    {
        // The host's own NetId is always 1 in ENet host mode
        return 1;
    }

    private static NetHostGameService? GetHostNetService(LanConnectLobbyRuntime runtime)
    {
        try
        {
            return runtime.GetHostNetService();
        }
        catch
        {
            return null;
        }
    }

    private static int ComputePlayerListHash(List<LobbyPlayerNameEntry> players)
    {
        int hash = players.Count;
        foreach (LobbyPlayerNameEntry entry in players)
        {
            hash = HashCode.Combine(hash, entry.PlayerNetId, entry.PlayerName);
        }

        return hash;
    }

    private static Label CreateSectionLabel(string text)
    {
        Label label = new()
        {
            Text = text,
        };
        label.AddThemeColorOverride("font_color", TextMutedColor);
        label.AddThemeFontSizeOverride("font_size", 15);
        return label;
    }

    private static HSeparator CreateSeparator()
    {
        HSeparator sep = new();
        StyleBoxFlat style = new()
        {
            BgColor = new Color(BorderColor, 0.4f),
            ContentMarginTop = 1,
            ContentMarginBottom = 1,
        };
        sep.AddThemeStyleboxOverride("separator", style);
        return sep;
    }

    private static Button CreatePixelButton(string text, Color bgColor, Color hoverColor, Color textColor)
    {
        Button button = new()
        {
            Text = text,
        };
        button.AddThemeColorOverride("font_color", textColor);
        button.AddThemeColorOverride("font_hover_color", textColor);
        button.AddThemeColorOverride("font_pressed_color", textColor);
        button.AddThemeFontSizeOverride("font_size", 16);

        StyleBoxFlat normal = CreatePixelStyleBox(bgColor, BorderColor, 2, 12);
        StyleBoxFlat hover = CreatePixelStyleBox(hoverColor, BorderColor, 2, 12);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", normal);
        return button;
    }

    private static StyleBoxFlat CreatePixelStyleBox(Color bgColor, Color borderColor, int borderWidth, int padding)
    {
        StyleBoxFlat style = new()
        {
            BgColor = bgColor,
            BorderColor = borderColor,
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
        };
        style.ShadowColor = new Color(borderColor, 0.4f);
        style.ShadowSize = 2;
        return style;
    }
}
