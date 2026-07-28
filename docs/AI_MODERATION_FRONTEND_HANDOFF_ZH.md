# AI 语义审核前端交接文档

> 适用版本：客户端与 lobby-service `0.5.4+`。当前实现已进入源码构建，GitHub Release 暂未发布。

后端、内嵌管理面板和 Godot 聊天客户端已经完成接入。本文作为后续维护与专项验收交接，记录现有状态投影、接口和必须保持的交互约束。

后端完整字段和错误码以 [AI_MODERATION_BACKEND_API_ZH.md](./AI_MODERATION_BACKEND_API_ZH.md) 为准。

## 管理面板现状与维护约束

现有子面板已在登录态、Cookie 和 CSRF 请求封装上提供“AI 语义审核”区域，数据源为 `GET /server-admin/moderation/ai`。

现有配置控件：

- 启用开关 `enabled`。
- 协议菜单：Responses、Chat Completions、Anthropic Messages，对应后端枚举值。
- 完整请求地址输入框 `endpoint`。
- 模型输入框 `model`。
- API Key 密码输入框。页面永远无法读取旧 Key；空白保存必须使用 `apiKeyAction=keep`，填写新值使用 `replace`，单独的清除命令使用 `clear`。
- “允许访问私网模型”开关 `allowPrivateNetwork`，开启时明确这是自托管端点选项。
- “兼容提示词 JSON 回退”开关 `jsonFallbackEnabled`。
- 测试按钮调用 `POST /server-admin/moderation/ai/test`，测试成功后展示协议模式和延迟；测试不自动保存。

保存前端约束：

- 开启时 endpoint、model 和 API Key 必须完整。
- GET 返回 `apiKeyConfigured=true` 时，未修改 Key 的表单必须提交 `keep`，不能提交空字符串覆盖。
- `credentialStatus=missing_master_key` 时禁止启用，并提示运维配置环境变量。
- `credentialStatus=decrypt_error` 时要求重新填写 Key；不要尝试展示或恢复旧密钥。
- 所有 PATCH、POST、DELETE 通过现有安全请求封装附加 `X-CSRF-Token`。

运行状态区域读取 `health`、`cache` 和 `reviews`：

- 状态：ready、熔断、活动请求、队列深度。
- 计数：开始、放行、阻止、降级、三类缓存命中。
- 延迟与最近错误。最近错误没有原消息，不需要请求额外聊天数据。
- 待复审数、永久白名单数和永久黑名单数。

人工复审界面：

- 列表调用 `GET /server-admin/moderation/reviews?status=pending&limit=50`，使用 `nextCursor` 翻页。
- 展开详情时才调用 `GET /reviews/:id` 获取解密证据。
- 详情与列表可展示 `aiVerdict`、`aiUsage`、`reasonCode` 和 `structuredMode`，不得将这些字段解释为人工批准状态。
- 批准必须让管理员明确选择“整个词”或“仅此语境”，不要预选全局词级放行；推荐默认 `context`。
- 拒绝调用 `/reject`，必须明确选择 `context`（默认推荐）或 `term`；成功后立即形成对应永久黑名单并刷新候选、状态和黑名单列表。
- 永久白名单页调用 `/allowlist`，显示 scope、规范化词、创建时间和来源复审 ID，并提供撤销按钮。
- 永久黑名单页调用 `/blocklist`，字段和翻页方式与白名单一致，并提供确认撤销按钮。
- 候选和规则均显示 `surface`，避免把聊天语境误解为房间名或用户名规则。
- 证据为 `null` 时显示已处理或已过期，不当作接口失败。

## Godot 聊天客户端现状与维护约束

客户端内部仍保存 Pending/Reviewing 项，但公共列表投影会在最终 ACK 前隐藏它们；维护时不得让待审原文提前出现在公共聊天栏。

建议每个频道维护以下本地状态：

```text
Idle -> Sending -> Reviewing -> Delivered
                    |
                    +-> Rejected
                    +-> Failed/Retryable
```

具体行为：

