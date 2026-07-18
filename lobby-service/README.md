<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

## 中文

# STS2 Lobby Service

> 本文档对应 **v0.5.1**。去中心化节点网络和 v0.5.0 聊天能力保持不变；v0.5.1 新增私有 gameplay MOD 预检。v0.3.x 时代的 `SERVER_REGISTRY_*` 环境变量自 v0.4.0 起已完全删除。

## 文档定位

这份文档是 **大厅服务服主 / 运维手册**，面向准备部署、维护、排障或打包 `lobby-service` 的管理员。

它主要回答：

- 该服务负责什么、**不**负责什么
- 当前 v0.5.1 推荐的部署路径是什么
- 首次部署完成后先检查哪些项目
- 如何配置节点网络、私有访问、管理面板与客户端默认大厅
- 需要深入查阅时，环境变量和 API 在哪里看

## 负责 / 不负责

### 负责

- 房间目录管理
- 房间密码校验
- 房主心跳与僵尸房间清理
- 控制通道握手与按房间广播
- 房间聊天 envelope 广播
- 服务器频道 ticket、历史缓冲、限流与广播
- Emoji / item / combat 引用的能力协商和 legacy 文本降级
- 房主踢人（`kick_player`）
- 房间设置同步（`room_settings`）
- 生成 `ENet` 直连优先、失败后自动切 relay 的连接计划
- 保存续局房间 `savedRun` 元数据与可接管角色槽位
- 内置 `/server-admin` 管理面板
- 公告下发（`GET /announcements`）
- 通过 `/peers/announce` + Cloudflare discovery worker 加入去中心化节点网络
- 暴露 `/peers/metrics` 给客户端 picker 做按节点活跃指标读取

### 不负责

- 战斗同步
- 账号系统
- 保证 NAT 一定打通
- 任何"母面板"/审核后台 —— v0.4.0 起已没有这种概念

> 当前 relay 的定位是"直连失败时的房间级兜底路径"，不是独立完整的联机协议。

## 推荐部署路径

当前 **推荐主路径** 是：

1. 优先使用 **systemd 安装脚本** 部署到 Linux 主机
2. 在首次安装时就填好公网地址（域名优先）
3. 生成并写入 `SERVER_ADMIN_PASSWORD_HASH` 与 `SERVER_ADMIN_SESSION_SECRET`
4. **手动补全 `PEER_SELF_ADDRESS` / `PEER_CF_DISCOVERY_BASE_URL`**（首次安装脚本生成的 `.env` 里可能还未包含）
5. 完成最小验证：`/health`、`/server-admin`、`/announcements`、`/rooms`、`/peers/health`
6. 按需为客户端重新打包默认大厅配置

容器部署也是受支持的；手动运行主要用于开发或临时排障。

## 首次部署最小步骤

### 1) 推荐：systemd 部署

从仓库根目录执行：

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

安装脚本会自动：

- 复制服务文件到 `/opt/sts2-lobby/lobby-service`
- 执行 `npm ci`
- 执行 `npm run build`
- 首次安装时生成 `.env`
- 生成 `/opt/sts2-lobby/start-lobby-service.sh`
- 在 systemd 可用且以 root 执行时安装并启动 `sts2-lobby.service`

> ⚠️ **首次安装后必检**：编辑 `/opt/sts2-lobby/lobby-service/.env`，确认里面包含 `PEER_SELF_ADDRESS` 与 `PEER_CF_DISCOVERY_BASE_URL`。如果没有，按本文"4) 配置节点网络"补齐——这是 v0.4.0+ 新部署最常见的遗漏，会导致 `/server-admin` 显示"节点网络未配置"。

### 2) 开放端口

默认需要放行：

- `8787/TCP`
- `39000-39149/UDP`

### 3) 生成管理面板密码哈希与会话密钥

