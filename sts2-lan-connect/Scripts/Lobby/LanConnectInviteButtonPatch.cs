using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.addons.mega_text;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectInviteButtonPatch
{
    private const string InviteButtonName = "LanConnectLobbyInviteButton";
    private const string HookedMetaKey = "sts2_lan_connect_invite_button_hooks";
    private const string NativeInviteManagedMetaKey = "sts2_lan_connect_native_invite_button_managed";
    private static readonly Harmony NativeInviteHarmony = new("sts2_lan_connect.invite_button");
    private static bool _nativeInvitePatched;

    internal static void ApplyNativeInvitePatches()
    {
        if (_nativeInvitePatched)
        {
            return;
        }

        _nativeInvitePatched = true;
        TryPatch(
            AccessTools.DeclaredMethod(typeof(NInvitePlayersButton), "UpdateVisibility"),
            prefix: null,
            postfix: new HarmonyMethod(typeof(LanConnectInviteButtonPatch), nameof(OnNativeInviteUpdateVisibilityPostfix)),
            "NInvitePlayersButton.UpdateVisibility");
        TryPatch(
            AccessTools.DeclaredMethod(typeof(NInvitePlayersButton), "OnRelease"),
            prefix: new HarmonyMethod(typeof(LanConnectInviteButtonPatch), nameof(OnNativeInviteReleasePrefix)),
            postfix: null,
            "NInvitePlayersButton.OnRelease");
    }

    private static void TryPatch(MethodInfo? target, HarmonyMethod? prefix, HarmonyMethod? postfix, string label)
    {
        if (target == null)
        {
            Log.Warn($"sts2_lan_connect invite_button: target method not found, skipping patch {label}");
            return;
        }

        try
        {
            NativeInviteHarmony.Patch(target, prefix: prefix, postfix: postfix);
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect invite_button: failed to patch {label}: {ex}");
        }
    }

    internal static void ScheduleEnsureInviteButton(NCharacterSelectScreen screen, string source)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        if (!screen.HasMeta(HookedMetaKey))
        {
            screen.SetMeta(HookedMetaKey, true);
            screen.Connect(Node.SignalName.TreeEntered, Callable.From(() => QueueEnsureInviteButton(screen, "tree_entered")));
            screen.Connect(Node.SignalName.Ready, Callable.From(() => QueueEnsureInviteButton(screen, "ready")));
            screen.Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(() => QueueEnsureInviteButton(screen, "visibility_changed")));
        }

        QueueEnsureInviteButton(screen, source);
    }

    private static void QueueEnsureInviteButton(NCharacterSelectScreen screen, string source)
    {
        Callable.From(() => TryEnsureInviteButton(screen, source)).CallDeferred();
    }

    private static void TryEnsureInviteButton(NCharacterSelectScreen screen, string source)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsNodeReady())
        {
            return;
        }

        bool hadButton = HasManagedLobbyInviteButton(screen);
        EnsureInviteButton(screen);
        if (!hadButton && HasManagedLobbyInviteButton(screen))
        {
            Log.Info($"sts2_lan_connect: invite button ensured via {source}");
        }
    }

    internal static void EnsureInviteButton(NCharacterSelectScreen screen)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsNodeReady())
        {
            return;
        }

        bool isLobbyHost = LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true;
        Button? existing = screen.FindChild(InviteButtonName, recursive: true, owned: false) as Button;
        NInvitePlayersButton? nativeInvite = FindNativeInviteButton(screen);

        if (!isLobbyHost)
        {
            if (existing != null)
            {
                existing.Visible = false;
            }
            if (nativeInvite != null && nativeInvite.HasMeta(NativeInviteManagedMetaKey))
            {
                nativeInvite.Visible = false;
                if (nativeInvite.GetParent() is CanvasItem container)
                {
                    container.Visible = false;
                }
            }
            return;
        }

        if (nativeInvite != null)
        {
            if (existing != null)
            {
                existing.Visible = false;
            }
            RepurposeNativeInviteButton(nativeInvite);
            return;
        }

        if (existing != null)
        {
            existing.Visible = true;
            return;
        }

        // Fallback: game scene layout changed or native invite control missing.
        CreateLobbyInviteButton(screen);
    }

    private static bool HasManagedLobbyInviteButton(NCharacterSelectScreen screen)
    {
        if (screen.FindChild(InviteButtonName, recursive: true, owned: false) is Button)
        {
            return true;
        }

        NInvitePlayersButton? nativeInvite = FindNativeInviteButton(screen);
        return nativeInvite != null && nativeInvite.HasMeta(NativeInviteManagedMetaKey);
    }

    private static NInvitePlayersButton? FindNativeInviteButton(Node root)
    {
        if (root is NInvitePlayersButton nativeInvite)
        {
            return nativeInvite;
        }

        foreach (Node child in root.GetChildren())
        {
            NInvitePlayersButton? found = FindNativeInviteButton(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool OnNativeInviteReleasePrefix(NInvitePlayersButton __instance)
    {
        if (!ShouldInterceptNativeInviteRelease(__instance))
        {
            return true;
        }

        OnLobbyInvitePressed();
        return false;
    }

    private static void OnNativeInviteUpdateVisibilityPostfix(NInvitePlayersButton __instance)
    {
        if (!ShouldForceNativeInviteVisible(__instance))
        {
            return;
        }

        RepurposeNativeInviteButton(__instance);
    }

    private static bool ShouldInterceptNativeInviteRelease(Node inviteButtonNode)
    {
        return GodotObject.IsInstanceValid(inviteButtonNode)
               && inviteButtonNode.IsInsideTree()
               && (inviteButtonNode.HasMeta(NativeInviteManagedMetaKey)
                   || LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true);
    }

    private static bool ShouldForceNativeInviteVisible(Node inviteButtonNode)
    {
        return GodotObject.IsInstanceValid(inviteButtonNode)
               && inviteButtonNode.IsInsideTree()
               && LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true;
    }

    private static void RepurposeNativeInviteButton(NInvitePlayersButton nativeButton)
    {
        nativeButton.SetMeta(NativeInviteManagedMetaKey, true);
        nativeButton.Visible = true;
        nativeButton.TooltipText = "生成邀请码并复制到剪贴板，发给朋友即可一键加入。";
        if (nativeButton.GetParent() is Control container)
        {
            container.Visible = true;
        }

        if (nativeButton.GetNodeOrNull<MegaRichTextLabel>("Label") is { } label)
        {
            label.SetTextAutoSize("大厅邀请");
        }
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
