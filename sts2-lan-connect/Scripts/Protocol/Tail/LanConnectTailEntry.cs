namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectTailEntry
{
    internal const string CapabilitiesId = "lan.capabilities";
    internal const string RejectionId = "lan.rejection";
    internal const string RosterId = "lan.roster";

    private readonly byte[] _payload;

    internal LanConnectTailEntry(
        string id,
        ushort version,
        bool isCritical,
        ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(id);

        Id = id;
        Version = version;
        IsCritical = isCritical;
        _payload = payload.ToArray();
    }

    internal string Id { get; }

    internal ushort Version { get; }

    internal bool IsCritical { get; }

    internal ReadOnlyMemory<byte> Payload => _payload;
}
