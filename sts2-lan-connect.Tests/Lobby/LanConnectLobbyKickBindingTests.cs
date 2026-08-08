using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyKickBindingTests
{
    [Fact]
    public async Task Legacy_service_uses_only_guarded_local_removal_without_sending_a_kick()
    {
        int serverKickSends = 0;
        LanConnectLobbyKickResult result =
            await LanConnectLobbyKickCompatibility.SendOrRemoveLocallyAsync(
                bindingAwareKickSupported: false,
                "Current Occupant",
                () =>
                {
                    serverKickSends++;
                    return Task.FromResult(new LanConnectLobbyKickResult(
                        true,
                        true,
                        true,
                        "accepted",
                        string.Empty));
                });

        Assert.Equal(0, serverKickSends);
        Assert.False(result.Accepted);
        Assert.True(result.ShouldScheduleDisconnect);
        Assert.False(result.PersistentBanRequested);
        Assert.Equal("local_only_not_banned", result.Reason);
        Assert.Equal(
            "旧版大厅服务不支持安全封禁：仅在本地移出 Current Occupant，不会封禁；该玩家仍可重新加入。",
            result.Message);

        LanConnectLobbyKickTargetDirectory directory = new();
        Assert.True(directory.RememberBinding("slot-legacy", "binding-original"));
        Assert.True(directory.ObserveConnected("slot-legacy"));
        LanConnectLobbyKickTarget original = directory.Capture("slot-legacy", "Current Occupant");
        Assert.True(directory.RememberBinding("slot-legacy", "binding-replacement"));

        int disconnects = 0;
        if (result.ShouldScheduleDisconnect)
        {
            Assert.False(directory.TryRunIfCurrent(original, () => disconnects++));
        }
        Assert.Equal(0, disconnects);
        Assert.Equal(
            "binding-replacement",
            directory.Capture("slot-legacy", "Replacement").BindingId);
    }

    [Fact]
    public void Modern_kick_carries_slot_and_opaque_binding_with_contract_casing()
    {
        LobbyControlEnvelope envelope = LanConnectLobbyRuntime.BuildKickPlayerEnvelope(
            "room-1",
            "save-slot-owner",
            "Current Occupant",
            "binding-current");

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(envelope, LanConnectJson.Options));
        Assert.Equal("kick_player", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("save-slot-owner", json.RootElement.GetProperty("playerNetId").GetString());
        Assert.Equal("binding-current", json.RootElement.GetProperty("bindingId").GetString());
        Assert.Equal("save-slot-owner", json.RootElement.GetProperty("targetPlayerNetId").GetString());
        Assert.False(json.RootElement.TryGetProperty("clientInstallationId", out _));
    }

    [Fact]
    public void Missing_server_handle_uses_only_the_legacy_slot_field()
    {
        LobbyControlEnvelope envelope = LanConnectLobbyRuntime.BuildKickPlayerEnvelope(
            "room-1",
            "save-slot-owner",
            "Legacy Occupant",
            null);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(envelope, LanConnectJson.Options));
        Assert.Equal("save-slot-owner", json.RootElement.GetProperty("targetPlayerNetId").GetString());
        Assert.False(json.RootElement.TryGetProperty("playerNetId", out _));
        Assert.False(json.RootElement.TryGetProperty("bindingId", out _));
    }

    [Fact]
    public void Render_then_takeover_then_click_sends_the_captured_stale_handle()
    {
        LanConnectLobbyKickTargetDirectory directory = new();
        Assert.True(directory.ObserveConnected("save-slot-owner"));
        Assert.True(directory.RememberBinding("save-slot-owner", "binding-original"));
        LanConnectLobbyKickTarget rendered = directory.Capture(
            "save-slot-owner",
            "Original Occupant");

        Assert.True(directory.RememberBinding("save-slot-owner", "binding-replacement"));
        LobbyControlEnvelope envelope = LanConnectLobbyRuntime.BuildKickPlayerEnvelope(
            "room-1",
            rendered,
            "request-rendered-target");

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(envelope, LanConnectJson.Options));
        Assert.Equal("save-slot-owner", json.RootElement.GetProperty("playerNetId").GetString());
        Assert.Equal("binding-original", json.RootElement.GetProperty("bindingId").GetString());
        Assert.Equal("Original Occupant", json.RootElement.GetProperty("targetPlayerName").GetString());
        Assert.Equal("request-rendered-target", json.RootElement.GetProperty("kickRequestId").GetString());
        Assert.False(directory.IsCurrent(rendered));
    }

    [Fact]
    public void Takeover_during_disconnect_delay_cancels_the_captured_target()
    {
        LanConnectLobbyKickTargetDirectory directory = new();
        Assert.True(directory.RememberBinding("slot-7", "binding-7a"));
        Assert.True(directory.ObserveConnected("slot-7"));
        LanConnectLobbyKickTarget acceptedTarget = directory.Capture("slot-7", "First");
        LanConnectLobbyKickResult accepted = LanConnectLobbyKickResult.FromResponse(
            acceptedTarget,
            accepted: true,
            playerNetId: "slot-7",
            bindingId: "binding-7a",
            reason: null,
            message: null);
        Assert.True(accepted.Accepted);
        Assert.True(directory.IsCurrent(acceptedTarget));

        Assert.True(directory.RememberBinding("slot-7", "binding-7b"));

        Assert.False(directory.IsCurrent(acceptedTarget));
        int disconnects = 0;
        Assert.False(directory.TryRunIfCurrent(acceptedTarget, () => disconnects++));
        Assert.Equal(0, disconnects);
        Assert.Equal("binding-7b", directory.Capture("slot-7", "Replacement").BindingId);
    }

    [Fact]
    public void Rejected_kick_has_retry_message_and_never_authorizes_disconnect()
    {
        LanConnectLobbyKickTargetDirectory directory = new();
        Assert.True(directory.RememberBinding("slot-8", "binding-8"));
        Assert.True(directory.ObserveConnected("slot-8"));
        LanConnectLobbyKickTarget target = directory.Capture("slot-8", "Player");

        LanConnectLobbyKickResult rejected = LanConnectLobbyKickResult.FromResponse(
            target,
            accepted: false,
            playerNetId: "slot-8",
            bindingId: "binding-8",
            reason: "stale_binding",
            message: "目标玩家已变化，请刷新列表后重试。");

        Assert.False(rejected.Accepted);
        Assert.False(rejected.ShouldScheduleDisconnect);
        Assert.Equal("stale_binding", rejected.Reason);
        Assert.Equal("目标玩家已变化，请刷新列表后重试。", rejected.Message);
        Assert.True(directory.IsCurrent(target));
        int disconnects = 0;
        if (rejected.ShouldScheduleDisconnect)
        {
            directory.TryRunIfCurrent(target, () => disconnects++);
        }
        Assert.Equal(0, disconnects);
    }

    [Fact]
    public void New_connection_generation_cancels_a_pending_disconnect_even_with_same_binding()
    {
        LanConnectLobbyKickTargetDirectory directory = new();
        Assert.True(directory.RememberBinding("slot-9", "binding-9"));
        Assert.True(directory.ObserveConnected("slot-9"));
        LanConnectLobbyKickTarget previousConnection = directory.Capture("slot-9", "Player");

        directory.ObserveDisconnected("slot-9");
        Assert.True(directory.RememberBinding("slot-9", "binding-9"));
        Assert.True(directory.ObserveConnected("slot-9"));

        LanConnectLobbyKickTarget replacementConnection = directory.Capture("slot-9", "Player");
        Assert.NotEqual(previousConnection.ConnectionGeneration, replacementConnection.ConnectionGeneration);
        Assert.False(directory.IsCurrent(previousConnection));
        Assert.True(directory.IsCurrent(replacementConnection));
    }

    [Fact]
    public void Binding_cache_is_capped_and_only_discards_disconnected_entries()
    {
        LanConnectLobbyKickTargetDirectory directory = new();
        for (int index = 0; index < LanConnectLobbyKickTargetDirectory.Capacity - 1; index++)
        {
            string slot = $"connected-{index}";
            Assert.True(directory.RememberBinding(slot, $"binding-{index}"));
            Assert.True(directory.ObserveConnected(slot));
        }
        Assert.True(directory.RememberBinding("obsolete", "binding-obsolete"));
        Assert.Equal(LanConnectLobbyKickTargetDirectory.Capacity, directory.Count);

        Assert.True(directory.RememberBinding("incoming", "binding-incoming"));

        Assert.Equal(LanConnectLobbyKickTargetDirectory.Capacity, directory.Count);
        Assert.Null(directory.Capture("obsolete", "Obsolete").BindingId);
        Assert.Equal("binding-incoming", directory.Capture("incoming", "Incoming").BindingId);
        Assert.Equal("binding-0", directory.Capture("connected-0", "Connected").BindingId);
    }
}
