using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectModSettingEntry(
    string Id,
    string Source,
    bool IsEnabled);

internal interface ILanConnectModDisableSettings
{
    IReadOnlyList<LanConnectModSettingEntry> Snapshot();

    void SetEnabled(string id, string source, bool enabled);

    void SaveSettings();
}

internal enum LanConnectModDisableStatus
{
    NoChanges,
    RequiresConfirmation,
    Applied,
    Rejected,
    Failed
}

internal sealed record LanConnectModDisableChange(
    string Id,
    string Source,
    bool OriginalEnabled);

internal sealed record LanConnectModDisableResult
{
    public LanConnectModDisableStatus Status { get; init; }
    public IReadOnlyList<LanConnectModDisableChange> ChangeSet { get; init; } = [];
    public bool RecoveryRequired { get; init; }
    public string? Message { get; init; }
}

internal sealed class LanConnectModDisableApplier
{
    private readonly ILanConnectModDisableSettings _settings;

    public LanConnectModDisableApplier(ILanConnectModDisableSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public LanConnectModDisableResult ApplyDisableSelection(
        IEnumerable<LobbyModDescriptor> candidates,
        IEnumerable<string> selectedIds,
        IEnumerable<string> requiredDependencyIds,
        bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(requiredDependencyIds);
        HashSet<string> selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return new LanConnectModDisableResult { Status = LanConnectModDisableStatus.NoChanges };
        }
        if (!confirmed)
        {
            return new LanConnectModDisableResult { Status = LanConnectModDisableStatus.RequiresConfirmation };
        }

        List<IGrouping<string, LobbyModDescriptor>> candidateGroups = candidates
            .GroupBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToList();
        if (candidateGroups.Any(group => group.Count() != 1))
        {
            return new LanConnectModDisableResult
            {
                Status = LanConnectModDisableStatus.Rejected,
                Message = "Duplicate MOD disable candidates are not allowed."
            };
        }
        Dictionary<string, LobbyModDescriptor> byId = candidateGroups
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        HashSet<string> required = requiredDependencyIds.ToHashSet(StringComparer.Ordinal);
        foreach (string id in selected)
        {
            if (!byId.TryGetValue(id, out LobbyModDescriptor? candidate) ||
                candidate.Role != LanConnectModRoles.Gameplay ||
                LanConnectModInventoryValidator.IsReservedId(id) ||
                required.Contains(id) ||
                candidate.Source is not (LanConnectModSources.ModsDirectory or LanConnectModSources.SteamWorkshop))
            {
                return new LanConnectModDisableResult
                {
                    Status = LanConnectModDisableStatus.Rejected,
                    Message = $"MOD {id} is not eligible for automatic disable."
                };
            }
        }

        IReadOnlyList<LanConnectModSettingEntry> snapshot = _settings.Snapshot();
        List<LanConnectModDisableChange> changes = [];
        foreach (string id in selected.OrderBy(value => value, StringComparer.Ordinal))
        {
            LobbyModDescriptor candidate = byId[id];
            LanConnectModSettingEntry? setting = snapshot.FirstOrDefault(entry =>
                entry.Id == id && entry.Source == candidate.Source);
            if (setting == null)
            {
                return new LanConnectModDisableResult
                {
                    Status = LanConnectModDisableStatus.Rejected,
                    Message = $"No settings entry exists for MOD {id}."
                };
            }
            if (setting.IsEnabled)
            {
                changes.Add(new LanConnectModDisableChange(id, candidate.Source, OriginalEnabled: true));
            }
        }
        if (changes.Count == 0)
        {
            return new LanConnectModDisableResult { Status = LanConnectModDisableStatus.NoChanges };
        }

        List<LanConnectModDisableChange> applied = [];
        try
        {
            foreach (LanConnectModDisableChange change in changes)
            {
                _settings.SetEnabled(change.Id, change.Source, enabled: false);
                applied.Add(change);
            }
            _settings.SaveSettings();
            return new LanConnectModDisableResult
            {
                Status = LanConnectModDisableStatus.Applied,
                ChangeSet = changes
            };
        }
        catch (Exception ex)
        {
            bool rollbackFailed = false;
            foreach (LanConnectModDisableChange change in applied.AsEnumerable().Reverse())
            {
                try
                {
                    _settings.SetEnabled(change.Id, change.Source, change.OriginalEnabled);
                }
                catch
                {
                    rollbackFailed = true;
                }
            }
            return new LanConnectModDisableResult
            {
                Status = LanConnectModDisableStatus.Failed,
                RecoveryRequired = true,
                ChangeSet = changes,
                Message = rollbackFailed
                    ? $"Disabling MODs failed and in-memory rollback was incomplete: {ex.Message}"
                    : $"Disabling MODs failed; changes were rolled back in memory: {ex.Message}"
            };
        }
    }

    public LanConnectModDisableResult RestorePendingDisableSelection(
        IEnumerable<LanConnectModDisableChange> changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        List<LanConnectModDisableChange> changes = changeSet.ToList();
        if (changes.Count == 0)
        {
            return new LanConnectModDisableResult { Status = LanConnectModDisableStatus.NoChanges };
        }
        try
        {
            foreach (LanConnectModDisableChange change in changes)
            {
                _settings.SetEnabled(change.Id, change.Source, change.OriginalEnabled);
            }
            _settings.SaveSettings();
            return new LanConnectModDisableResult
            {
                Status = LanConnectModDisableStatus.Applied,
                ChangeSet = changes
            };
        }
        catch (Exception ex)
        {
            return new LanConnectModDisableResult
            {
                Status = LanConnectModDisableStatus.Failed,
                RecoveryRequired = true,
                ChangeSet = changes,
                Message = $"Restoring MOD settings failed: {ex.Message}"
            };
        }
    }
}

internal sealed class LanConnectGameModDisableSettings : ILanConnectModDisableSettings
{
    public IReadOnlyList<LanConnectModSettingEntry> Snapshot()
    {
        ModSettings? settings = SaveManager.Instance.SettingsSave.ModSettings;
        return settings?.ModList.Select(entry => new LanConnectModSettingEntry(
                entry.Id,
                ToWireSource(entry.Source),
                entry.IsEnabled))
            .ToList()
            ?? [];
    }

    public void SetEnabled(string id, string source, bool enabled)
    {
        ModSettings settings = SaveManager.Instance.SettingsSave.ModSettings ??=
            new ModSettings();
        ModSource modSource = FromWireSource(source);
        SettingsSaveMod entry = settings.ModList.FirstOrDefault(candidate =>
            candidate.Id == id && candidate.Source == modSource)
            ?? throw new InvalidOperationException($"No settings entry exists for MOD {id} ({source}).");
        entry.IsEnabled = enabled;
    }

    public void SaveSettings() => SaveManager.Instance.SaveSettings();

    private static string ToWireSource(ModSource source) => source switch
    {
        ModSource.SteamWorkshop => LanConnectModSources.SteamWorkshop,
        ModSource.ModsDirectory => LanConnectModSources.ModsDirectory,
        _ => LanConnectModSources.Unknown
    };

    private static ModSource FromWireSource(string source) => source switch
    {
        LanConnectModSources.SteamWorkshop => ModSource.SteamWorkshop,
        LanConnectModSources.ModsDirectory => ModSource.ModsDirectory,
        _ => throw new InvalidOperationException($"Unsupported MOD source: {source}")
    };
}
