using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectPopupUtil
{
    public static bool TryShowAnnouncement(string title, string body)
    {
        NErrorPopup? popup = NErrorPopup.Create(title, LanConnectUiText.NormalizeForDisplay(body), showReportBugButton: false);
        if (popup == null || NModalContainer.Instance == null)
        {
            return false;
        }

        NModalContainer.Instance.Add(popup);
        NModalContainer.Instance.ShowBackstop();
        return true;
    }

    public static void ShowAnnouncement(string title, string body)
    {
        TryShowAnnouncement(title, body);
    }

    public static void ShowInfo(string body)
    {
        ShowAnnouncement("STS2 LAN Connect", body);
    }
}
