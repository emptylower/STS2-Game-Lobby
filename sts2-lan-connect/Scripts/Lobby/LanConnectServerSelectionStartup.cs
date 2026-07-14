using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectServerSelectionStartup
{
    private static int _switchGeneration;

    public static event Action<string>? ServerChosen;
    public static event Action? Cancelled;

    public static void Show(SceneTree tree, Action<string> onPicked, Action? onCancelled = null) =>
        Show(
            tree,
            address =>
            {
                onPicked(address);
                return Task.CompletedTask;
            },
            onCancelled);

    /// <summary>
    /// Show the picker as a top-level overlay over the current scene. Built
    /// programmatically — no PackedScene load — so script resolution can never
    /// fail inside the mod host.
    /// </summary>
    /// <param name="onPicked">Optional callback awaited AFTER Runtime has
    ///   switched servers and the global ServerChosen event has fired.</param>
    /// <param name="onCancelled">Optional callback fired AFTER the global
    ///   Cancelled event when the picker is dismissed without a selection.</param>
    public static void Show(SceneTree tree, Func<string, Task>? onPicked = null, Action? onCancelled = null)
    {
        try
        {
            var dlg = new LanConnectServerSelectionDialog();
            dlg.ServerChosen += addr =>
            {
                int generation = Interlocked.Increment(ref _switchGeneration);
                TaskHelper.RunSafely(HandleServerPickedAsync(addr, onPicked, generation));
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

    private static async Task HandleServerPickedAsync(
        string address,
        Func<string, Task>? onPicked,
        int generation)
    {
        try
        {
            LanConnectLobbyRuntime runtime = LanConnectLobbyRuntime.Instance ??
                throw new InvalidOperationException("Lobby runtime is unavailable.");
            await runtime.SwitchLobbyServerAsync(address);
            if (generation != Volatile.Read(ref _switchGeneration))
            {
                return;
            }
            ServerChosen?.Invoke(address);
            if (onPicked != null)
            {
                await onPicked(address);
            }
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _switchGeneration))
            {
                return;
            }
            Log.Warn($"sts2_lan_connect server switch failed: {ex.Message}");
            LanConnectPopupUtil.ShowInfo($"切换大厅服务器失败：{ex.Message}");
        }
    }
}
