using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectPopupUtil
{
    public static void ShowInfo(string body)
    {
        NErrorPopup? popup = NErrorPopup.Create("STS2 LAN Connect", LanConnectUiText.NormalizeForDisplay(body), showReportBugButton: false);
        if (popup != null)
        {
            NModalContainer.Instance?.Add(popup);
        }
    }
}
