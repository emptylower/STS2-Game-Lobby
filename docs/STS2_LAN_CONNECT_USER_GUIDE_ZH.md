<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

# STS2 LAN Connect 使用说明

## 进入大厅

1. 启动游戏并进入多人首页
2. 点击 `游戏大厅`
3. 直接在大厅里完成建房、刷新和加入
4. 如果默认大厅拥堵或不可用，点标题栏的 `切换服务器` 切换到其他可用大厅

## 顶部公告

- 大厅顶部显示服务器下发的公告轮播，支持 `更新`、`活动`、`警告`、`信息` 四类样式
- 桌面端可用左右箭头和点状页码切换；紧凑横屏模式改为 `1/N` 数字指示
- 公告默认每 6 秒自动切换，鼠标悬停时暂停，底部进度条从左往右累积

## 大厅列表操作

- 支持关键词搜索、分页和可叠加筛选（`公开`、`上锁`、`可加入`）
- `公开` 与 `上锁` 互斥，再次点击当前筛选可取消；`可加入` 过滤掉当前无法加入的房间
- 桌面端可用鼠标滚轮滚动列表，移动端可按住列表区域上下滑动
- 单击房间卡片选中，双击直接尝试加入
- 卡片显示状态、游戏版本、MOD 版本和 relay 就绪状态
- 连续刷新失败时，顶部状态条会提示建议切换服务器

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

## 房间聊天

- 进入已连接房间后，右上角出现 `房间聊天` 按钮；点击后展开面板，按 `Enter` 或点 `发送` 发消息
- 面板收起时，收到新消息会显示未读角标
- 聊天面板标题栏和按钮支持长按拖动，位置保存到本地配置
- 聊天走大厅控制通道，仅在当前房间内广播，不写入续局存档

## 房间管理

- 房主在游戏内暂停菜单中可找到 `房间管理` 按钮（位于"百科大全"和"放弃"之间），点击后可：
  - **聊天开关**：启用或禁用房间聊天；关闭后所有成员的聊天面板自动隐藏
  - **在线玩家列表**：查看当前房间内所有在线玩家；房主可点击 `移出` 踢出玩家
- 普通成员可查看面板，但无法操作
- 在准备页面，远程玩家名旁有红色 `X` 踢出按钮，房主可在开局前直接移除玩家
- 被踢出的玩家会收到提示且无法重新加入同一房间

## 多人续局

- 房主重新进入已存在的多人续局存档时，续局会自动重新发布到大厅，沿用原有房间信息，无需重新手动建房
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
- 公开包默认使用阿里云大厅 `47.111.146.69:8787`，公共服务器目录为 `47.111.146.69:18787`，固定策略为 `test_relaxed + relay-only`
- MOD 内置 5-8 人支持；`4` 人房自动启用 `legacy_4p` 兼容协议，可与 `0.2.2` 联机；`5-8` 人房仅支持 `0.2.3+`
- 检测到 RMP 等外部扩展人数 MOD 时，内置补丁会自动跳过以避免冲突
- `切换服务器` 从中心服务器拉取可用大厅列表，并将选择写入客户端覆盖设置
- 大厅显示的服务延迟来自独立探测，不是房间列表接口总耗时
- 房主机器开放 `33771/UDP` 直连可达时，加入速度更快；服务端启用 relay fallback 需放行 `39000-39149/UDP`
- `WS /control` 承担大厅协调、房主会话保活和房间聊天，不替代游戏联机数据通路

## 设置说明

- 普通玩家通常只需填写 `玩家名`
- 切换环境时优先使用 `切换服务器`；仅在排障时才展开 `开发网络设置`
- 如需切换公共服务器目录来源，在开发网络设置里修改 `中心服务器覆盖`
- 安装包附带 `lobby-defaults.json` 时，默认大厅地址自动生效，不在 UI 中明文展示
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

### 提示 MOD 不一致

- 所有联机玩家必须使用完全相同版本的 STS2 LAN Connect
- 以 `mods/sts2_lan_connect/sts2_lan_connect.json` 中的版本号为准

### 安卓端启动就弹"致命错误"

- 确认 `mods/sts2_lan_connect/sts2_lan_connect.json` 中的版本号为 `0.2.3`
- 如果是覆盖安装旧包，建议先完整卸载再重新安装，确保 `sts2_lan_connect.dll`、`sts2_lan_connect.pck` 和 `sts2_lan_connect.json` 同步更新
- 如仍崩溃，将最新 `godot.log` 和本地调试报告一并发给开发者

### 安卓端进了主菜单，但打开多人页面 / 游戏大厅异常

- 确认 `mods/sts2_lan_connect/sts2_lan_connect.json` 版本号为 `0.2.3`
- 确认安装的是重新打包后的 `0.2.3` 刷新包，而非更早的旧包
- 如果是覆盖安装旧包，建议先完整卸载再重新安装，确保三个文件来自同一批 release
- 如问题仍存在，将最新 `godot.log` 和本地调试报告一并发给开发者

### 需要回退到手动 LAN/IP

- 官方 Host / Join 页面的手动 LAN 调试入口仍然保留，可作为排障回退方案

