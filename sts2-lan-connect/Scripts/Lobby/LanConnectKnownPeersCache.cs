using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal sealed class KnownPeerEntry
{
    [JsonPropertyName("address")] public string Address { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("lastSeenInListing")] public string? LastSeenInListing { get; set; }
    [JsonPropertyName("lastSuccessConnect")] public string? LastSuccessConnect { get; set; }
    [JsonPropertyName("consecutiveFailures")] public int ConsecutiveFailures { get; set; }
    [JsonPropertyName("discoveredVia")] public string DiscoveredVia { get; set; } = "unknown";
    [JsonPropertyName("isFavorite")] public bool IsFavorite { get; set; }
}

internal sealed class KnownPeersFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
    [JsonPropertyName("entries")] public List<KnownPeerEntry> Entries { get; set; } = new();
}

internal static class LanConnectKnownPeersCache
{
    private const int MaxEntries = 200;
    private const int StaleDays = 14;
    private const int FailureThreshold = 5;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string PathFile => Path.Combine(LanConnectPaths.ResolveWritableDataDirectory(), "known_peers.json");

    public static List<KnownPeerEntry> Load()
    {
        lock (Sync)
        {
            if (!File.Exists(PathFile)) return new List<KnownPeerEntry>();
            try
            {
                string json = File.ReadAllText(PathFile);
                var file = JsonSerializer.Deserialize<KnownPeersFile>(json, LanConnectJson.Options);
                return file?.Entries ?? new List<KnownPeerEntry>();
            }
            catch (Exception ex)
            {
                Log.Warn($"sts2_lan_connect failed to load known_peers.json: {ex.Message}");
                return new List<KnownPeerEntry>();
            }
        }
    }

    public static void Save(IEnumerable<KnownPeerEntry> entries)
    {
        lock (Sync)
        {
            var file = new KnownPeersFile
            {
                Version = 1,
                UpdatedAt = DateTime.UtcNow.ToString("o"),
                Entries = entries.Take(MaxEntries).ToList(),
            };
            string tmp = PathFile + ".tmp";
            string dir = Path.GetDirectoryName(PathFile)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOpts));
            if (File.Exists(PathFile)) File.Delete(PathFile);
            File.Move(tmp, PathFile);
        }
    }

    public static List<KnownPeerEntry> Cleanup(IEnumerable<KnownPeerEntry> entries, DateTime now)
    {
        var staleCutoff = now - TimeSpan.FromDays(StaleDays);
        var keep = new List<KnownPeerEntry>();
        foreach (var e in entries)
        {
            if (e.IsFavorite) { keep.Add(e); continue; }
            if (DateTime.TryParse(e.LastSeenInListing ?? "", out var seen)
                && seen < staleCutoff
                && e.ConsecutiveFailures >= FailureThreshold)
            {
                continue;
            }
            keep.Add(e);
        }
        return keep
            .OrderByDescending(e => DateTime.TryParse(e.LastSuccessConnect ?? "", out var t) ? t : DateTime.MinValue)
            .Take(MaxEntries)
            .ToList();
    }
}
