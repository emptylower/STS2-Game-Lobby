# STS2 LAN Connect v0.5.1 发布说明

> 候选说明：在测试服务器、三游戏版本与双 Steam 客户端验收完成前，不创建正式 `v0.5.1` tag 或 Release。

v0.5.1 新增加入前 gameplay MOD 兼容预检。Steam 桌面客户端可在明确确认后通过 Steam Workshop 订阅缺失项；Android、非 Steam 或 SteamAPI 不可用时只显示手动处理项。MOD 安装或启用状态变化后必须重启，客户端会在 15 分钟内恢复服务器与房间，并对密码房重新询问密码。

## 玩家变化

- MOD 完全一致时预检无感通过，继续原有 join ticket 与直连/relay 流程。
- 缺少 Workshop gameplay MOD 时先显示 Steam 返回的标题、发布者、大小与目标版本；只有确认后才订阅，可取消、失败后重试或改为手动处理。
- 缺少手动 MOD 或没有有效 Workshop ID 时仅显示安装清单，不提供其他下载来源。
- 多余 gameplay MOD 默认全部不勾选；只有用户选择并通过二次确认后才修改本机启用状态。
- relaxed“仍然尝试加入”保留为次要操作，只适用于 MOD 差异。游戏版本不同没有该入口。
- 安装或禁用后必须重启。公开房在 pending 有效期内恢复并重新预检；密码房恢复房间和槽位后重新询问密码。

同一客户端包继续支持游戏 `0.107.1`、`0.108.0` 与 `0.109.0`。v0.5.0 服务端、客户端或旧房主不提供结构化清单时，v0.5.1 会回退既有加入流程，不改变旧 `/join` 契约。

## 安全边界

- 游戏版本不同始终直接拦截，不能通过 MOD 同步或 relaxed 继续绕过。
- 只处理 `affects_gameplay=true` 的 MOD 及其必要 dependency；普通非联机 MOD 不提示、不禁用、不影响加入。
- 自动获取只使用 Steam Workshop。不会从房主、大厅服务或任意 URL 下载 DLL、PCK、ZIP。
- 多余 gameplay MOD 默认不勾选，只有用户选择并二次确认后才会禁用。
- pending join 不保存房间密码、access token、create token 或 host token。
- v0.5.0 客户端、服务端和旧房主清单继续走既有兼容路径。

## 服务端升级

1. 保留当前 `.env`、`SERVER_ADMIN_STATE_FILE`、peer state 与 v0.5.0 service 压缩包。
2. 用新的 `sts2_lobby_service.zip` 完整替换程序文件并执行 `npm ci`、`npm run check`、`npm run test`。
3. 环境模板新增 `MOD_SYNC_ENABLED=false`、`MOD_SYNC_MAX_DESCRIPTORS=64`、`MOD_SYNC_MAX_PAYLOAD_BYTES=65536`。不要调高两个硬上限。
4. 先在测试节点显式设置 `MOD_SYNC_ENABLED=true`。确认 `/probe` 返回 `modSyncProtocolVersion: 1` 与 `modSyncEnabled: true`。
5. 验证密码校验、公开 `/rooms` 不含 inventory、预检不增加人数/状态且不返回 ticket，再执行双客户端验收。

## 客户端升级

1. 关闭游戏，完整覆盖安装 `sts2_lan_connect-release.zip`。
2. 确认 `mods/sts2_lan_connect/sts2_lan_connect.json` 为 `0.5.1`，DLL、PCK、JSON 必须来自同一候选包。
3. 启动后确认原大厅、频道和房间聊天仍可用；选择 MOD 不一致房间验证预检对话框。
4. 不要把 pending 文件当作凭据存储。它只保存有限的服务器/房间/槽位上下文，并在 15 分钟、取消、成功、房间消失或服务器切换后清理。

## 回滚

- 服务端先设置 `MOD_SYNC_ENABLED=false` 并重启，客户端会立即回退原加入流程；如需完全回滚，再恢复保留的 v0.5.0 service 包与原 `.env`/状态文件。
- 客户端严重故障时完整卸载 v0.5.1，再安装历史 v0.5.0 Release。不要覆盖或移动 v0.5.0 tag/资产。
- MOD 已由用户确认订阅或禁用时，版本回滚不会自动撤销这些本机 Steam/启用状态变更；应由用户在 Steam Workshop 或 MOD 设置中手动恢复并重启。

## 候选资产

- `sts2_lan_connect-release.zip`
- `sts2_lobby_service.zip`

两个资产必须由 `scripts/verify-release.sh` 在临时目录重新构建并验证；正式发布前还需测试节点、双 Steam 客户端、Android fallback 与三游戏版本验收。

## 设计贡献

感谢 Jianbao233（@Bilibili我叫煎包）在 [PR #38](https://github.com/emptylower/STS2-Game-Lobby/pull/38) 提供 AutoModSubscriber 集成与交互思路。v0.5.1 替代实现未 merge 或 cherry-pick 该 PR，不复制其源码，也不硬依赖 AutoModSubscriber。安全审计确认 AMS 的 `ModWorkshopMap` 是客机接收房主 sidecar 后的映射，并非本机 inventory 的权威来源，因此 v0.5.1 不读取该映射、不注册或覆盖 `ExternalDialogHandler`；所有核心能力由原生 Steam provider 独立完成。
