# lobby-service v0.5.3 发布说明

发布日期：2026-07-27

v0.5.3 服务端与客户端 `0.5.3` 同步发布于同一 GitHub Release。聊天与加入线协议完全兼容，旧版客户端可不升级继续使用；客户端 `0.5.3` 的变更（LAN 与大厅续局通道拆分、存档保护、聊天 HUD 化等）见 [`RELEASE_NOTES_V0.5.3_CLIENT_ZH.md`](./RELEASE_NOTES_V0.5.3_CLIENT_ZH.md)。本次服务端发布的核心内容是**大厅敏感词过滤**，同时包含 0.5.3 预发布周期引入的服务端管理面板（Dashboard UI）与自动升级能力。

## 敏感词过滤

词库快照自开源项目 [konsheng/Sensitive-lexicon](https://github.com/konsheng/Sensitive-lexicon)（MIT），按游戏大厅场景精选政治、反动、色情、暴恐、涉枪涉爆、广告、非法网址等类别，合并去重后 **49,172 词**，随安装包分发（`lobby-service/lexicon/`，含 `SOURCES.md` 来源说明），离线环境可直接部署。

**过滤范围与行为：**

- **聊天消息（大厅频道 + 房间聊天）**：命中的敏感片段自动以等量 `*` 替代，发送者与全房间看到一致的打码版本，历史快照同样保存打码版。
- **名称类字段**（房间名、建房者名、加入昵称、续局槽位角色名/玩家名、聊天票据昵称、MOD 预检昵称）：命中时拒绝，返回 `400 invalid_request` +「包含敏感词内容，请修改后重试」+ `details.reason: "sensitive_content"`。
- **房间 WS hello 昵称**：命中时握手被拒绝（`invalid_message`，非终态，可换名重试）。
- 不过滤：管理员公告与显示名、房间密码、gameMode/version/modList 等结构化字段。

**匹配与反绕过：**

- 归一化管线：NFC → 全角转半角 → 英文小写折叠 → 剔除空白/标点/符号/零宽字符 → 连续重复字符压缩。`法 轮 功`、`法。轮。功`、`ＡＶ`、插入零宽字符等常见绕过均可命中。
- **ASCII 词边界规则**：词库含短 ASCII 词（如 `av`），纯子串会误伤 `have`/`small`/`standard`。规则要求 ASCII 词首尾不得与更多 ASCII 字母数字直接相邻（被剔除字符形成的间隙视为边界），因此 `have a nice run`、`data center` 不误伤，而 `av movie` 仍命中。
- 匹配性能：单次检查约 6µs（49k 词 Trie），对聊天与建房路径无感知。

**管理面板控制：**

- 面板设置区新增「敏感词过滤」开关（默认开），保存即时生效并持久化；状态行实时显示 `词库 N 词 · 已打码 X 条消息 · 已拒绝 Y 个名称`。
- 词库缺失/为空/读取失败时服务 **fail-open**（不过滤但正常启动），面板显示「词库加载失败」，启动日志含 `[moderation]` 行。
- 统计自进程启动起累计，重启清零。
- 环境变量（仅首次启动种子值，之后以面板为准）：`SENSITIVE_FILTER_ENABLED=true`、`SENSITIVE_LEXICON_DIR=`（默认包根 `lexicon/`）。

**已知词库特性（来自上游词表，非缺陷）：**

- `测试`、`大法` 等词在平台过滤词表中：昵称「测试员」会被拒绝；聊天「大家」开头会命中 `大法`。
- 词库含 `da` 等垃圾短词，`da vinci` 这类罕见组合会命中。

## 0.5.3 预发布周期内容（随本版转正）

- 服务端管理面板 Dashboard UI（设置、公告、聊天治理、带宽与探针状态）。
- 服务端自动升级：面板内检查/安装 GitHub Release 更新，systemd 模式由 `service-runtime.mjs` 启动器接管重启；更新状态持久化于 `data/service-update/status.json`。

## 升级步骤

**systemd / 源码部署：**

1. 备份当前 `lobby-service/` 目录（保留 `.env` 与 `data/`）。
2. 用 `sts2_lobby_service.zip` 中 `lobby-service/` 完整替换程序文件（保留 `.env` 与 `data/`）。
3. 执行 `npm ci && npm run build`，重启服务。
4. 确认启动日志出现 `[moderation] loaded 49172 sensitive words ...` 与 `sts2-lobby-service@0.5.3`。
5. 注意：发布包为确定性打包（统一 mtime），用 rsync 部署时建议加 `--checksum`，避免同尺寸文件（如 package.json）被 quick-check 跳过。

**Docker：**

镜像已包含 `lexicon/`，重建镜像启动即可，无需额外挂载。

**自动升级（0.5.3 预发布部署）：** 面板「检查更新」可发现本版本并一键安装。

## 正式资产

- `sts2_lobby_service.zip`：Linux systemd / Docker 服务端源码、词库与安装材料。
- 客户端仍为 [`v0.5.2`](https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.2)，本 release 不含客户端资产。

- 服务端 ZIP SHA-256：`ccb4c781db7367063b0a0db303e666fb2f7cdb597b15c119bc0fb74927baa4a4`

## 验证结果

- lobby-service 测试 **509/509 通过**（含敏感词过滤单元测试 45 个、集成测试 11 个），`tsc --noEmit` 干净。
- 生产词库冒烟：49,172 词加载；建房/票据敏感名 400；插空格/符号/全角变体拦截；`have a nice run` 等不误伤；WebSocket 聊天 ack 与广播打码一致；面板开关热切换生效。
- 真实服务器（Tencent）完成 0.5.2 → 0.5.3 升级演练，含备份、回滚路径与启动校验。
