<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

# STS2 游戏大厅部署指南

> 本文档对应 **v0.4.0**（去中心化节点网络）。如果你正从 v0.3.x 升级上来，请先看文末的"v0.3.x → v0.4.0 升级要点"。

## 文档定位

这是 **当前推荐的 `lobby-service` 部署主手册**，面向：

- 自行部署大厅服务的服主 / 运维
- 需要给客户端分发默认大厅配置的维护者
- 需要核对节点网络、私有访问、最小验证步骤的管理员

推荐先读：

1. [`../README.md`](../README.md)
2. [`../lobby-service/README.md`](../lobby-service/README.md)

本文只讲 **v0.4.0 怎么部署与验证**。旧的 mother registry / SERVER_REGISTRY_* 流程已经在 v0.4.0 中删除，相关内容只在文末作为升级参考保留。

## v0.4.0 关键变更（一句话）

`lobby-service` 不再向任何"母面板"上报。每个节点自己通过 `PEER_SELF_ADDRESS` 公开对外地址，并由 Cloudflare 上的 discovery worker（`PEER_CF_DISCOVERY_BASE_URL`）做无状态聚合。`SERVER_REGISTRY_*` 一整组环境变量在运行时已经完全无效——配置了也不会生效。

## 当前推荐部署路径（主路径）

1. 在 Linux 主机上安装 `lobby-service`
2. 配置 **公网地址**（`RELAY_PUBLIC_HOST` + `PEER_SELF_ADDRESS`）、管理面板密码哈希、会话密钥
3. 决定是否加入去中心化公开节点网络（`PEER_PUBLIC_LISTING_ENABLED` + 管理面板开关）
4. 决定是否启用私有 / 半私有 token 收口
5. 验证 `/health`、`/server-admin`、`/announcements`、`/rooms`、`/peers/health`
6. 按需重新打包客户端默认大厅

项目当前公共节点与发现入口：

```text
默认社区节点(示例)：http://47.111.146.69:8787
控制通道：ws://47.111.146.69:8787/control
CF 发现入口：https://sts2-gamelobby-register.xyz
默认建房 token：Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E
```

> 注：v0.4.0 客户端通过 CF 发现入口 + 内置 seeds 聚合节点列表，不再通过任何"公开列表服务"。`http://47.111.146.69:18787` 在 v0.4.0 中已无运行时角色。

---

## 一、服务端部署

### 方案 A：systemd 安装脚本（推荐）

从仓库根目录执行：

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

脚本会自动完成：

- 将服务部署到 `/opt/sts2-lobby/lobby-service`
- 首次安装时生成 `.env`
- 执行 `npm ci` 和 `npm run build`
- 生成启动脚本
- 在 systemd 可用且以 root 执行时安装并启动 `sts2-lobby.service`

> ⚠️ **首次安装后必须手动检查 `/opt/sts2-lobby/lobby-service/.env`** 里是否含 `PEER_SELF_ADDRESS` 和 `PEER_CF_DISCOVERY_BASE_URL`。如果缺，请按本指南第二节"3. 节点网络（PEER_*）"补齐，否则 `/server-admin` 会显示"节点网络未配置"。

默认需要放行的端口：

- `8787/TCP`
- `39000-39149/UDP`

### 方案 B：Docker

在仓库根目录使用单服务编排：

```bash
cp deploy/lobby-service.env.example deploy/lobby-service.env
$EDITOR deploy/lobby-service.env   # 关键：填好 RELAY_PUBLIC_HOST 和 PEER_SELF_ADDRESS

docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml build
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml up -d
```

说明：

- 默认会将 `./deploy/data/lobby-service` 挂载到容器内 `/app/data`
- `SERVER_ADMIN_STATE_FILE` 默认指向 `/app/data/server-admin.json`
- Docker 不会自动推导公网地址，**必须手动填写 `RELAY_PUBLIC_HOST` 和 `PEER_SELF_ADDRESS`**

### 方案 C：手动运行

适合开发、本地试跑或临时排障：

```bash
cd lobby-service
npm ci
npm run build
npm start
```

默认监听：

- HTTP: `http://0.0.0.0:8787`
- WebSocket: `ws://0.0.0.0:8787/control`
- Relay UDP: `udp://0.0.0.0:39000-39149`

---

## 二、公网与管理配置

### 1. 管理面板密码哈希

`SERVER_ADMIN_PASSWORD_HASH` 必须填写 `salt:hash`，不是明文密码。

