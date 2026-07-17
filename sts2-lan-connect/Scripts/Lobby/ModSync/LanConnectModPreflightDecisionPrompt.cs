using Godot;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectModPreflightDecisionPrompt
{
    public static async Task<LanConnectModPreflightDecision> ShowAsync(
        Node parent,
        LobbyRoomSummary room,
        LobbyModPreflightResponse response,
        CancellationToken cancellationToken,
        string? desiredSavePlayerNetId = null)
    {
        return await LanConnectModSyncDialog.ShowAsync(
            parent,
            room,
            response,
            desiredSavePlayerNetId,
            cancellationToken);
    }

    internal static string BuildDifferenceSummary(LobbyModPreflightResponse response)
    {
        List<string> parts = [];
        AddCount(parts, "可从 Steam Workshop 获取的缺失项", response.MissingWorkshopMods.Count);
        AddCount(parts, "需要手动处理的缺失项", response.MissingManualMods.Count);
        AddCount(parts, "本机多出的 gameplay MOD", response.ExtraGameplayMods.Count);
        AddCount(parts, "版本不一致项", response.VersionMismatches.Count);
        return parts.Count == 0 ? "未发现 gameplay MOD 差异。" : string.Join("\n", parts);
    }

    private static void AddCount(List<string> parts, string label, int count)
    {
        if (count > 0)
        {
            parts.Add($"{label}：{count}");
        }
    }
}
