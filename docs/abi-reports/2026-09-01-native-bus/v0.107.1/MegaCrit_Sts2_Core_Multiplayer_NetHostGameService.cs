using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using MegaCrit.Sts2.Core.Platform;

namespace MegaCrit.Sts2.Core.Multiplayer;

public class NetHostGameService : INetHostHandler, INetHandler, INetHostGameService, INetGameService
{
	private NetHost? _netHost;

	private readonly NetMessageBus _messageBus = new NetMessageBus();

	private readonly NetQualityTracker _qualityTracker;

	private readonly List<NetClientData> _connectedPeers = new List<NetClientData>();

	public bool IsConnected => _netHost?.IsConnected ?? false;

	public IReadOnlyList<NetClientData> ConnectedPeers => _connectedPeers;

	public ulong NetId => (_netHost ?? throw new InvalidOperationException("Tried to get NetId while not connected!")).NetId;

	public bool IsGameLoading => _qualityTracker.IsGameLoading;

	public PlatformType Platform { get; private set; }

	public NetHost? NetHost => _netHost;

	public NetGameType Type => NetGameType.Host;

	public event Action<NetErrorInfo>? Disconnected;

	public event Action<ulong>? ClientConnected;

	public event Action<ulong, NetErrorInfo>? ClientDisconnected;

	public NetHostGameService()
	{
		_qualityTracker = new NetQualityTracker(this);
	}

	public NetErrorInfo? StartENetHost(ushort port, int maxClients)
	{
		return ((ENetHost)(_netHost = new ENetHost(this))).StartHost(port, maxClients);
	}

	public Task<NetErrorInfo?> StartSteamHost(int maxClients)
	{
		SteamHost steamHost = (SteamHost)(_netHost = new SteamHost(this));
		Platform = PlatformType.Steam;
		return steamHost.StartHost(maxClients);
	}

	public void Update()
	{
		NetHost? netHost = _netHost;
		if (netHost != null && netHost.IsConnected)
		{
			_netHost.Update();
			_qualityTracker.Update();
		}
	}

	public void SendMessage<T>(T message, ulong peerId) where T : INetMessage
	{
		SendMessageToClientInternal(message, peerId, message.Mode.ToChannelId(), null);
	}

	private void SendMessageToClientInternal<T>(T message, ulong peerId, int channel, ulong? overrideSenderId) where T : INetMessage
	{
		if (!IsConnected)
		{
			Log.Error($"Attempted to send message {message} while {this} is not connected!");
		}
		else
		{
			int length;
			byte[] bytes = _messageBus.SerializeMessage(overrideSenderId ?? _netHost.NetId, message, out length);
			_netHost.SendMessageToClient(peerId, bytes, length, message.Mode, channel);
		}
	}

	public void SendMessage<T>(T message) where T : INetMessage
	{
		if (!IsConnected)
		{
			Log.Error($"Attempted to send message {message} while {this} is not connected!");
			return;
		}
		int length;
		byte[] bytes = _messageBus.SerializeMessage(_netHost.NetId, message, out length);
		foreach (NetClientData connectedPeer in _connectedPeers)
		{
			if (connectedPeer.readyForBroadcasting)
			{
				_netHost.SendMessageToClient(connectedPeer.peerId, bytes, length, message.Mode, message.Mode.ToChannelId());
			}
		}
	}

	public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
	{
		_messageBus.RegisterMessageHandler(handler);
	}

	public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
	{
		_messageBus.UnregisterMessageHandler(handler);
	}

	public void OnPacketReceived(ulong senderId, byte[] packetBytes, NetTransferMode mode, int channel)
	{
		if (!_messageBus.TryDeserializeMessage(packetBytes, out INetMessage message, out ulong? overrideSenderId))
		{
			Log.Error($"Tried to deserialize packet of size {packetBytes.Length} as message, but we were not able to!");
			return;
		}
		if (message.ShouldBroadcast)
		{
			BroadcastMessage(message, senderId, channel, overrideSenderId.Value);
		}
		senderId = overrideSenderId ?? senderId;
		_messageBus.SendMessageToAllHandlers(message, senderId);
	}

	private void BroadcastMessage<T>(T message, ulong excludePeerId, int channel, ulong overrideSenderId) where T : INetMessage
	{
		foreach (NetClientData connectedPeer in _connectedPeers)
		{
			if (connectedPeer.readyForBroadcasting && connectedPeer.peerId != excludePeerId)
			{
				SendMessageToClientInternal(message, connectedPeer.peerId, channel, overrideSenderId);
			}
		}
	}

	/// <summary>
	/// Starts sending broadcasted messages to a peer.
	/// When a peer first connects, messages that have ShouldBroadcast set are not sent to that peer. They are only sent
	/// to the newly connected peer after this method is called, passing the newly connected peer's ID.
	/// This is used to prevent messages from being sent to a peer until the game-level connection flow has been completed.
	/// </summary>
	public void SetPeerReadyForBroadcasting(ulong peerId)
	{
		for (int i = 0; i < _connectedPeers.Count; i++)
		{
			if (_connectedPeers[i].peerId == peerId)
			{
				NetClientData value = _connectedPeers[i];
				value.readyForBroadcasting = true;
				_connectedPeers[i] = value;
			}
		}
	}

	public void DisconnectClient(ulong peerId, NetError reason, bool now = false)
	{
		_netHost.DisconnectClient(peerId, reason, now);
	}

	public void Disconnect(NetError reason, bool now = false)
	{
		NetHost? netHost = _netHost;
		if (netHost != null && netHost.IsConnected)
		{
			_netHost.StopHost(reason, now);
			_qualityTracker.Dispose();
		}
	}

	public void OnDisconnected(NetErrorInfo info)
	{
		this.Disconnected?.Invoke(info);
	}

	public void OnPeerConnected(ulong peerId)
	{
		_connectedPeers.Add(new NetClientData
		{
			peerId = peerId,
			readyForBroadcasting = false
		});
		_qualityTracker.OnPeerConnected(peerId);
		this.ClientConnected?.Invoke(peerId);
	}

	public void OnPeerDisconnected(ulong peerId, NetErrorInfo info)
	{
		_connectedPeers.RemoveAll((NetClientData p) => p.peerId == peerId);
		_qualityTracker.OnPeerDisconnected(peerId);
		this.ClientDisconnected?.Invoke(peerId, info);
	}

	public ConnectionStats? GetStatsForPeer(ulong peerId)
	{
		return _qualityTracker.GetStatsForPeer(peerId);
	}

	public void SetGameLoading(bool isLoading)
	{
		_qualityTracker.SetIsLoading(isLoading);
	}

	public void SetBufferMessages(bool bufferMessages)
	{
		_messageBus.SetBufferMessages(bufferMessages);
	}

	public string? GetRawLobbyIdentifier()
	{
		return _netHost?.GetRawLobbyIdentifier();
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
