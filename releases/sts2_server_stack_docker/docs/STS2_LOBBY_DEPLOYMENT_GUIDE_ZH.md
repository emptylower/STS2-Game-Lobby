<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

# STS2 游戏大厅部署指南

本文档对应公开仓库中的两个组件：

- 服务端：`lobby-service/`
- 客户端：`sts2-lan-connect/`

说明：

- 官方公共服务器母面板为私有服务，不包含在公开仓库中
- 公开仓库内的子服务可以接入官方母面板
- 当前客户端默认大厅：`http://47.111.146.69:8787`

推荐版本：

- 客户端：`0.2.3`
- 子服务：`0.2.2`

部署目标：

1. 在 Linux 机器上部署并启动 `lobby-service`
2. 按需向官方母面板申请公开展示
3. 生成带默认大厅绑定的客户端发布包
4. 在公开仓库中同步源码和发布产物

---

## 一、服务端部署

### 直接从仓库部署

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

脚本会自动执行以下操作：

- 将源码复制到 `/opt/sts2-lobby/lobby-service`
- 首次安装时生成 `.env`
- 执行 `npm ci` 和 `npm run build`
- 生成启动脚本并在 systemd 下安装和启动 `sts2-lobby.service`

默认需要放行的端口：

- `8787/TCP`
- `39000-39149/UDP`

部署完成后验证：

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/probe
```

若需要将此子服务加入官方公开列表，建议在安装时直接指定公网主机名：

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

安装脚本将自动填入以下环境变量：

- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`

若已完成安装，也可以事后手动编辑 `/opt/sts2-lobby/lobby-service/.env`，至少配置以下二选一：

- `RELAY_PUBLIC_HOST=<公网 IP 或域名>`
- 或显式填写全部 `SERVER_REGISTRY_PUBLIC_*` 字段

若以上字段均未配置，子服务将向母面板上报本机地址，母面板将无法从公网反向探测。

### 管理面板密码哈希生成

`SERVER_ADMIN_PASSWORD_HASH` 需填入 `salt:hash` 格式，而非明文密码。

使用仓库内置脚本生成：

```bash
cd lobby-service
npm run hash-admin-password -- '你的面板密码'
```

将输出内容填入 `.env`：

```text
SERVER_ADMIN_PASSWORD_HASH=<上一步输出的完整内容>
```

`SERVER_ADMIN_SESSION_SECRET` 的生成方式：

```bash
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

### 对外端点

在安全组 / 防火墙放行 `8787/TCP` 后，以下地址可从外部浏览器访问：

- 管理面板：`http://<公网 IP 或域名>:8787/server-admin`
- 健康检查：`http://<公网 IP 或域名>:8787/health`
- 公告接口：`http://<公网 IP 或域名>:8787/announcements`
- 房间列表接口：`http://<公网 IP 或域名>:8787/rooms`

说明：

- `lobby-service` 没有独立的网页大厅，玩家联机通过游戏客户端完成
- 服主通过 `/server-admin` 管理公告、公开列表申请和带宽设置
- 若页面可打开但无法登录，优先检查 `.env` 中的 `SERVER_ADMIN_PASSWORD_HASH` 和 `SERVER_ADMIN_SESSION_SECRET`
- 默认情况下，`/health` 只公开基础 `{ ok: true }` 响应；详细健康信息需要受信来源或有效 `LOBBY_ACCESS_TOKEN`（未设置时向后兼容回退到 `CREATE_ROOM_TOKEN`）
- 默认情况下，`/rooms` 不公开；若 `PUBLIC_ROOM_LIST_ENABLED=false`，则需要受信来源或有效 `LOBBY_ACCESS_TOKEN`（未设置时向后兼容回退到 `CREATE_ROOM_TOKEN`）
- `POST /rooms` 需要受信来源或有效 `CREATE_ROOM_TOKEN`（未设置时向后兼容回退到 `LOBBY_ACCESS_TOKEN`）
- 建议通过请求头 `x-lobby-access-token` / `x-create-room-token`（或 `Authorization: Bearer <token>`）传递 token，避免 query string 泄露到日志或浏览器历史

