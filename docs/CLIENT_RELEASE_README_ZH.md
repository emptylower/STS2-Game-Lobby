<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

## 中文

# STS2 LAN Connect 客户端安装说明

## 当前版本

| 项目 | 内容 |
|------|------|
| 客户端版本 | `0.6.0-alpha.1`（测试候选） |
| 默认大厅 | `sts2-test.43.133.192.249.nip.io`（可在 picker 内切换） |
| 去中心化发现 | `https://sts2-gamelobby-register.xyz`（CF Worker，apex 域名） |
| 连接策略 | `strict + relay-only` |

`0.6.0-alpha.1` 是客户端与 lobby-service 同步升级的双协议测试候选，不是正式版。升级后必须完整重启游戏与服务端进程。

本版会比较四张 ModelId net-id 表和四个位宽。`affects_gameplay: false` 的 MOD 仍可能改变线上编码；双方真实签名不一致时会在 ticket 签发或游戏 join request 之前拒绝，缺失/不可读签名则允许加入。发布默认配置已由 `test_relaxed` 改为 `strict`。

本版删除 RC4 对 RitsuLib 私有 postfix 的卸载、调用和恢复桥。兼容模式固定使用 `4/5-bit` 并禁止 RitsuLib；0.6 新协议保持原版 `2/3-bit` 主体，无 RitsuLib 时使用 standalone carrier。全员 RitsuLib 的设计路径只允许公开 typed-sidecar API；在真实 sidecar/barrier gate 未通过时，客户端以 `ritsulib_sidecar_unavailable` fail-closed。有 RitsuLib 只能连接有 RitsuLib，无 RitsuLib 只能连接无 RitsuLib。

续局来源现在按 `lan` / `lobby` / 未知三态处理，未知存档只询问一次；safe-load 和修复不会再误写或删除绑定。踢出使用与存档槽位分离的安装 credential 和当前占用者 binding handle，避免槽位接管后误封原主人。

同一房间内所有玩家必须使用完全相同的游戏版本，并在本轮测试中统一使用客户端与 lobby-service `0.6.0-alpha.1`。自动获取仅使用 Steam Workshop，不会从房主、服务端或任意 URL 下载 DLL、PCK、ZIP。历史 `0.3.x-0.5.x` 客户端真实互通不属于本 alpha 的测试范围。

本候选版是在既有功能之上叠加的，先前版本的能力全部保留：`0.5.5` 的游戏 ABI 向下兼容（运行时识别旧版平铺握手与 `0.110.x` 的 `PeerVersionInfo` 结构，并按运行时类型选择 `LobbyPlayer` 或 `StartRunLobbyPlayer` 的扩容序列化补丁）、`0.5.4` 的 AI 审核交互，以及 `0.5.3` 的 LAN/大厅续局通道拆分、续局身份码、存档保护和聊天 HUD。

### v0.6.0-alpha.1 测试重点

- 分别验证 no-Ritsu/no-Ritsu 与 Ritsu/Ritsu 的建房、ticket、加入、ready、begin-run 和首个同步状态。
- 分别验证 Ritsu/no-Ritsu 与 no-Ritsu/Ritsu 在 ticket 和 transport 前得到结构化 presence mismatch，且没有分配 slot/control/relay。
- Ritsu 房确认原版消息没有 standalone Tail，LAN container 只出现在公开 typed-sidecar frame，原版 handler 在配对验证前不运行。
- direct-IP 只允许兼容模式；Tail intent 或本地 Ritsu 在 initializer 创建前拒绝。
- 使用不同内容 MOD 组合加入同一房间，确认编码不一致时在黑屏前收到明确拒绝；该拒绝是预期行为。
- 从主菜单继续大厅存档、由房主执行重开，确认房间重新发布且队友可见。
- 测试槽位接管后踢出，确认原槽位主人没有被封禁，列表刷新后的 stale 操作不会转向新人。
- 遇到加入失败、黑屏或等待页卡住时，提交两台机器各自的完整 `godot.log`。日志会打印签名、位宽、表条目数和每个 MOD 的 `affects_gameplay` 标记。

### 加入前 MOD 预检

- 缺少 Workshop gameplay MOD：查看真实 Workshop 标题、发布者和大小后，勾选并确认订阅；可取消、重试或改为手动处理。
- 缺少手动 MOD 或 Workshop ID：按列表手动安装，客户端不会尝试其他下载来源。
- 多出 gameplay MOD：列表默认全部不勾选；只有选中并完成二次确认后才修改本机启用状态。
- 用户可在显式 relaxed 配置允许时选择“仍然尝试加入”，但该入口只适用于普通 MOD 差异，不能跳过游戏版本或 `WireCacheSignatureV1` 真实不一致。
- 安装或禁用完成后按提示重启游戏。公开房会恢复并重新预检；密码房会再次要求密码。

`0.5.2` 主要改进（保留作历史参考）：聊天引用体验升级——Android 点击输入区旁的“引用”按钮，桌面按 `Alt+R`，即可进入一次性引用模式；消息改为单一行内富文本自然换行，引用使用游戏原生预览，动态 Power 说明按实际层数和玩家上下文生成。

`0.5.0` 主要改进（保留作历史参考）：大厅新增节点级频道聊天；房间聊天升级为 Emoji、物品引用与 generation 校验的战斗状态引用，并完成 Android 输入、布局和图标修复。

### 富聊天引用怎么使用

