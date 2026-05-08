# 去中心化 Lobby 服务发现设计

- **创建日期**：2026-05-08
- **目标切换日（D-Day）**：2026-05-18（10 天）
- **状态**：Design — 待 review
- **作者**：项目维护者 + 协作 brainstorming

## 1. 背景

当前架构由三层组成：

- **客户端 mod**（`sts2-lan-connect/`，Godot/.NET）。打包时硬编码默认 registry 地址 `http://47.111.146.69:18787`（参见 `sts2-lan-connect/Scripts/LanConnectLobbyEndpointDefaults.cs:33`）。
- **大厅服务**（`lobby-service/`，Node.js/TypeScript）。每个第三方服主自行部署一份；通过 `SERVER_REGISTRY_BASE_URL` 向中心 registry 上报心跳与公开列表申请。
- **中心服务注册中心**（`server-registry/`）。维护者本人托管的一台公网服务，承担"列表展示 + 公告 + 公开列表审核 + 带宽探测"等中心化职能；客户端 GET `/servers/` 读取所有可用服。

两台维护者自有的服务器（`server-registry` 实例 + 维护者自己的 `lobby-service` 实例）将在 **2026-05-18** 到期。维护者决定不再续费，但希望服务整体不从生态中消失：现役有 10+ 位第三方服主仍愿意提供服务，需要一种不再依赖中心 registry 的发现机制让玩家继续找到他们。

## 2. 目标 / 非目标

### 2.1 目标

1. **零长期续费**：D 日之后维护者本人不再支付任何持续运营成本（CF 免费额度足够）。
2. **服务连续性**：D 日切换瞬间，已升级的客户端能从在线 peer 网络发现至少 5 个活跃服。
3. **去中心化**：单一组件失败（含 CF）不会让整个发现系统瘫痪；玩家在任意一处可达即可继续游玩。
4. **低运维负担**：现役服主可选两条接入路径——升级到 lobby-service v0.3，或旁挂一个 sidecar 进程；任意一种都不应破坏其现有部署。
5. **客户端一次发版承接**：客户端 v0.3 发布后无需后续大改即可长期工作。

### 2.2 非目标

1. **老客户端兼容**：D 日之后硬编码 `47.111.146.69:18787` 的 `0.2.x` 客户端会失效。**接受这一既定损失**（见 §6.1）。
2. **完全消除人类介入**：维护者仍偶尔需要更新 `peers:seeds`（约一年 1–2 次）和审视异常上报。
3. **跨网络游戏匹配联动**：本设计仅替换"服务器目录"层，不触碰房间撮合 / Relay / 续局逻辑。
4. **强信任 / 反钓鱼仿冒**：维护者在 brainstorming 中明确选择"全自动开放注册"（§5），承担轻度仿冒风险。
5. **CF China Network 接入**：维护者无 ICP 备案，CF 流量回源海外 POP；接受 100–300ms 额外延迟。

## 3. 现状回顾（Why & What 已经存在）

| 模块 | 路径 | 现状角色 |
|---|---|---|
| 客户端 registry 客户端 | `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyDirectoryClient.cs` | GET `<registry>/servers/` 拉取全网列表 |
| 客户端默认配置 | `sts2-lan-connect/Scripts/LanConnectLobbyEndpointDefaults.cs` | 硬编码 `RegistryBaseUrl = "http://47.111.146.69:18787"`；可被 `lobby-defaults.json` 覆盖 |
| 大厅服务上报模块 | `lobby-service/src/server-admin-sync.ts` | 心跳 / 申请上报；`SERVER_REGISTRY_BASE_URL` 等环境变量集中在 `server.ts:70-76` |
| 中心 registry | `server-registry/src/server.ts` `store.ts` `probe.ts` | 列表 + 公告 + 审核 + 带宽探测；PostgreSQL 持久化；当前部署在 `47.111.146.69:18787` |

**关键约束：跨运行时 DTO 手动同步**（见 `AGENTS.md`）—`sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs` 与 `lobby-service/src/store.ts` / `server.ts` 之间的 DTO 必须手工保持一致。本设计新增的所有协议数据结构都需遵循这一约定。

## 4. 总体架构

