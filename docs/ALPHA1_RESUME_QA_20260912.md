# Alpha 1 续局修复验收（2026-09-12）

## S/L 定义更正

用户明确：S/L 指「房间管理 → 重开一局」及双端自动重连。此前报告误把退出并恢复存档称为 S/L。原实测结论仅覆盖退出恢复。现已另行补测两次真正的「重开一局」，证据与结果见下节；两种流程分别记录。

## 结论与构建

本次指定的 Mac + Android 模拟器建房、加入同房、击败第一个怪物、退出并恢复存档、取消后再次恢复及「重开一局」S/L 均通过；独立代码与原实测证据审查 APPROVE，补测 S/L 功能证据审查 PASS（切换期异常见下文）。版本保持 0.6.2-alpha.1，最新本地构建位于 `/Users/mac/Desktop/STS2-alpha1-resume-20260912/`。未发布外部预发布或创意工坊更新。

- 客户端：`client/sts2_lan_connect-release.zip`
- ZIP SHA-256：`8d0dc09d415b061cd4e6c2bf6a01b4421b2a59e1b1918d024e7a81a9603bee55`
- DLL SHA-256：`9396e995bbfef02cf01172a9debb5899bcb65f402fbb47e4e98c16b18aa200d9`
- Mac、Android 已安装 DLL 与包内 DLL 完全相同；重新执行打包验证生成相同 ZIP 校验和。保留这份实际测试的产物，不在验收后另换二进制。
- 产物构建时 HEAD 为 e1b42ff，包含本次随后提交的 RitsuLib 握手修复；构建日志与哈希是产物溯源依据。

## 修复与回滚

开始前先提交 cba4604 保存已有工作，回滚分支 `codex/rollback-alpha1-before-resume-review`；原始 Alpha 1 基线 736a88c。e1b42ff 补齐续局取消及迟到发布的隔离。见 `ALPHA1_RESUME_ROLLBACK_20260912.md`。

根因是缓存的读档子页面退出时只隐藏，不触发 TreeExiting，已完成标记未清除。按页面访问代次重置发布状态，并隔离取消后迟到的提示、建房结果，允许同一进程重试。实际跨端测试又发现遗留 RitsuLib 相等检查误拒绝加入；移除该过时检查，保留协议版本及帧完整性验证。

## 实际操作证据

本地证据目录：`.omc/artifacts/alpha1-resume-20260912/`。日志不纳入 Git，以免混入运行环境信息。

| 项目 | 实测结果 | 证据 |
|---|---|---|
| 建房与加入 | Mac 主机、Android 原角色加入同房 44f82a81-2130-43ff-811a-b2afe951b7b9 | logs/rooms-joined-final.json、logs/macos-final-launch.log |
| 首怪 | 种子 4HNAY89W0MTZ，双铁甲，第三回合击败毛绒伏地虫，双方进入奖励页，80/80 HP | screenshots/android-first-monster-victory.png、android-rewards.png；Mac 日志 Combat ended |
| 保存退出 | 首怪后保存退出，正常断开与清理房间 | logs/rooms-after-save-exit.json |
| 取消后再次恢复 | 连续恢复三次，前两次取消后房间列表为空，第三次重新发布 | logs/rooms-resume-1.json 至 rooms-resume-3.json；rooms-cancel-1.json、rooms-cancel-2.json |
| 无需重启 | Mac PID 11114 自建房至全部恢复保持不变 | 同一份 macos-final-launch.log、进程检查 |
| 退出并恢复存档 | 第三次恢复后 Android 接管相同 netId 3470906098995623213，双方加载阶段 1、楼层 2、FinishedCombat 奖励页 | logs/rooms-resume-3-joined.json；screenshots/android-restored-slot.png、android-loaded.png |

读档遵循游戏原有房间保存点：奖励尚未写入下一房间存档，恢复后重新显示同一奖励；未修改保存机制。双端最终日志未检出 StateDivergence 或 checksum mismatch。测试结束再次正常保存退出。

## 「重开一局」S/L 补测

