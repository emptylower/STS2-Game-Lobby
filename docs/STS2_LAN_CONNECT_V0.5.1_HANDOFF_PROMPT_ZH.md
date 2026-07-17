# STS2 LAN Connect v0.5.1 新对话交接 Prompt

将下面整段复制到新的 Codex 对话：

```text
你现在负责完整实施 STS2 LAN Connect v0.5.1 的“加入前 MOD 兼容预检 + Steam Workshop 自动同步”升级。

仓库：
/Users/mac/Desktop/STS2-Game-Lobby

唯一执行计划：
/Users/mac/Desktop/STS2-Game-Lobby/docs/STS2_LAN_CONNECT_V0.5.1_MOD_SYNC_PLAN_ZH.md

参考 PR：
https://github.com/emptylower/STS2-Game-Lobby/pull/38

开始前必须：
1. 完整阅读执行计划和 sts2-lan-connect-dev skill。
2. 检查 git status、origin/main 和当前版本；以最新 main 为基线创建 feat/mod-sync-0.5.1。
3. 不直接 merge/cherry-pick PR #38。它当前有编译失败、TryGet 反射签名错误、默认 relaxed 流程不触发、固定 5 秒停止下载轮询等问题，只能参考产品思路。
4. 不修改、删除或提交现有 .omc/、.playwright-mcp/、.superpowers/ 和未跟踪 docs/superpowers/ 文件。

必须遵守的产品边界：
- 同一房间游戏版本必须完全一致；版本不同直接提示并中止，不能通过 MOD 同步绕过。
- 只比较 affects_gameplay=true 的 MOD，以及 gameplay MOD 的必要 dependency。
- 普通非联机 MOD 不提示、不禁用、不影响加入。
- relaxed 模式保留用户明确选择后的“仍然尝试加入”。
- 自动同步仅允许 Steam Workshop 订阅和用户确认后的选择性禁用，绝不从房主、lobby-service 或任意 URL 下载 DLL/PCK/ZIP。
- 所有订阅和禁用必须明确确认；多余 MOD 默认不勾选。
- Android、非 Steam 或 SteamAPI 不可用时只提供差异清单和手动处理，不显示虚假自动同步状态。
- MOD 改动必须重启游戏；不得尝试热加载。
- 一个 v0.5.1 客户端包仍要分别支持游戏 0.107.1、0.108.0、0.109.0，但跨游戏版本房间必须拦截。
- v0.5.1 client/service 必须与 v0.5.0 对端安全降级兼容。

执行方式：
- 严格按计划 Phase 0 到 Phase 8 顺序执行，每个 gate 通过后才能进入下一阶段。
- 每项使用 TDD：先写失败测试并确认失败，再做最小实现，跑 focused tests 和 regression tests，然后提交。
- 使用计划中的文件边界、DTO、64 KiB/64 项限制、私有 mod-preflight 接口和 feature flag，不另建平行协议。
- 房主 MOD inventory 必须保持私有，不得进入 /rooms、peer gossip、聊天、公开 health 或完整日志。
- Workshop fileId 必须作为十进制字符串传输，不能使用 JavaScript number。
- 把房主提供的 Workshop 元数据视为不可信：下载前查 Steam AppID/真实元数据，下载后验证实际 manifest ID。
- 重启恢复不得落盘房间密码或 token，并复用已有导航 in-flight lock/debounce，防止重复打开菜单。
- 遇到 STS2 版本 API 漂移时使用受约束的运行时解析并做 IL 外部成员引用审计。

测试与发布：
- 完成服务端 npm check/test、客户端 xUnit、GdUnit、完整 verify-release.sh。
- 完成 0.107.1、0.108.0、0.109.0 分版本同版本联机测试及跨版本拦截测试。
- 完成双 Steam 客户端的缺失 MOD、dependency、多余 gameplay MOD、non-gameplay 差异、取消、断网、超时、错误版本和重启恢复场景。
- 确定性产物同步必须使用 rsync -a --checksum --delete 并 diff -qr，避免标准化 mtime 导致旧文件未覆盖。
- 客户端和服务端都升到 0.5.1，并生成两个新的 Release 资产；不得覆盖 v0.5.0。
- 先把候选 service 部署到 ssh sub2api-tencent 的 /opt/sts2-lobby-test，保留 .env、状态和 0.5.0 回滚包。
- 在测试服务器与双客户端实测完成前，不合并 main、不创建正式 v0.5.1 tag/Release。到达此验收门槛后暂停并向我报告测试结果、候选包 SHA-256、剩余风险和需要我执行的实测步骤。

协作要求：
- 直接开始 Phase 0，不要只重复计划。
- 持续给出简短进度更新。
- 遇到计划内的构建、测试或兼容问题可以直接修复；不得通过删除测试、降低验证强度或扩大 relaxed 边界绕过问题。
- 每个阶段提交独立、可审查的 commit，最终给出 commit 列表、测试证据和部署状态。
```