```
                     ┌─────────────────────────────┐
                     │  Cloudflare Workers + KV    │
                     │  (free tier, 海外自有域名)   │
                     │                             │
                     │  GET /v1/servers   只读快照  │ ◄──── client 启动时拉
                     │  GET /v1/seeds     兜底种子  │ ◄──── client 启动时拉
                     │  GET /v1/announcements      │
                     │  cron(10min): 拉 N 台 peer   │
                     └──────────────┬──────────────┘
                                    │ pull
              ┌─────────────────────┼─────────────────────┐
              │                     │                     │
        ┌─────▼─────┐         ┌─────▼─────┐         ┌─────▼─────┐
        │ lobby A   │◄───────►│ lobby B   │◄───────►│ lobby C   │  ...10+ 台
        │ v0.3 内置 │  gossip │ v0.3 内置 │  gossip │ + sidecar │
        │ /peers/*  │         │ /peers/*  │         │  老 lobby │
        └─────┬─────┘         └───────────┘         └───────────┘
              │ 实时 GET /peers
              ▼
       ┌──────────────┐
       │  Client v0.3  │   启动 → 选服弹窗 → 连后补全列表 → 写本地缓存
       └──────────────┘
```

**职责划分**

| 层 | 维护者 | 持有数据 | 失败影响 |
|---|---|---|---|
| CF Workers + KV | 项目维护者（一次性部署） | 聚合服务器列表 + 兜底种子 + 公告 | 客户端退化到本地缓存 + hardcoded 种子 |
| lobby-service v0.3 / sidecar | 各服主自管 | 各自 peer 集合（gossip 同步） | 单台不可用不影响整网 |
| Client v0.3 | 项目维护者发版 | 本地 last-known peers 缓存 | 全黑（极端情况）；兜底种子提供恢复路径 |

**两个核心承诺**

1. **没有任何单点必须长期续费。** CF 免费额度永远够用；维护者的 lobby 关停后整网仍能正常工作。
2. **客户端冷启动有三道保险。** CF → 本地缓存 → hardcoded 种子，三者任一可用就能进入网络。

## 5. 组件设计

### 5.1 客户端 v0.3

#### 5.1.1 启动 UI 流程

替换现行"自动加载默认大厅"行为，改为：

```
游戏启动 + mod 加载
  │
  ▼
┌────────────────────────────────┐
│  弹窗：选择服务器               │
│  ─ 上次使用：xxx.com (3 分钟前) │ ← 高亮，回车默认
│  ─ 收藏夹：A 服 / B 服          │
│  ─ 在线列表（按延迟排序）：     │
│      D 服 - 32ms - [中国]      │
│      E 服 - 180ms - [日本]     │
│  ─ [手动输入地址]              │
│  ─ [刷新]                      │
└────────────────────────────────┘
  │ 用户选一台 / 倒计时跳过
  ▼
连接 → 房间列表（=现"大厅页"）
```

设置项："自动连接上次使用的服务器，3 秒倒计时跳过弹窗"（默认开启），保留老玩家肌肉记忆。

#### 5.1.2 三源并联 Bootstrap

启动瞬间同时发起三个查询，5 秒超时合并：

| 源 | 来源 | 优先级 |
|---|---|---|
| 本地缓存 | `<config-dir>/known_peers.json` | 最高（用户感知） |
| CF Worker | `GET https://<your-domain>/v1/servers`（来自 `lobby-defaults.json` 覆盖、否则 hardcoded） | 中 |
| Hardcoded 兜底种子 | 编入客户端二进制的 5–8 个老牌服主地址，每次发版刷新 | 兜底 |

UI 顺序：上次使用 → 在线列表（按 ping 排序）→ 兜底种子（CF + 缓存均失败时显式列出）。

#### 5.1.3 本地缓存数据结构

`known_peers.json`（条目示例）：

```json
{
  "version": 1,
  "updated_at": "2026-05-08T12:00:00Z",
  "entries": [
    {
      "address": "https://lobby.x.com",
      "display_name": "X 服主",
      "last_seen_in_listing": "2026-05-08T12:00:00Z",
      "last_success_connect": "2026-05-07T20:30:00Z",
      "consecutive_failures": 0,
      "discovered_via": "cf | peer-A | seed | manual",
      "is_favorite": false
    }
  ]
}
```

清理规则（客户端本地，独立于服务端 gossip）：

