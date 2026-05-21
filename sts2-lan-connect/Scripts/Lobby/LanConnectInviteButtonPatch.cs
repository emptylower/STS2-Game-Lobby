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
    private const string ContinueRunInviteButtonName = "LanConnectContinueRunInviteButton";
    private const string NativeInviteContainerName = "InviteButtonContainer";
    private const string NativeInviteControlName = "InviteButton";
    private const float ContinueRunInviteLeft = 72f;
    private const float ContinueRunInviteTop = 96f;
    private const float ContinueRunInviteWidth = 200f;
    private const float ContinueRunInviteHeight = 50f;
    private const string HookedMetaKey = "sts2_lan_connect_invite_button_hooks";
    private const string ContinueRunHookedMetaKey = "sts2_lan_connect_continue_run_invite_button_hooks";
    private const string NativeInviteManagedMetaKey = "sts2_lan_connect_native_invite_button_managed";
    private const string NativeInviteControlHookedMetaKey = "sts2_lan_connect_native_invite_control_hooks";
    private static readonly string[] RemotePlayerContainerScenePaths =
    {
        "res://scenes/screens/remote_player_container.tscn",
        "res://scenes/screens/char_select/remote_player_container.tscn",
        "res://scenes/screens/character_select/remote_player_container.tscn"
    };
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

    internal static void ScheduleEnsureInviteButton(Control screen, string source)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        if (!screen.HasMeta(ContinueRunHookedMetaKey))
        {
            screen.SetMeta(ContinueRunHookedMetaKey, true);
            screen.Connect(Node.SignalName.TreeEntered, Callable.From(() => QueueEnsureContinueRunInviteButton(screen, "tree_entered")));
            screen.Connect(Node.SignalName.Ready, Callable.From(() => QueueEnsureContinueRunInviteButton(screen, "ready")));
            screen.Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(() => QueueEnsureContinueRunInviteButton(screen, "visibility_changed")));
        }

        QueueEnsureContinueRunInviteButton(screen, source);
    }

    private static void QueueEnsureInviteButton(NCharacterSelectScreen screen, string source)
    {
        Callable.From(() => TryEnsureInviteButton(screen, source)).CallDeferred();
    }

    private static void QueueEnsureContinueRunInviteButton(Control screen, string source)
    {
        Callable.From(() => TryEnsureContinueRunInviteButton(screen, source)).CallDeferred();
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

    private static void TryEnsureContinueRunInviteButton(Control screen, string source)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsNodeReady())
        {
            return;
        }

        Control? existing = screen.FindChild(ContinueRunInviteButtonName, recursive: true, owned: false) as Control;
        Control? nativeInviteContainer = existing == null ? FindNativeInviteContainer(screen) : FindNativeInviteContainer(existing);
        Control? nativeInviteControl = existing == null ? FindNativeInviteControl(screen, nativeInviteContainer) : FindNativeInviteControl(existing, nativeInviteContainer);
        NInvitePlayersButton? nativeInvite = nativeInviteControl as NInvitePlayersButton ?? FindNativeInviteButton(screen);
        bool shouldShow = LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true;
        if (!shouldShow)
        {
            if (existing != null)
            {
                existing.Visible = false;
            }

            HideManagedNativeInvite(nativeInvite, nativeInviteControl, nativeInviteContainer);
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

        if (nativeInviteControl != null)
        {
            if (existing != null)
            {
                existing.Visible = false;
            }

            RepurposeNativeInviteControl(nativeInviteControl, nativeInviteContainer);
            return;
        }

        if (existing != null)
        {
            existing.Visible = true;
            ReparentAndPositionContinueRunInvite(screen, existing);
            if (nativeInvite != null)
            {
                RepurposeNativeInviteButton(nativeInvite);
            }
            else if (nativeInviteControl != null)
            {
                RepurposeNativeInviteControl(nativeInviteControl, nativeInviteContainer);
            }
            return;
        }

        Control inviteControl = CreateContinueRunInviteControl();
        screen.AddChild(inviteControl);
        ReparentAndPositionContinueRunInvite(screen, inviteControl);
        Log.Info($"sts2_lan_connect: continue-run invite button created via {source}");
        QueueEnsureContinueRunInviteButton(screen, "continue_invite_created");
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

    private static Control? FindNativeInviteContainer(Node root)
    {
        return root.FindChild(NativeInviteContainerName, recursive: true, owned: false) as Control;
    }

    private static Control? FindNativeInviteControl(Node root, Control? container)
    {
        if (container?.FindChild(NativeInviteControlName, recursive: true, owned: false) is Control inviteControl)
        {
            return inviteControl;
        }

        return root.FindChild(NativeInviteControlName, recursive: true, owned: false) as Control;
    }

    private static void ReparentAndPositionContinueRunInvite(Control screen, Control inviteControl)
    {
        if (inviteControl.GetParent() != screen)
        {
            inviteControl.GetParent()?.RemoveChild(inviteControl);
            screen.AddChild(inviteControl);
        }

        inviteControl.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        inviteControl.AnchorLeft = 0f;
        inviteControl.AnchorRight = 0f;
        inviteControl.AnchorTop = 0f;
        inviteControl.AnchorBottom = 0f;
        inviteControl.OffsetLeft = ContinueRunInviteLeft;
        inviteControl.OffsetTop = ContinueRunInviteTop;
        inviteControl.OffsetRight = ContinueRunInviteLeft + ContinueRunInviteWidth;
        inviteControl.OffsetBottom = ContinueRunInviteTop + ContinueRunInviteHeight;
        inviteControl.CustomMinimumSize = new Vector2(ContinueRunInviteWidth, ContinueRunInviteHeight);
        inviteControl.MouseFilter = Control.MouseFilterEnum.Pass;
        inviteControl.ZIndex = 25;
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

    private static void RepurposeNativeInviteControl(Control nativeControl, Control? container)
    {
        nativeControl.SetMeta(NativeInviteManagedMetaKey, true);
        nativeControl.Visible = true;
        nativeControl.MouseFilter = Control.MouseFilterEnum.Stop;
        nativeControl.TooltipText = "生成邀请码并复制到剪贴板，发给朋友即可一键加入。";
        if (container != null)
        {
            container.Visible = true;
        }
        else if (nativeControl.GetParent() is CanvasItem parent)
        {
            parent.Visible = true;
        }

        if (nativeControl.GetNodeOrNull<MegaRichTextLabel>("Label") is { } label)
        {
            label.SetTextAutoSize("大厅邀请");
        }

        if (!nativeControl.HasMeta(NativeInviteControlHookedMetaKey))
        {
            nativeControl.SetMeta(NativeInviteControlHookedMetaKey, true);
            if (nativeControl is Button button)
            {
                button.Pressed += OnLobbyInvitePressed;
            }
            else
            {
                nativeControl.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(OnNativeInviteControlGuiInput));
            }
        }
    }

    private static void HideManagedNativeInvite(NInvitePlayersButton? nativeInvite, Control? nativeControl, Control? container)
    {
        if (nativeInvite != null && nativeInvite.HasMeta(NativeInviteManagedMetaKey))
        {
            nativeInvite.Visible = false;
        }

        if (nativeControl != null && nativeControl.HasMeta(NativeInviteManagedMetaKey))
        {
            nativeControl.Visible = false;
        }

        if (container != null && (nativeInvite?.HasMeta(NativeInviteManagedMetaKey) == true || nativeControl?.HasMeta(NativeInviteManagedMetaKey) == true))
        {
            container.Visible = false;
        }
    }

    private static void OnNativeInviteControlGuiInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
            case InputEventScreenTouch { Pressed: false }:
                OnLobbyInvitePressed();
                break;
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

    private static Control CreateContinueRunInviteControl()
    {
        Control? nativeClone = TryCloneNativeInviteContainer();
        if (nativeClone != null)
        {
            nativeClone.Name = ContinueRunInviteButtonName;
            nativeClone.Visible = true;
            return nativeClone;
        }

        return CreateFallbackContinueRunInviteControl();
    }

    private static Control? TryCloneNativeInviteContainer()
    {
        foreach (string scenePath in RemotePlayerContainerScenePaths)
        {
            try
            {
                if (!ResourceLoader.Exists(scenePath))
                {
                    continue;
                }

                PackedScene? scene = ResourceLoader.Load<PackedScene>(scenePath);
                Control? sceneRoot = scene?.Instantiate<Control>();
                Control? inviteContainer = sceneRoot == null ? null : FindNativeInviteContainer(sceneRoot);
                if (sceneRoot == null || inviteContainer == null)
                {
                    sceneRoot?.QueueFree();
                    continue;
                }

                inviteContainer.GetParent()?.RemoveChild(inviteContainer);
                sceneRoot.QueueFree();
                return inviteContainer;
            }
            catch (Exception ex)
            {
                Log.Warn($"sts2_lan_connect: failed to clone native invite button from {scenePath}: {ex.Message}");
            }
        }

        return null;
    }

    private static Control CreateFallbackContinueRunInviteControl()
    {
        Control container = new()
        {
            Name = ContinueRunInviteButtonName,
            CustomMinimumSize = new Vector2(0f, 50f),
            MouseFilter = Control.MouseFilterEnum.Pass
        };

        Button inviteButton = new()
        {
            Name = NativeInviteControlName,
            Text = "大厅邀请",
            TooltipText = "生成邀请码并复制到剪贴板，发给朋友即可一键加入。",
            CustomMinimumSize = new Vector2(ContinueRunInviteWidth, ContinueRunInviteHeight),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        Color bgColor = new(0.28f, 0.21f, 0.08f, 0.96f);
        Color hoverColor = new(0.37f, 0.28f, 0.11f, 0.98f);
        Color pressedColor = new(0.47f, 0.34f, 0.12f, 1f);
        Color borderColor = new(0.86f, 0.69f, 0.33f, 1f);
        Color textColor = new(0.96f, 0.94f, 0.88f, 1f);

        inviteButton.AddThemeColorOverride("font_color", textColor);
        inviteButton.AddThemeColorOverride("font_hover_color", textColor);
        inviteButton.AddThemeColorOverride("font_pressed_color", textColor);
        inviteButton.AddThemeStyleboxOverride("normal", CreateBorderedStyle(bgColor, borderColor));
        inviteButton.AddThemeStyleboxOverride("hover", CreateBorderedStyle(hoverColor, borderColor));
        inviteButton.AddThemeStyleboxOverride("pressed", CreateBorderedStyle(pressedColor, borderColor));
        inviteButton.AnchorLeft = 0.5f;
        inviteButton.AnchorRight = 0.5f;
        inviteButton.AnchorTop = 0f;
        inviteButton.AnchorBottom = 0f;
        inviteButton.OffsetLeft = 0f;
        inviteButton.OffsetRight = ContinueRunInviteWidth;
        inviteButton.OffsetTop = 0f;
        inviteButton.OffsetBottom = ContinueRunInviteHeight;
        container.Visible = true;
        container.AddChild(inviteButton);
        return container;
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

    private static StyleBoxFlat CreateBorderedStyle(Color bgColor, Color borderColor)
    {
        return new StyleBoxFlat
        {
            BgColor = bgColor,
            BorderColor = borderColor,
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ContentMarginLeft = 14,
            ContentMarginTop = 8,
            ContentMarginRight = 14,
            ContentMarginBottom = 8,
        };
    }

}
