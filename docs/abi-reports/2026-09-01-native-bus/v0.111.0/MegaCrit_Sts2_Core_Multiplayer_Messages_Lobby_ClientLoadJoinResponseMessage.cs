using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Saves;

namespace MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

/// <summary>
/// Sent by a host to a client in response to a ClientLoadJoinRequestMessage.
/// </summary>
public struct ClientLoadJoinResponseMessage : INetMessage, IPacketSerializable
{
	public SerializableRun serializableRun;

	public List<LoadRunLobbyPlayer> playersAlreadyConnected;

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Info;

	public bool ShouldBuffer => true;

	public void Serialize(PacketWriter writer)
	{
		writer.Write(serializableRun);
		writer.WriteInt(playersAlreadyConnected.Count, 8);
		foreach (LoadRunLobbyPlayer item in playersAlreadyConnected)
		{
			writer.Write(item);
		}
	}

	public void Deserialize(PacketReader reader)
	{
		serializableRun = reader.Read<SerializableRun>();
		playersAlreadyConnected = new List<LoadRunLobbyPlayer>();
		int num = reader.ReadInt(8);
		for (int i = 0; i < num; i++)
		{
			playersAlreadyConnected.Add(reader.Read<LoadRunLobbyPlayer>());
		}
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
