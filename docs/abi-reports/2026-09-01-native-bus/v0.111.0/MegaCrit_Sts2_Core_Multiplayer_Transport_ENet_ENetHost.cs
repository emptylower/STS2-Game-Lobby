using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;

namespace MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

public class ENetHost : NetHost
{
	private record struct ClientConnection
	{
		public ulong netId;

		public ENetPacketPeer peer;
	}

	private struct HandshakeAwaitingResponse
	{
		public ulong receivedMsec;

		public ClientConnection conn;
	}

	private const int _handshakeTimeoutMsec = 10000;

	private const int _handshakeUpdateRateMsec = 100;

	private readonly List<ClientConnection> _connectedPeers = new List<ClientConnection>();

	private ENetConnection? _connection;

	private bool _isConnected;

	private readonly List<HandshakeAwaitingResponse> _receivedHandshakes = new List<HandshakeAwaitingResponse>();

	private readonly MegaCrit.Sts2.Core.Logging.Logger _logger = new MegaCrit.Sts2.Core.Logging.Logger("ENetHost", LogType.Network);

	public override bool IsConnected => _isConnected;

	public override IEnumerable<ulong> ConnectedPeerIds => _connectedPeers.Select((ClientConnection c) => c.netId);

	public override ulong NetId => 1uL;

	public ENetHost(INetHostHandler handler)
		: base(handler)
	{
	}

	public NetErrorInfo? StartHost(ushort port, int maxClients)
	{
		_connection = new ENetConnection();
		Error error = _connection.CreateHostBound("0.0.0.0", port, maxClients);
		if (error != Error.Ok)
		{
			_logger.Error($"Failed to create host! {error}");
			return new NetErrorInfo(error);
		}
		_isConnected = true;
		return null;
	}

	private async Task DoClientHandshake(ENetPacketPeer peer)
	{
		peer.SetTimeout(24, 20000, 20000);
		int timeoutTimer = 0;
		HandshakeAwaitingResponse? handshake = null;
		while (!handshake.HasValue)
		{
			foreach (HandshakeAwaitingResponse receivedHandshake in _receivedHandshakes)
			{
				if (receivedHandshake.conn.peer == peer)
				{
					handshake = receivedHandshake;
					break;
				}
			}
			if (!handshake.HasValue)
			{
				await Task.Delay(100);
				timeoutTimer += 100;
				if (timeoutTimer >= 10000)
				{
					_logger.Error("Timed out waiting for handshake!");
					peer.Reset();
					return;
				}
			}
		}
		if (GetConnectionById(handshake.Value.conn.netId).HasValue)
		{
			_logger.Info($"Second client attempted to connect with peer ID {handshake.Value.conn.netId}, disconnecting them");
			ENetPacket eNetPacket = ENetPacket.FromHandshakeResponse(new ENetHandshakeResponse
			{
				netId = handshake.Value.conn.netId,
				status = ENetHandshakeStatus.IdCollision
			});
			handshake.Value.conn.peer.Send(0, eNetPacket.AllBytes, 1);
			handshake.Value.conn.peer.PeerDisconnect();
		}
		else
		{
			_logger.Debug($"Acknowledging handshake for peer with ID {handshake.Value.conn.netId}");
			ENetPacket eNetPacket2 = ENetPacket.FromHandshakeResponse(new ENetHandshakeResponse
			{
				netId = handshake.Value.conn.netId,
				status = ENetHandshakeStatus.Success
			});
			handshake.Value.conn.peer.Send(0, eNetPacket2.AllBytes, 1);
			_connectedPeers.Add(handshake.Value.conn);
			_handler.OnPeerConnected(handshake.Value.conn.netId);
		}
	}

	private void HandleClientDisconnection(ClientConnection conn, NetError reason, bool notifyHandler = true)
	{
		_logger.Debug($"Peer ID {conn.netId} disconnected, reason: {reason}");
		_connectedPeers.Remove(conn);
		if (notifyHandler)
		{
			_handler.OnPeerDisconnected(conn.netId, new NetErrorInfo(reason, selfInitiated: false));
		}
	}

	private ClientConnection? GetConnectionByPeer(ENetPacketPeer peer)
	{
		foreach (ClientConnection connectedPeer in _connectedPeers)
		{
			if (connectedPeer.peer == peer)
			{
				return connectedPeer;
			}
		}
		return null;
	}

	private ClientConnection? GetConnectionById(ulong id)
	{
		foreach (ClientConnection connectedPeer in _connectedPeers)
		{
			if (connectedPeer.netId == id)
			{
				return connectedPeer;
			}
		}
		return null;
	}

