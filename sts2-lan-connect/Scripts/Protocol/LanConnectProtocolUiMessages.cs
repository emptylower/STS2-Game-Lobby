namespace Sts2LanConnect.Scripts;

internal static class LanConnectProtocolUiMessages
{
    public static string Describe(LanConnectProtocolFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Code switch
        {
            "client_update_required" or "lan_client_version_too_old" =>
                string.IsNullOrWhiteSpace(failure.RequiredClientVersion)
                    ? "客户端版本过旧，请更新后重试。"
                    : $"客户端版本过旧，请更新到 {failure.RequiredClientVersion} 或更高版本。",
            "protocol_profile_unsupported" => "当前客户端不支持该房间的联机协议。",
            "ritsulib_not_allowed_in_compat_mode" => "“兼容旧版 Mod”房间不能启用 RitsuLib。请关闭 RitsuLib 后重试，或改用新协议房间。",
            "ritsulib_presence_mismatch" => failure.RequiredRitsuLibPresent == true
                ? "该房间是旧版本创建的，要求所有玩家启用 RitsuLib；新协议房间不再有此限制。"
                : "该房间是旧版本创建的，要求所有玩家关闭 RitsuLib；新协议房间不再有此限制。",
            "ritsulib_sidecar_unavailable" => "该房间使用已停用的旧版 RitsuLib 通道，请房主升级 LAN Connect 后重新建房。",
            "lan_legacy_carrier_unsupported" => "该房间由旧版 LAN Connect 创建（旧载体），请房主升级后重新建房。",
            "lan_registry_fingerprint_required" or "lan_registry_fingerprint_mismatch" =>
                "双方的联机消息注册表不一致（通常是 Mod 列表不同），无法使用新协议加入。",
            "lan_native_frame_invalid" or "lan_type_id_mismatch" or "lan_extension_missing" =>
                $"新协议通信帧校验失败（{failure.Code}），连接已停止；请确认双方 LAN Connect 版本一致。",
            "game_version_mismatch" => "游戏版本不匹配，无法加入该房间。",
            "wire_cache_mismatch" => "联机数据版本不匹配，无法加入该房间。",
            "lan_protocol_version_mismatch" => "LAN 协议版本不匹配，无法继续连接。",
            "lan_tail_required" or "lan_tail_malformed" => "房间协议数据无效，连接已停止。",
            "protocol_patch_conflict" => "联机协议补丁未能完整安装（通常与 RitsuLib 的补丁冲突），本次启动联机功能已停用，单机不受影响。\n" +
                "恢复方法：在 MOD 菜单中只关闭 RitsuLib，启动一次游戏到主菜单后退出，再重新开启 RitsuLib。\n" +
                "若仍无法恢复，请把 user://sts2_lan_connect/diagnostics/ 中最新的诊断目录反馈给 MOD 作者。",
            _ => $"联机协议拒绝了本次操作（{failure.Code}）。"
        };
    }

    public static void Present(LanConnectProtocolFailure failure) =>
        LanConnectPopupUtil.ShowInfo(Describe(failure));
}
