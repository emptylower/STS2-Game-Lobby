using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.ModSync;

public sealed class LanConnectModSyncViewStateTests
{
    [Fact]
    public void View_state_exposes_all_nine_localized_presentations()
    {
        Assert.Equal(LanConnectModSyncViewKind.Checking, LanConnectModSyncViewState.Checking().Kind);
        Assert.Equal(LanConnectModSyncViewKind.GameVersionMismatch, Resolve(Compatible() with
        {
            GameVersion = new LanConnectGameVersionComparison { Host = "0.108.0", Local = "0.109.0", ExactMatch = false }
        }).Kind);
        Assert.Equal(LanConnectModSyncViewKind.Compatible, Resolve(Compatible()).Kind);
        Assert.Equal(LanConnectModSyncViewKind.AutomaticSync, Resolve(Compatible() with
        {
            MissingWorkshopMods = [Mod("needed", LanConnectModSources.SteamWorkshop, "123")]
        }).Kind);
        Assert.Equal(LanConnectModSyncViewKind.ManualAction, Resolve(Compatible() with
        {
            MissingManualMods = [Mod("manual", LanConnectModSources.ModsDirectory)]
        }).Kind);
        Assert.Equal(LanConnectModSyncViewKind.ExtraGameplaySelection, Resolve(Compatible() with
        {
            ExtraGameplayMods = [Mod("extra", LanConnectModSources.ModsDirectory)]
        }).Kind);
        Assert.Equal(LanConnectModSyncViewKind.Progress, LanConnectModSyncViewState.Progress([]).Kind);
        Assert.Equal(LanConnectModSyncViewKind.RestartRequired, LanConnectModSyncViewState.RestartRequired().Kind);
        Assert.Equal(LanConnectModSyncViewKind.UnsupportedPlatform, Resolve(Compatible() with
        {
            MissingWorkshopMods = [Mod("needed", LanConnectModSources.SteamWorkshop, "123")]
        }, LanConnectModSyncAvailability.Unsupported("android_manual_only", "manual")).Kind);
    }

    [Fact]
    public void Extra_gameplay_mods_are_unchecked_and_relaxed_is_never_primary()
    {
        LanConnectModSyncViewState state = Resolve(Compatible() with
        {
            ExtraGameplayMods = [Mod("extra", LanConnectModSources.ModsDirectory)],
            CanContinueRelaxed = true
        });

        Assert.All(state.Rows, row => Assert.False(row.Selected));
        Assert.Equal(LanConnectModSyncAction.ApplyChanges, state.PrimaryAction);
        Assert.Contains(LanConnectModSyncAction.ContinueRelaxed, state.SecondaryActions);
        Assert.NotEqual(LanConnectModSyncAction.ContinueRelaxed, state.PrimaryAction);
    }

    [Fact]
    public void Non_gameplay_mods_never_appear_in_rows()
    {
        LobbyModDescriptor nonGameplay = new()
        {
            Id = "cosmetic",
            Version = "1.0.0",
            Role = "non_gameplay",
            Source = LanConnectModSources.ModsDirectory
        };

        LanConnectModSyncViewState state = Resolve(Compatible() with
        {
            ExtraGameplayMods = [nonGameplay, Mod("gameplay", LanConnectModSources.ModsDirectory)]
        });

        Assert.DoesNotContain(state.Rows, row => row.Descriptor.Id == "cosmetic");
        Assert.Contains(state.Rows, row => row.Descriptor.Id == "gameplay");
    }

    [Fact]
    public void Required_dependencies_are_visible_but_never_selectable_for_disable()
    {
        LobbyModDescriptor dependency = new()
        {
            Id = "required-library",
            Version = "2.0.0",
            Role = LanConnectModRoles.Dependency,
            Source = LanConnectModSources.SteamWorkshop,
            WorkshopFileId = "456"
        };

        LanConnectModSyncViewState state = Resolve(Compatible() with
        {
            MissingWorkshopMods = [dependency]
        });

        LanConnectModSyncRowState row = Assert.Single(state.Rows);
        Assert.Equal("required-library", row.Descriptor.Id);
        Assert.False(row.Selectable);
    }

    [Theory]
    [InlineData("zh-CN", "检查 gameplay MOD")]
    [InlineData("en", "Checking gameplay MODs")]
    public void Localizer_has_stable_titles(string locale, string expected)
    {
        Assert.Equal(expected, LanConnectModSyncLocalizer.Title(LanConnectModSyncViewKind.Checking, locale));
    }

    private static LanConnectModSyncViewState Resolve(
        LobbyModPreflightResponse response,
        LanConnectModSyncAvailability? availability = null) =>
        LanConnectModSyncViewState.FromPreflight(
            response,
            availability ?? LanConnectModSyncAvailability.Available);

    private static LobbyModPreflightResponse Compatible() => new()
    {
        Enabled = true,
        ProtocolVersion = 1,
        HostInventoryAvailable = true,
        GameVersion = new LanConnectGameVersionComparison { Host = "0.109.0", Local = "0.109.0", ExactMatch = true }
    };

    private static LobbyModDescriptor Mod(string id, string source, string? workshopFileId = null) => new()
    {
        Id = id,
        Version = "1.0.0",
        Role = LanConnectModRoles.Gameplay,
        Source = source,
        WorkshopFileId = workshopFileId
    };
}
