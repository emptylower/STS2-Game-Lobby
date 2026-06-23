<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

## 中文

# STS2 LAN Connect 客户端安装说明

## 当前版本

| 项目 | 内容 |
|------|------|
| 客户端版本 | `0.4.0` |
| 默认大厅 | `47.111.146.69:8787`（兜底社区节点，可在 picker 内切换） |
| 去中心化发现 | `https://sts2-gamelobby-register.xyz`（CF Worker，apex 域名） |
| 连接策略 | `test_relaxed + relay-only` |

`0.4.0` 主要改进：大厅支持键盘 / 手柄式焦点导航，房间卡片可聚焦，`Enter` / `Space` / `ui_accept` 可加入当前聚焦房间；`Esc` 优先关闭最上层弹窗，再退出大厅；若安装 `say-the-spire2` 盲人辅助模组，客户端会软检测并把大厅焦点朗读桥接给该模组。新增 `F7` 邀请快捷键、`F8` 聊天快捷键，以及“剪贴板已有有效邀请码时跳过服务器选择器、直接弹出加入确认”的入口流程。发布包现在强制携带带 CF discovery 和内置 seed peers 的 `lobby-defaults.json`。

`0.3.1` 主要改进（v0.3 系列，去中心化发现，保留作历史参考）：进入"游戏大厅"时弹出
**服务器选择 picker**，列表来自 CF Worker 聚合 + 本地缓存 + 内置种子三路；
每个候选实时探活，对 v0.3+ 服务器走 `/peers/health` 拉取**运维设置的服务器名**，
对 v0.2 服务器自动回退 `/probe` 仅显示 ping。Picker 用大厅同款像素风样式，
占满游戏窗口约 92%（手机端友好）。验证期每次进入都弹，便于看清网络分布；
未来稳定后会改回"记住上次"。

`0.3.0` 引入了 v0.3 协议本身：客户端三路引导（CF + 本地缓存 + 内置种子），
服务端引入 peer 协议（ed25519 探活 + gossip）；与 v0.2 服务端通过 sidecar
过渡。详细背景见 `docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`。

`0.2.3` 主要改进（保留作历史参考）：运行时从常驻扫描器改为场景 `_Ready` hook，降低单人与移动端性能消耗；4 人房间自动启用 `0.2.2` 兼容协议，5-8 人房间使用扩展协议（仅支持 `0.2.3+`）；大幅改善安卓端稳定性，修复多处启动崩溃与 `MethodAccessException`；大厅新增公告轮播、聊天面板、搜索筛选与切换服务器功能；房主在暂停菜单可执行 `重开一局`，自动重启当前多人续局并让队友自动重连。版本单一真源为发布包内的 `sts2_lan_connect.json`。

---

## 安装前

- 关闭《Slay the Spire 2》
- 确保所有联机玩家使用同一版本 MOD
- 发布包内已包含 `lobby-defaults.json`，普通玩家无需手动填写大厅地址；该文件同时提供 CF 发现入口和内置种子列表
- 如使用 `Clash`、`Surge`、全局代理或 `TUN`，请将大厅服务器 IP 设为 `DIRECT`

---

## 一键安装 / 卸载

### macOS

双击 `install-sts2-lan-connect-macos.command`

- 已安装 MOD 则自动卸载；未安装则自动安装
- 安装 / 卸载后自动刷新 `SlayTheSpire2.app` 的 macOS 签名

### Windows

双击 `install-sts2-lan-connect-windows.bat`

- 已安装 MOD 则自动卸载；未安装则自动安装

---

## 命令行安装

**macOS**

```bash
./install-sts2-lan-connect-macos.sh --install --package-dir .
```

**Windows**

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Install -PackageDir .
```

---

## 命令行卸载

**macOS**

```bash
./install-sts2-lan-connect-macos.sh --uninstall --package-dir .
```

**Windows**

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Uninstall -PackageDir .
```

---

## 安装行为说明

安装时会复制以下文件到游戏 `mods/sts2_lan_connect/` 目录：`sts2_lan_connect.dll`、`sts2_lan_connect.pck`、`sts2_lan_connect.json`；如包内存在 `lobby-defaults.json` 也会一并复制。macOS 安装 / 卸载时自动刷新 app 签名，并执行一次 vanilla 到 modded 的单向存档同步。

如需跳过存档同步，仅安装 MOD：

**macOS**

```bash
./install-sts2-lan-connect-macos.sh --install --package-dir . --no-save-sync
```

**Windows**

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Install -PackageDir . -NoSaveSync
```

---

## 使用要点

- 房间列表支持关键词搜索、分页和筛选；`公开` / `上锁` 互斥，`可加入` 可叠加
- 单击房间卡片选中，双击直接尝试加入；键盘 / 手柄焦点落在房间卡片时，按 `Enter` / `Space` / `ui_accept` 也可加入
- `Esc` 优先关闭当前弹窗；无弹窗时退出大厅
- 复制有效邀请码后点击 `游戏大厅` 会直接弹出加入确认；也可在大厅中按 `F7` 处理剪贴板邀请码或接受当前邀请弹窗
- 进入房间后可通过右上角按钮展开聊天面板，也可按 `F8` 打开 / 收起；面板支持长按拖动，位置自动保存
- 如同时安装 `say-the-spire2` 盲人辅助模组，大厅焦点和房间卡片会被桥接到其朗读系统；未安装时无额外依赖
- 房主可在暂停菜单 `房间管理` 中点击 `重开一局`，自动重启当前多人续局
- 队友端在重开期间会自动回主菜单并尝试自动重连；超时可手动从 `游戏大厅` 加入
- 顶部公告栏每 6 秒轮播，鼠标悬停时暂停
- 加入进度较长时会显示阶段化提示；超时后进度弹窗右上角出现取消按钮
- 提示 `MOD 不一致` 时，会弹窗列出缺少的具体 MOD 名称
- 刷新失败或延迟异常，可优先通过标题栏 `切换服务器` 切换到其他可用大厅
- 如需临时排障或切到指定大厅，可在开发网络设置里填写 `HTTP 覆盖`；如服务端要求建房令牌，可在同一处填写 `建房令牌`

---

## 自建大厅服说明

如需让 `0.2.2` 与 `0.2.3` 兼容联机，关键在于大厅服已放宽版本校验。若服务端已为 relaxed 配置，本次客户端更新无需同步更新服务端。

---

<br>

---

## English

# STS2 LAN Connect — Client Installation Guide

## Current Version

| Field | Value |
|-------|-------|
| Client version | `0.4.0` |
| Default lobby | `47.111.146.69:8787` fallback community node |
| Decentralized discovery | `https://sts2-gamelobby-register.xyz` CF Worker plus bundled seed peers |
| Connection policy | `test_relaxed + relay-only` |

