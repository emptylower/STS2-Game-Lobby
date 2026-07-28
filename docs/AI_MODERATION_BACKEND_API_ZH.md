# AI 语义审核后端接口文档

> 适用版本：lobby-service `0.5.4+`。`0.5.4` 源码与构建已就绪，GitHub Release 暂未发布。

本文档描述 `lobby-service` 的 AI 敏感词语义复核、短期安全缓存、人工复审、永久白名单与永久黑名单接口。后端、内嵌管理面板和 Godot 客户端均已接入。

## 行为概览

公共服务器频道、`room_chat_v2`、房间名、玩家名和续局角色名都会触发语义审核。旧版 `room_chat` 保持确定性打码兼容逻辑。名称类请求在对应 HTTP 操作内等待审核结果，不使用聊天待审帧；审核拒绝统一返回 `400 content_blocked` 和 `details.surface`。

消息处理顺序：

1. 无损规范化消息并扫描敏感词，得到稳定 `termId`、来源、分类和原文区间。
2. 依次检查永久黑名单、永久白名单、30 天精确消息 HMAC 缓存、7 天限定语境 HMAC 缓存。黑名单优先于白名单和缓存。
3. 未覆盖的命中进入 AI 审核，并向发送者发送待审帧。
4. AI 全部放行时发送原文、立即写入两级缓存并聚合人工复审候选。
5. AI 阻止时返回 `content_blocked`，不写历史、不广播。
6. AI 超时、限流、拒答、结构错误、队列满或熔断时，使用 `*` 打码后正常 ACK 和广播；错误只进入服务日志与管理状态。

名称和缓存均按 `surface` 隔离：`chat_message`、`room_name`、`player_name`、`character_name`。词级人工规则跨 surface 全局生效；语境级规则只在相同 surface、相同词和相同规范化语境中生效。

### 跨消息刷屏规避

- 服务端按已认证发送者身份和频道/房间代际隔离上下文，不按可伪造的显示名合并。
- 只跟踪 30 秒内连续短消息（每条最多 4 个 Unicode 字符），最多取当前消息之前 9 条，与当前消息合计最多 10 条。
- 仅当敏感词命中跨越消息边界时才把拼接序列送审，普通长对话不会被拼接。
- 序列被拒绝时，当前消息不发送，相关历史消息从服务端历史移除，并广播撤回帧。被拒绝片段会在短窗口内继续参与后续补字判断，但不会重复发送撤回 ID。
- 跨消息序列属于主动规避路径；AI 降级、未配置或不可用时采取保守拒绝并撤回已有上下文。旧版 `room_chat` 不支持稳定撤回，仍保持原兼容逻辑。

## 管理接口通用约定

- 基址与现有子面板相同，例如 `https://lobby.example/server-admin`。
- 先调用 `POST /server-admin/login`，保存 `sts2_server_admin_session` Cookie 和响应中的 `csrfToken`。
- 所有 GET 只要求会话 Cookie；PATCH、POST、DELETE 还要求 `X-CSRF-Token: <csrfToken>`。
- 所有 `/server-admin/*` 响应带 `Cache-Control: no-store`。
- 通用错误结构为 `{ "code": "...", "message": "..." }`；输入校验失败使用 `400 invalid_request`。
- API Key 只允许在写请求中提交，任何 GET、日志和错误响应都不会返回明文。

## AI 配置与运行状态

### GET `/server-admin/moderation/ai`

响应：

```json
{
  "config": {
    "enabled": false,
    "protocol": "openai_responses",
    "endpoint": "",
    "model": "",
    "allowPrivateNetwork": false,
    "jsonFallbackEnabled": false,
    "apiKeyConfigured": false,
    "credentialKeyConfigured": true,
    "credentialStatus": "missing_api_key"
  },
  "health": {
    "ready": false,
    "started": 0,
    "allowed": 0,
    "blocked": 0,
    "degraded": 0,
    "exactCacheHits": 0,
    "contextCacheHits": 0,
    "permanentAllowHits": 0,
    "permanentBlockHits": 0,
    "active": 0,
    "queued": 0,
    "averageLatencyMs": 0,
    "circuitState": "closed",
    "circuitOpenUntil": null,
    "recentErrors": []
  },
  "cache": { "exactEntries": 0, "contextEntries": 0 },
  "reviews": {
    "pendingReviews": 0,
    "approvedReviews": 0,
    "rejectedReviews": 0,
    "allowRules": 0,
    "blockRules": 0
  }
}
```

