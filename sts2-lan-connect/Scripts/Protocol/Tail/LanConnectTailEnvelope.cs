using System.Collections.ObjectModel;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectTailEnvelope
{
    internal LanConnectTailEnvelope(
        ushort sessionProtocolVersion,
        IEnumerable<LanConnectTailEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        SessionProtocolVersion = sessionProtocolVersion;
        Entries = new ReadOnlyCollection<LanConnectTailEntry>(entries.ToArray());
    }

    internal ushort SessionProtocolVersion { get; }

    internal IReadOnlyList<LanConnectTailEntry> Entries { get; }
}
