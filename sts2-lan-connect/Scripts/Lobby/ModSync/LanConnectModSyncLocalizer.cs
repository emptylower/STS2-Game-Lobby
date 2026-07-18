namespace Sts2LanConnect.Scripts;

internal static class LanConnectModSyncLocalizer
{
    public static string Title(LanConnectModSyncViewKind kind, string? locale = null)
    {
        bool english = locale?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
        return english ? EnglishTitle(kind) : ChineseTitle(kind);
    }

    public static string Message(LanConnectModSyncViewKind kind, string? locale = null)
    {
        bool english = locale?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
        return english ? EnglishMessage(kind) : ChineseMessage(kind);
    }

    public static string Action(LanConnectModSyncAction action, string? locale = null)
    {
        bool english = locale?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
        return english ? EnglishAction(action) : ChineseAction(action);
    }

    private static string ChineseTitle(LanConnectModSyncViewKind kind) => kind switch
    {
        LanConnectModSyncViewKind.Checking => "检查 gameplay MOD",
        LanConnectModSyncViewKind.GameVersionMismatch => "游戏版本不匹配",
        LanConnectModSyncViewKind.Compatible => "MOD 兼容",
        LanConnectModSyncViewKind.AutomaticSync => "从 Steam Workshop 同步",
        LanConnectModSyncViewKind.ManualAction => "需要手动处理",
        LanConnectModSyncViewKind.ExtraGameplaySelection => "选择要禁用的 gameplay MOD",
        LanConnectModSyncViewKind.Progress => "正在同步 MOD",
        LanConnectModSyncViewKind.RestartRequired => "需要重启游戏",
        LanConnectModSyncViewKind.UnsupportedPlatform => "当前平台仅支持手动处理",
        _ => "MOD 兼容预检"
    };

    private static string EnglishTitle(LanConnectModSyncViewKind kind) => kind switch
    {
        LanConnectModSyncViewKind.Checking => "Checking gameplay MODs",
        LanConnectModSyncViewKind.GameVersionMismatch => "Game version mismatch",
        LanConnectModSyncViewKind.Compatible => "MODs compatible",
        LanConnectModSyncViewKind.AutomaticSync => "Sync from Steam Workshop",
        LanConnectModSyncViewKind.ManualAction => "Manual action required",
        LanConnectModSyncViewKind.ExtraGameplaySelection => "Choose gameplay MODs to disable",
        LanConnectModSyncViewKind.Progress => "Syncing MODs",
        LanConnectModSyncViewKind.RestartRequired => "Restart required",
        LanConnectModSyncViewKind.UnsupportedPlatform => "Manual action only on this platform",
        _ => "MOD compatibility preflight"
    };

    private static string ChineseMessage(LanConnectModSyncViewKind kind) => kind switch
    {
        LanConnectModSyncViewKind.GameVersionMismatch => "游戏版本不同，不能通过 MOD 同步绕过。",
        LanConnectModSyncViewKind.Compatible => "gameplay MOD 已完全兼容。",
        LanConnectModSyncViewKind.AutomaticSync => "确认后仅通过 Steam Workshop 订阅缺失项。",
        LanConnectModSyncViewKind.ManualAction => "这些项目不能自动下载，请手动安装或调整。",
        LanConnectModSyncViewKind.ExtraGameplaySelection => "默认不禁用任何 MOD；仅处理你主动勾选的项目。",
        LanConnectModSyncViewKind.Progress => "请等待 Steam 完成订阅、下载和安装验证。",
        LanConnectModSyncViewKind.RestartRequired => "MOD 已改变。退出并重新启动后会恢复本次加入。",
        LanConnectModSyncViewKind.UnsupportedPlatform => "Android、非 Steam 或 SteamAPI 不可用时不会自动下载。",
        _ => "正在比较房主与本机的 gameplay MOD。"
    };

    private static string EnglishMessage(LanConnectModSyncViewKind kind) => kind switch
    {
        LanConnectModSyncViewKind.GameVersionMismatch => "MOD sync cannot bypass a game version mismatch.",
        LanConnectModSyncViewKind.Compatible => "Gameplay MODs are fully compatible.",
        LanConnectModSyncViewKind.AutomaticSync => "With confirmation, missing items are subscribed through Steam Workshop only.",
        LanConnectModSyncViewKind.ManualAction => "These items cannot be downloaded automatically.",
        LanConnectModSyncViewKind.ExtraGameplaySelection => "No MOD is disabled by default; only selected items are changed.",
        LanConnectModSyncViewKind.Progress => "Waiting for Steam subscription, download, and install verification.",
        LanConnectModSyncViewKind.RestartRequired => "MODs changed. Restart to resume this join.",
        LanConnectModSyncViewKind.UnsupportedPlatform => "Android, non-Steam, or unavailable SteamAPI requires manual action.",
        _ => "Comparing host and local gameplay MODs."
    };

    private static string ChineseAction(LanConnectModSyncAction action) => action switch
    {
        LanConnectModSyncAction.Join => "加入房间",
        LanConnectModSyncAction.ApplyChanges => "处理所选项目",
        LanConnectModSyncAction.Cancel => "取消",
        LanConnectModSyncAction.Retry => "重试",
        LanConnectModSyncAction.Restart => "退出游戏并在重启后继续",
        LanConnectModSyncAction.ContinueRelaxed => "仍然尝试加入（可能失败）",
        _ => string.Empty
    };

    private static string EnglishAction(LanConnectModSyncAction action) => action switch
    {
        LanConnectModSyncAction.Join => "Join room",
        LanConnectModSyncAction.ApplyChanges => "Apply selected changes",
        LanConnectModSyncAction.Cancel => "Cancel",
        LanConnectModSyncAction.Retry => "Retry",
        LanConnectModSyncAction.Restart => "Quit and resume after restart",
        LanConnectModSyncAction.ContinueRelaxed => "Try joining anyway (may fail)",
        _ => string.Empty
    };
}