- `last_seen_in_listing` 超过 **14 天** 且 `consecutive_failures ≥ 5` → 删除
- `is_favorite = true` 的条目永不自动清理
- 缓存上限 **200 条**，溢出按"最久未成功连接"驱逐

#### 5.1.4 二次发现

玩家选定并成功连接到某台 peer 后，客户端立即向该 peer 调一次 `GET /peers`，把结果合并回本地缓存。此为 peer gossip 真正进入客户端侧的入口：CF 完全不可达时，只要老玩家还能连上任意 peer，下次启动也能拿到全网更新。

#### 5.1.5 降级表现

| 场景 | 表现 |
|---|---|
| CF 5 秒超时 / 大陆访问失败 | 弹窗仅展示本地缓存 + hardcoded 种子，标"⚠️ 在线列表暂不可达" |
| 本地缓存为空 + CF 不可达 | 仅显示 hardcoded 种子 |
| 全部种子也连不上 | 弹窗底部展示"手动输入服务器地址"输入框 |

#### 5.1.6 紧急恢复入口

设置中保留"重置本地服务器列表"按钮——清空本地缓存并强制从 CF 重新拉取。该入口为 D 日后协议出现严重 bug 时的远程修复抓手。

### 5.2 lobby-service v0.3 — Peer 网络协议

#### 5.2.1 新增端点

在现有 `8787/TCP` 上增加：

| 端点 | 调用方 | 行为 |
|---|---|---|
| `GET /peers` | 客户端 + 其他 peer | 返回本节点已知活跃 peer 列表（去重） |
| `POST /peers/announce` | 新加入 peer 或 CF Worker 转发 | 申请加入；需通过 liveness probe |
| `POST /peers/heartbeat` | 已知 peer | 续命；更新 `last_seen` |
| `GET /peers/health` | 任意调用方 | 受 challenge 协议保护的探活端点（防伪造） |

`GET /peers/health` 接受 query 参数 `challenge=<random>`，响应中携带 `signature = HMAC(node-private-key, challenge)`，用于 probe 时验证目标确实持有声称的身份。

#### 5.2.2 Gossip 节奏

```
启动时
  └─ 从 CF /v1/seeds 拉取初始 5–8 peer，挨个 probe，存入本地 peer 表

每 5 分钟（pull 周期）
  ├─ 随机选 3 个已知 peer，GET /peers
  ├─ 合并它们的 peer 列表 → 对新出现 peer 跑 liveness probe → 通过则入表
  └─ 整张表写入本地存储

每 30 分钟（push/heartbeat 周期）
  └─ 向随机选的 3 个 peer 发 POST /peers/heartbeat

TTL 规则
  ├─ peer 24h 没听到 heartbeat → 标记离线，但保留在表中 7 天
  ├─ 7 天没活过来 → 物理删除
  └─ 单次 liveness probe 失败 3 次 → 立刻标记离线
```

设计取舍：pull 周期短（5min）→ 新加入的服几分钟内全网可见；push 周期长（30min）→ 不浪费带宽；24h+7d 的双层 TTL → 偶尔重启的服不被误杀，真死掉的自然脱网。

#### 5.2.3 持久化

Peer 表持久化方案：复用现有 lobby-service 的本地存储约定（参考 `lobby-service/src/store.ts`）。新增独立文件 `peers.json` 或 SQLite 表，避免污染现有 room 状态。

#### 5.2.4 与现有 `SERVER_REGISTRY_BASE_URL` 上报模块的关系

D 日之后 `SERVER_REGISTRY_BASE_URL` 不再可达。lobby-service v0.3 启动时若检测到该变量为空或目标不可达，自动跳过现有 `server-admin-sync.ts` 的上报循环；不删除该模块的代码（保持向后兼容，便于私网部署）。

### 5.3 Sidecar — 老 lobby-service 接入路径

服主不愿/不能升级到 v0.3 时，发一个单文件 sidecar 旁挂运行：

```
host 上的 systemd 结构（不动 lobby-service.service）：

  ┌────────────────────────┐         ┌───────────────────────┐
  │ lobby-service v0.2.x   │◄────────│ sts2-peer-sidecar     │
  │  port 8787 (不变)      │  HTTP   │  port 18800           │
  │  老代码，不重启         │  本地   │  npm 单包，systemd     │
  └────────────────────────┘         └────────────┬──────────┘
                                                  │ 公网
                                                  ▼
                                           其他 peer / CF
```