```bash
cd lobby-service
npm run hash-admin-password -- '你的面板密码'
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

将两段输出分别写入 `.env`：

```text
SERVER_ADMIN_PASSWORD_HASH=<密码哈希>
SERVER_ADMIN_SESSION_SECRET=<会话密钥>
```

### 4) 配置节点网络（v0.4.0+ 必检）

在 `.env` 中加入 / 确认以下行：

```text
PEER_SELF_ADDRESS=http://<你的公网 IP 或域名>:8787
PEER_CF_DISCOVERY_BASE_URL=https://sts2-gamelobby-register.xyz
PEER_PUBLIC_LISTING_ENABLED=true
PEER_STATE_DIR=/app/data/peer
# 可选：覆盖客户端 picker 显示名
# PEER_DISPLAY_NAME=My Community Lobby
```

要点：

- `PEER_SELF_ADDRESS` 必须能从公网回访这台机器（含 scheme + 端口）；其他节点和 CF discovery worker 用它探活
- 走 HTTPS / 反向代理时，写代理对外的真正可达 URL
- 完全关闭节点网络：`PEER_NETWORK_ENABLED=false`，此时 `PEER_SELF_ADDRESS` 可留空
- 真理源：[`../deploy/lobby-service.env.example`](../deploy/lobby-service.env.example)

### 5) 首次检查

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/probe
curl http://127.0.0.1:8787/announcements
curl http://127.0.0.1:8787/rooms
curl http://127.0.0.1:8787/peers/health
```

`/peers/health` 应该显示 `selfAddress` 等于你配的公网地址，`publicListing: true`，过一会儿 `activePeers` 会 > 0。

并在浏览器打开：

```text
http://<公网 IP 或域名>:8787/server-admin
```

进入后查看"节点网络"区域：理想状态是 `已加入节点网络`（已观察到外部节点）或 `正在加入节点网络`（刚开机时）。

## 选择部署方式

### A. systemd（推荐）

适合长期运行的 Linux 主机。

安装：

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

查看状态与日志：

```bash
systemctl status sts2-lobby
journalctl -u sts2-lobby.service -n 200 --no-pager
```

如果遇到 `[peer] disabled (set PEER_SELF_ADDRESS to enable)`：

```bash
sudo ./scripts/diagnose-lobby-peer.sh
```

### B. Docker

适合已有容器基础设施的环境。在 `lobby-service/` 目录执行：

```bash
cp deploy/lobby-service.docker.env.example deploy/lobby-service.docker.env
$EDITOR deploy/lobby-service.docker.env
docker compose -f deploy/docker-compose.lobby-service.yml build
docker compose -f deploy/docker-compose.lobby-service.yml up -d
```

默认将 `./deploy/data/lobby-service` 挂载到容器内 `/app/data`，并将 `SERVER_ADMIN_STATE_FILE` 指向 `/app/data/server-admin.json`。

如果 Docker Hub 拉取较慢，可复制 `deploy/.env.example` 为 `deploy/.env`，再调整 `STS2_NODE_IMAGE`。

**Docker 额外注意：**

- Docker 不会自动推导公网地址，**必须手动填写 `RELAY_PUBLIC_HOST` 和 `PEER_SELF_ADDRESS`**
- 如果 `PEER_SELF_ADDRESS` 仍是 `127.0.0.1`、`0.0.0.0`、`localhost` 或占位值，CF discovery 反向探测会失败，节点不会出现在公共列表
- 日志轮转由 Docker `json-file` 驱动处理，默认单文件 `10MB`、保留 `5` 个历史文件

### C. 手动运行

主要用于开发、本地试跑或临时排障。

```bash
cd /path/to/STS2-Game-Lobby/lobby-service
npm ci
npm run build
npm start
```

默认监听：

- HTTP: `http://0.0.0.0:8787`
- WebSocket: `ws://0.0.0.0:8787/control`
- Relay UDP: `udp://0.0.0.0:39000-39149`

## 对外访问地址说明

`lobby-service` 提供的是 **API + 管理面板**，不是面向玩家浏览的独立网页大厅。

常见外部地址如下：

| 用途 | 示例地址 |
|------|----------|
| 管理面板 | `http://<公网 IP 或域名>:8787/server-admin` |
| 健康检查 | `http://<公网 IP 或域名>:8787/health` |
| 节点网络运行状态 | `http://<公网 IP 或域名>:8787/peers/health` |
| 节点指标快照 | `http://<公网 IP 或域名>:8787/peers/metrics` |
| 公告接口 | `http://<公网 IP 或域名>:8787/announcements` |
| 房间列表接口 | `http://<公网 IP 或域名>:8787/rooms` |
| 控制通道 | `ws://<公网 IP 或域名>:8787/control` |

说明：

