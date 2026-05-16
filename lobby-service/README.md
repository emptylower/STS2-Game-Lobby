<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

## 中文

# STS2 Lobby Service

## 文档定位

这份文档是 **大厅服务服主 / 运维手册**，面向准备部署、维护、排障或打包 `lobby-service` 的管理员。

它主要回答：

- 该服务负责什么、**不**负责什么
- 当前推荐的部署路径是什么
- 首次部署完成后先检查哪些项目
- 如何配置公开列表、私有访问、管理面板与客户端默认大厅
- 需要深入查阅时，环境变量和 API 在哪里看

## 负责 / 不负责

### 负责

- 房间目录管理
- 房间密码校验
- 房主心跳与僵尸房间清理
- 控制通道握手与按房间广播
- 房间聊天 envelope 广播
- 房主踢人（`kick_player`）
- 房间设置同步（`room_settings`）
- 生成 `ENet` 直连优先、失败后自动切 relay 的连接计划
- 保存续局房间 `savedRun` 元数据与可接管角色槽位
- 内置 `/server-admin` 管理面板
- 公告下发（`GET /announcements`）
- 公开列表申请、claim token、心跳同步

### 不负责

- 战斗同步
- 账号系统
- 保证 NAT 一定打通
- 官方私有母面板 / 审核后台发布

> 当前 relay 的定位是“直连失败时的房间级兜底路径”，不是独立完整的联机协议。

## 推荐部署路径

当前 **推荐主路径** 是：

1. 优先使用 **systemd 安装脚本** 部署到 Linux 主机
2. 在首次安装时就填好公网地址（域名优先）
3. 生成并写入 `SERVER_ADMIN_PASSWORD_HASH` 与 `SERVER_ADMIN_SESSION_SECRET`
4. 决定你的服务是 **公开列表模式** 还是 **私有 / 半私有模式**
5. 完成最小验证：`/health`、`/server-admin`、`/announcements`、`/rooms`
6. 按需为客户端重新打包默认大厅配置

如果你的环境已经标准化为容器部署，再使用 Docker；手动运行主要用于开发或临时排障。

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

### 2) 开放端口

默认需要放行：

- `8787/TCP`
- `39000-39149/UDP`

### 3) 生成管理面板密码哈希

`SERVER_ADMIN_PASSWORD_HASH` 不是明文密码，格式为 `salt:hash`。

```bash
cd lobby-service
npm run hash-admin-password -- '你的面板密码'
```

将输出写入 `.env`：

```text
SERVER_ADMIN_PASSWORD_HASH=<上一步输出的整串内容>
```

### 4) 生成会话密钥

```bash
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

写入 `.env`：

```text
SERVER_ADMIN_SESSION_SECRET=<上一步输出的随机字符串>
```

### 5) 首次检查

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/probe
curl http://127.0.0.1:8787/announcements
curl http://127.0.0.1:8787/rooms
```

并在浏览器打开：

```text
http://<公网 IP 或域名>:8787/server-admin
```

## 选择部署方式

### A. systemd（推荐）

适合长期运行的 Linux 主机，优点是路径清晰、便于日志查看、方便后续升级。

安装：

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

查看状态与日志：

```bash
systemctl status sts2-lobby
journalctl -u sts2-lobby.service -n 100 --no-pager
```

### B. Docker

适合已有容器基础设施的环境。

在 `lobby-service/` 目录执行：

```bash
cp deploy/lobby-service.docker.env.example deploy/lobby-service.docker.env
$EDITOR deploy/lobby-service.docker.env
docker compose -f deploy/docker-compose.lobby-service.yml build
docker compose -f deploy/docker-compose.lobby-service.yml up -d
```

默认将 `./deploy/data/lobby-service` 挂载到容器内 `/app/data`，并将 `SERVER_ADMIN_STATE_FILE` 指向 `/app/data/server-admin.json`。

如果 Docker Hub 拉取较慢，可复制 `deploy/.env.example` 为 `deploy/.env`，再调整 `STS2_NODE_IMAGE`。

**Docker 额外注意：**

- Docker 不会自动推导公网地址，必须手动填写 `RELAY_PUBLIC_HOST` 或全部 `SERVER_REGISTRY_PUBLIC_*`
- 如果这些值仍是 `127.0.0.1`、`0.0.0.0`、`localhost` 或占位值，公开列表反向探测会失败
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
| 公告接口 | `http://<公网 IP 或域名>:8787/announcements` |
| 房间列表接口 | `http://<公网 IP 或域名>:8787/rooms` |
| 控制通道 | `ws://<公网 IP 或域名>:8787/control` |

说明：

