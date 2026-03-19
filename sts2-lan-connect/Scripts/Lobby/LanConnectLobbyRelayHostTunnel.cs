using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectLobbyRelayHostTunnel : IAsyncDisposable
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("STS2R1");
    private const byte MessageTypeHostRegister = 1;
    private const byte MessageTypeHostData = 2;
    private const byte MessageTypeClientData = 3;
    private static readonly TimeSpan RegisterInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ClientIdleTimeout = TimeSpan.FromSeconds(90);

    private readonly UdpClient _relaySocket;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<uint, RelayPeerProxy> _peers = new();
    private readonly IPEndPoint _gameHostEndpoint = new(IPAddress.Loopback, LanConnectConstants.DefaultPort);
    private readonly string _roomId;
    private readonly string _hostToken;
    private readonly string _relayHost;
    private readonly IPAddress _relayAddress;
    private readonly int _relayPort;
    private readonly Task _receiveTask;
    private readonly Task _registerTask;

    public LanConnectLobbyRelayHostTunnel(string roomId, LobbyRelayEndpoint relayEndpoint, string hostToken)
    {
        _roomId = roomId;
        _hostToken = hostToken;
        _relayHost = TrimIpv6Brackets(relayEndpoint.Host);
        _relayPort = relayEndpoint.Port;
        _relayAddress = ResolveRelayAddress(_relayHost);
        bool useIpv6Socket = _relayAddress.AddressFamily == AddressFamily.InterNetworkV6;
        _relaySocket = useIpv6Socket
            ? new UdpClient(AddressFamily.InterNetworkV6)
            : new UdpClient(AddressFamily.InterNetwork);
        if (useIpv6Socket)
        {
            _relaySocket.Client.DualMode = true;
        }

        _relaySocket.Connect(_relayAddress, _relayPort);
        GD.Print($"sts2_lan_connect relay host tunnel: starting roomId={_roomId} relayHost={_relayHost} relayIp={LanConnectNetUtil.FormatEndpoint(_relayAddress.ToString(), _relayPort)}");
        _receiveTask = Task.Run(ReceiveLoopAsync);
        _registerTask = Task.Run(RegisterLoopAsync);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        try
        {
            await Task.WhenAll(_receiveTask, _registerTask);
        }
        catch
        {
        }

        foreach ((_, RelayPeerProxy peer) in _peers)
        {
            await peer.DisposeAsync();
        }

        _peers.Clear();
        _relaySocket.Dispose();
        _cts.Dispose();
        GD.Print($"sts2_lan_connect relay host tunnel: stopped roomId={_roomId}");
    }

    private async Task RegisterLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                byte[] packet = BuildHostRegisterPacket(_hostToken);
                await _relaySocket.SendAsync(packet, packet.Length);
                CleanupIdlePeers();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                GD.Print($"sts2_lan_connect relay host tunnel: register failed roomId={_roomId} -> {ex.Message}");
            }

            try
            {
                await Task.Delay(RegisterInterval, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await _relaySocket.ReceiveAsync(_cts.Token);
                if (!TryParseClientData(result.Buffer, out uint clientId, out byte[] payload))
                {
                    continue;
                }

                RelayPeerProxy peer = _peers.GetOrAdd(clientId, id => new RelayPeerProxy(id, this));
                await peer.SendToGameHostAsync(payload, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                GD.Print($"sts2_lan_connect relay host tunnel: receive failed roomId={_roomId} -> {ex.Message}");
            }
        }
    }

    private async Task ForwardFromGameHostAsync(uint clientId, byte[] payload)
    {
        byte[] packet = BuildHostDataPacket(clientId, payload);
        await _relaySocket.SendAsync(packet, packet.Length);
    }

    private void CleanupIdlePeers()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - ClientIdleTimeout;
        foreach ((uint clientId, RelayPeerProxy peer) in _peers)
        {
            if (peer.LastSeenAt >= cutoff)
            {
                continue;
            }

            if (_peers.TryRemove(clientId, out RelayPeerProxy? removed))
            {
                _ = removed.DisposeAsync();
                GD.Print($"sts2_lan_connect relay host tunnel: cleaned idle peer roomId={_roomId}, clientId={clientId}");
            }
        }
    }

    private static byte[] BuildHostRegisterPacket(string hostToken)
    {
        byte[] tokenBytes = Encoding.UTF8.GetBytes(hostToken);
        byte[] packet = new byte[Magic.Length + 1 + 2 + tokenBytes.Length];
        Magic.CopyTo(packet, 0);
        packet[Magic.Length] = MessageTypeHostRegister;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(Magic.Length + 1, 2), checked((ushort)tokenBytes.Length));
        tokenBytes.CopyTo(packet, Magic.Length + 3);
        return packet;
    }

    private static byte[] BuildHostDataPacket(uint clientId, byte[] payload)
    {
        byte[] packet = new byte[Magic.Length + 1 + 4 + payload.Length];
        Magic.CopyTo(packet, 0);
        packet[Magic.Length] = MessageTypeHostData;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(Magic.Length + 1, 4), clientId);
        payload.CopyTo(packet, Magic.Length + 5);
        return packet;
    }

    private static bool TryParseClientData(byte[] buffer, out uint clientId, out byte[] payload)
    {
        clientId = 0;
        payload = Array.Empty<byte>();
        if (buffer.Length < Magic.Length + 5)
        {
            return false;
        }

        if (!buffer.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return false;
        }

        if (buffer[Magic.Length] != MessageTypeClientData)
        {
            return false;
        }

        clientId = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(Magic.Length + 1, 4));
        payload = buffer[(Magic.Length + 5)..];
        return true;
    }

    private static string TrimIpv6Brackets(string value)
    {
        if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
        {
            return value[1..^1];
        }

        return value;
    }

    private static IPAddress ResolveRelayAddress(string host)
    {
        string trimmed = TrimIpv6Brackets(host).Trim();
        if (IPAddress.TryParse(trimmed, out IPAddress? address))
        {
            return address;
        }

        IPAddress[] addresses = Dns.GetHostAddresses(trimmed);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"No IP address resolved for relay host '{trimmed}'.");
        }

        bool preferIpv6 = HasUsableIpv6();
        IPAddress? ipv6 = null;
        IPAddress? ipv4 = null;
        foreach (IPAddress candidate in addresses)
        {
            if (candidate.AddressFamily == AddressFamily.InterNetworkV6 && ipv6 == null)
            {
                ipv6 = candidate;
            }
            else if (candidate.AddressFamily == AddressFamily.InterNetwork && ipv4 == null)
            {
                ipv4 = candidate;
            }
        }

        if (preferIpv6 && ipv6 != null)
        {
            return ipv6;
        }

        return ipv4 ?? ipv6 ?? addresses[0]!;
    }

    private static bool HasUsableIpv6()
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
            {
                IPAddress address = info.Address;
                if (address.AddressFamily == AddressFamily.InterNetworkV6 &&
                    !address.IsIPv6LinkLocal &&
                    !address.IsIPv6Multicast)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed class RelayPeerProxy : IAsyncDisposable
    {
        private readonly LanConnectLobbyRelayHostTunnel _owner;
        private readonly UdpClient _localSocket;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _receiveTask;

        public RelayPeerProxy(uint clientId, LanConnectLobbyRelayHostTunnel owner)
        {
            ClientId = clientId;
            _owner = owner;
            _localSocket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            LastSeenAt = DateTimeOffset.UtcNow;
            GD.Print($"sts2_lan_connect relay host tunnel: created local peer proxy roomId={_owner._roomId}, clientId={ClientId}");
            _receiveTask = Task.Run(ReceiveLoopAsync);
        }

        public uint ClientId { get; }

        public DateTimeOffset LastSeenAt { get; private set; }

        public async Task SendToGameHostAsync(byte[] payload, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastSeenAt = DateTimeOffset.UtcNow;
            await _localSocket.SendAsync(payload, payload.Length, _owner._gameHostEndpoint);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await _receiveTask;
            }
            catch
            {
            }

            _localSocket.Dispose();
            _cts.Dispose();
        }

        private async Task ReceiveLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult result = await _localSocket.ReceiveAsync(_cts.Token);
                    LastSeenAt = DateTimeOffset.UtcNow;
                    await _owner.ForwardFromGameHostAsync(ClientId, result.Buffer);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    GD.Print($"sts2_lan_connect relay host tunnel: local peer receive failed roomId={_owner._roomId}, clientId={ClientId} -> {ex.Message}");
                }
            }
        }
    }
}
