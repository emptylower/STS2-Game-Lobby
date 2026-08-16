using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;

namespace Sts2LanConnect.Scripts;

internal static class HostSubmenuPatches
{
    private const int DuplicateWithoutSignals = 14;
    private const string HookedMetaKey = "sts2_lan_connect_host_hooks";
    private static readonly FieldInfo? LoadingOverlayField = typeof(NMultiplayerHostSubmenu).GetField("_loadingOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? StackField = typeof(NSubmenu).GetField("_stack", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void EnsureLanHostButton(NMultiplayerHostSubmenu submenu)
    {
        try
        {
            EnsureLanHostModePicker(
                submenu,
                () => GetModeAvailability(submenu),
                mode => StartSelectedLanHostAsync(submenu, mode));

            if (FindHostButton(submenu) != null)
            {
                return;
            }

            NSubmenuButton template = submenu.GetNode<NSubmenuButton>("StandardButton");
            Node parent = template.GetParent();
            NSubmenuButton? lanButton = template.Duplicate(DuplicateWithoutSignals) as NSubmenuButton;
            if (lanButton == null)
            {
                Log.Error("sts2_lan_connect failed to duplicate StandardButton for LAN host button.");
                return;
            }

            lanButton.Name = LanConnectConstants.HostButtonName;
            ConfigureLanButton(lanButton);
            lanButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnLanHostPressed(submenu)));
            parent.AddChild(lanButton);
            parent.MoveChild(lanButton, template.GetIndex() + 1);
        }
        catch (Exception ex)
        {
            Log.Error($"sts2_lan_connect failed to inject LAN host button: {ex}");
        }
    }

    internal static LanConnectLanHostModePicker EnsureLanHostModePicker(
        Node parent,
        Func<LanConnectLanHostModeAvailability> availability,
        Func<LanConnectLanHostMode, Task> onSelected)
    {
        LanConnectLanHostModePicker? picker = parent.FindChild(
            LanConnectConstants.LanHostModePickerName,
            recursive: true,
            owned: false) as LanConnectLanHostModePicker;
        if (picker == null)
        {
            picker = new LanConnectLanHostModePicker
            {
                Name = LanConnectConstants.LanHostModePickerName
            };
            parent.AddChild(picker);
        }

        picker.Initialize(availability, onSelected);
        return picker;
    }

    internal static void ScheduleEnsureLanHostButton(NMultiplayerHostSubmenu submenu, string source)
    {
        if (!GodotObject.IsInstanceValid(submenu))
        {
            return;
        }

        if (!submenu.HasMeta(HookedMetaKey))
        {
            submenu.SetMeta(HookedMetaKey, true);
            submenu.Connect(Node.SignalName.TreeEntered, Callable.From(() => QueueEnsureLanHostButton(submenu, "tree_entered")));
            submenu.Connect(Node.SignalName.Ready, Callable.From(() => QueueEnsureLanHostButton(submenu, "ready")));
            submenu.Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(() => QueueEnsureLanHostButton(submenu, "visibility_changed")));
        }

        Callable.From(() => TryEnsureLanHostButton(submenu, source)).CallDeferred();
    }

    private static void QueueEnsureLanHostButton(NMultiplayerHostSubmenu submenu, string source)
    {
        Callable.From(() => TryEnsureLanHostButton(submenu, source)).CallDeferred();
    }

    private static void TryEnsureLanHostButton(NMultiplayerHostSubmenu submenu, string source)
    {
        if (!GodotObject.IsInstanceValid(submenu) || !submenu.IsInsideTree() || !submenu.IsNodeReady())
        {
            return;
        }

        bool alreadyInstalled = FindHostButton(submenu) != null;
        EnsureLanHostButton(submenu);
        if (!alreadyInstalled && FindHostButton(submenu) != null)
        {
            NSubmenuButton template = submenu.GetNode<NSubmenuButton>("StandardButton");
            Node? parent = template.GetParent();
            Log.Info($"sts2_lan_connect injected LAN host button via {source}; template={template.GetPath()}, parentType={parent?.GetType().FullName ?? "<null>"}");
        }
    }

    private static void OnLanHostPressed(NMultiplayerHostSubmenu submenu)
    {
        LanConnectLanHostModePicker? picker = submenu.FindChild(
            LanConnectConstants.LanHostModePickerName,
            recursive: true,
            owned: false) as LanConnectLanHostModePicker;
        if (picker == null)
        {
            Log.Error("sts2_lan_connect could not resolve LAN host mode picker.");
            LanConnectPopupUtil.ShowInfo("未能打开 LAN 模式选择：页面上下文未就绪，请重新打开 Host 页面后再试。");
            return;
        }

        picker.Open();
    }

    internal static LanConnectLanHostModeAvailability GetModeAvailability(
        NMultiplayerHostSubmenu submenu) => new(
        submenu.GetNode<NSubmenuButton>("StandardButton").IsEnabled,
        submenu.GetNode<NSubmenuButton>("DailyButton").IsEnabled,
        submenu.GetNode<NSubmenuButton>("CustomRunButton").IsEnabled);

    private static async Task StartSelectedLanHostAsync(
        NMultiplayerHostSubmenu submenu,
        LanConnectLanHostMode mode)
    {
        Control? loadingOverlay = LoadingOverlayField?.GetValue(submenu) as Control;
        NSubmenuStack? stack = StackField?.GetValue(submenu) as NSubmenuStack;
        if (loadingOverlay == null || stack == null)
        {
            Log.Error($"sts2_lan_connect could not resolve host flow dependencies. loadingOverlayNull={loadingOverlay == null} stackNull={stack == null}");
            LanConnectPopupUtil.ShowInfo("未能启动 LAN Host：页面上下文未就绪，请重新打开 Host 页面后再试。");
            return;
        }

        GameMode gameMode = mode switch
        {
            LanConnectLanHostMode.Standard => GameMode.Standard,
            LanConnectLanHostMode.Daily => GameMode.Daily,
            LanConnectLanHostMode.Custom => GameMode.Custom,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        await LanConnectHostFlow.StartLanHostAsync(gameMode, loadingOverlay, stack);
    }

    private static void ConfigureLanButton(NSubmenuButton button)
    {
        MegaLabel title = button.GetNode<MegaLabel>("%Title");
        MegaRichTextLabel description = button.GetNode<MegaRichTextLabel>("%Description");
        title.SetTextAutoSize("LAN 调试建房");
        description.Text = "选择标准、每日挑战或自定义模式，直接启动 ENet LAN Host。";
    }

    private static Control? FindHostButton(NMultiplayerHostSubmenu submenu)
    {
        return submenu.FindChild(LanConnectConstants.HostButtonName, recursive: true, owned: false) as Control;
    }
}
