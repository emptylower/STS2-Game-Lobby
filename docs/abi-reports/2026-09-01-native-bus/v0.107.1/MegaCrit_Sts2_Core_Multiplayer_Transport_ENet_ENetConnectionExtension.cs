using Godot;
using Godot.Collections;

namespace MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

public static class ENetConnectionExtension
{
	/// <summary>
	/// Polls an ENetConnection, returning the events that have occurred since the last poll.
	/// This wraps ENet.Service into a more digestible method.
	/// </summary>
	/// <param name="connection">The connection to poll.</param>
	/// <param name="output">The event we polled from the connection, if any.</param>
	/// <returns></returns>
	public static bool TryService(this ENetConnection connection, out ENetServiceData? output)
	{
		Array array = connection.Service();
		output = null;
		if (array == null)
		{
			return false;
		}
		ENetConnection.EventType eventType = array[0].As<ENetConnection.EventType>();
		if (eventType == ENetConnection.EventType.None)
		{
			return false;
		}
		ENetServiceData value = new ENetServiceData
		{
			type = eventType,
			peer = array[1].As<ENetPacketPeer>(),
			originalData = array
		};
		if (eventType == ENetConnection.EventType.Receive)
		{
			value.channel = array[3].As<int>();
			value.packetData = value.peer.GetPacket();
			value.error = value.peer.GetPacketError();
			value.mode = NetTransferMode.None;
		}
		output = value;
		return true;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