```bash
cd lobby-service
npm run hash-admin-password -- '你的面板密码'
```

将输出写入 `.env`：

```text
SERVER_ADMIN_PASSWORD_HASH=<上一步输出的完整内容>
```

### 2. 生成会话密钥

```bash
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

写入 `.env`：

```text
SERVER_ADMIN_SESSION_SECRET=<上一步输出的随机字符串>
```

### 3. 节点网络（PEER_*）—— **v0.4.0 最容易漏的一节**

`lobby-service` 默认开启节点网络（`PEER_NETWORK_ENABLED` 不显式为 `false` 即认为开启）。要真正加入节点网络，必须额外提供 **本机对外可达的 URL**：

```text
PEER_SELF_ADDRESS=http://<你的公网 IP 或域名>:8787
PEER_CF_DISCOVERY_BASE_URL=https://sts2-gamelobby-register.xyz
PEER_PUBLIC_LISTING_ENABLED=true
PEER_STATE_DIR=/app/data/peer
# 可选：覆盖客户端 picker 上显示的服务器名
# PEER_DISPLAY_NAME=My Community Lobby
```

要点：

- `PEER_SELF_ADDRESS` **必须能从公网回访这台机器**——它会被其他节点和 CF discovery worker 用来探活
- 如果走反向代理 / HTTPS / WSS，写成代理对外的真正可访问地址（含 scheme + 端口）
- 如果只想跑私有大厅、不希望出现在公共列表：保留 `PEER_SELF_ADDRESS` 不变，把 `PEER_PUBLIC_LISTING_ENABLED=false`，知道直连地址的玩家仍然能加入
- 如果想完全关掉节点网络：`PEER_NETWORK_ENABLED=false`，此时 `PEER_SELF_ADDRESS` 可留空
- 真理源：[`deploy/lobby-service.env.example`](../deploy/lobby-service.env.example)

### 4. 公网地址（Relay）

`RELAY_PUBLIC_HOST=<你的公网 IP 或域名>` 必须设置，否则 relay fallback 路径里会把本机地址下发给客户端，客户端连不上。

### 5. 常用外部地址

| 用途 | 示例地址 |
|------|----------|
| 管理面板 | `http://<公网 IP 或域名>:8787/server-admin` |
| 健康检查 | `http://<公网 IP 或域名>:8787/health` |
| 节点网络运行状态 | `http://<公网 IP 或域名>:8787/peers/health` |
| 节点指标快照 | `http://<公网 IP 或域名>:8787/peers/metrics` |
| 公告接口 | `http://<公网 IP 或域名>:8787/announcements` |
| 房间列表 | `http://<公网 IP 或域名>:8787/rooms` |
| 控制通道 | `ws://<公网 IP 或域名>:8787/control` |

说明：

- 玩家不需要在浏览器里操作这些地址；游戏客户端负责联机
- `/server-admin` 可直接打开，但未配置密码哈希和会话密钥时无法登录修改
- 如果你通过反向代理提供 HTTPS / WSS，`PEER_SELF_ADDRESS` 也要写成代理对外的实际地址

---

## 三、加入节点网络 vs 私有模式

### 加入去中心化公开节点网络

希望让这台 lobby 出现在 CF 聚合的节点列表里（玩家通过客户端 picker 看到）：

1. 把 `PEER_SELF_ADDRESS` 写成公网可达的 URL（见第二节第 3 条）
2. `PEER_PUBLIC_LISTING_ENABLED=true`
3. 重启服务，进 `/server-admin` 检查"节点网络"区域，状态应在 1-2 分钟内变为 `正在加入节点网络` 或 `已加入节点网络`

`/server-admin` 上"节点网络"状态对照：

| 状态 | 含义 |
|------|------|
| `节点网络未启用` | 设置了 `PEER_NETWORK_ENABLED=false` |
| `节点网络未配置` | 启用了但 `PEER_SELF_ADDRESS` 为空——**这是新部署最常见的错误** |
| `仅私有可见` | `PEER_SELF_ADDRESS` 已配，但 `publicListingEnabled=false` |
| `正在加入节点网络` | 已公开但还没观察到外部活跃节点 |
| `已加入节点网络` | 已观察到外部活跃节点，列表传播正常 |

### 私有 / 半私有访问收口

如果你不希望公开房间列表和详细健康信息，建议保留：

```text
PUBLIC_ROOM_LIST_ENABLED=false
PUBLIC_DETAILED_HEALTH_ENABLED=false
ENFORCE_LOBBY_ACCESS_TOKEN=true
ENFORCE_CREATE_ROOM_TOKEN=true
```

