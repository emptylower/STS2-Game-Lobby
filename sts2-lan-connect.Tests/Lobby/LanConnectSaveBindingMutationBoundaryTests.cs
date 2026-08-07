using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectSaveBindingMutationBoundaryTests
{
    [Fact]
    public void Safe_load_performs_no_persisted_write()
    {
        List<LanConnectRunBindingCoordinator<string>.BindingWrite> writes = new();
        LanConnectRunBindingCoordinator<string> coordinator = CreateCoordinator(
            "save-1",
            new Dictionary<string, LanConnectSavedRoomBinding>(),
            writes);

        bool loaded = coordinator.TryLoadForSafeLoad(out string? run, out string failureReason);

        Assert.True(loaded);
        Assert.Equal("save-1", run);
        Assert.Equal(string.Empty, failureReason);
        Assert.Empty(writes);
    }

    [Fact]
    public void Repair_preserves_a_valid_binding_and_its_publish_decision()
    {
        LanConnectSavedRoomBinding existing = new()
        {
            SchemaVersion = LanConnectSavedRoomBinding.CurrentSchemaVersion,
            SaveKey = "save-1",
            RoomName = "续局房间",
            HostChannel = LanConnectHostChannels.Lobby
        };
        Dictionary<string, LanConnectSavedRoomBinding> bindings = new()
        {
            [existing.SaveKey] = existing
        };
        List<LanConnectRunBindingCoordinator<string>.BindingWrite> writes = new();
        LanConnectRunBindingCoordinator<string> coordinator = CreateCoordinator("save-1", bindings, writes);

        LanConnectRunBindingCoordinator<string>.RepairBindingInspection inspection =
            coordinator.InspectRepairBinding();

        Assert.True(inspection.RunLoaded);
        Assert.True(inspection.HasBinding);
        Assert.Same(existing, bindings["save-1"]);
        Assert.Empty(writes);
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Publish,
            LanConnectContinueRunPublishDecision.Decide(existing.HostChannel, existing.SchemaVersion));
    }

    [Fact]
    public void Repair_does_not_invent_a_binding_when_none_exists()
    {
        Dictionary<string, LanConnectSavedRoomBinding> bindings = new();
        List<LanConnectRunBindingCoordinator<string>.BindingWrite> writes = new();
        LanConnectRunBindingCoordinator<string> coordinator = CreateCoordinator("save-2", bindings, writes);

        LanConnectRunBindingCoordinator<string>.RepairBindingInspection inspection =
            coordinator.InspectRepairBinding();

        Assert.True(inspection.RunLoaded);
        Assert.False(inspection.HasBinding);
        Assert.Empty(bindings);
        Assert.Empty(writes);
    }

    [Fact]
    public async Task Hosted_restart_persists_exact_binding_before_returning_to_main_menu()
    {
        List<string> events = new();
        List<LanConnectRunBindingCoordinator<string>.BindingWrite> writes = new();
        LanConnectRunBindingCoordinator<string> coordinator = new(
            () => new(true, "save-3", string.Empty),
            run => run,
            _ => null,
            (_, write) =>
            {
                writes.Add(write);
                events.Add("persist");
            });

        await coordinator.ExecuteHostedRestartAsync(
            "save-3",
            "大厅续局",
            "secret",
            "custom",
            () => events.Add("after_persist"),
            () =>
            {
                events.Add("prepare");
                return Task.CompletedTask;
            },
            () =>
            {
                events.Add("return_to_main_menu");
                return Task.CompletedTask;
            });

        LanConnectRunBindingCoordinator<string>.BindingWrite write = Assert.Single(writes);
        Assert.Equal("save-3", write.SaveKey);
        Assert.Equal(LanConnectHostChannels.Lobby, write.HostChannel);
        Assert.Equal(LanConnectSavedRoomBinding.CurrentSchemaVersion, write.SchemaVersion);
        Assert.Equal(
            ["persist", "after_persist", "prepare", "return_to_main_menu"],
            events);
    }

    private static LanConnectRunBindingCoordinator<string> CreateCoordinator(
        string saveKey,
        IReadOnlyDictionary<string, LanConnectSavedRoomBinding> bindings,
        ICollection<LanConnectRunBindingCoordinator<string>.BindingWrite> writes)
    {
        return new LanConnectRunBindingCoordinator<string>(
            () => new(true, saveKey, string.Empty),
            run => run,
            key => bindings.GetValueOrDefault(key),
            (_, write) => writes.Add(write));
    }
}
