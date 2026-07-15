using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectPointerGesture(bool Alt, bool Left, bool Pressed);

internal interface ILanConnectItemLinkCapturePorts
{
    object? GuiGetHoveredControl();

    object? GetParent(object node);

    bool IsChatInteractionBlocking { get; }

    bool ItemRefsEnabledForSelectedChannel { get; }

    bool IsCaptureBoundary(object node);

    bool IsSupportedHolder(object node);

    bool TryResolveCard(object node, out LanConnectItemRun run);

    bool TryResolveRelic(object node, out LanConnectItemRun run);

    bool TryResolvePotion(object node, out LanConnectItemRun run);

    bool InsertAndFocus(LanConnectItemRun run);
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
        try
        {
            return TryCaptureCore(gesture);
        }
        catch
        {
            return false;
        }
    }

    internal static bool ItemRefsEnabled(LanConnectChatFeatureVersions enabled)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        return enabled.RichContentVersion == 1 && enabled.ItemRefVersion == 1;
    }

    private bool TryCaptureCore(LanConnectPointerGesture gesture)
    {
        if (!gesture.Alt || !gesture.Left || !gesture.Pressed ||
            _ports.IsChatInteractionBlocking ||
            !_ports.ItemRefsEnabledForSelectedChannel)
        {
            return false;
        }

        for (object? node = _ports.GuiGetHoveredControl();
             node != null;
             node = _ports.GetParent(node))
        {
            if (_ports.IsCaptureBoundary(node))
            {
                return false;
            }

            if (_ports.TryResolveCard(node, out LanConnectItemRun run) ||
                _ports.TryResolveRelic(node, out run) ||
                _ports.TryResolvePotion(node, out run))
            {
                LanConnectItemRun? canonical = Canonicalize(run);
                return canonical != null && _ports.InsertAndFocus(canonical);
            }
            if (_ports.IsSupportedHolder(node))
            {
                return false;
            }
        }

        return false;
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

    internal LanConnectGodotItemLinkCapturePorts(LanConnectLobbyRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public object? GuiGetHoveredControl() => _runtime.GetViewport().GuiGetHoveredControl();

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

    public bool IsCaptureBoundary(object node) => node is NPreviewCardHolder;

    public bool IsSupportedHolder(object node) =>
        node is NCardHolder or NRelicInventoryHolder or NPotionHolder;

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

    public bool InsertAndFocus(LanConnectItemRun run)
    {
        if (_runtime.HasActiveRoomSession)
        {
            LanConnectRoomChatOverlay? overlay = ResolveRoomOverlay();
            if (overlay == null)
            {
                return false;
            }
            SelectedState.RichDraft.InsertEntity(run);
            overlay.OpenSelectedChannelAndFocusDraft();
            return true;
        }

        LanConnectLobbyOverlay? lobby = ResolveLobbyOverlay();
        if (lobby == null)
        {
            return false;
        }
        SelectedState.RichDraft.InsertEntity(run);
        lobby.OpenAndFocusServerChat();
        return true;
    }

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

    private static bool HasVisibleItemPreview(SceneTree tree)
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
        if (inputEvent is not InputEventMouseButton mouse)
        {
            return false;
        }

        bool consumed = capture.TryCapture(new LanConnectPointerGesture(
            mouse.AltPressed,
            mouse.ButtonIndex == MouseButton.Left,
            mouse.Pressed));
        if (consumed)
        {
            markHandled();
        }
        return consumed;
    }
}
