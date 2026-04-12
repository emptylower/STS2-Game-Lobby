<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

## 中文

# STS2 LAN Connect 客户端安装说明

## 当前版本

| 项目 | 内容 |
|------|------|
| 客户端版本 | `0.2.3` |
| 默认大厅 | `47.111.146.69:8787` |
| 公共服务器目录 | `47.111.146.69:18787` |
| 连接策略 | `test_relaxed + relay-only` |

`0.2.3` 主要改进：运行时从常驻扫描器改为场景 `_Ready` hook，降低单人与移动端性能消耗；4 人房间自动启用 `0.2.2` 兼容协议，5-8 人房间使用扩展协议（仅支持 `0.2.3+`）；大幅改善安卓端稳定性，修复多处启动崩溃与 `MethodAccessException`；大厅新增公告轮播、聊天面板、搜索筛选与切换服务器功能。版本单一真源为发布包内的 `sts2_lan_connect.json`。

---

## 安装前

- 关闭《Slay the Spire 2》
- 确保所有联机玩家使用同一版本 MOD
- 发布包内已包含 `lobby-defaults.json`，普通玩家无需手动填写大厅地址
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
- 单击房间卡片选中，双击直接尝试加入
- 进入房间后可通过右上角按钮展开聊天面板；面板支持长按拖动，位置自动保存
- 顶部公告栏每 6 秒轮播，鼠标悬停时暂停
- 加入进度较长时会显示阶段化提示；超时后进度弹窗右上角出现取消按钮
- 提示 `MOD 不一致` 时，会弹窗列出缺少的具体 MOD 名称
- 刷新失败或延迟异常，可优先通过标题栏 `切换服务器` 切换到其他可用大厅
- 如需切换中心目录服务，可在开发网络设置中填写 `中心服务器覆盖`

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
| Client version | `0.2.3` |
| Default lobby | `47.111.146.69:8787` |
| Public server directory | `47.111.146.69:18787` |
| Connection policy | `test_relaxed + relay-only` |

`0.2.3` key changes: the runtime hook is now scene-based (`_Ready`) instead of a polling scanner, reducing CPU and battery usage on solo play and mobile. 4-player rooms automatically use the `0.2.2` compatibility protocol; 5-8 player rooms use the extended protocol and require `0.2.3+`. Android stability is significantly improved, fixing startup crashes and `MethodAccessException` errors. The lobby UI gains an announcement carousel, an in-room chat panel, room search and filtering, and a server-switch button. The authoritative version source is `sts2_lan_connect.json` in the release package.

---

## Before Installing

- Close Slay the Spire 2 before proceeding.
- All players in a session must use the same MOD version.
- The release package includes `lobby-defaults.json`; most players do not need to enter a lobby address manually.
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
- Single-click a room card to select it; double-click to join immediately.
- Once inside a room, open the chat panel from the button in the top-right corner. The panel can be repositioned by long-pressing and dragging; its position is saved between sessions.
- The announcement carousel at the top rotates every 6 seconds and pauses on hover.
- A progress dialog appears for long join attempts; a cancel button appears in its top-right corner if the attempt takes too long.
- If a `MOD mismatch` error occurs, a dialog will list the specific missing MOD names.
- If the lobby feels slow or unavailable, use the `Switch Server` button in the title bar to move to another available lobby.
- To switch the central directory service, enter a value in the `Central Server Override` field in the developer network settings.

---

## Self-Hosted Lobby Notes

To allow `0.2.2` and `0.2.3` clients to play together through your own lobby, the lobby server must have version checking set to relaxed mode. If your server is already in relaxed mode, this client update does not require a server-side update.
