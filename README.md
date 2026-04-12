# STS2 LAN Connect

<div align="center">

![License](https://img.shields.io/badge/license-GPL--3.0-blue)
![Client](https://img.shields.io/badge/client-v0.2.3-green)
![Service](https://img.shields.io/badge/service-v0.2.2-green)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)

**[中文](#中文) · [English](#english)**

</div>

---

## 中文

**STS2 LAN Connect** 是《Slay the Spire 2》的第三方联机大厅方案，提供完整的房间管理、玩家匹配与 relay 中继支持。

> 本项目与 Mega Crit 无官方关联。《Slay the Spire 2》及相关版权归 Mega Crit 所有。

### 组件

| 组件 | 路径 | 说明 |
|------|------|------|
| 客户端 MOD | `sts2-lan-connect/` | 游戏内大厅 UI、建房/加房、续局绑定、调试报告 |
| 大厅服务 | `lobby-service/` | 房间目录、密码校验、心跳、控制通道、relay fallback |

当前版本：客户端 `0.2.3` · 大厅服务 `0.2.2`

### 主要特性

**大厅与 UI**
- 暖色羊皮纸像素风格 UI，基于 oklch 色彩空间精确定义，与 TypeScript 参考 UI 保持一致
- 75%/25% 锚点比例布局（房间列表:侧栏），任意分辨率自动缩放
- 固定 5 个卡片槽位/页，支持关键词搜索、分页和可叠加筛选
- 大厅公告轮播（由子服 `/server-admin` 配置下发），桌面端显示进度条与点状页码
- 可拖动房间内聊天面板，含未读提醒

**房间管理**
- 支持 2–8 人联机；5–8 人房需 `0.2.3+` 客户端
- 4 人房自动发布为 `legacy_4p`，与 `0.2.2` 兼容；5–8 人房发布为 `extended_8p`
- 房主可踢人（WebSocket 控制通道 + ENet 直连双通道执行）
- 房间设置同步（开关聊天室等），新加入客户端自动获取当前设置
- 房主可在暂停菜单执行 `重开一局`：自动返回主菜单并重启当前多人续局
- 队友在重开期间自动回到主菜单并按原续局槽位尝试自动重连；超时可手动从大厅加入
- 大厅邀请码：自动生成包含服务器地址、房间 ID 和密码的邀请码，支持跨服务器自动切换

**续局联机**
- 多人续局存档与大厅房间绑定，房主重新进入时自动重新发布
- 续局接管弹窗同时显示角色名和玩家名，方便多人选同角色时准确找回存档槽位

**加入流程**
- 加入失败细分：版本不一致、MOD 不一致、房间已开局、房间已满
- 进度弹窗支持超时后手动取消，检测到协议/兼容性错误时提前停止重试

**子服管理**
- 内置 `/server-admin` 管理面板：带宽限制、公开列表申请、公告维护
- 自动向官方母面板上报申请与 3 分钟心跳
- 响应式布局，移动端与窄屏桌面端完整支持
- 自动轮询状态时不覆盖未保存的草稿；手动重载配置前弹出确认提示

### 目录结构

```
.
├── docs/                   # 玩家说明与部署文档
├── lobby-service/          # 大厅服务源码 (Node.js / TypeScript)
│   ├── src/
│   ├── deploy/             # Docker / systemd 配置
│   └── scripts/
├── releases/               # 客户端与服务端发布产物
├── research/               # 研究资料与重建笔记
├── scripts/                # 构建、打包、安装、同步脚本
└── sts2-lan-connect/       # 客户端 MOD 源码 (GDScript / C#)
```

### 快速开始

#### 客户端

构建：

```bash
./scripts/build-sts2-lan-connect.sh
```

构建并安装到本机游戏：

```bash
./scripts/build-sts2-lan-connect.sh --install
```

打包：

```bash
./scripts/package-sts2-lan-connect.sh
# 产物：sts2-lan-connect/release/sts2_lan_connect-release.zip
```

#### 大厅服务

**systemd 一键部署（推荐）：**

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

**Docker 部署：**

```bash
cp lobby-service/deploy/lobby-service.docker.env.example lobby-service/deploy/lobby-service.docker.env
$EDITOR lobby-service/deploy/lobby-service.docker.env
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml up -d
```

**手动运行：**

```bash
cd lobby-service && npm ci && npm run build && npm start
```

默认端口：

| 用途 | 地址 |
|------|------|
| HTTP / WebSocket | `0.0.0.0:8787` |
| Relay UDP | `0.0.0.0:39000–39149` |

公网部署需放行：`8787/TCP`、`39000-39149/UDP`

#### 生成管理面板密码哈希

```bash
cd lobby-service
npm run hash-admin-password -- '你的面板密码'
# 将输出填入 .env：SERVER_ADMIN_PASSWORD_HASH=<输出值>
```

#### 加入官方公开列表

1. 在 `.env` 中设置 `SERVER_REGISTRY_BASE_URL=http://47.111.146.69:18787`
2. 确保 `RELAY_PUBLIC_HOST` 或 `SERVER_REGISTRY_PUBLIC_*` 填写公网 IP / 域名
3. 在 `/server-admin` 面板中打开"公开列表申请"

### 文档

| 文档 | 说明 |
|------|------|
| [客户端安装说明](./docs/CLIENT_RELEASE_README_ZH.md) | 玩家一键安装/卸载 |
| [客户端使用说明](./docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md) | 建房、加房、邀请码等操作指引 |
| [部署指南](./docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) | 服务端完整部署说明 |
| [大厅服务说明](./lobby-service/README.md) | API、环境变量、日志排查 |

### 许可证

本仓库源码以 [GPL-3.0-only](./LICENSE) 协议发布。

---

## English

**STS2 LAN Connect** is a third-party multiplayer lobby solution for *Slay the Spire 2*, providing room management, player matching, and relay fallback support.

> This project is not officially affiliated with Mega Crit. *Slay the Spire 2* and related IP are owned by Mega Crit.

### Components

| Component | Path | Description |
|-----------|------|-------------|
| Client MOD | `sts2-lan-connect/` | In-game lobby UI, room create/join, save-run binding, debug report |
| Lobby Service | `lobby-service/` | Room directory, password validation, heartbeat, control channel, relay fallback |

Current versions: Client `0.2.3` · Lobby Service `0.2.2`

### Key Features

**Lobby & UI**
- Warm parchment pixel-art UI with oklch-defined color palette, consistent with the TypeScript reference UI
- 75%/25% anchor-ratio layout (room list : sidebar), auto-scaling at any resolution
- Fixed 5-slot pages with keyword search, pagination, and stackable filters
- Announcement carousel (configured via `/server-admin`), with progress bar and dot indicators on desktop
- Draggable in-room chat panel with unread badge

**Room Management**
- 2–8 player support; 5–8 player rooms require client `0.2.3+`
- 4-player rooms are published as `legacy_4p` (compatible with `0.2.2`); 5–8 player rooms use `extended_8p`
- Host kick via dual channel: WebSocket control channel + ENet direct disconnect
- Room settings sync (toggle chat, etc.); new joiners auto-receive current settings
- Host can trigger `Restart Run` from the pause-menu room panel to return to main menu and restart the current multiplayer save
- Teammates are auto-routed back to main menu and auto-rejoin using their original save slot mapping; manual lobby join remains as fallback on timeout
- Lobby invite code: auto-generates a code containing server address, room ID, and password; supports cross-server auto-switching

**Save-Run Multiplayer**
- Multiplayer save runs are bound to lobby rooms; the host's room is automatically republished on re-entry
- Save-run takeover dialog shows both character name and player name, making it easy to reclaim your slot when multiple players share the same character

**Join Flow**
- Detailed join failure reasons: version mismatch, MOD mismatch, room started, room full
- Progress dialog supports manual cancel on timeout; stops retrying early on protocol/compatibility errors

**Sub-Server Admin**
- Built-in `/server-admin` panel: bandwidth throttling, public listing application, announcement management
- Auto-reports application and 3-minute heartbeat to the official registry
- Fully responsive layout for mobile and narrow desktop windows
- Auto-polling does not overwrite unsaved drafts; prompts confirmation before manual config reload

### Project Structure

```
.
├── docs/                   # Player guides and deployment documentation
├── lobby-service/          # Lobby service source (Node.js / TypeScript)
│   ├── src/
│   ├── deploy/             # Docker / systemd configurations
│   └── scripts/
├── releases/               # Published client and server builds
├── research/               # Research notes and reconstruction logs
├── scripts/                # Build, package, install, and sync scripts
└── sts2-lan-connect/       # Client MOD source (GDScript / C#)
```

### Quick Start

#### Client

Build:

```bash
./scripts/build-sts2-lan-connect.sh
```

Build and install to local game:

```bash
./scripts/build-sts2-lan-connect.sh --install
```

Package:

```bash
./scripts/package-sts2-lan-connect.sh
# Output: sts2-lan-connect/release/sts2_lan_connect-release.zip
```

#### Lobby Service

**systemd one-click deployment (recommended):**

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <your public IP or domain>
```

**Docker deployment:**

```bash
cp lobby-service/deploy/lobby-service.docker.env.example lobby-service/deploy/lobby-service.docker.env
$EDITOR lobby-service/deploy/lobby-service.docker.env
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml up -d
```

**Manual run:**

```bash
cd lobby-service && npm ci && npm run build && npm start
```

Default ports:

| Purpose | Address |
|---------|---------|
| HTTP / WebSocket | `0.0.0.0:8787` |
| Relay UDP | `0.0.0.0:39000–39149` |

Public deployment requires: `8787/TCP` and `39000-39149/UDP` open.

#### Generate Admin Password Hash

```bash
cd lobby-service
npm run hash-admin-password -- 'your-panel-password'
# Paste output into .env: SERVER_ADMIN_PASSWORD_HASH=<output>
```

#### Join the Official Public Listing

1. Set `SERVER_REGISTRY_BASE_URL=http://47.111.146.69:18787` in `.env`
2. Ensure `RELAY_PUBLIC_HOST` or `SERVER_REGISTRY_PUBLIC_*` is set to your public IP / domain
3. Enable "Public Listing Application" in the `/server-admin` panel

### Documentation

| Document | Description |
|----------|-------------|
| [Client Install Guide](./docs/CLIENT_RELEASE_README_ZH.md) | One-click install/uninstall for players (ZH) |
| [Client User Guide](./docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md) | Room creation, joining, invite codes (ZH) |
| [Deployment Guide](./docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) | Full server deployment instructions (ZH) |
| [Lobby Service README](./lobby-service/README.md) | API reference, environment variables, log troubleshooting |

### License

This repository is released under the [GPL-3.0-only](./LICENSE) license.