### 服务器上直接安装 / 升级

将打包产物上传并解压后，在服务器执行：

```bash
sudo ./install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

### 清理旧版本后重装

```bash
sudo systemctl stop sts2-lobby || true
sudo rm -rf /opt/sts2-lobby/lobby-service /opt/sts2-lobby/start-lobby-service.sh
sudo find /opt/sts2-lobby -maxdepth 1 -type f \( -name 'sts2_lobby_service*.zip' -o -name '*.tgz' \) -delete
sudo ./install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

### v0.3 升级 — 加入去中心化 peer 发现

> v0.3 引入了 peer 协议，让多台 lobby 互相发现并由 CF Workers 聚合给客户端，不再依赖中心化 server-registry。
> 推荐路径：升级 lobby-service 到 v0.3；不便升级的可暂用 [peer sidecar](./STS2_PEER_SIDECAR_GUIDE_ZH.md) 过渡。

**强制环境变量（v0.3 启用 peer 网络的开关）：**

- `PEER_SELF_ADDRESS=https://<lobby-public-url>`
  - 留空时 v0.3 默认禁用 peer 网络（兼容旧部署）
  - 设置后才会挂载 `/peers/*` 路由并加入 gossip
- `PEER_CF_DISCOVERY_BASE_URL=https://<cf-domain>`（推荐）
  - 启动时拉取 CF Worker `/v1/seeds` 作为引导列表
- `PEER_NETWORK_ENABLED=false`
  - 显式关闭 peer 网络（默认 true，仅在 `PEER_SELF_ADDRESS` 已设时生效）
- `PEER_STATE_DIR=./data/peer`
  - 节点 ed25519 身份与 known-peers 状态目录
- `PEER_DISPLAY_NAME="<服务器名>"`（v0.3.1+ 可选）
  - 强制覆盖客户端 picker 看到的服务器名，优先级最高
  - 留空时回退到 admin 面板里设置的 `displayName`，再回退到 `社区服务器 <host>`
  - admin 面板改名后 60 秒内自动生效，无需重启

**升级步骤：**

```bash
cd /opt/sts2-lobby/lobby-service
git pull   # 或重新解压 v0.3.0 tarball
npm ci
npm run build
# 编辑 .env 加入 PEER_SELF_ADDRESS / PEER_CF_DISCOVERY_BASE_URL
sudo systemctl restart sts2-lobby
journalctl -u sts2-lobby -f | grep '\[peer\]'
```

成功标志：日志出现

```
[peer] mounted; self=https://your-lobby.example.com cf=https://discovery.example
```

**端口与防火墙：** peer 协议复用 lobby-service 的 8787/TCP，无需开新端口。
但要确保 `PEER_SELF_ADDRESS` 指向的端口可被其它 peer 从公网到达
（用于 ed25519 challenge 探活和 heartbeat）。

**关停旧 server-registry 上报：** v0.3 启动时若未设置 `SERVER_REGISTRY_BASE_URL` 会自动跳过中心化上报循环，
日志会打印 `[server-admin] registry sync disabled until SERVER_REGISTRY_BASE_URL is configured`，无需额外操作。

**v0.2.x 客户端兼容（重要）：** v0.3 把 `ENFORCE_LOBBY_ACCESS_TOKEN` / `ENFORCE_CREATE_ROOM_TOKEN` 默认改成了 `true`。
而 v0.2.x 客户端打包的 `lobby-defaults.json` 不含 `lobbyAccessToken`，结果是：

- `GET /rooms` → **403** `room_list_disabled`
- `POST /rooms` → **503** `create_room_token_not_configured`