- 玩家通常不需要手动打开这些 URL；他们通过游戏客户端建房 / 加房
- `/server-admin` 页面可以打开，但要登录修改配置，必须设置 `SERVER_ADMIN_PASSWORD_HASH` 和 `SERVER_ADMIN_SESSION_SECRET`
- 若你在反向代理后提供 HTTPS / WSS，请确保客户端默认大厅、`PEER_SELF_ADDRESS` 都更新成代理对外地址

## 节点网络 / 私有部署说明

### 加入去中心化公开节点网络

希望这台 lobby 出现在 CF 聚合的节点列表里：

1. `PEER_SELF_ADDRESS` 写成公网可达的 URL
2. `PEER_PUBLIC_LISTING_ENABLED=true`
3. 重启服务，在 `/server-admin` 查看"节点网络"状态

`/server-admin` 节点网络状态映射：

| 状态 | 含义 |
|------|------|
| `节点网络未启用` | `PEER_NETWORK_ENABLED=false` |
| `节点网络未配置` | 启用了节点网络但 `PEER_SELF_ADDRESS` 为空 —— **新部署最容易踩的坑** |
| `仅私有可见` | `PEER_SELF_ADDRESS` 已配但 `publicListingEnabled=false` |
| `正在加入节点网络` | 已公开但还没观察到外部活跃节点 |
| `已加入节点网络` | 已观察到外部活跃节点，列表传播正常 |

### 私有 / 半私有模式

如果你不希望公开房间列表和详细健康信息，保持以下默认值即可：

```text
PUBLIC_ROOM_LIST_ENABLED=false
PUBLIC_DETAILED_HEALTH_ENABLED=false
ENFORCE_LOBBY_ACCESS_TOKEN=true
ENFORCE_CREATE_ROOM_TOKEN=true
```

推荐同时配置：

```text
LOBBY_ACCESS_TOKEN=<strong-random-read-token>
CREATE_ROOM_TOKEN=<strong-random-create-token>
CREATE_ROOM_TRUSTED_PROXIES=127.0.0.1,::1
CREATE_JOIN_RATE_LIMIT_WINDOW_MS=60000
CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS=30
```

访问策略说明：

- `GET /health` 默认只公开基础 `{ ok: true }`；详细字段需要受信来源或有效 `LOBBY_ACCESS_TOKEN`
- `GET /rooms` 默认不公开；需要受信来源或有效 `LOBBY_ACCESS_TOKEN`
- `POST /rooms` 需要受信来源或有效 `CREATE_ROOM_TOKEN`
- 建议通过 `x-lobby-access-token` / `x-create-room-token` 请求头传递，或使用 `Authorization: Bearer ***`
- 不建议把 token 放进 query string，避免出现在日志和浏览器历史中
- 如果只配置其中一个 token，服务端会向后兼容回退到该 token

> 注：将 `PEER_PUBLIC_LISTING_ENABLED=false` 只是不出现在公共列表，**节点网络心跳仍然进行**（仅私有可见）。如果要完全不接触 CF discovery，请 `PEER_NETWORK_ENABLED=false`。

## 常见运维入口

### 打包分发

```bash
./scripts/package-lobby-service.sh
```

产物：

- `lobby-service/release/sts2_lobby_service/`
- `lobby-service/release/sts2_lobby_service.zip`

打包内容包含：

- `Dockerfile`
- `.dockerignore`
- `deploy/docker-compose.lobby-service.yml`
- `deploy/lobby-service.docker.env.example`

### 服务器上安装打包产物

上传并解压后，在服务器执行：

```bash
sudo ./install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

### 重装旧部署

```bash
sudo systemctl stop sts2-lobby || true
sudo rm -rf /opt/sts2-lobby/lobby-service /opt/sts2-lobby/start-lobby-service.sh
sudo find /opt/sts2-lobby -maxdepth 1 -type f \( -name 'sts2_lobby_service*.zip' -o -name '*.tgz' \) -delete
sudo ./install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

### 日志排查

systemd：

```bash
journalctl -u sts2-lobby.service -n 200 --no-pager
```

Docker：

```bash
docker compose -f deploy/docker-compose.lobby-service.yml logs --tail 200 -f
```

常见日志条目：

