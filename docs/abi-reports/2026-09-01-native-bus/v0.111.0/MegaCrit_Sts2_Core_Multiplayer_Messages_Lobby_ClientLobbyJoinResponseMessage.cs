using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Daily;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

/// <summary>
/// Sent from the host to a newly joined client in response to their ClientLobbyJoinRequestMessage.
/// On other clients, PlayerJoinedMessage is sent.
/// </summary>
public struct ClientLobbyJoinResponseMessage : INetMessage, IPacketSerializable
{
	/// <summary>
	/// List of players already in the lobby.
	/// </summary>
	public List<StartRunLobbyPlayer>? playersInLobby;

	/// <summary>
	/// If the run is a daily, this contains information about the time that the host requested from the time server.
	/// </summary>
	public TimeServerResult? dailyTime;

	/// <summary>
	/// Current ascension that the lobby is set to.
	/// </summary>
	public int ascension;

	/// <summary>
	/// Seed set at time of join. Only non-null if in a custom game. The true seed to use is sent when the run begins.
	/// </summary>
	public string? seed;

	/// <summary>
	/// Modifiers present in the lobby at the time of join.
	/// </summary>
	public List<SerializableModifier> modifiers;

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Info;

	public bool ShouldBuffer => true;

	public void Serialize(PacketWriter writer)
	{
		if (playersInLobby == null)
		{
			throw new InvalidOperationException("Tried to serialize ClientSlotGrantedMessage with null list!");
		}
		writer.WriteList(playersInLobby, 3);
		writer.WriteBool(dailyTime.HasValue);
		if (dailyTime.HasValue)
		{
			writer.Write(dailyTime.Value);
		}
		writer.WriteBool(seed != null);
		if (seed != null)
		{
			writer.WriteString(seed);
		}
		writer.WriteInt(ascension, 5);
		writer.WriteList(modifiers);
	}

	public void Deserialize(PacketReader reader)
	{
		playersInLobby = reader.ReadList<StartRunLobbyPlayer>(3);
		if (reader.ReadBool())
		{
			dailyTime = reader.Read<TimeServerResult>();
		}
		if (reader.ReadBool())
		{
			seed = reader.ReadString();
		}
		ascension = reader.ReadInt(5);
		modifiers = reader.ReadList<SerializableModifier>();
	}

	public override string ToString()
	{
		return $"{"ClientLobbyJoinResponseMessage"} Players: {playersInLobby?.Count} Ascension: {ascension}";
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