- 点击输入框右侧笑脸按钮可把 Emoji 插入草稿；`Enter` 发送，`Shift + Enter` 换行。
- Android 点击输入区旁的“引用”按钮进入一次性引用模式；桌面按 `Alt+R` 进入或取消。成功点击一个支持对象后会自动退出，并把焦点交回文字输入位置。
- 原有桌面 `Alt+左键` 直接引用继续保留。卡牌、遗物、药水可在服务器或房间频道引用；战斗状态和玩家只允许房间频道；怪物目标引用当前未开放。
- 点击不支持区域不会吞掉正常游戏操作；再次点击引用按钮、`Esc`、切频道、关闭聊天或离开房间都会取消 armed 状态。
- 引用可以和普通文字自然混排，并可用方向键、`Backspace`、`Delete`、选择和粘贴继续编辑。
- 桌面悬停或点击消息引用可查看原生说明；Android 点击引用会打开固定预览，点击外部、`Esc` 或关闭按钮退出。

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

v0.6 不再支持 `0.2.x` 客户端。自建大厅需要与客户端一起升级到 `0.6.0-alpha.1`；`0.3-0.5` 客户端只能加入兼容房，不能加入 `tail_v1` 房间。

---

<br>

---

## English

# STS2 LAN Connect — Client Installation Guide

## Current Version

| Field | Value |
|-------|-------|
| Client version | `0.6.0-alpha.1` (prerelease candidate) |
| Default lobby | `sts2-test.43.133.192.249.nip.io` |
| Decentralized discovery | `https://sts2-gamelobby-register.xyz` CF Worker plus bundled seed peers |
| Connection policy | `strict + relay-only` |

`0.6.0-alpha.1` is a synchronized client and lobby-service dual-protocol candidate, not a final release. Restart both the game and service after upgrading.

The candidate fingerprints the four ModelId net-id tables and bit widths. A genuine peer mismatch is rejected before ticket issuance or the game join request, while missing or unreadable signatures remain fail-open. The shipped compatibility profile is now `strict`.

This version removes the RC4 private RitsuLib postfix bridge. Compat uses fixed `4/5-bit` encoding and forbids RitsuLib. Tail v1 preserves the vanilla `2/3-bit` body and uses a standalone carrier when RitsuLib is absent. The all-Ritsu design path may use only the public typed-sidecar API; until the real sidecar/barrier gate passes, the client fails closed with `ritsulib_sidecar_unavailable`. Mixed presence is rejected before transport.

Continue-run origin is an explicit LAN/lobby/unknown choice, with a one-time prompt for ambiguous legacy saves. Safe load and repair preserve bindings. Kick identity is separate from save slots and uses the rendered occupant's binding handle.

Every participant must use the exact same game version and client `0.6.0-alpha.1`; the lobby service must also be `0.6.0-alpha.1`. Historical-client interoperability is outside this alpha gate.

This candidate builds on top of the existing feature set; nothing from earlier versions was removed. It still carries `0.5.5`'s backward-compatible game ABI handling (detecting the legacy flat handshake or the `0.110.x` `PeerVersionInfo` handshake at runtime and selecting the old `LobbyPlayer` or new `StartRunLobbyPlayer` serialization carrier), `0.5.4`'s AI moderation flow, and `0.5.3`'s LAN/lobby continue-run channel split, resume identity code, save protection, and chat HUD.

### v0.6.0-alpha.1 Test Focus

- Exercise no-Ritsu/no-Ritsu and Ritsu/Ritsu through ticket, ready, begin-run, and first synchronized state.
- Exercise both mixed-presence directions and require zero slot, ticket, control, and transport allocation.
- Verify Ritsu rooms carry LAN data only in the public sidecar frame and do not append a standalone Tail to vanilla messages.
- Verify direct-IP rejects Tail intent and local Ritsu before creating an initializer.
- Verify mismatched content-MOD wire tables are rejected before a black screen; this refusal is intentional.
- Resume a lobby save and restart as host, then verify the room is republished and visible to teammates.
- Exercise slot takeover followed by kick, confirming the original slot owner is not banned and stale actions do not retarget a replacement.
- For join failures, black screens, or stuck waiting rooms, provide the complete `godot.log` from both machines.

### MOD Preflight Before Join

- Inspect real Workshop metadata before consenting to subscriptions; cancel and retry remain available.
- Manually install items without a valid Workshop mapping. No host, lobby-service, or arbitrary-URL DLL/PCK/ZIP download is supported.
- Extra gameplay MODs are never disabled silently. Select them explicitly and confirm again.
- Explicit relaxed continuation applies only to ordinary MOD differences and never bypasses the exact game-version or genuine `WireCacheSignatureV1` mismatch requirements.
- Restart after installation or disablement. Public rooms resume and preflight again; password rooms ask for the password again.

Historical `0.5.0` changes: node-local server chat, rich room chat, generation-checked combat references, and Android input/layout/icon fixes.

### Using Rich Chat References

- Use the smile button to insert Emoji. Press `Enter` to send and `Shift + Enter` for a newline.
- Tap the Reference button beside the composer on Android, or press `Alt+R` on desktop, to arm one-shot reference mode. One successful capture exits the mode and restores focus to the text insertion point.
- The existing desktop `Alt+left-click` shortcut remains available. Cards, relics, and potions work in server or room chat; combat powers and players are room-chat only. Monster targets remain disabled.
- Unsupported clicks keep reference mode armed without consuming the normal game action. The button, `Esc`, channel changes, closing chat, or leaving the room cancels the mode.
- References flow inline with text and remain editable with selection, arrows, `Backspace`, `Delete`, and paste.
- Desktop supports hover and pinned click previews. Android opens a pinned preview by tapping a reference; tap outside, press `Esc`, or use the close button to dismiss it.

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

v0.6 no longer supports `0.2.x` clients. Self-hosted lobbies must upgrade together with the client to `0.6.0-alpha.1`; `0.3-0.5` clients can only join compat rooms and cannot join `tail_v1` rooms.
