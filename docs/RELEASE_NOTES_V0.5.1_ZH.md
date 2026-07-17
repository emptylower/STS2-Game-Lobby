# STS2 LAN Connect v0.5.1 发布说明

> 候选说明：在测试服务器、三游戏版本与双 Steam 客户端验收完成前，不创建正式 `v0.5.1` tag 或 Release。

v0.5.1 新增加入前 gameplay MOD 兼容预检。Steam 桌面客户端可在明确确认后通过 Steam Workshop 订阅缺失项；Android、非 Steam 或 SteamAPI 不可用时只显示手动处理项。MOD 安装或启用状态变化后必须重启，客户端会在 15 分钟内恢复服务器与房间，并对密码房重新询问密码。

## 安全边界

- 游戏版本不同始终直接拦截，不能通过 MOD 同步或 relaxed 继续绕过。
- 只处理 `affects_gameplay=true` 的 MOD 及其必要 dependency；普通非联机 MOD 不提示、不禁用、不影响加入。
- 自动获取只使用 Steam Workshop。不会从房主、大厅服务或任意 URL 下载 DLL、PCK、ZIP。
- 多余 gameplay MOD 默认不勾选，只有用户选择并二次确认后才会禁用。
- pending join 不保存房间密码、access token、create token 或 host token。
- v0.5.0 客户端、服务端和旧房主清单继续走既有兼容路径。

## 设计贡献

感谢 Jianbao233（@Bilibili我叫煎包）在 [PR #38](https://github.com/emptylower/STS2-Game-Lobby/pull/38) 提供 AutoModSubscriber 集成与交互思路。v0.5.1 替代实现未 merge 或 cherry-pick 该 PR，不复制其源码，也不硬依赖 AutoModSubscriber。安全审计确认 AMS 的 `ModWorkshopMap` 是客机接收房主 sidecar 后的映射，并非本机 inventory 的权威来源，因此 v0.5.1 不读取该映射、不注册或覆盖 `ExternalDialogHandler`；所有核心能力由原生 Steam provider 独立完成。

完整玩家变化、升级步骤、回滚和发布资产将在 Phase 7 候选包验证后补齐。