同时建议配置：

```text
LOBBY_ACCESS_TOKEN=<strong-random-read-token>
CREATE_ROOM_TOKEN=<strong-random-create-token>
CREATE_ROOM_TRUSTED_PROXIES=127.0.0.1,::1
CREATE_JOIN_RATE_LIMIT_WINDOW_MS=60000
CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS=30
```

行为说明：

- `GET /health` 默认只公开 `{ ok: true }`；详细字段需要受信来源或 `LOBBY_ACCESS_TOKEN`
- `GET /rooms` 默认不公开；需要受信来源或 `LOBBY_ACCESS_TOKEN`
- `POST /rooms` 需要受信来源或 `CREATE_ROOM_TOKEN`
- 建议通过 `x-lobby-access-token` / `x-create-room-token` 请求头传递，或使用 `Authorization: Bearer ***`
- 不建议把 token 放进 query string，避免出现在日志和浏览器历史中

> 关于"完全私有 lobby"：把 `PEER_PUBLIC_LISTING_ENABLED=false` 仅仅是不出现在公共列表，节点网络本身仍在；如果完全不想跟 CF 发现网络打交道，请同时把 `PEER_NETWORK_ENABLED=false` 并清空 `PEER_CF_DISCOVERY_BASE_URL`。

---

## 四、客户端默认大厅打包

如果你希望分发"默认就指向你这台大厅"的客户端包：

```bash
export STS2_LOBBY_DEFAULT_BASE_URL="http://<你的公网 IP 或域名>:8787"
export STS2_LOBBY_DEFAULT_WS_URL="ws://<你的公网 IP 或域名>:8787/control"
export STS2_LOBBY_DEFAULT_CREATE_ROOM_TOKEN="<服务端 .env 里的 CREATE_ROOM_TOKEN>"
export STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL="https://sts2-gamelobby-register.xyz"
export STS2_LOBBY_SEEDS_FILE="$PWD/data/seeds.json"

./scripts/package-sts2-lan-connect.sh
```

产物：

- `sts2-lan-connect/release/sts2_lan_connect/`
- `sts2-lan-connect/release/sts2_lan_connect-release.zip`

说明：

- 若未显式设置 `STS2_LOBBY_DEFAULT_WS_URL`，打包脚本会根据 `STS2_LOBBY_DEFAULT_BASE_URL` 自动推导
- 若服务端启用了 `CREATE_ROOM_TOKEN`，客户端打包时的默认值需与服务端 `.env` 保持一致
- `STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL` + `STS2_LOBBY_SEEDS_FILE` 决定客户端 picker 默认能看到 CF 聚合列表 + 内置种子
- `STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL` 在 v0.4.0 客户端里只用于诊断报告字符串，不再用于发起任何 HTTP 请求，**新部署可不设置**
- 玩家安装 / 卸载说明：[`./CLIENT_RELEASE_README_ZH.md`](./CLIENT_RELEASE_README_ZH.md)

---

## 五、验证清单

### 1. 本机最小验证

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/probe
curl http://127.0.0.1:8787/announcements
curl http://127.0.0.1:8787/rooms
curl http://127.0.0.1:8787/peers/health
curl http://127.0.0.1:8787/peers/metrics
```

`/peers/health` 关键字段：

- `publicListing: true` —— 已开启公开
- `selfAddress` —— 必须是你配的公网地址，不能是 `127.0.0.1`
- `activePeers` —— 已发现的外部活跃节点数，正常运行时 > 0

### 2. 浏览器验证

确认以下地址按部署策略正常响应：

- `http://<公网 IP 或域名>:8787/server-admin`
- `http://<公网 IP 或域名>:8787/health`
- `http://<公网 IP 或域名>:8787/announcements`
- `http://<公网 IP 或域名>:8787/rooms`

### 3. 运维验证

- `/server-admin` 可以登录
- "节点网络"状态显示 `正在加入节点网络` 或 `已加入节点网络`（公开节点）/ `仅私有可见`（私有节点）
- 公告可以保存并被客户端拉取
- 若是私有 / 半私有模式，未带 token 的访问会按预期被限制

### 4. 游戏内验证

建议在游戏中确认：

1. 大厅刷新正常
2. 公告轮播正常
3. 搜索、分页、筛选正常
4. 建房、加房、房间聊天正常
5. 如使用扩展人数补丁，房间人数元数据与实际配置一致
6. `复制本地调试报告` 功能可用

