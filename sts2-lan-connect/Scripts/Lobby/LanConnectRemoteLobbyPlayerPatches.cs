using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectRemoteLobbyPlayerPatches
{
    private const string KickButtonName = "LanConnectKickButton";
    private const string KickTargetMetaKey = "sts2_lan_connect_kick_target";
    private const string RegisteredMetaKey = "sts2_lan_connect_remote_lobby_player_registered";
    private static readonly object RegistrySync = new();
    private static readonly Dictionary<ulong, NRemoteLobbyPlayer> RegisteredPlayers = new();
    private static bool _refreshQueued;

    private static readonly Color DangerColor = new(0.80f, 0.15f, 0.18f, 0.85f);
    private static readonly Color DangerHoverColor = new(0.90f, 0.25f, 0.20f, 0.95f);
    private static readonly Color BorderColor = new(0.60f, 0.20f, 0.15f, 0.8f);
    private static readonly Color TextColor = new(0.99f, 0.97f, 0.93f, 1f);

    internal static void RegisterAndRefresh(NRemoteLobbyPlayer player, string source)
    {
        if (!GodotObject.IsInstanceValid(player))
        {
            return;
        }

        lock (RegistrySync)
        {
            RegisteredPlayers[player.GetInstanceId()] = player;
        }

        if (!player.HasMeta(RegisteredMetaKey))
        {
            player.SetMeta(RegisteredMetaKey, true);
            player.Connect(Node.SignalName.TreeExiting, Callable.From(() => Unregister(player)));
        }

        Log.Info($"sts2_lan_connect remote_lobby_player: registered source={source} netId={player.PlayerId}");
        RefreshNameplate(player);
    }

    internal static void QueueRefreshAll()
    {
        lock (RegistrySync)
        {
            if (_refreshQueued)
            {
                return;
            }

            _refreshQueued = true;
        }

        Callable.From(RefreshAllRegistered).CallDeferred();
    }

    internal static void RefreshNameplate(NRemoteLobbyPlayer player)
    {
        if (!GodotObject.IsInstanceValid(player) || !player.IsInsideTree() || !player.IsNodeReady())
        {
            return;
        }

        string? resolvedName = LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(player.PlayerId);
        if (!string.IsNullOrWhiteSpace(resolvedName))
        {
            MegaLabel? label = player.GetNodeOrNull<MegaLabel>("%NameplateLabel");
            if (label != null && !string.Equals(label.Text, resolvedName, StringComparison.Ordinal))
            {
                label.SetTextAutoSize(resolvedName);
            }
        }

        EnsureKickButton(player);
    }

    private static void RefreshAllRegistered()
    {
        List<KeyValuePair<ulong, NRemoteLobbyPlayer>> players;
        lock (RegistrySync)
        {
            _refreshQueued = false;
            players = RegisteredPlayers.ToList();
        }

        foreach ((ulong instanceId, NRemoteLobbyPlayer player) in players)
        {
            if (!GodotObject.IsInstanceValid(player))
            {
                Unregister(instanceId);
                continue;
            }

            RefreshNameplate(player);
        }
    }

    private static void EnsureKickButton(NRemoteLobbyPlayer player)
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        bool isHost = runtime?.HasActiveHostedRoom == true;
        Button? existing = player.GetNodeOrNull<Button>(KickButtonName);

        // Don't show kick on the host's own entry or if not the host
        bool isPlayerTheHost = player.PlayerId == 1;
        if (!isHost || isPlayerTheHost)
        {
            if (existing != null)
            {
                existing.Visible = false;
            }

            return;
        }

        ulong targetNetId = player.PlayerId;
        LanConnectLobbyKickTarget target = runtime!.CaptureKickTarget(
            targetNetId.ToString(),
            ResolvePlayerName(targetNetId));
        if (existing != null &&
            !string.Equals(existing.GetMeta(KickTargetMetaKey).AsString(), target.Fingerprint, StringComparison.Ordinal))
        {
            player.RemoveChild(existing);
            existing.QueueFree();
            existing = null;
        }

        if (existing != null)
        {
            existing.Visible = true;
            RepositionKickButton(player, existing);
            return;
        }

        Button kickButton = new()
        {
            Name = KickButtonName,
            Text = "X",
            TooltipText = "踢出该玩家",
            CustomMinimumSize = new Vector2(42, 42),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        kickButton.SetMeta(KickTargetMetaKey, target.Fingerprint);
        kickButton.AddThemeColorOverride("font_color", TextColor);
        kickButton.AddThemeColorOverride("font_hover_color", TextColor);
        kickButton.AddThemeColorOverride("font_pressed_color", TextColor);
        kickButton.AddThemeFontSizeOverride("font_size", 18);

        StyleBoxFlat normal = CreateButtonStyle(DangerColor);
        StyleBoxFlat hover = CreateButtonStyle(DangerHoverColor);
        kickButton.AddThemeStyleboxOverride("normal", normal);
        kickButton.AddThemeStyleboxOverride("hover", hover);
        kickButton.AddThemeStyleboxOverride("pressed", hover);
        kickButton.AddThemeStyleboxOverride("focus", normal);

        kickButton.Pressed += () => TaskHelper.RunSafely(OnLobbyKickPressedAsync(targetNetId, target));

        // Add directly to the NRemoteLobbyPlayer control and position absolutely
        player.AddChild(kickButton);
        RepositionKickButton(player, kickButton);
        Log.Info($"sts2_lan_connect: kick button added for lobby player netId={targetNetId}");
    }

    private static void RepositionKickButton(NRemoteLobbyPlayer player, Button kickButton)
    {
        // Position the button to the right of the nameplate label
        MegaLabel? nameplate = player.GetNodeOrNull<MegaLabel>("%NameplateLabel");
        if (nameplate == null)
        {
            return;
        }

        // Get the nameplate's global position and size, then place button to its right
        Vector2 nameplatePos = nameplate.GetGlobalRect().Position - player.GetGlobalRect().Position;
        Vector2 nameplateSize = nameplate.GetGlobalRect().Size;

        kickButton.Position = new Vector2(
            nameplatePos.X + nameplateSize.X + 8,
            nameplatePos.Y + (nameplateSize.Y - 42) / 2
        );
    }

    private static async Task OnLobbyKickPressedAsync(
        ulong targetNetId,
        LanConnectLobbyKickTarget target)
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null || !runtime.HasActiveHostedRoom)
        {
            return;
        }

        Log.Info(
            $"sts2_lan_connect lobby_kick: requesting netId={target.PlayerNetId} "
            + $"bindingId={target.BindingId ?? "<legacy>"} name={target.OccupantName}");
        LanConnectLobbyKickResult result = await runtime.SendKickPlayerAsync(target);
        if (!result.ShouldScheduleDisconnect)
        {
            Log.Warn(
                $"sts2_lan_connect lobby_kick: rejected netId={target.PlayerNetId} "
                + $"bindingId={target.BindingId ?? "<legacy>"} reason={result.Reason}");
            LanConnectPopupUtil.ShowInfo(result.Message);
            return;
        }

        ScheduleDelayedDisconnect(runtime, targetNetId, target);
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            LanConnectPopupUtil.ShowInfo(result.Message);
        }
    }

    private static string ResolvePlayerName(ulong targetNetId)
    {
        return LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(targetNetId) ?? targetNetId.ToString();
    }

    internal static void ScheduleDelayedDisconnect(
        LanConnectLobbyRuntime runtime,
        ulong targetNetId,
        LanConnectLobbyKickTarget target)
    {
        NetHostGameService? hostService = runtime.GetHostNetService();
        if (hostService == null)
        {
            return;
        }

        SceneTree? tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
        {
            return;
        }
        Func<Action, bool> disconnectIfCurrent = runtime.CreateKickDisconnectAction(target);

        tree.CreateTimer(1.5).Timeout += () =>
        {
            try
            {
                bool disconnected = disconnectIfCurrent(() =>
                    hostService.DisconnectClient(
                        targetNetId,
                        MegaCrit.Sts2.Core.Entities.Multiplayer.NetError.Quit,
                        now: false));
                if (!disconnected)
                {
                    Log.Info(
                        $"sts2_lan_connect kick: delayed ENet disconnect cancelled for "
                        + $"netId={targetNetId} bindingId={target.BindingId ?? "<legacy>"}");
                    return;
                }
                Log.Info($"sts2_lan_connect kick: delayed ENet disconnect for netId={targetNetId}");
            }
            catch (Exception ex)
            {
                Log.Warn($"sts2_lan_connect kick: delayed ENet disconnect failed: {ex.Message}");
            }
        };
    }

    private static void Unregister(NRemoteLobbyPlayer player)
    {
        Unregister(player.GetInstanceId());
    }

    private static void Unregister(ulong instanceId)
    {
        lock (RegistrySync)
        {
            RegisteredPlayers.Remove(instanceId);
        }
    }

    private static StyleBoxFlat CreateButtonStyle(Color bgColor)
    {
        return new StyleBoxFlat
        {
            BgColor = bgColor,
            BorderColor = BorderColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 6,
            ContentMarginTop = 2,
            ContentMarginRight = 6,
            ContentMarginBottom = 2,
        };
    }
}
