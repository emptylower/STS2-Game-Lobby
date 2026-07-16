using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectRoomRichCompatibilityTests
{
    private static readonly LanConnectChatFeatureVersions All = new(1, 1, 1, 1);
    private static readonly LanConnectChatFeatureVersions None = new();

    [Fact]
    public void New_new_new_old_old_new_and_missing_version_matrix_is_fail_closed()
    {
        LanConnectChatContent content = new(1,
        [
            new LanConnectTextSegment("look "),
            new LanConnectEmojiSegment("heart"),
            new LanConnectItemRefSegment("card", "MegaCrit.Strike", 1),
            new LanConnectPowerStateSegment("MegaCrit.Strength", 2, "session-a"),
            new LanConnectTargetRefSegment("player", "net:watcher", "session-a")
        ]);

        LanConnectChatFeatureVersions newNew = Resolve(All, All);
        LanConnectChatFeatureVersions newOld = Resolve(All, None);
        LanConnectChatFeatureVersions oldNew = Resolve(None, All);
        Assert.Equal(All, newNew);
        Assert.Equal(None, newOld);
        Assert.Equal(None, oldNew);
        Assert.True(LanConnectChatFeatureResolver.SupportsContent(content, newNew));
        Assert.False(LanConnectChatFeatureResolver.SupportsContent(content, newOld));
        Assert.False(LanConnectChatFeatureResolver.SupportsContent(content, oldNew));
        Assert.Equal(
            "look [Emoji][Card][Power][Player]",
            LanConnectServerChatProtocol.RenderLegacyRoomFallback(content));

        LanConnectRoomChatReadyEnvelope missingVersions = JsonSerializer.Deserialize<LanConnectRoomChatReadyEnvelope>(
            """{"type":"room_chat_ready","protocolVersion":1,"roomId":"room-a","roomSessionId":"session-a"}""",
            LanConnectJson.Options) ?? throw new InvalidOperationException("Missing-version fixture returned null.");
        Assert.Equal(None, missingVersions.EnabledFeatures);

        foreach (string modVersion in new[] { "0.5.0", "0.4.0", "0.2.2" })
        {
            LobbyControlEnvelope legacy = JsonSerializer.Deserialize<LobbyControlEnvelope>(
                $$"""{"type":"room_chat","roomId":"room-a","playerName":"Legacy {{modVersion}}","playerNetId":"net:legacy","messageId":"legacy-1","messageText":"legacy text","sentAtUnixMs":1784073600000}""",
                LanConnectJson.Options) ?? throw new InvalidOperationException("Legacy fixture returned null.");
            Assert.Equal("room_chat", legacy.Type);
            Assert.Equal("legacy text", legacy.MessageText);
        }
    }

    [Fact]
    public async Task Same_generation_reconnect_preserves_room_while_republish_replaces_only_room_context()
    {
        FakeServerChatClient server = new();
        await using LanConnectLobbyRuntimeChatCoordinator coordinator = new(server);
        coordinator.EnterRoom("room-a");
        coordinator.State.Room.SetDraft("generation-a draft");
        coordinator.State.Room.AppendConfirmedForTests("room-a-message", "Watcher", "keep", 1, false);
        coordinator.SetRoomRichFeatures(All);
        server.State.SetDraft("server draft");
        server.State.AppendConfirmedForTests("server-message", "Server", "independent", 1, false);

        bool closed = false;
        LanConnectLobbyRuntime.RebindRoomGeneration(
            coordinator,
            "room-a",
            "session-a",
            "room-a",
            "session-a",
            () => closed = true);

        Assert.True(closed);
        Assert.Equal("generation-a draft", coordinator.State.Room.Draft);
        Assert.Single(coordinator.State.Room.Messages);
        Assert.Equal(None, coordinator.State.Room.EnabledRichFeatures);
        Assert.Equal("server draft", server.State.Draft);
        Assert.Single(server.State.Messages);

        LanConnectLobbyRuntime.RebindRoomGeneration(
            coordinator,
            "room-a",
            "session-b",
            "room-a",
            "session-a",
            static () => { });

        Assert.Equal("room-a", coordinator.State.ActiveRoomId);
        Assert.Equal(string.Empty, coordinator.State.Room.Draft);
        Assert.Empty(coordinator.State.Room.Messages);
        Assert.Equal(None, coordinator.State.Room.EnabledRichFeatures);
        Assert.Equal("server draft", server.State.Draft);
        Assert.Single(server.State.Messages);
    }

    [Fact]
    public void Stale_combat_degrades_per_segment_static_items_survive_and_monster_stays_disabled()
    {
        CompatibilityCombatContext context = new();
        LanConnectRoomCombatReferenceResolver resolver = new(context, new LanConnectChatLocalizer());
        LanConnectCombatRun power = new(new LanConnectPowerStateSegment(
            "MegaCrit.Strength", 2, "session-a"));
        LanConnectCombatRun player = new(new LanConnectTargetRefSegment(
            "player", "net:watcher", "session-a"));
        LanConnectCombatRun monster = new(new LanConnectTargetRefSegment(
            "monster", "prototype:1", "session-a"));

        Assert.Equal("Strength +2", resolver.Resolve(power, "en-US").Label);
        Assert.Equal("Watcher", resolver.Resolve(player, "en-US").Label);
        Assert.Equal("Target is no longer available", resolver.Resolve(monster, "en-US").Label);
        Assert.False(LanConnectChatFeatureResolver.MonsterTargetRefsEnabled);

        context.ActiveRoomSessionId = "session-b";
        Assert.Equal("Unknown power", resolver.Resolve(power, "en-US").Label);
        Assert.Equal("Target is no longer available", resolver.Resolve(player, "en-US").Label);
        LanConnectChatContent staticItem = new(1,
            [new LanConnectItemRefSegment("relic", "MegaCrit.Anchor")]);
        Assert.True(LanConnectChatFeatureResolver.SupportsContent(staticItem, All));
        Assert.True(LanConnectRoomChatSessionContext.ContentMatches(staticItem, "session-b"));
    }

    private static LanConnectChatFeatureVersions Resolve(
        LanConnectChatFeatureVersions sender,
        LanConnectChatFeatureVersions receiver) =>
        LanConnectChatFeatureResolver.Resolve(new LanConnectChatFeatureInput
        {
            Channel = LanConnectChatChannel.Room,
            Compiled = All,
            Configured = All,
            ChannelEnabled = true,
            RoomV2Enabled = true,
            Sender = sender,
            Receiver = receiver
        });

    private sealed class CompatibilityCombatContext : ILanConnectRoomCombatContext
    {
        public string ActiveRoomSessionId { get; set; } = "session-a";

        public bool IsCurrentPeer(string playerNetId) => playerNetId == "net:watcher";

        public bool TryGetCurrentPeerName(string playerNetId, out string name)
        {
            name = playerNetId == "net:watcher" ? "Watcher" : string.Empty;
            return name.Length > 0;
        }

        public bool TryResolveLocalPower(string modelId, out LanConnectLocalPowerReference power)
        {
            power = new LanConnectLocalPowerReference("Strength", "Gain attack damage.");
            return modelId == "MegaCrit.Strength";
        }
    }

    private sealed class FakeServerChatClient : ILanConnectServerChatClient
    {
        public LanConnectChatChannelState State { get; } = new(LanConnectChatChannel.Server);

        public event Action? StateChanged
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(
            Uri lobbyBaseUri,
            string playerNetId,
            string playerName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendAsync(
            LanConnectChatContent content,
            string clientMessageId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RetryAsync(string clientMessageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
