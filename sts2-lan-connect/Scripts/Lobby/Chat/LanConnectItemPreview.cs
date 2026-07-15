using Godot;
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

internal sealed class LanConnectProductionCardPreviewVisualFactory : ILanConnectCardPreviewVisualFactory
{
    public Control Create(object card)
    {
        NCard cardNode = NCard.Create((CardModel)card) ??
                         throw new InvalidOperationException("The local card visual is unavailable.");
        NPreviewCardHolder holder = NPreviewCardHolder.Create(
            cardNode,
            showHoverTips: false,
            scaleOnHover: false) ?? throw new InvalidOperationException(
                "The local preview card holder is unavailable.");
        holder.SetCardScale(Vector2.One * 0.72f);
        holder.CustomMinimumSize = new Vector2(232, 332);
        return holder;
    }
}

internal sealed partial class LanConnectItemPreview : PopupPanel
{
    internal const string PreviewName = "ChatItemPreview";
    internal const string SurfaceName = "ChatItemPreviewSurface";
    internal const string ContentName = "ChatItemPreviewContent";

    private static readonly Vector2 CardPreferredSize = new(252, 352);
    private static readonly Vector2 HoverTipPreferredSize = new(340, 184);
    private const float AnchorGap = 10f;

    private readonly ILanConnectCardPreviewVisualFactory _cardVisualFactory;
    private MarginContainer? _surface;
    private VBoxContainer? _content;
    private string _itemType = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private bool _hasLocalVisual;
    private bool _closing;

    internal LanConnectItemPreview()
        : this(new LanConnectProductionCardPreviewVisualFactory())
    {
    }

    internal LanConnectItemPreview(ILanConnectCardPreviewVisualFactory cardVisualFactory)
    {
        _cardVisualFactory = cardVisualFactory ??
                             throw new ArgumentNullException(nameof(cardVisualFactory));
    }

    internal LanConnectItemPreviewTestState TestState
    {
        get
        {
            int cardVisualCount = _content?.GetChildren()
                .OfType<Control>()
                .Count(control => control.HasMeta("lan_connect_card_preview")) ?? 0;
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
                _content?.GetChildCount() ?? 0,
                new Rect2(new Vector2(Position.X, Position.Y), new Vector2(Size.X, Size.Y)),
                blocksCapture);
        }
    }

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
        if (!IsInsideTree() ||
            item.Status != LanConnectResolvedItemStatus.Resolved ||
            item.Preview == null ||
            _content == null ||
            !GodotObject.IsInstanceValid(_content))
        {
            return;
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
            Popup(new Rect2I(
                Mathf.RoundToInt(bounds.Position.X),
                Mathf.RoundToInt(bounds.Position.Y),
                Mathf.RoundToInt(bounds.Size.X),
                Mathf.RoundToInt(bounds.Size.Y)));
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
        _surface.AddThemeConstantOverride("margin_left", 12);
        _surface.AddThemeConstantOverride("margin_top", 12);
        _surface.AddThemeConstantOverride("margin_right", 12);
        _surface.AddThemeConstantOverride("margin_bottom", 12);
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
        if (!GodotObject.IsInstanceValid(visual))
        {
            throw new InvalidOperationException("The local card visual is invalid.");
        }
        visual.SetMeta("lan_connect_card_preview", true);
        visual.MouseFilter = Control.MouseFilterEnum.Stop;
        _content!.AddChild(visual);
        _hasLocalVisual = true;
        return CardPreferredSize;
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
        return HoverTipPreferredSize;
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

    private void ClearContent()
    {
        if (_content == null || !GodotObject.IsInstanceValid(_content))
        {
            return;
        }
        foreach (Node child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.Free();
        }
    }

    private void ResetPresentationState()
    {
        _itemType = string.Empty;
        _title = string.Empty;
        _description = string.Empty;
        _hasLocalVisual = false;
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
