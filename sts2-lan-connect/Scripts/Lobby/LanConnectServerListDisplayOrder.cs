using System.Linq;
using System.Text;

namespace Sts2LanConnect.Scripts;

// Display-order stabilization for the server picker (plan §6):
//
// - resort=true  → full §3 ranking (pinned → reachable → version tier desc →
//   ping ms asc → address).
// - resort=false → addresses still present in previousOrder keep their relative
//   order; brand-new entries are ranked per §3 and appended at the end; entries
//   that disappeared are dropped. Mid-refresh updates therefore only rewrite
//   row content — the list never jumps until the single final resort.
internal static class LanConnectServerListDisplayOrder
{
    internal static List<ServerListEntry> Apply(
        IReadOnlyList<string> previousOrder,
        IEnumerable<ServerListEntry> entries,
        bool resort)
    {
        List<ServerListEntry> materialized = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Address))
            .ToList();

        if (resort)
        {
            return LanConnectServerListBootstrap.OrderForDisplay(materialized).ToList();
        }

        var previousRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < previousOrder.Count; i++)
        {
            string key = LanConnectServerListBootstrap.NormalizeAddress(previousOrder[i]);
            if (!previousRank.ContainsKey(key))
            {
                previousRank[key] = i;
            }
        }

        var kept = new List<(int Rank, ServerListEntry Entry)>();
        var added = new List<ServerListEntry>();
        foreach (ServerListEntry entry in materialized)
        {
            string key = LanConnectServerListBootstrap.NormalizeAddress(entry.Address);
            if (previousRank.TryGetValue(key, out int rank))
            {
                kept.Add((rank, entry));
            }
            else
            {
                added.Add(entry);
            }
        }

        kept.Sort((left, right) => left.Rank.CompareTo(right.Rank));

        List<ServerListEntry> result = kept.Select(item => item.Entry).ToList();
        result.AddRange(LanConnectServerListBootstrap.OrderForDisplay(added));
        return result;
    }

    // Waits for BOTH refresh tasks before letting the caller continue, even when
    // one of them faults — Task.WhenAll rethrows only after every task finished.
    // Extracted so the "final resort happens after the ping task ends even when
    // the Cloudflare task throws" contract is unit-testable.
    internal static Task AwaitRefreshTasksAsync(Task cloudflareTask, Task pingTask) =>
        Task.WhenAll(cloudflareTask, pingTask);
}

// Pure formatters for the E2E-verifiable picker log lines (plan §6). Kept free
// of Godot dependencies so xUnit can assert the exact wire format.
internal static class LanConnectServerPickerLogLines
{
    internal static string Refresh(int dialogId, int generation, string state) =>
        $"sts2_lan_connect server picker refresh: dialog={dialogId} generation={generation} state={state}";

    internal static string Render(int dialogId, int generation, bool resort, IReadOnlyList<ServerListEntry> entries) =>
        $"sts2_lan_connect server picker render: dialog={dialogId} generation={generation} "
        + $"resort={(resort ? "true" : "false")} count={entries.Count} "
        + $"order={string.Join(",", entries.Select(entry => EncodeAddress(entry.Address)))}";

    internal static string OrderSnapshot(int dialogId, int generation, IReadOnlyList<ServerListEntry> entries)
    {
        var builder = new StringBuilder(
            $"sts2_lan_connect server picker order: dialog={dialogId} generation={generation} count={entries.Count} items=");
        for (int i = 0; i < entries.Count; i++)
        {
            ServerListEntry entry = entries[i];
            if (i > 0)
            {
                builder.Append("; ");
            }

            builder
                .Append(i + 1).Append('=')
                .Append(EncodeAddress(entry.Address)).Append('|')
                .Append(entry.IsPinned ? "pinned" : "-").Append('|')
                .Append(entry.Version.Display).Append('|')
                .Append(ProbeStateName(entry.ProbeState)).Append('|')
                .Append(entry.PingMs is int ms ? $"{ms}ms" : "-");
        }

        return builder.ToString();
    }

    internal static string Closed(int dialogId) =>
        $"sts2_lan_connect server picker closed: dialog={dialogId}";

    // Keeps each log line single-line and unambiguous: `%`, `,`, `|`, `;`,
    // whitespace, and control characters in an address are percent-encoded
    // with uppercase hex (`,`→`%2C`, newline→`%0A`); every other character —
    // including normal http(s) addresses — is emitted verbatim. Addresses are
    // only encoded here at the log boundary, never filtered upstream.
    private static string EncodeAddress(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return address;
        }

        StringBuilder builder = new(address.Length);
        foreach (char c in address)
        {
            if (c == '%' || c == ',' || c == '|' || c == ';' || char.IsWhiteSpace(c) || char.IsControl(c))
            {
                builder.Append('%').Append(((int)c).ToString("X2"));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string ProbeStateName(ServerProbeState state) => state switch
    {
        ServerProbeState.Reachable => "reachable",
        ServerProbeState.Unreachable => "unreachable",
        _ => "pending",
    };
}
