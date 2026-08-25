<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

## 中文

# STS2 LAN Connect 客户端安装说明

## 当前版本

| 项目 | 内容 |
|------|------|
| 客户端版本 | `0.6.0`（正式版） |
| lobby-service 版本 | `0.6.0`（正式版，与 `0.6.0-alpha.6` 代码一致） |
| 默认大厅 | `sts2-test.43.133.192.249.nip.io`（可在 picker 内切换） |
| 去中心化发现 | `https://sts2-gamelobby-register.xyz`（CF Worker，apex 域名） |
| 连接策略 | `strict + relay-only` |

`0.6.0` 是 `0.5.5` 之后的第一个正式版，收敛了 `0.5.6-rc1`~`rc4` 与 `0.6.0-alpha.1`~`alpha.9` 全部九个测试候选；客户端与 lobby-service 的版本号同步对齐为 `0.6.0`。本轮通过 GitHub Release 分发，**Steam 创意工坊暂不上传**（Workshop 条目仍停留在 `0.6.0-alpha.7`）。安装或更新后必须完整重启游戏。完整说明见 `docs/RELEASE_NOTES_V0.6.0_ZH.md`。

「安装/更新 MOD 后大厅入口不出现」已在本版修复。根因是 MOD 加载顺序竞态：RitsuLib 先初始化时会毒化闭合泛型补丁目标，本 MOD 的协议补丁随之抛 `InvalidProgramException` 并中止初始化。现在 Tail 补丁全平台默认使用 15 步 `non_generic_v2` 非泛型计划，不再注册任何闭合泛型目标；线上字节格式零变化。alpha.8 及更早版本的临时绕过办法（先关 RitsuLib、启动一次、再开 RitsuLib）**不再需要**。桌面端如需紧急回滚旧计划，可设置环境变量 `STS2_LAN_CONNECT_TAIL_PLAN=desktop_generic_v1`。

若协议补丁仍然失败，MOD 会进入降级模式而不是整体失效：大厅照常安装、可以浏览，但建房 / 加入 / 续局发布会被拒绝，并弹出游戏原生提示说明 `protocol_patch_conflict` 的原因与恢复步骤。

本版会比较四张 ModelId net-id 表和四个位宽。`affects_gameplay: false` 的 MOD 仍可能改变线上编码；双方真实签名不一致时会在 ticket 签发或游戏 join request 之前拒绝，缺失/不可读签名则允许加入。发布默认配置为 `strict`。

本版删除 RC4 对 RitsuLib 私有 postfix 的卸载、调用和恢复桥。兼容模式固定使用 `4/5-bit` 并禁止 RitsuLib；0.6 新协议保持原版 `2/3-bit` 主体，无 RitsuLib 时使用 standalone carrier。全员 RitsuLib 的路径只允许公开 typed-sidecar API；在真实 sidecar/barrier gate 未通过时，客户端以 `ritsulib_sidecar_unavailable` fail-closed。有 RitsuLib 只能连接有 RitsuLib，无 RitsuLib 只能连接无 RitsuLib。macOS 与 Android 均要求官方 RitsuLib v0.5.13 及以上；官方 v0.5.12 不应继续使用。

续局来源按 `lan` / `lobby` / 未知三态处理，未知存档只询问一次；safe-load 和修复不会再误写或删除绑定。踢出使用与存档槽位分离的安装 credential 和当前占用者 binding handle，避免槽位接管后误封原主人。

同一房间内所有玩家必须使用完全相同的游戏版本，并统一使用客户端 `0.6.0`；lobby-service 建议使用 `0.6.0`（与 `0.6.0-alpha.6` 功能相同）。自动获取仅使用 Steam Workshop，不会从房主、服务端或任意 URL 下载 DLL、PCK、ZIP。历史 `0.3.x`-`0.5.x` 客户端与 `0.6.0` 的真实互通不在发布门禁范围内。

本正式版是在既有功能之上叠加的，先前版本的能力全部保留：`0.5.5` 的游戏 ABI 向下兼容（运行时识别旧版平铺握手与 `0.110.x` 的 `PeerVersionInfo` 结构，并按运行时类型选择 `LobbyPlayer` 或 `StartRunLobbyPlayer` 的扩容序列化补丁）、`0.5.4` 的 AI 审核交互，以及 `0.5.3` 的 LAN/大厅续局通道拆分、续局身份码、存档保护和聊天 HUD。

### v0.6.0 安装后自查

