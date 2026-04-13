<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

## 中文

# STS2 Lobby Service

`STS2 Lobby Service` 是 `STS2 LAN Connect` 的大厅服务端，负责以下功能：

- 房间目录管理
- 房间密码校验
- 房主心跳与僵尸房间清理
- 控制通道握手与广播
- 控制通道按房间透明广播聊天 envelope，可直接承载房间聊天
- 控制通道支持房主踢人（`kick_player`）：服务端主动关闭被踢玩家的 WebSocket 并阻止其重新加入
- 控制通道支持房间设置同步（`room_settings`）：房主可开关聊天，新加入的客户端自动收到当前设置
- 向客户端返回 `ENet` 直连优先、失败时自动切 relay 的连接计划
- 保存续局大厅房间的 `savedRun` 元数据与可接管角色槽位
- `maxPlayers/currentPlayers` 上限已放宽，用于兼容扩展人数房间元数据
- 房间持久化并回显 `protocolProfile`；4 人房自动发布为 `legacy_4p` 兼容 `0.2.2`，5-8 人房保持 `extended_8p`
- 对旧 `0.2.2` 四人房，若无显式 `protocolProfile`，服务端按 `maxPlayers + modVersion + modList` 自动推断兼容档位
- 在 join 前置校验里区分 `version_mismatch`、`mod_version_mismatch`、`mod_mismatch`、`room_started`、`room_full`
- 记录 `direct_timeout` / `relay_success` / `relay_failure` 等连接阶段日志
- 内置子服务器控制面板 `/server-admin`
- 子服务器控制面板可维护大厅公告，并通过 `GET /announcements` 下发给客户端
- 子服务器控制面板显示 `未申请`、`已提交待审`、`已加入公开列表`、`已拒绝`、`配置错误`、`同步失败` 等状态，异常时弹出提醒
- 子服务器控制面板 header 与状态区支持响应式布局，移动端和桌面端窄窗口下正常显示
- 子服务器控制面板自动轮询状态时不会覆盖未保存的显示名称、带宽上限和公告草稿；手动重新加载配置前会先提示确认
- 向官方母面板自动上报公开申请、claim 令牌和 3 分钟心跳

不负责以下功能：

- 战斗同步
- 账号系统
- NAT 必成功穿透

当前 relay 的定位是"直连失败时的房间级兜底路径"，不是完整的独立联机协议。

说明：

- 官方公共服务器母面板不随公开仓库一起发布
- 当前公开仓库只保留 `lobby-service` 本体
- 如需进入官方公开列表，只需将 `SERVER_REGISTRY_BASE_URL` 指向官方母面板，并在 `/server-admin` 中打开"公开列表申请"

---

## Docker 部署

在本目录下执行：

```bash
cp deploy/lobby-service.docker.env.example deploy/lobby-service.docker.env
$EDITOR deploy/lobby-service.docker.env

docker compose -f deploy/docker-compose.lobby-service.yml build
docker compose -f deploy/docker-compose.lobby-service.yml up -d
```

默认将 `./deploy/data/lobby-service` 挂载到容器内 `/app/data`，并将 `SERVER_ADMIN_STATE_FILE` 指向 `/app/data/server-admin.json`。

如果部署机器拉取 Docker Hub 较慢，可复制 `deploy/.env.example` 为 `deploy/.env`，再将 `STS2_NODE_IMAGE` 改成国内镜像。

注意事项：

- Docker 方式同样需要将 `RELAY_PUBLIC_HOST` 或 `SERVER_REGISTRY_PUBLIC_*` 改成公网 IP / 域名
- `SERVER_REGISTRY_PUBLIC_*` 不是 Docker 自动推导的；若仍为 `127.0.0.1`、`0.0.0.0`、`localhost` 或占位值，母面板无法反向探测此子服

日志轮转由 Docker `json-file` 驱动处理，默认配置：

- 单文件上限：`10MB`
- 历史文件数：`5`

---

## systemd 部署

从仓库根目录执行：

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

安装脚本会自动执行以下操作：

- 复制服务文件到 `/opt/sts2-lobby/lobby-service`
- 执行 `npm ci`
- 执行 `npm run build`
- 首次安装时生成 `.env`
- 生成 `/opt/sts2-lobby/start-lobby-service.sh`
- 在 systemd 可用且以 root 执行时，自动安装并启动 `sts2-lobby.service`

安装后健康检查：

