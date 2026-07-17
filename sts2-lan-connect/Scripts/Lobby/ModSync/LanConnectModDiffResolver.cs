namespace Sts2LanConnect.Scripts;

internal static class LanConnectModDiffResolver
{
    public static LanConnectModDiff Resolve(
        IEnumerable<LobbyModDescriptor> hostMods,
        IEnumerable<LobbyModDescriptor> localMods,
        string hostGameVersion,
        string localGameVersion)
    {
        string hostVersion = hostGameVersion?.Trim() ?? string.Empty;
        string localVersion = localGameVersion?.Trim() ?? string.Empty;
        bool gameVersionsMatch = string.Equals(
            NormalizeGameVersion(hostVersion),
            NormalizeGameVersion(localVersion),
            StringComparison.Ordinal);
        LanConnectGameVersionComparison gameVersion = new()
        {
            Host = hostVersion,
            Local = localVersion,
            ExactMatch = gameVersionsMatch
        };
        if (!gameVersionsMatch)
        {
            return new LanConnectModDiff { GameVersion = gameVersion, CanContinueRelaxed = false };
        }

        IReadOnlyList<LobbyModDescriptor> canonicalHost = LanConnectModInventoryValidator.Canonicalize(hostMods);
        IReadOnlyList<LobbyModDescriptor> canonicalLocal = LanConnectModInventoryValidator.Canonicalize(localMods);
        Dictionary<string, LobbyModDescriptor> hostById = canonicalHost.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        Dictionary<string, LobbyModDescriptor> localById = canonicalLocal.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        List<LobbyModDescriptor> missingWorkshop = [];
        List<LobbyModDescriptor> missingManual = [];
        List<LanConnectModVersionMismatch> versionMismatches = [];

        foreach (LobbyModDescriptor host in canonicalHost)
        {
            if (!localById.TryGetValue(host.Id, out LobbyModDescriptor? local))
            {
                if (host.Source == LanConnectModSources.SteamWorkshop && host.WorkshopFileId != null)
                {
                    missingWorkshop.Add(host);
                }
                else
                {
                    missingManual.Add(host);
                }
                continue;
            }
            if (!string.Equals(host.Version, local.Version, StringComparison.Ordinal))
            {
                versionMismatches.Add(new LanConnectModVersionMismatch
                {
                    Id = host.Id,
                    HostVersion = host.Version,
                    LocalVersion = local.Version,
                    WorkshopFileId = host.WorkshopFileId
                });
            }
        }

        List<LobbyModDescriptor> extraGameplay = canonicalLocal
            .Where(mod => mod.Role == LanConnectModRoles.Gameplay && !hostById.ContainsKey(mod.Id))
            .ToList();
        return new LanConnectModDiff
        {
            GameVersion = gameVersion,
            MissingWorkshopMods = missingWorkshop,
            MissingManualMods = missingManual,
            ExtraGameplayMods = extraGameplay,
            VersionMismatches = versionMismatches,
            CanContinueRelaxed = true
        };
    }

    private static string NormalizeGameVersion(string value) =>
        value.StartsWith('v') || value.StartsWith('V') ? value[1..] : value;
}