这会让所有还在用 v0.2.x 客户端的玩家在升级后立刻"无法连接"。
若你打算继续接收 v0.2.x 客户端，**升级 v0.3 时必须显式关闭强制校验**：

```dotenv
# v0.2.x 客户端兼容三连，写进 lobby-service.env
ENFORCE_LOBBY_ACCESS_TOKEN=false
ENFORCE_CREATE_ROOM_TOKEN=false
PUBLIC_ROOM_LIST_ENABLED=true
```

> 修改 env 后必须 `docker compose up -d --force-recreate --no-deps lobby-service`
> （`docker restart` 不会重读 env 文件）。
>
> 待所有客户端都升级到 v0.3+ 并自带令牌后，可以重新打开这三个开关，并设置 `LOBBY_ACCESS_TOKEN` / `CREATE_ROOM_TOKEN`。

### v0.3.1 增量（2026-05-09）

`v0.3.1` 在 v0.3.0 协议不变的前提下补齐了几条服务器主体验：

- **服务器自定义名通过 peer 协议端到端打通**。lobby-service 启动时把 self
  作为 `source:"self"` 插入本地 PeerStore，`/peers/health` 和 `/peers` 都会带上
  `displayName`；客户端 picker 在每次刷新时实时拉取，admin 面板改名后 60s 内
  自动反映到所有连进来的玩家界面，不需要重启。
- 客户端 picker 改成进大厅时**每次都弹**（验证期），列表充满游戏窗口；
  ping 走真实 `GET /peers/health?challenge=…`，对老 v0.2 服务器自动回退到
  `/probe`。v0.2 服务器没有 peer 端点，picker 会显示其地址直到运维升级。
- CF Worker 支持自定义域名。当前公共网格使用
  `https://sts2-gamelobby-register.xyz`（apex）作为客户端
  `cfDiscoveryBaseUrl` 默认值。

升级路径：

```bash
cd /opt/sts2-server-stack-docker
# 拉新代码
git pull
# 重建 lobby-service 镜像并重启（会重新读 env，picks up v0.3.1 自带的 self-entry）
cd deploy
docker compose -f docker-compose.public-stack.yml \
  up -d --no-deps --force-recreate lobby-service
# 看到 [peer] mounted ... displayName="<你的服务器名>" 即升级完成
docker logs sts2-lobby-service 2>&1 | grep '\[peer\] mounted'
```

如果你只想换服务器名而不动其他东西，登 `http://<your-host>:8787/server-admin`
改 `displayName`，60 秒内 `/peers` 列表 + 客户端 picker 就会刷新过来。

### v0.3.2 增量（2026-05-09）

`v0.3.2` 是一个 federation 自加入的 bug fix：v0.3.1 及更早版本部署后，
新 lobby 永远不会自动出现在公网格里——bootstrap 只能拉别人，gossip
heartbeat 又被设计成只能 refresh 已经认识的对端，所以新服务器需要人工
向某个已知节点 POST `/peers/announce` 才会被发现。

v0.3.2 在 [`server.ts`](../lobby-service/src/server.ts) 的 bootstrap 之后
追加了 `announceToBootstrappedPeers`（[`peer/auto-announce.ts`](../lobby-service/src/peer/auto-announce.ts)）：
对每个 probe 通过的对端 POST 一次 `/peers/announce`，把 self 塞进它们
的 PeerStore；下一次 CF cron 周期内（≤10 分钟）就会出现在
`/v1/servers`，对运维完全透明。

**协议 + 接口完全不变**，是纯单边升级——同时存在 v0.3.1 和 v0.3.2 节点
不会互相干扰。升级路径同 v0.3.1 增量那一节，只把 release zip 换成 v0.3.2
即可。日志多一行 `[peer] announced self to N bootstrapped peer(s)` 表示
自动加入成功。

详细操作（含 v0.3.1 及更老版本的手动 announce 兜底）见
[`STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md`](./STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md)。

---

## 二、官方公开列表

