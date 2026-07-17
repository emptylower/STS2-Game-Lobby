using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.ModSync;

public sealed class LanConnectModDisableApplierTests
{
    [Fact]
    public void Disable_selection_defaults_to_empty()
    {
        FakeDisableSettings settings = new(Entry("extra.mod"));
        LanConnectModDisableApplier sut = new(settings);

        LanConnectModDisableResult result = sut.ApplyDisableSelection(
            [Descriptor("extra.mod")],
            selectedIds: [],
            requiredDependencyIds: [],
            confirmed: false);

        Assert.Equal(LanConnectModDisableStatus.NoChanges, result.Status);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public void Disable_applier_requires_second_confirmation()
    {
        FakeDisableSettings settings = new(Entry("extra.mod"));
        LanConnectModDisableApplier sut = new(settings);

        LanConnectModDisableResult result = sut.ApplyDisableSelection(
            [Descriptor("extra.mod")],
            selectedIds: ["extra.mod"],
            requiredDependencyIds: [],
            confirmed: false);

        Assert.Equal(LanConnectModDisableStatus.RequiresConfirmation, result.Status);
        Assert.True(settings.IsEnabled("extra.mod"));
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public void Disable_applier_never_disables_lan_connect_dependency_or_non_gameplay_mods()
    {
        FakeDisableSettings settings = new(
            Entry("sts2_lan_connect"),
            Entry("dependency.mod"),
            Entry("visual.mod"),
            Entry("host.dependency"));
        LanConnectModDisableApplier sut = new(settings);

        LanConnectModDisableResult result = sut.ApplyDisableSelection(
        [
            Descriptor("sts2_lan_connect"),
            Descriptor("dependency.mod", LanConnectModRoles.Dependency),
            Descriptor("visual.mod", role: "non_gameplay"),
            Descriptor("host.dependency")
        ],
        selectedIds: ["sts2_lan_connect", "dependency.mod", "visual.mod", "host.dependency"],
        requiredDependencyIds: ["host.dependency"],
        confirmed: true);

        Assert.Equal(LanConnectModDisableStatus.Rejected, result.Status);
        Assert.All(settings.Entries, entry => Assert.True(entry.IsEnabled));
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public void Disable_applier_saves_settings_exactly_once_after_all_changes_succeed()
    {
        FakeDisableSettings settings = new(Entry("one.mod"), Entry("two.mod"));
        LanConnectModDisableApplier sut = new(settings);

        LanConnectModDisableResult result = sut.ApplyDisableSelection(
            [Descriptor("one.mod"), Descriptor("two.mod")],
            selectedIds: ["one.mod", "two.mod"],
            requiredDependencyIds: [],
            confirmed: true);

        Assert.Equal(LanConnectModDisableStatus.Applied, result.Status);
        Assert.False(settings.IsEnabled("one.mod"));
        Assert.False(settings.IsEnabled("two.mod"));
        Assert.Equal(1, settings.SaveCalls);
        Assert.Equal(2, result.ChangeSet.Count);
    }

    [Fact]
    public void Disable_applier_rolls_back_partial_failure_and_surfaces_recovery()
    {
        FakeDisableSettings settings = new(Entry("one.mod"), Entry("two.mod"))
        {
            ThrowWhenSettingId = "two.mod"
        };
        LanConnectModDisableApplier sut = new(settings);

        LanConnectModDisableResult result = sut.ApplyDisableSelection(
            [Descriptor("one.mod"), Descriptor("two.mod")],
            selectedIds: ["one.mod", "two.mod"],
            requiredDependencyIds: [],
            confirmed: true);

        Assert.Equal(LanConnectModDisableStatus.Failed, result.Status);
        Assert.True(result.RecoveryRequired);
        Assert.True(settings.IsEnabled("one.mod"));
        Assert.True(settings.IsEnabled("two.mod"));
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public void Disable_applier_rejects_duplicate_candidates_without_throwing()
    {
        FakeDisableSettings settings = new(Entry("duplicate.mod"));
        LanConnectModDisableApplier sut = new(settings);

        LanConnectModDisableResult result = sut.ApplyDisableSelection(
            [Descriptor("duplicate.mod"), Descriptor("duplicate.mod")],
            selectedIds: ["duplicate.mod"],
            requiredDependencyIds: [],
            confirmed: true);

        Assert.Equal(LanConnectModDisableStatus.Rejected, result.Status);
        Assert.True(settings.IsEnabled("duplicate.mod"));
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public void Restore_pending_disable_selection_restores_original_values_and_saves_once()
    {
        FakeDisableSettings settings = new(Entry("one.mod"));
        LanConnectModDisableApplier sut = new(settings);
        LanConnectModDisableResult applied = sut.ApplyDisableSelection(
            [Descriptor("one.mod")],
            selectedIds: ["one.mod"],
            requiredDependencyIds: [],
            confirmed: true);

        LanConnectModDisableResult restored = sut.RestorePendingDisableSelection(applied.ChangeSet);

        Assert.Equal(LanConnectModDisableStatus.Applied, restored.Status);
        Assert.True(settings.IsEnabled("one.mod"));
        Assert.Equal(2, settings.SaveCalls);
    }

    private static LobbyModDescriptor Descriptor(
        string id,
        string role = LanConnectModRoles.Gameplay) => new()
    {
        Id = id,
        Version = "1.0.0",
        Role = role,
        Source = LanConnectModSources.ModsDirectory
    };

    private static LanConnectModSettingEntry Entry(string id) =>
        new(id, LanConnectModSources.ModsDirectory, IsEnabled: true);

    private sealed class FakeDisableSettings(params LanConnectModSettingEntry[] entries) : ILanConnectModDisableSettings
    {
        public List<LanConnectModSettingEntry> Entries { get; } = [.. entries];
        public string? ThrowWhenSettingId { get; init; }
        public int SaveCalls { get; private set; }

        public IReadOnlyList<LanConnectModSettingEntry> Snapshot() =>
            Entries.Select(entry => entry with { }).ToList();

        public void SetEnabled(string id, string source, bool enabled)
        {
            if (string.Equals(id, ThrowWhenSettingId, StringComparison.Ordinal) && !enabled)
            {
                throw new InvalidOperationException("injected setter failure");
            }
            int index = Entries.FindIndex(entry =>
                entry.Id == id && entry.Source == source);
            if (index < 0)
            {
                throw new InvalidOperationException("missing setting");
            }
            Entries[index] = Entries[index] with { IsEnabled = enabled };
        }

        public void SaveSettings() => SaveCalls++;

        public bool IsEnabled(string id) => Entries.Single(entry => entry.Id == id).IsEnabled;
    }
}