---

## 六、常见排障

### 6.1 `/server-admin` 显示"节点网络未配置"

判定逻辑在 [`lobby-service/src/server.ts`](../lobby-service/src/server.ts) 中：`PEER_NETWORK_ENABLED ≠ "false"` 且 `PEER_SELF_ADDRESS=""` 时即报"未配置"。

修复：

1. 编辑 lobby-service 真正使用的 `.env`（systemd 默认 `/opt/sts2-lobby/lobby-service/.env`）
2. 加上 `PEER_SELF_ADDRESS=http://<你的公网 IP 或域名>:8787`
3. 加上 `PEER_CF_DISCOVERY_BASE_URL=https://sts2-gamelobby-register.xyz`
4. 重启：`sudo systemctl restart sts2-lobby`
5. 等待 1-2 分钟后刷新 `/server-admin`

如果重启后日志里仍打印 `[peer] disabled (set PEER_SELF_ADDRESS to enable)`，说明 env 文件没被进程读到。可用诊断脚本：

```bash
sudo ./scripts/diagnose-lobby-peer.sh
```

### 6.2 状态卡在"正在加入节点网络"

CF discovery worker 还没拉到本节点的 announce，或本节点的 `PEER_SELF_ADDRESS` 对 CF / 其他节点不可达。

排查：

- `curl <PEER_SELF_ADDRESS>/health` 从外部机器是否能 200
- 检查防火墙 / 安全组对 `8787/TCP` 是否真正放行
- `journalctl -u sts2-lobby -n 200 | grep -E '\[peer\]|announce|bootstrap'`

### 6.3 客户端连得上 lobby 但加房后没有 relay 流量

通常是 `RELAY_PUBLIC_HOST` 没配，或 `39000-39149/UDP` 未放行。

---

## 七、v0.3.x → v0.4.0 升级要点

适用：你正从 v0.3.x systemd 或单服务 Docker 部署升级到 v0.4.0。

1. **环境变量**：删除/忽略 `SERVER_REGISTRY_BASE_URL`、`SERVER_REGISTRY_PUBLIC_BASE_URL`、`SERVER_REGISTRY_PUBLIC_WS_URL`、`SERVER_REGISTRY_BANDWIDTH_PROBE_URL`、`SERVER_REGISTRY_SYNC_INTERVAL_SECONDS`、`SERVER_REGISTRY_SYNC_TIMEOUT_MS`、`SERVER_REGISTRY_PROBE_FILE_BYTES`、`SERVER_REGISTRY_PUBLIC_HOST`、`SERVER_REGISTRY_PROBE_FILE_BYTES`。这些在 v0.4.0 lobby-service 中已经不读取了，保留不会出错，但也不会生效。
2. **新增**：`PEER_SELF_ADDRESS`、`PEER_CF_DISCOVERY_BASE_URL`、`PEER_PUBLIC_LISTING_ENABLED`、`PEER_STATE_DIR`（可选 `PEER_DISPLAY_NAME`）。
3. **server-registry 服务**：v0.4.0 不再需要单独的 server-registry。如果你之前部署了它，可以保留运行（无害），但 lobby-service 不会再向它上报，CF discovery worker 已经替代了它的角色。
4. **客户端**：v0.4.0 客户端只通过 CF discovery + 内置 seeds 聚合节点列表，不再向 `server-registry` 发请求。

历史升级与兼容资料：

- [`./STS2_PEER_SIDECAR_GUIDE_ZH.md`](./STS2_PEER_SIDECAR_GUIDE_ZH.md) —— v0.2.x → v0.3 sidecar 过渡
- [`./STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md`](./STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md) —— v0.3.2 升级说明

---

<a name="english"></a>

# STS2 Game Lobby Deployment Guide

> Targets **v0.4.0** (decentralized peer network). If you are upgrading from v0.3.x, see the upgrade notes at the end.

## Document scope

This is the **current deployment guide** for `lobby-service` and operator-facing client packaging.

Prerequisite reading:

1. [`../README.md`](../README.md)
2. [`../lobby-service/README.md`](../lobby-service/README.md)

## What changed in v0.4.0 (one sentence)

`lobby-service` no longer reports to any "master registry". Each node advertises its own externally-reachable URL via `PEER_SELF_ADDRESS` and a Cloudflare discovery worker (`PEER_CF_DISCOVERY_BASE_URL`) provides stateless aggregation. All `SERVER_REGISTRY_*` env vars are inert at runtime — setting them does nothing.