**Sidecar 职能**

- 暴露与 v0.3 内置版完全一致的 `/peers/*` 端点，监听独立端口
- 通过本机 HTTP 调老 lobby 的 `/healthz`、`/rooms` 等现有接口拿到"还活着 + 当前房间数"等元信息
- 把自己声明为对应 lobby 的代表对外做 gossip——客户端最终连接的依然是老 lobby 的 8787 端口

**实现**：从 lobby-service v0.3 抽出的 peer 模块独立打包，约 300 行 TypeScript。

**安装（3 行）**：

```bash
curl -O https://<your-domain>/sts2-peer-sidecar.tar.gz
tar -xzf sts2-peer-sidecar.tar.gz -C /opt/
sudo systemctl enable --now sts2-peer-sidecar
```

**最小配置**：

```env
LOBBY_PUBLIC_BASE_URL=https://lobby.example.com
SEED_PEERS=https://peer-a.example.com,https://peer-b.example.com
PEER_LISTEN_PORT=18800
```

### 5.4 CF Worker + KV

#### 5.4.1 KV 键

| Key | 内容 | 写入方 |
|---|---|---|
| `peers:active` | cron 每 10 分钟刷新的活跃服务器聚合表 | Worker cron |
| `peers:seeds` | 维护者手填的 5–8 个老牌服主，永不被 cron 覆盖 | 维护者（KV 直写或简易管理 UI） |
| `announcements` | 公告条目 | 维护者（KV 直写） |

#### 5.4.1.1 初始 `peers:seeds` 内容（P0 第一晚写入）

```json
{
  "version": 1,
  "updated_at": "2026-05-08T00:00:00Z",
  "seeds": [
    { "address": "http://lt.syx2023.icu:52000",  "note": "" },
    { "address": "http://223.167.230.132:52000", "note": "" },
    { "address": "https://120.55.3.230",         "note": "" },
    { "address": "http://112.74.164.10",         "note": "" },
    { "address": "http://106.75.7.210:8787",     "note": "" },
    { "address": "http://110.42.9.59:37000",     "note": "" }
  ]
}
```

P0 部署 CF Worker 之前，需先验证以上 6 个地址当下都能响应 `/peers/health`（v0.3 上线后）或至少能响应 `/healthz`（兼容旧版用作占位探活）；任何未响应的地址记入 P5 协调推送阶段的"催升级名单"。Hardcoded 种子在客户端打包阶段从该列表生成。

#### 5.4.2 公开端点

| 端点 | Edge 缓存 | 行为 |
|---|---|---|
| `GET /v1/servers` | 60s | 返回 `peers:active` |
| `GET /v1/seeds` | 1h | 返回 `peers:seeds` |
| `GET /v1/announcements` | 5min | 返回 `announcements` |
| `GET /` | static | 列表展示 + "提交我的服务器" 表单（带 Turnstile） |
| `POST /v1/submit` | — | 验证 Turnstile token → 转发到 `peers:seeds` 中随机 2 台 peer 的 `POST /peers/announce` |

#### 5.4.3 Cron（每 10 分钟）

```
1. 从 peers:seeds + 上一轮 peers:active 各取最多 5 个 peer 作为 sampler
2. 并行 GET /peers，5s 超时
3. 合并、去重；对每个新出现 peer 跑一次 GET /peers/health（带 challenge）
4. 通过的写入 peers:active；如本轮全失败则保留上一轮（不写空表）
5. 写入 KV
```

### 5.5 信任 / 反垃圾控制

按 brainstorming Q4 选项 A（全自动开放注册）实现：

- **Liveness probe**：所有 peer 在接受 announce 前，必须能成功完成一次 `GET /peers/health` challenge。bot 难以伪造一个真正可达且能正确签名 challenge 的 lobby 实例，自然过滤大多数垃圾。
- **IP 限频**：每台 peer 对来源 IP 限制 `POST /peers/announce` 频率（5 次/小时）。
- **Cloudflare Turnstile**：仅作用于 CF 表单提交路径（`POST /v1/submit`）。Worker 端验证 token 后再转发到 peer。
- **传播半径限制**：每个 announce 最多被 forwarded 一跳（防止 gossip storm 与回环）。
- **TTL 决定生死**：未持续 heartbeat 的 peer 被自然清理，无须人工剔除。

