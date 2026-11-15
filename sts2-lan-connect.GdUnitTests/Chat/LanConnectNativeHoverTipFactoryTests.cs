using Godot;
using GdUnit4;
using MegaCrit.Sts2.addons.mega_text;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectNativeHoverTipFactoryTests
{
    [TestCase]
    public async Task Native_card_reapplies_the_requested_model_after_the_hover_scene_enters_the_tree()
    {
        FakeNativePort port = new();
        object requested = new();
        LanConnectNativeHoverTipFactory factory = new(port, new FakeCardFallback());
        Control visual = factory.Create(requested);
        Control host = new() { Name = "NativeCardHoverTipHost" };
        host.AddChild(visual);
        using ISceneRunner runner = ISceneRunner.Load(host, autoFree: true);

        await runner.AwaitIdleFrame();

        AssertThat(port.CardConfigurationCalls).IsEqual(2);
        AssertThat(ReferenceEquals(port.LastConfiguredCard, requested)).IsTrue();
        AssertThat(port.LastConfigurationWasInsideTree).IsTrue();
    }

    [TestCase]
    public async Task Native_card_ready_failure_releases_placeholder_and_shows_closable_failure()
    {
        FakeNativePort port = new() { ThrowOnReadyConfiguration = true };
        LanConnectNativeHoverTipFactory factory = new(port, new FakeCardFallback());
        LanConnectItemPreview preview = new(factory);
        Control host = new() { Name = "NativeCardReadyFailureHost" };
        host.AddChild(preview);
        using ISceneRunner runner = ISceneRunner.Load(host, autoFree: true);
        await runner.AwaitIdleFrame();

        preview.ShowResolved(new LanConnectResolvedItem(
                LanConnectResolvedItemStatus.Resolved, "card", "chat.card", "Defend", "Defend",
                new LanConnectCardPreviewData(new object(), 0)),
            new Rect2(new Vector2(20, 20), new Vector2(20, 20)),
            new Rect2(Vector2.Zero, new Vector2(800, 600)), pinned: true);
        await runner.AwaitIdleFrame();

        AssertThat(port.CardConfigurationCalls).IsEqual(2);
        AssertThat(port.LastInstantiated != null).IsTrue();
        AssertThat(GodotObject.IsInstanceValid(port.LastInstantiated)).IsFalse();
        AssertThat(preview.TestState.Visible).IsTrue();
        AssertThat(preview.TestState.Unavailable).IsTrue();
        AssertThat(preview.TestState.CardVisualCount).IsEqual(0);
        AssertThat(preview.TestState.HasLocalVisual).IsFalse();
        AssertThat(preview.TestState.ContentNodeCount).IsEqual(1);
        AssertThat(preview.TestState.Description.Contains("ready configuration failed", StringComparison.Ordinal)).IsFalse();
        Button close = preview.FindChild(LanConnectReferencePreviewController.CloseButtonName,
            recursive: true, owned: false) as Button ?? throw new InvalidOperationException("Close button missing.");
        AssertThat(close.IsVisibleInTree()).IsTrue();
        close.EmitSignal(Button.SignalName.Pressed);
        AssertThat(preview.TestState.Visible).IsFalse();
        AssertThat(preview.TestState.Unavailable).IsFalse();
    }

    [TestCase]
    public void Native_hover_tip_keeps_complete_rich_description_and_debuff_style()
    {
        FakeNativePort port = new();
        LanConnectNativeHoverTipFactory factory = new(port);
        string description = string.Join('\n', Enumerable.Range(1, 12).Select(index => $"line {index}"));

        Control visual = AutoFree(factory.CreateHoverTip(new LanConnectHoverTipPreviewData(
            "power",
            "Catalyst",
            description,
            port.Icon,
            IsDebuff: true)))!;

        AssertThat(port.SceneRequests).Contains("res://scenes/ui/hover_tip.tscn");
        AssertThat(visual.FindChild("Title", true, false) is MegaLabel title &&
                   title.Text == "Catalyst").IsTrue();
        MegaRichTextLabel rich = visual.FindChildren("*", string.Empty, true, false)
            .OfType<MegaRichTextLabel>()
            .Single();
        AssertThat(rich.Text).IsEqual(description);
        AssertThat(rich.FitContent).IsTrue();
        AssertThat(port.DebuffMaterialRequests).IsEqual(1);
        AssertThat(visual.GetMeta("lan_connect_native_preview").AsBool()).IsTrue();
    }

    [TestCase]
    public void Missing_native_scene_falls_back_to_complete_mega_rich_text_without_line_cap()
    {
        FakeNativePort port = new() { ReturnNullScene = true };
        LanConnectNativeHoverTipFactory factory = new(port);
        string description = string.Concat(Enumerable.Repeat("完整说明 mixed description ", 40));

        Control visual = AutoFree(factory.CreateHoverTip(new LanConnectHoverTipPreviewData(
            "relic",
            "Anchor",
            description,
            null)))!;

        MegaRichTextLabel rich = visual.FindChildren("*", string.Empty, true, false)
            .OfType<MegaRichTextLabel>()
            .Single();
        AssertThat(rich.Text).IsEqual(description);
        AssertThat(rich.FitContent).IsTrue();
        AssertThat(visual.GetMeta("lan_connect_native_preview").AsBool()).IsFalse();
        AssertThat(visual.FindChildren("*", "Label", true, false)
            .OfType<Label>()
            .All(label => label.MaxLinesVisible <= 0)).IsTrue();
    }

    private sealed class FakeNativePort : ILanConnectNativeHoverTipPort
    {
        internal List<string> SceneRequests { get; } = [];
        internal int DebuffMaterialRequests { get; private set; }
        internal int CardConfigurationCalls { get; private set; }
        internal object? LastConfiguredCard { get; private set; }
        internal bool LastConfigurationWasInsideTree { get; private set; }
        internal bool ReturnNullScene { get; init; }
        internal bool ThrowOnReadyConfiguration { get; init; }
        internal Control? LastInstantiated { get; private set; }
        internal ImageTexture Icon { get; } = CreateIcon();

        public Control? Instantiate(string scenePath)
        {
            SceneRequests.Add(scenePath);
            if (ReturnNullScene)
            {
                return null;
            }

            Control root = new();
            MegaLabel title = new() { Name = "Title", UniqueNameInOwner = true };
            title.AddThemeFontOverride("font", ThemeDB.FallbackFont);
            root.AddChild(title);
            MegaRichTextLabel description = new()
            {
                Name = "Description",
                UniqueNameInOwner = true
            };
            description.AddThemeFontOverride("normal_font", ThemeDB.FallbackFont);
            root.AddChild(description);
            root.AddChild(new TextureRect { Name = "Icon", UniqueNameInOwner = true });
            root.AddChild(new ColorRect { Name = "Bg", UniqueNameInOwner = true });
            LastInstantiated = root;
            return root;
        }

        public Material? LoadDebuffMaterial()
        {
            DebuffMaterialRequests++;
            return new CanvasItemMaterial();
        }

        public void ConfigureCard(Control preview, object card)
        {
            CardConfigurationCalls++;
            if (ThrowOnReadyConfiguration && preview.IsInsideTree())
            {
                throw new InvalidOperationException("ready configuration failed");
            }
            LastConfiguredCard = card;
            LastConfigurationWasInsideTree = preview.IsInsideTree();
        }

        private static ImageTexture CreateIcon()
        {
            Image image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
            image.Fill(Colors.White);
            return ImageTexture.CreateFromImage(image);
        }
    }

    private sealed class FakeCardFallback : ILanConnectCardPreviewVisualFactory
    {
        public Control Create(object card)
        {
            _ = card;
            return new Control { Name = "CardFallback" };
        }
    }
}