- 启动日志应显示 Tail 使用 `non_generic_v2`，并出现 `applied=15/15`、`generic_target_count=0` 与最终初始化哨兵；`beginRunMessageBusBoundary` 为 `skipped_non_generic_plan`（无外部占用）或 `skipped_foreign_owner`（RitsuLib 先占）都正常。
- 主菜单应能看到「联机大厅」入口。任意 MOD 加载顺序（含 RitsuLib 先加载）下都应出现；如果没有出现，请按下方取证步骤保存日志反馈。
- 房主端原生多人等待页和局内玩家列表应显示客机设置的昵称，不应显示数字平台 ID。
- 双方以官方 RitsuLib v0.5.13 及以上进入同一续局后，由房主从房间管理点击「重开一局」；客机应自动返回、按原存档槽位加入新房间，并与房主再次进入同一局。
- 重开日志应出现双方旧 `RunManager.NetService` 已清理、新房间发布成功和客机 `relay_success`；不应出现 `handshake_transport_budget`、`LobbyJoinTimeout` 或 `ritsulib_not_allowed_in_compat_mode`。
- Android 房主经 relay 开局时，即使加载阶段超过房间心跳窗口，连接也应保持稳定。
- 点击「放弃多人存档」后，「备份并永久放弃」和「保留存档」两个按钮都应完整可见。
- 使用不同内容 MOD 组合加入同一房间时，编码不一致会在黑屏前收到明确拒绝；该拒绝是预期行为。
- Ritsu 房与非 Ritsu 房不能互相加入；混合组合会在连接前明确拒绝。
- direct-IP 直连只支持兼容模式。
- 遇到启动失败、加入失败、黑屏或等待页卡住时，优先按下方「Android 启动取证」保存 launcher `sts2*.log`、`adb logcat` 与可导出的 diagnostics session（桌面端提供完整 `godot.log`）。

### Android 启动取证

1. **先保存 launcher 日志**：导出 launcher 提供的完整 `sts2*.log`。客户端会把每条 `patch_diag` 同步镜像到普通游戏日志，因此即使 app-private 目录不可读，也能定位最后一个初始化阶段和 patch ID。
2. **同时采集 logcat**：冷启动前执行 `adb logcat -c`，启动后保存 `adb logcat -d` 输出。不要只截取崩溃末尾；需要包含本次启动从 `Entry.Init` 开始的完整记录。
3. **可访问私有文件时再导出 session**：优先尝试 `run-as com.megacrit.sts2re` 导出 `user://sts2_lan_connect/diagnostics/`；如果 `run-as` 不可用，可在已 root 设备上读取同一 app-private 目录，或使用 launcher 文件提供器导出整个 diagnostics session，而不是只取单个 JSONL。

如果 `run-as`、root 和 launcher 文件提供器都不可用，不要因此停止取证：提交 launcher 的 `sts2*.log` 与完整 `adb logcat` 即为降级路径。公开日志中的 `sts2_lan_connect patch_diag:` 镜像应仍能报告最后 stage、patch ID、MethodInfo、程序集指纹和回滚结果；日志提交前不要附带配置正文、聊天内容、密码、token 或完整 URL/query。

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

v0.6 不再支持 `0.2.x` 客户端。自建大厅建议升级到 lobby-service `0.6.0`（与 `0.6.0-alpha.6` 功能相同），同房客户端统一升级到 `0.6.0`；`0.3-0.5` 客户端只能加入兼容房，不能加入 `tail_v1` 房间。

---

## 反馈与交流

遇到问题、想反馈 bug 或参与测试，欢迎加群：

- **联机大厅 8 群：341498145**
- **测试群（要求会导出 log）：1093309523**

反馈时请附上双方完整的 `godot.log`（Android 见上方「Android 启动取证」）和客户端内的本地调试报告，并注明客户端版本 `0.6.0`。

---

<br>

---

## English

# STS2 LAN Connect — Client Installation Guide

## Current Version

| Field | Value |
|-------|-------|
| Client version | `0.6.0` (stable) |
| Lobby-service version | `0.6.0` (stable; functionally identical to `0.6.0-alpha.6`) |
| Default lobby | `sts2-test.43.133.192.249.nip.io` |
| Decentralized discovery | `https://sts2-gamelobby-register.xyz` CF Worker plus bundled seed peers |
| Connection policy | `strict + relay-only` |

`0.6.0` is the first stable release after `0.5.5`, consolidating every candidate from `0.5.6-rc1` through `0.6.0-alpha.9`. The client and lobby-service versions are aligned at `0.6.0`. This round ships through GitHub Releases only; **Steam Workshop is not updated** and its entry remains on `0.6.0-alpha.7`. Fully restart the game after updating.

The "lobby entry never appears after installing or updating" bug is fixed. Its root cause was a mod load-order race: when RitsuLib initializes first it poisons closed-generic patch targets, so our protocol patches threw `InvalidProgramException` and aborted initialization. The Tail patch plan now defaults to the 15-step, non-generic `non_generic_v2` plan on every platform and registers no closed-generic targets; wire bytes are unchanged. The old workaround (disable RitsuLib, launch once, re-enable) is no longer needed. For an emergency rollback on desktop, set `STS2_LAN_CONNECT_TAIL_PLAN=desktop_generic_v1`.

