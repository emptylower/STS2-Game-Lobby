using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace Sts2LanConnect.Scripts;

internal interface ILanConnectNativeHoverTipPort
{
    Control? Instantiate(string scenePath);
    Material? LoadDebuffMaterial();
    void ConfigureCard(Control preview, object card) { }
}

internal interface ILanConnectReferencePreviewVisualFactory : ILanConnectCardPreviewVisualFactory
{
    Control CreateHoverTip(LanConnectHoverTipPreviewData preview);
}

internal sealed class LanConnectProductionNativeHoverTipPort : ILanConnectNativeHoverTipPort
{
    public Control? Instantiate(string scenePath) =>
        PreloadManager.Cache.GetScene(scenePath).Instantiate<Control>();

    public Material? LoadDebuffMaterial() =>
        PreloadManager.Cache.GetMaterial("res://materials/ui/hover_tip_debuff.tres");

    public void ConfigureCard(Control preview, object card)
    {
        NCard cardNode = preview.GetNodeOrNull<NCard>("%Card") ??
                         preview.FindChild("Card", recursive: true, owned: false) as NCard ??
                         throw new MissingMemberException(preview.GetType().FullName, "Card");
        cardNode.Model = (CardModel)card;
        cardNode.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
    }
}

internal sealed class LanConnectNativeHoverTipFactory : ILanConnectReferencePreviewVisualFactory
{
    internal const string CardScenePath = "res://scenes/ui/card_hover_tip.tscn";
    internal const string HoverTipScenePath = "res://scenes/ui/hover_tip.tscn";

    private readonly ILanConnectNativeHoverTipPort _port;
    private readonly ILanConnectCardPreviewVisualFactory _cardFallback;

    internal LanConnectNativeHoverTipFactory()
        : this(
            new LanConnectProductionNativeHoverTipPort(),
            new LanConnectProductionCardPreviewVisualFactory())
    {
    }

    internal LanConnectNativeHoverTipFactory(
        ILanConnectNativeHoverTipPort port,
        ILanConnectCardPreviewVisualFactory? cardFallback = null)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
        _cardFallback = cardFallback ?? new LanConnectProductionCardPreviewVisualFactory();
    }

    public Control Create(object card)
    {
        Control? native = null;
        try
        {
            native = _port.Instantiate(CardScenePath) ??
                     throw new InvalidOperationException("The native card hover tip scene is unavailable.");
            _port.ConfigureCard(native, card);
            Control requestedPreview = native;
            requestedPreview.Ready += () =>
            {
                if (!GodotObject.IsInstanceValid(requestedPreview))
                {
                    return;
                }
                try
                {
                    // The native scene assigns its Broken Card placeholder in _Ready.
                    // Reapply the resolved local model after that lifecycle step.
                    _port.ConfigureCard(requestedPreview, card);
                }
                catch
                {
                    // The initial configuration already validated the scene and model.
                    // A disappearing preview should not escape the UI callback.
                }
            };
            ConfigureVisual(native, nativePreview: true);
            return native;
        }
        catch
        {
            ReleaseTemporary(native);
            Control fallback = _cardFallback.Create(card);
            ConfigureVisual(fallback, nativePreview: false);
            return fallback;
        }
    }

    public Control CreateHoverTip(LanConnectHoverTipPreviewData preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        Control? native = null;
        try
        {
            native = _port.Instantiate(HoverTipScenePath) ??
                     throw new InvalidOperationException("The native hover tip scene is unavailable.");
            MegaLabel title = FindRequired<MegaLabel>(native, "Title");
            MegaRichTextLabel description = FindRequired<MegaRichTextLabel>(native, "Description");
            TextureRect icon = FindRequired<TextureRect>(native, "Icon");
            title.SetTextAutoSize(preview.Title);
            description.Text = preview.Description;
            description.FitContent = true;
            description.ScrollActive = false;
            icon.Texture = preview.Visual as Texture2D;
            if (preview.IsDebuff)
            {
                FindRequired<CanvasItem>(native, "Bg").Material = _port.LoadDebuffMaterial();
            }
            ConfigureVisual(native, nativePreview: true);
            native.ResetSize();
            return native;
        }
        catch
        {
            ReleaseTemporary(native);
            return BuildCompleteFallback(preview);
        }
    }

    private static Control BuildCompleteFallback(LanConnectHoverTipPreviewData preview)
    {
        PanelContainer panel = new()
        {
            Name = "ChatReferencePreviewFallback",
            CustomMinimumSize = new Vector2(300, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        VBoxContainer body = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        body.AddThemeConstantOverride("separation", 8);
        HBoxContainer header = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        if (preview.Visual is Texture2D texture)
        {
            header.AddChild(new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(40, 40),
                MouseFilter = Control.MouseFilterEnum.Ignore
            });
        }
        MegaLabel title = new()
        {
            Name = "Title",
            Text = preview.Title,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        title.AddThemeFontOverride("font", ThemeDB.FallbackFont);
        header.AddChild(title);
        body.AddChild(header);
        MegaRichTextLabel description = new()
        {
            Name = "Description",
            Text = preview.Description,
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        description.AddThemeFontOverride("normal_font", ThemeDB.FallbackFont);
        body.AddChild(description);
        panel.AddChild(body);
        ConfigureVisual(panel, nativePreview: false);
        return panel;
    }

    private static T FindRequired<T>(Node root, string name) where T : Node =>
        root.GetNodeOrNull<T>($"%{name}") ??
        root.FindChild(name, recursive: true, owned: false) as T ??
        throw new MissingMemberException(root.GetType().FullName, name);

    private static void ConfigureVisual(Node root, bool nativePreview)
    {
        if (root is Control control)
        {
            control.SetMeta("lan_connect_native_preview", nativePreview);
        }
        SetMouseFilterRecursive(root, Control.MouseFilterEnum.Ignore);
    }

    private static void SetMouseFilterRecursive(Node node, Control.MouseFilterEnum value)
    {
        if (node is Control control)
        {
            control.MouseFilter = value;
        }
        foreach (Node child in node.GetChildren())
        {
            SetMouseFilterRecursive(child, value);
        }
    }

    private static void ReleaseTemporary(Node? node)
    {
        if (node != null && GodotObject.IsInstanceValid(node))
        {
            LanConnectItemPreviewOwnership.Release(node);
        }
    }
}
