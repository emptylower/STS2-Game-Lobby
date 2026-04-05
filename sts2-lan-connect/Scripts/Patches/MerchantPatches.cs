using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Sts2LanConnect.Scripts;

internal static class MerchantPatches
{
    private const int VanillaMultiplayerHolderCount = 4;
    private const float MerchantForwardShiftX = 160f;
    private const float MerchantForwardShiftY = 35f;
    private const float MerchantRowStartOffsetX = -110f;
    private const float MerchantRowStepY = -40f;
    private const float MerchantColumnStepX = -230f;

    public static void Apply(Harmony harmony)
    {
        MethodInfo? afterLoaded = AccessTools.Method(typeof(NMerchantRoom), "AfterRoomIsLoaded");
        if (afterLoaded != null)
        {
            harmony.Patch(afterLoaded, postfix: new HarmonyMethod(typeof(MerchantPatches), nameof(AfterRoomIsLoadedPostfix)));
        }

        Log.Info("sts2_lan_connect gameplay: merchant patches applied.");
    }

    // ReSharper disable UnusedMember.Local

    private static void AfterRoomIsLoadedPostfix(NMerchantRoom __instance)
    {
        RepositionMerchantVisuals(__instance.PlayerVisuals);
    }

    // ReSharper restore UnusedMember.Local

    private static void RepositionMerchantVisuals(IReadOnlyList<NMerchantCharacter> visuals)
    {
        if (visuals.Count <= VanillaMultiplayerHolderCount)
        {
            return;
        }

        int rowCount = visuals.Count <= VanillaMultiplayerHolderCount * 2
            ? 2
            : Mathf.CeilToInt((float)visuals.Count / VanillaMultiplayerHolderCount);
        int columnCount = Mathf.CeilToInt((float)visuals.Count / rowCount);

        int visualIndex = 0;
        for (int row = 0; row < rowCount; row++)
        {
            float x = MerchantForwardShiftX + MerchantRowStartOffsetX * row;
            float y = MerchantForwardShiftY + MerchantRowStepY * row;
            for (int column = 0; column < columnCount && visualIndex < visuals.Count; column++)
            {
                NMerchantCharacter character = visuals[visualIndex];
                character.Position = new Vector2(x, y);
                x += MerchantColumnStepX;
                visualIndex++;
            }
        }
    }
}
