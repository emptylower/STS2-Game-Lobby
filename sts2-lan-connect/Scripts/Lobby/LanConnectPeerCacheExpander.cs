using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectPeerCacheExpander
{
    public static async Task ExpandAsync(string lobbyBaseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lobbyBaseUrl)) return;
        try
        {
            var peers = await LanConnectLobbyDirectoryClient.GetPeersAsync(lobbyBaseUrl, ct);
            if (peers.Count == 0) return;
            var cache = LanConnectKnownPeersCache.Load();
            var byAddr = cache.ToDictionary(c => c.Address, StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var p in peers)
            {
                if (byAddr.ContainsKey(p.Address)) continue;
                byAddr[p.Address] = new KnownPeerEntry
                {
                    Address = p.Address,
                    DisplayName = p.DisplayName,
                    LastSeenInListing = p.LastSeen,
                    DiscoveredVia = $"peer:{lobbyBaseUrl}",
                };
                added += 1;
            }
            if (added > 0)
            {
                LanConnectKnownPeersCache.Save(byAddr.Values);
                Log.Info($"sts2_lan_connect peer cache expanded by {added} from {lobbyBaseUrl}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect peer cache expand failed: {ex.Message}");
        }
    }
}