- `[peer] mounted; self=...` —— 节点网络已启动，附带本机 self URL
- `[peer] disabled (set PEER_SELF_ADDRESS to enable)` —— `PEER_SELF_ADDRESS` 没读到，会显示"节点网络未配置"
- `create room`
- `join ticket issued`
- `relay_host_registered`
- `relay_client_connected`
- `connection_event ... phase=direct_timeout`
- `connection_event ... phase=relay_success`
- `connection_event ... phase=relay_failure`
- `relay_allocated`
- `relay_removed`

如果出现 `create room`、`join ticket issued`，但始终没有 `relay_host_registered`，通常意味着客户端到 relay UDP 端口段没有真正到达服务器。常见原因：

- 服务器公网 `39000-39149/UDP` 未放行
- 客户端使用了全局代理 / TUN，且大厅服务器地址没有直连

### Docker 网络提醒

如果将大段 UDP relay 端口通过 Docker bridge 暴露到公网，某些小规格云主机上可能出现 `8787` 响应异常、其他端口超时，严重时甚至影响 SSH。

在这类环境下，更推荐使用宿主机网络而不是 bridge 方式发布整段 relay UDP 端口。

## 环境变量参考

完整示例见仓库根目录的 [`../deploy/lobby-service.env.example`](../deploy/lobby-service.env.example)。以下仅保留运维最常查的分组说明。

### 网络

| 变量 | 说明 |
|------|------|
| `HOST` | 服务监听地址 |
| `PORT` | 服务监听端口 |
| `WS_PATH` | WebSocket 路径 |

### 房间与心跳

| 变量 | 说明 |
|------|------|
| `HEARTBEAT_TIMEOUT_SECONDS` | 房主心跳超时时间 |
| `TICKET_TTL_SECONDS` | 加入票据有效期 |

### Relay

| 变量 | 说明 |
|------|------|
| `RELAY_BIND_HOST` | relay 监听地址 |
| `RELAY_PUBLIC_HOST` | relay 对外公网地址；留空时可能退回本机地址，客户端连不上 |
| `RELAY_PORT_START` | relay UDP 端口段起始 |
| `RELAY_PORT_END` | relay UDP 端口段结束 |
| `RELAY_HOST_IDLE_SECONDS` | relay 房主空闲超时 |
| `RELAY_CLIENT_IDLE_SECONDS` | relay 客户端空闲超时 |

### 版本与连接策略

| 变量 | 说明 |
|------|------|
| `STRICT_GAME_VERSION_CHECK` | `false` 时不因游戏版本字符串不同拒绝 join |
| `STRICT_MOD_VERSION_CHECK` | `false` 时不因 MOD 版本字符串不同拒绝 join |
| `CONNECTION_STRATEGY` | `direct-first`、`relay-first`、`relay-only` |

说明：若客户端和房主均上报 `modList`，服务端会额外比对缺失项，并在 `mod_mismatch` 中返回差异信息。

### 加入前 MOD 预检

| 变量 | 说明 |
|------|------|
| `MOD_SYNC_ENABLED` | 新安装或旧状态迁移时的 v1 私有 MOD 预检种子值；默认开启 |
| `MOD_SYNC_MAX_DESCRIPTORS` | 单端结构化 MOD 清单上限，默认及硬上限为 `64` |
| `MOD_SYNC_MAX_PAYLOAD_BYTES` | canonical MOD 清单 UTF-8 字节上限，默认及硬上限为 `65536` |

管理面板中的“加入前 MOD 兼容预检与 Workshop 自动同步”开关是运行时真理源，保存后立即生效并持久化，无需重启。`MOD_SYNC_ENABLED` 仅在状态文件尚未包含该字段时提供初始值。启用后，`/probe` 在 `capabilities` 中返回 `modSyncProtocolVersion: 1` 与 `modSyncEnabled: true`。v0.5.1 客户端可在领取 join ticket 前调用 `POST /rooms/:id/mod-preflight`；该请求不增加人数、不改变房间状态，也不签发 ticket。功能关闭或客户端协议不匹配时，接口返回 HTTP 200 的 disabled 响应，客户端继续使用 v0.5.0 加入流程。

房主的 `hostModInventory` 只保存在房间私有对象中。MOD 清单不会出现在公开 `/rooms`、health、metrics、peer gossip 或聊天中；密码正确后预检才返回差异。预检无论 `STRICT_GAME_VERSION_CHECK` 如何设置都会硬拦截不同游戏版本，`STRICT_MOD_VERSION_CHECK=false` 只允许用户确认后继续尝试 MOD 差异。

