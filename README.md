# STS2 LAN Connect

<div align="center">

![License](https://img.shields.io/badge/license-GPL--3.0-blue)
![Client](https://img.shields.io/badge/client-v0.5.2--candidate-yellow)
![Service](https://img.shields.io/badge/service-v0.5.1-green)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

## 中文

**STS2 LAN Connect** 是《Slay the Spire 2》的第三方联机大厅方案。当前分支准备客户端 **v0.5.2 候选**，大厅服务继续使用 **v0.5.1**，主要服务对象是：

- 想自行部署大厅服务的服主 / 运维
- 想构建或分发客户端 MOD 的维护者
- 需要查阅接口、脚本和打包路径的开发者

> 本项目与 Mega Crit 无官方关联。《Slay the Spire 2》及相关版权归 Mega Crit 所有。

### 仓库包含什么

| 组件 | 路径 | 作用 |
|------|------|------|
| 客户端 MOD | `sts2-lan-connect/` | 游戏内大厅 UI、建房 / 加房、续局绑定、服务器频道与房间富聊天 |
| 大厅服务 | `lobby-service/` | 房间目录、聊天网关、管理面板、公告、relay fallback、加入去中心化节点网络 |
| (可选) 公共列表服务源码 | `server-registry/` | v0.3.x 时代的母面板源码；v0.5.1 不再需要，仅供想自托管列表服务的运维参考 |
| 文档 | `docs/` | 玩家说明、部署指南、历史兼容文档 |
| 脚本 | `scripts/` | 构建、打包、安装、同步发布产物 |

### 当前架构（v0.4.0 引入）

- 节点之间通过 `lobby-service` 内置的 peer-announce 协议彼此发现
- 客户端通过 Cloudflare discovery worker（`https://sts2-gamelobby-register.xyz`）拿到聚合节点列表
- 不再有任何"母面板"或中心化审核后台；`SERVER_REGISTRY_*` 一组环境变量自 v0.4.0 起已从 lobby-service 中完全移除

### v0.5.2 客户端候选要点

- Android 点击“引用”按钮、桌面按 `Alt+R` 进入一次性引用模式；原有 `Alt+左键` 继续保留
- 成功引用一个卡牌、遗物、药水、状态或玩家后自动退出，并回到真实文本输入位置继续中文输入
- 混合文字、Emoji 和引用使用单一行内控件自然换行；原生预览显示完整动态 Power 说明
- Android 可点击消息引用打开固定预览，并通过点击外部、`Esc` 或关闭按钮退出
- 客户端继续使用 v0.5.1 聊天协议和 lobby-service 0.5.1；不改变 MOD 同步与加入流程
- 同一个 v0.5.2 客户端包以游戏 `0.107.1` 与 `0.109.0` 为加载目标

### 当前版本

- 客户端 MOD：`0.5.2` 候选
- 大厅服务：`0.5.1`
- GitHub 正式版：仍为 [`v0.5.1`](https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.1)，玩家确认后才发布 v0.5.2
- Steam 创意工坊：[`游戏大厅`](https://steamcommunity.com/sharedfiles/filedetails/?id=3749766330)

### 推荐阅读顺序

**如果你是服主 / 运维：**
1. 本页（仓库总览）
2. [`lobby-service/README.md`](./lobby-service/README.md) — 服主运维手册
3. [`docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](./docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) — 当前部署主路径
4. (可选) 想自托管完整公共列表服务时再看 [`server-registry/README.md`](./server-registry/README.md)（v0.5.1 不依赖它）

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
export STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL="https://sts2-gamelobby-register.xyz"
export STS2_LOBBY_SEEDS_FILE="$PWD/data/seeds.json"
./scripts/package-sts2-lan-connect.sh
# 输出：sts2-lan-connect/release/sts2_lan_connect-release.zip
```

客户端验证：

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

> 公开客户端包必须包含 `lobby-defaults.json`，且其中应带有 `cfDiscoveryBaseUrl=https://sts2-gamelobby-register.xyz` 与 `data/seeds.json` 中的内置种子；`scripts/package-sts2-lan-connect.sh` 会在打包时强制检查这些字段。

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
默认社区节点(示例): http://47.111.146.69:8787
控制通道: ws://47.111.146.69:8787/control
CF 发现入口: https://sts2-gamelobby-register.xyz
默认建房 token: Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E
```

> `http://47.111.146.69:18787`（v0.3.x 的母面板入口）自 v0.4.0 起已无运行时角色，仅为兼容旧客户端而保留可达。

### 客户端无障碍与键盘操作

v0.5.1 客户端大厅支持键盘 / 手柄式焦点导航，房间卡片可聚焦，`Enter` / `Space` / `ui_accept` 可对当前房间执行加入操作；`Esc` 优先关闭最上层弹窗，再退出大厅。若检测到 `say-the-spire2` 盲人辅助模组，会通过反射软桥接把大厅焦点交给该模组朗读，不安装该模组时无额外依赖。

- `F7`：剪贴板有有效邀请码时直接弹出加入确认；邀请确认弹窗已打开时执行加入。
- `F8`：进入房间后切换右上角房间聊天面板。
- 复制有效邀请码后点击 `游戏大厅` 会跳过服务器选择器，直接进入大厅并显示邀请确认。

### 聊天治理与发布边界

- 服务器频道历史只存在于当前 `lobby-service` 节点的有界进程内存中，服务重启即清空；房间聊天不保存历史，节点之间也不复制聊天。聊天昵称来自未验证的客户端 session，只能展示，不能用于身份认证或授权。
- 每次建房/续局重新发布都会产生新的 `roomSessionId` generation。战斗引用必须属于当前 generation；旧 power/player/monster 引用在客户端安全降级，静态卡牌、遗物、药水和相邻文本不受影响。
- 旧客户端 fallback 先按原顺序保留最多 60 个 UTF-16 unit 的全部用户文本，再在剩余预算内追加完整的通用实体占位符；不会拆代理对或输出 model ID。monster target 仍是双客户端稳定 ID 原型 gate，发布版本保持关闭。
- `/server-admin` 把六个治理开关写入现有 `SERVER_ADMIN_STATE_FILE`，落盘成功后才按序广播；消息、指标和历史不写入该文件。环境变量只为缺失键提供默认值，已持久化值在重启后优先。关闭 rich 只让 Emoji/item/combat 的有效版本归零，不抹掉子开关；关闭 room-v2 会关闭 combat-v2，但 legacy 房间文本仍可用，服务器频道开关独立。
- 三个配置来源必须保持同一组默认值：`lobby-service/.env.example`、`deploy/lobby-service.env.example`、`lobby-service/deploy/sts2-lobby.service.example`。分阶段回滚时先关闭 combat refs，再关闭 Emoji/item refs 与 rich，最后按需关闭 room-v2；服务器频道可独立回滚。
- 安装预检使用 `./scripts/build-sts2-lan-connect.sh --install --dry-run`。发布验证只在临时输出目录生成和检查包，不读写 `releases/`。公开包不得包含 `typing.dll`、游戏程序集、游戏图片/字体或除本 MOD PCK 外的游戏 PCK。

### 文档索引

| 文档 | 说明 |
|------|------|
| [`CHANGELOG.md`](./CHANGELOG.md) | 客户端与服务端版本更新日志 |
| [`docs/RELEASE_NOTES_V0.5.2_ZH.md`](./docs/RELEASE_NOTES_V0.5.2_ZH.md) | v0.5.2 客户端候选说明、引用操作、兼容范围与回滚步骤 |
| [`docs/RELEASE_NOTES_V0.5.1_ZH.md`](./docs/RELEASE_NOTES_V0.5.1_ZH.md) | v0.5.1 发布说明、升级与回滚步骤 |
| [`lobby-service/README.md`](./lobby-service/README.md) | 服主 / 运维手册：推荐部署路径、运维入口、环境变量、API |
| [`docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](./docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) | 当前中文部署主路径（v0.5.1） |
| [`docs/STS2_SERVER_DOCKER_OPERATION_GUIDE_ZH.md`](./docs/STS2_SERVER_DOCKER_OPERATION_GUIDE_ZH.md) | Docker 部署与运维指南（v0.5.1 单容器 + v0.3.x 双服务栈兼容路径） |
| [`docs/CLIENT_RELEASE_README_ZH.md`](./docs/CLIENT_RELEASE_README_ZH.md) | 客户端安装 / 卸载说明 |
| [`docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md`](./docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md) | 玩家侧大厅使用说明 |
| [`server-registry/README.md`](./server-registry/README.md) | (可选) 自托管公共列表服务源码说明，v0.5.1 不再依赖 |
| [`docs/STS2_PEER_SIDECAR_GUIDE_ZH.md`](./docs/STS2_PEER_SIDECAR_GUIDE_ZH.md) | 历史：v0.2.x → v0.3 peer sidecar 兼容文档 |
| [`docs/STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md`](./docs/STS2_LOBBY_OPERATOR_UPGRADE_V0.3.2_ZH.md) | 历史：v0.3.2 升级说明 |

### 许可证

本仓库源码以 [GPL-3.0-only](./LICENSE) 协议发布。

---

<a name="english"></a>

## English

**STS2 LAN Connect** is a third-party multiplayer lobby stack for *Slay the Spire 2*. This branch prepares the **v0.5.2 client candidate** while the lobby service remains at **v0.5.1**.

### What is in this repository

| Component | Path | Purpose |
|-----------|------|---------|
| Client MOD | `sts2-lan-connect/` | In-game lobby UI, room create/join, save-run binding, server-channel and rich-room chat |
| Lobby Service | `lobby-service/` | Room directory, chat gateways, admin panel, announcements, relay fallback, decentralized peer-network membership |
| (Optional) Public listing service source | `server-registry/` | Source for v0.3.x-style self-hosted public listing service; not required in v0.5.1 |
| Docs | `docs/` | Player docs, deployment guide, historical compatibility notes |
| Scripts | `scripts/` | Build, package, install, and release-sync helpers |

### Current architecture (introduced in v0.4.0)

Each `lobby-service` node advertises itself to peers via the built-in peer-announce protocol. Clients aggregate the public node list through a Cloudflare discovery worker (`https://sts2-gamelobby-register.xyz`). There is no master panel and no central review backend; the `SERVER_REGISTRY_*` env vars from v0.3.x have been removed from `lobby-service` and have been inert since v0.4.0.

### v0.5.2 client candidate highlights

- Android taps the Reference button and desktop presses `Alt+R` to arm one-shot references; the existing `Alt+left-click` path remains available.
- One successful card, relic, potion, power, or player capture exits the mode and restores the real text input for continued composition.
- Text, Emoji, and references flow through one inline control, with native previews and complete dynamic Power descriptions.
- Android taps message references to open pinned previews and dismisses them via outside tap, `Esc`, or the close button.
- The client retains the v0.5.1 chat protocol, lobby-service 0.5.1, MOD synchronization, and join behavior.
- One v0.5.2 client package targets game versions `0.107.1` and `0.109.0`.

### Current versions

- Client MOD: `0.5.2` candidate
- Lobby Service: `0.5.1`
- GitHub release: still [`v0.5.1`](https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.1); v0.5.2 waits for player acceptance
- Steam Workshop: [`游戏大厅`](https://steamcommunity.com/sharedfiles/filedetails/?id=3749766330)

### Recommended reading order

**For server operators:**
1. This README
2. [`lobby-service/README.md`](./lobby-service/README.md)
3. [`docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](./docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) *(current deployment guide, Chinese)*
4. (Optional) [`server-registry/README.md`](./server-registry/README.md) only if you want to self-host the v0.3.x-style public listing service — v0.5.1 itself does not require it

**For client maintainers:**
1. This README
2. [`docs/CLIENT_RELEASE_README_ZH.md`](./docs/CLIENT_RELEASE_README_ZH.md)
3. [`docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md`](./docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md)

### Quick start

**Build the client MOD**

```bash
./scripts/build-sts2-lan-connect.sh
STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL="https://sts2-gamelobby-register.xyz" \
STS2_LOBBY_SEEDS_FILE="$PWD/data/seeds.json" \
./scripts/package-sts2-lan-connect.sh
```

**Verify the client MOD**

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

Public client packages must include `lobby-defaults.json` with `cfDiscoveryBaseUrl=https://sts2-gamelobby-register.xyz` and bundled seed peers from `data/seeds.json`; `scripts/package-sts2-lan-connect.sh` now fails the package if those runtime defaults are missing.

**Deploy the lobby service (recommended systemd path)**

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <your public IP or domain>
```

**Current project defaults**

```text
Sample community node: http://47.111.146.69:8787
Control WebSocket: ws://47.111.146.69:8787/control
CF discovery worker: https://sts2-gamelobby-register.xyz
Default create-room token: Jsp-vspQBS8jI1L0aFshxr-wHZo2dyhSsYGvgh-QI8E
```

> The legacy `http://47.111.146.69:18787` registry endpoint has had no runtime role since v0.4.0; it is kept reachable only for older clients.

### Client accessibility and keyboard operation

The v0.5.1 client lobby supports keyboard/controller-style focus navigation. Room cards are focusable, `Enter` / `Space` / `ui_accept` joins the focused room, and `Esc` closes the topmost dialog before leaving the lobby. If the `say-the-spire2` accessibility mod is present, the lobby soft-bridges focus announcements to it through reflection; without that mod, no extra dependency is required.

- `F7`: opens the invite confirmation when the clipboard contains a valid invite; accepts the visible invite confirmation when it is already open.
- `F8`: toggles the room chat panel after joining a room.
- Copying a valid invite before clicking `Game Lobby` skips the server picker and opens the lobby invite confirmation directly.

### Chat governance and release boundaries

- Server-channel history is bounded, node-local process memory and is lost on restart. Room chat has no retained history, and peer nodes do not replicate chat. Display nicknames come from unverified client session data and are never authentication or authorization claims.
- Every room creation or continue-run republish establishes a new `roomSessionId` generation. Combat references must match the active generation; stale power/player/monster references degrade locally without damaging static item links or adjacent text.
- Legacy fallback spends at most 60 UTF-16 units on all user text first, in original order, then appends only complete generic entity placeholders that still fit. It never splits a surrogate pair or exposes model IDs. Monster targets remain hard-disabled behind the unproven two-client stable-ID prototype gate.
- `/server-admin` persists the six governance toggles in the existing `SERVER_ADMIN_STATE_FILE` before broadcasting ordered state. Messages, metrics, and history are not persisted. Environment values seed missing keys only; persisted values win after restart. Disabling rich content clears effective Emoji/item/combat versions without erasing child toggles; disabling room-v2 clears combat-v2 while legacy room text remains available; server chat is independent.
- Keep these three default surfaces in parity: `lobby-service/.env.example`, `deploy/lobby-service.env.example`, and `lobby-service/deploy/sts2-lobby.service.example`. For staged rollback, disable combat refs first, then Emoji/item refs and rich content, and only then room-v2 if needed; roll back server chat separately.
- Preflight an install with `./scripts/build-sts2-lan-connect.sh --install --dry-run`. Release verification generates and checks packages only in temporary output directories and never reads or writes `releases/`. Public packages must not contain `typing.dll`, game assemblies, game images/fonts, or any game PCK other than this mod's own PCK.

For operator details, environment variables, and API reference, go to [`lobby-service/README.md`](./lobby-service/README.md).
