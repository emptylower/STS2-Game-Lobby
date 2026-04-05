using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2LanConnect.Scripts;

internal static class RestSitePatches
{
    private static readonly Vector2 LeftExtraFrontOffset = new(-250f, 35f);
    private static readonly Vector2 LeftExtraBackOffset = new(-240f, -20f);
    private static readonly Vector2 RightExtraFrontOffset = new(250f, 35f);
    private static readonly Vector2 RightExtraBackOffset = new(240f, -20f);
    private static readonly Vector2 LogXOffsetLeft = new(-250f, 0f);
    private static readonly Vector2 LogXOffsetRight = new(250f, 0f);
    private static readonly Vector2 ExtraSeatStep = new(70f, -45f);

    private static readonly MethodInfo? CharacterContainerGetter =
        AccessTools.PropertyGetter(typeof(List<Control>), "Item");

    private static readonly MethodInfo? SafeContainerGetter =
        AccessTools.Method(typeof(RestSitePatches), nameof(GetContainerSafe));

    public static void Apply(Harmony harmony)
    {
        MethodInfo? ready = AccessTools.Method(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready));
        if (ready != null)
        {
            harmony.Patch(ready, transpiler: new HarmonyMethod(typeof(RestSitePatches), nameof(ReadyTranspiler)));
        }

        MethodInfo? hover = AccessTools.Method(typeof(NRestSiteRoom), "OnPlayerChangedHoveredRestSiteOption");
        if (hover != null)
        {
            harmony.Patch(hover, prefix: new HarmonyMethod(typeof(RestSitePatches), nameof(HoverPrefix)));
        }

        MethodInfo? beforeSelect = AccessTools.Method(typeof(NRestSiteRoom), "OnBeforePlayerSelectedRestSiteOption");
        if (beforeSelect != null)
        {
            harmony.Patch(beforeSelect, prefix: new HarmonyMethod(typeof(RestSitePatches), nameof(BeforeSelectPrefix)));
        }

        MethodInfo? afterSelect = AccessTools.Method(typeof(NRestSiteRoom), "OnAfterPlayerSelectedRestSiteOption");
        if (afterSelect != null)
        {
            harmony.Patch(afterSelect, prefix: new HarmonyMethod(typeof(RestSitePatches), nameof(AfterSelectPrefix)));
        }

