<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

# STS2 游戏大厅部署指南

## 文档定位

这篇文档是 **当前推荐的 STS2 Lobby Service 部署主手册**，面向：

- 自行部署大厅服务的服主 / 运维
- 需要给客户端分发默认大厅配置的维护者
- 需要核对公开列表、私有访问、最小验证步骤的管理员

推荐先读：

1. [`../README.md`](../README.md)
2. [`../lobby-service/README.md`](../lobby-service/README.md)

本文只讲 **当前怎么部署与验证 v0.4.0**。旧版本升级、旧兼容策略、`v0.3.x` 过渡资料会被降级到文末“历史升级与兼容说明”。

## 当前推荐部署路径（主路径）

推荐按以下顺序完成部署：

1. 在 Linux 主机上安装 `lobby-service`
2. 配置公网地址、管理面板密码哈希、会话密钥
3. 决定是否接入公开列表
4. 决定是否启用私有 / 半私有 token 收口
5. 验证 `/health`、`/server-admin`、`/announcements`、`/rooms`
6. 按需重新打包客户端默认大厅

项目当前默认大厅与公共目录如下：

```text
大厅地址: http://47.111.146.69:8787
控制通道: ws://47.111.146.69:8787/control
公开列表服务: http://47.111.146.69:18787
建房 token: Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E
```

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

默认需要放行的端口：

- `8787/TCP`
- `39000-39149/UDP`

### 方案 B：Docker

在 `lobby-service/` 目录执行：

```bash
cp deploy/lobby-service.docker.env.example deploy/lobby-service.docker.env
$EDITOR deploy/lobby-service.docker.env
docker compose -f deploy/docker-compose.lobby-service.yml build
docker compose -f deploy/docker-compose.lobby-service.yml up -d
```

说明：

- 默认会将 `./deploy/data/lobby-service` 挂载到容器内 `/app/data`
- `SERVER_ADMIN_STATE_FILE` 默认指向 `/app/data/server-admin.json`
- Docker 不会自动推导公网地址，必须手动填写 `RELAY_PUBLIC_HOST` 或 `SERVER_REGISTRY_PUBLIC_*`

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

### 3. 公网地址

至少满足以下任一方式：

- `RELAY_PUBLIC_HOST=<你的公网 IP 或域名>`
- 或显式设置：
  - `SERVER_REGISTRY_PUBLIC_BASE_URL=http://<你的公网 IP 或域名>:8787`
  - `SERVER_REGISTRY_PUBLIC_WS_URL=ws://<你的公网 IP 或域名>:8787/control`
  - `SERVER_REGISTRY_BANDWIDTH_PROBE_URL=http://<你的公网 IP 或域名>:8787/registry/bandwidth-probe.bin`

如果这些值都留空，服务可能向外上报本机地址，导致公开列表反向探测失败。

### 4. 常用外部地址

| 用途 | 示例地址 |
|------|----------|
| 管理面板 | `http://<公网 IP 或域名>:8787/server-admin` |
| 健康检查 | `http://<公网 IP 或域名>:8787/health` |
| 公告接口 | `http://<公网 IP 或域名>:8787/announcements` |
| 房间列表 | `http://<公网 IP 或域名>:8787/rooms` |
| 控制通道 | `ws://<公网 IP 或域名>:8787/control` |

说明：

- 玩家不需要在浏览器里操作这些地址；游戏客户端负责联机
- `/server-admin` 可直接打开，但未配置密码哈希和会话密钥时无法登录修改
- 如果你通过反向代理提供 HTTPS / WSS，请确保客户端默认大厅配置也使用相同的公开地址

---

## 三、公开 / 私有访问策略

### 公开列表申请（Public Listing）

如果你希望进入公共列表：

1. 设置 `SERVER_REGISTRY_BASE_URL=http://47.111.146.69:18787`
2. 确保公网地址配置正确
3. 在 `/server-admin` 中启用“公开列表申请”

关键说明：

- `SERVER_REGISTRY_BASE_URL` 表示“申请提交到哪台列表服务”
- 它不会自动替你修正子服务对外地址
- 若上报地址仍是 `127.0.0.1`、`0.0.0.0`、`localhost` 或占位值，申请会失败或被标记为配置错误
- 如果你根本不打算接入公开列表，可将 `SERVER_REGISTRY_BASE_URL=` 留空

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

---

## 四、客户端默认大厅打包

如果你希望分发“默认就指向你这台大厅”的客户端包，可在打包前覆盖默认值：

```bash
export STS2_LOBBY_DEFAULT_BASE_URL="http://47.111.146.69:8787"
export STS2_LOBBY_DEFAULT_WS_URL="ws://47.111.146.69:8787/control"
export STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL="http://47.111.146.69:18787"
export STS2_LOBBY_DEFAULT_CREATE_ROOM_TOKEN="Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E"
export STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL="https://sts2-gamelobby-register.xyz"
export STS2_LOBBY_SEEDS_FILE="$PWD/data/seeds.json"

./scripts/package-sts2-lan-connect.sh
```

产物：

- `sts2-lan-connect/release/sts2_lan_connect/`
- `sts2-lan-connect/release/sts2_lan_connect-release.zip`

说明：