If protocol patches still fail, the mod enters degraded mode instead of failing entirely: the lobby installs and stays browsable, hosting/joining/continue-run publication are refused, and a native popup explains the `protocol_patch_conflict` cause and recovery steps.

The client fingerprints the four ModelId net-id tables and bit widths. A genuine peer mismatch is rejected before ticket issuance or the game join request, while missing or unreadable signatures remain fail-open. The shipped compatibility profile is `strict`.

This version removes the RC4 private RitsuLib postfix bridge. Compat uses fixed `4/5-bit` encoding and forbids RitsuLib. Tail v1 preserves the vanilla `2/3-bit` body and uses a standalone carrier when RitsuLib is absent. The all-Ritsu path may use only the public typed-sidecar API; until the real sidecar/barrier gate passes, the client fails closed with `ritsulib_sidecar_unavailable`. Mixed presence is rejected before transport. Both macOS and Android require official RitsuLib v0.5.13 or newer; v0.5.12 should not be used.

Continue-run origin is an explicit LAN/lobby/unknown choice, with a one-time prompt for ambiguous legacy saves. Safe load and repair preserve bindings. Kick identity is separate from save slots and uses the rendered occupant's binding handle.

Every participant must use the exact same game version and client `0.6.0`; self-hosted lobby services should run `0.6.0`. Interoperability with historical `0.3.x`-`0.5.x` clients is outside the release gate.

This release builds on top of the existing feature set; nothing from earlier versions was removed. It still carries `0.5.5`'s backward-compatible game ABI handling (detecting the legacy flat handshake or the `0.110.x` `PeerVersionInfo` handshake at runtime and selecting the old `LobbyPlayer` or new `StartRunLobbyPlayer` serialization carrier), `0.5.4`'s AI moderation flow, and `0.5.3`'s LAN/lobby continue-run channel split, resume identity code, save protection, and chat HUD.

### v0.6.0 Post-Install Checks

- Startup must report the Tail plan as `non_generic_v2` with `applied=15/15`, `generic_target_count=0`, and the final initialization sentinel. `beginRunMessageBusBoundary` reading `skipped_non_generic_plan` or `skipped_foreign_owner` is expected.
- The Game Lobby entry must appear on the main menu under any mod load order, including RitsuLib loading first.
- The host's native multiplayer load screen and in-run roster must show the guest's configured player name instead of a numeric platform ID.
- With official RitsuLib v0.5.13+ on both peers, **Restart Run** after resuming the same save must return the guest automatically, reclaim its original slot in the republished room, and re-enter the same run.
- Restart logs must show stale `RunManager.NetService` cleanup on both peers, successful room republication, and guest `relay_success`, without `handshake_transport_budget`, `LobbyJoinTimeout`, or `ritsulib_not_allowed_in_compat_mode`.
- During a relay-backed run start, an Android host may exceed the room-heartbeat window while loading; both peers must remain connected.
- Both the destructive abandon-save action and the keep-save action must remain fully visible in the confirmation dialog.
- Mismatched content-MOD wire tables are rejected before a black screen; this refusal is intentional.
- Ritsu and non-Ritsu peers cannot join each other; mixed presence is rejected before connecting.
- Direct IP supports compat mode only.
- For startup failures, join failures, black screens, or stuck waiting rooms, collect the launcher `sts2*.log`, complete `adb logcat`, and any exportable diagnostics session as described below (desktop: attach the full `godot.log`).

### Android Startup Evidence

1. Export the launcher's complete `sts2*.log` first. Every `patch_diag` event is mirrored into ordinary game logs, so the last stage and patch ID remain visible even when app-private files cannot be read.
2. Capture logcat for the same cold start: clear it with `adb logcat -c` before launch, then save `adb logcat -d` afterward. Keep the complete startup sequence beginning at `Entry.Init`.
3. When app-private files are accessible, export the whole `user://sts2_lan_connect/diagnostics/` session with `run-as com.megacrit.sts2re`. If `run-as` is unavailable, use root access or the launcher's file provider.

When none of `run-as`, root, or a launcher file provider is available, submit the launcher `sts2*.log` and complete `adb logcat` as the supported fallback. The `sts2_lan_connect patch_diag:` mirror should still contain the final stage, patch ID, MethodInfo, assembly fingerprint, and rollback result. Do not include configuration bodies, chat text, passwords, tokens, or complete URL queries.

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

v0.6 no longer supports `0.2.x` clients. Self-hosted lobbies should run lobby-service `0.6.0` (functionally identical to `0.6.0-alpha.6`), and every peer in a room must use client `0.6.0`. Clients `0.3-0.5` can only join compat rooms and cannot join `tail_v1` rooms.


---

## Feedback and Community

Chinese-language QQ groups for bug reports and testing:

- **Game Lobby group 8: 341498145**
- **Testing group (log export required): 1093309523**

When reporting an issue, attach the complete `godot.log` from both peers (Android: see the evidence steps above) plus the in-client local debug report, and state the client version `0.6.0`.