        Log.Info("sts2_lan_connect gameplay: rest site patches applied.");
    }

    // ReSharper disable UnusedMember.Local

    private static IEnumerable<CodeInstruction> ReadyTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (CharacterContainerGetter != null && SafeContainerGetter != null && instruction.Calls(CharacterContainerGetter))
            {
                yield return new CodeInstruction(OpCodes.Call, SafeContainerGetter);
                continue;
            }

            yield return instruction;
        }
    }

    private static bool HoverPrefix(NRestSiteRoom __instance, ulong playerId)
    {
        if (!TryGetCharacter(__instance, playerId, out NRestSiteCharacter character))
        {
            return false;
        }

        character.ShowHoveredRestSiteOption(TryGetHoveredOption(playerId));
        return false;
    }

    private static bool BeforeSelectPrefix(NRestSiteRoom __instance, RestSiteOption option, ulong playerId)
    {
        if (TryGetCharacter(__instance, playerId, out NRestSiteCharacter character))
        {
            character.SetSelectingRestSiteOption(option);
        }

        return false;
    }

    private static bool AfterSelectPrefix(NRestSiteRoom __instance, RestSiteOption option, bool success, ulong playerId)
    {
        if (!TryGetCharacter(__instance, playerId, out NRestSiteCharacter character))
        {
            return false;
        }

        character.SetSelectingRestSiteOption(null);
        if (success)
        {
            character.ShowSelectedRestSiteOption(option);
            if (!LocalContext.IsMe(character.Player))
            {
                MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(option.DoRemotePostSelectVfx());
            }
        }

        return false;
    }

    // ReSharper restore UnusedMember.Local

    internal static Control GetContainerSafe(List<Control> containers, int index)
    {
        if (containers.Count == 0)
        {
            throw new InvalidOperationException("No character containers found in rest site room.");
        }

        EnsureRestSiteContainers(containers, index + 1);
        return containers[NormalizeWrappedIndex(index, containers.Count)];
    }

    private static int NormalizeWrappedIndex(int index, int count)
    {
        int num = index % count;
        return num >= 0 ? num : num + count;
    }

    private static void EnsureRestSiteContainers(List<Control> containers, int requiredCount)
    {
        if (requiredCount <= containers.Count)
        {
            return;
        }

        Control parent = containers[0].GetParent<Control>();
        if (parent == null)
        {
            return;
        }

        EnsureExtraLogs(parent);
        int templateCount = containers.Count;
        while (containers.Count < requiredCount)
        {
            int count = containers.Count;
            Control source = containers[count % templateCount];
            Control control = source.Duplicate() as Control ?? new Control();
            RemoveAllChildren(control);
            control.Name = $"Character_Auto_{count + 1}";
            control.Position = GetExtraContainerPosition(containers, count);
            parent.AddChild(control);
            containers.Add(control);
        }
    }

    private static void RemoveAllChildren(Node node)
    {
        for (int i = node.GetChildCount() - 1; i >= 0; i--)
        {
            Node child = node.GetChild(i);
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Vector2 GetExtraContainerPosition(List<Control> containers, int index)
    {
        if (containers.Count < 4)
        {
            return containers[containers.Count - 1].Position;
        }

        int effectiveMax = LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();
        if (index >= effectiveMax)
        {
            Log.Warn($"sts2_lan_connect rest site: character index {index} exceeds configured max {effectiveMax}.");
        }

        if (index < 4)
        {
            return containers[index].Position;
        }

        int extraSeatIndex = index - 4;
        bool isLeftSide = extraSeatIndex % 2 == 0;
        int depthLevel = extraSeatIndex / 2;

        Vector2 frontSeatPosition = isLeftSide
            ? containers[0].Position + LeftExtraFrontOffset
            : containers[1].Position + RightExtraFrontOffset;
        Vector2 backSeatPosition = isLeftSide
            ? containers[2].Position + LeftExtraBackOffset
            : containers[3].Position + RightExtraBackOffset;

        if (depthLevel == 0)
        {
            return frontSeatPosition;
        }

        if (depthLevel == 1)
        {
            return backSeatPosition;
        }

        int extraDepth = depthLevel - 1;
        Vector2 extraOffset = new((isLeftSide ? -1f : 1f) * ExtraSeatStep.X * extraDepth, ExtraSeatStep.Y * extraDepth);
        return backSeatPosition + extraOffset;
    }

    private static void EnsureExtraLogs(Control parent)
    {
        Node? background = parent.GetChildCount() > 0 ? parent.GetChild(0) : null;
        if (background == null || background.GetNodeOrNull<Node>("AutoExtraLogsMarker") != null)
        {
            return;
        }

        Node marker = new();
        marker.Name = "AutoExtraLogsMarker";
        background.AddChild(marker);

        bool leftLogOk = DuplicateShiftedNode(background, "RestSiteLLog", LogXOffsetLeft, "AutoL");
        bool rightLogOk = DuplicateShiftedNode(background, "RestSiteRLog", LogXOffsetRight, "AutoR");
        bool leftLogLayer2Ok = DuplicateShiftedNode(background, "RestSiteLighting/RestSiteLLog2", LogXOffsetLeft, "AutoL");
        bool rightLogLayer2Ok = DuplicateShiftedNode(background, "RestSiteLighting/RestSiteRLog2", LogXOffsetRight, "AutoR");

        if (!leftLogOk && !rightLogOk && !leftLogLayer2Ok && !rightLogLayer2Ok)
        {
            Log.Warn("sts2_lan_connect rest site: no log nodes found for duplication. Scene tree may have changed.");
        }
    }

    private static bool DuplicateShiftedNode(Node root, string nodePath, Vector2 offset, string suffix)
    {
        Node? node = root.GetNodeOrNull<Node>(nodePath);
        if (node == null)
        {
            return false;
        }

        Node? parent = node.GetParent();
        if (parent == null)
        {
            return false;
        }

        Node clone = node.Duplicate();
        clone.Name = $"{node.Name}_{suffix}";
        parent.AddChild(clone);

        if (node is Control control && clone is Control cloneControl)
        {
            cloneControl.Position = control.Position + offset;
        }

        if (node is Node2D node2D && clone is Node2D clone2D)
        {
            clone2D.Position = node2D.Position + offset;
        }

        return true;
    }

    private static bool TryGetCharacter(NRestSiteRoom room, ulong playerId, out NRestSiteCharacter character)
    {
        NRestSiteCharacter? found = room.Characters.FirstOrDefault(c => c.Player.NetId == playerId);
        if (found == null)
        {
            character = null!;
            return false;
        }

        character = found;
        return true;
    }

    private static RestSiteOption? TryGetHoveredOption(ulong playerId)
    {
        int? hoveredIndex = RunManager.Instance.RestSiteSynchronizer.GetHoveredOptionIndex(playerId);
        if (!hoveredIndex.HasValue)
        {
            return null;
        }

        IReadOnlyList<RestSiteOption> options = RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(playerId);
        int value = hoveredIndex.Value;
        if ((uint)value >= (uint)options.Count)
        {
            return null;
        }

        return options[value];
    }
}
