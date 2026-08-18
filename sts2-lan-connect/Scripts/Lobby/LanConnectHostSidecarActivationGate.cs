namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectHostSidecarActivationGate
{
    private readonly object _sync = new();
    private readonly HashSet<ulong> _connectedPeerIds = new();
    private readonly HashSet<ulong> _preparedPeerIds = new();

    internal bool ObserveControlBinding(ulong peerNetId)
    {
        lock (_sync)
        {
            _preparedPeerIds.Add(peerNetId);
            return _connectedPeerIds.Contains(peerNetId);
        }
    }

    internal bool ObservePeerConnected(ulong peerNetId)
    {
        lock (_sync)
        {
            _connectedPeerIds.Add(peerNetId);
            return _preparedPeerIds.Contains(peerNetId);
        }
    }

    internal void ObservePeerDisconnected(ulong peerNetId)
    {
        lock (_sync)
        {
            _connectedPeerIds.Remove(peerNetId);
            _preparedPeerIds.Remove(peerNetId);
        }
    }
}