官方公共服务器母面板不在公开仓库中，但子服务默认已准备好接入。

默认配置：

- 官方母面板：`http://47.111.146.69:18787`
- 官方默认大厅：`http://47.111.146.69:8787`

`SERVER_REGISTRY_BASE_URL` 默认写为 `http://47.111.146.69:18787`，表示"申请发往哪台母面板"，不代表母面板可以自动访问到你的子服务。

在 `/server-admin` 中开启"公开列表申请"后，子服务会自动创建申请、获取审核结果并按周期发送心跳。

要让申请真正生效，必须确保母面板能访问到子服务的公网地址：

- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`

若这三项留空，服务端会尝试从 `RELAY_PUBLIC_HOST` 推导；若后者也未配置，则回退到 `127.0.0.1` / `0.0.0.0` 等本机绑定地址。

当前版本已加入显式校验：若公开申请上报的是本机地址，`/server-admin` 会直接显示 `公网地址配置错误`。

`/server-admin` 会展示以下申请状态：`未申请`、`已提交待审`、`已加入公开列表`、`已拒绝`、`申请发送失败`、`同步失败`，并在异常时弹出提醒。

若不需要接入官方公开列表，将以下配置留空即可：

```text
SERVER_REGISTRY_BASE_URL=
```

### 私有/半私有访问收口

若不希望公开房间列表和详细健康信息，可保持以下默认值：

```text
PUBLIC_ROOM_LIST_ENABLED=false
PUBLIC_DETAILED_HEALTH_ENABLED=false
```

此时：

- `GET /rooms` 需要受信来源或有效 `LOBBY_ACCESS_TOKEN`（未设置时回退到 `CREATE_ROOM_TOKEN`）
- `GET /health` 默认仅返回基础 `{ ok: true }`，详细字段需要受信来源或有效 `LOBBY_ACCESS_TOKEN`（未设置时回退到 `CREATE_ROOM_TOKEN`）
- `POST /rooms` 需要受信来源或有效 `CREATE_ROOM_TOKEN`（未设置时回退到 `LOBBY_ACCESS_TOKEN`）
- 非受信来源的 `POST /rooms` / `POST /rooms/:id/join` 会受到基于来源 IP 的轻量限流
- 公共房间列表会对续局敏感字段做裁剪；受信 / token 访问可看到完整 `savedRun` 信息

推荐同时配置：

```text
LOBBY_ACCESS_TOKEN=<strong-random-read-token>
CREATE_ROOM_TOKEN=<strong-random-create-token>
CREATE_ROOM_TRUSTED_PROXIES=127.0.0.1,::1
CREATE_JOIN_RATE_LIMIT_WINDOW_MS=60000
CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS=30
```

注意：`CREATE_ROOM_TRUSTED_PROXIES` 现在只按真实 TCP 来源地址判断，不再信任 `x-forwarded-for`，并支持 IPv4 / IPv6 / IPv4-mapped IPv6 来源；token 也建议只通过请求头传递，不要放到 query string。若只配置其中一个 token，服务端会向后兼容地回退到该 token。

### Docker 部署额外说明

Docker 不会自动填入公网地址。使用以下文件时：

- `deploy/lobby-service.docker.env.example`
- `deploy/docker-compose.lobby-service.yml`

需手动将以下占位值替换为真实公网 IP 或域名：

- `RELAY_PUBLIC_HOST`
- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`

若使用了反向代理、HTTPS 或非 `8787` 的外部端口，也需按实际情况修改。

### Docker 网络注意事项

在小规格 ECS 上通过 Docker bridge 模式映射大段 UDP relay 端口时，可能出现 `8787` 空响应、`18787` 超时，严重时甚至导致 SSH banner 无法返回。

根本原因在于 "Docker bridge + 大段 UDP 端口映射" 这一网络模型本身，而非业务逻辑问题。

