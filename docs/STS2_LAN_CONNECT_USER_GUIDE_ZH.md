<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

# STS2 LAN Connect 使用说明

## v0.5.1 加入前 MOD 预检

- 选择房间后，客户端先比较会影响联机的 gameplay MOD 与必要 dependency；普通非联机 MOD 不会提示、禁用或影响加入
- 游戏版本不同会先直接拦截，不能通过 MOD 同步或 relaxed“仍然尝试加入”绕过
- Steam 桌面客户端会先显示真实 Workshop 信息，只有确认后才订阅缺失项；Android、非 Steam 或 SteamAPI 不可用时只提供手动处理
- 多余 gameplay MOD 默认不勾选，必须手动选择并二次确认才会禁用
- 安装或禁用 MOD 后必须重启。公开房在 15 分钟内恢复并重新预检；密码房重新询问密码，客户端不会保存密码或 token
- 客户端不会从房主、大厅服务或任意 URL 下载 DLL、PCK、ZIP

## v0.5.0 聊天升级

- 大厅右侧新增 `频道聊天`，连接当前大厅服务器后即可收发消息，不需要先加入房间
- 频道聊天历史只保存在当前服务器进程内，服务重启后清空，也不会同步到其他大厅节点
- 房间聊天支持 Emoji、卡牌 / 遗物 / 药水引用，以及安全降级的战斗状态引用；旧客户端只能看到兼容文本
- 同一个 v0.5.0 客户端包兼容游戏 `0.107.1`、`0.108.0` 与 `0.109.0`
- 同一房间的房主和客户端必须使用完全相同的游戏版本；不同版本会在加入阶段直接提示并中止
- Android 富聊天输入不会在每次输入或删除后重启系统键盘
- 完整聊天能力要求客户端和大厅服务都升级到 `0.5.0`

## 进入大厅

1. 启动游戏并进入多人首页
2. 点击 `游戏大厅`
3. 直接在大厅里完成建房、刷新和加入
4. 如果默认大厅拥堵或不可用，点标题栏的 `切换服务器` 切换到其他可用大厅

如果剪贴板里已经复制了有效的邀请码，点击 `游戏大厅` 时会跳过服务器选择器，直接进入大厅并弹出加入确认；邀请码里包含目标服务器和房间 ID，因此即使邀请来自另一台大厅，也会在加入时临时切换到对应服务器。

## 顶部公告

- 大厅顶部显示服务器下发的公告轮播，支持 `更新`、`活动`、`警告`、`信息` 四类样式
- 桌面端可用左右箭头和点状页码切换；紧凑横屏模式改为 `1/N` 数字指示
- 公告默认每 6 秒自动切换，鼠标悬停时暂停，底部进度条从左往右累积

## 大厅列表操作

- 支持关键词搜索、分页和可叠加筛选（`公开`、`上锁`、`可加入`）
- `公开` 与 `上锁` 互斥，再次点击当前筛选可取消；`可加入` 过滤掉当前无法加入的房间
- 桌面端可用鼠标滚轮滚动列表，移动端可按住列表区域上下滑动
- 单击房间卡片选中，双击直接尝试加入；键盘 / 手柄焦点落在房间卡片上时，按 `Enter` / `Space` / `ui_accept` 也会尝试加入
- 卡片显示状态、游戏版本、MOD 版本和 relay 就绪状态
- 连续刷新失败时，顶部状态条会提示建议切换服务器

## 键盘、手柄与无障碍

- 大厅支持键盘 / 手柄式焦点导航：`Tab` / `Shift+Tab` 和方向键在按钮、输入框、筛选、分页与房间卡片之间移动焦点
- `Esc` 优先关闭当前最上层弹窗；没有弹窗时才退出大厅，避免误返回游戏主菜单
- 房间卡片可被焦点选中，焦点移动到卡片时会同步更新右侧加入按钮状态
- 如果安装了 `say-the-spire2` 盲人辅助模组，STS2 LAN Connect 会在启动时软检测并把大厅焦点交给其朗读系统；未安装该模组时不会增加额外依赖
- 房间卡片朗读内容包括房名、房主、人数、是否需要密码、当前是否可加入、游戏模式与选中状态；不会朗读房间密码
- `F7`：剪贴板有有效邀请码时弹出加入确认；邀请确认弹窗已打开时执行加入
- `F8`：进入房间后打开 / 收起右上角房间聊天面板

