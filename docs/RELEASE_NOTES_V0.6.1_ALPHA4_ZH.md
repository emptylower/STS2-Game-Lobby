# STS2 LAN Connect v0.6.1-alpha.4 发布说明（内部预览）

- 日期：2026-09-05
- 版本：`0.6.1-alpha.4`（客户端与 lobby-service 同步；tail 房间 `minimumClientVersion` 仍为 `0.6.1-alpha.1`）
- **本版为 alpha 预览：GitHub-only Pre-release，不更新 Steam 创意工坊。**
- lobby-service 代码与 alpha.2 / alpha.3 相同，只对齐版本号；已部署 alpha.2+ 服务端的节点可不升级。

## 一、修复：与 RitsuLib 0.5.18 共存时新协议房间无法加入（房主 10 秒 LobbyJoinTimeout / 加入方“模组不匹配”）

alpha.1 起所有“进不去新协议房间”的反馈（Windows / Android 加入方握手成功后静默，10 秒后被房主以 `LobbyJoinTimeout` 踢出，
或直接显示“模组不匹配”）在本机双实例复现并做了对照：两端都装 RitsuLib 必现，两端都不装则加入成功。

机制：RitsuLib 0.5.18 会给 `NetMessageBus.SerializeMessage<InitialGameInfoMessage>`（以及 LobbyBeginRun / StateDivergence）打 Harmony 补丁，
Harmony 为其生成的替换体是优化编译的，会把小结构体 `InitialGameInfoMessage.Serialize` 直接内联进去；本 Mod 挂在该方法上的
“容器生产”钩子被绕过，扩展帧从未发出，加入方一直等不到。无 RitsuLib 时该方法走分层编译不内联，所以正常。
同一机制也是 alpha.1 兼容房“双方准备后黑屏”的原因（LobbyBeginRun 的 5-bit 名单计数转译器被绕过）。

alpha.4 起：
- 桌面平台把 9 个容器生产钩子改挂到 `NetMessageBus.SerializeMessage<T>` 本身（安卓因运行时限制保持原样）；
- 若 RitsuLib 先于本 Mod 加载并已占用该方法，则对 InitialGameInfo / LobbyBeginRun 回退旧钩子并在日志告警——**请把 STS2 LAN Connect 排在 RitsuLib 之前加载**；
- 传输层改为按内容匹配待发扩展帧（原包为前缀，后面允许第三方 trailer）；
- 配对屏障超时改为定时触发，扩展帧缺失在 2 秒内以 `lan_extension_missing` 明确报错，不再沉默到房主踢人。

## 二、说明

- alpha.3 补齐的拒绝码表在本版才真正派上用场：如仍失败，加入方现在会看到具体错误码，请连同双方 `godot.log` 一起反馈。
- 兼容房（`compat_4_5_v1`）不受影响。

## 三、升级与回滚

- 客户端覆盖安装即可；服务端可不升级。
- 回滚到 alpha.3：客户端覆盖回 alpha.3（与 RitsuLib 共存时新协议房间将再次无法加入）。

## 四、发布前验收

- ✅ `scripts/verify-release.sh`——见 GitHub Release 正文。
- ✅ 本机双实例 E2E：两端均装 RitsuLib 0.5.18，新协议房间加入成功——见 GitHub Release 正文。
- ✅ 本机 0.111.0 新协议建房 / 0.107.1 兼容房建房冒烟——见 GitHub Release 正文。
