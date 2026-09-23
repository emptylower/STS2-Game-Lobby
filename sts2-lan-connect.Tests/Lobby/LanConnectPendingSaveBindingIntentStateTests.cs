using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectPendingSaveBindingIntentStateTests
{
    private static readonly LanConnectProtocolSelection TestSelection =
        LanConnectProtocolSelection.CreateLocalCompat(4, "game");
    private static readonly LanConnectProtocolSelection OtherSelection =
        LanConnectProtocolSelection.CreateLocalCompat(5, "game");

    [Fact]
    public void Save_then_teardown_persists_the_exact_pending_binding_once()
    {
        PendingHarness harness = new("save-1");
        Assert.True(harness.Coordinator.AttachHostedRoom("大厅续局", "secret", "standard", "save-1", TestSelection));

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
    public void Save_after_teardown_preserves_the_keyed_room_selection()
    {
        PendingHarness harness = new("save-1");
        Assert.True(harness.Coordinator.AttachHostedRoom("大厅续局", null, "standard", "save-1", TestSelection));

        harness.Coordinator.HostedSessionTornDown();

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.Persisted,
            harness.Coordinator.PersistForCurrentSave("late_save"));
        AssertExactLobbyWrite(harness, "save-1", "大厅续局", "late_save:pending_lobby_intent");
    }

    [Fact]
    public void Hosted_flow_end_discards_an_unconsumed_intent_without_writing()
    {
        PendingHarness harness = new("save-2");
        Assert.True(harness.Coordinator.AttachHostedRoom("大厅续局", null, "custom", "save-2", TestSelection));

        harness.Coordinator.HostedSessionTornDown();
        harness.Coordinator.HostedFlowEnded();

        Assert.Empty(harness.Writes);
        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.NoIntent,
            harness.Coordinator.PersistForCurrentSave("later_save"));
    }

    [Fact]
    public void Pending_intent_for_a_different_save_is_refused_and_discarded()
    {
        PendingHarness harness = new("save-new");
        harness.Coordinator.AttachHostedRoom("旧大厅", null, "standard", "save-old", TestSelection);

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

        Assert.False(harness.Coordinator.AttachHostedRoom("新大厅", null, "standard", saveKey, TestSelection));
        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.NoIntent,
            harness.Coordinator.PersistForCurrentSave("save_event"));
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void Null_key_room_attach_discards_a_retained_intent_instead_of_wildcarding_it()
    {
        PendingHarness harness = new("save-1");
        harness.Coordinator.AttachHostedRoom("旧大厅", null, "standard", "save-1", TestSelection);
        harness.Coordinator.HostedSessionTornDown();

        harness.Coordinator.DifferentHostedRoomWillAttach();
        Assert.False(harness.Coordinator.AttachHostedRoom("新房间", null, "standard", null, OtherSelection));

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.NoIntent,
            harness.Coordinator.PersistForCurrentSave("later_save"));
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void First_save_after_teardown_uses_keyless_intent_only_for_the_same_host_service()
    {
        PendingHarness harness = new("first-save");
        object hostService = new();
        harness.CurrentNetService = hostService;
        Assert.True(harness.Coordinator.AttachHostedRoom(
            "新协议房间", null, "standard", null, TestSelection, hostService));

        harness.Coordinator.HostedSessionTornDown();

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.Persisted,
            harness.Coordinator.PersistForCurrentSave("late_first_save"));
        AssertExactLobbyWrite(harness, "first-save", "新协议房间", "late_first_save:pending_lobby_intent");
    }

    [Fact]
    public void Keyless_intent_cannot_bind_a_save_after_the_host_service_changes()
    {
        PendingHarness harness = new("unrelated-save");
        object oldService = new();
        harness.CurrentNetService = new object();
        Assert.True(harness.Coordinator.AttachHostedRoom(
            "旧房间", null, "standard", null, TestSelection, oldService));

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.RefusedDifferentNetService,
            harness.Coordinator.PersistForCurrentSave("late_save"));
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void Keyless_intent_does_not_replace_an_explicit_lan_save_binding()
    {
        PendingHarness harness = new("first-save");
        object hostService = new();
        harness.CurrentNetService = hostService;
        harness.ExistingBinding = new LanConnectSavedRoomBinding
        {
            SaveKey = "first-save",
            HostChannel = LanConnectHostChannels.Lan,
            SchemaVersion = LanConnectSavedRoomBinding.CurrentSchemaVersion
        };
        harness.Coordinator.AttachHostedRoom(
            "大厅房间", null, "standard", null, TestSelection, hostService);

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.RefusedExplicitLanBinding,
            harness.Coordinator.PersistForCurrentSave("late_save"));
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void Different_hosted_room_replaces_the_retained_intent()
    {
        PendingHarness harness = new("save-2");
        harness.Coordinator.AttachHostedRoom("旧大厅", null, "standard", "save-1", TestSelection);
        harness.Coordinator.HostedSessionTornDown();

        harness.Coordinator.DifferentHostedRoomWillAttach();
        harness.Coordinator.AttachHostedRoom("新大厅", null, "custom", "save-2", OtherSelection);
        harness.Coordinator.PersistForCurrentSave("save_event");

        AssertExactLobbyWrite(harness, "save-2", "新大厅", "save_event:pending_lobby_intent", OtherSelection);
    }

    [Fact]
    public void Failed_persistence_keeps_the_pending_intent_for_retry()
    {
        PendingHarness harness = new("save-1")
        {
            PersistResult = false
        };
        harness.Coordinator.AttachHostedRoom("大厅续局", null, "standard", "save-1", TestSelection);

        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.SkippedByPersistence,
            harness.Coordinator.PersistForCurrentSave("first_save_event"));

        harness.PersistResult = true;
        Assert.Equal(
            LanConnectPendingSaveBindingCoordinator.PendingPersistResult.Persisted,
            harness.Coordinator.PersistForCurrentSave("retry_save_event"));
        Assert.Equal(2, harness.Writes.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Joined_session_or_hosted_flow_end_discards_an_unconsumed_intent(bool joinedSession)
    {
        PendingHarness harness = new("save-1");
        harness.Coordinator.AttachHostedRoom("大厅", null, "standard", "save-1", TestSelection);

        if (joinedSession)
        {
            harness.Coordinator.AttachJoinedClient();
        }
        else
        {
            harness.CurrentSaveKey = null;
            harness.Coordinator.HostedFlowEnded();
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
        string source,
        LanConnectProtocolSelection? selection = null)
    {
        (LanConnectPendingSaveBindingCoordinator.LoadedSave Save,
            LanConnectPendingSaveBindingCoordinator.PersistenceRequest Request) write =
            Assert.Single(harness.Writes);
        Assert.Equal(saveKey, write.Save.SaveKey);
        Assert.Equal(roomName, write.Request.RoomName);
        Assert.Equal(LanConnectHostChannels.Lobby, write.Request.HostChannel);
        Assert.Equal(LanConnectSavedRoomBinding.CurrentSchemaVersion, write.Request.SchemaVersion);
        Assert.Equal(source, write.Request.Source);
        Assert.Same(selection ?? TestSelection, write.Request.FrozenSelection);
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
                (save, request) =>
                {
                    Writes.Add((save, request));
                    return PersistResult;
                },
                () => CurrentNetService,
                _ => ExistingBinding);
        }

        public string? CurrentSaveKey { get; set; }

        public object? CurrentNetService { get; set; }

        public LanConnectSavedRoomBinding? ExistingBinding { get; set; }

        public LanConnectPendingSaveBindingCoordinator Coordinator { get; }

        public bool PersistResult { get; set; } = true;

        public List<(LanConnectPendingSaveBindingCoordinator.LoadedSave Save,
            LanConnectPendingSaveBindingCoordinator.PersistenceRequest Request)> Writes { get; } = new();
    }
}
