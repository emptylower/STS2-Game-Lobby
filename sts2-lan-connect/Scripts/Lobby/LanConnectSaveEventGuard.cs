using System;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectSaveEventGuard
{
    public static bool Run(string source, Action body, Action<string> log)
    {
        try
        {
            body();
            return true;
        }
        catch (Exception ex)
        {
            string failureSegment = ex is LanConnectProtocolException protocolException
                ? $", failureCode={protocolException.Failure.Code}"
                : string.Empty;
            log?.Invoke(
                $"sts2_lan_connect save_binding: persist failed source={source}, error={ex.GetType().Name}: {ex.Message}{failureSegment}");
            return false;
        }
    }
}
