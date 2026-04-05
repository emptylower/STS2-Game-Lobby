using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.addons.mega_text;

namespace Sts2LanConnect.Scripts;

internal static class PauseMenuPatches
{
    private const int DuplicateWithoutSignals = 14;
    private const string HookedMetaKey = "sts2_lan_connect_pause_menu_hooks";

    private static NPauseMenu? _lastPauseMenu;
    private static NPauseMenuButton? _roomManagementButton;

    internal static void ScheduleEnsureRoomManagementButton(NPauseMenu pauseMenu, string source)
    {
        if (!pauseMenu.HasMeta(HookedMetaKey))
        {
            pauseMenu.SetMeta(HookedMetaKey, true);
            pauseMenu.Connect(Node.SignalName.TreeEntered, Callable.From(() =>
            {
                Callable.From(() => TryEnsureRoomManagementButton(pauseMenu)).CallDeferred();
            }));
            pauseMenu.Connect(Node.SignalName.Ready, Callable.From(() =>
            {
                Callable.From(() => TryEnsureRoomManagementButton(pauseMenu)).CallDeferred();
            }));
            pauseMenu.Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(() =>
            {
                Callable.From(() => TryEnsureRoomManagementButton(pauseMenu)).CallDeferred();
            }));
        }

        Callable.From(() => TryEnsureRoomManagementButton(pauseMenu)).CallDeferred();
    }

    private static void TryEnsureRoomManagementButton(NPauseMenu pauseMenu)
    {
        if (!GodotObject.IsInstanceValid(pauseMenu) || !pauseMenu.IsInsideTree() || !pauseMenu.IsNodeReady())
        {
            return;
        }

        EnsureRoomManagementButton(pauseMenu);
    }

    private static void EnsureRoomManagementButton(NPauseMenu pauseMenu)
    {
        Control? buttonContainer = pauseMenu.GetNodeOrNull<Control>("%ButtonContainer");
        if (buttonContainer == null)
        {
            return;
        }

        NPauseMenuButton? existingButton = buttonContainer.GetNodeOrNull<NPauseMenuButton>(LanConnectConstants.RoomManagementButtonName);
        if (existingButton != null)
        {
            RefreshVisibility(existingButton);
            return;
        }

        NPauseMenuButton? compendiumButton = buttonContainer.GetNodeOrNull<NPauseMenuButton>("Compendium");
        if (compendiumButton == null)
        {
            return;
        }

        NPauseMenuButton roomMgmtButton = (NPauseMenuButton)compendiumButton.Duplicate(DuplicateWithoutSignals);
        roomMgmtButton.Name = LanConnectConstants.RoomManagementButtonName;

        MegaLabel? label = roomMgmtButton.GetNodeOrNull<MegaLabel>("Label");
        if (label != null)
        {
            label.Text = "房间管理";
        }

        int compendiumIndex = compendiumButton.GetIndex();
        buttonContainer.AddChild(roomMgmtButton);
        buttonContainer.MoveChild(roomMgmtButton, compendiumIndex + 1);

        roomMgmtButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnRoomManagementPressed(pauseMenu)));

        _lastPauseMenu = pauseMenu;
        _roomManagementButton = roomMgmtButton;
        RefreshVisibility(roomMgmtButton);
        Log.Info("sts2_lan_connect: room management button installed in pause menu.");
    }

    private static void RefreshVisibility(NPauseMenuButton button)
    {
        bool hasSession = LanConnectLobbyRuntime.Instance?.HasActiveRoomSession == true;
        button.Visible = hasSession;
    }

    private static void OnRoomManagementPressed(NPauseMenu pauseMenu)
    {
        LanConnectLobbyRuntime? runtime = LanConnectLobbyRuntime.Instance;
        if (runtime == null || !runtime.HasActiveRoomSession)
        {
            return;
        }

        LanConnectRoomManagementPanel.ShowPanel(pauseMenu);
    }
}
