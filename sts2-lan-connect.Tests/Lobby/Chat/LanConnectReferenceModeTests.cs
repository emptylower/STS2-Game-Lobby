using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectReferenceModeTests
{
    [Theory]
    [InlineData((int)LanConnectReferenceModeSource.TouchButton)]
    [InlineData((int)LanConnectReferenceModeSource.KeyboardShortcut)]
    public void Button_and_shortcut_toggle_idle_armed_idle(int sourceValue)
    {
        LanConnectReferenceModeSource source = (LanConnectReferenceModeSource)sourceValue;
        LanConnectReferenceMode mode = new();
        LanConnectReferenceModeContext context = RoomContext();

        Assert.True(mode.Toggle(context, source));
        Assert.Equal(LanConnectReferenceModeState.Armed, mode.State);
        Assert.Equal(source, mode.Source);
        Assert.Equal(1, mode.ArmedGeneration);
        Assert.Same(context.ChannelState, mode.ArmedChannelState);

        Assert.True(mode.Toggle(context, source));
        Assert.Equal(LanConnectReferenceModeState.Idle, mode.State);
        Assert.Equal(LanConnectReferenceModeExitReason.Canceled, mode.LastExitReason);
        Assert.Null(mode.Source);
    }

    [Fact]
    public void Direct_alt_click_uses_the_same_armed_generation_without_toggling_it_off()
    {
        LanConnectReferenceMode mode = new();
        LanConnectReferenceModeContext context = RoomContext();

        Assert.True(mode.ArmDirect(context));
        long generation = mode.ArmedGeneration;
        Assert.Equal(LanConnectReferenceModeSource.DirectAltClick, mode.Source);

        Assert.False(mode.ArmDirect(context));
        Assert.Equal(LanConnectReferenceModeState.Armed, mode.State);
        Assert.Equal(generation, mode.ArmedGeneration);
    }

    [Fact]
    public void Successful_capture_consumes_once_exits_and_rejects_duplicate_or_stale_generation()
    {
        LanConnectReferenceMode mode = new();
        LanConnectReferenceModeContext context = RoomContext();
        mode.Toggle(context, LanConnectReferenceModeSource.TouchButton);
        long firstGeneration = mode.ArmedGeneration;

        Assert.True(mode.CanCapture(firstGeneration, context, LanConnectReferenceTargetKind.Item));
        Assert.True(mode.CaptureSucceeded(firstGeneration, context, LanConnectReferenceTargetKind.Item));
        Assert.Equal(LanConnectReferenceModeState.Idle, mode.State);
        Assert.Equal(LanConnectReferenceModeExitReason.Captured, mode.LastExitReason);
        Assert.False(mode.CaptureSucceeded(firstGeneration, context, LanConnectReferenceTargetKind.Item));

        Assert.True(mode.Toggle(context, LanConnectReferenceModeSource.TouchButton));
        Assert.Equal(firstGeneration + 1, mode.ArmedGeneration);
        Assert.False(mode.CanCapture(firstGeneration, context, LanConnectReferenceTargetKind.Item));
        Assert.False(mode.CaptureSucceeded(firstGeneration, context, LanConnectReferenceTargetKind.Item));
        Assert.Equal(LanConnectReferenceModeState.Armed, mode.State);
    }

    [Fact]
    public void Unsupported_or_failed_capture_stays_armed_and_is_not_consumed()
    {
        LanConnectReferenceMode mode = new();
        LanConnectReferenceModeContext context = ServerContext();
        mode.Toggle(context, LanConnectReferenceModeSource.TouchButton);
        long generation = mode.ArmedGeneration;

        Assert.False(mode.CanCapture(generation, context, LanConnectReferenceTargetKind.Combat));
        Assert.False(mode.CaptureFailed(generation, context));
        Assert.Equal(LanConnectReferenceModeState.Armed, mode.State);
        Assert.Equal(generation, mode.ArmedGeneration);
        Assert.Null(mode.LastExitReason);
    }

    [Fact]
    public void Server_allows_only_items_and_room_allows_negotiated_item_or_combat_targets()
    {
        LanConnectReferenceMode server = new();
        LanConnectReferenceModeContext serverContext = ServerContext();
        server.Toggle(serverContext, LanConnectReferenceModeSource.KeyboardShortcut);
        Assert.True(server.CanCapture(server.ArmedGeneration, serverContext, LanConnectReferenceTargetKind.Item));
        Assert.False(server.CanCapture(server.ArmedGeneration, serverContext, LanConnectReferenceTargetKind.Combat));

        LanConnectReferenceMode room = new();
        LanConnectReferenceModeContext combatOnly = RoomContext(
            LanConnectReferenceTargetKind.Combat);
        room.Toggle(combatOnly, LanConnectReferenceModeSource.TouchButton);
        Assert.False(room.CanCapture(room.ArmedGeneration, combatOnly, LanConnectReferenceTargetKind.Item));
        Assert.True(room.CanCapture(room.ArmedGeneration, combatOnly, LanConnectReferenceTargetKind.Combat));
    }

    [Theory]
    [InlineData((int)LanConnectReferenceModeExitReason.Canceled)]
    [InlineData((int)LanConnectReferenceModeExitReason.OverlayClosed)]
    public void Explicit_cancel_paths_exit_without_changing_the_draft(int reasonValue)
    {
        LanConnectReferenceModeExitReason reason = (LanConnectReferenceModeExitReason)reasonValue;
        LanConnectReferenceMode mode = new();
        LanConnectReferenceModeContext context = RoomContext();
        mode.Toggle(context, LanConnectReferenceModeSource.KeyboardShortcut);

        Assert.True(mode.Exit(reason));
        Assert.Equal(LanConnectReferenceModeState.Idle, mode.State);
        Assert.Equal(reason, mode.LastExitReason);
        Assert.False(mode.Exit(reason));
    }

    [Fact]
    public void Channel_identity_room_and_capability_changes_exit_with_specific_reasons()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        LanConnectReferenceModeContext original = RoomContext(channelState: state);

        AssertContextExit(
            original,
            original with
            {
                Channel = LanConnectChatChannel.Server,
                ChannelState = new LanConnectChatChannelState(LanConnectChatChannel.Server),
                RoomId = null,
                RoomSessionId = null,
                AllowedTargets = LanConnectReferenceTargetKind.Item
            },
            LanConnectReferenceModeExitReason.ChannelChanged);
        AssertContextExit(
            original,
            original with { ChannelState = new LanConnectChatChannelState(LanConnectChatChannel.Room) },
            LanConnectReferenceModeExitReason.ChannelChanged);
        AssertContextExit(
            original,
            original with { RoomSessionId = "session-2" },
            LanConnectReferenceModeExitReason.RoomChanged);
        AssertContextExit(
            original,
            original with { RoomId = "room-2" },
            LanConnectReferenceModeExitReason.RoomChanged);
        AssertContextExit(
            original,
            original with { AllowedTargets = LanConnectReferenceTargetKind.None },
            LanConnectReferenceModeExitReason.CapabilityLost);
    }

    [Fact]
    public void Missing_chat_target_cannot_arm_and_partial_capability_loss_keeps_room_mode_armed()
    {
        LanConnectReferenceMode mode = new();
        LanConnectReferenceModeContext missing = RoomContext() with { HasChatTarget = false };

        Assert.False(mode.Toggle(missing, LanConnectReferenceModeSource.TouchButton));
        Assert.Equal(LanConnectReferenceModeState.Idle, mode.State);

        LanConnectReferenceModeContext both = RoomContext();
        Assert.True(mode.Toggle(both, LanConnectReferenceModeSource.TouchButton));
        LanConnectReferenceModeContext combatOnly = both with
        {
            AllowedTargets = LanConnectReferenceTargetKind.Combat
        };
        Assert.False(mode.Synchronize(combatOnly));
        Assert.Equal(LanConnectReferenceModeState.Armed, mode.State);
        Assert.False(mode.CanCapture(mode.ArmedGeneration, combatOnly, LanConnectReferenceTargetKind.Item));
        Assert.True(mode.CanCapture(mode.ArmedGeneration, combatOnly, LanConnectReferenceTargetKind.Combat));
    }

    [Fact]
    public void State_machine_has_no_Godot_node_draft_or_transport_ownership()
    {
        Type type = typeof(LanConnectReferenceMode);

        Assert.False(typeof(Node).IsAssignableFrom(type));
        Assert.DoesNotContain(type.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType == typeof(LanConnectRichDraft) ||
                     typeof(Node).IsAssignableFrom(field.FieldType) ||
                     typeof(Task).IsAssignableFrom(field.FieldType));
    }

    private static void AssertContextExit(
        LanConnectReferenceModeContext original,
        LanConnectReferenceModeContext changed,
        LanConnectReferenceModeExitReason reason)
    {
        LanConnectReferenceMode mode = new();
        mode.Toggle(original, LanConnectReferenceModeSource.TouchButton);

        Assert.True(mode.Synchronize(changed));
        Assert.Equal(LanConnectReferenceModeState.Idle, mode.State);
        Assert.Equal(reason, mode.LastExitReason);
    }

    private static LanConnectReferenceModeContext ServerContext() => new(
        HasChatTarget: true,
        LanConnectChatChannel.Server,
        new LanConnectChatChannelState(LanConnectChatChannel.Server),
        RoomId: null,
        RoomSessionId: null,
        LanConnectReferenceTargetKind.Item);

    private static LanConnectReferenceModeContext RoomContext(
        LanConnectReferenceTargetKind allowedTargets =
            LanConnectReferenceTargetKind.Item | LanConnectReferenceTargetKind.Combat,
        LanConnectChatChannelState? channelState = null) => new(
        HasChatTarget: true,
        LanConnectChatChannel.Room,
        channelState ?? new LanConnectChatChannelState(LanConnectChatChannel.Room),
        RoomId: "room-1",
        RoomSessionId: "session-1",
        allowedTargets);
}
