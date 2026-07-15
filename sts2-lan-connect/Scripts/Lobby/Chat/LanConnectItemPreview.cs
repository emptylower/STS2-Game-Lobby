using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectItemPreviewInvalidation
{
    PointerExited,
    MessageRemoved,
    TabSwitched,
    ContextCleared
}

internal readonly record struct LanConnectItemPreviewTestState(
    bool Visible,
    string ItemType,
    string Title,
    string Description,
    bool HasLocalVisual,
    int CardVisualCount,
    int ContentNodeCount,
    Rect2 Bounds,
    bool BlocksAltCapture);

internal interface ILanConnectCardPreviewVisualFactory
{
    Control Create(object card);
}

internal interface ILanConnectCardPreviewNativePort
{
    Node? CreateCard(object card);

    Control? CreateHolder(Node card);

    void ConfigureHolder(Control holder);

    void Release(Node node);
}

internal interface ILanConnectItemPreviewNodePort
{
    void Attach(Node parent, Node child);

    void Release(Node node);
}

internal sealed class LanConnectProductionCardPreviewVisualFactory : ILanConnectCardPreviewVisualFactory
{
    private readonly ILanConnectCardPreviewNativePort _native;

    internal LanConnectProductionCardPreviewVisualFactory()
        : this(new LanConnectCardPreviewNativePort())
    {
    }

    internal LanConnectProductionCardPreviewVisualFactory(ILanConnectCardPreviewNativePort native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public Control Create(object card)
    {
        Node cardNode = _native.CreateCard(card) ??
                        throw new InvalidOperationException("The local card visual is unavailable.");
        if (!GodotObject.IsInstanceValid(cardNode))
        {
            throw new InvalidOperationException("The local card visual is invalid.");
        }
        Control? holder = null;
        try
        {
            holder = _native.CreateHolder(cardNode);
            if (holder == null || !GodotObject.IsInstanceValid(holder))
            {
                throw new InvalidOperationException("The local preview card holder is unavailable.");
            }
            _native.ConfigureHolder(holder);
            return holder;
        }
        catch
        {
            Node ownedRoot = holder != null && GodotObject.IsInstanceValid(holder)
                ? holder
                : cardNode.GetParent() is { } parent && GodotObject.IsInstanceValid(parent)
                    ? parent
                    : cardNode;
            _native.Release(ownedRoot);
            throw;
        }
    }
}

internal sealed class LanConnectCardPreviewNativePort : ILanConnectCardPreviewNativePort
{
    public Node? CreateCard(object card) => NCard.Create((CardModel)card);

    public Control? CreateHolder(Node card) => NPreviewCardHolder.Create(
        (NCard)card,
        showHoverTips: false,
        scaleOnHover: false);

    public void ConfigureHolder(Control holder)
    {
        NPreviewCardHolder previewHolder = (NPreviewCardHolder)holder;
        previewHolder.SetCardScale(Vector2.One * 0.72f);
        previewHolder.CustomMinimumSize = LanConnectItemPreview.CardVisualMinimumSize;
    }

    public void Release(Node node) => LanConnectItemPreviewOwnership.Release(node);
}

internal sealed class LanConnectItemPreviewNodePort : ILanConnectItemPreviewNodePort
{
    public void Attach(Node parent, Node child) => parent.AddChild(child);

    public void Release(Node node) => LanConnectItemPreviewOwnership.Release(node);
}

internal static class LanConnectItemPreviewOwnership
{
    internal static void Release(Node node)
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }
        if (node is NPreviewCardHolder holder &&
            holder.CardNode is { } card &&
            GodotObject.IsInstanceValid(card) &&
            ReferenceEquals(card.GetParent(), holder) &&
            !holder.IsInsideTree())
        {
            holder.RemoveChild(card);
            card.QueueFreeSafely();
        }
        node.QueueFreeSafely();
    }
}

internal sealed partial class LanConnectItemPreview : PopupPanel
{
    internal const string PreviewName = "ChatItemPreview";
    internal const string SurfaceName = "ChatItemPreviewSurface";
    internal const string ContentName = "ChatItemPreviewContent";

    internal const int SurfaceMargin = 12;
    internal static readonly Vector2 CardVisualMinimumSize = new(232, 332);
    internal static readonly Vector2 CardSurfacePreferredSize =
        CardVisualMinimumSize + new Vector2(SurfaceMargin * 2, SurfaceMargin * 2);
    private static readonly Vector2 HoverTipSurfacePreferredSize = new(340, 184);
    private const float AnchorGap = 10f;

    private readonly ILanConnectCardPreviewVisualFactory _cardVisualFactory;
    private readonly ILanConnectItemPreviewNodePort _nodePort;
    private MarginContainer? _surface;
    private VBoxContainer? _content;
    private Control? _cardVisual;
    private string _itemType = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private bool _hasLocalVisual;
    private bool _closing;

    internal LanConnectItemPreview()
        : this(
            new LanConnectProductionCardPreviewVisualFactory(),
            new LanConnectItemPreviewNodePort())
    {
    }