```bash
curl http://127.0.0.1:8787/health
```

如需让此子服进入官方公开列表，建议首次安装时带上公网主机名：

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

安装脚本会自动将 `SERVER_REGISTRY_PUBLIC_BASE_URL`、`SERVER_REGISTRY_PUBLIC_WS_URL`、`SERVER_REGISTRY_BANDWIDTH_PROBE_URL` 写成对应的公网地址。

如已完成安装，可手动编辑 `/opt/sts2-lobby/lobby-service/.env`，至少满足以下两种方式之一：

- 方式 A：配置 `RELAY_PUBLIC_HOST=<公网 IP 或域名>`
- 方式 B：显式配置全部 `SERVER_REGISTRY_PUBLIC_*`

否则母面板在收到申请后反向探测时将拿到本机地址，公开申请将失败。

---

## 生成 SERVER_ADMIN_PASSWORD_HASH

`SERVER_ADMIN_PASSWORD_HASH` 格式为 `salt:hash`，不是明文密码。

使用内置脚本生成：

```bash
cd lobby-service
npm run hash-admin-password -- '你的面板密码'
```

将输出结果填入 `.env`：

```text
SERVER_ADMIN_PASSWORD_HASH=<上一步输出的整串内容>
```

单独生成会话密钥：

```bash
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

将结果填入 `.env`：

```text
SERVER_ADMIN_SESSION_SECRET=<上一步输出的随机字符串>
```

---

## 外部访问地址

`lobby-service` 对外提供接口和管理面板，不提供供玩家直接浏览房间的独立网页大厅。

自部署后，常用的外部访问地址如下：

| 用途 | 地址 |
|------|------|
| 子服务管理面板 | `http://<公网 IP 或域名>:8787/server-admin` |
| 健康检查 | `http://<公网 IP 或域名>:8787/health` |
| 公告接口 | `http://<公网 IP 或域名>:8787/announcements` |
| 房间列表接口 | `http://<公网 IP 或域名>:8787/rooms` |

说明：

- 外部访问需放行 `8787/TCP`
- 如果使用域名，将上面的 `<公网 IP 或域名>` 替换成域名即可
- `/server-admin` 页面可直接打开，但未配置 `SERVER_ADMIN_PASSWORD_HASH` 和 `SERVER_ADMIN_SESSION_SECRET` 时无法登录修改设置
- 玩家不需要打开这些地址；玩家通过游戏客户端加入房间，浏览器页面供服主进行健康检查和管理
- 默认情况下，`/health` 只公开基础 `{ ok: true }` 响应；详细健康信息需要受信来源或有效 `LOBBY_ACCESS_TOKEN`（未设置时向后兼容回退到 `CREATE_ROOM_TOKEN`）。若 `ENFORCE_LOBBY_ACCESS_TOKEN=false`，则关闭读取令牌校验，用于兼容老版本 mod。
- 默认情况下，`/rooms` 不公开；若 `PUBLIC_ROOM_LIST_ENABLED=false`，则需要受信来源或有效 `LOBBY_ACCESS_TOKEN`（未设置时向后兼容回退到 `CREATE_ROOM_TOKEN`）。若 `ENFORCE_LOBBY_ACCESS_TOKEN=false`，则关闭读取令牌校验，用于兼容老版本 mod。
- `POST /rooms` 需要受信来源或有效 `CREATE_ROOM_TOKEN`（未设置时向后兼容回退到 `LOBBY_ACCESS_TOKEN`）。若 `ENFORCE_CREATE_ROOM_TOKEN=false`，则关闭建房令牌校验，用于兼容老版本 mod。
- 建议通过请求头 `x-lobby-access-token` / `x-create-room-token`（或 `Authorization: Bearer <token>`）传递 token，避免使用 query string 泄露到日志/历史记录

---

## 手动运行

```bash
cd /path/to/STS2_Learner/lobby-service
npm ci
npm run build
npm start
```

默认监听：

- HTTP: `http://0.0.0.0:8787`
- WebSocket: `ws://0.0.0.0:8787/control`
- Relay UDP: `udp://0.0.0.0:39000-39149`

公网部署时需放行：

- `8787/TCP`
- `39000-39149/UDP`

---

## 打包分发

将服务端单独打包：

```bash
./scripts/package-lobby-service.sh
```

产物：

- `lobby-service/release/sts2_lobby_service/`
- `lobby-service/release/sts2_lobby_service.zip`