## 房主流程

1. 打开 `游戏大厅`，点击 `创建房间`
2. 填写房间名，选择类型，可选填密码；最大人数默认 8 人，上限 8 人
3. 如需与 `0.2.2` 玩家兼容联机，请将房间人数设为 `4`；`5-8` 人房仅支持 `0.2.3+`
4. 发布成功后，客户端会自动启动本地 ENet Host、向大厅注册房间并持续发送心跳保活

## 玩家流程

1. 打开 `游戏大厅`，点击 `刷新大厅`
2. 用搜索和筛选定位目标房间
3. 如果刷新失败或延迟异常，先用 `切换服务器` 换一个大厅
4. 选择目标房间加入；如房间有密码，按提示输入

## 频道聊天与房间聊天

- 大厅右侧 `频道聊天` 属于当前服务器节点；切换服务器后会进入另一条频道
- 频道昵称来自客户端显示名，不代表已验证账号或管理身份

- 进入已连接房间后，右上角出现 `房间聊天` 按钮；点击后展开面板，按 `Enter` 或点 `发送` 发消息
- 也可以按 `F8` 打开或收起聊天面板；文本输入框聚焦时不会把快捷键内容写进聊天文本
- 面板收起时，收到当前房间的新消息会显示未读角标
- 房间内仍可手动切到频道页查看大厅消息；大厅频道新消息不会触发房间角标、唤醒淡出面板或自动切页
- 聊天面板标题栏和按钮支持长按拖动，位置保存到本地配置
- 房间聊天走大厅控制通道，仅在当前房间内广播，不写入续局存档，也不保留历史
- Emoji 与物品引用会按双方协商能力显示；旧版本或功能关闭时自动降级为普通文本

### 富聊天引用操作

- **Emoji**：点击输入框右侧的笑脸按钮，在面板中选择表情。表情会先插入草稿，可继续输入文字，再按 `Enter` 或点击 `发送`；`Shift + Enter` 用于换行。
- **卡牌 / 遗物 / 药水**：先点击游戏画面空白处，让聊天输入框失去焦点并关闭表情或物品预览；macOS 按住 `Option`、Windows/Linux 按住 `Alt`，再左键点击可见卡牌、遗物栏图标或药水栏图标。引用会插入当前选中频道的草稿，不会立即发送。
- **战斗状态**：在战斗中使用同样的 `Option/Alt + 左键` 操作点击能力、增益/减益图标或玩家角色，可引用能力名称、层数、持有者、施加者或玩家。战斗状态只能分享到 `房间聊天`，不能分享到大厅 `频道聊天`；怪物目标引用当前版本尚未开放。
- **编辑与查看**：引用可以与普通文字混排。用方向键移动光标，在引用旁按 `Backspace` / `Delete`，或选中引用后删除。收到卡牌、遗物或药水引用后，桌面端可将鼠标悬停在引用标签上查看本地化预览。
- **Android 限制**：纯触屏目前可以发送文字和 Emoji，也可以接收并显示引用；主动插入物品或战斗引用仍依赖 `Alt + 左键`，需要外接键盘和鼠标。

## 房间管理

- 房主在游戏内暂停菜单中可找到 `房间管理` 按钮（位于"百科大全"和"放弃"之间），点击后可：
  - **聊天开关**：启用或禁用房间聊天；关闭后所有成员的聊天面板自动隐藏
  - **在线玩家列表**：查看当前房间内所有在线玩家；房主可点击 `移出` 踢出玩家
  - **重开一局**：自动通知队友进入重开流程，并将房主带回主菜单重启当前多人续局
- 普通成员可查看面板，但无法操作
- 在准备页面，远程玩家名旁有红色 `X` 踢出按钮，房主可在开局前直接移除玩家
- 被踢出的玩家会收到提示且无法重新加入同一房间

## 多人续局

- 房主重新进入已存在的多人续局存档时，续局会自动重新发布到大厅，沿用原有房间信息，无需重新手动建房
- 房主点击 `重开一局` 后，会短暂断开并自动回到多人续局载入页面
- 队友会自动回主菜单并按自己的 `desiredSavePlayerNetId` 轮询重连同一续局房间
- 若自动重连超时，可在 `游戏大厅` 手动加入作为兜底
- 如续局仍有空闲角色槽位，加入方会先看到角色选择，再进入联机
- 角色选择弹窗同时显示角色名和原玩家名（如"铁甲战士（小明）"），方便准确找回自己的槽位