MOD 同步只允许客户端在明确确认后调用 Steam Workshop 订阅，以及选择性修改本机启用状态。服务端不会把 DLL、PCK、ZIP 作为下载或传输内容，也不会托管 MOD 文件；房主和任意 URL 都不能成为二进制来源。

### 房间访问收口

| 变量 | 说明 |
|------|------|
| `PUBLIC_ROOM_LIST_ENABLED` | 是否公开 `GET /rooms`；默认 `false` |
| `PUBLIC_DETAILED_HEALTH_ENABLED` | 是否公开详细 `GET /health`；默认 `false` |
| `ENFORCE_LOBBY_ACCESS_TOKEN` | 是否强制校验读取令牌；默认 `true` |
| `ENFORCE_CREATE_ROOM_TOKEN` | 是否强制校验建房令牌；默认 `true` |
| `LOBBY_ACCESS_TOKEN` | 读取令牌；未设置时回退到 `CREATE_ROOM_TOKEN` |
| `CREATE_ROOM_TOKEN` | 建房令牌；未设置时回退到 `LOBBY_ACCESS_TOKEN` |
| `CREATE_ROOM_TRUSTED_PROXIES` | 可绕过建房令牌的受信来源 IP/CIDR |
| `CREATE_JOIN_RATE_LIMIT_WINDOW_MS` | 建房 / 加房请求限流窗口 |
| `CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS` | 单来源窗口内允许请求数 |

### 服务器频道与房间富聊天

- 这是 v0.5.0 的服务端协议面。升级客户端而不升级 lobby-service 时，服务器频道 ticket 和富聊天协商不可用；发布时必须同步更新两个组件。
- 服务器频道只保留当前节点、当前进程的有界内存历史，重启即清空；房间聊天不保留历史，peer 节点也不会复制聊天。昵称来自未验证的客户端 session，只能展示，不得作为身份或授权依据。
- 建房和 continue-run 重新发布会创建新的 `roomSessionId` generation。power/player/monster 战斗引用必须匹配当前 generation；过期引用在客户端降级，不影响静态 item 引用或相邻文本。
- legacy fallback 先按原顺序使用最多 60 个 UTF-16 unit 保存所有用户文本，再用剩余预算追加完整通用实体占位符；不会拆 surrogate pair，也不会暴露 model ID。monster target 因缺少双客户端稳定 ID 证明而保持 hard-disabled。
- 六个治理开关由 `/server-admin` 写入现有 `SERVER_ADMIN_STATE_FILE`，持久化成功后才向 server/room gateway 顺序广播。消息、metrics、history 不写入状态文件。环境变量只填充缺失键，持久化值在重启后优先。
- 依赖顺序：rich 关闭时 Emoji/item/combat 的有效版本为 0，但子开关持久值保留；room-v2 关闭时 combat-v2 为 0，legacy 房间文本仍可用；服务器频道开关独立。分阶段回滚先关 combat，再关 Emoji/item 与 rich，最后才按需关 room-v2。
- 三份配置默认面必须一致：`lobby-service/.env.example`、`deploy/lobby-service.env.example`、`lobby-service/deploy/sts2-lobby.service.example`。`SERVER_CHAT_TRUSTED_PROXY_CIDRS` 默认留空，只有明确受信的反向代理地址才能加入。
- 安装零写预检：`./scripts/build-sts2-lan-connect.sh --install --dry-run`。发布验证只使用临时输出目录，不读写 `releases/`；包内不得出现 `typing.dll`、游戏程序集、游戏图片/字体或除本 MOD PCK 外的游戏 PCK。

当 `LOBBY_ACCESS_TOKEN` 未设置时，`CREATE_ROOM_TOKEN` 也可授权受保护的房间列表和详细健康读取；`CREATE_ROOM_TOKEN` 本身不授权聊天 ticket。

### 子服务管理面板

| 变量 | 说明 |
|------|------|
| `SERVER_ADMIN_USERNAME` | 管理面板用户名 |
| `SERVER_ADMIN_PASSWORD_HASH` | 管理面板密码哈希（`salt:hash`） |
| `SERVER_ADMIN_SESSION_SECRET` | 会话密钥 |
| `SERVER_ADMIN_SESSION_TTL_HOURS` | 会话有效期（小时） |
| `SERVER_ADMIN_STATE_FILE` | 状态持久化文件路径 |