打包结果包含：

- `Dockerfile`
- `.dockerignore`
- `deploy/docker-compose.lobby-service.yml`
- `deploy/lobby-service.docker.env.example`

如果将此服务放进公共双服务 Docker 栈运行，推荐使用宿主机网络，而非通过 Docker bridge 发布整段 relay UDP 端口。这是为了规避已在线上复现过的问题：某些云主机上，通过 bridge 映射大段 UDP relay 端口可能同时拖慢 `8787`、`18787`，甚至导致 SSH 只剩 TCP 连接而不回 banner。

---

## 环境变量

示例见 [`.env.example`](./.env.example)。

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
| `RELAY_PUBLIC_HOST` | relay 对外公网地址；若 `SERVER_REGISTRY_PUBLIC_*` 留空，服务端优先从此字段推导；未配置时退回本机地址 |
| `RELAY_PORT_START` | relay UDP 端口段起始 |
| `RELAY_PORT_END` | relay UDP 端口段结束 |
| `RELAY_HOST_IDLE_SECONDS` | relay 房主空闲超时 |
| `RELAY_CLIENT_IDLE_SECONDS` | relay 客户端空闲超时 |

### 版本与连接策略

| 变量 | 说明 |
|------|------|
| `STRICT_GAME_VERSION_CHECK` | `false` 时不因游戏版本字符串不同拒绝 join |
| `STRICT_MOD_VERSION_CHECK` | `false` 时不因 MOD 版本字符串不同拒绝 join |
| `CONNECTION_STRATEGY` | 可选 `direct-first`、`relay-first`、`relay-only`；当前公开服默认 `relay-only` |

说明：若客户端和房主均上报 `modList`，服务端会额外比对双方缺失项，并在 `mod_mismatch` 里返回 `missingModsOnLocal` / `missingModsOnHost`。

### 房间访问收口

| 变量 | 说明 |
|------|------|
| `PUBLIC_ROOM_LIST_ENABLED` | 是否公开 `GET /rooms`；默认 `false` |
| `PUBLIC_DETAILED_HEALTH_ENABLED` | 是否公开详细 `GET /health`；默认 `false` |
| `ENFORCE_LOBBY_ACCESS_TOKEN` | 是否强制校验读取令牌；默认 `true`。关闭后可兼容不会发送读取令牌的老版本 mod |
| `ENFORCE_CREATE_ROOM_TOKEN` | 是否强制校验建房令牌；默认 `true`。关闭后可兼容不会发送建房令牌的老版本 mod |
| `LOBBY_ACCESS_TOKEN` | 私有/半私有模式下的读取令牌；用于 `GET /rooms` 与详细 `GET /health`，未设置时回退到 `CREATE_ROOM_TOKEN` |
| `CREATE_ROOM_TOKEN` | 私有/半私有模式下的建房令牌；用于 `POST /rooms`，未设置时回退到 `LOBBY_ACCESS_TOKEN` |
| `CREATE_ROOM_TRUSTED_PROXIES` | 可绕过 `CREATE_ROOM_TOKEN` 的受信来源 IP/CIDR；支持 IPv4 / IPv6 / IPv4-mapped IPv6，且仅按真实 TCP 来源地址判断，不信任 `x-forwarded-for` |
| `CREATE_JOIN_RATE_LIMIT_WINDOW_MS` | 建房/加房请求限流窗口，默认 `60000` 毫秒 |
| `CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS` | 单个来源 IP 在窗口内允许的建房/加房总请求数，默认 `30` |

### 子服务管理面板

| 变量 | 说明 |
|------|------|
| `SERVER_ADMIN_USERNAME` | 管理面板用户名 |
| `SERVER_ADMIN_PASSWORD_HASH` | 管理面板密码哈希（`salt:hash` 格式） |
| `SERVER_ADMIN_SESSION_SECRET` | 会话密钥 |
| `SERVER_ADMIN_SESSION_TTL_HOURS` | 会话有效期（小时） |
| `SERVER_ADMIN_STATE_FILE` | 持久化文件路径；保存显示名称、公开设置、公告配置和同步状态 |

未配置 `SERVER_ADMIN_PASSWORD_HASH` 和 `SERVER_ADMIN_SESSION_SECRET` 时，`/server-admin` 页面可打开但无法登录修改。

### 母面板同步

