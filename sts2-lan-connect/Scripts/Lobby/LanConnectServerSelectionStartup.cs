using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectServerSelectionStartup
{
    public static event Action<string>? ServerChosen;
    public static event Action? Cancelled;

    /// <summary>
    /// Show the picker as a top-level overlay over the current scene. Built
    /// programmatically — no PackedScene load — so script resolution can never
    /// fail inside the mod host.
    /// </summary>
    /// <param name="onPicked">Optional callback fired AFTER the global
    ///   ServerChosen event, useful when the caller wants to chain follow-up
    ///   work (e.g. opening the lobby overlay) once a server is picked.</param>
    /// <param name="onCancelled">Optional callback fired AFTER the global
    ///   Cancelled event when the picker is dismissed without a selection.</param>
    public static void Show(SceneTree tree, Action<string>? onPicked = null, Action? onCancelled = null)
    {
        try
        {
            var dlg = new LanConnectServerSelectionDialog();
            dlg.ServerChosen += addr =>
            {
                LanConnectConfig.LobbyServerBaseUrl = addr;
                LanConnectConfig.LastUsedServerAddress = addr;
                ServerChosen?.Invoke(addr);
                onPicked?.Invoke(addr);
            };
            dlg.Cancelled += () =>
            {
                Cancelled?.Invoke();
                onCancelled?.Invoke();
            };
            tree.Root.AddChild(dlg);
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect server selection failed: {ex.Message}");
            onCancelled?.Invoke();
        }
    }
}
