using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.addons.mega_text;

namespace Sts2LanConnect.Scripts;

internal static class MultiplayerSubmenuPatches
{
    private const int DuplicateWithoutSignals = 14;
    private const string HookedMetaKey = "sts2_lan_connect_multiplayer_hooks";
    private static readonly FieldInfo? LoadingOverlayField = typeof(NMultiplayerSubmenu).GetField("_loadingOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? StackField = typeof(NSubmenu).GetField("_stack", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void ScheduleEnsureLobbyEntry(NMultiplayerSubmenu submenu, string source)
    {
        if (!GodotObject.IsInstanceValid(submenu))
        {
            return;
        }

        if (!submenu.HasMeta(HookedMetaKey))
        {
            submenu.SetMeta(HookedMetaKey, true);
            submenu.Connect(Node.SignalName.TreeEntered, Callable.From(() => QueueEnsureLobbyEntry(submenu, "tree_entered")));
            submenu.Connect(Node.SignalName.Ready, Callable.From(() => QueueEnsureLobbyEntry(submenu, "ready")));
            submenu.Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(() => QueueEnsureLobbyEntry(submenu, "visibility_changed")));
        }

        Callable.From(() => TryEnsureLobbyEntry(submenu, source)).CallDeferred();
    }

    private static void QueueEnsureLobbyEntry(NMultiplayerSubmenu submenu, string source)
    {
        Callable.From(() => TryEnsureLobbyEntry(submenu, source)).CallDeferred();
    }

    private static void TryEnsureLobbyEntry(NMultiplayerSubmenu submenu, string source)
    {
        if (!GodotObject.IsInstanceValid(submenu) || !submenu.IsInsideTree() || !submenu.IsNodeReady())
        {
            return;
        }

        bool buttonAlreadyInstalled = FindLobbyEntryButton(submenu) != null;
        EnsureLobbyEntry(submenu);
        if (!buttonAlreadyInstalled && FindLobbyEntryButton(submenu) != null)
        {
            NSubmenuButton joinButton = submenu.GetNode<NSubmenuButton>("ButtonContainer/JoinButton");
            Node? parent = joinButton.GetParent();
            Log.Info($"sts2_lan_connect injected lobby entry via {source}; joinButton={joinButton.GetPath()}, parentType={parent?.GetType().FullName ?? "<null>"}");
        }
    }

    internal static void EnsureLobbyEntry(NMultiplayerSubmenu submenu)
    {
        try
        {
            PatchLoadAndAbandonButtons(submenu);
            NSubmenuButton joinButton = submenu.GetNode<NSubmenuButton>("ButtonContainer/JoinButton");
            NSubmenuButton? lobbyButton = FindLobbyEntryButton(submenu);
            if (lobbyButton == null)
            {
                Node parent = joinButton.GetParent();
                lobbyButton = joinButton.Duplicate(DuplicateWithoutSignals) as NSubmenuButton;
                if (lobbyButton == null)
                {
                    Log.Error("sts2_lan_connect failed to duplicate JoinButton for lobby entry.");
                    return;
                }

                lobbyButton.Name = LanConnectConstants.LobbyEntryButtonName;
                ConfigureLobbyEntryButton(lobbyButton);
                lobbyButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnLobbyPressed(submenu)));
                parent.AddChild(lobbyButton);
                parent.MoveChild(lobbyButton, joinButton.GetIndex() + 1);
            }

            lobbyButton.Visible = joinButton.Visible;
            lobbyButton.SetEnabled(joinButton.IsEnabled);

            if (FindLobbyOverlay(submenu) == null)
            {
                Control? loadingOverlay = LoadingOverlayField?.GetValue(submenu) as Control;
                NSubmenuStack? stack = StackField?.GetValue(submenu) as NSubmenuStack;
                if (loadingOverlay != null && stack != null)
                {
                    LanConnectLobbyOverlay overlay = new();
                    overlay.Initialize(submenu, joinButton, stack, loadingOverlay);
                    submenu.AddChild(overlay);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect failed to inject lobby entry: {ex}");
        }
    }

