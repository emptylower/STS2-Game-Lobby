# 敏感词过滤设计（lobby-service 0.5.3）

**日期：** 2026-07-27
**目标版本：** lobby-service 0.5.3 正式版（基于 main 分支，即 0.5.2 + 管理面板 + 自动升级）
**词库来源：** [konsheng/Sensitive-lexicon](https://github.com/konsheng/Sensitive-lexicon)（MIT）

## 1. 背景与目标

大厅不允许出现敏感词内容，覆盖范围：大厅聊天、房间聊天、用户名/昵称、房间名、续局槽位名称等所有用户可控的展示性文本。聊天内容命中时自动用 `*` 替代；名称类字段命中时拒绝。

本功能是 0.5.3 最后一次功能升级，完成后发布 0.5.3 正式版。设计约束：求稳、不破坏既有协议兼容性、保持零外部运行时依赖（当前仅 express/ws）。

## 2. 已确认的决策

| 决策点 | 结论 |
|---|---|
| 词库接入方式 | Vendor 快照进仓库（`lobby-service/lexicon/`），随 npm 包 / release 分发，离线可部署 |
| 命中策略 | 聊天消息打码（`敏感词` → `***`）；用户名/房间名等名称类字段拒绝 |
| 匹配强度 | 轻量归一化匹配（NFC、全半角、大小写折叠、剔除空白/标点/零宽字符、连续重复字符压缩），防「插入符号/空格」绕过 |
| 管理面板 | 开关（默认开）+ 状态展示（词库条数、累计打码数、累计拒绝数），即时生效 |
| 选词范围 | 游戏场景精选：政治、反动、色情、暴恐、涉枪涉爆、广告、非法网址、GFW 补充、综合/补充；排除疫情、民生、贪腐、新思想启蒙 |
| 拒绝提示文案 | 「包含敏感词内容，请修改后重试」 |
| 开发分支 | main（0.5.3 预发布工作 `claude/musing-napier-814d3b` 已并入 main，无需再合并；`fix/lan-lobby-continue-channel` 为客户端分支，独立继续） |

方案选型：自研 DFA 过滤模块（否决了引入 npm 敏感词包、外挂 Go 检测服务两个备选）。

## 3. 架构与模块

```text
lobby-service/
├─ lexicon/                        # vendor 词库快照，随包分发
│  ├─ politics.txt                 # ← 政治类型 + 反动词库
│  ├─ porn.txt                     # ← 色情类型 + 色情词库
│  ├─ violence.txt                 # ← 暴恐 + 涉枪涉爆
│  ├─ ads.txt                      # ← 广告类型 + 非法网址
│  ├─ misc.txt                     # ← GFW补充 + 补充/其他/网易/腾讯 合并去重
│  └─ SOURCES.md                   # 上游 commit hash、类别取舍说明、MIT 许可声明
└─ src/moderation/
   ├─ normalize.ts                 # 归一化器（产出归一化串 + 原文索引映射）
   ├─ dfa.ts                       # Trie 构建与扫描匹配
   ├─ lexicon-loader.ts            # 启动时读 lexicon/ → 构建 Trie，返回词数
   ├─ filter.ts                    # SensitiveFilter 对外 API + 命中统计
   └─ *.test.ts                    # 同目录 node:test 单测
```

### 3.1 归一化管线（normalize.ts）

对输入文本依次：NFC → 全角转半角 → 英文小写折叠 → 剔除空白/标点/符号/零宽字符 → 连续重复字符压缩。每个归一化后的字符记录其在原文中的索引；命中后把原文对应区间（含中间被剔除的符号）整体替换为 `*`。例：`「敏 感」→「***」`、`「M  G  党」` 可命中。

### 3.2 匹配器（dfa.ts）

经典 Trie，从每个起点逐字符扫描，复杂度 `O(文本长 × 最长词长)`。聊天上限 300 字符、名称 80 字符、词库数万条——无需 Aho-Corasick 失效指针。词表词入 Trie 前过同一归一化管线，两端口径一致。

### 3.3 对外 API（filter.ts）

```ts
interface SensitiveFilter {
  contains(text: string): boolean;                       // 名称类字段用（拒绝）
  mask(text: string): { text: string; masked: boolean }; // 聊天用（打码）
  readonly wordCount: number;
  stats(): { maskedMessages: number; rejectedNames: number };
}
```

另提供 `NullSensitiveFilter`（开关关闭时的 no-op 实现）。内部持有可替换的 `current` 引用，面板切换开关时整体替换，挂接点无 if-else。

## 4. 入口挂接点（已在 main 分支验证）

### 4.1 聊天消息（打码）

- 收口点：`src/chat/protocol.ts` 的 `canonicalizeContent`（`canonicalizeChatContent` :259 与 `canonicalizeRoomContent` :266 共用），只处理 text segment，emoji 等结构化 segment 不动。
- 顺序：先做现有结构校验（300 字符 / 32 segments 预算），通过后打码。打码为等量替换，不改长度，不影响 wire 预算与去重。
- 打码后内容进历史 buffer 并广播给所有人（含发送者本人），历史快照存打码版。命中计入 `maskedMessages`。
- 注入：网关持有 filter 引用，作为可选参数传入 canonicalize 函数（默认不过滤，现有测试签名不破坏）。打码不抛错、不新增 wire 帧。

### 4.2 名称类字段（拒绝）

| 入口 | 位置（main 分支） | 行为 |
|---|---|---|
| 建房 `roomName` / `hostPlayerName` | `src/app.ts:506,508` `boundedString` 之后 | `InputError` → 400 `invalid_request`，`details.reason: "sensitive_content"` |
| 加入房间 `playerName` | `src/app.ts:594` | 同上 |
| 续局槽位 `characterName` / `playerName` | `src/app.ts:544` 续局路由 | 同上 |
| 聊天票据 `playerName` | `POST /chat/tickets` 路由层，白名单校验后 | 同上 |
| 房间 WS hello 昵称 | `src/chat/room-gateway.ts:301,390` 昵称规范化后 | 复用现有握手拒绝路径，不新增 wire 错误码 |
| MOD 预检 `playerName` | `src/app.ts:618` 预检路由 | 同 HTTP 拒绝 |

错误 message 统一为「包含敏感词内容，请修改后重试」。命中计入 `rejectedNames`。

### 4.3 明确不过滤

- 管理员侧 `displayName` / `announcements`（管理员鉴权，信任级别不同）
- 房间密码、`gameMode`/`version`/`modVersion`/`modList` 等结构化或预设字段
- 仅写日志的连接事件字段

### 4.4 存量数据

开关打开时已在场的房间名/昵称不追溯（房间有 TTL 心跳，自然过期）；新建/续写一律过检。

## 5. 配置与管理面板

**环境变量（`src/config.ts`，仅首次种子值）：**

| 变量 | 默认 | 说明 |
|---|---|---|
| `SENSITIVE_FILTER_ENABLED` | `true` | 首次启动的开关种子值 |
| `SENSITIVE_LEXICON_DIR` | 包根 `lexicon/` | 词库目录覆盖（测试/自定义部署用） |

**运行时状态：** `ServerAdminStateStore` 新增 `sensitiveFilterEnabled: boolean`，持久化进 `data/server-admin.json`；env 只种子第一次，之后以面板为准。`PATCH /server-admin/settings`（`app.ts:802`）白名单加入该字段，改完即时生效（关闭即换成 `NullSensitiveFilter`）。

**面板 UI（`server-admin-ui.ts`）：** 设置区新增「敏感词过滤」行：开关 checkbox（走现有 settings PATCH + CSRF 流程）+ 状态展示（`词库 N 词 · 已打码 X 条消息 · 已拒绝 Y 个名称`）。数据来自 `GET /server-admin/settings` 响应新增的 `sensitiveFilter: { enabled, wordCount, maskedMessages, rejectedNames }`。

**降级：** 启动时词库缺失/为空/读取失败 → error 日志 + 以 0 词状态继续运行（fail-open，不阻断启动），面板显示「词库加载失败」。

**统计口径：** 自进程启动起累计，重启清零，不持久化。

## 6. 测试策略

**单元测试（`src/moderation/*.test.ts`，node:test）：**

- `normalize`：NFC / 全半角 / 大小写折叠 / 符号剔除 / 重复压缩 / 索引映射正确性（含 emoji、中英混合）
- `dfa`：构建、命中、空词表、无命中
- `filter`：打码位置精确（夹符号情形）、`contains`、统计计数、`NullSensitiveFilter` no-op、词表与输入同一归一化口径
- `lexicon-loader`：临时目录加载、注释空行去重、缺目录 fail-open 返回 0 词

**集成测试（沿用 `app.integration.test.ts` 模式，注入小词表目录）：**

- 建房房间名命中 → 400 + `details.reason` + 中文提示；加入/票据/续局/预检名称命中同上
- 大厅与房间 WS 聊天：命中消息 → 全员（含发送者）收到打码版
- 房间 WS hello 昵称命中 → 握手被拒（现有拒绝路径）
- 面板 PATCH 关开关 → 同名可建、消息不打码；再开恢复；GET settings 返回 `sensitiveFilter` 状态
- 回归：`npm run check && npm test` 全绿

## 7. 打包与发版

- `package.json` 的 `files` 加 `lexicon/`；`scripts/package-lobby-service.sh` 同步 `lexicon/` 进 release 镜像；不直接改 `releases/`
- `lexicon/SOURCES.md` 记录上游 commit、类别取舍、MIT 许可声明
- 版本号 bump 至 0.5.3（main 当前为 0.5.2）
- 注意与自动升级（service-update）的兼容：词库随包分发，升级即换新词库，无额外迁移

## 8. 非目标（YAGNI）

- 繁简转换、拼音/谐音等强对抗匹配
- 面板自定义词/白名单管理
- 命中趋势图、统计持久化
- 存量房间/昵称的追溯清理
- 客户端侧过滤（过滤权威在服务端）

## 9. 实施期修订（2026-07-27，已随实现落地）

1. **ASCII 词边界规则**：真实词库含短 ASCII 词（`da`、`av`、`sm`、`b` 等），纯子串匹配会误伤 "have"/"small"/"standard"。规则：命中词首/尾是 ASCII 字母数字时，外侧相邻字符不得也是 ASCII 字母数字。**间隙感知**：相邻字符在原文中被剔除字符（空白/标点）隔开时视为存在边界（"av movie"、"a v" 命中；"have"、"testing" 不命中）。已知代价："da vinci" 会命中 `da`（词库垃圾词所致，可接受）。
2. **NFC 下标契约**：`normalizeForMatch` 的原文下标相对 NFC 串；`filter.mask` 先转 NFC 再套用 span，非 NFC 输入返回其 NFC 形式的打码结果。
3. **既有测试与生产词库解耦**：测试夹具名（"standard"、"close-test"）会被真实词库误伤，集成/单测默认指向空词库目录（fail-open），过滤行为一律用显式小词表测试。
4. **WS hello 拒绝机制**：用既有 `sendError("invalid_message")` + return（非终态，可用正确名字重试），而非计划中的 throw——既有 catch 会把 throw 统一压成 protocol_mismatch 终态。
5. **package.json 无 `files` 字段**：分发走打包脚本白名单（EXPECTED_MANIFEST + package-content.test.ts），lexicon/ 已加入清单，功能等价于 spec §7 的 files 方案。
