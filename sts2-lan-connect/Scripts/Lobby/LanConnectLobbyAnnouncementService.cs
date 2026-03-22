using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectLobbyAnnouncementService
{
    private static bool _requestedThisLaunch;
    private static bool _popupShownThisLaunch;
    private static string? _pendingTitle;
    private static string? _pendingBody;

    internal static void RequestOnceForLaunch()
    {
        if (_requestedThisLaunch)
        {
            return;
        }

        _requestedThisLaunch = true;
        _ = Task.Run(FetchAndShowAsync);
    }

    internal static void TryShowPending()
    {
        if (_popupShownThisLaunch)
        {
            return;
        }

        string? title = _pendingTitle;
        string? body = _pendingBody;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        if (!LanConnectPopupUtil.TryShowAnnouncement(title, body))
        {
            return;
        }

        _popupShownThisLaunch = true;
        _pendingTitle = null;
        _pendingBody = null;
    }

    private static async Task FetchAndShowAsync()
    {
        try
        {
            LobbyAnnouncementResponse response = await LanConnectLobbyDirectoryClient.GetAnnouncementAsync();
            if (!response.Ok || !response.Visible || response.Announcement == null)
            {
                return;
            }

            string title = string.IsNullOrWhiteSpace(response.Announcement.Title)
                ? "大厅公告"
                : response.Announcement.Title.Trim();
            string body = response.Announcement.Body?.Trim() ?? string.Empty;
            if (body == string.Empty)
            {
                return;
            }

            _pendingTitle = title;
            _pendingBody = body;
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect announcement fetch failed: {ex.Message}");
        }
    }
}
