using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectPointerGesture(bool Alt, bool Left, bool Pressed);

internal interface ILanConnectItemLinkCapturePorts
{
    object? GuiGetHoveredControl();

    object? GuiGetControlAtPosition(Vector2 position) => null;

    bool HasVisibleReferenceTarget(LanConnectReferenceTargetKind allowedTargets) => false;

    object? GetParent(object node);

    bool IsChatInteractionBlocking { get; }

    bool ItemRefsEnabledForSelectedChannel { get; }

    bool CombatRefsEnabledForSelectedChannel { get; }

    bool IsRoomChannelSelected { get; }

    bool IsCaptureBoundary(object node);

    bool IsSupportedHolder(object node);

    bool IsPowerHolder(object node);

    bool IsPlayerHolder(object node);

    bool TryResolveCard(object node, out LanConnectItemRun run);

    bool TryResolveRelic(object node, out LanConnectItemRun run);

    bool TryResolvePotion(object node, out LanConnectItemRun run);

    bool TryResolvePower(object node, out LanConnectCombatRun run);

    bool TryResolvePlayerTarget(object node, out LanConnectCombatRun run);

    bool InsertAndFocus(LanConnectItemRun run);

    bool InsertCombatAndFocus(LanConnectCombatRun run);

    void ShowCombatRoomOnlyWarning();
}

internal sealed class LanConnectItemLinkCapture
{
    private readonly ILanConnectItemLinkCapturePorts _ports;

    internal LanConnectItemLinkCapture(ILanConnectItemLinkCapturePorts ports)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
    }

    internal bool TryCapture(LanConnectPointerGesture gesture)
    {
        if (!gesture.Alt || !gesture.Left)
        {
            return false;
        }
        return TryCapture(new LanConnectReferenceCaptureRequest(
            LanConnectReferenceModeSource.DirectAltClick,
            LanConnectReferencePointerKind.Mouse,
            gesture.Pressed,
            Vector2.Zero,
            StartingControl: null,
            ArmedGeneration: 1)).Consumed;
    }

    internal LanConnectReferenceCaptureResult TryCapture(LanConnectReferenceCaptureRequest request)
    {
        try
        {
            return TryCaptureCore(request);
        }
        catch
        {
            return new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Unsupported);
        }
    }

    internal static bool ItemRefsEnabled(LanConnectChatFeatureVersions enabled)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        return enabled.RichContentVersion == 1 && enabled.ItemRefVersion == 1;
    }

    internal static bool CombatRefsEnabled(LanConnectChatFeatureVersions enabled)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        return enabled.RichContentVersion == 1 && enabled.CombatRefVersion == 1;
    }

    private LanConnectReferenceCaptureResult TryCaptureCore(LanConnectReferenceCaptureRequest request)
    {
        if (!request.Pressed || request.ArmedGeneration <= 0 || _ports.IsChatInteractionBlocking)
        {
            return new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Blocked);
        }

        object? startingControl = request.StartingControl ??
            (request.PointerKind == LanConnectReferencePointerKind.Touch
                ? _ports.GuiGetControlAtPosition(request.ScreenPosition)
                : _ports.GuiGetHoveredControl());
        for (object? node = startingControl;
             node != null;
             node = _ports.GetParent(node))
        {
            if (_ports.IsCaptureBoundary(node))
            {
                return new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Blocked);
            }

            if (_ports.TryResolveCard(node, out LanConnectItemRun run) ||
                _ports.TryResolveRelic(node, out run) ||
                _ports.TryResolvePotion(node, out run))
            {
                if (!_ports.ItemRefsEnabledForSelectedChannel)
                {
                    return new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Unsupported);
                }
                LanConnectItemRun? canonical = Canonicalize(run);
                return canonical != null && _ports.InsertAndFocus(canonical)
                    ? new LanConnectReferenceCaptureResult(
                        LanConnectReferenceCaptureStatus.Captured,
                        LanConnectReferenceTargetKind.Item)
                    : new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Unsupported);
            }
            if (_ports.IsSupportedHolder(node))
            {
                return new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Unsupported);
            }
            if (_ports.TryResolvePower(node, out LanConnectCombatRun combat))
            {
                return TryInsertCombat(combat)
                    ? new LanConnectReferenceCaptureResult(
                        LanConnectReferenceCaptureStatus.Captured,
                        LanConnectReferenceTargetKind.Combat)
                    : new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Unsupported);
            }
            if (_ports.IsPowerHolder(node))
            {
                return new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Unsupported);
            }
            if (_ports.TryResolvePlayerTarget(node, out combat))
            {
                return TryInsertCombat(combat)
                    ? new LanConnectReferenceCaptureResult(
                        LanConnectReferenceCaptureStatus.Captured,
                        LanConnectReferenceTargetKind.Combat)
                    : new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Unsupported);
            }
            if (_ports.IsPlayerHolder(node))
            {
                return new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Unsupported);
            }
        }

        return new LanConnectReferenceCaptureResult(LanConnectReferenceCaptureStatus.Unsupported);
    }

    private bool TryInsertCombat(LanConnectCombatRun run)
    {
        if (!_ports.IsRoomChannelSelected)
        {
            _ports.ShowCombatRoomOnlyWarning();
            return false;
        }
        return _ports.CombatRefsEnabledForSelectedChannel &&
               _ports.InsertCombatAndFocus(run);
    }

    private static LanConnectItemRun? Canonicalize(LanConnectItemRun run)
    {
        if (!LanConnectServerChatProtocol.IsValidModelId(run.ModelId))
        {
            return null;
        }

        return run.ItemType switch
        {
            "card" => run with { UpgradeLevel = Math.Clamp(run.UpgradeLevel ?? 0, 0, 9) },
            "relic" when !run.UpgradeLevel.HasValue => run,
            "potion" when !run.UpgradeLevel.HasValue => run,
            _ => null
        };
    }
}

