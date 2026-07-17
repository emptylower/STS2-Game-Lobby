namespace Sts2LanConnect.Scripts;

internal enum LanConnectModSyncViewKind
{
    Checking,
    GameVersionMismatch,
    Compatible,
    AutomaticSync,
    ManualAction,
    ExtraGameplaySelection,
    Progress,
    RestartRequired,
    UnsupportedPlatform
}

internal enum LanConnectModSyncAction
{
    None,
    Join,
    ApplyChanges,
    Cancel,
    Retry,
    Restart,
    ContinueRelaxed
}

internal sealed record LanConnectModSyncRowState(
    LobbyModDescriptor Descriptor,
    bool Selectable,
    bool Selected,
    LanConnectWorkshopJobSnapshot? Job = null,
    LanConnectWorkshopMetadata? Metadata = null);

internal sealed record LanConnectModSyncViewState
{
    public LanConnectModSyncViewKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<LanConnectModSyncRowState> Rows { get; init; } = [];
    public LanConnectModSyncAction PrimaryAction { get; init; }
    public IReadOnlyList<LanConnectModSyncAction> SecondaryActions { get; init; } = [];
    public bool CanContinueRelaxed { get; init; }

    public static LanConnectModSyncViewState Checking(string locale = "zh-CN") =>
        Create(LanConnectModSyncViewKind.Checking, LanConnectModSyncAction.Cancel, locale);

    public static LanConnectModSyncViewState Progress(
        IEnumerable<LanConnectWorkshopJobSnapshot> jobs,
        string locale = "zh-CN")
    {
        ArgumentNullException.ThrowIfNull(jobs);
        return Create(
            LanConnectModSyncViewKind.Progress,
            LanConnectModSyncAction.Cancel,
            locale,
            jobs.Select(job => new LanConnectModSyncRowState(CopyDescriptor(job.Descriptor), false, true, job)).ToList());
    }

    public static LanConnectModSyncViewState RestartRequired(string locale = "zh-CN") =>
        Create(LanConnectModSyncViewKind.RestartRequired, LanConnectModSyncAction.Restart, locale);

    public static LanConnectModSyncViewState FromPreflight(
        LobbyModPreflightResponse response,
        LanConnectModSyncAvailability availability,
        string locale = "zh-CN")
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(availability);
        if (!response.GameVersion.ExactMatch)
        {
            return Create(LanConnectModSyncViewKind.GameVersionMismatch, LanConnectModSyncAction.Cancel, locale);
        }

        List<LanConnectModSyncRowState> rows = [];
        rows.AddRange(GameplayRows(response.MissingWorkshopMods, selectable: false));
        rows.AddRange(GameplayRows(response.MissingManualMods, selectable: false));
        rows.AddRange(GameplayRows(response.ExtraGameplayMods, selectable: true));
        foreach (LanConnectModVersionMismatch mismatch in response.VersionMismatches)
        {
            rows.Add(new LanConnectModSyncRowState(new LobbyModDescriptor
            {
                Id = mismatch.Id,
                Version = mismatch.HostVersion,
                Role = LanConnectModRoles.Gameplay,
                Source = mismatch.WorkshopFileId == null
                    ? LanConnectModSources.ModsDirectory
                    : LanConnectModSources.SteamWorkshop,
                WorkshopFileId = mismatch.WorkshopFileId
            }, false, false));
        }

        LanConnectModSyncViewKind kind;
        LanConnectModSyncAction primary;
        if (response.MissingWorkshopMods.Count > 0 && !availability.IsAvailable)
        {
            kind = LanConnectModSyncViewKind.UnsupportedPlatform;
            primary = LanConnectModSyncAction.Cancel;
        }
        else if (response.MissingManualMods.Count > 0 || response.VersionMismatches.Count > 0)
        {
            kind = LanConnectModSyncViewKind.ManualAction;
            primary = LanConnectModSyncAction.Cancel;
        }
        else if (response.MissingWorkshopMods.Count > 0)
        {
            kind = LanConnectModSyncViewKind.AutomaticSync;
            primary = LanConnectModSyncAction.ApplyChanges;
        }
        else if (response.ExtraGameplayMods.Any(mod => mod.Role == LanConnectModRoles.Gameplay))
        {
            kind = LanConnectModSyncViewKind.ExtraGameplaySelection;
            primary = LanConnectModSyncAction.ApplyChanges;
        }
        else
        {
            kind = LanConnectModSyncViewKind.Compatible;
            primary = LanConnectModSyncAction.Join;
        }

        IReadOnlyList<LanConnectModSyncAction> secondary = response.CanContinueRelaxed && kind != LanConnectModSyncViewKind.Compatible
            ? [LanConnectModSyncAction.ContinueRelaxed, LanConnectModSyncAction.Cancel]
            : [LanConnectModSyncAction.Cancel];
        return Create(kind, primary, locale, rows, secondary, response.CanContinueRelaxed);
    }

    private static LanConnectModSyncViewState Create(
        LanConnectModSyncViewKind kind,
        LanConnectModSyncAction primary,
        string locale,
        IReadOnlyList<LanConnectModSyncRowState>? rows = null,
        IReadOnlyList<LanConnectModSyncAction>? secondary = null,
        bool canContinueRelaxed = false) => new()
        {
            Kind = kind,
            Title = LanConnectModSyncLocalizer.Title(kind, locale),
            Message = LanConnectModSyncLocalizer.Message(kind, locale),
            Rows = rows ?? [],
            PrimaryAction = primary,
            SecondaryActions = secondary ?? [],
            CanContinueRelaxed = canContinueRelaxed
        };

    private static IEnumerable<LanConnectModSyncRowState> GameplayRows(
        IEnumerable<LobbyModDescriptor> descriptors,
        bool selectable) =>
        descriptors
            .Where(descriptor => descriptor.Role is LanConnectModRoles.Gameplay or LanConnectModRoles.Dependency)
            .Select(descriptor => new LanConnectModSyncRowState(
                CopyDescriptor(descriptor),
                selectable && descriptor.Role == LanConnectModRoles.Gameplay,
                false));

    private static LobbyModDescriptor CopyDescriptor(LobbyModDescriptor descriptor) => new()
    {
        Id = descriptor.Id,
        Version = descriptor.Version,
        Role = descriptor.Role,
        Source = descriptor.Source,
        WorkshopFileId = descriptor.WorkshopFileId,
        Dependencies = descriptor.Dependencies.ToList()
    };
}
