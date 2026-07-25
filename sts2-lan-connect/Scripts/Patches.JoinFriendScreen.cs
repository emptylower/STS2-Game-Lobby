using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Sts2LanConnect.Scripts;

internal static class JoinFriendScreenPatches
{
    private const string HookedMetaKey = "sts2_lan_connect_join_hooks";
    private static readonly FieldInfo? StackField =
        typeof(NSubmenu).GetField("_stack", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? LoadingOverlayField =
        typeof(NJoinFriendScreen).GetField("_loadingOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly ConditionalWeakTable<NJoinFriendScreen, DirectJoinState> DirectJoinStates = new();

    internal static void EnsureLanJoinControls(NJoinFriendScreen screen)
    {
        try
        {
            InstallLanJoinControls(screen);
            RefreshStoredEndpoint(screen);
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect failed to set up LAN join UI: {ex}");
        }
    }

    internal static void ScheduleEnsureLanJoinControls(NJoinFriendScreen screen, string source)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        if (!screen.HasMeta(HookedMetaKey))
        {
            screen.SetMeta(HookedMetaKey, true);
            screen.Connect(Node.SignalName.TreeEntered, Callable.From(() => QueueEnsureLanJoinControls(screen, "tree_entered")));
            screen.Connect(Node.SignalName.Ready, Callable.From(() => QueueEnsureLanJoinControls(screen, "ready")));
            screen.Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(() => OnVisibilityChanged(screen)));
            screen.Connect(Node.SignalName.TreeExiting, Callable.From(() => CancelActiveJoin(screen)));
        }

        Callable.From(() => TryEnsureLanJoinControls(screen, source)).CallDeferred();
    }

    private static void QueueEnsureLanJoinControls(NJoinFriendScreen screen, string source)
    {
        Callable.From(() => TryEnsureLanJoinControls(screen, source)).CallDeferred();
    }

    private static void OnVisibilityChanged(NJoinFriendScreen screen)
    {
        if (!screen.Visible)
        {
            CancelActiveJoin(screen);
            return;
        }

        QueueEnsureLanJoinControls(screen, "visibility_changed");
    }