1. 发送 `chat_send` 或 `room_chat_v2` 后，只在本地保存 `clientMessageId`、原内容和发送时间，不插入公共列表。
2. 收到 `chat_review_pending` 或 `room_chat_review_pending` 后，在输入框上方显示小浮窗“在审核中”。浮窗关联 `clientMessageId`，不能影响其他频道。
3. 收到对应 ACK 后关闭浮窗。公共消息应由现有广播帧统一插入，避免 ACK 与自广播造成重复。
4. 收到 `content_blocked` 后关闭浮窗并弹窗“包含违禁词，请稍后再试”，不保留失败消息在公共列表。
5. 收到 `moderation_busy` 后保留输入内容供用户稍后重试，不要创建第二个审核浮窗。
6. AI 故障时后端会直接 ACK 并广播打码内容。客户端无需显示服务异常，也不要用本地原文覆盖服务端内容。
7. 连接断开、切换服务器、离开房间或 room session 更新时，清理对应待审浮窗和本地待发送状态。
8. 收到 `chat_messages_redacted` 时，按 `messageIds` 从公共频道已确认列表中幂等删除；收到 `room_chat_messages_redacted` 时，先校验当前 `roomId + roomSessionId`，再删除房间消息。

待审帧字段：

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

房间类型名为 `room_chat_review_pending`。`reviewId` 只用于诊断，不是重试凭据；重试仍使用原 `clientMessageId` 和完全相同的内容。

跨消息撤回帧详见后端接口文档。撤回不产生额外错误弹窗；触发审核的当前消息仍通过 `content_blocked` 使用既有“包含违禁词”弹窗。客户端不得只隐藏当前消息而保留被服务端标记的上文。

## 房间名与用户名审核交互

- 创建房间时，进度状态显示“正在审核房间名与用户名”，避免把模型等待误认为卡死。
- 加入/预检阶段显示“正在审核用户名并检查 MOD 兼容性”。
- 创建请求返回 `content_blocked` 时，使用游戏原生 `NErrorPopup` 样式显示“包含敏感词，请修改房间名或用户名后重试。”，不展示原始 HTTP 错误。
- 加入、MOD 预检或用户名相关请求返回 `content_blocked` 时，使用相同游戏弹窗显示“包含敏感词，请修改用户名后重试。”。
- `details.surface` 可用于诊断和将来的定向文案，当前 UI 不显示后端内部原因。

## 兼容与文案

- 后端协议版本仍为 1，现有 ACK、广播和错误帧外壳未改变。
- 未升级客户端会忽略未知待审帧，随后照常收到 ACK 或错误。
- 服务器频道与房间频道的待审状态必须隔离。
- 旧版 `room_chat` 不会进入待审流程，不需要新增处理。
- 必需中文文案只有“在审核中”和“包含违禁词，请稍后再试”；其他网络错误沿用现有客户端文案体系。

## 前端验收场景

1. 清洁消息：没有待审浮窗，按现有 ACK/广播流程显示一次。
2. 首次命中且 AI 放行：先显示“在审核中”，公共列表无消息；放行后浮窗消失，原文显示一次。
3. 同一消息再次发送：命中安全缓存，不出现待审浮窗，直接发送。
4. AI 阻止：浮窗消失，显示违禁词弹窗，公共列表无消息。
5. AI 超时或 429：最终显示服务端打码消息，不显示模型错误弹窗；管理面板计入降级和最近错误。
6. 待审期间再次发送：收到 `moderation_busy`，原待审消息不受影响。
7. 相同 ID 重发相同内容：只恢复待审或最终结果，不产生第二次公共消息。
8. 相同 ID 改内容：收到 `duplicate_message`。
9. 房间在审核期间关闭、换代或禁用聊天：不显示过期消息，按最终错误清理浮窗。
10. 管理员批准语境规则后立即生效；批准词级规则后，聊天和名称检查均放行；撤销后立即恢复审核/拒绝。
11. 页面刷新后 API Key 输入框保持空白，但 `apiKeyConfigured` 状态正确。
12. 错误主密钥、缺少主密钥和熔断状态均能在管理区域明确区分。
13. 管理员拒绝候选后，永久黑名单立即出现；相同词/语境再次提交时不出现 AI 待审，直接阻止；撤销后恢复审核。
14. 同一认证用户依次发送“习”“近”“平”“下”“台”：一旦组合语义被阻止，当前项消失，上方相关已确认消息也被撤回；另一用户的短消息不应被合并或删除。
15. 房间撤回帧来自旧 `roomSessionId` 时必须忽略；当前代际帧应立即移除对应消息。
16. 敏感房间名或用户名经 AI 判定为正常语义时可继续创建/加入；拒绝时只显示游戏面板样式的敏感词提示。