### 节点网络（PEER_*）—— v0.4.0 新增

| 变量 | 说明 |
|------|------|
| `PEER_NETWORK_ENABLED` | 默认 `true`（任何非 `false` 值都按 true 处理）；设为 `false` 完全关闭节点网络 |
| `PEER_SELF_ADDRESS` | 本机公网 URL；空值即"节点网络未配置" |
| `PEER_CF_DISCOVERY_BASE_URL` | Cloudflare discovery worker 基地址；客户端与本节点 bootstrap 都从这里读取节点列表 |
| `PEER_PUBLIC_LISTING_ENABLED` | 首次安装时种子值；管理面板开关是运行时真理源 |
| `PEER_STATE_DIR` | 本地 peer 状态目录（`peers.json` + identity 密钥对） |
| `PEER_DISPLAY_NAME` | 可选；覆盖客户端 picker 显示名 |

## API 参考

### 通用

- `GET /health`
- `GET /probe`
- `GET /announcements`
- `GET /rooms`

### 节点网络（v0.4.0 新增）

- `GET /peers/health` —— 节点网络运行状态
- `GET /peers/metrics` —— 当前节点的房间数、带宽、guard 状态、显示名快照（替代旧版 server-registry 的 `/servers/` 读取）
- `POST /peers/announce` —— 接收其他节点的 announce
- `POST /peers/heartbeat` —— 节点间心跳

### 房间管理

- `POST /rooms`
- `POST /rooms/:id/join`
- `POST /rooms/:id/heartbeat`
- `POST /rooms/:id/connection-events`
- `DELETE /rooms/:id`

### 管理面板

- `GET /server-admin`
- `POST /server-admin/login`
- `GET /server-admin/settings`
- `PATCH /server-admin/settings`

### WebSocket

- `WS /control`

### 续局联机相关字段

**`POST /rooms`**

- 支持可选 `savedRun`
- `savedRun.saveKey` 用于把续局存档与大厅房间绑定
- `savedRun.slots` 描述可接管角色槽位及其 `netId`
- 非受信来源需要有效 `CREATE_ROOM_TOKEN`

**`POST /rooms/:id/join`**

- 支持可选 `desiredSavePlayerNetId`
- 支持可选 `modList`
- 多空闲角色槽位的续局房间，客户端必须显式选择槽位
- 失败时会返回 `version_mismatch`、`mod_version_mismatch`、`mod_mismatch`、`room_started`、`room_full`

**`POST /rooms/:id/heartbeat`**

- 支持上报 `connectedPlayerNetIds`
- 服务端据此更新续局角色槽位占用状态

**`POST /rooms/:id/connection-events`**

- 客户端可上报 `direct_timeout`、`relay_success`、`relay_failure` 等事件，用于公网联机排障

## 历史兼容 / 补充说明

以下内容 **不是 v0.5.0 推荐主路径**，但仍保留供旧部署管理员查阅：

