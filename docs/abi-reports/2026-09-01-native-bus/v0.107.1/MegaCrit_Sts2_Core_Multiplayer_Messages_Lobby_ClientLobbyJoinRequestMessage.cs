using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Unlocks;

namespace MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

/// <summary>
/// Sent from a newly connected client to the host as the first message in the joining flow.
/// </summary>
public struct ClientLobbyJoinRequestMessage : INetMessage, IPacketSerializable
{
	public int maxAscensionUnlocked;

	public SerializableUnlockState unlockState;

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Info;

	public bool ShouldBuffer => true;

	public void Serialize(PacketWriter writer)
	{
		writer.WriteInt(maxAscensionUnlocked);
		writer.Write(unlockState);
	}

	public void Deserialize(PacketReader reader)
	{
		maxAscensionUnlocked = reader.ReadInt();
		unlockState = reader.Read<SerializableUnlockState>();
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