| 变量 | 说明 |
|------|------|
| `SERVER_REGISTRY_BASE_URL` | 母面板地址；申请和心跳发送目标；默认为官方母面板 `http://47.111.146.69:18787` |
| `SERVER_REGISTRY_SYNC_INTERVAL_SECONDS` | 心跳同步间隔 |
| `SERVER_REGISTRY_SYNC_TIMEOUT_MS` | 同步请求超时时间 |
| `SERVER_REGISTRY_PUBLIC_BASE_URL` | 此子服对外的 HTTP 地址，上报给母面板 |
| `SERVER_REGISTRY_PUBLIC_WS_URL` | 此子服对外的 WebSocket 地址，上报给母面板 |
| `SERVER_REGISTRY_BANDWIDTH_PROBE_URL` | 带宽探针地址，上报给母面板 |
| `SERVER_REGISTRY_PROBE_FILE_BYTES` | 带宽探针文件大小 |

说明：`SERVER_REGISTRY_PUBLIC_*` 留空时，服务端先尝试用 `RELAY_PUBLIC_HOST` 推导；如果打开了公开申请但上报地址仍是 `127.0.0.1`、`0.0.0.0`、`localhost` 等本机地址，子面板会将同步状态标为 `公网地址配置错误`。

---

## API

### 通用

- `GET /health`
- `GET /probe`
- `GET /registry/bandwidth-probe.bin`
- `GET /announcements`
- `GET /rooms`

说明：

- `GET /announcements` 默认公开
- `GET /health` 默认仅返回基础 `{ ok: true }`；详细字段需公开开关或受信访问
- `GET /rooms` 默认不公开；需开启 `PUBLIC_ROOM_LIST_ENABLED` 或通过受信来源 / `LOBBY_ACCESS_TOKEN` 访问（未设置时回退到 `CREATE_ROOM_TOKEN`）。如果要兼容老版本 mod，可将 `ENFORCE_LOBBY_ACCESS_TOKEN=false`。
- 访问 token 推荐放在 `x-lobby-access-token` / `x-create-room-token` 请求头，或 `Authorization: Bearer <token>`；不建议放到 query string
- 非受信来源的 `POST /rooms` / `POST /rooms/:id/join` 会受到基于来源 IP 的轻量限流
- 公共房间列表会对续局敏感字段做裁剪；受信 / token 访问可看到完整 `savedRun` 信息

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

- 支持可选的 `savedRun`
- `savedRun.saveKey`：将续局存档与大厅房间绑定
- `savedRun.slots`：描述每个可接管角色槽位及其 `netId`
- 当请求不是受信来源时，需要有效 `CREATE_ROOM_TOKEN`
- 如需兼容不会发送建房令牌的老版本 mod，可将 `ENFORCE_CREATE_ROOM_TOKEN=false`

**`POST /rooms/:id/join`**

- 支持可选的 `desiredSavePlayerNetId`
- 支持可选的 `modList`
- 当续局房间存在多个空闲角色槽位时，客户端必须显式选择一个槽位才能加入
- 失败时明确返回 `version_mismatch`、`mod_version_mismatch`、`mod_mismatch`、`room_started`、`room_full` 等错误码

**`POST /rooms/:id/heartbeat`**

- 支持上报 `connectedPlayerNetIds`，服务端据此更新续局角色槽位的占用状态

**`POST /rooms/:id/connection-events`**

- 客户端上报 `direct_timeout`、`relay_success`、`relay_failure` 等阶段事件，进入服务端日志以便排查公网联机失败

---

## 控制通道协议

### 连接参数

| 参数 | 说明 |
|------|------|
| `roomId` | 房间 ID |
| `controlChannelId` | 控制通道 ID |
| `role` | `host` 或 `client` |
| `token` 或 `ticketId` | 鉴权凭据 |

### 当前实现

- host/client 握手校验
- ping/pong 保活
- 同房间 peers 广播

整体联机以游戏原生 `ENet` 直连为主；控制通道已足够支撑当前大厅模式。

---

## 日志排查

### systemd 部署

```bash
journalctl -u sts2-lobby.service -n 100 --no-pager
```

### Docker 部署

```bash
docker compose -f deploy/docker-compose.lobby-service.yml logs --tail 200 -f
```

### 常见日志条目

- `create room`
- `join ticket issued`
- `relay_host_registered`
- `relay_client_connected`
- `connection_event ... phase=direct_timeout`
- `connection_event ... phase=relay_success`
- `connection_event ... phase=relay_failure`
- `relay_allocated`
- `relay_removed`