internal sealed class LanConnectGodotItemLinkCapturePorts : ILanConnectItemLinkCapturePorts
{
    internal const string ItemPreviewGroupName = "sts2_lan_connect_item_preview";

    private readonly LanConnectLobbyRuntime _runtime;
    private readonly LanConnectRoomCombatReferenceResolver _combatResolver;

    internal LanConnectGodotItemLinkCapturePorts(LanConnectLobbyRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _combatResolver = new LanConnectRoomCombatReferenceResolver(runtime);
    }

    public object? GuiGetHoveredControl() => _runtime.GetViewport().GuiGetHoveredControl();

    public object? GuiGetControlAtPosition(Vector2 position)
    {
        Node root = _runtime.GetTree().Root;
        return SelectControlAtPosition(
            root,
            position,
            node => IsSupportedHolder(node) || IsPowerHolder(node) || IsPlayerHolder(node),
            IsCaptureBoundary);
    }

    internal static object? SelectControlAtPosition(
        Node root,
        Vector2 position,
        Func<object, bool> isReferenceTarget,
        Func<object, bool> isCaptureBoundary)
    {
        Control? selected = null;
        Control? selectedReference = null;
        foreach (Node node in root.FindChildren(
                     "*",
                     "Control",
                     recursive: true,
                     owned: false))
        {
            if (node is not Control control ||
                control.IsQueuedForDeletion() ||
                !control.IsVisibleInTree() ||
                control.MouseFilter == Control.MouseFilterEnum.Ignore ||
                !control.GetGlobalRect().HasPoint(position))
            {
                continue;
            }
            selected = control;
            for (Node? ancestor = control; ancestor != null; ancestor = ancestor.GetParent())
            {
                if (isCaptureBoundary(ancestor))
                {
                    break;
                }
                if (isReferenceTarget(ancestor))
                {
                    selectedReference = control;
                    break;
                }
            }
        }
        return selectedReference ?? selected;
    }

    public object? GetParent(object node) => (node as Node)?.GetParent();

    public bool IsChatInteractionBlocking
    {
        get
        {
            Viewport viewport = _runtime.GetViewport();
            if (LanConnectAccessibilityKeyboard.IsTextInput(viewport.GuiGetFocusOwner()))
            {
                return true;
            }
            SceneTree tree = _runtime.GetTree();
            if (LanConnectBlockingModal.IsAnyVisible(tree) || HasVisibleItemPreview(tree))
            {
                return true;
            }

            if (_runtime.HasActiveRoomSession)
            {
                return ResolveRoomOverlay()?.ItemLinkCaptureBlocked == true;
            }
            return ResolveLobbyOverlay()?.ItemLinkCaptureBlocked == true;
        }
    }