## Current recommended path

1. Install `lobby-service` on Linux
2. Configure the public address (`RELAY_PUBLIC_HOST` + `PEER_SELF_ADDRESS`), admin password hash, and session secret
3. Decide whether to join the decentralized public network (`PEER_PUBLIC_LISTING_ENABLED` + admin panel toggle)
4. Decide on public vs. private/token-gated access
5. Verify `/health`, `/server-admin`, `/announcements`, `/rooms`, `/peers/health`
6. Repackage the client defaults if needed

### Recommended install command

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <your public IP or domain>
```

> ⚠️ After install, manually verify `/opt/sts2-lobby/lobby-service/.env` contains `PEER_SELF_ADDRESS` and `PEER_CF_DISCOVERY_BASE_URL`. If missing, add them as below — otherwise `/server-admin` will show "Peer network unconfigured".

### Minimum `PEER_*` settings

```text
PEER_SELF_ADDRESS=http://<your public IP or domain>:8787
PEER_CF_DISCOVERY_BASE_URL=https://sts2-gamelobby-register.xyz
PEER_PUBLIC_LISTING_ENABLED=true
PEER_STATE_DIR=/app/data/peer
```

The canonical reference is [`deploy/lobby-service.env.example`](../deploy/lobby-service.env.example).

### Public vs. private access

- **Public node**: set `PEER_SELF_ADDRESS` to a public URL and keep `PEER_PUBLIC_LISTING_ENABLED=true`. The CF discovery worker aggregates this node into the client picker.
- **Private but networked**: `PEER_PUBLIC_LISTING_ENABLED=false`. Direct connections still work; the node just isn't aggregated.
- **Fully offline of the peer network**: `PEER_NETWORK_ENABLED=false`. Only players who know the direct URL can connect.
- **Token-gated** (private API): keep defaults `PUBLIC_ROOM_LIST_ENABLED=false`, `PUBLIC_DETAILED_HEALTH_ENABLED=false`, `ENFORCE_LOBBY_ACCESS_TOKEN=true`, `ENFORCE_CREATE_ROOM_TOKEN=true` and issue strong tokens.

### Client packaging

```bash
export STS2_LOBBY_DEFAULT_BASE_URL="http://<your public IP or domain>:8787"
export STS2_LOBBY_DEFAULT_WS_URL="ws://<your public IP or domain>:8787/control"
export STS2_LOBBY_DEFAULT_CREATE_ROOM_TOKEN="<token from server .env>"
export STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL="https://sts2-gamelobby-register.xyz"
export STS2_LOBBY_SEEDS_FILE="$PWD/data/seeds.json"
./scripts/package-sts2-lan-connect.sh
```

> `STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL` is retained only for diagnostic strings in the v0.4.0 client; you do not need to set it for new packaging.

### Verification

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/probe
curl http://127.0.0.1:8787/announcements
curl http://127.0.0.1:8787/rooms
curl http://127.0.0.1:8787/peers/health
curl http://127.0.0.1:8787/peers/metrics
```

`/peers/health` should show `publicListing: true`, `selfAddress` matching your public URL, and `activePeers > 0` once aggregation kicks in (1-2 minutes after first boot).

### Common triage

- **"Peer network unconfigured"** in `/server-admin` → `PEER_SELF_ADDRESS` missing from the env file the process actually loaded. Run `sudo ./scripts/diagnose-lobby-peer.sh`.
- **Stuck on "joining"** → `PEER_SELF_ADDRESS` not reachable from the public internet, or firewall blocks `8787/TCP`.
- **No relay traffic after a player joins** → `RELAY_PUBLIC_HOST` unset, or `39000-39149/UDP` not allowed through.

### Upgrading from v0.3.x

1. Remove (or ignore) all `SERVER_REGISTRY_*` env vars — they are inert in v0.4.0.
2. Add `PEER_SELF_ADDRESS`, `PEER_CF_DISCOVERY_BASE_URL`, `PEER_PUBLIC_LISTING_ENABLED`, `PEER_STATE_DIR`.
3. The standalone `server-registry` service is optional in v0.4.0 — `lobby-service` does not contact it. CF discovery has taken over the aggregation role.

Historical material (kept for old deployments only):

- [`./STS2_PEER_SIDECAR_GUIDE_ZH.md`](./STS2_PEER_SIDECAR_GUIDE_ZH.md)
- [`./STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md`](./STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md)
