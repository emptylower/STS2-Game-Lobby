# STS2 LAN Connect

<div align="center">

![License](https://img.shields.io/badge/license-GPL--3.0-blue)
![Client](https://img.shields.io/badge/client-v0.4.0-green)
![Service](https://img.shields.io/badge/service-v0.4.0-green)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

## 中文

**STS2 LAN Connect** 是《Slay the Spire 2》的第三方联机大厅方案。当前公开仓库以 **v0.4.0** 为准，主要服务对象是：

- 想自行部署大厅服务的服主 / 运维
- 想构建或分发客户端 MOD 的维护者
- 需要查阅接口、脚本和打包路径的开发者

> 本项目与 Mega Crit 无官方关联。《Slay the Spire 2》及相关版权归 Mega Crit 所有。

### 仓库包含什么

| 组件 | 路径 | 作用 |
|------|------|------|
| 客户端 MOD | `sts2-lan-connect/` | 游戏内大厅 UI、建房 / 加房、续局绑定、默认大厅配置 |
| 大厅服务 | `lobby-service/` | 房间目录、控制通道、管理面板、公告、relay fallback |
| 公共列表服务源码 | `server-registry/` | 自建公开列表 / 审核服务的源码与说明 |
| 文档 | `docs/` | 玩家说明、部署指南、历史兼容文档 |
| 脚本 | `scripts/` | 构建、打包、安装、同步发布产物 |

### 仓库不包含什么

- **官方生产环境的私有母面板 / 审核后台** 不随本仓库发布

### 当前版本

- 客户端 MOD：`0.4.0`
- 大厅服务：`0.4.0`

### 推荐阅读顺序

**如果你是服主 / 运维：**
1. 本页（仓库总览）
2. [`lobby-service/README.md`](./lobby-service/README.md) — 服主运维手册
3. [`docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](./docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) — 当前部署主路径
4. 如需自建公开列表，再看 [`server-registry/README.md`](./server-registry/README.md)

**如果你是客户端维护者：**
1. 本页
2. [`docs/CLIENT_RELEASE_README_ZH.md`](./docs/CLIENT_RELEASE_README_ZH.md)
3. [`docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md`](./docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md)

### 快速开始

#### 客户端 MOD

构建：

```bash
./scripts/build-sts2-lan-connect.sh
```

构建并安装到本机游戏：

```bash
./scripts/build-sts2-lan-connect.sh --install
```

打包发布：

```bash
./scripts/package-sts2-lan-connect.sh
# 输出：sts2-lan-connect/release/sts2_lan_connect-release.zip
```

#### 大厅服务

**推荐路径：systemd 安装脚本**

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

**Docker 方式：**

```bash
cp lobby-service/deploy/lobby-service.docker.env.example lobby-service/deploy/lobby-service.docker.env
$EDITOR lobby-service/deploy/lobby-service.docker.env
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml up -d
```

**手动运行：**

```bash
cd lobby-service
npm ci
npm run build
npm start
```

默认监听端口：

| 用途 | 默认值 |
|------|--------|
| HTTP / WebSocket | `8787/TCP` |
| Relay UDP | `39000-39149/UDP` |

### 项目默认大厅与公共目录

下面这些值是当前项目内置 / 文档默认引用的实际入口：

```text
大厅基地址: http://47.111.146.69:8787
控制通道: ws://47.111.146.69:8787/control
公开列表服务: http://47.111.146.69:18787
读取 / 建房 token: Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E
```

### 文档索引

| 文档 | 说明 |
|------|------|
| [`lobby-service/README.md`](./lobby-service/README.md) | 服主 / 运维手册：推荐部署路径、运维入口、环境变量、API |
| [`docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](./docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) | 当前中文部署主路径 |
| [`server-registry/README.md`](./server-registry/README.md) | 自建公开列表服务说明 |
| [`docs/CLIENT_RELEASE_README_ZH.md`](./docs/CLIENT_RELEASE_README_ZH.md) | 客户端安装 / 卸载说明 |
| [`docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md`](./docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md) | 玩家侧大厅使用说明 |
| [`docs/STS2_PEER_SIDECAR_GUIDE_ZH.md`](./docs/STS2_PEER_SIDECAR_GUIDE_ZH.md) | 历史 peer sidecar 兼容文档 |
| [`docs/STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md`](./docs/STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md) | 历史 v0.3.2 升级说明 |

### 许可证

本仓库源码以 [GPL-3.0-only](./LICENSE) 协议发布。

---

<a name="english"></a>

## English

**STS2 LAN Connect** is a third-party multiplayer lobby stack for *Slay the Spire 2*. The public repository currently documents and packages **v0.4.0**.

### What is in this repository

| Component | Path | Purpose |
|-----------|------|---------|
| Client MOD | `sts2-lan-connect/` | In-game lobby UI, room create/join, save-run binding, default lobby packaging |
| Lobby Service | `lobby-service/` | Room directory, control channel, admin panel, announcements, relay fallback |
| Public listing service source | `server-registry/` | Source and docs for self-hosted public listing / review service |
| Docs | `docs/` | Player docs, deployment guide, historical compatibility notes |
| Scripts | `scripts/` | Build, package, install, and release-sync helpers |

### What is not included

- The **official production master panel / review backend** is not published in this repository

### Current versions

- Client MOD: `0.4.0`
- Lobby Service: `0.4.0`

### Recommended reading order

**For server operators:**
1. This README
2. [`lobby-service/README.md`](./lobby-service/README.md)
3. [`docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](./docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) *(current deployment guide, Chinese)*
4. [`server-registry/README.md`](./server-registry/README.md) if you want to self-host a public listing

**For client maintainers:**
1. This README
2. [`docs/CLIENT_RELEASE_README_ZH.md`](./docs/CLIENT_RELEASE_README_ZH.md)
3. [`docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md`](./docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md)

### Quick start

**Build the client MOD**

```bash
./scripts/build-sts2-lan-connect.sh
./scripts/package-sts2-lan-connect.sh
```

**Deploy the lobby service (recommended systemd path)**

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <your public IP or domain>
```

**Current project defaults**

```text
Lobby base URL: http://47.111.146.69:8787
Control WebSocket: ws://47.111.146.69:8787/control
Registry endpoint: http://47.111.146.69:18787
Default token: Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E
```

For operator details, environment variables, and API reference, go to [`lobby-service/README.md`](./lobby-service/README.md).
