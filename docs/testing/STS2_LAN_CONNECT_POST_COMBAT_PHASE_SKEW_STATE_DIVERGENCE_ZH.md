# STS2 LAN Connect 战后阶段错位导致 StateDivergence 调查记录

日期：2026-07-27

状态：已定位，暂未修复

优先级：后续联机稳定性修复

## 摘要

一次 MuMu Android 房主与电脑端真人联机测试在战斗结束时触发了游戏原生
`StateDivergence`。两端使用相同的游戏数据和兼容基线，本次问题不是版本、模组内容或
随机数算法不同，而是双方以不同速度处理同一套战斗结算流程：远端已进入奖励和地图选择，
Android 房主仍在完成回合开始与战斗结束。

奖励选择消息本身会在房主尚未创建对应奖励集合时被缓冲；随后到达的地图投票没有同等的
阶段屏障，被房主在仍处于战斗状态时立即执行。校验系统因此比较了两个不同处理阶段的状态，
将暂时的进度差判定为永久数据分歧并断开远端玩家。

本问题与同分支完成的房间聊天 HUD 回归修复相互独立。聊天修复已通过本轮双端实机验收；
本文仅记录后续需要单独解决的联机状态同步问题。

## 测试环境

- Android：MuMu 模拟器，房主。
- 远端：电脑端真人玩家，通过大厅邀请进入房间。
- 大厅服务器：魔仙堡，`http://101.35.217.99:8788`。
- 房间：`P7FinalReCompat052051`。
- 房间 ID：`175577da-47c6-48c9-bb5a-0e11f24fec16`。
- 连接策略：`relay-only`。
- 游戏数据：双方一致，均以此前版本的兼容基线运行当前游戏版本。
- Android 游戏版本：`v0.109.1`，commit `c8c577f6`。
- LAN Connect：`0.5.2.0`。
- Android 日志：
  `files/instances/profile-sts2-v0.109.1-c8c577f6-016c6df717d9/logs/godot.log`。

## 日志时间线

日志使用运行时内部时间；关键窗口为 `06:55:17` 至 `06:55:25`。

1. Android 房主依次生成校验 `179`、`180` 和 `181`：
   - `after player turn phase two end`
   - `After enemy turn start`
   - Living Fog 执行 `SUPER_GAS_BLAST_MOVE`
   - `After enemy turn end`
2. 远端已完成随后的战斗结束流程，并发送奖励集合 `7` 的选择结果。
3. 房主尚未创建奖励集合 `7`，`RewardsSetSynchronizer` 正确记录：

   ```text
   Buffering RewardSelectedMessage because RewardsSet id 7 hasn't been created yet
   ```

4. 远端完成奖励后发送下一节点投票：

   ```text
   NetVoteForMapCoordAction act 0 coord (2, 12)->MapVote (gen: 1 coord: (2, 13))
   ```

5. `RunLocationTargetedMessageBuffer` 只看到消息仍属于同一
   `act 0 coord (2, 12) room 0`，于是立即交给 `ActionQueueSynchronizer`。房主执行该投票并生成
   校验 `182`。
6. 校验 `182` 失败：

   ```text
   State divergence detected!
   Local: 3717301937
   Remote: 2855182244
   ```

7. 远端以 `StateDivergence` 原因断开后，房主继续处理原本尚未完成的本地流程：

   ```text
   After player turn start
   CHARACTER.IRONCLAD fought ENCOUNTER.LIVING_FOG_NORMAL ... WON
   Combat state becomes NotInCombat
   ```

## 状态转储解释

校验失败时，房主状态仍包含 `MONSTER.LIVING_FOG`，生命为 `12/176`；远端状态已经没有该
怪物，并已进入奖励与地图选择。两端牌堆位置和 `CombatTargets` RNG 计数也不同，但这些差异
符合“远端已经处理下一阶段、房主尚未处理”的时间顺序，不是相同阶段内的确定性计算结果不同。

断开后房主随即完成 `After player turn start`、战斗胜利和 `NotInCombat`，进一步证明异常窗口是
阶段错位，而不是永久状态内容不一致。

## 已排除项

- **双方游戏数据或兼容基线不同**：双方使用相同数据和相同兼容方式。
- **大厅服务中断**：事发前后房间心跳持续返回 `200`。
- **明显网络故障**：事发时延迟约 `92-95 ms`，统计丢包接近零。
- **人数难度缩放差异**：本局只有两名玩家，有效人数在相关配置两侧均为 `2`。
- **奖励消息本身未缓冲**：日志证明缺失奖励集合时已有缓冲逻辑。

延迟和 Android 处理速度会放大阶段窗口，但它们是触发条件，不应成为数据分歧的充分原因。

## 根因假设

当前最强假设是战斗结束与地图选择之间缺少主机侧阶段屏障：

- `RunLocationTargetedMessageBuffer` 的位置粒度只有 act、地图坐标和 room index。
- 战斗、战斗奖励和同一房间后的地图投票共享相同位置标识。
- `RewardSelectedMessage` 有“奖励集合尚未创建”的专用缓冲。
- `NetVoteForMapCoordAction` 没有“主机尚未退出战斗或尚未进入地图选择”的缓冲。
- 地图投票因此越过仍未完成的本地战斗结算，并在错误阶段生成 checksum。

## 后续修复方向

不要通过关闭或放宽 `StateDivergence` 校验掩盖问题。优先在远端地图投票的主机接收/入队路径
增加基于游戏阶段的延迟执行：

1. 收到 `NetVoteForMapCoordAction` 时，若房主仍在战斗或尚未进入可地图投票阶段，先缓存消息。
2. 房主进入 `NotInCombat`、建立本地奖励集合并完成必要结算后，再按原顺序入队投票。
3. 缓存必须按玩家和到达顺序处理，并防止重复执行。
4. 房间位置改变、玩家断开或 run 结束时清理失效缓存。
5. checksum 只能在投票真正越过阶段屏障后生成，不能在缓存时生成或全局跳过。

具体 Harmony 切入点需要结合当前 STS2 程序集确认；候选位置包括
`ActionQueueSynchronizer.HandleRequestEnqueueActionMessage` 前的请求过滤，或地图投票 net action 的
专用接收路径。

## 回归门禁

后续修复至少需要覆盖：

- 房主延迟停留在战斗结算、远端提前完成奖励并投票时，不触发 `StateDivergence`。
- `RewardSelectedMessage` 与地图投票均只应用一次，顺序保持不变。
- 房主进入地图选择后，正常即时投票路径不增加可感知延迟。
- 玩家断开、换房间或 run 结束时不会重放旧投票。
- 真实 Android 房主 + 电脑远端，经魔仙堡 relay 完成同类战斗、奖励和下一节点选择。
- 日志新增“收到 / 延迟 / 释放地图投票”及当时战斗阶段，便于确认屏障生效。
