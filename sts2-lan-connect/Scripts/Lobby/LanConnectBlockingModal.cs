using Godot;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectBlockingModal
{
    internal static void Register(Node modalRoot)
    {
        ArgumentNullException.ThrowIfNull(modalRoot);
        modalRoot.AddToGroup(LanConnectConstants.BlockingModalGroupName);
    }

    internal static bool IsAnyVisible(SceneTree tree, Node? excludedRoot = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        foreach (Node node in tree.GetNodesInGroup(LanConnectConstants.BlockingModalGroupName))
        {
            if (!GodotObject.IsInstanceValid(node) ||
                !node.IsInsideTree() ||
                IsExcluded(node, excludedRoot))
            {
                continue;
            }

            bool visible = node switch
            {
                Control control => control.IsVisibleInTree(),
                CanvasLayer layer => layer.Visible,
                Window window => window.Visible,
                _ => false
            };
            if (visible)
            {
                return true;
            }
        }

        foreach (Node node in tree.Root.FindChildren("*", "Window", recursive: true, owned: false))
        {
            if (node is Window { Visible: true, Exclusive: true } window &&
                !IsExcluded(window, excludedRoot))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsExcluded(Node node, Node? excludedRoot) =>
        excludedRoot != null &&
        (ReferenceEquals(node, excludedRoot) || excludedRoot.IsAncestorOf(node));
}
