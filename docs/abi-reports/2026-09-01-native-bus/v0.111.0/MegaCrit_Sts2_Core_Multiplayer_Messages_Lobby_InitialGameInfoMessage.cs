using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

/// <summary>
/// Sent from the host to the client as the first message after the client connects.
/// </summary>
public struct InitialGameInfoMessage : INetMessage, IPacketSerializable
{
	/// <summary>
	/// What state the run is currently in.
	/// </summary>
	public RunSessionState sessionState;

	/// <summary>
	/// What kind of run this is (standard, daily, custom).
	/// </summary>
	public GameMode gameMode;

	/// <summary>
	/// If the host is about to disconnect the client, why.
	/// </summary>
	public ConnectionFailureReason? connectionFailureReason;

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Info;

	public bool ShouldBuffer => true;

	public void Serialize(PacketWriter writer)
	{
		writer.WriteEnum(sessionState);
		writer.WriteEnum(gameMode);
		writer.WriteBool(connectionFailureReason.HasValue);
		if (connectionFailureReason.HasValue)
		{
			writer.WriteEnum(connectionFailureReason.Value);
		}
	}

	public void Deserialize(PacketReader reader)
	{
		sessionState = reader.ReadEnum<RunSessionState>();
		gameMode = reader.ReadEnum<GameMode>();
		if (reader.ReadBool())
		{
			connectionFailureReason = reader.ReadEnum<ConnectionFailureReason>();
		}
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