`0.4.0` key changes: the lobby supports keyboard/controller-style focus navigation, focusable room cards, `Enter` / `Space` / `ui_accept` room joining, and dialog-first `Esc` behavior. If the `say-the-spire2` accessibility mod is present, the client soft-detects it and forwards lobby focus announcements to its speech system. `F7` handles clipboard/visible invite confirmation, `F8` toggles the room chat panel, and valid clipboard invites skip the server picker and open the lobby invite confirmation directly. Release packages now require `lobby-defaults.json` with CF discovery and bundled seed peers.

Historical `0.3.x` changes: the server picker lists lobbies from CF Worker aggregation, local cache, and bundled seed peers. Historical `0.2.3` changes: scene-based runtime hook, 4-player legacy compatibility, 5-8 player extended protocol, Android stability fixes, announcement carousel, room chat, search/filtering, and pause-menu `Restart Run`.

---

## Before Installing

- Close Slay the Spire 2 before proceeding.
- All players in a session must use the same MOD version.
- The release package includes `lobby-defaults.json`; most players do not need to enter a lobby address manually. This file also provides the CF discovery endpoint and bundled seed peers.
- If you use Clash, Surge, a system-wide proxy, or TUN mode, route the lobby server IP as `DIRECT`.

---

## One-Click Install / Uninstall

### macOS

Double-click `install-sts2-lan-connect-macos.command`

- Installs the MOD if it is not present; uninstalls it if it is already installed.
- Automatically re-signs `SlayTheSpire2.app` after install or uninstall.

### Windows

Double-click `install-sts2-lan-connect-windows.bat`

- Installs the MOD if it is not present; uninstalls it if it is already installed.

---

## Command-Line Install

**macOS**

```bash
./install-sts2-lan-connect-macos.sh --install --package-dir .
```

**Windows**

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Install -PackageDir .
```

---

## Command-Line Uninstall

**macOS**

```bash
./install-sts2-lan-connect-macos.sh --uninstall --package-dir .
```

**Windows**

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Uninstall -PackageDir .
```

---

## What the Installer Does

The installer copies `sts2_lan_connect.dll`, `sts2_lan_connect.pck`, and `sts2_lan_connect.json` into the game's `mods/sts2_lan_connect/` directory. If `lobby-defaults.json` is present in the package, it is copied there as well. On macOS, the app signature is refreshed automatically. Install also performs a one-way save sync from the vanilla save location to the modded one.

To install without the save sync step:

**macOS**

```bash
./install-sts2-lan-connect-macos.sh --install --package-dir . --no-save-sync
```

**Windows**

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Install -PackageDir . -NoSaveSync
```

---

## Usage Tips

- The room list supports keyword search, pagination, and filters. `Public` and `Locked` are mutually exclusive; `Joinable` can be combined with either.
- Single-click a room card to select it; double-click to join immediately. With keyboard/controller focus on a room card, `Enter` / `Space` / `ui_accept` also joins it.
- `Esc` closes the current dialog first; when no dialog is open, it leaves the lobby.
- Copying a valid invite before clicking `Game Lobby` opens the lobby invite confirmation directly; `F7` handles clipboard invites or accepts the visible invite confirmation.
- Once inside a room, open the chat panel from the button in the top-right corner or press `F8`. The panel can be repositioned by long-pressing and dragging; its position is saved between sessions.
- If the `say-the-spire2` accessibility mod is installed, lobby focus and room-card announcements are bridged to its speech system; without it, there is no extra dependency.
- The host can click `Restart Run` from pause-menu `Room Management` to restart the current multiplayer save quickly.
- During restart, teammates are auto-routed back to main menu and auto-rejoin; if timeout occurs, manual join from `Game Lobby` remains available.
- The announcement carousel at the top rotates every 6 seconds and pauses on hover.
- A progress dialog appears for long join attempts; a cancel button appears in its top-right corner if the attempt takes too long.
- If a `MOD mismatch` error occurs, a dialog will list the specific missing MOD names.
- If the lobby feels slow or unavailable, use the `Switch Server` button in the title bar to move to another available lobby.
- To switch to a specific lobby for troubleshooting, enter it in `HTTP Override` in the developer network settings. Public discovery itself comes from the packaged CF discovery endpoint, local cache, and bundled seed peers.

---

## Self-Hosted Lobby Notes

To allow `0.2.2` and `0.2.3` clients to play together through your own lobby, the lobby server must have version checking set to relaxed mode. If your server is already in relaxed mode, this client update does not require a server-side update.
