# lobby-service v0.5.4 候选发布说明

> 状态：源码版本与构建版本已更新为 `0.5.4`，GitHub Release 暂未发布。本文件用于发布前验收和后续正式发布交接。

## 本版重点

`0.5.4` 将原有确定性敏感词过滤升级为“词库扫描 + AI 语义审核 + 短期安全缓存 + 人工永久规则”的完整治理链路，同时保持未配置 AI 时的原有打码兼容行为。

- 支持 `openai_responses`、`openai_chat_completions`、`anthropic_messages` 三类协议，管理员填写完整请求 URL、模型和 API Key。
- AI 只返回服务端提供的 `matchId` 与 `contextCandidateId`；未知 ID、缺项、截断、拒答或非法 JSON 均按调用失败处理。
- AI 放行后原文立即发送，并写入 30 天精确消息缓存与 7 天限定语境缓存；永久白名单仍需人工批准。
- 管理员拒绝复审候选后立即生成永久黑名单，可选择“整个词”或“仅此语境”，并可在独立管理页撤销。
- 审核范围覆盖公共服务器频道、`room_chat_v2`、房间名、玩家名和续局角色名；旧版 `room_chat` 保持确定性打码。
- 同一认证用户在同一频道 30 秒内拆分发送的最多 10 条短消息会组合分析；判定违规时拒绝当前消息并撤回关联上文。
- AI 配置、健康指标、错误摘要、人工复审、永久白名单和永久黑名单均已进入 `/server-admin`。

## 故障与安全策略

- 普通单条聊天遇到超时、429、网络错误、结构错误、队列溢出或熔断时，使用现有 `*` 打码后发送。
- 主动拆字规避路径在 AI 不可用时保守拒绝，避免利用故障窗口绕过审核。
- API Key 通过 `AI_MODERATION_CREDENTIAL_KEY` 指定的 32 字节主密钥进行 AES-256-GCM 加密；管理 API 永不回显明文。
- 精确消息和语境缓存只保存 HMAC；人工复审证据加密保存并按 30 天期限清理。
- 请求 URL 默认要求 HTTPS，禁止重定向并限制响应体为 64 KiB；私网访问必须显式允许。

## 升级配置

新增环境变量：

```text
AI_MODERATION_CREDENTIAL_KEY=<32 字节主密钥，hex 或 base64>
AI_MODERATION_STATE_FILE=./data/ai-moderation-state.json
AI_MODERATION_CACHE_FILE=./data/ai-moderation-cache.json
```

升级时必须保留现有 `.env`、`SERVER_ADMIN_STATE_FILE`、`AI_MODERATION_STATE_FILE`、`AI_MODERATION_CACHE_FILE` 和数据目录。更换主密钥不支持在线轮换；旧 Key 无法解密时 AI 自动停用并回退打码，管理员需重新保存 API Key。

完整 REST、WebSocket、错误码和部署字段见：

- [`AI_MODERATION_BACKEND_API_ZH.md`](./AI_MODERATION_BACKEND_API_ZH.md)
- [`AI_MODERATION_FRONTEND_HANDOFF_ZH.md`](./AI_MODERATION_FRONTEND_HANDOFF_ZH.md)

## 兼容性

- 新待审帧和撤回帧为向后兼容扩展；旧客户端可忽略未知帧并继续接收最终 ACK 或错误。
- 旧客户端无法撤回已经显示的拆字上下文，建议公共节点同时推广 `0.5.4` 客户端。
- 未启用 AI 时，名称拒绝、聊天打码、旧版房间聊天和加入协议维持既有行为。
- 客户端与服务端版本号同步为 `0.5.4`，但未创建 GitHub Release 或标签。

## 发布前验证

- `cd lobby-service && npm run check && npm run test`
- `./scripts/build-sts2-lan-connect.sh`
- `dotnet test STS2-Game-Lobby.sln`
- 真实 Godot GdUnit 测试
- `./scripts/verify-release.sh`
- 管理后台实际登录，验证 AI 测试、复审批准/拒绝、永久白名单、永久黑名单及撤销流程