### 排查要点

如果日志中出现 `create room`、`join ticket issued`，但始终没有 `relay_host_registered`，通常不是服务端 API 问题，而是客户端到 relay 端口段的 UDP 未能到达服务器。常见原因：

- 服务器公网 `39000-39149/UDP` 未放行
- 客户端使用了 `Clash`、`Surge`、系统全局代理或 `TUN`，且大厅服务器 IP 未走 `DIRECT`

---

---

## English

# STS2 Lobby Service

`STS2 Lobby Service` is the lobby server for `STS2 LAN Connect`. It is responsible for:

- Room directory management
- Room password validation
- Host heartbeat monitoring and stale room cleanup
- Control channel handshake and broadcast
- Transparent per-room broadcast of chat envelopes over the control channel
- Host kick support via the control channel (`kick_player`): the server closes the kicked player's WebSocket and prevents them from rejoining
- Room settings sync via the control channel (`room_settings`): the host can toggle chat; newly joined clients receive the current settings automatically
- Returning a connection plan to clients that prefers `ENet` direct connection with automatic relay fallback on failure
- Storing `savedRun` metadata and takeover character slots for save-continue lobby rooms
- Relaxed `maxPlayers/currentPlayers` limits for compatibility with extended-player-count room metadata
- Persistent `protocolProfile` echo; 4-player rooms are automatically published as `legacy_4p` for `0.2.2` compatibility, and 5-8 player rooms remain `extended_8p`
- Automatic compatibility-tier inference for legacy `0.2.2` 4-player rooms without an explicit `protocolProfile`, based on `maxPlayers + modVersion + modList`
- Pre-join validation that distinguishes `version_mismatch`, `mod_version_mismatch`, `mod_mismatch`, `room_started`, and `room_full`
- Connection phase logging: `direct_timeout`, `relay_success`, `relay_failure`
- Built-in sub-server admin panel at `/server-admin`
- Admin panel for managing lobby announcements delivered to clients via `GET /announcements`
- Admin panel status display covering `未申请`, `已提交待审`, `已加入公开列表`, `已拒绝`, `配置错误`, `同步失败`, with alerts on abnormal states
- Responsive layout for the admin panel header and status area, working correctly on mobile and narrow desktop windows
- Auto-polling that does not overwrite unsaved display name, bandwidth limit, or announcement drafts; a confirmation prompt is shown before manually reloading configuration
- Automatic reporting of public listing applications, claim tokens, and 3-minute heartbeats to the official master panel

It does not handle:

- Battle synchronization
- Account systems
- Guaranteed NAT traversal

The current relay is a per-room fallback path for when direct connection fails, not a standalone multiplayer protocol.

Notes:

- The official public server master panel is not published with the public repository
- The public repository contains only the `lobby-service` itself
- To join the official public listing, point `SERVER_REGISTRY_BASE_URL` at the official master panel and enable the "Public Listing Application" in `/server-admin`

---

## Docker Deployment

From this directory:

```bash
cp deploy/lobby-service.docker.env.example deploy/lobby-service.docker.env
$EDITOR deploy/lobby-service.docker.env

docker compose -f deploy/docker-compose.lobby-service.yml build
docker compose -f deploy/docker-compose.lobby-service.yml up -d
```

By default, `./deploy/data/lobby-service` is mounted to `/app/data` inside the container, with `SERVER_ADMIN_STATE_FILE` pointing to `/app/data/server-admin.json`.

If Docker Hub pulls are slow, copy `deploy/.env.example` to `deploy/.env` and set `STS2_NODE_IMAGE` to a mirror registry.

Notes:

- Docker deployments still require setting `RELAY_PUBLIC_HOST` or `SERVER_REGISTRY_PUBLIC_*` to your public IP or domain
- The `SERVER_REGISTRY_PUBLIC_*` values are not derived automatically by Docker; if they remain as `127.0.0.1`, `0.0.0.0`, `localhost`, or placeholder values, the master panel cannot probe this sub-server from the public network

Log rotation is handled by the Docker `json-file` driver with these defaults:

- Max file size: `10MB`
- Retained history files: `5`

---

## systemd Deployment

From the repository root:

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

The install script automatically:

- Copies service files to `/opt/sts2-lobby/lobby-service`
- Runs `npm ci`
- Runs `npm run build`
- Generates a `.env` file on first install
- Generates `/opt/sts2-lobby/start-lobby-service.sh`
- Installs and starts `sts2-lobby.service` via systemd when run as root with systemd available

Post-install health check:

```bash
curl http://127.0.0.1:8787/health
```

To join the official public listing, pass your public hostname at install time:

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <your public IP or domain>
```

The script will write `SERVER_REGISTRY_PUBLIC_BASE_URL`, `SERVER_REGISTRY_PUBLIC_WS_URL`, and `SERVER_REGISTRY_BANDWIDTH_PROBE_URL` using the same public address.

If already installed, manually edit `/opt/sts2-lobby/lobby-service/.env` and ensure at least one of the following:

- Option A: Set `RELAY_PUBLIC_HOST=<public IP or domain>`
- Option B: Explicitly set all `SERVER_REGISTRY_PUBLIC_*` variables

Without this, the master panel will receive a local address when probing your sub-server, causing the public listing application to fail.

---

## Generating SERVER_ADMIN_PASSWORD_HASH

`SERVER_ADMIN_PASSWORD_HASH` is not a plaintext password; the format is `salt:hash`.

Use the built-in script to generate it:

```bash
cd lobby-service
npm run hash-admin-password -- 'your-admin-password'
```

Write the output directly to `.env`:

```text
SERVER_ADMIN_PASSWORD_HASH=<output from the previous step>
```

To generate a session secret separately:

```bash
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

Write the output to `.env`:

```text
SERVER_ADMIN_SESSION_SECRET=<random string from the previous step>
```

---

## External Access URLs

`lobby-service` exposes an API and an admin panel. It does not provide a player-facing room browser.

After self-hosting, the commonly used external addresses are:

| Purpose | URL |
|---------|-----|
| Sub-server admin panel | `http://<public IP or domain>:8787/server-admin` |
| Health check | `http://<public IP or domain>:8787/health` |
| Announcements endpoint | `http://<public IP or domain>:8787/announcements` |
| Room list endpoint | `http://<public IP or domain>:8787/rooms` |

Notes:

- External access requires opening port `8787/TCP`
- Replace `<public IP or domain>` with your domain name if applicable
- The `/server-admin` page is accessible without credentials, but login and configuration changes require `SERVER_ADMIN_PASSWORD_HASH` and `SERVER_ADMIN_SESSION_SECRET` to be set
- Players do not need to access these URLs; they join rooms through the game client. The browser pages are for server operators to perform health checks and administration
- By default, `/health` only exposes a minimal `{ ok: true }` response; detailed health fields require a trusted source or a valid `LOBBY_ACCESS_TOKEN` (and fall back to `CREATE_ROOM_TOKEN` for backward compatibility if the read token is unset)
- By default, `/rooms` is not public; when `PUBLIC_ROOM_LIST_ENABLED=false`, access requires a trusted source or a valid `LOBBY_ACCESS_TOKEN` (and falls back to `CREATE_ROOM_TOKEN` for backward compatibility if the read token is unset)

---

## Manual Run

```bash
cd /path/to/STS2_Learner/lobby-service
npm ci
npm run build
npm start
```

Default listeners:

- HTTP: `http://0.0.0.0:8787`
- WebSocket: `ws://0.0.0.0:8787/control`
- Relay UDP: `udp://0.0.0.0:39000-39149`

Required open ports for public deployment:

- `8787/TCP`
- `39000-39149/UDP`

---

## Packaging for Distribution

To package the server for deployment to another machine:

```bash
./scripts/package-lobby-service.sh
```

Output artifacts:

- `lobby-service/release/sts2_lobby_service/`
- `lobby-service/release/sts2_lobby_service.zip`

The package includes:

- `Dockerfile`
- `.dockerignore`
- `deploy/docker-compose.lobby-service.yml`
- `deploy/lobby-service.docker.env.example`

If running this service inside a shared dual-service Docker stack, using host networking is recommended over Docker bridge for the relay UDP port range. This avoids a known issue observed in production where mapping a large UDP relay port range through bridge can simultaneously degrade `8787`, `18787`, and even cause SSH connections to stall without returning a banner.

---

## Environment Variables

See [`.env.example`](./.env.example) for a full example.

### Network

| Variable | Description |
|----------|-------------|
| `HOST` | Service bind address |
| `PORT` | Service listen port |
| `WS_PATH` | WebSocket path |

