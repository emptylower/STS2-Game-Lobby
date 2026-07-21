using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectReferenceCaptureRequestTests
{
    [Fact]
    public void Touch_capture_uses_position_hit_test_instead_of_hover_state()
    {
        CapturePorts ports = new();
        LanConnectItemLinkCapture capture = new(ports);
        LanConnectReferenceCaptureRequest request = new(
            LanConnectReferenceModeSource.TouchButton,
            LanConnectReferencePointerKind.Touch,
            Pressed: true,
            ScreenPosition: new Vector2(120, 240),
            StartingControl: null,
            ArmedGeneration: 4);

        LanConnectReferenceCaptureResult result = capture.TryCapture(request);

        Assert.Equal(LanConnectReferenceCaptureStatus.Captured, result.Status);
        Assert.Equal(LanConnectReferenceTargetKind.Item, result.TargetKind);
        Assert.True(result.Consumed);
        Assert.Equal(new Vector2(120, 240), ports.LastHitTestPosition);
        Assert.Equal(0, ports.HoverCalls);
        Assert.Equal(1, ports.InsertCalls);
    }

    [Fact]
    public void Unsupported_touch_does_not_consume_or_insert()
    {
        CapturePorts ports = new() { HitTarget = false };
        LanConnectReferenceCaptureResult result = new LanConnectItemLinkCapture(ports).TryCapture(
            new LanConnectReferenceCaptureRequest(
            LanConnectReferenceModeSource.TouchButton,
            LanConnectReferencePointerKind.Touch,
            Pressed: true,
            ScreenPosition: new Vector2(40, 80),
            StartingControl: null,
            ArmedGeneration: 1));

        Assert.Equal(LanConnectReferenceCaptureStatus.Unsupported, result.Status);
        Assert.False(result.Consumed);
        Assert.Equal(0, ports.InsertCalls);
    }

    [Fact]
    public void Mouse_request_prefers_explicit_start_and_direct_alt_source_uses_same_capture_path()
    {
        CapturePorts ports = new();
        object explicitStart = ports.Holder;
        LanConnectReferenceCaptureResult result = new LanConnectItemLinkCapture(ports).TryCapture(
            new LanConnectReferenceCaptureRequest(
            LanConnectReferenceModeSource.DirectAltClick,
            LanConnectReferencePointerKind.Mouse,
            Pressed: true,
            ScreenPosition: new Vector2(10, 20),
            StartingControl: explicitStart,
            ArmedGeneration: 9));

        Assert.Equal(LanConnectReferenceCaptureStatus.Captured, result.Status);
        Assert.Equal(0, ports.HoverCalls);
        Assert.Equal(0, ports.HitTestCalls);
    }

    [Fact]
    public void Touch_mouse_deduplicator_suppresses_only_the_matching_synthetic_mouse_press()
    {
        LanConnectReferenceTouchMouseDeduplicator dedupe = new(
            maxAgeMilliseconds: 600,
            maxDistance: 24f);
        dedupe.ObserveTouch(new Vector2(100, 100), timestampMilliseconds: 1_000);

        Assert.True(dedupe.IsSyntheticMouse(new Vector2(112, 108), timestampMilliseconds: 1_300));
        Assert.False(dedupe.IsSyntheticMouse(new Vector2(112, 108), timestampMilliseconds: 1_301));

        dedupe.ObserveTouch(new Vector2(100, 100), timestampMilliseconds: 2_000);
        Assert.False(dedupe.IsSyntheticMouse(new Vector2(180, 180), timestampMilliseconds: 2_100));
        Assert.False(dedupe.IsSyntheticMouse(new Vector2(100, 100), timestampMilliseconds: 3_000));
    }

    private sealed class CapturePorts : ILanConnectItemLinkCapturePorts
    {
        internal object Holder { get; } = new object();
        internal bool HitTarget { get; set; } = true;
        internal int HoverCalls { get; private set; }
        internal int HitTestCalls { get; private set; }
        internal int InsertCalls { get; private set; }
        internal Vector2 LastHitTestPosition { get; private set; }

        public object? GuiGetHoveredControl() { HoverCalls++; return Holder; }
        public object? GuiGetControlAtPosition(Vector2 position)
        {
            HitTestCalls++;
            LastHitTestPosition = position;
            return HitTarget ? Holder : null;
        }
        public object? GetParent(object node) => null;
        public bool IsChatInteractionBlocking => false;
        public bool ItemRefsEnabledForSelectedChannel => true;
        public bool CombatRefsEnabledForSelectedChannel => true;
        public bool IsRoomChannelSelected => true;
        public bool IsCaptureBoundary(object node) => false;
        public bool IsSupportedHolder(object node) => ReferenceEquals(node, Holder);
        public bool IsPowerHolder(object node) => false;
        public bool IsPlayerHolder(object node) => false;
        public bool TryResolveCard(object node, out LanConnectItemRun run)
        {
            run = new LanConnectItemRun("card", "MegaCrit.Strike", 1);
            return ReferenceEquals(node, Holder);
        }
        public bool TryResolveRelic(object node, out LanConnectItemRun run) { run = null!; return false; }
        public bool TryResolvePotion(object node, out LanConnectItemRun run) { run = null!; return false; }
        public bool TryResolvePower(object node, out LanConnectCombatRun run) { run = null!; return false; }
        public bool TryResolvePlayerTarget(object node, out LanConnectCombatRun run) { run = null!; return false; }
        public bool InsertAndFocus(LanConnectItemRun run) { InsertCalls++; return true; }
        public bool InsertCombatAndFocus(LanConnectCombatRun run) => false;
        public void ShowCombatRoomOnlyWarning() { }
    }
}