- 若未显式设置 `STS2_LOBBY_DEFAULT_WS_URL`，打包脚本会根据 `STS2_LOBBY_DEFAULT_BASE_URL` 自动推导
- 若服务端启用了 `CREATE_ROOM_TOKEN`，建议客户端打包时的默认值与服务端 `.env` 保持一致
- 若希望客户端默认 picker 直接看到 CF 聚合与内置种子，请同时设置 `STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL` 与 `STS2_LOBBY_SEEDS_FILE`
- 如需玩家安装 / 卸载说明，可继续阅读 [`./CLIENT_RELEASE_README_ZH.md`](./CLIENT_RELEASE_README_ZH.md)

---

## 五、验证清单

### 1. 本机最小验证

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/probe
curl http://127.0.0.1:8787/announcements
curl http://127.0.0.1:8787/rooms
```

### 2. 浏览器验证

确认以下地址可按你的部署策略正常响应：

- `http://<公网 IP 或域名>:8787/server-admin`
- `http://<公网 IP 或域名>:8787/health`
- `http://<公网 IP 或域名>:8787/announcements`
- `http://<公网 IP 或域名>:8787/rooms`

### 3. 运维验证

- `/server-admin` 可以登录
- 公告可以保存并被客户端拉取
- 若启用了公开列表申请，状态能进入 `pending_review`、`approved` 或 `heartbeat_ok`
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

## 六、历史升级与兼容说明

> 本节内容 **不是当前 v0.4.0 推荐主路径**。仅当你正在维护旧部署、旧客户端兼容，或查阅历史迁移资料时再看。

### 历史版本号为何仍保留在文档中

以下版本号可能仍在相关历史文档或兼容说明中出现：

- `0.2.2`
- `0.2.3`
- `0.3.0`
- `0.3.1`
- `0.3.2`

它们保留的原因是：

- 解释 `legacy_4p`、`extended_8p` 等历史协议兼容背景
- 指向旧部署升级资料
- 说明 token 强制策略在旧版本升级过程中的兼容问题

### 旧版本兼容背景

- `0.2.2` / `0.2.3` 相关叙事只适用于旧客户端 / 旧房间协议兼容说明
- `legacy_4p` 与 `extended_8p` 仍有参考价值，因为它们解释了旧房间元数据来源
- 这些内容不应再作为“当前推荐部署版本”阅读

### v0.3.x 历史迁移资料

以下资料仅适用于 **旧部署升级 / 兼容场景**：

- [`./STS2_PEER_SIDECAR_GUIDE_ZH.md`](./STS2_PEER_SIDECAR_GUIDE_ZH.md)
- [`./STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md`](./STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md)

适用语境包括：

- 你正在处理从旧中心化 / peer 过渡方案升级上来的老环境
- 你需要理解 `v0.3.1` / `v0.3.2` 的 peer 自加入或 sidecar 过渡背景
- 你需要兼容未及时升级的旧客户端

---

<a name="english"></a>

# STS2 Game Lobby Deployment Guide

## Document scope

This is the **current deployment guide** for `lobby-service` and operator-facing client packaging.

Recommended prerequisite reading:

1. [`../README.md`](../README.md)
2. [`../lobby-service/README.md`](../lobby-service/README.md)

This guide documents the **current v0.4.0 recommended path**. Older migration material is intentionally demoted to the historical compatibility section.

## Current recommended path

1. Deploy `lobby-service` on Linux
2. Configure public address, admin password hash, and session secret
3. Choose public-listing mode or private-token mode
4. Verify `/health`, `/server-admin`, `/announcements`, and `/rooms`
5. Repackage the client defaults if needed

### Recommended install command

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

### Sanitized examples

```text
Lobby URL: http://47.111.146.69:8787
Control WebSocket: ws://47.111.146.69:8787/control
Registry URL: http://47.111.146.69:18787
Create-room token: Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E
```

## Public vs private access

- Public listing: set `SERVER_REGISTRY_BASE_URL=http://47.111.146.69:18787`, configure public URLs, then enable listing in `/server-admin`
- Private mode: keep room list and detailed health private, and use strong tokens

## Client packaging

```bash
export STS2_LOBBY_DEFAULT_BASE_URL="http://47.111.146.69:8787"
export STS2_LOBBY_DEFAULT_WS_URL="ws://47.111.146.69:8787/control"
export STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL="http://47.111.146.69:18787"
export STS2_LOBBY_DEFAULT_CREATE_ROOM_TOKEN="Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E"
export STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL="https://sts2-gamelobby-register.xyz"
export STS2_LOBBY_SEEDS_FILE="$PWD/data/seeds.json"
./scripts/package-sts2-lan-connect.sh
```

如果希望打包后的服务器 picker 默认带上 CF 聚合入口和内置种子，请在打包时同时设置 `STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL` 与 `STS2_LOBBY_SEEDS_FILE`。

## Verification

Verify:

- `curl http://127.0.0.1:8787/health`
- `curl http://127.0.0.1:8787/probe`
- `curl http://127.0.0.1:8787/announcements`
- `curl http://127.0.0.1:8787/rooms`
- Browser access to `/server-admin`

## Historical compatibility

References to `0.2.2`, `0.2.3`, `0.3.0`, `0.3.1`, and `0.3.2` are kept only for old-deployment compatibility and migration context, not as current deployment recommendations.