### Rooms and Heartbeat

| Variable | Description |
|----------|-------------|
| `HEARTBEAT_TIMEOUT_SECONDS` | Host heartbeat timeout |
| `TICKET_TTL_SECONDS` | Join ticket validity period |

### Relay

| Variable | Description |
|----------|-------------|
| `RELAY_BIND_HOST` | Relay bind address |
| `RELAY_PUBLIC_HOST` | Relay public address; used to derive `SERVER_REGISTRY_PUBLIC_*` if those are unset; falls back to localhost if not configured |
| `RELAY_PORT_START` | Relay UDP port range start |
| `RELAY_PORT_END` | Relay UDP port range end |
| `RELAY_HOST_IDLE_SECONDS` | Relay host idle timeout |
| `RELAY_CLIENT_IDLE_SECONDS` | Relay client idle timeout |

### Version and Connection Strategy

| Variable | Description |
|----------|-------------|
| `STRICT_GAME_VERSION_CHECK` | When `false`, game version string differences do not cause join rejection |
| `STRICT_MOD_VERSION_CHECK` | When `false`, mod version string differences do not cause join rejection |
| `CONNECTION_STRATEGY` | One of `direct-first`, `relay-first`, `relay-only`; public servers default to `relay-only` |

### Access Hardening

| Variable | Description |
|----------|-------------|
| `PUBLIC_ROOM_LIST_ENABLED` | Whether `GET /rooms` is public; defaults to `false` |
| `PUBLIC_DETAILED_HEALTH_ENABLED` | Whether detailed `GET /health` is public; defaults to `false` |
| `ENFORCE_LOBBY_ACCESS_TOKEN` | Whether to enforce the read token; defaults to `true`. Set to `false` to stay compatible with older mods that do not send a read token |
| `ENFORCE_CREATE_ROOM_TOKEN` | Whether to enforce the create-room token; defaults to `true`. Set to `false` to stay compatible with older mods that do not send a create token |
| `LOBBY_ACCESS_TOKEN` | Read token for private/semi-private mode; used for `GET /rooms` and detailed `GET /health`, and falls back to `CREATE_ROOM_TOKEN` when unset |
| `CREATE_ROOM_TOKEN` | Create-room token for private/semi-private mode; used for `POST /rooms`, and falls back to `LOBBY_ACCESS_TOKEN` when unset |
| `CREATE_ROOM_TRUSTED_PROXIES` | Trusted source IPs/CIDRs allowed to bypass `CREATE_ROOM_TOKEN`; supports IPv4 / IPv6 / IPv4-mapped IPv6 and is evaluated only against the real TCP peer address, not `x-forwarded-for` |
| `CREATE_JOIN_RATE_LIMIT_WINDOW_MS` | Rate-limit window for create/join requests; defaults to `60000` ms |
| `CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS` | Max create/join requests allowed per source IP within the window; defaults to `30` |

### Admin Panel

| Variable | Description |
|----------|-------------|
| `SERVER_ADMIN_USERNAME` | Admin panel username |
| `SERVER_ADMIN_PASSWORD_HASH` | Admin panel password hash (`salt:hash` format) |
| `SERVER_ADMIN_SESSION_SECRET` | Session signing secret |
| `SERVER_ADMIN_SESSION_TTL_HOURS` | Session validity period in hours |
| `SERVER_ADMIN_STATE_FILE` | Persistence file path; stores display name, public settings, announcements, and sync state |

Without `SERVER_ADMIN_PASSWORD_HASH` and `SERVER_ADMIN_SESSION_SECRET`, the `/server-admin` page can be opened but login is not possible.

### Master Panel Sync

| Variable | Description |
|----------|-------------|
| `SERVER_REGISTRY_BASE_URL` | Master panel URL; the target for applications and heartbeats; defaults to the official master panel at `http://47.111.146.69:18787` |
| `SERVER_REGISTRY_SYNC_INTERVAL_SECONDS` | Heartbeat sync interval |
| `SERVER_REGISTRY_SYNC_TIMEOUT_MS` | Sync request timeout |
| `SERVER_REGISTRY_PUBLIC_BASE_URL` | This sub-server's public HTTP URL, reported to the master panel |
| `SERVER_REGISTRY_PUBLIC_WS_URL` | This sub-server's public WebSocket URL, reported to the master panel |
| `SERVER_REGISTRY_BANDWIDTH_PROBE_URL` | Bandwidth probe URL, reported to the master panel |
| `SERVER_REGISTRY_PROBE_FILE_BYTES` | Bandwidth probe file size |