---

<a name="english"></a>

# STS2 LAN Connect User Guide

## Entering the Lobby

1. Launch the game and go to the multiplayer home screen
2. Click `Game Lobby`
3. Create, refresh, and join rooms directly from the lobby
4. If the default lobby is congested or unavailable, click `Switch Server` in the title bar to select another

## Announcements

- The top of the lobby displays a rotating announcement banner from the server, supporting four styles: `Update`, `Event`, `Warning`, and `Info`
- On desktop, use the left/right arrows or dot indicators to navigate; in compact landscape mode, a `1/N` counter is shown instead
- Announcements rotate every 6 seconds by default; hovering pauses rotation and the progress bar fills from left to right

## Lobby List Operations

- Supports keyword search, pagination, and stackable filters: `Public`, `Locked`, `Joinable`
- `Public` and `Locked` are mutually exclusive; clicking the active filter again deselects it; `Joinable` hides rooms that cannot currently be entered
- Desktop supports mouse-wheel scrolling; mobile supports press-and-drag scrolling
- Single-click a room card to select it; double-click to attempt joining immediately
- Room cards display status, game version, MOD version, and relay readiness
- If repeated refreshes fail, the status bar suggests switching servers

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

## Room Chat

- After connecting to a room, a `Room Chat` button appears in the top-right corner; click to expand the panel and send messages with `Enter` or the `Send` button
- When the panel is collapsed, unread messages show a badge indicator
- The chat panel title bar and button support press-and-drag repositioning; the position is saved to local config
- Chat uses the lobby control channel and is broadcast only within the current room; it is not written to save files

## Room Management

- The host can find the `Room Management` button in the in-game pause menu (between "Compendium" and "Abandon"); clicking it opens a panel with:
  - **Chat Toggle**: enable or disable room chat; disabling it hides the chat panel for all members
  - **Online Player List**: view all players currently in the room; the host can click `Remove` to kick a player
- Regular members can view the panel but cannot make changes
- On the ready screen, a red `X` kick button appears next to each remote player's name; the host can remove players before the run starts
- Kicked players receive a notification and cannot rejoin the same room

## Save-Run Multiplayer

- When a host re-enters an existing multiplayer save, the run is automatically re-published to the lobby using the original room info — no need to create a new room manually
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

## Debug Report

- The lobby settings area provides a `Copy Local Debug Report` button
- The report includes the selected room's roomId, local platform player ID, save snapshot, and recent client logs
- When reporting issues to the developer, please provide this report first

## Network Notes

- The default connection strategy is determined by `lobby-defaults.json` in the installation package: `direct-first`, `relay-first`, or `relay-only`
- The public release defaults to the Alibaba Cloud lobby at `47.111.146.69:8787`, with the public server directory at `47.111.146.69:18787`, using `test_relaxed + relay-only`
- MOD supports 2-8 players natively; 4-player rooms automatically use the `legacy_4p` compatibility protocol for `0.2.2` clients; 5-8 player rooms require `0.2.3+`
- If external player-count expansion MODs such as RMP are detected, the built-in patch skips automatically to avoid conflicts
- `Switch Server` fetches the available lobby list from the central server and writes the selection to client override settings
- The latency shown in the lobby comes from an independent probe, not the total round-trip time of the room list request
- Opening port `33771/UDP` on the host machine improves connection speed; relay fallback requires ports `39000-39149/UDP` to be open on the server
- `WS /control` handles lobby coordination, host session keepalive, and room chat; it does not replace the game's multiplayer data channel

## Settings

- Regular players typically only need to set their `Player Name`
- Use `Switch Server` when changing environments; only open `Developer Network Settings` for troubleshooting
- To change the public server directory source, modify `Central Server Override` in Developer Network Settings
- If the installation package includes `lobby-defaults.json`, the default lobby address takes effect automatically and is not shown in plain text in the UI
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

### MOD version mismatch error

- All players in a session must use the exact same version of STS2 LAN Connect
- Verify the version number in `mods/sts2_lan_connect/sts2_lan_connect.json`

### Android: "Fatal Error" on launch

- Confirm the version number in `mods/sts2_lan_connect/sts2_lan_connect.json` is `0.2.3`
- If you installed over an older package, fully uninstall first and then reinstall to ensure `sts2_lan_connect.dll`, `sts2_lan_connect.pck`, and `sts2_lan_connect.json` are all updated together
- If the crash persists, send the latest `godot.log` and the local debug report to the developer

### Android: Main menu loads, but multiplayer page / Game Lobby behaves abnormally

- Confirm the version number in `mods/sts2_lan_connect/sts2_lan_connect.json` is `0.2.3`
- Confirm you installed the re-packaged `0.2.3` refresh build, not an earlier `0.2.3` release
- If you installed over an older package, fully uninstall first and then reinstall to ensure all three files come from the same release batch
- If the issue persists, send the latest `godot.log` and the local debug report to the developer

### Need to fall back to manual LAN/IP

- The manual LAN debug entry point in the official Host / Join pages is still available as a fallback for troubleshooting