## 加入等待提示

- 点击加入后，出现加载弹窗并按阶段更新，例如：
  - 正在向大厅申请加入
  - 大厅已响应，开始连接房主
  - 正在尝试公网 / 局域网候选地址
  - 直连超时后自动尝试 relay fallback
  - 连接成功，进入联机界面
- 加入时间过长时，弹窗右上角出现取消按钮，点击停止当前流程
- 加入失败时，弹窗会区分版本不一致、MOD 不一致、房间已开局、房间已满或具体网络失败原因

## 调试报告

- 大厅设置区提供 `复制本地调试报告`
- 报告包含当前选中房间的 roomId、本地平台玩家 ID、存档快照和最近相关客户端日志
- 向开发者反馈问题时，请优先提供此报告

## 网络说明

- 默认连接策略由安装包内的 `lobby-defaults.json` 决定，可选 `direct-first`、`relay-first` 或 `relay-only`
- 公开包默认使用阿里云大厅 `47.111.146.69:8787` 作为兜底社区节点，并通过 CF 发现入口 `https://sts2-gamelobby-register.xyz` + 内置种子聚合可用服务器；测试节点 `101.35.217.99:8788` 固定排在服务器列表第一位。显示“支持 0.5.1+ MOD 同步”的服务器已实时声明加入前 gameplay MOD 预检/Workshop 同步能力；旧的 `47.111.146.69:18787` 公开目录在 v0.4.0 中不再参与运行时发现
- 兼容矩阵当前统一规则为：
  - `4` 人房发布 `legacy_4p`，用于兼容 `0.2.2`
  - `5-8` 人房发布 `extended_8p`，仅支持 `0.2.3+`
  - 客户端实际日志 / 调试报告会同时记录 `compatibilityProfile`、`connectionStrategy`、`effectiveMaxPlayers`、`publishedProtocolProfile`
- MOD 内置 5-8 人支持；`4` 人房自动启用 `legacy_4p` 兼容协议，可与 `0.2.2` 联机；`5-8` 人房仅支持 `0.2.3+`
- 检测到 RMP 等外部扩展人数 MOD 时，内置补丁会自动跳过以避免冲突
- `切换服务器` 从 CF 发现入口、本地缓存与内置种子聚合可用大厅，并将选择写入客户端的 HTTP 覆盖设置
- 大厅显示的服务延迟来自独立探测，不是房间列表接口总耗时
- 房主机器开放 `33771/UDP` 直连可达时，加入速度更快；服务端启用 relay fallback 需放行 `39000-39149/UDP`
- `WS /control` 承担大厅协调、房主会话保活和房间聊天，不替代游戏联机数据通路

## 设置说明

- 普通玩家通常只需填写 `玩家名`
- 切换环境时优先使用 `切换服务器`；仅在排障时才展开 `开发网络设置`
- 开发网络设置当前只保留 `HTTP 覆盖` 与 `建房令牌`；公共发现入口和内置种子来自安装包内的 `lobby-defaults.json`
- 安装包附带 `lobby-defaults.json` 时，默认大厅地址、默认建房令牌、CF 发现入口与内置种子会自动生效，不在 UI 中明文展示
- 当前 MOD 版本号以 `mods/sts2_lan_connect/sts2_lan_connect.json` 为准

## 常见问题

### 大厅里看不到房间

- 确认大厅服务健康检查正常
- 确认搜索关键词、分页和筛选没有遗漏目标房间
- 尝试 `切换服务器` 后重新刷新
- 确认房主房间是否发布成功

### 大厅能刷出来，但加入总是超时

- 建房、刷新、加入申请等控制面请求走 `HTTP/TCP`；直连和 relay fallback 走 `UDP`
- 先尝试 `切换服务器`，排除单个节点拥堵或抖动
- 如启用了 Clash、Surge、TUN、系统全局代理或本地网络过滤工具，必须让大厅服务器 IP 走 `DIRECT`
- 如房间详情显示 `relay` 尚未就绪，先刷新后再重试；此时常见提示是“房主 relay 尚未注册完成，请稍后刷新后再试”
- 向开发者反馈时，优先附上本地调试报告；其中会带 `compatibility_matrix_policy`、选中房间兼容摘要与最近连接日志