    private static void TryEnsureLanJoinControls(NJoinFriendScreen screen, string source)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsNodeReady())
        {
            return;
        }

        bool alreadyInstalled = FindJoinContainer(screen) != null;
        EnsureLanJoinControls(screen);
        if (!alreadyInstalled && FindJoinContainer(screen) != null)
        {
            Control buttonContainer = screen.GetNode<Control>("%ButtonContainer");
            Node? parent = buttonContainer.GetParent();
            Log.Info($"sts2_lan_connect injected LAN join UI via {source}; buttonContainer={buttonContainer.GetPath()}, parentType={parent?.GetType().FullName ?? "<null>"}");
        }
    }

    private static void InstallLanJoinControls(NJoinFriendScreen screen)
    {
        if (FindJoinContainer(screen) != null)
        {
            return;
        }

        Control buttonContainer = screen.GetNode<Control>("%ButtonContainer");
        Control parent = buttonContainer.GetParent<Control>();

        VBoxContainer container = new()
        {
            Name = LanConnectConstants.JoinContainerName,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };

        Label title = new()
        {
            Text = "LAN/IP 调试直连",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        NMegaLineEdit endpointInput = new()
        {
            Name = LanConnectConstants.EndpointInputName,
            PlaceholderText = "输入 IPv4 或 IPv4:端口，例如 192.168.1.20:33771",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Text = LanConnectConfig.LastEndpoint
        };

        Button joinButton = new()
        {
            Name = LanConnectConstants.JoinButtonName,
            Text = "调试直连",
            CustomMinimumSize = new Vector2(160f, 0f)
        };

        joinButton.Connect(Button.SignalName.Pressed, Callable.From(() => JoinByEndpoint(screen)));
        endpointInput.Connect(LineEdit.SignalName.TextSubmitted, Callable.From<string>(_ => JoinByEndpoint(screen)));

        row.AddChild(endpointInput);
        row.AddChild(joinButton);
        container.AddChild(title);
        container.AddChild(row);

        NMegaLineEdit resumeCodeInput = new()
        {
            Name = LanConnectConstants.ResumeCodeInputName,
            PlaceholderText = "旧存档续局时粘贴房主发来的 STS2LANRESUME 身份码（新游戏留空）",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Secret = false
        };
        resumeCodeInput.Connect(LineEdit.SignalName.TextSubmitted, Callable.From<string>(_ => JoinByEndpoint(screen)));
        container.AddChild(resumeCodeInput);

        parent.AddChild(container);
        parent.MoveChild(container, buttonContainer.GetIndex() + 1);
    }

    private static void RefreshStoredEndpoint(NJoinFriendScreen screen)
    {
        NMegaLineEdit? endpointInput = FindEndpointInput(screen);
        if (endpointInput != null && string.IsNullOrWhiteSpace(endpointInput.Text))
        {
            endpointInput.Text = LanConnectConfig.LastEndpoint;
        }
    }

    private static void JoinByEndpoint(NJoinFriendScreen screen)
    {
        NMegaLineEdit? endpointInput = FindEndpointInput(screen);
        if (endpointInput == null)
        {
            LanConnectPopupUtil.ShowInfo("LAN 输入框未找到，请重新打开加入页面。");
            return;
        }

        string raw = endpointInput.Text.Trim();
        if (!LanConnectNetUtil.TryParseEndpoint(raw, out string ip, out ushort port, out string error))
        {
            LanConnectPopupUtil.ShowInfo(error);
            return;
        }

        DirectJoinState state = DirectJoinStates.GetOrCreateValue(screen);
        if (state.ActiveTask is { IsCompleted: false })
        {
            Log.Warn("sts2_lan_connect lan_direct_join: ignored duplicate submit while a join is active.");
            return;
        }

        LanConnectConfig.LastEndpoint = raw;
        state.CancellationSource?.Dispose();
        state.CancellationSource = new CancellationTokenSource();
        Task task = JoinByEndpointAsync(screen, ip, port, state);
        state.ActiveTask = TaskHelper.RunSafely(task);
    }

    private static async Task JoinByEndpointAsync(
        NJoinFriendScreen screen,
        string ip,
        ushort port,
        DirectJoinState state)
    {
        SetJoinControlsEnabled(screen, enabled: false);
        if (LoadingOverlayField?.GetValue(screen) is Control loadingOverlay)
        {
            loadingOverlay.Visible = true;
        }

        try
        {
            if (StackField?.GetValue(screen) is not NSubmenuStack stack || screen.GetTree() is not SceneTree sceneTree)
            {
                LanConnectPopupUtil.ShowInfo("LAN 加入页面上下文未就绪，请重新打开加入页面后再试。");
                return;
            }

            ulong netId;
            string identitySource;
            string? resumeCode = FindResumeCodeInput(screen)?.Text.Trim();
            if (!string.IsNullOrWhiteSpace(resumeCode))
            {
                if (!LanConnectLanResumeCode.TryDecode(resumeCode, out LanConnectLanResumePayload payload, out string error))
                {
                    LanConnectPopupUtil.ShowInfo(error);
                    return;
                }

                netId = payload.PlayerNetId;
                identitySource = "lan_resume_code";
                Log.Info(
                    $"sts2_lan_connect lan_direct_join: selected saved slot from resume code saveKey={payload.SaveKey}, netId={netId}, character='{payload.CharacterName}', player='{payload.PlayerName}'");
            }
            else
            {
                netId = LanConnectConfig.GetOrCreateClientNetId();
                identitySource = "persistent_installation";
            }

            await LanConnectDirectJoinFlow.JoinAsync(
                stack,
                sceneTree,
                ip,
                port,
                netId,
                identitySource,
                state.CancellationSource?.Token ?? CancellationToken.None);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(screen))
            {
                if (LoadingOverlayField?.GetValue(screen) is Control activeLoadingOverlay)
                {
                    activeLoadingOverlay.Visible = false;
                }

                SetJoinControlsEnabled(screen, enabled: true);
            }

            state.CancellationSource?.Dispose();
            state.CancellationSource = null;
            state.ActiveTask = null;
        }
    }

    private static void SetJoinControlsEnabled(NJoinFriendScreen screen, bool enabled)
    {
        if (FindEndpointInput(screen) is { } endpointInput)
        {
            endpointInput.Editable = enabled;
        }

        if (FindResumeCodeInput(screen) is { } resumeCodeInput)
        {
            resumeCodeInput.Editable = enabled;
        }

        if (screen.FindChild(LanConnectConstants.JoinButtonName, recursive: true, owned: false) is Button joinButton)
        {
            joinButton.Disabled = !enabled;
        }
    }

    private static void CancelActiveJoin(NJoinFriendScreen screen)
    {
        if (DirectJoinStates.TryGetValue(screen, out DirectJoinState? state))
        {
            state.CancellationSource?.Cancel();
        }
    }

    private static Control? FindJoinContainer(NJoinFriendScreen screen)
    {
        return screen.FindChild(LanConnectConstants.JoinContainerName, recursive: true, owned: false) as Control;
    }

    private static NMegaLineEdit? FindEndpointInput(NJoinFriendScreen screen)
    {
        return screen.FindChild(LanConnectConstants.EndpointInputName, recursive: true, owned: false) as NMegaLineEdit;
    }

    private static NMegaLineEdit? FindResumeCodeInput(NJoinFriendScreen screen)
    {
        return screen.FindChild(LanConnectConstants.ResumeCodeInputName, recursive: true, owned: false) as NMegaLineEdit;
    }

    private sealed class DirectJoinState
    {
        public Task? ActiveTask { get; set; }

        public CancellationTokenSource? CancellationSource { get; set; }
    }
}