补测使用同一份已验收 Alpha 1 二进制，Mac + Android 模拟器；Mac PID 16433 在两次重开之间保持不变。证据目录：`.omc/artifacts/alpha1-sl-20260912/`。本轮只补充测试和报告，未修改运行时代码或替换构建。

| 场景 | 操作与观察 | 证据 |
|---|---|---|
| 首怪奖励阶段重开 | 房主点击「房间管理 → 重开一局」，主机自动恢复房间，Android 自动重新加入原角色；只确认提示和准备，双方回到奖励页 | screenshots/android-sl1-auto.png、android-sl1-loaded.png；logs/rooms-before-sl1.json、rooms-after-sl1.json |
| 战斗中途再次重开 | 进入楼层 3，房主打击使中型史莱姆 61→55 HP；再次点击同一重开按钮，双端自动恢复，敌人回到 61 HP，房主能量由 2 回到 3 | screenshots/android-sl2-before.png、android-sl2-auto.png、android-sl2-reset.png |
| 重开后继续战斗 | 双方各打一张打击，中型史莱姆同步为 49/61 HP；双方结束回合，正常进入第 2 回合，两人均 76/80 HP | screenshots/android-sl2-play.png、android-sl2-turn2.png；logs/macos-sl.log、android-sl.log |

两次重开均没有手动进入大厅选房或选择角色。房间 ID 依次由 `9f0d75f9-8235-410a-a4df-94254a9329ff` → `c59a7182-ae59-40da-909a-2967c96df0e3` → `b83e8b48-d6de-42a8-9f1d-41db4d1cacdd`；每次快照只有一个房间、两名玩家。saveKey 保持 `27c62e99249b8ce413cc29de5625ecf36b07147a6814c1a36c69314cdee8546f`，Android desiredNetId 始终为 `3470906098995623213`，种子保持 `4HNAY89W0MTZ`。最后正常保存退出，`logs/rooms-after-cleanup.json` 为空。

功能场景实测通过；切换期并非零错误日志：Mac 两次、Android 第二次出现游戏 `NMultiplayerNetworkProblemIndicator.UpdateLoop` 的 NullReferenceException，另有断开期 ENet `Peer not connected` / 空 peer 信息。它们没有阻止本轮自动恢复、双端同步或继续回合；不将其描述为无异常运行。双端日志未检出 StateDivergence 或 checksum mismatch。独立证据审查：功能 PASS，保留切换异常。归因是既有重开清理将 RunManager.NetService 置空，与旧网络指示器异步任务直接读取 NetService.IsConnected 发生竞争；ENet 心跳在客户端先断开后仍发送。属于既有重开生命周期问题，本次补测未改动此代码；不将其简单归因成纯游戏问题。

## 自动检查

- 客户端全量：1172 通过、0 失败、1 个既有跳过（Two_client_stable_monster_id_proof）。
- 最后首次全量运行出现既有聊天重连测试超时：模拟 transport 入列表与 Closed 订阅之间存在竞争。相关文件与原基线相同；定向重跑 3/3，全量重跑 1172/0/1。保留首次失败日志，不隐藏重试。
- 续局专项：38 通过。
- GdUnit 握手运行时专项：26 通过，含双向 RitsuLib 安装差异及错误版本拒绝。
- 大厅服务端 check/test：611 通过。
- verify-release.sh --artifacts-only：通过，输出客户端与服务端相同 ZIP 校验和。
- 日志：client-tests-final-rerun.log、client-tests-final.log、chat-reconnect-targeted.log、runtime-tests.log、verify-release-final.log，以及原 service 测试日志。

## 观察与范围

重复恢复第二次起，服务端 JSON 的 Android save-slot playerName 为空，但 netId、角色接管及实际显示正常。独立审查未发现与本次变更的因果关系；不宣称所有元数据完全不变。现有 Steam 容量补丁启动警告仍存在，本轮使用 ENet 两人链路。此次没有额外重跑旧客户端升级提示 UI 和 compat 房间全流程；验收结论限于上述自动回归与实际跨端场景。