### 提示版本不一致 / MOD 不一致

- `version_mismatch` 通常表示游戏版本、协议版本或关键数据版本不一致
- `mod_mismatch` / `mod_version_mismatch` 表示双方 STS2 LAN Connect 或相关联机 MOD 组合不一致
- 所有联机玩家应尽量使用同一批 release，并核对 `mods/sts2_lan_connect/sts2_lan_connect.json` 中的版本号
- `4` 人房兼容 `0.2.2` 的 `legacy_4p`；`5-8` 人房要求 `0.2.3+` 的 `extended_8p`

### 提示房间已满 / 已关闭 / 已开局

- `room_full`：房间人数已满，只能等待空位或让房主调整房间
- `room_closed`：房主已关闭房间，或该房间已经从大厅下线
- `room_started`：该房间已经进入游戏，不能再以新玩家身份加入

### 提示续局角色不可用

- `save_slot_required`：这是续局房间，必须先选择一个可接管角色
- `save_slot_invalid`：所选角色槽位不存在，通常是房间状态已变化
- `save_slot_unavailable`：该角色已被其他玩家接管，或当前已没有可接管角色
- 此类问题优先刷新房间列表后重试，必要时让房主重新确认当前续局槽位状态

### 提示 MOD 不一致

- 所有联机玩家必须使用完全相同版本的 STS2 LAN Connect
- 以 `mods/sts2_lan_connect/sts2_lan_connect.json` 中的版本号为准
- 缺少 Workshop 项时先核对标题、发布者、大小和目标版本，再决定是否订阅
- 缺少手动项时按列表自行安装；没有其他自动下载来源
- 多余 gameplay MOD 默认不会禁用；选择禁用后必须二次确认并重启
- relaxed“仍然尝试加入”只保留给 MOD 差异，游戏版本不同仍然拒绝

### 安卓端启动就弹"致命错误"

- 确认 `mods/sts2_lan_connect/sts2_lan_connect.json` 中的版本号为当前发布版本（本仓库 v0.5.1 文档对应 `0.5.1`）
- 如果是覆盖安装旧包，建议先完整卸载再重新安装，确保 `sts2_lan_connect.dll`、`sts2_lan_connect.pck` 和 `sts2_lan_connect.json` 同步更新
- 如仍崩溃，将最新 `godot.log` 和本地调试报告一并发给开发者

### 安卓端进了主菜单，但打开多人页面 / 游戏大厅异常

- 确认 `mods/sts2_lan_connect/sts2_lan_connect.json` 版本号为当前发布版本（本仓库 v0.5.1 文档对应 `0.5.1`）
- 确认安装的是当前发布的客户端包，而非更早的旧包
- 如果是覆盖安装旧包，建议先完整卸载再重新安装，确保三个文件来自同一批 release
- 如问题仍存在，将最新 `godot.log` 和本地调试报告一并发给开发者

### 需要回退到手动 LAN/IP

- 官方 Host / Join 页面的手动 LAN 调试入口仍然保留，可作为排障回退方案

---

<a name="english"></a>

# STS2 LAN Connect User Guide

## v0.5.1 MOD Preflight Before Join

- Only gameplay-affecting MODs and required dependencies are compared. Ordinary unrelated MODs are not shown, disabled, or used to block joining.
- Game-version mismatches are rejected first and cannot be bypassed by synchronization or relaxed continuation.
- Steam desktop shows real Workshop metadata and subscribes only after consent. Android, non-Steam, and unavailable SteamAPI environments provide manual guidance only.
- Extra gameplay MODs start unchecked and require explicit selection plus a second confirmation before disablement.
- Restart after any MOD change. Public rooms can resume and preflight again for 15 minutes; password rooms ask for the password again. Passwords and tokens are never persisted.
- The client never downloads DLL, PCK, or ZIP content from hosts, lobby services, or arbitrary URLs.

## v0.5.0 Chat Upgrade

- The lobby sidebar now includes server-channel chat for the currently selected lobby node; joining a room is not required.
- Server-channel history is node-local process memory, disappears on restart, and is not replicated to other lobby nodes.
- Room chat supports Emoji, card/relic/potion references, and safely degraded combat references. Older clients receive compatibility text only.
- The same v0.5.0 client package supports game versions `0.107.1`, `0.108.0`, and `0.109.0`.
- Every host and client in a room must use the exact same game version; a mismatch is reported and rejected during join.
- Rich-chat edits on Android no longer restart the system keyboard after each insertion or deletion.
- The complete chat feature set requires both the v0.5.0 client and v0.5.0 lobby service.