	public override void Update()
	{
		AssertHostStarted();
		ENetServiceData? output;
		while (_connection.TryService(out output))
		{
			ENetConnection.EventType type = output.Value.type;
			ENetConnection.EventType num = type - -1;
			if ((ulong)num <= 4uL)
			{
				switch (num)
				{
				case ENetConnection.EventType.Receive:
					continue;
				case ENetConnection.EventType.None:
					_logger.Error("Got error from ENetConnection! TODO: Expand me");
					continue;
				case ENetConnection.EventType.Disconnect:
					TaskHelper.RunSafely(DoClientHandshake(output.Value.peer));
					continue;
				case (ENetConnection.EventType)4L:
					HandlePacketReceived(output.Value);
					continue;
				}
			}
			throw new ArgumentOutOfRangeException();
		}
	}

	private void HandlePacketReceived(ENetServiceData data)
	{
		byte[] packetData = data.packetData;
		_logger.VeryDebug($"Received packet of length {packetData.Length}");
		ENetPacketPeer peer = data.peer;
		ENetPacket eNetPacket = new ENetPacket(packetData);
		if (eNetPacket.PacketType == ENetPacketType.HandshakeRequest)
		{
			ENetHandshakeRequest eNetHandshakeRequest = eNetPacket.AsHandshakeRequest();
			ClientConnection conn = new ClientConnection
			{
				peer = peer,
				netId = eNetHandshakeRequest.netId
			};
			HandshakeAwaitingResponse item = new HandshakeAwaitingResponse
			{
				conn = conn,
				receivedMsec = Time.GetTicksMsec()
			};
			_logger.Debug($"Received handshake packet containing peer ID {conn.netId}");
			_receivedHandshakes.Add(item);
		}
		else if (eNetPacket.PacketType == ENetPacketType.Disconnection)
		{
			ENetDisconnection eNetDisconnection = eNetPacket.AsDisconnection();
			ClientConnection? connectionByPeer = GetConnectionByPeer(peer);
			if (connectionByPeer.HasValue)
			{
				HandleClientDisconnection(connectionByPeer.Value, eNetDisconnection.reason);
			}
		}
		else
		{
			ClientConnection? connectionByPeer2 = GetConnectionByPeer(peer);
			if (!connectionByPeer2.HasValue)
			{
				_logger.Error($"Received a non-handshake packet length {packetData.Length} for a peer, but no connection exists!");
			}
			else
			{
				_handler.OnPacketReceived(connectionByPeer2.Value.netId, eNetPacket.AsAppMessage(), data.mode, data.channel);
			}
		}
	}

	public override void SendMessageToClient(ulong peerId, byte[] bytes, int length, NetTransferMode mode, int channel = 0)
	{
		AssertHostStarted();
		ClientConnection? connectionById = GetConnectionById(peerId);
		if (!connectionById.HasValue)
		{
			_logger.Error($"Tried to send message to client with ID {peerId}, but no client with that ID is connected!");
		}
		else
		{
			ENetPacket eNetPacket = ENetPacket.FromAppMessage(bytes, length);
			_logger.VeryDebug($"Sending packet of length {eNetPacket.AllBytes.Length}");
			connectionById.Value.peer.Send(channel, eNetPacket.AllBytes, ENetUtil.FlagsFromMode(mode));
		}
	}

	public override void DisconnectClient(ulong peerId, NetError reason, bool now = false)
	{
		DisconnectClientInternal(peerId, reason, now);
	}

	private void DisconnectClientInternal(ulong peerId, NetError reason, bool now = false, bool notifyHandler = true)
	{
		_logger.Info($"Disconnecting client {peerId}, reason: {reason}. Now: {now}");
		AssertHostStarted();
		ClientConnection? connectionById = GetConnectionById(peerId);
		if (connectionById.HasValue)
		{
			if (now)
			{
				connectionById.Value.peer.PeerDisconnectNow();
			}
			else
			{
				ENetPacket eNetPacket = ENetPacket.FromDisconnection(new ENetDisconnection
				{
					reason = reason
				});
				connectionById.Value.peer.Send(0, eNetPacket.AllBytes, 1);
				connectionById.Value.peer.PeerDisconnectLater();
			}
			HandleClientDisconnection(connectionById.Value, reason, notifyHandler);
		}
	}

	public override void SendMessageToAll(byte[] bytes, int length, NetTransferMode mode, int channel = 0)
	{
		AssertHostStarted();
		foreach (ClientConnection connectedPeer in _connectedPeers)
		{
			SendMessageToClient(connectedPeer.netId, bytes, length, mode, channel);
		}
	}

	public override void SetHostIsClosed(bool isClosed)
	{
	}

	public override void StopHost(NetError reason, bool now = false)
	{
		Log.Info($"Stopping host. Reason: {reason}. Now: {now}");
		foreach (ClientConnection item in _connectedPeers.ToList())
		{
			DisconnectClientInternal(item.netId, reason, now, notifyHandler: false);
		}
		_connectedPeers.Clear();
		_connection.Flush();
		_connection.Destroy();
		_isConnected = false;
		_handler.OnDisconnected(new NetErrorInfo(reason, selfInitiated: true));
	}

	private void AssertHostStarted()
	{
		if (_connection == null)
		{
			throw new InvalidOperationException("Must call StartHost first!");
		}
	}

	public override string? GetRawLobbyIdentifier()
	{
		return null;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