- v0.3.x 升级与 sidecar 过渡资料：[`../docs/STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md`](../docs/STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md)、[`../docs/STS2_PEER_SIDECAR_GUIDE_ZH.md`](../docs/STS2_PEER_SIDECAR_GUIDE_ZH.md)
- 当前部署主路径中文版：[`../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md)
- `SERVER_REGISTRY_*` 一组变量在 v0.4.0 lobby-service 中已彻底无效；旧 `.env` 里保留这些行不会出错也不会生效，建议清理

---

<a name="english"></a>

## English

# STS2 Lobby Service

> Targets **v0.5.1**. It retains the v0.4.0 decentralized peer network and v0.5.0 chat capabilities, and adds private gameplay-MOD preflight. All `SERVER_REGISTRY_*` env vars from the v0.3.x era have been inert since v0.4.0.

This README is the **operator/admin guide** for `lobby-service`.

### What it does

- Room directory and password validation
- Host heartbeat and stale room cleanup
- Control-channel handshake and room-scoped broadcast
- Server-channel tickets, bounded history, rate limits, and broadcast
- Capability negotiation and legacy fallback for Emoji/item/combat references
- Private preflight for gameplay MODs and required dependencies before join-ticket issuance
- In-room announcements and `/server-admin` management
- Relay fallback planning when direct ENet connection fails
- Joins the decentralized peer network via `/peers/announce` + the Cloudflare discovery worker
- Serves per-node live metrics at `/peers/metrics` for the client picker

### What it does not do

- Battle synchronization
- Account systems
- Guaranteed NAT traversal
- Any "master panel" / review backend — this has not existed since v0.4.0
- Hosting, downloading, or relaying MOD DLL, PCK, or ZIP files

### Recommended path

1. Use the Linux systemd installer
2. Set a real public hostname or domain during install
3. Generate `SERVER_ADMIN_PASSWORD_HASH` and `SERVER_ADMIN_SESSION_SECRET`
4. **Manually add `PEER_SELF_ADDRESS` / `PEER_CF_DISCOVERY_BASE_URL`** to the generated `.env` (the install script may not include them yet on a fresh install)
5. Verify the default-enabled MOD sync toggle in `/server-admin`, then verify `/health`, `/probe`, `/announcements`, `/rooms`, `/peers/health`, and private preflight

Recommended install command:

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <your public IP or domain>
```

### Minimum peer network settings

```text
PEER_SELF_ADDRESS=http://<your public IP or domain>:8787
PEER_CF_DISCOVERY_BASE_URL=https://sts2-gamelobby-register.xyz
PEER_PUBLIC_LISTING_ENABLED=true
PEER_STATE_DIR=/app/data/peer
```

If `PEER_SELF_ADDRESS` is missing, `/server-admin` will show "Peer network unconfigured". The canonical reference is [`../deploy/lobby-service.env.example`](../deploy/lobby-service.env.example).

### Admin panel peer-network states

| State | Meaning |
|-------|---------|
| `Peer network disabled` | `PEER_NETWORK_ENABLED=false` |
| `Peer network unconfigured` | Enabled but `PEER_SELF_ADDRESS` empty — most common fresh-install issue |
| `Private only` | `PEER_SELF_ADDRESS` set but `publicListingEnabled=false` |
| `Joining` | Public, no external peers observed yet |
| `Joined` | Public and external peers observed |

### Chat governance and rich-room operations

- This is the v0.5.0 service protocol surface. Updating only the client does not provide server-channel tickets or rich capability negotiation; publish and deploy both components together.
- Server-channel history is bounded, node-local process memory and disappears on restart. Room chat retains no history. Display nicknames are unverified client-session data, never authenticated identities.
- Room creation and continue-run republish rotate the authoritative `roomSessionId` generation. Stale power/player/monster references degrade locally while static item links and adjacent text remain intact.
- Legacy projection spends its 60 UTF-16-unit budget on user text first, then adds only whole generic entity placeholders that fit. It never splits surrogate pairs or exposes model IDs. Monster targets ship disabled until a two-client stable-ID proof exists.
- `/server-admin` persists six toggles in the existing `SERVER_ADMIN_STATE_FILE` before ordered gateway broadcasts. Messages, metrics, and history are runtime-only. Environment variables fill missing keys; persisted values win after restart.
- Rich-off makes effective Emoji/item/combat versions zero without erasing child toggles. Room-v2-off disables combat-v2 but keeps legacy room text. Server chat is independent. Roll back in stages: combat, then Emoji/item and rich, then room-v2 only if needed.
- Keep `lobby-service/.env.example`, `deploy/lobby-service.env.example`, and `lobby-service/deploy/sts2-lobby.service.example` in exact parity. Use `./scripts/build-sts2-lan-connect.sh --install --dry-run` for zero-write install planning. Release verification uses temporary output only, never `releases/`, and packages no `typing.dll` or game assemblies/assets/fonts/PCKs.

When `LOBBY_ACCESS_TOKEN` is unset, `CREATE_ROOM_TOKEN` also authorizes protected room-list and detailed-health reads. `CREATE_ROOM_TOKEN` alone does not authorize chat tickets.

### Reference sections

- Environment variables: see [`../deploy/lobby-service.env.example`](../deploy/lobby-service.env.example)
- Current deployment guide (Chinese): [`../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md)
- Historical compatibility notes are intentionally demoted and not part of the v0.5.1 path