    internal LanConnectItemPreview(ILanConnectCardPreviewVisualFactory cardVisualFactory)
        : this(cardVisualFactory, new LanConnectItemPreviewNodePort())
    {
    }

    internal LanConnectItemPreview(
        ILanConnectCardPreviewVisualFactory cardVisualFactory,
        ILanConnectItemPreviewNodePort nodePort)
    {
        _cardVisualFactory = cardVisualFactory ??
                             throw new ArgumentNullException(nameof(cardVisualFactory));
        _nodePort = nodePort ?? throw new ArgumentNullException(nameof(nodePort));
    }

    internal LanConnectItemPreviewTestState TestState
    {
        get
        {
            int cardVisualCount = _content?.GetChildren()
                .OfType<Control>()
                .Count(control =>
                    !control.IsQueuedForDeletion() &&
                    control.HasMeta("lan_connect_card_preview")) ?? 0;
            bool blocksCapture = Visible &&
                                 _surface != null &&
                                 GodotObject.IsInstanceValid(_surface) &&
                                 _surface.IsInGroup(LanConnectGodotItemLinkCapturePorts.ItemPreviewGroupName) &&
                                 _surface.IsVisibleInTree();
            return new LanConnectItemPreviewTestState(
                Visible,
                _itemType,
                _title,
                _description,
                _hasLocalVisual,
                cardVisualCount,
                _content?.GetChildren().Count(child => !child.IsQueuedForDeletion()) ?? 0,
                new Rect2(new Vector2(Position.X, Position.Y), new Vector2(Size.X, Size.Y)),
                blocksCapture);
        }
    }

    internal Vector2 CardPreferredSizeForTests => CardSurfacePreferredSize + PopupChromeSize;

    public override void _Ready()
    {
        Name = PreviewName;
        Unresizable = true;
        Exclusive = false;
        BuildSurface();
        PopupHide += OnPopupHide;
    }

