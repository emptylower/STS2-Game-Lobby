using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectLoadedRunContext
{
    private static readonly FieldInfo? MultiplayerLoadLobbyField =
        typeof(NMultiplayerLoadGameScreen).GetField("_runLobby", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CustomLoadLobbyField =
        typeof(NCustomRunLoadScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? DailyLoadLobbyField =
        typeof(NDailyRunLoadScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static bool TryResolve(Control screen, out LoadRunLobby lobby)
    {
        object? value = screen switch
        {
            NMultiplayerLoadGameScreen => MultiplayerLoadLobbyField?.GetValue(screen),
            NCustomRunLoadScreen => CustomLoadLobbyField?.GetValue(screen),
            NDailyRunLoadScreen => DailyLoadLobbyField?.GetValue(screen),
            _ => null
        };
        lobby = value as LoadRunLobby ?? null!;
        return lobby != null;
    }

    internal static Control? FindScreen(Node node)
    {
        Node? current = node;
        while (current != null)
        {
            if (current is NMultiplayerLoadGameScreen or NCustomRunLoadScreen or NDailyRunLoadScreen)
            {
                return (Control)current;
            }

            current = current.GetParent();
        }

        return null;
    }
}
