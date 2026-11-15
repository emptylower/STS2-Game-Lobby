using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

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
    bool BlocksAltCapture,
    bool Pinned = false,
    bool UsesNativeScene = false,
    bool Unavailable = false);

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

internal interface ILanConnectPreviewHotkeyScope
{
    void Acquire(Node owner);
    void Release(Node owner);
}

internal sealed class LanConnectPreviewHotkeyScope : ILanConnectPreviewHotkeyScope
{
    private NHotkeyManager? _manager;
    private Node? _owner;

    public void Acquire(Node owner)
    {
        if (_owner != null)
        {
            return;
        }
        NHotkeyManager? manager = NHotkeyManager.Instance;
        if (manager == null)
        {
            return;
        }
        manager.AddBlockingScreen(owner);
        _manager = manager;
        _owner = owner;
    }

    public void Release(Node owner)
    {
        if (!ReferenceEquals(_owner, owner))
        {
            return;
        }
        _manager?.RemoveBlockingScreen(owner);
        _manager = null;
        _owner = null;
    }
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
        previewHolder.CustomMinimumSize = LanConnectReferencePreviewController.CardVisualMinimumSize;
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
        if (node.IsInsideTree())
        {
            node.QueueFreeSafely();
        }
        else
        {
            node.Free();
        }
    }
}

internal partial class LanConnectReferencePreviewController : PanelContainer
{
    internal const string PreviewName = "ChatItemPreview";
    internal const string SurfaceName = "ChatItemPreviewSurface";
    internal const string ContentName = "ChatItemPreviewContent";
    internal const string CloseButtonName = "ChatItemPreviewClose";
    internal const string HoverRootName = "ChatItemPreviewHoverRoot";
    internal const string HoverTitleName = "Title";
    internal const string HoverDescriptionName = "Description";

    internal const int SurfaceMargin = 12;
    internal static readonly Vector2 CardVisualMinimumSize = new(232, 332);
    internal static readonly Vector2 CardSurfacePreferredSize =
        CardVisualMinimumSize + new Vector2(SurfaceMargin * 2, SurfaceMargin * 2);
    private static readonly Vector2 HoverTipPreferredSize = new(380, 280);
    private const float AnchorGap = 10f;

    private readonly ILanConnectReferencePreviewVisualFactory _visualFactory;
    private readonly ILanConnectItemPreviewNodePort _nodePort;
    private readonly ILanConnectPreviewHotkeyScope _hotkeyScope;
    private readonly LanConnectReferenceTouchMouseDeduplicator _touchMouseDeduplicator = new();
    private MarginContainer? _surface;
    private ScrollContainer? _scroll;
    private VBoxContainer? _content;
    private Button? _closeButton;
    private Control? _visual;
    private string _itemType = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private bool _hasLocalVisual;
    private bool _pinned;
    private bool _closing;
    private bool _consumeCloseShortcutRelease;
    private bool _hotkeyScopeActive;
    private int _hotkeyScopeGeneration;
    private Task _hotkeyScopeReleaseTask = Task.CompletedTask;
    private string _closeAccessibilityLabel = "Close reference preview";
    private Rect2 _viewport;
    private Rect2 _bounds;
    private int _referenceId;
    private bool _unavailable;

    internal LanConnectReferencePreviewController()
        : this(
            new LanConnectNativeHoverTipFactory(),
            new LanConnectItemPreviewNodePort(),
            new LanConnectPreviewHotkeyScope())
    {
    }

    internal LanConnectReferencePreviewController(
        ILanConnectCardPreviewVisualFactory cardVisualFactory,
        ILanConnectItemPreviewNodePort? nodePort = null)
        : this(
            cardVisualFactory as ILanConnectReferencePreviewVisualFactory ??
            new LanConnectNativeHoverTipFactory(
                new UnavailableNativeHoverTipPort(),
                cardVisualFactory),
            nodePort ?? new LanConnectItemPreviewNodePort(),
            new LanConnectPreviewHotkeyScope())
    {
    }