    public bool ItemRefsEnabledForSelectedChannel
    {
        get
        {
            LanConnectChatChannelState state = SelectedState;
            return state.Presentation == LanConnectServerChatPresentation.Ready &&
                   state.ChatEnabled &&
                   LanConnectItemLinkCapture.ItemRefsEnabled(state.EnabledRichFeatures);
        }
    }

    public bool CombatRefsEnabledForSelectedChannel
    {
        get
        {
            LanConnectChatChannelState state = SelectedState;
            return state.Presentation == LanConnectServerChatPresentation.Ready &&
                   state.ChatEnabled &&
                   LanConnectItemLinkCapture.CombatRefsEnabled(state.EnabledRichFeatures);
        }
    }

    public bool IsRoomChannelSelected =>
        _runtime.HasActiveRoomSession &&
        _runtime.Chat.SelectedChannel == LanConnectChatChannel.Room;

    public bool IsCaptureBoundary(object node) =>
        node is NPreviewCardHolder or LanConnectBasicChatPanel ||
        node is Node godotNode &&
        (godotNode.IsInGroup(ItemPreviewGroupName) ||
         godotNode.IsInGroup(LanConnectConstants.BlockingModalGroupName));

    public bool IsSupportedHolder(object node) =>
        node is NCardHolder or NRelicInventoryHolder or NPotionHolder;

    public bool IsPowerHolder(object node) => node is NPower;

    public bool IsPlayerHolder(object node) =>
        node is NCreature { Entity.IsPlayer: true };

    public bool TryResolveCard(object node, out LanConnectItemRun run)
    {
        if (node is NCardHolder { CardModel: { } model } && node is not NPreviewCardHolder)
        {
            run = new LanConnectItemRun(
                "card",
                model.Id.ToString(),
                Math.Clamp(model.CurrentUpgradeLevel, 0, 9));
            return true;
        }
        run = null!;
        return false;
    }

    public bool TryResolveRelic(object node, out LanConnectItemRun run)
    {
        if (node is NRelicInventoryHolder { Relic.Model: { } model })
        {
            run = new LanConnectItemRun("relic", model.Id.ToString());
            return true;
        }
        run = null!;
        return false;
    }

    public bool TryResolvePotion(object node, out LanConnectItemRun run)
    {
        if (node is NPotionHolder { Potion.Model: { } model })
        {
            run = new LanConnectItemRun("potion", model.Id.ToString());
            return true;
        }
        run = null!;
        return false;
    }

    public bool TryResolvePower(object node, out LanConnectCombatRun run)
    {
        run = null!;
        if (node is not NPower powerNode)
        {
            return false;
        }
        try
        {
            MegaCrit.Sts2.Core.Models.PowerModel model = powerNode.Model;
            return _combatResolver.TryCreatePowerRun(
                new LanConnectPowerCaptureCandidate(
                    model.Id.ToString(),
                    model.Amount,
                    ((ILanConnectRoomCombatContext)_runtime).ActiveRoomSessionId,
                    model.Owner?.Player?.NetId.ToString(),
                    model.Applier?.Player?.NetId.ToString()),
                out run);
        }
        catch
        {
            return false;
        }
    }

    public bool TryResolvePlayerTarget(object node, out LanConnectCombatRun run)
    {
        run = null!;
        if (node is not NCreature { Entity.Player: { } player })
        {
            return false;
        }
        return _combatResolver.TryCreatePlayerTargetRun(
            new LanConnectPlayerTargetCaptureCandidate(
                player.NetId.ToString(),
                ((ILanConnectRoomCombatContext)_runtime).ActiveRoomSessionId),
            out run);
    }

    public bool InsertAndFocus(LanConnectItemRun run)
    {
        LanConnectChatChannelState expectedState = SelectedState;
        if (_runtime.HasActiveRoomSession)
        {
            LanConnectRoomChatOverlay? overlay = ResolveRoomOverlay();
            return TryInsertAndFocus(
                roomActive: true,
                expectedState,
                run,
                overlay,
                lobbyOverlay: null);
        }

        LanConnectLobbyOverlay? lobby = ResolveLobbyOverlay();
        return TryInsertAndFocus(
            roomActive: false,
            expectedState,
            run,
            roomOverlay: null,
            lobby);
    }

