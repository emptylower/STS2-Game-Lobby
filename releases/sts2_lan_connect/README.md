<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

## 中文

# STS2 LAN Connect 客户端安装说明

## 当前版本

| 项目 | 内容 |
|------|------|
| 客户端版本 | `0.5.1` |
| 默认大厅 | `47.111.146.69:8787`（兜底社区节点，可在 picker 内切换） |
| 去中心化发现 | `https://sts2-gamelobby-register.xyz`（CF Worker，apex 域名） |
| 连接策略 | `test_relaxed + relay-only` |

`0.5.1` 主要改进：加入前只比较 gameplay MOD 与必要 dependency。Steam 桌面客户端可在确认后订阅缺失 Workshop 项；Android、非 Steam 或 SteamAPI 不可用时只显示手动项。多余 gameplay MOD 默认不选择禁用，必须由用户选择并二次确认。任何 MOD 改动后都必须重启；客户端只在 15 分钟内恢复服务器、房间和槽位，不保存密码或 token。游戏版本不同仍直接拦截，不能通过同步或 relaxed 继续绕过。同一个客户端包兼容游戏 `0.107.1`、`0.108.0` 与 `0.109.0`，并与 v0.5.0 对端安全降级。

同一房间内的所有玩家必须使用完全相同的游戏版本。房主和客户端版本不同时，加入流程会直接提示双方版本并中止；普通非联机 MOD 不进入预检、不提示、不禁用，也不影响加入。自动获取仅使用 Steam Workshop，不会从房主、服务端或任意 URL 下载 DLL、PCK、ZIP。

### 加入前 MOD 预检

- 缺少 Workshop gameplay MOD：查看真实 Workshop 标题、发布者和大小后，勾选并确认订阅；可取消、重试或改为手动处理。
- 缺少手动 MOD 或 Workshop ID：按列表手动安装，客户端不会尝试其他下载来源。
- 多出 gameplay MOD：列表默认全部不勾选；只有选中并完成二次确认后才修改本机启用状态。
- 用户可在 relaxed 配置允许时选择“仍然尝试加入”，但该入口只适用于 MOD 差异，不能跳过游戏版本不一致。
- 安装或禁用完成后按提示重启游戏。公开房会恢复并重新预检；密码房会再次要求密码。

`0.5.0` 主要改进（保留作历史参考）：大厅新增节点级频道聊天；房间聊天升级为 Emoji、物品引用与 generation 校验的战斗状态引用，并完成 Android 输入、布局和图标修复。

### 富聊天引用怎么使用

- 点击输入框右侧笑脸按钮可把 Emoji 插入草稿；`Enter` 发送，`Shift + Enter` 换行。
- 插入卡牌、遗物或药水引用前，先点击游戏画面空白处让聊天输入框失去焦点，并关闭表情或物品预览。macOS 按住 `Option`、Windows/Linux 按住 `Alt`，再左键点击可见卡牌、遗物栏图标或药水栏图标。
- 在战斗中用相同手势点击能力、增益/减益图标或玩家角色，可插入战斗状态引用。战斗引用只能发送到房间聊天；怪物目标引用当前未开放。
- 引用可以与普通文字混排，并可用方向键、`Backspace` 和 `Delete` 编辑。桌面端将鼠标悬停在收到的物品引用上可查看本地化预览。
- Android 纯触屏可以发送 Emoji 和查看引用；主动插入物品或战斗引用需要外接键盘、鼠标。

`0.4.0` 主要改进（保留作历史参考）：大厅支持键盘 / 手柄式焦点导航，房间卡片可聚焦，`Enter` / `Space` / `ui_accept` 可加入当前聚焦房间；`Esc` 优先关闭最上层弹窗，再退出大厅；若安装 `say-the-spire2` 盲人辅助模组，客户端会软检测并把大厅焦点朗读桥接给该模组。新增 `F7` 邀请快捷键、`F8` 聊天快捷键，以及“剪贴板已有有效邀请码时跳过服务器选择器、直接弹出加入确认”的入口流程。发布包强制携带带 CF discovery 和内置 seed peers 的 `lobby-defaults.json`。

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
| Client version | `0.5.1` |
| Default lobby | `47.111.146.69:8787` fallback community node |
| Decentralized discovery | `https://sts2-gamelobby-register.xyz` CF Worker plus bundled seed peers |
| Connection policy | `test_relaxed + relay-only` |

`0.5.1` preflights gameplay-affecting MODs and required dependencies before join. Steam desktop can subscribe to missing Workshop items only after consent; Android, non-Steam, and unavailable SteamAPI environments receive manual guidance. Extra gameplay MODs start unchecked and require selection plus a second confirmation before disablement. Any MOD change requires restart, and pending resume never stores passwords or tokens. Game-version mismatches remain hard-blocked. One client package supports game versions `0.107.1`, `0.108.0`, and `0.109.0`, with safe fallback for v0.5.0 peers.

### MOD Preflight Before Join

- Inspect real Workshop metadata before consenting to subscriptions; cancel and retry remain available.
- Manually install items without a valid Workshop mapping. No host, lobby-service, or arbitrary-URL DLL/PCK/ZIP download is supported.
- Extra gameplay MODs are never disabled silently. Select them explicitly and confirm again.
- Relaxed continuation applies only to MOD differences and never bypasses the exact game-version requirement.
- Restart after installation or disablement. Public rooms resume and preflight again; password rooms ask for the password again.

Historical `0.5.0` changes: node-local server chat, rich room chat, generation-checked combat references, and Android input/layout/icon fixes.

### Using Rich Chat References

- Use the smile button to insert Emoji. Press `Enter` to send and `Shift + Enter` for a newline.
- Before capturing an item, click an empty part of the game view so the composer loses focus and close any emoji or item preview. Hold `Option` on macOS or `Alt` on Windows/Linux, then left-click a visible card, relic icon, or potion icon.
- In combat, use the same gesture on a power, buff/debuff icon, or player character. Combat references are room-chat only; monster-target references are not enabled.
- References can be mixed with text and removed with caret selection plus `Backspace` / `Delete`. Desktop users can hover received item chips for localized previews.
- Android touch-only users can send Emoji and view references; inserting item or combat references requires an external keyboard and mouse.

Historical `0.4.0` changes: keyboard/controller lobby navigation, focusable room cards, dialog-first `Esc`, optional `say-the-spire2` announcements, `F7` invite handling, `F8` room-chat toggling, clipboard invite routing, and mandatory CF discovery/seed defaults in the release package.

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