## 6. 迁移计划（10 天，D-Day = 2026-05-18）

### 6.1 阶段时间表

| 阶段 | 起止 | 关键产出 |
|---|---|---|
| **P0 基础设施** | D-10（5/8）→ D-9（5/9） | CF Worker 部署到自有海外域名 + KV 三键就位 + DNS 生效 + `peers:seeds` 手填 5–8 老牌服。**P0 必须包含**只读端点（`/v1/servers` `/v1/seeds` `/v1/announcements`）与每 10 分钟 cron；**P0 可选包含**提交表单（`POST /v1/submit` + Turnstile）——若进度紧张按 §6.3 #3 推到 D+7。 |
| **P1 协议核心** | D-9（5/9）→ D-6（5/12） | lobby-service v0.3 的 `/peers/*` 四端点 + gossip 调度器 + liveness probe + 单元测试 |
| **P2 Sidecar 抽取** | D-6（5/12）→ D-4（5/14） | 把 v0.3 peer 模块独立打包 + systemd unit + 安装脚本 |
| **P3 客户端** | D-9（5/9）→ D-5（5/13），与 P1+P2 并行 | 选服弹窗 + 三源 bootstrap + 本地缓存 + 二次发现 + Godot UI 适配 |
| **P4 联调** | D-4（5/14）→ D-3（5/15） | 1 Worker + 3 v0.3 lobby + 1 sidecar 全链路 e2e；新服注册 → gossip 传播 → 客户端可见 |
| **P5 协调推送** | D-3（5/15）→ D-1（5/17） | 发布 v0.3 客户端 + 通知 10+ 服主升级或装 sidecar + 在老 server-registry 公告挂"将下线"横幅 |
| **P6 切换日** | D（5/18） | 老 IP 到期、CF + peer 网络承接全部流量；监控前 24h |
| **P7 善后** | D+1（5/19）→ D+7（5/25） | 处理掉队服主、补 hardcoded 种子最新版、收集 bug 反馈；CF 提交表单（Turnstile）若被减法可在此阶段补上 |

### 6.2 风险与对策

| 风险 | 缓解 |
|---|---|
| 服主升级跟不上节奏 | P3 末期就把 sidecar 包甩给最稳的 3–4 个服主试装；其余 P5 通知。即便 D 日仅一半升级，gossip 网络仍可工作。 |
| 老客户端 D 日变砖 | 既定接受。D-7 起在即将下线的 server-registry 公告轮播挂"X 月 X 日后请升级到 v0.3"，复用现有公告系统对老客户端推送一次升级提醒。 |
| CF + 海外域名在大陆抽风 | 客户端 P3 阶段就实现"5 秒超时退化到 hardcoded 种子"。Hardcoded 种子在 P0 即生成（拷贝 `peers:seeds`）。 |
| D 日后发现协议 bug | 客户端 v0.3 留 force-refresh-from-CF 兜底入口（"重置本地服务器列表"按钮）；紧急情况下更新 CF KV 即可远程修复。 |

### 6.3 MVP 减法（按"砍掉损失最小"排序）

P5 那天评估剩余工作，必要时按以下顺序砍：

1. **公告系统迁移**：D 日不迁，玩家暂时看不到公告。事后补。
2. **客户端"按延迟排序"**：列表先按字母序，事后版本再加 ping。
3. **CF Worker 提交表单**：D 日只先放只读列表，新服主想加入暂时只能维护者手工加 `peers:seeds`。表单提交流程做在 D+7 之后。
4. **Sidecar**：极端情况下放弃，强制所有服主升级到 v0.3。代价是没升级的服离线（但他们的 8787 仍能被客户端直连，只是不出现在自动列表）。

底线：1+2 可砍；3 视情况；4 是最后底线。

## 7. 端到端验收（D 日合格标准）

```
[ ] 新装客户端打开 → 弹窗 5 秒内出现服务器列表（来自 CF）
[ ] CF Worker 主动 down 后，客户端弹窗内仍显示 hardcoded 种子和本地缓存
[ ] 已升级的 lobby 和未升级（带 sidecar）的 lobby 互相能在彼此的 /peers 中看到
[ ] 一台 peer 突然挂掉 → 30 分钟内其它 peer 通过 liveness probe 失败 3 次将其标记离线 → 该 peer 不再出现在客户端可见的"在线列表"中（按 §5.2.2 定义：标记离线即从对外 `/peers` 响应中过滤；24h 后正式从内部 peer 表的"活跃集"剥离；7 天后物理删除） |
[ ] 新服主走 CF 表单提交 → 5 分钟内被 cron 抓到 → 客户端可见（若该项被减法砍则跳过）
[ ] 客户端连上任意 peer 后，本地缓存条目被它的 /peers 数据扩充
```