Note: if `SERVER_REGISTRY_PUBLIC_*` are unset, the server attempts to derive them from `RELAY_PUBLIC_HOST`. If the public listing application is enabled but the reported address is still `127.0.0.1`, `0.0.0.0`, or `localhost`, the admin panel will mark the sync status as a public address configuration error.

---

## API

### General

- `GET /health`
- `GET /probe`
- `GET /registry/bandwidth-probe.bin`
- `GET /announcements`
- `GET /rooms`

Notes:

- `GET /announcements` is public by default
- `GET /health` returns only a minimal `{ ok: true }` response by default; detailed fields require the public flag or trusted/token-based access
- `GET /rooms` is private by default; it requires `PUBLIC_ROOM_LIST_ENABLED=true` or trusted/token-based access
- Non-trusted `POST /rooms` / `POST /rooms/:id/join` requests are subject to a lightweight per-IP rate limit
- Public room lists now redact sensitive `savedRun` fields; trusted/token-authenticated requests can still access the full `savedRun` payload

### Room Management

- `POST /rooms`
- `POST /rooms/:id/join`
- `POST /rooms/:id/heartbeat`
- `POST /rooms/:id/connection-events`
- `DELETE /rooms/:id`

### Admin Panel

- `GET /server-admin`
- `POST /server-admin/login`
- `GET /server-admin/settings`
- `PATCH /server-admin/settings`

### WebSocket

- `WS /control`

### Save-Continue Room Fields

**`POST /rooms`**

- Accepts optional `savedRun`
- `savedRun.saveKey`: binds the save file to the lobby room
- `savedRun.slots`: describes each takeover character slot and its `netId`
- When the request is not from a trusted source, `GET /rooms` / detailed `GET /health` require a valid `LOBBY_ACCESS_TOKEN`, and `POST /rooms` requires a valid `CREATE_ROOM_TOKEN`
- If only one of the two tokens is configured, the service falls back to that token for backward compatibility
- Prefer sending the token in `x-lobby-access-token` / `x-create-room-token` headers (or `Authorization: Bearer <token>`) instead of query strings

**`POST /rooms/:id/join`**

- Accepts optional `desiredSavePlayerNetId`
- Accepts optional `modList`
- When multiple free character slots exist in a save-continue room, the client must explicitly select one before joining
- On failure, returns one of: `version_mismatch`, `mod_version_mismatch`, `mod_mismatch`, `room_started`, `room_full`

**`POST /rooms/:id/heartbeat`**

- Accepts `connectedPlayerNetIds`; the server updates which save-continue character slots are currently occupied

**`POST /rooms/:id/connection-events`**

- Clients report phase events: `direct_timeout`, `relay_success`, `relay_failure`; these are written to server logs for diagnosing public network connectivity failures

---

## Control Channel Protocol

### Connection Parameters

| Parameter | Description |
|-----------|-------------|
| `roomId` | Room ID |
| `controlChannelId` | Control channel ID |
| `role` | `host` or `client` |
| `token` or `ticketId` | Authentication credential |

### Current Implementation

- Host/client handshake validation
- ping/pong keepalive
- Per-room peer broadcast

The overall multiplayer experience is driven primarily by the game's native `ENet` direct connection. The control channel provides sufficient infrastructure for the current lobby mode.

---

## Log Troubleshooting

### systemd Deployment

```bash
journalctl -u sts2-lobby.service -n 100 --no-pager
```

### Docker Deployment

```bash
docker compose -f deploy/docker-compose.lobby-service.yml logs --tail 200 -f
```

### Common Log Entries

- `create room`
- `join ticket issued`
- `relay_host_registered`
- `relay_client_connected`
- `connection_event ... phase=direct_timeout`
- `connection_event ... phase=relay_success`
- `connection_event ... phase=relay_failure`
- `relay_allocated`
- `relay_removed`

### Diagnosis

If `create room` and `join ticket issued` appear in the logs but `relay_host_registered` never does, the issue is typically not a server API failure but rather UDP traffic from the client not reaching the relay port range. Common causes:

- The server's public port range `39000-39149/UDP` is not open
- The client is running `Clash`, `Surge`, a system-wide proxy, or a TUN interface without routing the lobby server IP through `DIRECT`
