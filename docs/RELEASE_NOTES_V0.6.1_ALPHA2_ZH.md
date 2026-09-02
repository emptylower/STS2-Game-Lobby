# STS2 LAN Connect v0.6.1-alpha.2 发布说明（内部预览）

- 日期：2026-09-02
- 版本：`0.6.1-alpha.2`（客户端与 lobby-service 同步；tail 房间 `minimumClientVersion` 仍为 `0.6.1-alpha.1`，alpha.1 客户端可继续加入 alpha.2 房间）
- **本版为 alpha 预览：GitHub-only Pre-release，不更新 Steam 创意工坊。**

## 一、协议措辞：只分「兼容旧版 Mod」与「新协议」

0.6.1 起协议载体已切换为 `native_bus_v1`（游戏官方 Mod 消息注册通道），新协议与是否安装 RitsuLib 无关，
因此前端不再出现“Ritsu 联机协议 / 兼容协议”的旧划分：

| 位置 | alpha.1 | alpha.2 |
|------|---------|---------|
| 建房按钮 | 兼容协议 / Ritsu 联机协议 | **兼容旧版 Mod** / **新协议** |
| 新协议说明 | 依赖 RitsuLib 措辞 | 通过官方 Mod 消息注册通道传输；需 0.6.1 及以上客户端；与是否安装 RitsuLib 无关 |
| 房间卡片 / 详情 | 含 RitsuLib 标签 | 兼容房：「兼容旧版 Mod / 旧版协议 / 不支持 RitsuLib」；新协议房：「新协议 / 官方通道」，不再显示 RitsuLib 标签 |
| 加入门禁提示 | — | “需要游戏测试分支（0.111+）” |

线上标识 `compat_4_5_v1` / `tail_v1` 与 selection / capability digest 字段均不变。

## 二、修复：新协议房间不再要求 RitsuLib 安装状态一致

- lobby-service `assertJoinerCompatible` 与客户端 `LanConnectProtocolSelection.Validate` 删除 tail_v1 房间的
  `ritsulib_presence_mismatch` 门禁；房主装了 RitsuLib、加入者没装（或反之）可以正常加入新协议房间。
- 兼容房（`compat_4_5_v1`）继续禁止 RitsuLib，行为不变。
- **该修复需要服务端一并升级到 0.6.1-alpha.2**：旧服务端仍会在签发加入工单时返回 409 `ritsulib_presence_mismatch`。
  服务端自动更新只接受非 pre-release 版本，alpha 服务端需运维手动安装 `sts2_lobby_service.zip`。

## 三、错误提示补齐

`LanConnectProtocolUiMessages` 补齐 0.6.1 新错误码的中文提示：`lan_legacy_carrier_unsupported`、
`lan_registry_fingerprint_required` / `lan_registry_fingerprint_mismatch`、`lan_client_version_too_old`、
`lan_native_frame_invalid`、`lan_type_id_mismatch`、`lan_extension_missing`。

## 四、已知问题（本版未修复）

- 0.111.0 上加入新协议房间仍可能显示“模组不匹配”：加入方的 `LobbyJoinRequest` 扩展帧未送达时，
  `LanConnectRejectionCodec` 尚未识别 `lan_extension_missing` / `lan_native_frame_invalid` / `lan_type_id_mismatch`，
  错误被降级为原版 ModMismatch。根因仍在排查。
- 兼容房在 0.111.0 上双方准备后黑屏（房主 3-bit 名单计数 vs 加入方 5-bit）的 Windows 触发条件仍未复现。

## 五、升级与回滚

- 客户端覆盖安装；服务端部署 `lobby-service` 0.6.1-alpha.2 并重启（保留 `.env` 与 `data/`）。
- 回滚到 alpha.1：客户端覆盖回 alpha.1 即可；服务端回滚后新协议房间会重新要求 RitsuLib 状态一致。

## 六、发布前验收

- ✅ `scripts/verify-release.sh`：lobby-service 608/608，xUnit、GdUnit、ProtocolPlan 全部通过；客户端与服务端包各两次独立打包 SHA-256 一致。
- ✅ 测试节点 `sts2-test.43.133.192.249.nip.io` 已部署 0.6.1-alpha.2（`/probe` 声明 `native_bus_v1`）。
- ⏳ 本机 0.111.0 / 0.107.1 建房冒烟（见 GitHub Release 正文更新）。
