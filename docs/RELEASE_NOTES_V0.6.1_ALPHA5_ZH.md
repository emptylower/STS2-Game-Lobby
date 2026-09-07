# STS2 LAN Connect v0.6.1-alpha.5（alpha.4 反馈修复：新协议房间存档异常 / 续局无法恢复 / QuickSL 失效）

发布日期：2026-09-07

> 状态：**GitHub-only Pre-release**；**不更新 Steam 创意工坊**。tail 房间 `minimumClientVersion` 仍为 `0.6.1-alpha.1`。lobby-service 代码与 alpha.2 / alpha.3 / alpha.4 相同，仅对齐版本号，已部署 alpha.2+ 服务端的节点可不升级。

## 反馈现象（alpha.4，Windows / 0.111.0 / RitsuLib 0.5.18 + QuickSL 1.8.0）

1. 中途"保存并退出"后，续局时弹"无法确认多人存档上次使用的是大厅还是 LAN"；选"恢复大厅房间"报"‘兼容旧版 Mod’房间不能启用 RitsuLib"，无法恢复进度开房。
2. QuickSL 多人快速 SL 失效：客机收到同步重载指令后一秒内被房主 `Quit` 断线，报"等待主机开始载入被取消"。
3. 日志里每次存档都有 `Failed to save run: LanConnectProtocolException: Unknown protocol carrier enum value 3`。

## 根因

0.6.1 把新协议房间的载体改成了 `native_bus_v1`（枚举值 3），但 `LanConnectProtocolCarrier.ToWireValue` 只映射了旧的三个值，`NativeBusV1` 落到 `throw`。房主在新协议房间里每次存档，`SaveManager.Saved` 事件处理器写房间绑定时都会抛异常：

- 原版存档文件本身已写出，但异常进入原版 "Failed to save run" 分支，并沿 RitsuLib / JmcModLib 的存档后置链继续传播。
- 房间绑定从未写入（`binding=missing`），续局时无法判断上次是大厅还是 LAN；选"恢复大厅房间"后走兼容档案兜底，本机装了 RitsuLib 就被 `ritsulib_not_allowed_in_compat_mode` 拒绝。
- QuickSL 的同步重载在房主侧先存档再重载，同一异常打断了重载，房主随即断开所有客机。

## 本版变更

- `ToWireValue` 补上 `NativeBusV1 => "native_bus_v1"`（`ParseCarrier` 一侧原本就支持）；新增枚举全值往返测试。
- 存档事件处理器增加异常兜底 `LanConnectSaveEventGuard`：MOD 内部持久化失败只记录 `sts2_lan_connect save_binding: persist failed source=save_event, ...` 告警，不再把异常抛进原版存档管线。

## 验证（本机双实例，两端均装 RitsuLib 0.5.18，0.111.0，测试节点 sts2-test）

| 场景 | 结果 |
|------|------|
| 新协议房间开局后存档 | ✅ `save_binding: persisted ... hostChannel=lobby`，绑定文件 `ProtocolCarrier=native_bus_v1`，无 `Failed to save run` |
| 房主"保存并退出"后 → 多人模式 → 读档多人游戏 | ✅ 不再弹"无法确认…"，自动恢复大厅房间（`continue_run_publish: publish succeeded`），`stored protocol selection rejected` 0 次 |
| 队友从"游戏大厅"重新加入续局房间并开始 | ✅ 续局房间显示"可接管 1 个角色槽位"，加入后 `LobbyBeginLoadedRunMessage`，双方回到同一局 |
| QuickSL 1.8.0 + JmcModLib 1.9.0，房主发起多人快速 SL，客机同意 | ✅ `多人快速 SL 完成，RequestId=2`，重载期间两次存档均 `save_binding: persisted`，双方保持连接 |
| `scripts/verify-release.sh` | ✅ lobby-service 608/608，xUnit 1156，ProtocolPlan 11，GdUnit 全绿，双次打包哈希一致 |

## 测试者注意事项

反馈日志里房主与加入者是**同一台电脑、同一个 Steam 账号开的两个实例**，两者共用 `current_run_mp.save` 和 `godot.log`：一个实例"放弃多人存档"会把另一个实例的存档一起删掉。双开自测时请把第二个实例切到不同的存档槽位（存档 2）。

## 资产

| 文件 | SHA-256 |
|------|---------|
| `sts2_lan_connect-release.zip` | `c6db72743d0aeccf0b9e9620c20ed4b3eb32514778cb815e48ea1eee514d5986` |
| `sts2_lobby_service.zip` | `5ee251131ce8fbf23ea41f371646346901b6f70bcb4c7695f00301c7b9515e9e` |

变更记录：`CHANGELOG.md`。
