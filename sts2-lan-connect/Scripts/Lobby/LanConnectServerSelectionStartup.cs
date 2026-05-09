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
            // Construct the dialog directly in C# — do not load a packed scene.
            // .tscn-based instantiation requires Godot to resolve the script
            // via res:// at runtime, which is not reliable inside mod hosts
            // where the .cs files live in the mod DLL but never on disk.
            var dlg = new LanConnectServerSelectionDialog();
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
