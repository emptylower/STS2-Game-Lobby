using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectRuntimeMonitor : Node
{
    private const string MonitorName = "Sts2LanConnectRuntimeMonitor";
    private const double ScanIntervalSeconds = 0.25d;

    private double _timeUntilScan;

    internal static void Install()
    {
        Callable.From(InstallDeferred).CallDeferred();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _timeUntilScan = 0d;
        LanConnectSaveDiagnostics.LogNow("runtime_monitor_ready");
        Log.Info("sts2_lan_connect runtime monitor ready.");
    }

    public override void _Process(double delta)
    {
        _timeUntilScan -= delta;
        if (_timeUntilScan > 0d)
        {
            return;
        }

        _timeUntilScan = ScanIntervalSeconds;
        LanConnectSaveDiagnostics.Poll("runtime_monitor");
        ScanTree();
    }

    private static void InstallDeferred()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            Callable.From(InstallDeferred).CallDeferred();
            return;
        }

        if (tree.Root.GetNodeOrNull<Node>(MonitorName) != null)
        {
            return;
        }

        LanConnectRuntimeMonitor monitor = new()
        {
            Name = MonitorName
        };
        tree.Root.AddChild(monitor);
        Log.Info("sts2_lan_connect runtime monitor installed.");
    }

    private void ScanTree()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return;
        }

        ScanNode(tree.Root);
    }

    private void ScanNode(Node node)
    {
        if (node is NJoinFriendScreen joinScreen)
        {
            JoinFriendScreenPatches.ScheduleEnsureLanJoinControls(joinScreen, "runtime_monitor");
        }
        else if (node is NMultiplayerLoadGameScreen multiplayerLoadScreen)
        {
            LanConnectContinueRunLobbyAutoPublisher.ScheduleEnsureAutoPublish(multiplayerLoadScreen, "runtime_monitor");
        }
        else if (node is NCustomRunLoadScreen customRunLoadScreen)
        {
            LanConnectContinueRunLobbyAutoPublisher.ScheduleEnsureAutoPublish(customRunLoadScreen, "runtime_monitor");
        }
        else if (node is NDailyRunLoadScreen dailyRunLoadScreen)
        {
            LanConnectContinueRunLobbyAutoPublisher.ScheduleEnsureAutoPublish(dailyRunLoadScreen, "runtime_monitor");
        }
        else if (node is NCharacterSelectScreen characterSelectScreen)
        {
            LanConnectInviteButtonPatch.EnsureInviteButton(characterSelectScreen);
        }
        else if (node is NMultiplayerSubmenu multiplayerSubmenu)
        {
            MultiplayerSubmenuPatches.ScheduleEnsureLobbyEntry(multiplayerSubmenu, "runtime_monitor");
        }
        else if (node is NPauseMenu pauseMenu)
        {
            PauseMenuPatches.ScheduleEnsureRoomManagementButton(pauseMenu, "runtime_monitor");
        }
        else if (node is NRemoteLobbyPlayer remoteLobbyPlayer)
        {
            LanConnectRemoteLobbyPlayerPatches.RefreshNameplate(remoteLobbyPlayer);
        }

        foreach (Node child in node.GetChildren())
        {
            ScanNode(child);
        }
    }
}
