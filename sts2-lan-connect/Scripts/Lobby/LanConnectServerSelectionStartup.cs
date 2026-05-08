using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectServerSelectionStartup
{
    public static event Action<string>? ServerChosen;

    public static void Show(SceneTree tree)
    {
        try
        {
            var scene = GD.Load<PackedScene>("res://Scenes/Lobby/ServerSelectionDialog.tscn");
            if (scene == null)
            {
                Log.Warn("sts2_lan_connect: missing ServerSelectionDialog.tscn — falling back to default lobby");
                return;
            }

            var dlg = (LanConnectServerSelectionDialog)scene.Instantiate();
            dlg.ServerChosen += addr =>
            {
                LanConnectConfig.LobbyServerBaseUrl = addr;
                LanConnectConfig.LastUsedServerAddress = addr;
                ServerChosen?.Invoke(addr);
            };
            tree.Root.AddChild(dlg);
            dlg.PopupCentered();
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect server selection failed: {ex.Message}");
        }
    }
}
