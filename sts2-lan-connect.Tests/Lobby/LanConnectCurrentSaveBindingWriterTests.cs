using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectCurrentSaveBindingWriterTests
{
    [Fact]
    public void Active_session_with_a_different_save_key_does_not_write()
    {
        object netService = new();
        WriterHarness harness = new("save-current", netService);

        LanConnectCurrentSaveBindingWriter.PersistOutcome outcome = harness.Writer.Persist(
            Target(netService, expectedSaveKey: "save-session"),
            "save_event");

        Assert.Equal(
            LanConnectCurrentSaveBindingWriter.PersistResult.RefusedDifferentSave,
            outcome.Result);
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void Closing_active_session_does_not_write()
    {
        object netService = new();
        WriterHarness harness = new("save-1", netService);

        LanConnectCurrentSaveBindingWriter.PersistOutcome outcome = harness.Writer.Persist(
            Target(netService, expectedSaveKey: "save-1", isClosing: true),
            "save_event");

        Assert.Equal(
            LanConnectCurrentSaveBindingWriter.PersistResult.RefusedClosing,
            outcome.Result);
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void Keyless_new_room_requires_the_current_run_net_service()
    {
        object hostedNetService = new();
        WriterHarness harness = new("save-1", currentNetService: new object());

        LanConnectCurrentSaveBindingWriter.PersistOutcome outcome = harness.Writer.Persist(
            Target(hostedNetService, expectedSaveKey: null),
            "save_event");

        Assert.Equal(
            LanConnectCurrentSaveBindingWriter.PersistResult.RefusedDifferentNetService,
            outcome.Result);
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void Host_origin_with_a_captured_save_key_refuses_a_different_save()
    {
        object netService = new();
        WriterHarness harness = new("save-new", netService);

        LanConnectCurrentSaveBindingWriter.PersistOutcome outcome = harness.Writer.Persist(
            Target(netService, expectedSaveKey: "save-origin"),
            "save_event");

        Assert.Equal(
            LanConnectCurrentSaveBindingWriter.PersistResult.RefusedDifferentSave,
            outcome.Result);
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void Keyless_host_origin_writes_only_for_the_current_run_net_service()
    {
        object netService = new();
        WriterHarness harness = new("save-1", netService);

        LanConnectCurrentSaveBindingWriter.PersistOutcome outcome = harness.Writer.Persist(
            Target(netService, expectedSaveKey: null),
            "save_event");

        Assert.Equal(LanConnectCurrentSaveBindingWriter.PersistResult.Persisted, outcome.Result);
        Assert.Single(harness.Writes);
    }

    [Fact]
    public void Matching_session_key_writes_lobby_binding_once()
    {
        object netService = new();
        WriterHarness harness = new("save-1", currentNetService: new object());

        LanConnectCurrentSaveBindingWriter.PersistOutcome outcome = harness.Writer.Persist(
            Target(netService, expectedSaveKey: "save-1"),
            "save_event");

        Assert.Equal(LanConnectCurrentSaveBindingWriter.PersistResult.Persisted, outcome.Result);
        (LanConnectCurrentSaveBindingWriter.LoadedSave Save,
            LanConnectCurrentSaveBindingWriter.PersistenceRequest Request) write = Assert.Single(harness.Writes);
        Assert.Equal("save-1", write.Save.SaveKey);
        Assert.Equal(LanConnectHostChannels.Lobby, write.Request.HostChannel);
    }

    private static LanConnectCurrentSaveBindingWriter.BindingTarget Target(
        object netService,
        string? expectedSaveKey,
        bool isClosing = false) =>
        new(
            netService,
            isClosing,
            expectedSaveKey,
            "大厅续局",
            null,
            "standard",
            LanConnectHostChannels.Lobby);

    private sealed class WriterHarness
    {
        public WriterHarness(string saveKey, object currentNetService)
        {
            Writer = new LanConnectCurrentSaveBindingWriter(
                () => new LanConnectCurrentSaveBindingWriter.LoadResult(
                    true,
                    saveKey,
                    saveKey,
                    string.Empty),
                () => currentNetService,
                (save, request) => Writes.Add((save, request)));
        }

        public LanConnectCurrentSaveBindingWriter Writer { get; }

        public List<(LanConnectCurrentSaveBindingWriter.LoadedSave Save,
            LanConnectCurrentSaveBindingWriter.PersistenceRequest Request)> Writes { get; } = new();
    }
}
