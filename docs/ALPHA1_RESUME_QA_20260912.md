# Alpha 1 续局修复验收（2026-09-12）

## 结论与构建

本次指定的 Mac + Android 模拟器建房、加入同房、击败第一个怪物、S/L、取消后再次恢复均通过；独立代码与实测证据审查 APPROVE。版本保持 0.6.2-alpha.1，最新本地构建位于 `/Users/mac/Desktop/STS2-alpha1-resume-20260912/`。未发布外部预发布或创意工坊更新。

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
| S/L | 第三次恢复后 Android 接管相同 netId 3470906098995623213，双方加载阶段 1、楼层 2、FinishedCombat 奖励页 | logs/rooms-resume-3-joined.json；screenshots/android-restored-slot.png、android-loaded.png |

读档遵循游戏原有房间保存点：奖励尚未写入下一房间存档，恢复后重新显示同一奖励；未修改保存机制。双端最终日志未检出 StateDivergence 或 checksum mismatch。测试结束再次正常保存退出。

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
