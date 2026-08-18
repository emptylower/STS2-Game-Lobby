# STS2 LAN Connect v0.6.0-alpha.7 测试版说明

发布日期：2026-08-18

这是客户端玩家昵称与 RitsuLib 自动 SL 重开返回修复测试版。客户端升级到 `0.6.0-alpha.7`；lobby-service 继续使用 `0.6.0-alpha.6`，已经部署 alpha.6 服务端的服主无需再次升级。同一房间的所有玩家必须统一客户端与游戏版本，更新后完整重启游戏。

## 本轮修复

- 修复房主端把客机显示为数字平台 ID 的问题。大厅控制通道认证玩家身份后，客户端会在 Godot 主线程刷新原生多人等待页和局内玩家列表，显示玩家设置的昵称。
- 修复 RitsuLib 房间中玩家控制绑定与 ENet 连接先后顺序不稳定时的 sidecar 激活竞态。现在两项条件都满足后才激活可信 sidecar 流。
- 修复自动 SL 后由房主点击“房间管理 → 重开一局”时，旧 `RunManager.NetService` 被 RitsuLib 继续观察、导致新会话协议状态错绑的问题。
- 重开期间保留房主续局协议租约，房主与客机返回主菜单后分别清理已断开的旧网络服务；客机随后按原 `desiredSavePlayerNetId` 自动加入新发布的房间。
- 在线玩家状态通过加锁快照读取，避免重开、断线和心跳并发时出现不完整名单。

## 兼容性

- 本版只改客户端，不改变 lobby-service API、房间协议、Relay 或部署配置；服务端继续使用 `0.6.0-alpha.6`。
- 同一房间所有玩家必须统一客户端 `0.6.0-alpha.7`，并在更新 DLL、PCK 与 manifest 后完整重启游戏。
- Ritsu 模式继续要求 macOS 与 Android 全员使用官方 RitsuLib v0.5.13；有/无 RitsuLib 混合组合仍会在 ticket 和 transport 前拒绝。

## 发布验收

- macOS 使用 `--force-steam=off` 启动；Android 使用 Android Studio 管理的 Android 15 ARM64 AVD，双方均加载官方 RitsuLib v0.5.13。
- 房主创建 `tail_v1 / ritsulib_sidecar_v1` 房间，Android 客机加入原存档槽位；macOS 原生玩家列表正确显示客机昵称“鬼神易”。
- 双方进入同一第 2 层战斗后，房主从房间管理执行“重开一局”。旧房间被删除，新房间自动发布，Android 客机自动返回、获取新票据并按原槽位重连。
- 双方再次确认载入并进入同一第 2 层战斗；新房间状态显示 2 名玩家和 2 个已连接存档槽位。
- 自动门禁通过 lobby-service 607 项、客户端 xUnit 1116 项（另有 1 项既有原型测试跳过）和 GdUnit 357 项。

## 下载校验

- `sts2_lan_connect-release.zip`: `cc59f060c457a562d1ee451e9814420f34eb1f0c9b9ea55bd6e292f13f5e5d51`
- 客户端运行时 `sts2_lan_connect.dll`: `795b42f04f923f78780f878a0ac36b635e3d544b533aaf941da16b169a2dac4a`
- 客户端运行时 `sts2_lan_connect.pck`: `b9907bd5a1e2afc1609d589fc3f43c9943f6f0900792ca68fafc154028720597`

## 已知边界

- 本轮 macOS 端到端验收使用 `--force-steam=off` 的 ENet 路径；Steam 房主路径未包含在本轮验证范围内。
- 这是 GitHub Pre-release 测试包，不替代稳定版。遇到异常时请保留双方从启动开始的完整 `godot.log`。

## 更新要求

- 所有同房玩家统一安装客户端 `0.6.0-alpha.7`，删除旧 DLL/PCK 后完整重启游戏。
- lobby-service 继续使用 `0.6.0-alpha.6`；已经升级到 alpha.6 的自建服务端无需重新部署。
- 若仍失败，请同时提交两端从游戏启动开始的完整日志，并注明操作系统、游戏版本、RitsuLib 版本及是否实际加载成功。
