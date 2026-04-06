using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectInviteButtonPatch
{
    private const string InviteButtonName = "LanConnectLobbyInviteButton";

    internal static void EnsureInviteButton(NCharacterSelectScreen screen)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsNodeReady())
        {
            return;
        }

        bool isLobbyHost = LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true;

        Button? existing = screen.FindChild(InviteButtonName, recursive: true, owned: false) as Button;

        if (!isLobbyHost)
        {
            if (existing != null)
            {
                existing.Visible = false;
            }
            return;
        }

        if (existing != null)
        {
            existing.Visible = true;
            return;
        }

        // Try to find the native invite button to clone its position/style
        Button? nativeInvite = FindNativeInviteButton(screen);
        if (nativeInvite != null)
        {
            // Native button exists — repurpose it
            RepurposeNativeInviteButton(nativeInvite);
            return;
        }

        // Native button not found — create our own in the same area
        CreateLobbyInviteButton(screen);
    }

    private static Button? FindNativeInviteButton(NCharacterSelectScreen screen)
    {
        return FindButtonByText(screen, "邀请");
    }

    private static Button? FindButtonByText(Node root, string text)
    {
        if (root is Button button && button.Text.Contains(text, StringComparison.Ordinal))
        {
            return button;
        }

        foreach (Node child in root.GetChildren())
        {
            Button? found = FindButtonByText(child, text);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void RepurposeNativeInviteButton(Button nativeButton)
    {
        // Disconnect all existing signals and reconnect to our invite logic
        nativeButton.Text = "大厅邀请";

        // Remove existing Pressed connections by setting a meta flag
        if (!nativeButton.HasMeta(InviteButtonName))
        {
            nativeButton.SetMeta(InviteButtonName, true);

            // We can't easily disconnect anonymous signal handlers.
            // Instead, connect our handler and let it run — the native handler
            // (Steam invite) will be a no-op when there's no Steam lobby session.
            nativeButton.Pressed += OnLobbyInvitePressed;
        }

        nativeButton.Visible = true;
    }

    private static void CreateLobbyInviteButton(NCharacterSelectScreen screen)
    {
        Button inviteButton = new()
        {
            Name = InviteButtonName,
            Text = "大厅邀请",
            TooltipText = "生成邀请码并复制到剪贴板，发给朋友即可一键加入。",
            CustomMinimumSize = new Vector2(140f, 42f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        // Match the native invite button style (teal/cyan)
        Color tealColor = new(0.17f, 0.73f, 0.71f, 1f);
        Color tealHoverColor = new(0.22f, 0.80f, 0.78f, 1f);
        Color tealPressedColor = new(0.12f, 0.60f, 0.58f, 1f);
        Color textColor = new(1f, 1f, 1f, 1f);

        inviteButton.AddThemeColorOverride("font_color", textColor);
        inviteButton.AddThemeColorOverride("font_hover_color", textColor);
        inviteButton.AddThemeColorOverride("font_pressed_color", textColor);
        inviteButton.AddThemeFontSizeOverride("font_size", 18);

        StyleBoxFlat normal = CreateRoundedStyle(tealColor);
        StyleBoxFlat hover = CreateRoundedStyle(tealHoverColor);
        StyleBoxFlat pressed = CreateRoundedStyle(tealPressedColor);
        inviteButton.AddThemeStyleboxOverride("normal", normal);
        inviteButton.AddThemeStyleboxOverride("hover", hover);
        inviteButton.AddThemeStyleboxOverride("pressed", pressed);
        inviteButton.AddThemeStyleboxOverride("focus", normal);

        inviteButton.Pressed += OnLobbyInvitePressed;

        // Position: top-left area, below the player list.
        // The player list starts around y=50. Place the button below it.
        // Use absolute positioning since the screen is a plain Control.
        inviteButton.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        inviteButton.Position = new Vector2(30f, 180f);

        screen.AddChild(inviteButton);
        Log.Info("sts2_lan_connect: lobby invite button created on character select screen");
    }

    private static void OnLobbyInvitePressed()
    {
        try
        {
            LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
            if (runtime == null || !runtime.HasActiveHostedRoom)
            {
                LanConnectPopupUtil.ShowInfo("当前没有托管中的房间，无法生成邀请码。");
                return;
            }

            string serverBaseUrl = LanConnectConfig.LobbyServerBaseUrl;
            string? roomId = runtime.ActiveRoomId;
            if (string.IsNullOrWhiteSpace(roomId))
            {
                LanConnectPopupUtil.ShowInfo("房间ID不可用，无法生成邀请码。");
                return;
            }

            string? password = runtime.GetHostedRoomPassword();
            string inviteCode = LanConnectInviteCode.Encode(serverBaseUrl, roomId, password);
            DisplayServer.ClipboardSet(inviteCode);
            GD.Print($"sts2_lan_connect: lobby invite code copied to clipboard for roomId={roomId}");
            LanConnectPopupUtil.ShowInfo("邀请码已复制到剪贴板。\n发给朋友，对方打开大厅后会自动提示加入。");
        }
        catch (Exception ex)
        {
            GD.Print($"sts2_lan_connect: failed to copy invite code from character select -> {ex}");
            LanConnectPopupUtil.ShowInfo($"复制邀请码失败：{ex.Message}");
        }
    }

    private static StyleBoxFlat CreateRoundedStyle(Color bgColor)
    {
        return new StyleBoxFlat
        {
            BgColor = bgColor,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 16,
            ContentMarginTop = 8,
            ContentMarginRight = 16,
            ContentMarginBottom = 8,
        };
    }
}