## Entering the Lobby

1. Launch the game and go to the multiplayer home screen
2. Click `Game Lobby`
3. Create, refresh, and join rooms directly from the lobby
4. If the default lobby is congested or unavailable, click `Switch Server` in the title bar to select another

If the clipboard already contains a valid invite code, clicking `Game Lobby` skips the server picker, opens the lobby directly, and shows the invite confirmation. The invite payload includes the target server and room ID, so invites from another lobby can temporarily switch to that server during join.

## Announcements

- The top of the lobby displays a rotating announcement banner from the server, supporting four styles: `Update`, `Event`, `Warning`, and `Info`
- On desktop, use the left/right arrows or dot indicators to navigate; in compact landscape mode, a `1/N` counter is shown instead
- Announcements rotate every 6 seconds by default; hovering pauses rotation and the progress bar fills from left to right

## Lobby List Operations

- Supports keyword search, pagination, and stackable filters: `Public`, `Locked`, `Joinable`
- `Public` and `Locked` are mutually exclusive; clicking the active filter again deselects it; `Joinable` hides rooms that cannot currently be entered
- Desktop supports mouse-wheel scrolling; mobile supports press-and-drag scrolling
- Single-click a room card to select it; double-click to attempt joining immediately. When a room card has keyboard/controller focus, `Enter` / `Space` / `ui_accept` also attempts to join it
- Room cards display status, game version, MOD version, and relay readiness
- If repeated refreshes fail, the status bar suggests switching servers

## Keyboard, Controller, and Accessibility

- The lobby supports keyboard/controller-style focus navigation: `Tab` / `Shift+Tab` and arrow keys move between buttons, inputs, filters, pagination, and room cards
- `Esc` closes the topmost dialog first; only when no dialog is open does it leave the lobby, avoiding accidental returns to the game main menu
- Room cards are focusable, and focusing one also updates the sidebar join-button state
- If the `say-the-spire2` accessibility mod is installed, STS2 LAN Connect soft-detects it at startup and forwards lobby focus announcements to its speech system; without that mod, no extra dependency is required
- Room-card announcements include room name, host, player count, password requirement, joinability, game mode, and selection state; room passwords are never spoken
- `F7`: opens invite confirmation when the clipboard has a valid invite; accepts the visible invite confirmation when it is already open
- `F8`: opens or collapses the top-right room chat panel after joining a room

## Host Flow

1. Open `Game Lobby` and click `Create Room`
2. Enter a room name, choose a room type, and optionally set a password; max players defaults to 8 (upper limit: 8)
3. To allow `0.2.2` players to join, set max players to `4`; rooms of `5-8` require `0.2.3+`
4. After a successful publish, the client automatically starts the local ENet Host, registers the room with the lobby, and sends periodic heartbeats

## Player Flow

1. Open `Game Lobby` and click `Refresh Lobby`
2. Use search and filters to locate the target room
3. If refresh fails or latency is abnormal, use `Switch Server` first
4. Select the room and join; if the room has a password, enter it when prompted

## Server-Channel and Room Chat

- `Channel Chat` in the lobby belongs to the current server node; switching servers moves to a different channel.
- Display names are client-provided and are not verified account or moderator identities.

- After connecting to a room, a `Room Chat` button appears in the top-right corner; click to expand the panel and send messages with `Enter` or the `Send` button
- You can also press `F8` to open or collapse the chat panel; focused text inputs do not receive shortcut text
- When the panel is collapsed, new messages from the current room show a badge indicator
- The server-channel tab remains available for manual viewing in-room, but server-channel messages do not trigger room badges, wake the faded panel, or switch tabs automatically
- The chat panel title bar and button support press-and-drag repositioning; the position is saved to local config
- Room chat uses the lobby control channel, is broadcast only within the current room, is not written to save files, and retains no history
- Emoji and item references follow negotiated peer capabilities and degrade to ordinary compatibility text for old clients or disabled features

### Rich Chat Reference Controls