    private static void PatchLoadAndAbandonButtons(NMultiplayerSubmenu submenu)
    {
        NSubmenuButton loadButton = submenu.GetNode<NSubmenuButton>("ButtonContainer/LoadButton");
        NSubmenuButton abandonButton = submenu.GetNode<NSubmenuButton>("ButtonContainer/AbandonButton");
        bool hasRunSave = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.HasMultiplayerRunSave;
        bool intercept = LanConnectMultiplayerSaveCompatibility.ShouldInterceptOfficialLoadButtons();

        NSubmenuButton? safeLoadButton = FindSafeLoadButton(submenu);
        if (safeLoadButton == null)
        {
            safeLoadButton = DuplicateActionButton(loadButton, LanConnectConstants.SafeLoadButtonName);
            if (safeLoadButton != null)
            {
                safeLoadButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnSafeLoadPressed(submenu)));
            }
        }

        NSubmenuButton? safeAbandonButton = FindSafeAbandonButton(submenu);
        if (safeAbandonButton == null)
        {
            safeAbandonButton = DuplicateActionButton(abandonButton, LanConnectConstants.SafeAbandonButtonName);
            if (safeAbandonButton != null)
            {
                safeAbandonButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnSafeAbandonPressed(submenu)));
            }
        }

        if (safeLoadButton != null)
        {
            safeLoadButton.Visible = intercept && hasRunSave;
            safeLoadButton.SetEnabled(loadButton.IsEnabled);
        }

        if (safeAbandonButton != null)
        {
            safeAbandonButton.Visible = intercept && hasRunSave;
            safeAbandonButton.SetEnabled(abandonButton.IsEnabled);
        }

        loadButton.Visible = !intercept && hasRunSave;
        abandonButton.Visible = !intercept && hasRunSave;
    }

    private static NSubmenuButton? DuplicateActionButton(NSubmenuButton sourceButton, string name)
    {
        Node? parent = sourceButton.GetParent();
        if (parent == null)
        {
            Log.Error($"sts2_lan_connect failed to duplicate submenu button '{sourceButton.Name}' because parent is missing.");
            return null;
        }

        NSubmenuButton? clonedButton = sourceButton.Duplicate(DuplicateWithoutSignals) as NSubmenuButton;
        if (clonedButton == null)
        {
            Log.Error($"sts2_lan_connect failed to duplicate submenu button '{sourceButton.Name}'.");
            return null;
        }

        clonedButton.Name = name;
        parent.AddChild(clonedButton);
        parent.MoveChild(clonedButton, sourceButton.GetIndex() + 1);
        return clonedButton;
    }

    private static void OnLobbyPressed(NMultiplayerSubmenu submenu)
    {
        LanConnectLobbyOverlay? overlay = FindLobbyOverlay(submenu);
        if (overlay == null)
        {
            LanConnectPopupUtil.ShowInfo("大厅页面尚未准备好，请重新打开多人页面后再试。");
            return;
        }

        // Verification-phase policy: always show the picker on every entry,
        // even when the player already has a saved server. This makes the
        // decentralized discovery path visible/auditable on each launch — we'd
        // rather pay the extra click than silently keep falling back to the
        // last URL. Once decentralized discovery has been validated in the
        // wild, we can reintroduce an opt-in "auto-connect last" path here.
        SceneTree? tree = submenu.GetTree();
        if (tree == null)
        {
            // Defensive — should never happen, but if it does, don't strand
            // the player by failing to open the lobby.
            overlay.ShowOverlay();
            return;
        }

        try
        {
            string clipboardText = DisplayServer.ClipboardGet();
            LanConnectInviteEntryDecision inviteDecision = LanConnectInviteEntryDecider.Decide(clipboardText);
            if (inviteDecision.Action == LanConnectInviteEntryAction.ShowInvite)
            {
                GD.Print("sts2_lan_connect lobby entry: invite code detected, skipping server picker");
                overlay.ShowOverlay();
                return;
            }

            if (LanConnectLanResumeCode.TryDecode(clipboardText, out _, out _))
            {
                GD.Print("sts2_lan_connect lobby entry: LAN resume code detected in clipboard, showing usage hint");
                LanConnectPopupUtil.ShowInfo(
                    "检测到剪贴板里是 LAN 续局身份码，它不能直接用于联机大厅。\n" +
                    "· 房主是 LAN 直连续局：请打开“加入好友”页面，把身份码粘贴到续局身份码输入框，再填房主 IP 连接。\n" +
                    "· 想通过联机大厅续局：让房主在多人页面载入该存档（大厅房间会自动恢复），再把大厅邀请码发给你。");
            }
        }
        catch (Exception ex)
        {
            GD.Print($"sts2_lan_connect lobby entry: clipboard invite precheck failed -> {ex.Message}");
        }

        LanConnectServerSelectionStartup.Show(
            tree,
            onPicked: _ =>
            {
                overlay.ShowOverlay();
                return Task.CompletedTask;
            },
            onCancelled: null);
    }

    private static void OnSafeLoadPressed(NMultiplayerSubmenu submenu)
    {
        if (!LanConnectMultiplayerSaveCompatibility.TryStartLoadedRunAsLanHostFromSubmenu(submenu))
        {
            LanConnectPopupUtil.ShowInfo("多人续局页面上下文未就绪，请重新打开多人页面后再试。");
            return;
        }
    }

    private static void OnSafeAbandonPressed(NMultiplayerSubmenu submenu)
    {
        TaskHelper.RunSafely(LanConnectMultiplayerSaveCompatibility.AbandonCurrentRunAsync(submenu));
    }

    private static void ConfigureLobbyEntryButton(NSubmenuButton button)
    {
        MegaLabel title = button.GetNode<MegaLabel>("%Title");
        MegaRichTextLabel description = button.GetNode<MegaRichTextLabel>("%Description");
        title.SetTextAutoSize("游戏大厅");
        description.Text = "进入房间大厅，查看房间列表、创建房间，并直接走现有 ENet/JoinFlow 连接。";
    }

    private static NSubmenuButton? FindLobbyEntryButton(NMultiplayerSubmenu submenu)
    {
        return submenu.FindChild(LanConnectConstants.LobbyEntryButtonName, recursive: true, owned: false) as NSubmenuButton;
    }

    private static NSubmenuButton? FindSafeLoadButton(NMultiplayerSubmenu submenu)
    {
        return submenu.FindChild(LanConnectConstants.SafeLoadButtonName, recursive: true, owned: false) as NSubmenuButton;
    }

    private static NSubmenuButton? FindSafeAbandonButton(NMultiplayerSubmenu submenu)
    {
        return submenu.FindChild(LanConnectConstants.SafeAbandonButtonName, recursive: true, owned: false) as NSubmenuButton;
    }

    private static LanConnectLobbyOverlay? FindLobbyOverlay(NMultiplayerSubmenu submenu)
    {
        return submenu.FindChild(LanConnectConstants.LobbyOverlayName, recursive: true, owned: false) as LanConnectLobbyOverlay;
    }
}
