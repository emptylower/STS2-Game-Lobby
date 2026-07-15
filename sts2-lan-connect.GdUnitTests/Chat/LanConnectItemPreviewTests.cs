using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectItemPreviewTests
{
    [TestCase]
    public async Task Resolved_card_creates_one_local_visual_and_clamps_wholly_inside_viewport()
    {
        FakeCardVisualFactory factory = new();
        using PreviewFixture fixture = await PreviewFixture.Create(factory);
        Rect2 viewport = new(new Vector2(50, 40), new Vector2(640, 420));
        LanConnectResolvedItem item = ResolvedCard(upgradeLevel: 2);

        fixture.Preview.ShowResolved(
            item,
            new Rect2(new Vector2(650, 390), new Vector2(36, 24)),
            viewport);
        await fixture.Runner.AwaitIdleFrame();

        LanConnectItemPreviewTestState state = fixture.Preview.TestState;
        AssertThat(state.Visible).IsTrue();
        AssertThat(state.ItemType).IsEqual("card");
        AssertThat(state.CardVisualCount).IsEqual(1);
        AssertThat(state.ContentNodeCount).IsEqual(1);
        AssertThat(factory.Calls).IsEqual(1);
        AssertThat(factory.LastCard is FakeCardModel).IsTrue();
        AssertThat(state.Bounds.Position.X >= viewport.Position.X).IsTrue();
        AssertThat(state.Bounds.Position.Y >= viewport.Position.Y).IsTrue();
        AssertThat(state.Bounds.End.X <= viewport.End.X).IsTrue();
        AssertThat(state.Bounds.End.Y <= viewport.End.Y).IsTrue();
        AssertThat(state.Bounds.Size == fixture.Preview.CardPreferredSizeForTests).IsTrue();
        Control surface = fixture.Preview.GetNode<Control>(LanConnectItemPreview.SurfaceName);
        AssertThat(surface.GetThemeConstant("margin_left")).IsEqual(12);
        AssertThat(surface.GetThemeConstant("margin_top")).IsEqual(12);
        AssertThat(surface.GetThemeConstant("margin_right")).IsEqual(12);
        AssertThat(surface.GetThemeConstant("margin_bottom")).IsEqual(12);
        AssertThat(state.BlocksAltCapture).IsTrue();
        AssertThat(LanConnectGodotItemLinkCapturePorts.HasVisibleItemPreview(
            fixture.Preview.GetTree())).IsTrue();
        AssertDescendantsInsidePopup(fixture.Preview);
    }

    [TestCase(264, 364)]
    [TestCase(220, 300)]
    public async Task Exact_or_smaller_nonzero_viewport_keeps_popup_and_descendants_inside(
        int viewportWidth,
        int viewportHeight)
    {
        using PreviewFixture fixture = await PreviewFixture.Create(new FakeCardVisualFactory());
        Rect2 viewport = new(new Vector2(37, 29), new Vector2(viewportWidth, viewportHeight));

        fixture.Preview.ShowResolved(
            ResolvedCard(upgradeLevel: 1),
            new Rect2(viewport.End - new Vector2(8, 8), new Vector2(8, 8)),
            viewport);
        await fixture.Runner.AwaitIdleFrame();

        Rect2 bounds = fixture.Preview.TestState.Bounds;
        AssertThat(bounds.Position.X >= viewport.Position.X).IsTrue();
        AssertThat(bounds.Position.Y >= viewport.Position.Y).IsTrue();
        AssertThat(bounds.End.X <= viewport.End.X).IsTrue();
        AssertThat(bounds.End.Y <= viewport.End.Y).IsTrue();
        AssertThat(bounds.Size.X <= viewport.Size.X).IsTrue();
        AssertThat(bounds.Size.Y <= viewport.Size.Y).IsTrue();
        AssertDescendantsInsidePopup(fixture.Preview);
    }

    [TestCase("relic")]
    [TestCase("potion")]
    public async Task Relic_and_potion_render_local_hovertip_style_data(string itemType)
    {
        using PreviewFixture fixture = await PreviewFixture.Create(new FakeCardVisualFactory());
        ImageTexture visual = PreviewFixture.Icon();
        LanConnectResolvedItem item = new(
            LanConnectResolvedItemStatus.Resolved,
            itemType,
            "chat." + itemType,
            "Local title",
            "Local title",
            new LanConnectHoverTipPreviewData(
                itemType,
                "Local title",
                "Local description",
                visual));

        fixture.Preview.ShowResolved(
            item,
            new Rect2(new Vector2(40, 40), new Vector2(20, 20)),
            new Rect2(Vector2.Zero, new Vector2(800, 600)));
        await fixture.Runner.AwaitIdleFrame();

        LanConnectItemPreviewTestState state = fixture.Preview.TestState;
        AssertThat(state.Visible).IsTrue();
        AssertThat(state.ItemType).IsEqual(itemType);
        AssertThat(state.Title).IsEqual("Local title");
        AssertThat(state.Description).IsEqual("Local description");
        AssertThat(state.HasLocalVisual).IsTrue();
        AssertThat(state.CardVisualCount).IsEqual(0);
        AssertThat(state.BlocksAltCapture).IsTrue();
    }

    [TestCase]
    public async Task Unknown_item_has_no_preview_and_never_exposes_model_identifier()
    {
        using PreviewFixture fixture = await PreviewFixture.Create(new FakeCardVisualFactory());
        LanConnectResolvedItem unknown = new(
            LanConnectResolvedItemStatus.Unknown,
            "relic",
            "chat.unknown_relic",
            LocalizedTitle: null,
            AccessibleText: "chat.unknown_relic",
            Preview: null);

        fixture.Preview.ShowResolved(
            unknown,
            new Rect2(Vector2.Zero, new Vector2(20, 20)),
            new Rect2(Vector2.Zero, new Vector2(800, 600)));
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Preview.TestState.Visible).IsFalse();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(0);
        AssertThat(unknown.AccessibleText.Contains("PrivateMod", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase]
    public async Task Unknown_replacement_closes_resolved_popup_releases_content_and_capture_guard()
    {
        using PreviewFixture fixture = await PreviewFixture.Create(new FakeCardVisualFactory());
        fixture.ShowCard();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Preview.TestState.Visible).IsTrue();
        AssertThat(LanConnectGodotItemLinkCapturePorts.HasVisibleItemPreview(
            fixture.Preview.GetTree())).IsTrue();

        fixture.Preview.ShowResolved(
            UnknownRelic(),
            new Rect2(Vector2.Zero, new Vector2(20, 20)),
            new Rect2(Vector2.Zero, new Vector2(800, 600)));
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Preview.TestState.Visible).IsFalse();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(0);
        AssertThat(fixture.Preview.TestState.BlocksAltCapture).IsFalse();
        AssertThat(LanConnectGodotItemLinkCapturePorts.HasVisibleItemPreview(
            fixture.Preview.GetTree())).IsFalse();
    }

    [TestCase]
    public async Task Card_visual_exception_closes_preview_and_leaves_no_orphan_content()
    {
        FakeCardVisualFactory factory = new() { Throw = true };
        using PreviewFixture fixture = await PreviewFixture.Create(factory);

        fixture.Preview.ShowResolved(
            ResolvedCard(upgradeLevel: 1),
            new Rect2(Vector2.Zero, new Vector2(20, 20)),
            new Rect2(Vector2.Zero, new Vector2(800, 600)));
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Preview.TestState.Visible).IsFalse();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(0);
        AssertThat(fixture.Preview.TestState.BlocksAltCapture).IsFalse();
    }

    [TestCase("holder_null")]
    [TestCase("holder_throw_after_ownership")]
    [TestCase("configure_throw")]
    public void Production_factory_releases_allocated_native_tree_on_every_post_allocation_failure(
        string failureStage)
    {
        FakeCardPreviewNativePort native = new(failureStage);
        LanConnectProductionCardPreviewVisualFactory factory = new(native);
        bool threw = false;

        try
        {
            factory.Create(new FakeCardModel());
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        AssertThat(threw).IsTrue();
        AssertThat(native.ReleaseCalls).IsEqual(1);
        AssertThat(native.ReleasedNode != null).IsTrue();
        AssertThat(GodotObject.IsInstanceValid(native.ReleasedNode)).IsFalse();
        AssertThat(GodotObject.IsInstanceValid(native.Card)).IsFalse();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Presenter_releases_factory_visual_once_when_attach_throws(
        bool throwAfterAttach)
    {
        FakeCardVisualFactory factory = new();
        FakePreviewNodePort nodePort = new()
        {
            ThrowOnAttach = true,
            ThrowAfterAttach = throwAfterAttach
        };
        using PreviewFixture fixture = await PreviewFixture.Create(factory, nodePort);

        fixture.ShowCard();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(nodePort.AttachCalls).IsEqual(1);
        AssertThat(nodePort.ReleaseCalls).IsEqual(1);
        AssertThat(factory.LastVisual != null).IsTrue();
        AssertThat(GodotObject.IsInstanceValid(factory.LastVisual)).IsFalse();
        AssertThat(fixture.Preview.TestState.Visible).IsFalse();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(0);
        AssertThat(LanConnectGodotItemLinkCapturePorts.HasVisibleItemPreview(
            fixture.Preview.GetTree())).IsFalse();
    }

    [TestCase]
    public async Task Real_viewport_escape_closes_preview_and_marks_no_preview_visible()
    {
        using PreviewFixture fixture = await PreviewFixture.Create(new FakeCardVisualFactory());
        fixture.ShowCard();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Preview.TestState.Visible).IsTrue();

        fixture.Preview.GetViewport().PushInput(new InputEventKey
        {
            Keycode = Key.Escape,
            Pressed = true,
            Echo = false
        });
        await fixture.Runner.AwaitInputProcessed();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Preview.TestState.Visible).IsFalse();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(0);
    }

    [TestCase(nameof(LanConnectItemPreviewInvalidation.MessageRemoved))]
    [TestCase(nameof(LanConnectItemPreviewInvalidation.TabSwitched))]
    [TestCase(nameof(LanConnectItemPreviewInvalidation.ContextCleared))]
    public async Task All_owner_invalidation_reasons_close_and_clear(string reasonName)
    {
        using PreviewFixture fixture = await PreviewFixture.Create(new FakeCardVisualFactory());
        fixture.ShowCard();
        await fixture.Runner.AwaitIdleFrame();

        fixture.Preview.Invalidate(Enum.Parse<LanConnectItemPreviewInvalidation>(reasonName));
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Preview.TestState.Visible).IsFalse();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(0);
        AssertThat(fixture.Preview.TestState.BlocksAltCapture).IsFalse();
    }

    [TestCase]
    public async Task Real_pointer_exit_signal_closes_preview_and_releases_capture_guard()
    {
        using PreviewFixture fixture = await PreviewFixture.Create(new FakeCardVisualFactory());
        fixture.ShowCard();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(LanConnectGodotItemLinkCapturePorts.HasVisibleItemPreview(
            fixture.Preview.GetTree())).IsTrue();

        Control surface = fixture.Preview.GetNode<Control>(LanConnectItemPreview.SurfaceName);
        surface.EmitSignal(Control.SignalName.MouseExited);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Preview.TestState.Visible).IsFalse();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(0);
        AssertThat(LanConnectGodotItemLinkCapturePorts.HasVisibleItemPreview(
            fixture.Preview.GetTree())).IsFalse();
    }

    [TestCase]
    public async Task Replacing_preview_keeps_exactly_one_visual_and_close_is_idempotent()
    {
        FakeCardVisualFactory factory = new();
        using PreviewFixture fixture = await PreviewFixture.Create(factory);
        fixture.ShowCard();
        fixture.ShowCard();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Preview.TestState.CardVisualCount).IsEqual(1);
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(1);
        fixture.Preview.ClosePreview();
        fixture.Preview.ClosePreview();

        AssertThat(fixture.Preview.TestState.Visible).IsFalse();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(0);
    }

    [TestCase]
    public async Task External_popup_hide_releases_local_visual_and_model_content()
    {
        using PreviewFixture fixture = await PreviewFixture.Create(new FakeCardVisualFactory());
        fixture.ShowCard();
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(1);

        fixture.Preview.Hide();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Preview.TestState.Visible).IsFalse();
        AssertThat(fixture.Preview.TestState.ContentNodeCount).IsEqual(0);
        AssertThat(fixture.Preview.TestState.ItemType).IsEmpty();
        AssertThat(fixture.Preview.TestState.BlocksAltCapture).IsFalse();
        AssertThat(LanConnectGodotItemLinkCapturePorts.HasVisibleItemPreview(
            fixture.Preview.GetTree())).IsFalse();
    }

    private static LanConnectResolvedItem ResolvedCard(int upgradeLevel) => new(
        LanConnectResolvedItemStatus.Resolved,
        "card",
        "chat.card",
        "Strike+" + upgradeLevel,
        "Strike+" + upgradeLevel,
        new LanConnectCardPreviewData(new FakeCardModel(), upgradeLevel));

    private static LanConnectResolvedItem UnknownRelic() => new(
        LanConnectResolvedItemStatus.Unknown,
        "relic",
        "chat.unknown_relic",
        LocalizedTitle: null,
        AccessibleText: "chat.unknown_relic",
        Preview: null);

    private static void AssertDescendantsInsidePopup(LanConnectItemPreview preview)
    {
        Rect2 localPopup = new(Vector2.Zero, new Vector2(preview.Size.X, preview.Size.Y));
        Control[] descendants = preview.FindChildren(
                "*",
                "Control",
                recursive: true,
                owned: false)
            .OfType<Control>()
            .Where(control => control.Visible && !control.IsQueuedForDeletion())
            .ToArray();
        AssertThat(descendants.Length > 0).IsTrue();
        foreach (Control control in descendants)
        {
            Rect2 rect = control.GetGlobalRect();
            AssertThat(rect.Position.X >= localPopup.Position.X).IsTrue();
            AssertThat(rect.Position.Y >= localPopup.Position.Y).IsTrue();
            AssertThat(rect.End.X <= localPopup.End.X).IsTrue();
            AssertThat(rect.End.Y <= localPopup.End.Y).IsTrue();
        }
    }

    private sealed class FakeCardModel;

    private sealed class FakeCardVisualFactory : ILanConnectCardPreviewVisualFactory
    {
        internal int Calls { get; private set; }

        internal object? LastCard { get; private set; }

        internal Control? LastVisual { get; private set; }

        internal bool Throw { get; init; }

        public Control Create(object card)
        {
            Calls++;
            LastCard = card;
            if (Throw)
            {
                throw new InvalidOperationException("visual failed");
            }
            LastVisual = new ColorRect
            {
                Name = "FakeLocalCardVisual",
                Color = Colors.CornflowerBlue,
                CustomMinimumSize = new Vector2(220, 320)
            };
            return LastVisual;
        }
    }

    private sealed class FakeCardPreviewNativePort : ILanConnectCardPreviewNativePort
    {
        private readonly string _failureStage;

        internal FakeCardPreviewNativePort(string failureStage)
        {
            _failureStage = failureStage;
        }

        internal Node? Card { get; private set; }

        internal Node? ReleasedNode { get; private set; }

        internal int ReleaseCalls { get; private set; }

        public Node? CreateCard(object card)
        {
            _ = card;
            Card = new Control { Name = "AllocatedNativeCard" };
            return Card;
        }

        public Control? CreateHolder(Node card)
        {
            if (_failureStage == "holder_null")
            {
                return null;
            }
            Control holder = new() { Name = "AllocatedNativeHolder" };
            holder.AddChild(card);
            if (_failureStage == "holder_throw_after_ownership")
            {
                throw new InvalidOperationException("holder creation failed");
            }
            return holder;
        }

        public void ConfigureHolder(Control holder)
        {
            _ = holder;
            if (_failureStage == "configure_throw")
            {
                throw new InvalidOperationException("holder configuration failed");
            }
        }

        public void Release(Node node)
        {
            ReleaseCalls++;
            ReleasedNode = node;
            node.Free();
        }
    }

    private sealed class FakePreviewNodePort : ILanConnectItemPreviewNodePort
    {
        internal bool ThrowOnAttach { get; init; }

        internal bool ThrowAfterAttach { get; init; }

        internal int AttachCalls { get; private set; }

        internal int ReleaseCalls { get; private set; }

        public void Attach(Node parent, Node child)
        {
            AttachCalls++;
            if (ThrowOnAttach)
            {
                if (ThrowAfterAttach)
                {
                    parent.AddChild(child);
                }
                throw new InvalidOperationException("attach failed");
            }
            parent.AddChild(child);
        }

        public void Release(Node node)
        {
            ReleaseCalls++;
            node.Free();
        }
    }

    private sealed class PreviewFixture : IDisposable
    {
        private PreviewFixture(
            LanConnectItemPreview preview,
            ISceneRunner runner)
        {
            Preview = preview;
            Runner = runner;
        }

        internal LanConnectItemPreview Preview { get; }

        internal ISceneRunner Runner { get; }

        internal static Task<PreviewFixture> Create(ILanConnectCardPreviewVisualFactory factory) =>
            Create(factory, new LanConnectItemPreviewNodePort());

        internal static async Task<PreviewFixture> Create(
            ILanConnectCardPreviewVisualFactory factory,
            ILanConnectItemPreviewNodePort nodePort)
        {
            LanConnectItemPreview preview = new(factory, nodePort);
            Control host = new() { Name = "ItemPreviewTestHost" };
            host.AddChild(preview);
            ISceneRunner runner = ISceneRunner.Load(host, autoFree: true);
            await runner.AwaitIdleFrame();
            return new PreviewFixture(preview, runner);
        }

        internal void ShowCard() => Preview.ShowResolved(
            ResolvedCard(upgradeLevel: 1),
            new Rect2(new Vector2(100, 100), new Vector2(20, 20)),
            new Rect2(Vector2.Zero, new Vector2(800, 600)));

        internal static ImageTexture Icon()
        {
            Image image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            image.Fill(Colors.White);
            return ImageTexture.CreateFromImage(image);
        }

        public void Dispose() => Runner.Dispose();
    }
}