- **Emoji**: click the smile button beside the composer and choose an emoji. It is inserted into the draft, so you can add text before sending with `Enter` or `Send`; use `Shift + Enter` for a newline.
- **Cards / relics / potions**: first click an empty part of the game view so the chat composer loses focus and close any emoji or item preview. Hold `Option` on macOS or `Alt` on Windows/Linux, then left-click a visible card, relic inventory icon, or potion-slot icon. The reference is inserted into the selected channel draft and is not sent immediately.
- **Combat state**: use the same `Option/Alt + left-click` gesture on a power, buff/debuff icon, or player character to share the power name, amount, owner, applier, or player. Combat references are room-chat only; monster-target references are not enabled in this release.
- **Editing and viewing**: references can be mixed with ordinary text. Move the caret with the arrow keys and remove a neighboring or selected reference with `Backspace` / `Delete`. On desktop, hover a received card, relic, or potion chip to open its localized preview.
- **Android limitation**: touch-only users can send text and Emoji and can receive references. Inserting item or combat references still requires an external keyboard and mouse for the `Alt + left-click` gesture.

## Room Management

- The host can find the `Room Management` button in the in-game pause menu (between "Compendium" and "Abandon"); clicking it opens a panel with:
  - **Chat Toggle**: enable or disable room chat; disabling it hides the chat panel for all members
  - **Online Player List**: view all players currently in the room; the host can click `Remove` to kick a player
  - **Restart Run**: notify teammates, return the host to main menu, and restart the current multiplayer save flow
- Regular members can view the panel but cannot make changes
- On the ready screen, a red `X` kick button appears next to each remote player's name; the host can remove players before the run starts
- Kicked players receive a notification and cannot rejoin the same room

## Save-Run Multiplayer

- When a host re-enters an existing multiplayer save, the run is automatically re-published to the lobby using the original room info — no need to create a new room manually
- After the host clicks `Restart Run`, the host briefly disconnects and is auto-routed back to multiplayer save-load
- Teammates are auto-routed to main menu and rejoin by polling with their own `desiredSavePlayerNetId`
- If auto-rejoin times out, manual join from `Game Lobby` remains the fallback path
- If the save still has open character slots, joining players will see a character selection screen before entering the session
- The character selection dialog shows both the character name and the original player's name (e.g., "Ironclad (Alice)") to help players accurately reclaim their slots

## Join Progress Dialog

- After clicking join, a loading dialog appears and updates by stage, for example:
  - Requesting to join from the lobby
  - Lobby responded; connecting to host
  - Trying public/LAN candidate addresses
  - Direct connection timed out; attempting relay fallback
  - Connection successful; entering multiplayer
- If joining takes too long, a cancel button appears in the top-right of the dialog
- On failure, the dialog distinguishes between version mismatch, MOD mismatch, game already started, room full, and specific network errors
- The client debug report and runtime logs now record the effective compatibility matrix summary, including `compatibilityProfile`, `connectionStrategy`, `effectiveMaxPlayers`, and `publishedProtocolProfile`

## Debug Report

- The lobby settings area provides a `Copy Local Debug Report` button
- The report includes the selected room's roomId, local platform player ID, save snapshot, and recent client logs
- When reporting issues to the developer, please provide this report first

## Network Notes

- The default connection strategy is determined by `lobby-defaults.json` in the installation package: `direct-first`, `relay-first`, or `relay-only`
- The public release defaults to the Alibaba Cloud lobby at `47.111.146.69:8787` as a fallback community node and aggregates available servers through the CF discovery worker `https://sts2-gamelobby-register.xyz` plus bundled seed peers. Test node `101.35.217.99:8788` is always pinned first. Servers tagged `Supports 0.5.1+ MOD Sync` have declared live gameplay-MOD preflight/Workshop sync capability. The legacy `47.111.146.69:18787` directory is no longer used for runtime discovery in v0.4.0
- The compatibility matrix is currently unified as:
  - `4`-player rooms publish `legacy_4p` for `0.2.2` compatibility
  - `5-8`-player rooms publish `extended_8p` and require `0.2.3+`
  - Client runtime logs and debug reports record `compatibilityProfile`, `connectionStrategy`, `effectiveMaxPlayers`, and `publishedProtocolProfile`