    public bool InsertCombatAndFocus(LanConnectCombatRun run)
    {
        if (!_combatResolver.CanCommit(run) || !IsRoomChannelSelected)
        {
            return false;
        }
        LanConnectChatChannelState expectedState = SelectedState;
        return ResolveRoomOverlay()?.TryInsertCombatReferenceAndFocus(expectedState, run) == true;
    }

    public void ShowCombatRoomOnlyWarning() =>
        ResolveRoomOverlay()?.ShowCombatRoomOnlyWarning();

    internal static bool TryInsertAndFocus(
        bool roomActive,
        LanConnectChatChannelState expectedState,
        LanConnectItemRun run,
        LanConnectRoomChatOverlay? roomOverlay,
        LanConnectLobbyOverlay? lobbyOverlay) =>
        roomActive
            ? roomOverlay?.TryInsertItemAndFocus(expectedState, run) == true
            : lobbyOverlay?.TryInsertItemAndFocus(expectedState, run) == true;

    private LanConnectChatChannelState SelectedState =>
        _runtime.HasActiveRoomSession && _runtime.Chat.SelectedChannel == LanConnectChatChannel.Room
            ? _runtime.Chat.Room
            : _runtime.Chat.Server;

    private static LanConnectLobbyOverlay? ResolveLobbyOverlay()
    {
        LanConnectLobbyOverlay? overlay = LanConnectLobbyOverlay.Instance;
        return GodotObject.IsInstanceValid(overlay) ? overlay : null;
    }

    private LanConnectRoomChatOverlay? ResolveRoomOverlay() =>
        _runtime.GetTree().Root.GetNodeOrNull<LanConnectRoomChatOverlay>(LanConnectConstants.RoomChatOverlayName);

    internal static bool HasVisibleItemPreview(SceneTree tree)
    {
        foreach (Node node in tree.GetNodesInGroup(ItemPreviewGroupName))
        {
            if (node is CanvasItem { Visible: true } visible && visible.IsVisibleInTree())
            {
                return true;
            }
        }
        return false;
    }

    public bool HasVisibleReferenceTarget(LanConnectReferenceTargetKind allowedTargets)
    {
        foreach (Node node in _runtime.GetTree().Root.FindChildren("*", "", true, false))
        {
            if (node is not CanvasItem canvas ||
                canvas.IsQueuedForDeletion() ||
                !canvas.IsVisibleInTree() ||
                node is NPreviewCardHolder)
            {
                continue;
            }
            if ((allowedTargets.HasFlag(LanConnectReferenceTargetKind.Item) && IsSupportedHolder(node)) ||
                (allowedTargets.HasFlag(LanConnectReferenceTargetKind.Combat) &&
                 (IsPowerHolder(node) || IsPlayerHolder(node))))
            {
                return true;
            }
        }
        return false;
    }
}

internal static class LanConnectItemLinkCaptureInputRoute
{
    internal static bool TryRoute(
        InputEvent inputEvent,
        LanConnectItemLinkCapture capture,
        Action markHandled)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(markHandled);
        LanConnectReferenceCaptureRequest request;
        switch (inputEvent)
        {
            case InputEventMouseButton mouse when
                mouse.AltPressed && mouse.ButtonIndex == MouseButton.Left:
                request = new LanConnectReferenceCaptureRequest(
                    LanConnectReferenceModeSource.DirectAltClick,
                    LanConnectReferencePointerKind.Mouse,
                    mouse.Pressed,
                    mouse.GlobalPosition,
                    StartingControl: null,
                    ArmedGeneration: 1);
                break;
            case InputEventScreenTouch touch:
                request = new LanConnectReferenceCaptureRequest(
                    LanConnectReferenceModeSource.TouchButton,
                    LanConnectReferencePointerKind.Touch,
                    touch.Pressed,
                    touch.Position,
                    StartingControl: null,
                    ArmedGeneration: 1);
                break;
            default:
                return false;
        }

        bool consumed = capture.TryCapture(request).Consumed;
        if (consumed)
        {
            markHandled();
        }
        return consumed;
    }
}
