using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.LanHost;

public sealed class LanConnectNetHostCompatibilityTests
{
    [Fact]
    public void Disconnects_legacy_host_with_the_original_reason_and_timing()
    {
        LegacyHost host = new();

        LanConnectNetHostCompatibility.DisconnectClient(host, 42, DisconnectReason.Quit, now: true);

        Assert.Equal((42UL, DisconnectReason.Quit, true), host.Disconnection);
    }

    [Fact]
    public void Disconnects_0_113_host_with_the_original_reason_and_default_debug_text()
    {
        CurrentHost host = new();

        LanConnectNetHostCompatibility.DisconnectClient(host, 43, DisconnectReason.ModMismatch);

        Assert.Equal((43UL, DisconnectReason.ModMismatch, false), host.Disconnection);
        Assert.Null(host.DebugReason);
    }

    [Fact]
    public void Preserves_a_disconnect_failure_instead_of_wrapping_it_in_reflection_error()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LanConnectNetHostCompatibility.DisconnectClient(new FailingHost(), 42, DisconnectReason.Quit));

        Assert.Equal("transport failure", exception.Message);
    }

    [Fact]
    public void Enumerates_legacy_mutable_peer_list()
    {
        LegacyHost host = new();

        Assert.Equal([7UL, 8UL], LanConnectNetHostCompatibility.GetConnectedPeerIds(host));
    }

    [Fact]
    public void Enumerates_0_113_readonly_peer_list_and_returns_a_snapshot()
    {
        CurrentHost host = new();
        ulong[] peerIds = LanConnectNetHostCompatibility.GetConnectedPeerIds(host);
        host.AddPeer(99);

        Assert.Equal([7UL, 8UL], peerIds);
        Assert.Equal([7UL, 8UL, 99UL], LanConnectNetHostCompatibility.GetConnectedPeerIds(host));
    }

    [Fact]
    public void Mod_does_not_bind_to_changing_host_members()
    {
        using PEReader pe = new(File.OpenRead(typeof(Entry).Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        List<string> violations = [];
        foreach (MemberReferenceHandle handle in reader.MemberReferences)
        {
            MemberReference member = reader.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            TypeReference type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            string memberName = reader.GetString(member.Name);
            if (reader.GetString(type.Name) == "NetHostGameService"
                && memberName is "DisconnectClient" or "get_ConnectedPeers")
            {
                violations.Add(memberName);
            }
        }

        Assert.Empty(violations);
    }

    private enum DisconnectReason { Quit, ModMismatch }

    private sealed class Peer(ulong id)
    {
        public readonly ulong peerId = id;
    }

    private sealed class LegacyHost
    {
        public (ulong, DisconnectReason, bool) Disconnection { get; private set; }
        public List<Peer> ConnectedPeers { get; } = [new(7), new(8)];

        public void DisconnectClient(ulong peerId, DisconnectReason reason, bool now = false)
            => Disconnection = (peerId, reason, now);
    }

    private sealed class CurrentHost
    {
        private readonly List<Peer> _peers = [new(7), new(8)];
        public IReadOnlyList<Peer> ConnectedPeers => _peers.AsReadOnly();
        public (ulong, DisconnectReason, bool) Disconnection { get; private set; }
        public string? DebugReason { get; private set; } = "not called";

        public void AddPeer(ulong peerId) => _peers.Add(new(peerId));

        public void DisconnectClient(ulong peerId, DisconnectReason reason, bool now = false, string? debugReason = null)
        {
            Disconnection = (peerId, reason, now);
            DebugReason = debugReason;
        }
    }

    private sealed class FailingHost
    {
        public void DisconnectClient(ulong peerId, DisconnectReason reason, bool now = false)
            => throw new InvalidOperationException("transport failure");
    }
}
