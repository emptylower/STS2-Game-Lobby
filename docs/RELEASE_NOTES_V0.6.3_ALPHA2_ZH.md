# STS2 LAN Connect v0.6.3-alpha.2 发布说明（内部预览）

- 日期：2026-09-19
- 版本：`0.6.3-alpha.2`（客户端 MOD 与 lobby-service 版本号同步；修复在客户端，服务端仅版本号变化，不改 wire 协议，tail 房间 `minimumClientVersion` 保持 `0.6.2-alpha.1` 不变）
- **本版为 pre-release 预发布（仅 GitHub），不更新 Steam 创意工坊；当前正式版仍为 `0.6.2`。**

## 一、修的是什么：PC 作「兼容旧版 Mod」房主，开局后客机卡在选人界面、房主黑屏

现象（`0.6.3-alpha.1` 反馈，Windows 房主 + Android 客机；本机 macOS 双实例同样必现）：加入、选人、准备都正常，房主点开始后——

- 客机停在选人界面，日志 `ModelNotFoundException: Model id=CHARACTER.<乱码> not found`，随后两条 `no message handlers are registered`（`SyncPlayerDataMessage` / `SyncRngMessage`），然后断开；
- 房主 `Embarking` 之后永久停在 `Waiting to receive all sync messages from all clients`，画面黑屏。

**这不是 alpha.1 引入的问题：`0.6.1`、`0.6.2` 正式版同样受影响**，只是 `0.6.2` 连旧协议房间都进不去（alpha.1 已修），所以到 alpha.1 才暴露。

原因：旧协议把玩家列表位宽从原版 3 bit 扩到 5 bit，靠改写 `LobbyBeginRunMessage.Serialize` 实现。`0.6.1` 迁移到官方消息通道时，桌面端改为给 `NetMessageBus.SerializeMessage<T>` 打补丁；补丁后的方法由 JIT 全优化编译，会把这个很小的 `Serialize` 按**原始代码**内联进去，位宽改写被绕过。于是桌面房主按 3 bit 写开局消息，客机按 5 bit 读，整条消息错位。同一次迁移还关掉了原本专门防这个问题的「消息总线边界」补丁。

不受影响的组合：新协议房间（原版位宽即正确位宽）；Android 作房主（Android 不走那条补丁路径）；`0.6.0` 及更早。

修复：桌面端恢复消息总线边界补丁，旧协议下由 MOD 显式按 5 bit 写出开局消息；加入响应消息（`ClientLobbyJoinResponseMessage`）处在同样的风险下（目前只是碰巧没被内联），一并处理。新协议房间下补丁直接放行，线上字节逐位不变；Android 路径未改动。

## 二、启动日志自查值变化

桌面端启动日志 `beginRunMessageBusBoundary=` 的正常值由 `skipped_non_generic_plan` 变为 `patched`，并新增 `joinResponseMessageBusBoundary=`。Android 仍为 `skipped_android`。旧协议开局时房主日志会出现 `lobby begin-run forced at message-bus boundary players=…, lobbyListBits=5`。

## 三、兼容性与升级

- **需要房主升级**（问题出在房主写出的字节）；客机版本不限。alpha.1 的修复（旧协议房间可加入、房主名字）仍包含在内。
- 服务端可不升级；如升级，自动更新不接受 pre-release，需手动部署。
- 回滚：重装 `0.6.2` 即可，无数据迁移。
- 暂不升级的规避办法：PC 建房选「新协议」，或由 Android 作房主。

## 四、验收范围

- ✅ 自动测试：客户端单测 1,235 通过（1 项既有跳过）、协议计划 11 通过、大厅服务端 613 通过；Release 构建、0.107.1 类型加载与打包校验通过。
- ✅ 真机 E2E（Mac，游戏 0.111.0，同机双实例、各自独立日志、本地 lobby-service）：
  - 修复前：必现，客机 `CHARACTER.IMBALANCED_POWER not found`，房主永久等待；
  - 修复后旧协议：房主日志出现 join-response 与 begin-run 两条 `forced at message-bus boundary … lobbyListBits=5`，两端种子一致，双向 `Received sync`，客机进入开局事件，无报错；
  - 修复后新协议：无 `forced` 日志（补丁放行），两端种子一致，双向 `Received sync`，无报错。
- ⚠️ 未在 Windows 房主 + Android 客机的原始组合上实测；JIT 内联行为与平台相关，请反馈者用原组合复测，并查看房主日志里是否有上述 `forced` 行。
- ⚠️ 新增的 GdUnit 运行时用例已编译通过但本次未在 Godot 内运行；GdUnit 不在发布门禁内。

## 校验和

- 客户端 ZIP SHA-256：`d67a9f14d769afd3d2d0657227b5b76fc6bfbac907f798e67643e5ae553b3b0d`
- 服务端 ZIP SHA-256：`0ae749eb0914779ddf85987375c32435b2d3dc915439b846b42f7d3faa75b64b`
