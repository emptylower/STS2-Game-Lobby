using System.Collections.Generic;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
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
	/// The version of the game the host is running.
	/// </summary>
	public string version;

	/// <summary>
	/// A hash of all the IDs in the model database.
	/// </summary>
	public uint idDatabaseHash;

	/// <summary>
	/// A list of all the gameplay-affecting mods that the host has installed, if any.
	/// </summary>
	public List<string>? gameplayAffectingMods;

	/// <summary>
	/// A list of all the non-gameplay-affecting mods that the host has installed, if any.
	/// </summary>
	public List<string>? otherMods;

	/// <summary>
	/// What kind of run this is (standard, daily, custom).
	/// </summary>
	public GameMode gameMode;

	/// <summary>
	/// What state the run is currently in.
	/// </summary>
	public RunSessionState sessionState;

	/// <summary>
	/// If the host is about to disconnect the client, why.
	/// </summary>
	public ConnectionFailureReason? connectionFailureReason;

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Info;

	public bool ShouldBuffer => true;

	/// <summary>
	/// Returns an InitialGameInfoMessage with the version and idDatabaseHash filled in.
	/// </summary>
	public static InitialGameInfoMessage Basic()
	{
		return new InitialGameInfoMessage
		{
			version = (ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? GitHelper.ShortCommitId ?? "UNKNOWN"),
			idDatabaseHash = ModelIdSerializationCache.Hash,
			gameplayAffectingMods = ModManager.GetGameplayRelevantModNameList(),
			otherMods = ModManager.GetNonGameplayRelevantModNameList()
		};
	}

	public void Serialize(PacketWriter writer)
	{
		writer.WriteString(version);
		writer.WriteUInt(idDatabaseHash);
		writer.WriteEnum(gameMode);
		writer.WriteEnum(sessionState);
		writer.WriteBool(connectionFailureReason.HasValue);
		if (connectionFailureReason.HasValue)
		{
			writer.WriteEnum(connectionFailureReason.Value);
		}
		writer.WriteBool(gameplayAffectingMods != null);
		if (gameplayAffectingMods != null)
		{
			writer.WriteInt(gameplayAffectingMods.Count);
			foreach (string gameplayAffectingMod in gameplayAffectingMods)
			{
				writer.WriteString(gameplayAffectingMod);
			}
		}
		writer.WriteBool(otherMods != null);
		if (otherMods == null)
		{
			return;
		}
		writer.WriteInt(otherMods.Count);
		foreach (string otherMod in otherMods)
		{
			writer.WriteString(otherMod);
		}
	}

	public void Deserialize(PacketReader reader)
	{
		version = reader.ReadString();
		idDatabaseHash = reader.ReadUInt();
		gameMode = reader.ReadEnum<GameMode>();
		sessionState = reader.ReadEnum<RunSessionState>();
		if (reader.ReadBool())
		{
			connectionFailureReason = reader.ReadEnum<ConnectionFailureReason>();
		}
		if (reader.ReadBool())
		{
			int num = reader.ReadInt();
			gameplayAffectingMods = new List<string>();
			for (int i = 0; i < num; i++)
			{
				gameplayAffectingMods.Add(reader.ReadString());
			}
		}
		if (reader.ReadBool())
		{
			int num2 = reader.ReadInt();
			otherMods = new List<string>();
			for (int j = 0; j < num2; j++)
			{
				otherMods.Add(reader.ReadString());
			}
		}
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
