using System;
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
            screen.Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(() => QueueEnsureLanJoinControls(screen, "visibility_changed")));
        }

        Callable.From(() => TryEnsureLanJoinControls(screen, source)).CallDeferred();
    }

    private static void QueueEnsureLanJoinControls(NJoinFriendScreen screen, string source)
    {
        Callable.From(() => TryEnsureLanJoinControls(screen, source)).CallDeferred();
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
            PlaceholderText = "输入 IPv4/IPv6:端口，例如 192.168.1.20:33771 或 [2001:db8::2]:33771",
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

        LanConnectConfig.LastEndpoint = raw;
        ulong netId = LanConnectNetUtil.GenerateClientNetId();
        ENetClientConnectionInitializer initializer = new(netId, ip, port);
        TaskHelper.RunSafely(screen.JoinGameAsync(initializer));
    }

    private static Control? FindJoinContainer(NJoinFriendScreen screen)
    {
        return screen.FindChild(LanConnectConstants.JoinContainerName, recursive: true, owned: false) as Control;
    }

    private static NMegaLineEdit? FindEndpointInput(NJoinFriendScreen screen)
    {
        return screen.FindChild(LanConnectConstants.EndpointInputName, recursive: true, owned: false) as NMegaLineEdit;
    }
}
