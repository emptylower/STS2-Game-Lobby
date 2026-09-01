using System;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Platform;

namespace MegaCrit.Sts2.Core.Multiplayer;

public class NetClientGameService : INetClientHandler, INetHandler, INetClientGameService, INetGameService, IHandshakeHandler
{
	private readonly PacketReader _reader = new PacketReader();

	private readonly PacketWriter _writer = new PacketWriter();

	private readonly NetMessageBus _messageBus;

	private readonly NetQualityTracker _qualityTracker;

	private readonly HandshakeManager _handshakeManager;

	public NetClient? NetClient { get; private set; }

	public bool IsConnected { get; private set; }

	public ulong NetId => (NetClient ?? throw new InvalidOperationException("Tried to get NetId while not connected!")).NetId;

	public ulong HostNetId => (NetClient ?? throw new InvalidOperationException("Tried to get HostNetId while not connected!")).HostNetId;

	public bool IsGameLoading => _qualityTracker.IsGameLoading;

	public PlatformType Platform { get; private set; }

	public PeerVersionInfo LocalVersion { get; }

	public NetGameType Type => NetGameType.Client;

	public event Action? ConnectedToHost;

	public event Action<NetErrorInfo>? ConnectionFailed;

	public event Action<NetErrorInfo>? Disconnected;

	public NetClientGameService(PeerVersionInfo versionInfo)
	{
		LocalVersion = versionInfo;
		_messageBus = new NetMessageBus(_reader, _writer);
		_handshakeManager = new HandshakeManager(this, versionInfo, _writer);
		_qualityTracker = new NetQualityTracker(this);
	}

	public void Initialize(NetClient client, PlatformType platform)
	{
		NetClient = client;
		Platform = platform;
	}

	public void Update()
	{
		NetClient? netClient = NetClient;
		if (netClient != null && netClient.IsConnected)
		{
			NetClient.Update();
		}
		if (IsConnected)
		{
			_qualityTracker.Update();
		}
	}

	public void SendMessage<T>(T message, ulong playerId) where T : INetMessage
	{
		if (playerId != NetClient?.HostNetId)
		{
			throw new NotImplementedException("Cannot send messages to non-host players as client!");
		}
		SendMessage(message);
	}

	public void SendMessage<T>(T message) where T : INetMessage
	{
		if (!IsConnected)
		{
			Log.Error($"Attempted to send message {message} while {this} is not connected!");
		}
		else
		{
			int length;
			byte[] bytes = _messageBus.SerializeMessage(NetClient.NetId, message, out length);
			NetClient.SendMessageToHost(bytes, length, message.Mode, message.Mode.ToChannelId());
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
		NetClient? netClient = NetClient;
		if (netClient != null)
		{
			_ = netClient.HostNetId;
			if (true && _handshakeManager.IsHandshaking(NetClient.HostNetId))
			{
				_reader.Reset(packetBytes);
				_handshakeManager.HandshakeMessageReceived(senderId, _reader);
				return;
			}
		}
		if (_messageBus.TryDeserializeMessage(packetBytes, out INetMessage message, out ulong? overrideSenderId))
		{
			senderId = overrideSenderId ?? senderId;
			_messageBus.SendMessageToAllHandlers(message, senderId);
		}
	}

	public void Disconnect(NetError reason, bool now = false)
	{
		NetClient? netClient = NetClient;
		if (netClient != null && netClient.IsConnected)
		{
			NetClient.DisconnectFromHost(reason, now);
		}
	}

	public void OnConnectedToHost()
	{
		_handshakeManager.BeginHandshakeFor(HostNetId);
	}

	public void SendHandshakeMessage(ulong peerId, PacketWriter writer)
	{
		if (peerId != HostNetId)
		{
			throw new InvalidOperationException($"Tried to send handshake message as client to {peerId}, who is not the host!");
		}
		NetClient.SendMessageToHost(writer.Buffer, writer.BytePosition, NetTransferMode.Reliable, NetTransferMode.Reliable.ToChannelId());
	}

	public void HandshakeFailed(ulong peerId, NetErrorInfo info)
	{
		Disconnect(info.GetReason());
		this.ConnectionFailed?.Invoke(info);
	}

	public void HandshakeSucceeded(ulong peerId, PeerVersionInfo versionInfo)
	{
		IsConnected = true;
		_qualityTracker.OnPeerConnected(peerId);
		this.ConnectedToHost?.Invoke();
	}

	public void OnDisconnectedFromHost(ulong hostNetId, NetErrorInfo info)
	{
		_qualityTracker.OnPeerDisconnected(hostNetId);
		_qualityTracker.Dispose();
		if (IsConnected)
		{
			IsConnected = false;
			this.Disconnected?.Invoke(info);
		}
		else if (_handshakeManager.IsHandshaking(hostNetId))
		{
			_handshakeManager.AbortHandshake(hostNetId);
			this.ConnectionFailed?.Invoke(info);
		}
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
		return NetClient?.GetRawLobbyIdentifier();
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
