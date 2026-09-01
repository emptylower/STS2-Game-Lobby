using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

/// <summary>
/// Sent from host to clients when a client has joined.
/// The newly joined client receives ClientLobbyJoinResponseMessage and not this message.
/// </summary>
public struct PlayerJoinedMessage : INetMessage, IPacketSerializable
{
	public StartRunLobbyPlayer lobbyPlayer;

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.VeryDebug;

	public bool ShouldBuffer => true;

	public void Serialize(PacketWriter writer)
	{
		writer.Write(lobbyPlayer);
	}

	public void Deserialize(PacketReader reader)
	{
		lobbyPlayer = reader.Read<StartRunLobbyPlayer>();
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