建议将 `lobby-service` 改为使用宿主机网络（`--network host`），以避免此类故障。在私有环境中同机部署"子服务 + 私有母面板"时，同样建议避开大段 UDP bridge 发布方式。

---

## 三、客户端打包

### 1. 使用仓库默认大厅

仓库内的 [`lobby-defaults.json`](../sts2-lan-connect/lobby-defaults.json) 默认指向：

- `baseUrl`: `http://47.111.146.69:8787`
- `registryBaseUrl`: `http://47.111.146.69:18787`
- `createRoomToken`: `Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E`
- `wsUrl`: `ws://47.111.146.69:8787/control`

若不设置额外的环境变量，打包出的客户端将使用上述默认大厅与默认建房令牌。

### 2. 生成客户端包

```bash
./scripts/package-sts2-lan-connect.sh
```

产物：

- `sts2-lan-connect/release/sts2_lan_connect/`
- `sts2-lan-connect/release/sts2_lan_connect-release.zip`

### 3. 临时覆盖默认大厅

```bash
export STS2_LOBBY_DEFAULT_BASE_URL="http://<your-host-or-domain>:8787"
export STS2_LOBBY_DEFAULT_WS_URL="ws://<your-host-or-domain>:8787/control"
export STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL="http://<your-registry-host-or-domain>:18787"
export STS2_LOBBY_DEFAULT_CREATE_ROOM_TOKEN="<your-create-room-token>"

./scripts/package-sts2-lan-connect.sh
```

若不显式设置 `STS2_LOBBY_DEFAULT_WS_URL`，打包脚本会根据 `STS2_LOBBY_DEFAULT_BASE_URL` 自动推导。
如需让分发给客户端的默认建房令牌与服务端一致，可同时设置 `STS2_LOBBY_DEFAULT_CREATE_ROOM_TOKEN`，并在服务端 `.env` 中保持 `CREATE_ROOM_TOKEN` 同值。

---

## 四、客户端安装与卸载

### macOS

双击 `install-sts2-lan-connect-macos.command`，或在命令行执行：

```bash
./install-sts2-lan-connect-macos.sh --install --package-dir .
```

### Windows

双击 `install-sts2-lan-connect-windows.bat`，或在 PowerShell 执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Install -PackageDir .
```

---

## 五、公开仓库同步

本地已 clone 公开仓库 `STS-Game-Lobby` 后执行：

```bash
./scripts/sync-release-repo.sh --repo-dir ~/Desktop/STS-Game-Lobby
```

同步结果：

- 源码目录同步至公开仓库根目录
- 发布产物集中同步至公开仓库 `releases/`
- 私有母面板的源码、脚本和 release 产物不会同步

---

## 六、游戏内验证

部署完成后，建议在游戏内确认以下项目：

1. 大厅刷新正常
2. 顶部公告轮播正常
3. 搜索、分页、筛选正常
4. 进房后房间聊天可双向收发消息，未读角标和拖动保存位置正常
5. 建房和加入房间正常；使用扩展人数补丁时，确认房间人数元数据与实际配置一致
6. `复制本地调试报告` 功能可用
7. 外部浏览器可访问 `http://<公网 IP 或域名>:8787/server-admin`
8. 子服 `/server-admin` 可登录并维护大厅公告
9. 若已开启"公开列表申请"，确认同步状态为 `pending_review`、`approved` 或 `heartbeat_ok`

---

<a name="english"></a>

# STS2 Game Lobby Deployment Guide

This document covers two components in the public repository:

- Server: `lobby-service/`
- Client: `sts2-lan-connect/`

Notes:

- The official public server master panel is a private service and is not included in the public repository
- Sub-services in the public repository can connect to the official master panel
- Current client default lobby: `http://47.111.146.69:8787`

Recommended versions:

- Client: `0.2.3`
- Sub-service: `0.2.2`

Deployment goals:

1. Deploy and start `lobby-service` on a Linux machine
2. Register with the official master panel for public listing when needed
3. Generate a client release package with a bound default lobby
4. Sync source code and release artifacts to the public repository

