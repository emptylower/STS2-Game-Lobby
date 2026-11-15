using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

namespace Sts2LanConnect.Scripts;

// Keep the managed join independent of JoinResult's changing field layout (0.113
// made sessionState nullable and moved custom runs out of the GameMode enum).
internal sealed record LanConnectManagedJoinResult(
    RunSessionState SessionState,
    string GameMode,
    bool IsCustom,
    ClientLobbyJoinResponseMessage? JoinResponse = null,
    ClientLoadJoinResponseMessage? LoadJoinResponse = null,
    ClientRejoinResponseMessage? RejoinResponse = null);