    internal LanConnectReferencePreviewController(
        ILanConnectCardPreviewVisualFactory cardVisualFactory,
        ILanConnectItemPreviewNodePort nodePort,
        ILanConnectPreviewHotkeyScope hotkeyScope)
        : this(
            cardVisualFactory as ILanConnectReferencePreviewVisualFactory ??
            new LanConnectNativeHoverTipFactory(
                new UnavailableNativeHoverTipPort(),
                cardVisualFactory),
            nodePort,
            hotkeyScope)
    {
    }

    internal LanConnectReferencePreviewController(
        ILanConnectReferencePreviewVisualFactory visualFactory,
        ILanConnectItemPreviewNodePort nodePort,
        ILanConnectPreviewHotkeyScope hotkeyScope)
    {
        _visualFactory = visualFactory ?? throw new ArgumentNullException(nameof(visualFactory));
        _nodePort = nodePort ?? throw new ArgumentNullException(nameof(nodePort));
        _hotkeyScope = hotkeyScope ?? throw new ArgumentNullException(nameof(hotkeyScope));
        Name = PreviewName;
        TopLevel = true;
        ZIndex = 1000;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
    }

    internal LanConnectItemPreviewTestState TestState
    {
        get
        {
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
                _visual?.GetMeta("lan_connect_card_preview", false).AsBool() == true ? 1 : 0,
                _content?.GetChildren().Count(child => !child.IsQueuedForDeletion()) ?? 0,
                _bounds,
                blocksCapture,
                _pinned,
                _visual?.GetMeta("lan_connect_native_preview", false).AsBool() == true,
                _unavailable);
        }
    }

    internal Vector2 CardPreferredSizeForTests => CardSurfacePreferredSize;
    internal bool TrailingHideProtectionActiveForTests => false;
    internal Task HotkeyScopeReleaseTaskForTests => _hotkeyScopeReleaseTask;

    internal void ConfigureAccessibility(string closeLabel)
    {
        if (string.IsNullOrWhiteSpace(closeLabel))
        {
            return;
        }
        _closeAccessibilityLabel = closeLabel;
        if (_closeButton != null && GodotObject.IsInstanceValid(_closeButton))
        {
            _closeButton.AccessibilityName = closeLabel;
            _closeButton.TooltipText = closeLabel;
        }
    }

    public override void _Ready()
    {
        BuildSurface();
    }

    public override void _ExitTree()
    {
        LanConnectReferenceDiagnostics.Shared.Write("preview", "exit_tree", ("reference_id", _referenceId),
            ("visible", Visible), ("pinned", _pinned));
        _consumeCloseShortcutRelease = false;
        ReleaseHotkeyScope();
        ClearContent();
        ResetPresentationState();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (Visible && TryGetPressedPosition(inputEvent, out Vector2 pressedPosition))
        {
            LanConnectReferenceDiagnostics.Shared.Write("preview", "pointer_press",
                ("reference_id", _referenceId), ("input", inputEvent.GetType().Name),
                ("device", inputEvent.Device), ("position", pressedPosition), ("bounds", _bounds));
        }
        if (inputEvent is InputEventScreenTouch touch)
        {
            // Observe even while hidden: the GUI opens the preview after this input pass.
            _touchMouseDeduplicator.ObserveTouch(touch.Position, Time.GetTicksMsec());
        }
        if (inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse &&
            _touchMouseDeduplicator.IsSyntheticMouse(mouse.Position, Time.GetTicksMsec()))
        {
            LanConnectReferenceDiagnostics.Shared.Write("preview", "synthetic_mouse_ignored",
                ("reference_id", _referenceId), ("visible", Visible), ("position", mouse.Position));
            // Do not consume it globally; the touch has already had its normal GUI path.
            return;
        }
        if (_consumeCloseShortcutRelease && IsCloseShortcutReleased(inputEvent))
        {
            _consumeCloseShortcutRelease = false;
            GetViewport().SetInputAsHandled();
            DeferHotkeyScopeRelease();
            return;
        }
        if (!Visible)
        {
            return;
        }
        bool shortcutPressed = IsCloseShortcutPressed(inputEvent);
        bool shortcutReleased = IsCloseShortcutReleased(inputEvent);
        if (shortcutPressed || shortcutReleased)
        {
            ClosePreview(keepHotkeyScopeUntilShortcutRelease: true, reason: "close_shortcut");
            _consumeCloseShortcutRelease = shortcutPressed;
            GetViewport().SetInputAsHandled();
            if (shortcutReleased)
            {
                DeferHotkeyScopeRelease();
            }
            return;
        }
        if (!_pinned || !TryGetPressedPosition(inputEvent, out Vector2 position) ||
            GetGlobalRect().HasPoint(position))
        {
            return;
        }
        ClosePreview(keepHotkeyScopeUntilShortcutRelease: false, reason: "outside_press");
    }

    internal void ShowResolved(
        LanConnectResolvedItem item,
        Rect2 anchor,
        Rect2 viewport,
        bool pinned = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Visible && _pinned && !pinned)
        {
            LanConnectReferenceDiagnostics.Shared.Write("preview", "hover_ignored_while_pinned", ("reference_id", _referenceId));
            return;
        }
        _referenceId = LanConnectReferenceDiagnostics.Identity(item);
        LanConnectReferenceDiagnostics.Shared.Write("preview", "request", ("reference_id", _referenceId),
            ("item_type", item.ItemType), ("status", item.Status), ("preview_type", item.Preview?.GetType().Name),
            ("in_tree", IsInsideTree()), ("visible", Visible), ("pinned", pinned),
            ("bounds", anchor), ("viewport", viewport));
        if (!IsInsideTree() ||
            item.Status != LanConnectResolvedItemStatus.Resolved ||
            item.Preview is not (LanConnectCardPreviewData or LanConnectHoverTipPreviewData))
        {
            LanConnectReferenceDiagnostics.Shared.Write("preview", "gate_rejected", ("reference_id", _referenceId),
                ("in_tree", IsInsideTree()), ("status", item.Status), ("preview_type", item.Preview?.GetType().Name));
            ClosePreview(keepHotkeyScopeUntilShortcutRelease: false, reason: "gate_rejected");
            return;
        }
        try
        {
            LanConnectReferenceDiagnostics.Shared.Write("visual", "create_begin", ("reference_id", _referenceId),
                ("item_type", item.ItemType), ("preview_type", item.Preview.GetType().Name));
            Control visual = item.Preview switch
            {
                LanConnectCardPreviewData card => _visualFactory.Create(card.Card),
                LanConnectHoverTipPreviewData hoverTip => _visualFactory.CreateHoverTip(hoverTip),
                _ => throw new InvalidOperationException("Unsupported local preview data.")
            };
            ShowVisual(
                visual,
                item.ItemType,
                item.Preview is LanConnectHoverTipPreviewData data ? data.Title : item.LocalizedTitle ?? string.Empty,
                item.Preview is LanConnectHoverTipPreviewData hover ? hover.Description : string.Empty,
                item.Preview is LanConnectHoverTipPreviewData { Visual: not null },
                item.Preview is LanConnectCardPreviewData,
                anchor,
                viewport,
                pinned);
        }
        catch (Exception exception)
        {
            LanConnectReferenceDiagnostics.Shared.Failure("preview", "visual_exception", exception,
                ("reference_id", _referenceId), ("item_type", item.ItemType));
            ShowUnavailable(item.ItemType, item.LocalizedTitle, anchor, viewport, pinned);
        }
    }

    internal void ShowCombat(
        LanConnectResolvedCombatReference combat,
        Rect2 anchor,
        Rect2 viewport,
        bool pinned = false)
    {
        ArgumentNullException.ThrowIfNull(combat);
        if (Visible && _pinned && !pinned)
        {
            return;
        }
        _referenceId = LanConnectReferenceDiagnostics.Identity(combat);
        LanConnectReferenceDiagnostics.Shared.Write("preview", "combat_request", ("reference_id", _referenceId),
            ("status", combat.Status), ("in_tree", IsInsideTree()), ("visible", Visible), ("pinned", pinned));
        if (combat.Status != LanConnectResolvedCombatReferenceStatus.Resolved)
        {
            LanConnectReferenceDiagnostics.Shared.Write("preview", "combat_gate_rejected", ("reference_id", _referenceId),
                ("status", combat.Status));
            ClosePreview(keepHotkeyScopeUntilShortcutRelease: false, reason: "combat_gate_rejected");
            return;
        }
        LanConnectHoverTipPreviewData preview = combat.Preview ?? new LanConnectHoverTipPreviewData(
            "power",
            combat.Label,
            combat.Description,
            null);
        try
        {
            ShowVisual(
                _visualFactory.CreateHoverTip(preview),
                "power",
                preview.Title,
                preview.Description,
                preview.Visual != null,
                card: false,
                anchor,
                viewport,
                pinned);
        }
        catch (Exception exception)
        {
            LanConnectReferenceDiagnostics.Shared.Failure("preview", "combat_visual_exception", exception,
                ("reference_id", _referenceId));
            ShowUnavailable("power", combat.Label, anchor, viewport, pinned);
        }
    }

    private void ShowUnavailable(string itemType, string? title, Rect2 anchor, Rect2 viewport, bool pinned)
    {
        if (!IsInsideTree() || _content == null || !GodotObject.IsInstanceValid(_content))
        {
            LanConnectReferenceDiagnostics.Shared.Write("preview", "failure_surface_unavailable", ("reference_id", _referenceId));
            ClosePreview(keepHotkeyScopeUntilShortcutRelease: false, reason: "failure_surface_unavailable");
            return;
        }
        string locale = TranslationServer.GetLocale();
        bool chinese = locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        string message = chinese
            ? "预览暂时无法显示，请关闭后重试。"
            : "Preview is unavailable. Close it and try again.";
        Label label = new()
        {
            Name = "ChatReferencePreviewUnavailable",
            Text = message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", LanConnectLobbyFonts.WithChineseFallback(ThemeDB.FallbackFont));
        try
        {
            ShowVisual(label, itemType, title ?? string.Empty, message, hasLocalVisual: false,
                card: false, anchor, viewport, pinned);
            _unavailable = true;
            LanConnectReferenceDiagnostics.Shared.Write("preview", "failure_surface_shown", ("reference_id", _referenceId),
                ("visible", Visible), ("bounds", _bounds), ("pinned", pinned));
        }
        catch (Exception exception)
        {
            LanConnectReferenceDiagnostics.Shared.Failure("preview", "failure_surface_exception", exception,
                ("reference_id", _referenceId));
            ClosePreview(keepHotkeyScopeUntilShortcutRelease: false, reason: "failure_surface_exception");
        }
    }

    internal void ClosePreview() => ClosePreview(keepHotkeyScopeUntilShortcutRelease: false);

    private void ClosePreview(bool keepHotkeyScopeUntilShortcutRelease, string reason = "requested")
    {
        if (_closing)
        {
            return;
        }
        LanConnectReferenceDiagnostics.Shared.Write("preview", "close", ("reference_id", _referenceId),
            ("source", reason), ("visible", Visible), ("pinned", _pinned));
        _closing = true;
        try
        {
            base.Hide();
            ClearContent();
            ResetPresentationState();
            if (!keepHotkeyScopeUntilShortcutRelease)
            {
                ReleaseHotkeyScope();
            }
        }
        finally
        {
            _closing = false;
        }
    }

    public new void Hide() => ClosePreview();

    internal void Invalidate(LanConnectItemPreviewInvalidation reason)
    {
        if (reason == LanConnectItemPreviewInvalidation.PointerExited && _pinned)
        {
            LanConnectReferenceDiagnostics.Shared.Write("preview", "pointer_exit_ignored_while_pinned",
                ("reference_id", _referenceId));
            return;
        }
        ClosePreview(keepHotkeyScopeUntilShortcutRelease: false, reason: reason.ToString());
    }

    private void ShowVisual(
        Control visual,
        string itemType,
        string title,
        string description,
        bool hasLocalVisual,
        bool card,
        Rect2 anchor,
        Rect2 viewport,
        bool pinned)
    {
        if (_content == null || !GodotObject.IsInstanceValid(_content) ||
            !GodotObject.IsInstanceValid(visual))
        {
            throw new InvalidOperationException("The local preview controller is unavailable.");
        }
        ClearContent();
        visual.SetMeta("lan_connect_card_preview", card);
        try
        {
            _nodePort.Attach(_content, visual);
            if (visual.HasMeta("lan_connect_preview_unavailable") &&
                visual.GetMeta("lan_connect_preview_unavailable").AsBool())
            {
                throw new InvalidOperationException("The local preview could not be initialized.");
            }
        }
        catch
        {
            if (GodotObject.IsInstanceValid(visual) && visual.GetParent() is { } parent)
            {
                parent.RemoveChild(visual);
            }
            if (GodotObject.IsInstanceValid(visual))
            {
                _nodePort.Release(visual);
            }
            throw;
        }
        _visual = visual;
        _unavailable = false;
        _itemType = itemType;
        _title = title;
        _description = description;
        _hasLocalVisual = hasLocalVisual || card;
        _pinned = pinned;
        AccessibilityName = string.IsNullOrWhiteSpace(description)
            ? title
            : $"{title}. {description}";
        _viewport = viewport;
        _closeButton!.Visible = pinned;
        _surface!.MouseFilter = pinned ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        MouseFilter = pinned ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        if (pinned)
        {
            AcquireHotkeyScope();
        }
        else
        {
            ReleaseHotkeyScope();
        }
        Vector2 preferred = card ? CardSurfacePreferredSize : HoverTipPreferredSize;
        Rect2 bounds = ClampBounds(anchor, viewport, preferred);
        _bounds = bounds;
        GlobalPosition = bounds.Position;
        Size = bounds.Size;
        CustomMinimumSize = Vector2.Zero;
        if (_scroll != null)
        {
            _scroll.CustomMinimumSize = new Vector2(
                Math.Max(48, bounds.Size.X - SurfaceMargin * 2),
                Math.Max(48, bounds.Size.Y - SurfaceMargin * 2 - (pinned ? 34 : 0)));
        }
        Visible = true;
        LanConnectReferenceDiagnostics.Shared.Write("preview", "shown", ("reference_id", _referenceId),
            ("item_type", itemType), ("visible", Visible), ("in_tree", IsInsideTree()),
            ("visible_in_tree", IsVisibleInTree()), ("bounds", _bounds), ("actual_bounds", GetGlobalRect()),
            ("viewport", viewport), ("pinned", pinned),
            ("preview_type", visual.GetType().Name));
        QueueRedraw();
    }

    private void AcquireHotkeyScope()
    {
        _hotkeyScopeGeneration++;
        if (_hotkeyScopeActive)
        {
            return;
        }
        _hotkeyScope.Acquire(this);
        _hotkeyScopeActive = true;
    }

    private void ReleaseHotkeyScope()
    {
        _hotkeyScopeGeneration++;
        if (!_hotkeyScopeActive)
        {
            return;
        }
        _hotkeyScope.Release(this);
        _hotkeyScopeActive = false;
    }

    private void DeferHotkeyScopeRelease()
    {
        int generation = ++_hotkeyScopeGeneration;
        _hotkeyScopeReleaseTask = ReleaseHotkeyScopeAfterInputQuiescenceAsync(generation);
        TaskHelper.RunSafely(_hotkeyScopeReleaseTask);
    }

    private async Task ReleaseHotkeyScopeAfterInputQuiescenceAsync(int generation)
    {
        SceneTree tree = GetTree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (_hotkeyScopeGeneration == generation && !Visible)
        {
            ReleaseHotkeyScope();
        }
    }

    private void BuildSurface()
    {
        _surface = new MarginContainer
        {
            Name = SurfaceName,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _surface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _surface.AddThemeConstantOverride("margin_left", SurfaceMargin);
        _surface.AddThemeConstantOverride("margin_top", SurfaceMargin);
        _surface.AddThemeConstantOverride("margin_right", SurfaceMargin);
        _surface.AddThemeConstantOverride("margin_bottom", SurfaceMargin);
        _surface.AddToGroup(LanConnectGodotItemLinkCapturePorts.ItemPreviewGroupName);
        _surface.MouseExited += () => Invalidate(LanConnectItemPreviewInvalidation.PointerExited);
        AddChild(_surface);

        VBoxContainer shell = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _surface.AddChild(shell);
        _closeButton = new Button
        {
            Name = CloseButtonName,
            Text = "×",
            AccessibilityName = _closeAccessibilityLabel,
            TooltipText = _closeAccessibilityLabel,
            Visible = false,
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            CustomMinimumSize = new Vector2(44, 44)
        };
        _closeButton.Pressed += ClosePreview;
        shell.AddChild(_closeButton);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Pass
        };
        shell.AddChild(_scroll);
        _content = new VBoxContainer
        {
            Name = ContentName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _scroll.AddChild(_content);
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
                _content.RemoveChild(child);
                _nodePort.Release(child);
            }
        }
        _visual = null;
    }

    private void ResetPresentationState()
    {
        _itemType = string.Empty;
        _title = string.Empty;
        _description = string.Empty;
        _hasLocalVisual = false;
        _unavailable = false;
        _pinned = false;
        AccessibilityName = string.Empty;
        _viewport = default;
        _bounds = default;
        if (_closeButton != null && GodotObject.IsInstanceValid(_closeButton))
        {
            _closeButton.Visible = false;
        }
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
        return new Rect2(
            new Vector2(
                Math.Clamp(x, viewport.Position.X, maxX),
                Math.Clamp(y, viewport.Position.Y, maxY)),
            new Vector2(width, height));
    }

    private static bool TryGetPressedPosition(InputEvent inputEvent, out Vector2 position)
    {
        switch (inputEvent)
        {
            case InputEventScreenTouch { Pressed: true } touch:
                position = touch.Position;
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse:
                position = mouse.Position;
                return true;
            default:
                position = default;
                return false;
        }
    }

    private static bool IsCloseShortcutPressed(InputEvent inputEvent) =>
        inputEvent is InputEventKey
        {
            Pressed: true,
            Echo: false
        } keyEvent && IsCloseKey(keyEvent) ||
        inputEvent.IsActionPressed(MegaInput.pauseAndBack) && !inputEvent.IsEcho();

    private static bool IsCloseShortcutReleased(InputEvent inputEvent) =>
        inputEvent is InputEventKey { Pressed: false } keyEvent && IsCloseKey(keyEvent) ||
        inputEvent.IsActionReleased(MegaInput.pauseAndBack);

    private static bool IsCloseKey(InputEventKey keyEvent) =>
        keyEvent.Keycode is Key.Escape or Key.Back ||
        keyEvent.PhysicalKeycode is Key.Escape or Key.Back;

    private sealed class UnavailableNativeHoverTipPort : ILanConnectNativeHoverTipPort
    {
        public Control? Instantiate(string scenePath) => null;
        public Material? LoadDebuffMaterial() => null;
    }
}

