using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;

namespace MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

public class ENetClient : NetClient
{
	private const int _handshakeTimeoutMsec = 10000;

	private const int _handshakeUpdateRateMsec = 100;

	private readonly MegaCrit.Sts2.Core.Logging.Logger _logger = new MegaCrit.Sts2.Core.Logging.Logger("ENetClient", LogType.Network);

	private ENetConnection? _connection;

	private ENetPacketPeer? _peer;

	private bool _isConnected;

	private ulong _netId;

	public override bool IsConnected => _isConnected;

	public override ulong NetId => _netId;

	public override ulong HostNetId => 1uL;

	public ENetClient(INetClientHandler handler)
		: base(handler)
	{
	}

	public async Task<NetErrorInfo?> ConnectToHost(ulong netId, string ip, ushort port, CancellationToken cancelToken = default(CancellationToken))
	{
		_connection = new ENetConnection();
		_connection.CreateHost();
		_peer = _connection.ConnectToHost(ip, port);
		int timeoutTimer = 0;
		ENetServiceData? output;
		while (!_connection.TryService(out output) || output.Value.type != ENetConnection.EventType.Connect)
		{
			await Task.Delay(100, cancelToken);
			if (cancelToken.IsCancellationRequested)
			{
				DisconnectFromHost(NetError.CancelledJoin);
				_logger.Warn("User cancelled join flow");
				return null;
			}
			timeoutTimer += 100;
			if (timeoutTimer > 10000)
			{
				_peer.Reset();
				_logger.Error("Connection timed out!");
				return new NetErrorInfo(NetError.Timeout, selfInitiated: false);
			}
		}
		if (_peer.GetState() != ENetPacketPeer.PeerState.Connected)
		{
			_logger.Error($"Connection to {ip}:{port} failed!");
			return new NetErrorInfo(NetError.UnknownNetworkError, selfInitiated: false);
		}
		List<ENetServiceData> bufferedPackets = new List<ENetServiceData>();
		NetErrorInfo? result = await SendAndWaitForNetIdAck(netId, bufferedPackets, cancelToken);
		if (result.HasValue)
		{
			_peer.PeerDisconnect();
			return result;
		}
		_netId = netId;
		_isConnected = true;
		_handler.OnConnectedToHost();
		foreach (ENetServiceData item in bufferedPackets)
		{
			HandleMessageReceived(item);
		}
		return null;
	}

	private async Task<NetErrorInfo?> SendAndWaitForNetIdAck(ulong netId, List<ENetServiceData> bufferedPackets, CancellationToken cancelToken = default(CancellationToken))
	{
		_logger.Info($"Sending handshake with net ID {netId}");
		ENetPacket eNetPacket = ENetPacket.FromHandshakeRequest(new ENetHandshakeRequest
		{
			netId = netId
		});
		_peer.Send(0, eNetPacket.AllBytes, 1);
		bool receivedAck = false;
		int timeoutTimer = 0;
		while (!receivedAck)
		{
			await Task.Delay(100, cancelToken);
			if (cancelToken.IsCancellationRequested)
			{
				_logger.Warn("User cancelled join flow");
				DisconnectFromHost(NetError.CancelledJoin);
				return null;
			}
			if (_connection.TryService(out var output) && output.Value.type == ENetConnection.EventType.Receive)
			{
				byte[] packetData = output.Value.packetData;
				ENetPacket eNetPacket2 = new ENetPacket(packetData);
				if (eNetPacket2.PacketType == ENetPacketType.ApplicationMessage)
				{
					bufferedPackets.Add(output.Value);
					continue;
				}
				ENetHandshakeResponse eNetHandshakeResponse = eNetPacket2.AsHandshakeResponse();
				if (eNetHandshakeResponse.netId != netId)
				{
					_logger.Error($"Received net ID ({eNetHandshakeResponse.netId}) during handshake that did not match ours!");
					return new NetErrorInfo(NetError.InternalError, selfInitiated: false);
				}
				if (eNetHandshakeResponse.status != ENetHandshakeStatus.Success)
				{
					_logger.Error($"Received non-success code during handshake ({eNetHandshakeResponse.status})!");
					return new NetErrorInfo(NetError.Kicked, selfInitiated: false);
				}
				receivedAck = true;
			}
			timeoutTimer += 100;
			if (timeoutTimer > 10000)
			{
				_logger.Error("Timed out waiting for handshake ack!");
				DisconnectFromHost(NetError.Timeout);
				return new NetErrorInfo(NetError.Timeout, selfInitiated: false);
			}
		}
		return null;
	}

