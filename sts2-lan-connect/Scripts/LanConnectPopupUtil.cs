using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectPopupUtil
{
    public static void ShowInfo(string body)
    {
        ShowInfo("STS2 LAN Connect", body);
    }

    public static void ShowInfo(string title, string body)
    {
        NErrorPopup? popup = NErrorPopup.Create(
            LanConnectUiText.NormalizeForDisplay(title),
            LanConnectUiText.NormalizeForDisplay(body),
            showReportBugButton: false);
        if (popup != null)
        {
            NModalContainer.Instance?.Add(popup);
        }
    }
}