internal sealed partial class LanConnectItemPreview : LanConnectReferencePreviewController
{
    internal new const string PreviewName = LanConnectReferencePreviewController.PreviewName;
    internal new const string SurfaceName = LanConnectReferencePreviewController.SurfaceName;
    internal new const string ContentName = LanConnectReferencePreviewController.ContentName;
    internal new const string HoverRootName = LanConnectReferencePreviewController.HoverRootName;
    internal new const string HoverTitleName = LanConnectReferencePreviewController.HoverTitleName;
    internal new const string HoverDescriptionName = LanConnectReferencePreviewController.HoverDescriptionName;
    internal new const int SurfaceMargin = LanConnectReferencePreviewController.SurfaceMargin;
    internal new static readonly Vector2 CardVisualMinimumSize =
        LanConnectReferencePreviewController.CardVisualMinimumSize;
    internal new static readonly Vector2 CardSurfacePreferredSize =
        LanConnectReferencePreviewController.CardSurfacePreferredSize;

    internal LanConnectItemPreview()
    {
    }

    internal LanConnectItemPreview(ILanConnectCardPreviewVisualFactory cardVisualFactory)
        : base(cardVisualFactory)
    {
    }

    internal LanConnectItemPreview(
        ILanConnectCardPreviewVisualFactory cardVisualFactory,
        ILanConnectItemPreviewNodePort nodePort)
        : base(cardVisualFactory, nodePort)
    {
    }

    internal LanConnectItemPreview(
        ILanConnectCardPreviewVisualFactory cardVisualFactory,
        ILanConnectItemPreviewNodePort nodePort,
        ILanConnectPreviewHotkeyScope hotkeyScope)
        : base(cardVisualFactory, nodePort, hotkeyScope)
    {
    }
}
