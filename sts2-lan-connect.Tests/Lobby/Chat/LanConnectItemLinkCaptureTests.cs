using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectItemLinkCaptureTests
{
    [Theory]
    [InlineData("card", "MegaCrit.Strike", 2)]
    [InlineData("relic", "MegaCrit.Anchor", null)]
    [InlineData("potion", "MegaCrit.FirePotion", null)]
    public void Successful_alt_left_capture_inserts_and_focuses_without_sending(
        string itemType,
        string modelId,
        int? upgradeLevel)
    {
        FakeCaptureNode holder = new(itemType, new LanConnectItemRun(itemType, modelId, upgradeLevel));
        FakeCapturePorts ports = new() { Hovered = new FakeCaptureNode("child", parent: holder) };

        bool consumed = Capture(ports);

        Assert.True(consumed);
        Assert.Equal(new LanConnectItemRun(itemType, modelId, upgradeLevel), Assert.Single(ports.Inserted));
        Assert.Equal(1, ports.OpenAndFocusCalls);
        Assert.Equal(0, ports.SendCalls);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Wrong_gesture_does_not_consume_or_insert(bool alt, bool left, bool pressed)
    {
        FakeCapturePorts ports = CardPorts();

        bool consumed = new LanConnectItemLinkCapture(ports).TryCapture(
            new LanConnectPointerGesture(alt, left, pressed));

        AssertNotCaptured(ports, consumed);
    }

    [Fact]
    public void Unsupported_child_without_supported_parent_is_not_consumed()
    {
        FakeCapturePorts ports = new() { Hovered = new FakeCaptureNode("button") };

        bool consumed = Capture(ports);

        AssertNotCaptured(ports, consumed);
    }

    [Theory]
    [InlineData(nameof(FakeCapturePorts.ItemRefsEnabledForSelectedChannel))]
    [InlineData(nameof(FakeCapturePorts.ActiveTextControl))]
    [InlineData(nameof(FakeCapturePorts.PickerVisible))]
    [InlineData(nameof(FakeCapturePorts.PreviewVisible))]
    [InlineData(nameof(FakeCapturePorts.BlockingDialogVisible))]
    [InlineData(nameof(FakeCapturePorts.CardPreviewInteraction))]
    public void Disabled_feature_or_blocked_interaction_is_not_consumed(string guard)
    {
        FakeCapturePorts ports = CardPorts();
        switch (guard)
        {
            case nameof(FakeCapturePorts.ItemRefsEnabledForSelectedChannel):
                ports.ItemRefsEnabledForSelectedChannel = false;
                break;
            case nameof(FakeCapturePorts.ActiveTextControl):
                ports.ActiveTextControl = true;
                break;
            case nameof(FakeCapturePorts.PickerVisible):
                ports.PickerVisible = true;
                break;
            case nameof(FakeCapturePorts.PreviewVisible):
                ports.PreviewVisible = true;
                break;
            case nameof(FakeCapturePorts.BlockingDialogVisible):
                ports.BlockingDialogVisible = true;
                break;
            case nameof(FakeCapturePorts.CardPreviewInteraction):
                ports.CardPreviewInteraction = true;
                break;
        }

        bool consumed = Capture(ports);

        AssertNotCaptured(ports, consumed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mega Crit.Strike")]
    [InlineData("MegaCrit.Strike\nInjected")]
    [InlineData("MegaCrit.Strike/../../secret")]
    public void Malformed_model_id_is_not_consumed(string modelId)
    {
        FakeCapturePorts ports = new()
        {
            Hovered = new FakeCaptureNode("card", new LanConnectItemRun("card", modelId, 1))
        };

        bool consumed = Capture(ports);

        AssertNotCaptured(ports, consumed);
    }

    [Theory]
    [InlineData(-8, 0)]
    [InlineData(4, 4)]
    [InlineData(18, 9)]
    public void Card_upgrade_is_clamped_to_protocol_range(int sourceUpgrade, int expectedUpgrade)
    {
        FakeCapturePorts ports = new()
        {
            Hovered = new FakeCaptureNode(
                "card",
                new LanConnectItemRun("card", "MegaCrit.Strike", sourceUpgrade))
        };

        bool consumed = Capture(ports);

        Assert.True(consumed);
        Assert.Equal(
            new LanConnectItemRun("card", "MegaCrit.Strike", expectedUpgrade),
            Assert.Single(ports.Inserted));
    }

    [Fact]
    public void Parent_walk_stops_at_first_supported_holder()
    {
        FakeCaptureNode outerRelic = new(
            "relic",
            new LanConnectItemRun("relic", "MegaCrit.Anchor"));
        FakeCaptureNode innerCard = new(
            "card",
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            parent: outerRelic);
        FakeCapturePorts ports = new()
        {
            Hovered = new FakeCaptureNode("hitbox", parent: innerCard)
        };

        bool consumed = Capture(ports);

        Assert.True(consumed);
        Assert.Equal(
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            Assert.Single(ports.Inserted));
        Assert.Equal(
            [
                "card:hitbox",
                "relic:hitbox",
                "potion:hitbox",
                "power:hitbox",
                "player:hitbox",
                "card:card"
            ],
            ports.ResolveAttempts);
    }

    [Fact]
    public void Empty_supported_holder_stops_before_supported_parent()
    {
        FakeCaptureNode outerRelic = new(
            "relic",
            new LanConnectItemRun("relic", "MegaCrit.Anchor"));
        FakeCapturePorts ports = new()
        {
            Hovered = new FakeCaptureNode("card", parent: outerRelic)
        };

        bool consumed = Capture(ports);

        AssertNotCaptured(ports, consumed);
        Assert.Equal(["card:card", "relic:card", "potion:card"], ports.ResolveAttempts);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(1, 2, false)]
    public void Item_capability_requires_exact_supported_versions(
        int richVersion,
        int itemVersion,
        bool expected)
    {
        Assert.Equal(
            expected,
            LanConnectItemLinkCapture.ItemRefsEnabled(
                new LanConnectChatFeatureVersions(richVersion, 0, itemVersion, 0)));
    }

    [Fact]
    public void Resolver_exception_is_not_consumed()
    {
        FakeCapturePorts ports = CardPorts();
        ports.ThrowOnResolve = true;

        bool consumed = Capture(ports);

        AssertNotCaptured(ports, consumed);
    }

    [Fact]
    public void Failed_insert_and_focus_is_not_consumed()
    {
        FakeCapturePorts ports = CardPorts();
        ports.InsertAndFocusSucceeds = false;

        bool consumed = Capture(ports);

        Assert.False(consumed);
        Assert.Empty(ports.Inserted);
        Assert.Empty(ports.InsertedCombat);
        Assert.Equal(1, ports.OpenAndFocusCalls);
        Assert.Equal(0, ports.SendCalls);
    }

    [Fact]
    public void Power_capture_inserts_signed_session_bound_reference_without_sending()
    {
        LanConnectCombatRun power = new(new LanConnectPowerStateSegment(
            "MegaCrit.Strength",
            -2,
            "session-1",
            "net:owner",
            "net:applier"));
        FakeCapturePorts ports = new()
        {
            Hovered = new FakeCaptureNode("power", combat: power)
        };

        Assert.True(Capture(ports));
        Assert.Same(power, Assert.Single(ports.InsertedCombat));
        Assert.Equal(1, ports.OpenAndFocusCalls);
        Assert.Equal(0, ports.SendCalls);
    }

    [Fact]
    public void Player_capture_inserts_current_room_target_without_sending()
    {
        LanConnectCombatRun player = new(new LanConnectTargetRefSegment(
            "player",
            "net:target",
            "session-1"));
        FakeCapturePorts ports = new()
        {
            Hovered = new FakeCaptureNode("player", combat: player)
        };

        Assert.True(Capture(ports));
        Assert.Same(player, Assert.Single(ports.InsertedCombat));
        Assert.Equal(0, ports.SendCalls);
    }

    [Fact]
    public void Server_destination_shows_room_only_warning_without_consuming_or_inserting()
    {
        FakeCapturePorts ports = new()
        {
            Hovered = new FakeCaptureNode(
                "power",
                combat: new LanConnectCombatRun(new LanConnectPowerStateSegment(
                    "MegaCrit.Strength",
                    2,
                    "session-1"))),
            IsRoomChannelSelected = false
        };

        Assert.False(Capture(ports));
        Assert.Equal(1, ports.RoomOnlyWarnings);
        Assert.Empty(ports.InsertedCombat);
        Assert.Equal(0, ports.OpenAndFocusCalls);
    }

    [Fact]
    public void Disabled_combat_capability_or_failed_commit_does_not_consume()
    {
        FakeCapturePorts disabled = CombatPorts();
        disabled.CombatRefsEnabledForSelectedChannel = false;
        Assert.False(Capture(disabled));

        FakeCapturePorts staleAtCommit = CombatPorts();
        staleAtCommit.InsertCombatAndFocusSucceeds = false;
        Assert.False(Capture(staleAtCommit));

        Assert.Empty(disabled.InsertedCombat);
        Assert.Empty(staleAtCommit.InsertedCombat);
    }

    [Fact]
    public void Blocking_interaction_rejects_combat_candidate_before_resolution()
    {
        FakeCapturePorts ports = CombatPorts();
        ports.ActiveTextControl = true;

        Assert.False(Capture(ports));
        Assert.Empty(ports.ResolveAttempts);
        Assert.Empty(ports.InsertedCombat);
    }

    [Fact]
    public void Invalid_power_holder_stops_before_player_parent()
    {
        FakeCaptureNode player = new(
            "player",
            combat: new LanConnectCombatRun(new LanConnectTargetRefSegment(
                "player",
                "net:target",
                "session-1")));
        FakeCapturePorts ports = new()
        {
            Hovered = new FakeCaptureNode("power", parent: player)
        };

        Assert.False(Capture(ports));
        Assert.Empty(ports.InsertedCombat);
        Assert.DoesNotContain("player:player", ports.ResolveAttempts);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(1, 2, false)]
    public void Combat_capability_requires_exact_supported_versions(
        int richVersion,
        int combatVersion,
        bool expected)
    {
        Assert.Equal(
            expected,
            LanConnectItemLinkCapture.CombatRefsEnabled(
                new LanConnectChatFeatureVersions(richVersion, 0, 0, combatVersion)));
    }

    private static bool Capture(FakeCapturePorts ports) =>
        new LanConnectItemLinkCapture(ports).TryCapture(
            new LanConnectPointerGesture(Alt: true, Left: true, Pressed: true));

    private static FakeCapturePorts CardPorts() => new()
    {
        Hovered = new FakeCaptureNode(
            "card",
            new LanConnectItemRun("card", "MegaCrit.Strike", 2))
    };

    private static FakeCapturePorts CombatPorts() => new()
    {
        Hovered = new FakeCaptureNode(
            "power",
            combat: new LanConnectCombatRun(new LanConnectPowerStateSegment(
                "MegaCrit.Strength",
                2,
                "session-1")))
    };

    private static void AssertNotCaptured(FakeCapturePorts ports, bool consumed)
    {
        Assert.False(consumed);
        Assert.Empty(ports.Inserted);
        Assert.Equal(0, ports.OpenAndFocusCalls);
        Assert.Equal(0, ports.SendCalls);
    }

    private sealed class FakeCaptureNode(
        string kind,
        LanConnectItemRun? item = null,
        LanConnectCombatRun? combat = null,
        FakeCaptureNode? parent = null)
    {
        internal string Kind { get; } = kind;

        internal LanConnectItemRun? Item { get; } = item;

        internal LanConnectCombatRun? Combat { get; } = combat;

        internal FakeCaptureNode? Parent { get; } = parent;
    }

    private sealed class FakeCapturePorts : ILanConnectItemLinkCapturePorts
    {
        internal FakeCaptureNode? Hovered { get; init; }

        public bool ItemRefsEnabledForSelectedChannel { get; set; } = true;

        public bool CombatRefsEnabledForSelectedChannel { get; set; } = true;

        public bool IsRoomChannelSelected { get; set; } = true;

        public bool ActiveTextControl { get; set; }

        public bool PickerVisible { get; set; }

        public bool PreviewVisible { get; set; }

        public bool BlockingDialogVisible { get; set; }

        public bool CardPreviewInteraction { get; set; }

        internal bool ThrowOnResolve { get; set; }

        internal bool InsertAndFocusSucceeds { get; set; } = true;

        internal bool InsertCombatAndFocusSucceeds { get; set; } = true;

        public bool IsChatInteractionBlocking =>
            ActiveTextControl || PickerVisible || PreviewVisible ||
            BlockingDialogVisible || CardPreviewInteraction;

        internal List<LanConnectItemRun> Inserted { get; } = new();

        internal List<LanConnectCombatRun> InsertedCombat { get; } = new();

        internal List<string> ResolveAttempts { get; } = new();

        internal int OpenAndFocusCalls { get; private set; }

        internal int SendCalls { get; private set; }

        internal int RoomOnlyWarnings { get; private set; }

        public object? GuiGetHoveredControl() => Hovered;

        public object? GetParent(object node) => ((FakeCaptureNode)node).Parent;

        public bool IsCaptureBoundary(object node) =>
            CardPreviewInteraction || ((FakeCaptureNode)node).Kind == "card_preview";

        public bool IsSupportedHolder(object node) =>
            ((FakeCaptureNode)node).Kind is "card" or "relic" or "potion";

        public bool IsPowerHolder(object node) => ((FakeCaptureNode)node).Kind == "power";

        public bool IsPlayerHolder(object node) => ((FakeCaptureNode)node).Kind == "player";

        public bool TryResolveCard(object node, out LanConnectItemRun run) =>
            TryResolve("card", node, out run);

        public bool TryResolveRelic(object node, out LanConnectItemRun run) =>
            TryResolve("relic", node, out run);

        public bool TryResolvePotion(object node, out LanConnectItemRun run) =>
            TryResolve("potion", node, out run);

        public bool TryResolvePower(object node, out LanConnectCombatRun run) =>
            TryResolveCombat("power", node, out run);

        public bool TryResolvePlayerTarget(object node, out LanConnectCombatRun run) =>
            TryResolveCombat("player", node, out run);

        public bool InsertAndFocus(LanConnectItemRun run)
        {
            OpenAndFocusCalls++;
            if (!InsertAndFocusSucceeds)
            {
                return false;
            }
            Inserted.Add(run);
            return true;
        }

        public bool InsertCombatAndFocus(LanConnectCombatRun run)
        {
            OpenAndFocusCalls++;
            if (!InsertCombatAndFocusSucceeds)
            {
                return false;
            }
            InsertedCombat.Add(run);
            return true;
        }

        public void ShowCombatRoomOnlyWarning() => RoomOnlyWarnings++;

        private bool TryResolve(string kind, object node, out LanConnectItemRun run)
        {
            if (ThrowOnResolve)
            {
                throw new InvalidOperationException("resolver failed");
            }
            FakeCaptureNode target = (FakeCaptureNode)node;
            ResolveAttempts.Add($"{kind}:{target.Kind}");
            if (target.Kind == kind && target.Item != null)
            {
                run = target.Item;
                return true;
            }
            run = null!;
            return false;
        }

        private bool TryResolveCombat(
            string kind,
            object node,
            out LanConnectCombatRun run)
        {
            if (ThrowOnResolve)
            {
                throw new InvalidOperationException("resolver failed");
            }
            FakeCaptureNode target = (FakeCaptureNode)node;
            ResolveAttempts.Add($"{kind}:{target.Kind}");
            if (target.Kind == kind && target.Combat != null)
            {
                run = target.Combat;
                return true;
            }
            run = null!;
            return false;
        }
    }
}
