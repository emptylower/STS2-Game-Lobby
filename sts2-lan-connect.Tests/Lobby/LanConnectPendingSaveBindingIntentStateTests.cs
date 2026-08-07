using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectPendingSaveBindingIntentStateTests
{
    [Fact]
    public void Save_then_teardown_persists_the_exact_pending_binding_once()
    {
        PendingHarness harness = new("save-1");
        Assert.True(harness.Coordinator.AttachHostedRoom("大厅续局", "secret", "standard", "save-1"));

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.Persisted,
            harness.Coordinator.PersistForCurrentSave("save_event"));
        harness.Coordinator.HostedSessionTornDown();

        AssertExactLobbyWrite(harness, "save-1", "大厅续局", "save_event:pending_lobby_intent");
        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.NoIntent,
            harness.Coordinator.PersistForCurrentSave("duplicate"));
    }

    [Fact]
    public void Teardown_then_save_persists_the_exact_pending_binding_once()
    {
        PendingHarness harness = new("save-2");
        Assert.True(harness.Coordinator.AttachHostedRoom("大厅续局", null, "custom", "save-2"));

        harness.Coordinator.HostedSessionTornDown();

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.Persisted,
            harness.Coordinator.HostedFlowEnded("teardown_completion"));
        AssertExactLobbyWrite(harness, "save-2", "大厅续局", "teardown_completion:pending_lobby_intent");
    }

    [Fact]
    public void Pending_intent_for_a_different_save_is_refused_and_discarded()
    {
        PendingHarness harness = new("save-new");
        harness.Coordinator.AttachHostedRoom("旧大厅", null, "standard", "save-old");

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.RefusedDifferentSave,
            harness.Coordinator.PersistForCurrentSave("save_event"));
        Assert.Empty(harness.Writes);
        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.NoIntent,
            harness.Coordinator.PersistForCurrentSave("second_event"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_blank_save_key_never_creates_a_pending_intent(string? saveKey)
    {
        PendingHarness harness = new("unrelated-save");

        Assert.False(harness.Coordinator.AttachHostedRoom("新大厅", null, "standard", saveKey));
        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.NoIntent,
            harness.Coordinator.PersistForCurrentSave("save_event"));
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void Null_key_room_attach_discards_a_retained_intent_instead_of_wildcarding_it()
    {
        PendingHarness harness = new("save-1");
        harness.Coordinator.AttachHostedRoom("旧大厅", null, "standard", "save-1");
        harness.Coordinator.HostedSessionTornDown();

        harness.Coordinator.DifferentHostedRoomWillAttach();
        Assert.False(harness.Coordinator.AttachHostedRoom("新房间", null, "standard", null));

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.NoIntent,
            harness.Coordinator.PersistForCurrentSave("later_save"));
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void Different_hosted_room_replaces_the_retained_intent()
    {
        PendingHarness harness = new("save-2");
        harness.Coordinator.AttachHostedRoom("旧大厅", null, "standard", "save-1");
        harness.Coordinator.HostedSessionTornDown();

        harness.Coordinator.DifferentHostedRoomWillAttach();
        harness.Coordinator.AttachHostedRoom("新大厅", null, "custom", "save-2");
        harness.Coordinator.PersistForCurrentSave("save_event");

        AssertExactLobbyWrite(harness, "save-2", "新大厅", "save_event:pending_lobby_intent");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Joined_session_or_hosted_flow_end_discards_an_unconsumed_intent(bool joinedSession)
    {
        PendingHarness harness = new("save-1");
        harness.Coordinator.AttachHostedRoom("大厅", null, "standard", "save-1");

        if (joinedSession)
        {
            harness.Coordinator.AttachJoinedClient();
        }
        else
        {
            harness.CurrentSaveKey = null;
            harness.Coordinator.HostedFlowEnded("hosted_flow_end");
        }

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.NoIntent,
            harness.Coordinator.PersistForCurrentSave("save_event"));
        Assert.Empty(harness.Writes);
    }

    private static void AssertExactLobbyWrite(
        PendingHarness harness,
        string saveKey,
        string roomName,
        string source)
    {
        (LanConnectPendingSaveBindingCoordinator.LoadedSave Save,
            LanConnectPendingSaveBindingCoordinator.PersistenceRequest Request) write =
            Assert.Single(harness.Writes);
        Assert.Equal(saveKey, write.Save.SaveKey);
        Assert.Equal(roomName, write.Request.RoomName);
        Assert.Equal(LanConnectHostChannels.Lobby, write.Request.HostChannel);
        Assert.Equal(LanConnectSavedRoomBinding.CurrentSchemaVersion, write.Request.SchemaVersion);
        Assert.Equal(source, write.Request.Source);
    }

    private sealed class PendingHarness
    {
        public PendingHarness(string? currentSaveKey)
        {
            CurrentSaveKey = currentSaveKey;
            Coordinator = new LanConnectPendingSaveBindingCoordinator(
                () => CurrentSaveKey == null
                    ? null
                    : new LanConnectPendingSaveBindingCoordinator.LoadedSave(CurrentSaveKey, CurrentSaveKey),
                (save, request) => Writes.Add((save, request)));
        }

        public string? CurrentSaveKey { get; set; }

        public LanConnectPendingSaveBindingCoordinator Coordinator { get; }

        public List<(LanConnectPendingSaveBindingCoordinator.LoadedSave Save,
            LanConnectPendingSaveBindingCoordinator.PersistenceRequest Request)> Writes { get; } = new();
    }
}