	public override void Update()
	{
		AssertClientStarted();
		while (true)
		{
			ENetPacketPeer? peer = _peer;
			if (peer == null || !peer.IsActive())
			{
				break;
			}
			ENetConnection? connection = _connection;
			if (connection == null || !connection.TryService(out var output))
			{
				break;
			}
			ENetConnection.EventType type = output.Value.type;
			ENetConnection.EventType num = type - -1;
			if ((ulong)num <= 4uL)
			{
				switch (num)
				{
				case ENetConnection.EventType.None:
					_logger.Error($"Got error from ENetConnection! Error: {output.Value.error} TODO: Expand me");
					continue;
				case ENetConnection.EventType.Disconnect:
					_logger.Debug("Received connect on client");
					continue;
				case ENetConnection.EventType.Receive:
					_logger.Debug($"Received disconnect on client. Already disconnected: {!_isConnected}");
					if (_isConnected)
					{
						_isConnected = false;
						_handler.OnDisconnectedFromHost(HostNetId, new NetErrorInfo(NetError.UnknownNetworkError, selfInitiated: false));
					}
					continue;
				case (ENetConnection.EventType)4L:
					HandleMessageReceived(output.Value);
					continue;
				}
			}
			throw new ArgumentOutOfRangeException();
		}
	}

	private void HandleMessageReceived(ENetServiceData data)
	{
		if (_isConnected)
		{
			_logger.VeryDebug($"Received packet of length {data.packetData.Length}");
			ENetPacket eNetPacket = new ENetPacket(data.packetData);
			if (eNetPacket.PacketType == ENetPacketType.ApplicationMessage)
			{
				_handler.OnPacketReceived(1uL, eNetPacket.AsAppMessage(), data.mode, data.channel);
			}
			else if (eNetPacket.PacketType == ENetPacketType.Disconnection)
			{
				ENetDisconnection eNetDisconnection = eNetPacket.AsDisconnection();
				_logger.Debug($"Received disconnection packet with reason: {eNetDisconnection.reason}");
				_handler.OnDisconnectedFromHost(HostNetId, new NetErrorInfo(eNetDisconnection.reason, selfInitiated: false));
				_isConnected = false;
			}
			else
			{
				_logger.Error($"Got unexpected packet of type {eNetPacket.PacketType} while we were connected to the host!");
			}
		}
	}

	public override void DisconnectFromHost(NetError reason, bool now = false)
	{
		if (now)
		{
			_peer?.PeerDisconnectNow();
		}
		else
		{
			ENetPacket eNetPacket = ENetPacket.FromDisconnection(new ENetDisconnection
			{
				reason = reason
			});
			_peer?.Send(0, eNetPacket.AllBytes, 8);
			_peer?.PeerDisconnectLater();
		}
		_connection?.Flush();
		if (_isConnected)
		{
			_isConnected = false;
			_handler.OnDisconnectedFromHost(HostNetId, new NetErrorInfo(reason, selfInitiated: true));
			_connection?.Destroy();
		}
	}

	public override void SendMessageToHost(byte[] bytes, int length, NetTransferMode mode, int channel = 0)
	{
		if (!_isConnected)
		{
			throw new InvalidOperationException("Tried to send message to host while disconnected!");
		}
		AssertClientStarted();
		ENetPacket eNetPacket = ENetPacket.FromAppMessage(bytes, length);
		_logger.VeryDebug($"Sending packet of length {eNetPacket.AllBytes.Length}");
		_peer?.Send(channel, eNetPacket.AllBytes, ENetUtil.FlagsFromMode(mode));
	}

	private void AssertClientStarted()
	{
		if (_connection == null)
		{
			throw new InvalidOperationException("Must call ConnectToHost first and wait for connection!");
		}
	}

	public override string? GetRawLobbyIdentifier()
	{
		return null;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