    public override void _ExitTree()
    {
        ClearContent();
        ResetPresentationState();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!Visible || inputEvent is not InputEventKey
            {
                Pressed: true,
                Echo: false,
                Keycode: Key.Escape
            })
        {
            return;
        }
        ClosePreview();
        GetViewport().SetInputAsHandled();
    }

    internal void ShowResolved(
        LanConnectResolvedItem item,
        Rect2 anchor,
        Rect2 viewport)
    {
        ArgumentNullException.ThrowIfNull(item);
        bool canShow = IsInsideTree() &&
                       item.Status == LanConnectResolvedItemStatus.Resolved &&
                       item.Preview is LanConnectCardPreviewData or LanConnectHoverTipPreviewData &&
                       _content != null &&
                       GodotObject.IsInstanceValid(_content);
        if (!canShow)
        {
            ClosePreview();
            return;
        }
        if (Visible)
        {
            // Replacing an open popup must not schedule a native Hide whose delayed
            // PopupHide signal could close the replacement in the same frame.
            ClearContent();
            ResetPresentationState();
        }
        else
        {
            ClosePreview();
        }
        try
        {
            Vector2 preferredSize = item.Preview switch
            {
                LanConnectCardPreviewData card => BuildCard(card),
                LanConnectHoverTipPreviewData hoverTip => BuildHoverTip(hoverTip),
                _ => throw new InvalidOperationException("Unsupported local preview data.")
            };
            _itemType = item.ItemType;
            Rect2 bounds = ClampBounds(anchor, viewport, preferredSize);
            ConstrainContentToBounds(bounds.Size);
            Vector2 popupContentSize = new(
                Math.Max(1f, bounds.Size.X - PopupChromeSize.X),
                Math.Max(1f, bounds.Size.Y - PopupChromeSize.Y));
            Popup(new Rect2I(
                Mathf.RoundToInt(bounds.Position.X),
                Mathf.RoundToInt(bounds.Position.Y),
                Mathf.RoundToInt(popupContentSize.X),
                Mathf.RoundToInt(popupContentSize.Y)));
            ClampActualPopupToViewport(bounds.Position, viewport);
        }
        catch
        {
            ClosePreview();
        }
    }

    internal void ClosePreview()
    {
        if (_closing)
        {
            return;
        }
        _closing = true;
        try
        {
            if (Visible)
            {
                Hide();
            }
            ClearContent();
            ResetPresentationState();
        }
        finally
        {
            _closing = false;
        }
    }

    internal void Invalidate(LanConnectItemPreviewInvalidation reason)
    {
        _ = reason;
        ClosePreview();
    }

    private void BuildSurface()
    {
        _surface = new MarginContainer
        {
            Name = SurfaceName,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _surface.AddThemeConstantOverride("margin_left", SurfaceMargin);
        _surface.AddThemeConstantOverride("margin_top", SurfaceMargin);
        _surface.AddThemeConstantOverride("margin_right", SurfaceMargin);
        _surface.AddThemeConstantOverride("margin_bottom", SurfaceMargin);
        _surface.AddToGroup(LanConnectGodotItemLinkCapturePorts.ItemPreviewGroupName);
        _surface.Connect(
            Control.SignalName.MouseExited,
            Callable.From(() => Invalidate(LanConnectItemPreviewInvalidation.PointerExited)));
        AddChild(_surface);

        _content = new VBoxContainer
        {
            Name = ContentName,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _content.AddThemeConstantOverride("separation", 8);
        _surface.AddChild(_content);
    }

    private Vector2 BuildCard(LanConnectCardPreviewData preview)
    {
        Control visual = _cardVisualFactory.Create(preview.Card);
        bool attached = false;
        try
        {
            if (!GodotObject.IsInstanceValid(visual))
            {
                throw new InvalidOperationException("The local card visual is invalid.");
            }
            visual.SetMeta("lan_connect_card_preview", true);
            visual.MouseFilter = Control.MouseFilterEnum.Stop;
            visual.CustomMinimumSize = CardVisualMinimumSize;
            _nodePort.Attach(_content!, visual);
            attached = true;
            _cardVisual = visual;
            _hasLocalVisual = true;
            return CardSurfacePreferredSize + PopupChromeSize;
        }
        catch
        {
            if (!attached && GodotObject.IsInstanceValid(visual))
            {
                _nodePort.Release(visual);
            }
            throw;
        }
    }

    private Vector2 BuildHoverTip(LanConnectHoverTipPreviewData preview)
    {
        if (string.IsNullOrWhiteSpace(preview.Title) ||
            string.IsNullOrWhiteSpace(preview.Description))
        {
            throw new InvalidOperationException("The local hover-tip data is incomplete.");
        }
        HBoxContainer header = new();
        header.AddThemeConstantOverride("separation", 8);
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
            _hasLocalVisual = true;
        }
        Label title = new()
        {
            Text = preview.Title,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        header.AddChild(title);
        _content!.AddChild(header);
        Label description = new()
        {
            Text = preview.Description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _content.AddChild(description);
        _title = preview.Title;
        _description = preview.Description;
        return HoverTipSurfacePreferredSize + PopupChromeSize;
    }

    private static Rect2 ClampBounds(Rect2 anchor, Rect2 viewport, Vector2 preferredSize)
    {
        float width = Math.Min(preferredSize.X, Math.Max(1f, viewport.Size.X));
        float height = Math.Min(preferredSize.Y, Math.Max(1f, viewport.Size.Y));
        float x = anchor.End.X + AnchorGap;
        if (x + width > viewport.End.X)
        {
            x = anchor.Position.X - AnchorGap - width;
        }
        float y = anchor.Position.Y;
        float maxX = Math.Max(viewport.Position.X, viewport.End.X - width);
        float maxY = Math.Max(viewport.Position.Y, viewport.End.Y - height);
        x = Math.Clamp(x, viewport.Position.X, maxX);
        y = Math.Clamp(y, viewport.Position.Y, maxY);
        return new Rect2(new Vector2(x, y), new Vector2(width, height));
    }

    private void ConstrainContentToBounds(Vector2 popupSize)
    {
        MinSize = Vector2I.Zero;
        if (_cardVisual == null || !GodotObject.IsInstanceValid(_cardVisual))
        {
            return;
        }
        Vector2 available = new(
            Math.Max(1f, popupSize.X - PopupChromeSize.X - SurfaceMargin * 2),
            Math.Max(1f, popupSize.Y - PopupChromeSize.Y - SurfaceMargin * 2));
        _cardVisual.CustomMinimumSize = new Vector2(
            Math.Min(CardVisualMinimumSize.X, available.X),
            Math.Min(CardVisualMinimumSize.Y, available.Y));
    }

    private Vector2 PopupChromeSize =>
        GetThemeStylebox("panel")?.GetMinimumSize() ?? Vector2.Zero;

    private void ClampActualPopupToViewport(Vector2 preferredPosition, Rect2 viewport)
    {
        Vector2 actualSize = new(Size.X, Size.Y);
        float minX = Mathf.Ceil(viewport.Position.X);
        float minY = Mathf.Ceil(viewport.Position.Y);
        float maxX = Math.Max(minX, Mathf.Floor(viewport.End.X - actualSize.X));
        float maxY = Math.Max(minY, Mathf.Floor(viewport.End.Y - actualSize.Y));
        Position = new Vector2I(
            Mathf.RoundToInt(Math.Clamp(preferredPosition.X, minX, maxX)),
            Mathf.RoundToInt(Math.Clamp(preferredPosition.Y, minY, maxY)));
    }

    private void ClearContent()
    {
        if (_content == null || !GodotObject.IsInstanceValid(_content))
        {
            return;
        }
        foreach (Node child in _content.GetChildren())
        {
            if (!child.IsQueuedForDeletion())
            {
                _nodePort.Release(child);
            }
        }
    }

    private void ResetPresentationState()
    {
        _itemType = string.Empty;
        _title = string.Empty;
        _description = string.Empty;
        _hasLocalVisual = false;
        _cardVisual = null;
    }

    private void OnPopupHide()
    {
        // PopupHide can be delivered after an old Hide followed by a same-frame replacement.
        // A currently visible popup owns newer content and must not be cleared by that stale signal.
        if (_closing || Visible)
        {
            return;
        }
        ClearContent();
        ResetPresentationState();
    }

}
