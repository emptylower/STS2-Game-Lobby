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

    public static void Show(
        SceneTree tree,
        Action<string> onPicked,
        Action? onCancelled = null,
        Action? onSettled = null) =>
        Show(
            tree,
            address =>
            {
                onPicked(address);
                return Task.CompletedTask;
            },
            onCancelled,
            onSettled);

    /// <summary>
    /// Show the picker as a top-level overlay over the current scene. Built
    /// programmatically — no PackedScene load — so script resolution can never
    /// fail inside the mod host.
    /// </summary>
    /// <param name="onPicked">Optional callback awaited AFTER Runtime has
    ///   switched servers and the global ServerChosen event has fired.</param>
    /// <param name="onCancelled">Optional callback fired AFTER the global
    ///   Cancelled event when the picker is dismissed without a selection.</param>
    /// <param name="onSettled">Optional callback fired exactly once after the
    ///   selection attempt succeeds, fails, is superseded, or is cancelled.
    ///   A blocked player name keeps the picker open for correction before settling.</param>
    public static void Show(
        SceneTree tree,
        Func<string, Task>? onPicked = null,
        Action? onCancelled = null,
        Action? onSettled = null)
    {
        int settled = 0;
        void SettleOnce()
        {
            if (Interlocked.Exchange(ref settled, 1) == 0)
            {
                onSettled?.Invoke();
            }
        }

        try
        {
            var dlg = new LanConnectServerSelectionDialog();
            dlg.ServerChosen += addr =>
            {
                TaskHelper.RunSafely(HandleServerPickedAsync(dlg, addr, onPicked, SettleOnce));
            };
            dlg.Cancelled += () =>
            {
                try
                {
                    Cancelled?.Invoke();
                    onCancelled?.Invoke();
                }
                finally
                {
                    SettleOnce();
                }
            };
            tree.Root.AddChild(dlg);
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect server selection failed: {ex.Message}");
            try
            {
                onCancelled?.Invoke();
            }
            finally
            {
                SettleOnce();
            }
        }
    }

    internal static async Task HandleServerPickedAsync(
        LanConnectServerSelectionDialog dialog,
        string address,
        Func<string, Task>? onPicked,
        Action onSettled,
        Func<string, Task>? switchServerForTests = null,
        Action<string>? showFailureForTests = null)
    {
        int generation = Interlocked.Increment(ref _switchGeneration);
        bool awaitingPlayerName = false;
        try
        {
            if (switchServerForTests != null)
            {
                await switchServerForTests(address);
            }
            else
            {
                LanConnectLobbyRuntime runtime = LanConnectLobbyRuntime.Instance ??
                    throw new InvalidOperationException("Lobby runtime is unavailable.");
                await runtime.SwitchLobbyServerAsync(address);
            }
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
        catch (LobbyServiceException ex) when (LanConnectModerationUiMessages.IsContentBlocked(ex))
        {
            if (generation == Volatile.Read(ref _switchGeneration) &&
                GodotObject.IsInstanceValid(dialog) && dialog.IsInsideTree() && !dialog.IsQueuedForDeletion())
            {
                awaitingPlayerName = true;
                dialog.ShowPlayerNameBlocked();
            }
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _switchGeneration))
            {
                return;
            }
            Log.Warn($"sts2_lan_connect server switch failed: {ex.Message}");
            string message = $"切换大厅服务器失败：{ex.Message}";
            if (showFailureForTests != null)
            {
                showFailureForTests(message);
            }
            else
            {
                LanConnectPopupUtil.ShowInfo(message);
            }
        }
        finally
        {
            if (!awaitingPlayerName)
            {
                if (GodotObject.IsInstanceValid(dialog))
                {
                    dialog.QueueFree();
                }
                onSettled();
            }
        }
    }
}