## 8. 设计要素决议

以下 4 条在 brainstorming 阶段一并敲定，实施阶段直接按此执行。

### 8.1 Hardcoded 种子的滚动刷新

**决议**：客户端打包脚本 `scripts/build-sts2-lan-connect.sh`（或其上游 `scripts/package-sts2-lan-connect.sh`）新增一步：从仓库内的 `data/seeds.json`（一份与 CF `peers:seeds` 同步的快照，纳入 git）读取最新种子列表，写入打包出的 `lobby-defaults.json` 的 `seedPeers` 字段。每次发新版客户端前，维护者只需手工把最新 `peers:seeds` 拷贝到 `data/seeds.json` 并提交 PR——一次复制粘贴。

不做"打包时直接 fetch CF"——避免构建对网络可达性的依赖，也避免不同时段打出来的客户端种子不一致。

### 8.2 节点身份与签名密钥

**决议**：每台 lobby-service v0.3 / sidecar 在首次启动时，本地生成 ed25519 密钥对并持久化到 `<state-dir>/peer-identity.key`（`0600` 权限）。

- 节点公钥作为 `/peers` 响应中每个 entry 的字段一并暴露
- `/peers/health` 协议：requester 发 nonce → target 用私钥签名 → requester 用此前已知的目标公钥验证
- 第一次接触某个新 peer 时采用 **TOFU**（Trust On First Use）：跟随首个 announce 它的 peer 给出的公钥
- 公钥变化的处理：若 `/peers/health` 验签失败（公钥与本地记录不符），等同于 §5.2.2 中的"单次 liveness probe 失败"——累计 3 次失败后标记离线，TTL 之后自然脱网。不引入额外的剔除路径，保证规则面单一
- 不引入任何中心 CA / 证书链

Sidecar 与同机器上的 lobby-service v0.3 的身份独立——sidecar 代表的是其旁挂的老 lobby 的"对外身份"，不是 lobby 进程本身。

### 8.3 Peer 表持久化形态

**决议**：JSON 文件 + 原子写入（写临时文件 → fsync → rename）。

- 路径：v0.3 内置 — `<lobby-data-dir>/peers.json`；sidecar — `<sidecar-data-dir>/peers.json`
- 数据规模 10–500 条，JSON 完全够用；避免 sidecar 引入 SQLite 依赖（保持单文件部署目标）
- 写入策略：每次 peer 表变更（announce / heartbeat / TTL 清理）后立即原子写盘；高频更新场景（heartbeat）用 100ms debounce 合并写
- 启动时优先读盘恢复 peer 表，再叠加从 CF `/v1/seeds` 拉到的种子

### 8.4 客户端"按延迟排序"的 ping 实现

**决议**：HTTP HEAD `<peer-base-url>/peers/health?ping=1`（或老 lobby 兼容 `/healthz`），测量 TTFB。

- ICMP 在 Godot/.NET 上需 root 权限或特定 socket 类型，跨平台不一致——放弃
- UDP echo 没有现成端点，需要新增协议——不做
- HTTP HEAD 复用现有可达性逻辑，5 秒超时，并发上限 10
- 弹窗中并行 ping 全部已知 peer，500ms 内回的归"低延迟组"，500ms–2s 归"中延迟组"，2s 以上或失败的归"高延迟/不可达组"，分组内部按字母序

带宽差的玩家可在设置里关掉自动 ping（弹窗回退到字母序）。

## 9. 不在本次范围内（明确不做）

- 客户端的多语言公告分发改造
- Relay UDP 中继协议改动
- 房间撮合 / 续局相关任何变更
- server-registry 现有数据库（PostgreSQL）数据导出迁移——D 日之后 `server-registry/` 仓库目录保留作为参考，但不再被任何运行实例使用

---

**Spec 终。** 实施计划由后续 writing-plans 阶段产出，不在本文档范围。