- 玩家通常不需要手动打开这些 URL；他们通过游戏客户端建房 / 加房
- `/server-admin` 页面可以打开，但要登录修改配置，必须设置 `SERVER_ADMIN_PASSWORD_HASH` 和 `SERVER_ADMIN_SESSION_SECRET`
- 若你在反向代理后提供 HTTPS / WSS，请确保客户端默认大厅也对应更新

## 公开列表 / 私有部署说明

### 公开列表模式（Public Listing）

如果你希望让服务出现在公共列表中，需要：

1. 设置 `SERVER_REGISTRY_BASE_URL=http://47.111.146.69:18787`
2. 配置对外可访问的公网地址：
   - `RELAY_PUBLIC_HOST=<公网 IP 或域名>`
   - 或显式设置全部 `SERVER_REGISTRY_PUBLIC_*`
3. 在 `/server-admin` 中启用“公开列表申请”

关键点：

- `SERVER_REGISTRY_BASE_URL` 表示“申请发往哪里”，**不等于** 母面板已经能反向访问你的服务
- 若上报地址仍是本机地址，`/server-admin` 会显示公网地址配置错误
- 不需要接入公开列表时，可将 `SERVER_REGISTRY_BASE_URL=` 留空

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
journalctl -u sts2-lobby.service -n 100 --no-pager
```

Docker：

```bash
docker compose -f deploy/docker-compose.lobby-service.yml logs --tail 200 -f
```

常见日志条目：

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

完整示例见 [`.env.example`](./.env.example)。以下仅保留运维最常查的分组说明。

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
| `RELAY_PUBLIC_HOST` | relay 对外公网地址；留空时可能退回本机地址 |
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

### 子服务管理面板

| 变量 | 说明 |
|------|------|
| `SERVER_ADMIN_USERNAME` | 管理面板用户名 |
| `SERVER_ADMIN_PASSWORD_HASH` | 管理面板密码哈希（`salt:hash`） |
| `SERVER_ADMIN_SESSION_SECRET` | 会话密钥 |
| `SERVER_ADMIN_SESSION_TTL_HOURS` | 会话有效期（小时） |
| `SERVER_ADMIN_STATE_FILE` | 状态持久化文件路径 |

### 公开列表同步

| 变量 | 说明 |
|------|------|
| `SERVER_REGISTRY_BASE_URL` | 公开列表服务地址 |
| `SERVER_REGISTRY_SYNC_INTERVAL_SECONDS` | 心跳同步间隔 |
| `SERVER_REGISTRY_SYNC_TIMEOUT_MS` | 同步请求超时 |
| `SERVER_REGISTRY_PUBLIC_BASE_URL` | 上报给公开列表的 HTTP 地址 |
| `SERVER_REGISTRY_PUBLIC_WS_URL` | 上报给公开列表的 WebSocket 地址 |
| `SERVER_REGISTRY_BANDWIDTH_PROBE_URL` | 上报给公开列表的带宽探针地址 |
| `SERVER_REGISTRY_PROBE_FILE_BYTES` | 带宽探针文件大小 |

## API 参考

### 通用

- `GET /health`
- `GET /probe`
- `GET /registry/bandwidth-probe.bin`
- `GET /announcements`
- `GET /rooms`

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

以下内容 **不是当前 v0.4.0 推荐主路径**，但仍保留供旧部署管理员查阅：

- `legacy_4p` / `extended_8p`、`0.2.2` / `0.2.3` 兼容叙事：用于解释历史房间协议兼容背景
- 旧中心化 / peer sidecar / `v0.3.x` 迁移资料：请改看 [`../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) 中的“历史升级与兼容说明”

---

<a name="english"></a>

## English

# STS2 Lobby Service

This README is the **operator/admin guide** for `lobby-service`.

### What it does

- Room directory and password validation
- Host heartbeat and stale room cleanup
- Control-channel handshake and room-scoped broadcast
- In-room announcements and `/server-admin` management
- Relay fallback planning when direct ENet connection fails
- Public listing application and sync integration

### What it does not do

- Battle synchronization
- Account systems
- Guaranteed NAT traversal
- Publishing the official private master panel

### Recommended path

1. Use the Linux systemd installer
2. Set a real public hostname or domain during install
3. Generate `SERVER_ADMIN_PASSWORD_HASH` and `SERVER_ADMIN_SESSION_SECRET`
4. Decide between public-listing mode and private-token mode
5. Verify `/health`, `/server-admin`, `/announcements`, and `/rooms`

Recommended install command:

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

### Public examples

```text
Admin panel: http://<public IP or domain>:8787/server-admin
Lobby API: http://<public IP or domain>:8787
Control WS: ws://<public IP or domain>:8787/control
Registry base URL: http://47.111.146.69:18787
Token: Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E
```

### Reference sections

- Environment variables: see [`.env.example`](./.env.example)
- Current deployment guide (Chinese): [`../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md)
- Historical compatibility context remains intentionally demoted and is not the recommended v0.4.0 path
