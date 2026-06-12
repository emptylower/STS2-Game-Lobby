using System;
using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectAccessibilityAnnouncements
{
    public static string BuildRoomCardAnnouncement(
        string roomName,
        string hostName,
        int currentPlayers,
        int maxPlayers,
        bool requiresPassword,
        bool canJoin,
        string? joinDisabledReason,
        bool isSelected,
        string? gameModeLabel)
    {
        List<string> parts = new();
        if (isSelected)
        {
            parts.Add("已选中");
        }

        parts.Add($"房间 {Clean(roomName, "未命名")}");
        parts.Add($"房主 {Clean(hostName, "未知")}");
        parts.Add($"人数 {currentPlayers}/{maxPlayers}");

        if (!string.IsNullOrWhiteSpace(gameModeLabel))
        {
            parts.Add(gameModeLabel.Trim());
        }

        if (requiresPassword)
        {
            parts.Add("需要密码");
        }

        if (!canJoin)
        {
            parts.Add(string.IsNullOrWhiteSpace(joinDisabledReason) ? "当前不可加入" : joinDisabledReason.Trim());
        }
        else
        {
            parts.Add("可加入");
        }

        return string.Join("，", parts);
    }

    private static string Clean(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
