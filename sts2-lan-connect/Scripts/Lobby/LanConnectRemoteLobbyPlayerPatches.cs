using System;
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

    private static readonly Color DangerColor = new(0.80f, 0.15f, 0.18f, 0.85f);
    private static readonly Color DangerHoverColor = new(0.90f, 0.25f, 0.20f, 0.95f);
    private static readonly Color BorderColor = new(0.60f, 0.20f, 0.15f, 0.8f);
    private static readonly Color TextColor = new(0.99f, 0.97f, 0.93f, 1f);

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

    private static void EnsureKickButton(NRemoteLobbyPlayer player)
    {
        bool isHost = LanConnectLobbyRuntime.Instance?.HasActiveHostedRoom == true;
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

        ulong targetNetId = player.PlayerId;
        string targetName = LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(targetNetId) ?? targetNetId.ToString();
        kickButton.Pressed += () => OnLobbyKickPressed(targetNetId, targetName);

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

    private static void OnLobbyKickPressed(ulong targetNetId, string targetName)
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null || !runtime.HasActiveHostedRoom)
        {
            return;
        }

        string netIdStr = targetNetId.ToString();
        Log.Info($"sts2_lan_connect lobby_kick: kicking netId={netIdStr} name={targetName}");

        // 1. Send through control channel (server-side enforcement)
        TaskHelper.RunSafely(runtime.SendKickPlayerAsync(netIdStr, targetName));

        // 2. Delayed ENet disconnect — give WebSocket kicked message 1.5s to arrive first
        ScheduleDelayedDisconnect(runtime, targetNetId);
    }

    internal static void ScheduleDelayedDisconnect(LanConnectLobbyRuntime runtime, ulong targetNetId)
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

        tree.CreateTimer(1.5).Timeout += () =>
        {
            try
            {
                hostService.DisconnectClient(targetNetId, MegaCrit.Sts2.Core.Entities.Multiplayer.NetError.Quit, now: false);
                Log.Info($"sts2_lan_connect kick: delayed ENet disconnect for netId={targetNetId}");
            }
            catch (Exception ex)
            {
                Log.Warn($"sts2_lan_connect kick: delayed ENet disconnect failed: {ex.Message}");
            }
        };
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