- MOD supports 2-8 players natively; 4-player rooms automatically use the `legacy_4p` compatibility protocol for `0.2.2` clients; 5-8 player rooms require `0.2.3+`
- If external player-count expansion MODs such as RMP are detected, the built-in patch skips automatically to avoid conflicts
- `Switch Server` aggregates available lobbies from the CF discovery worker, local cache, and bundled seed peers, then writes the selected lobby to client override settings
- The latency shown in the lobby comes from an independent probe, not the total round-trip time of the room list request
- Opening port `33771/UDP` on the host machine improves connection speed; relay fallback requires ports `39000-39149/UDP` to be open on the server
- `WS /control` handles lobby coordination, host session keepalive, and room chat; it does not replace the game's multiplayer data channel

## Settings

- Regular players typically only need to set their `Player Name`
- Use `Switch Server` when changing environments; only open `Developer Network Settings` for troubleshooting
- Developer Network Settings currently expose `HTTP Override` and `Create-Room Token`; the public discovery endpoint and bundled seed peers come from the package's `lobby-defaults.json`
- If the installation package includes `lobby-defaults.json`, the default lobby address, create-room token, CF discovery endpoint, and bundled seed peers take effect automatically and are not shown in plain text in the UI
- The current MOD version is determined by `mods/sts2_lan_connect/sts2_lan_connect.json`

## FAQ

### No rooms visible in the lobby

- Confirm the lobby service health check is passing
- Confirm search keywords, pagination, and filters are not hiding the target room
- Try `Switch Server` and refresh again
- Confirm the host's room was published successfully

### Lobby refreshes fine, but joining always times out

- Room creation, refresh, and join requests use `HTTP/TCP`; direct connections and relay fallback use `UDP`
- Try `Switch Server` first to rule out congestion or instability on a single node
- If you are using Clash, Surge, TUN, a system-wide proxy, or a local network filter, ensure the lobby server IP is routed `DIRECT`
- If the room shows relay as not ready yet, refresh and retry later; the common server-side error is `relay_host_not_ready`
- When reporting issues, include the local debug report first; it now contains `compatibility_matrix_policy`, selected-room compatibility, and recent connection logs

### Version mismatch / MOD mismatch errors

- `version_mismatch` usually means the game version, protocol layer, or critical data version does not line up
- `mod_mismatch` / `mod_version_mismatch` means the STS2 LAN Connect build or related multiplayer MOD set differs between peers
- All players should use the same release batch whenever possible, and verify the version in `mods/sts2_lan_connect/sts2_lan_connect.json`
- `4`-player rooms use the `legacy_4p` compatibility path for `0.2.2`; `5-8`-player rooms require `0.2.3+` with `extended_8p`

### Room full / closed / already started

- `room_full`: the room has no free slot
- `room_closed`: the host already closed the room or the listing went offline
- `room_started`: the run has already started and new players cannot join as fresh participants

### Save-slot unavailable errors

- `save_slot_required`: this is a save-run room and you must pick a reclaimable character first
- `save_slot_invalid`: the selected slot no longer exists
- `save_slot_unavailable`: the slot has already been reclaimed by someone else, or no reclaimable slot is currently available
- Refresh the room list first; if it still fails, ask the host to confirm the current save-slot state

### MOD version mismatch error

- All players in a session must use the exact same version of STS2 LAN Connect
- Verify the version number in `mods/sts2_lan_connect/sts2_lan_connect.json`

### Android: "Fatal Error" on launch

- Confirm the version number in `mods/sts2_lan_connect/sts2_lan_connect.json` matches the current release (this v0.5.1 documentation corresponds to `0.5.1`)
- If you installed over an older package, fully uninstall first and then reinstall to ensure `sts2_lan_connect.dll`, `sts2_lan_connect.pck`, and `sts2_lan_connect.json` are all updated together
- If the crash persists, send the latest `godot.log` and the local debug report to the developer

### Android: Main menu loads, but multiplayer page / Game Lobby behaves abnormally

- Confirm the version number in `mods/sts2_lan_connect/sts2_lan_connect.json` matches the current release (this v0.5.1 documentation corresponds to `0.5.1`)
- Confirm you installed the current release package, not an older package
- If you installed over an older package, fully uninstall first and then reinstall to ensure all three files come from the same release batch
- If the issue persists, send the latest `godot.log` and the local debug report to the developer

### Need to fall back to manual LAN/IP

- The manual LAN debug entry point in the official Host / Join pages is still available as a fallback for troubleshooting