`credentialStatus` 取值为 `ready`、`missing_master_key`、`missing_api_key` 或 `decrypt_error`。`recentErrors` 最多 20 条，只含时间、错误码和可选 HTTP 状态。

### PATCH `/server-admin/moderation/ai`

所有字段可选，未提交字段保持现值：

```json
{
  "enabled": true,
  "protocol": "openai_responses",
  "endpoint": "https://api.openai.com/v1/responses",
  "model": "your-model-id",
  "allowPrivateNetwork": false,
  "jsonFallbackEnabled": false,
  "apiKeyAction": "replace",
  "apiKey": "secret"
}
```

- `protocol`：`openai_responses`、`openai_chat_completions`、`anthropic_messages`。
- `endpoint`：完整请求 URL，服务端不会追加路径。
- `apiKeyAction`：`keep`、`replace`、`clear`，默认 `keep`。
- 只有 `replace` 可以同时提交 `apiKey`。
- 启用前必须有有效的 `AI_MODERATION_CREDENTIAL_KEY`、已保存 API Key、endpoint 和 model。
- 默认只允许 HTTPS 公网地址。自托管内网端点需设置 `allowPrivateNetwork=true`；该设置也允许私网 HTTP。

成功响应与 GET 相同。配置先原子落盘，再对新消息生效。

### POST `/server-admin/moderation/ai/test`

测试当前配置，或在请求体中临时覆盖以下字段：

```json
{
  "protocol": "anthropic_messages",
  "endpoint": "https://api.anthropic.com/v1/messages",
  "model": "your-model-id",
  "allowPrivateNetwork": false,
  "jsonFallbackEnabled": false,
  "apiKey": "optional-temporary-key"
}
```

该接口不会保存请求体。省略 `apiKey` 时使用已加密保存的 Key。

成功响应：

```json
{
  "ok": true,
  "decision": "allow",
  "structuredMode": "strict",
  "latencyMs": 842
}
```

`structuredMode` 为 `strict` 或 `prompted_json`。提供商调用失败返回 `502 ai_provider_test_failed`。

## 人工复审、永久白名单与永久黑名单

### GET `/server-admin/moderation/reviews`

查询参数：

- `status`：可选，`pending`、`approved`、`rejected`。
- `cursor`：上一页的 `nextCursor`。
- `limit`：1–100，默认 50。

响应为 `{ "items": [...], "nextCursor": "..." | null }`。候选字段包含：

- `id`、`status`、`surface`、`termId`、`normalizedTerm`、`displayTerm`、`category`、`source`。
- `contextSignature`、`observations`、`firstObservedAt`、`lastObservedAt`。
- `provider`、`model`、`reasonCode`、`aiDecision`、`aiVerdict`、`aiUsage`。
- `aiContextCandidateId`、`structuredMode`、可选 `note`、`evidenceAvailable`。

相同“surface + 词 + 语境签名”的 AI 放行会累计 `observations`，不会重复建项。

### GET `/server-admin/moderation/reviews/:id`

在列表字段之外返回：

```json
{
  "evidence": {
    "message": "原始消息",
    "context": "命中词所在分句"
  }
}
```

证据在磁盘上使用 AES-256-GCM 加密，只在已认证的详情接口中解密。证据保存 30 天，批准、拒绝或到期后变为 `null`。

### POST `/server-admin/moderation/reviews/:id/approve`

```json
{ "scope": "context", "note": "人工确认是正常讨论" }
```

- `scope=term`：该规范化词跨所有 surface 全局永久放行。
- `scope=context`：只放行相同 surface 中，相同规范化词与相同规范化分句签名。
- 规则保存后立即生效，没有 TTL，直到撤销。

成功响应为 `{ "ok": true, "rule": {...} }`。

### POST `/server-admin/moderation/reviews/:id/reject`

```json
{ "scope": "context", "note": "人工确认该语境违规" }
```

- `scope=context`（默认）：创建相同 surface、词和语境的永久黑名单规则。
- `scope=term`：创建跨所有 surface 的全局永久黑名单规则。
- 创建规则时会立即清除对应短期安全缓存；相同内容下次直接阻止，不再请求 AI。

成功响应为 `{ "ok": true, "review": {...}, "rule": {...} }`。

### GET `/server-admin/moderation/allowlist`

查询参数为可选 `scope=term|context`、`cursor`、`limit`，响应同样使用 `items/nextCursor`。

### DELETE `/server-admin/moderation/allowlist/:id`