---

## 1. Server Deployment

### Method A: Deploy Directly from Repository

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

The script automatically:

- Copies source to `/opt/sts2-lobby/lobby-service`
- Generates `.env` on first install
- Runs `npm ci` and `npm run build`
- Generates a startup script and installs and starts `sts2-lobby.service` under systemd

Default ports to open:

- `8787/TCP`
- `39000-39149/UDP`

Verify after deployment:

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/probe
```

If you plan to join the official public listing, specify the public hostname at install time:

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <your-public-ip-or-domain>
```

The install script will automatically populate:

- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`

If you have already installed, you can edit `/opt/sts2-lobby/lobby-service/.env` manually. Set at least one of the following:

- `RELAY_PUBLIC_HOST=<public-ip-or-domain>`
- Or explicitly fill in all `SERVER_REGISTRY_PUBLIC_*` fields

If none of these are set, the sub-service will report its local address to the master panel, making reverse probing from the internet impossible.

### Admin Panel Password Hash

`SERVER_ADMIN_PASSWORD_HASH` must be set in `salt:hash` format, not as a plaintext password.

Generate it using the bundled script:

```bash
cd lobby-service
npm run hash-admin-password -- 'your-panel-password'
```

Paste the full output into `.env`:

```text
SERVER_ADMIN_PASSWORD_HASH=<full output from the previous step>
```

Generate `SERVER_ADMIN_SESSION_SECRET`:

```bash
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

### External Endpoints

Once `8787/TCP` is open in your firewall or security group, the following are accessible from an external browser:

- Admin panel: `http://<public-ip-or-domain>:8787/server-admin`
- Health check: `http://<public-ip-or-domain>:8787/health`
- Announcements: `http://<public-ip-or-domain>:8787/announcements`
- Room list: `http://<public-ip-or-domain>:8787/rooms`

Notes:

- `lobby-service` does not provide a standalone web lobby for players; multiplayer is handled through the game client
- Server operators manage announcements, public listing, and bandwidth settings via `/server-admin`
- If the page loads but login fails, check `SERVER_ADMIN_PASSWORD_HASH` and `SERVER_ADMIN_SESSION_SECRET` in `.env`

### Method B: Package Locally, Then Deploy to Server

Run the packaging script locally:

```bash
./scripts/package-lobby-service.sh
```

Artifacts:

- `lobby-service/release/sts2_lobby_service/`
- `lobby-service/release/sts2_lobby_service.zip`

After uploading and extracting on the server, run:

```bash
sudo ./install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

### Method C: Remove Old Version and Reinstall

```bash
sudo systemctl stop sts2-lobby || true
sudo rm -rf /opt/sts2-lobby/lobby-service /opt/sts2-lobby/start-lobby-service.sh
sudo find /opt/sts2-lobby -maxdepth 1 -type f \( -name 'sts2_lobby_service*.zip' -o -name '*.tgz' \) -delete
sudo ./install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

---

## 2. Official Public Listing

The official master panel is not in the public repository, but sub-services are ready to connect to it by default.

Default configuration:

- Official master panel: `http://47.111.146.69:18787`
- Official default lobby: `http://47.111.146.69:8787`

`SERVER_REGISTRY_BASE_URL` defaults to `http://47.111.146.69:18787`, indicating where registration requests are sent. It does not guarantee the master panel can reach your sub-service.

When "Public Listing" is enabled in `/server-admin`, the sub-service automatically creates a registration request, retrieves the review result, and sends heartbeats on a fixed interval.

For the registration to succeed, the master panel must be able to reach the sub-service's public address via:

- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`

If these are left empty, the server will try to derive them from `RELAY_PUBLIC_HOST`. If that is also unset, it falls back to local bind addresses like `127.0.0.1` / `0.0.0.0`.

The current version includes explicit validation: if the reported address is a local address, `/server-admin` will show `Public address misconfigured`.

`/server-admin` displays the following listing states: `Not registered`, `Submitted, pending review`, `Approved and listed`, `Rejected`, `Submission failed`, `Sync failed`, with alerts on errors.

To opt out of the official public listing, leave the following empty:

```text
SERVER_REGISTRY_BASE_URL=
```

### Docker Deployment Notes

Docker does not automatically populate public addresses. When using:

- `deploy/lobby-service.docker.env.example`
- `deploy/docker-compose.lobby-service.yml`

Replace all placeholder values with real public IPs or domain names:

- `RELAY_PUBLIC_HOST`
- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`

If you use a reverse proxy, HTTPS, or an external port other than `8787`, update these fields accordingly.

### Docker Network Warning

When publishing a large range of UDP relay ports through Docker bridge mode on a small-spec ECS instance, you may encounter empty responses on `8787`, timeouts on `18787`, and in severe cases, an SSH connection that hangs without returning a banner.

The root cause is the "Docker bridge + large UDP port range" network model, not the application logic.

It is recommended to run `lobby-service` using host networking (`--network host`) to avoid this class of failure. The same applies when co-deploying a sub-service and a private master panel on the same host.

---

## 3. Client Packaging

### 1. Using the Repository Default Lobby

The repository's [`lobby-defaults.json`](../sts2-lan-connect/lobby-defaults.json) points to:

- `baseUrl`: `http://47.111.146.69:8787`
- `registryBaseUrl`: `http://47.111.146.69:18787`
- `wsUrl`: `ws://47.111.146.69:8787/control`

Without additional environment variables, packaged clients will use these defaults.

### 2. Build the Client Package

```bash
./scripts/package-sts2-lan-connect.sh
```

Artifacts:

- `sts2-lan-connect/release/sts2_lan_connect/`
- `sts2-lan-connect/release/sts2_lan_connect-release.zip`

### 3. Override the Default Lobby

```bash
export STS2_LOBBY_DEFAULT_BASE_URL="http://<your-host-or-domain>:8787"
export STS2_LOBBY_DEFAULT_WS_URL="ws://<your-host-or-domain>:8787/control"
export STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL="http://<your-registry-host-or-domain>:18787"

./scripts/package-sts2-lan-connect.sh
```

If `STS2_LOBBY_DEFAULT_WS_URL` is not set, the packaging script will derive it from `STS2_LOBBY_DEFAULT_BASE_URL`.

---

## 4. Client Installation and Uninstallation

### macOS

Double-click `install-sts2-lan-connect-macos.command`, or run from the command line:

```bash
./install-sts2-lan-connect-macos.sh --install --package-dir .
```

### Windows

Double-click `install-sts2-lan-connect-windows.bat`, or run in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Install -PackageDir .
```

---

## 5. Public Repository Sync

Once you have cloned the `STS-Game-Lobby` public repository locally:

```bash
./scripts/sync-release-repo.sh --repo-dir ~/Desktop/STS-Game-Lobby
```

Sync results:

- Source directories are synced to the root of the public repository
- Release artifacts are collected into `releases/` in the public repository
- Private master panel source, scripts, and release artifacts are excluded

---

## 6. In-Game Verification Checklist

After deployment, verify the following in the game client:

1. Lobby refreshes correctly
2. Top announcement carousel displays correctly
3. Search, pagination, and filtering work correctly
4. Room chat sends and receives messages in both directions; unread badge and drag-to-reposition work correctly
5. Room creation and joining work correctly; if using the extended player count patch, confirm room size metadata matches the actual configuration
6. "Copy local debug report" is functional
7. External browser can open `http://<public-ip-or-domain>:8787/server-admin`
8. Sub-service `/server-admin` login works and announcements can be managed
9. If public listing is enabled, confirm the sync state reaches `pending_review`, `approved`, or `heartbeat_ok`
