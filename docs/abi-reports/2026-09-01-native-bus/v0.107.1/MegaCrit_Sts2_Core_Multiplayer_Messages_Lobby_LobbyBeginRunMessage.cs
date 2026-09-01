using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

/// <summary>
/// Sent when the lobby closes and the run begins.
/// </summary>
public struct LobbyBeginRunMessage : INetMessage, IPacketSerializable
{
	public List<LobbyPlayer>? playersInLobby;

	public string seed;

	public List<SerializableModifier> modifiers;

	public string act1;

	public bool ShouldBroadcast => true;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.VeryDebug;

	public bool ShouldBuffer => true;

	public void Serialize(PacketWriter writer)
	{
		if (playersInLobby == null)
		{
			throw new InvalidOperationException("Tried to serialize ClientSlotGrantedMessage with null list!");
		}
		writer.WriteList(playersInLobby, 3);
		writer.WriteString(seed);
		writer.WriteList(modifiers);
		writer.WriteString(act1);
	}

	public void Deserialize(PacketReader reader)
	{
		playersInLobby = reader.ReadList<LobbyPlayer>(3);
		seed = reader.ReadString();
		modifiers = reader.ReadList<SerializableModifier>();
		act1 = reader.ReadString();
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
