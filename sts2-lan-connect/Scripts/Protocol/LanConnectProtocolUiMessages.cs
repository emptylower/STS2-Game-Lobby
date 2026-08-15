namespace Sts2LanConnect.Scripts;

internal static class LanConnectProtocolUiMessages
{
    public static string Describe(LanConnectProtocolFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Code switch
        {
            "client_update_required" => string.IsNullOrWhiteSpace(failure.RequiredClientVersion)
                ? "客户端版本过旧，请更新后重试。"
                : $"客户端版本过旧，请更新到 {failure.RequiredClientVersion} 或更高版本。",
            "protocol_profile_unsupported" => "当前客户端不支持该房间的联机协议。",
            "ritsulib_not_allowed_in_compat_mode" => "兼容模式不能启用 RitsuLib。请关闭 RitsuLib 后重试。",
            "ritsulib_presence_mismatch" => failure.RequiredRitsuLibPresent == true
                ? "该房间要求所有玩家启用 RitsuLib。"
                : "该房间要求所有玩家关闭 RitsuLib。",
            "ritsulib_sidecar_unavailable" => "RitsuLib 已启用，但公开 sidecar 通道当前不可用。",
            "game_version_mismatch" => "游戏版本不匹配，无法加入该房间。",
            "wire_cache_mismatch" => "联机数据版本不匹配，无法加入该房间。",
            "lan_protocol_version_mismatch" => "LAN 协议版本不匹配，无法继续连接。",
            "lan_tail_required" or "lan_tail_malformed" => "房间协议数据无效，连接已停止。",
            _ => $"联机协议拒绝了本次操作（{failure.Code}）。"
        };
    }

    public static void Present(LanConnectProtocolFailure failure) =>
        LanConnectPopupUtil.ShowInfo(Describe(failure));
}