成功响应 `{ "ok": true }`，规则立即失效。不存在时返回 `404 moderation_allow_rule_not_found`。

### GET `/server-admin/moderation/blocklist`

查询参数为可选 `scope=term|context`、`cursor`、`limit`，响应为 `{ "items": [...], "nextCursor": ... }`。规则字段包括 `id`、`scope`、`surface`、`termId`、`normalizedTerm`、可选 `contextSignature`、`reviewId`、`createdAt` 和可选 `note`。

### DELETE `/server-admin/moderation/blocklist/:id`

成功响应 `{ "ok": true }`，规则立即失效。不存在时返回 `404 moderation_block_rule_not_found`。

## WebSocket 协议

### 公共服务器频道待审帧

```json
{
  "type": "chat_review_pending",
  "protocolVersion": 1,
  "clientMessageId": "uuid",
  "reviewId": "review_request_uuid",
  "startedAt": "2026-07-28T00:00:00.000Z",
  "timeoutMs": 5000
}
```

### 房间 V2 待审帧

字段相同，`type` 为 `room_chat_review_pending`。

待审帧只发给发送者，不进入历史或广播。审核结束后的终态：

- 放行：现有 `chat_ack` / `room_chat_ack`，随后现有消息广播，内容为原文。
- 阻止：现有错误帧，`code=content_blocked`，不广播。
- AI 故障：现有 ACK 和广播，内容中命中的文本被 `*` 打码。
- 同一连接已有待审消息：错误帧 `code=moderation_busy`。
- 重发相同 `clientMessageId` 和内容：重复发送待审帧或最终缓存结果，不再次调用 AI。
- 相同 ID 但内容不同：`duplicate_message`。

旧客户端可以忽略未知待审帧，并继续处理最终 ACK 或错误。`room_chat` 不发送待审帧，也不调用 AI。

### 跨消息撤回帧

公共服务器频道：

```json
{
  "type": "chat_messages_redacted",
  "protocolVersion": 1,
  "messageIds": ["server-message-uuid"],
  "reason": "content_blocked",
  "redactedAt": "2026-07-28T00:00:00.000Z"
}
```

房间 V2 使用 `type=room_chat_messages_redacted`，并额外包含 `roomId` 与 `roomSessionId`。客户端必须按 `messageId` 幂等删除已确认消息；未知或已经删除的 ID 直接忽略。房间客户端只接受当前 room session 的撤回帧。

## 模型结构化输出

服务端为每次请求生成 `match_1...N` 和 `context_1...N`，模型只能引用这些 ID：

```json
{
  "schemaVersion": 1,
  "decision": "allow",
  "reasonCode": "quoted_or_discussed",
  "matches": [
    {
      "matchId": "match_1",
      "verdict": "allow",
      "usage": "quoted_reference",
      "contextCandidateId": "context_1"
    }
  ]
}
```

`reasonCode`：`benign_use`、`quoted_or_discussed`、`ambiguous`、`prohibited_use`。`usage`：`benign_literal`、`quoted_reference`、`negated_or_condemned`、`proper_noun`、`prohibited`、`uncertain`。

缺少命中、重复/未知 ID、未知枚举、额外字段、聚合决策冲突、拒答或截断均按提供商故障处理。模型不会直接生成白名单、正则或规则文本。

## 部署与安全

```dotenv
AI_MODERATION_CREDENTIAL_KEY=<32-byte-hex-or-base64>
AI_MODERATION_STATE_FILE=./data/ai-moderation-state.json
AI_MODERATION_CACHE_FILE=./data/ai-moderation-cache.json
AI_MODERATION_TIMEOUT_MS=5000
AI_MODERATION_MAX_CONCURRENCY=4
AI_MODERATION_MAX_QUEUE=64
AI_MODERATION_REVIEWS_PER_IP_MINUTE=10
```

- 响应体上限 64 KiB，禁止 HTTP 重定向。
- 连续 5 次提供商失败后熔断 30 秒，期间直接打码发送。
- 单连接最多一个待审消息，全局默认并发 4、队列 64、每 IP 每分钟 10 次。
- 模型网络错误、408、429 和 5xx 在总截止时间允许时重试一次。
- 精确消息和语境缓存只保存 HMAC；词级人工规则保存规范化词，语境规则保存签名。
- 主密钥不支持在线轮换。更换主密钥后重新提交 API Key，并删除旧的加密证据/缓存文件；错误密钥会显示 `decrypt_error`，AI 自动退回打码。
