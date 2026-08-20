using System.Collections.ObjectModel;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectStartupStages
{
    public const string ConfigLoad = "config_load";
    public const string ExternalModDetection = "external_mod_detection";
    public const string TailRuntimeConfigure = "tail_runtime_configure";
    public const string SentryCompatibility = "sentry_compatibility";
    public const string AccessibilityBridge = "accessibility_bridge";
    public const string MultiplayerCompatibility = "multiplayer_compatibility";
    public const string GameplayPatches = "gameplay_patches";
    public const string SceneReadyPatches = "scene_ready_patches";
    public const string LobbyRuntime = "lobby_runtime";
    public const string RoomChatOverlay = "room_chat_overlay";

    private static readonly ReadOnlyCollection<string> OrderedStageIds = Array.AsReadOnly(
    [
        ConfigLoad,
        ExternalModDetection,
        TailRuntimeConfigure,
        SentryCompatibility,
        AccessibilityBridge,
        MultiplayerCompatibility,
        GameplayPatches,
        SceneReadyPatches,
        LobbyRuntime,
        RoomChatOverlay
    ]);

    public static IReadOnlyList<string> Ordered => OrderedStageIds;

    public static int GetOrdinal(string stageId)
    {
        for (int index = 0; index < OrderedStageIds.Count; index++)
        {
            if (string.Equals(OrderedStageIds[index], stageId, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 0;
    }
}
