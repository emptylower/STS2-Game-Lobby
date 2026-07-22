# Changelog

本项目从 v0.5.0 起在此记录客户端 MOD 与 lobby-service 的公开版本变更。

## [0.5.2] - 2026-07-22

公开预览标签为 [`v0.5.2-rc.1`](https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.2-rc.1)。该 Pre-release 从功能分支发布，未合并 main，也不更新 Steam Workshop。

### Added

- 新增统一的一次性引用模式：Android 点击聊天输入区旁的“引用”按钮，桌面按 `Alt+R` 进入或取消；成功捕获一个卡牌、遗物、药水、状态或玩家后自动退出并把焦点交回真实文本输入位置。
- Android 可点击消息中的引用打开固定原生说明预览，并通过点击外部、`Esc` 或关闭按钮退出。

### Changed

- 原有 `Alt+左键` 直接引用继续保留，并与按钮和 `Alt+R` 共用同一状态机、目标能力矩阵和一次性捕获链路。
- 混合文字、Emoji 与引用改为单一行内富文本控件自然换行；引用使用仅本地可解析的 opaque meta，不向聊天协议暴露模型 ID。
- 卡牌、遗物、药水和 Power 使用游戏原生 hover-tip 体系；Power 说明注入实际层数、玩家和动态变量，不再固定裁成四行。
- 客户端升级到 `0.5.2`；lobby-service 与聊天线协议继续使用 `0.5.1`，无需升级服务端。

### Fixed

- 修复引用实体造成的桌面异常空隙、独立块换行和位置偏移。
- 修复空草稿插入引用后焦点落在 Button，导致中文组合输入或 Android 键盘输入无法继续的问题。
- 修复 Android 没有触屏引用入口和消息说明只能依赖桌面悬停的问题。

### Security and Compatibility

- 不改变 v0.5.1 feature intersection、限额、generation、legacy fallback、MOD 同步或加入流程；v0.5.2 客户端可与 v0.5.1 服务端和客户端安全互通。
- 不启用 monster target reference，不从远端下载预览资源，不让引用执行游戏动作，不泄漏内部 model ID。
- 同一客户端包以游戏 `0.107.1` 与 `0.109.0` 为加载兼容目标；`0.108.0` 不在本次发布适配范围内。

## [0.5.1] - 2026-07-18

### Added

- 加入前 gameplay MOD 私有预检：只比较 `affects_gameplay=true` 的 MOD 及必要 dependency，不公开房主清单，也不提前签发 join ticket。
- Steam 桌面客户端可在用户确认后订阅缺失的 Workshop 项，显示真实条目元数据、下载进度、取消和重试状态。
- MOD 改动后的重启恢复：15 分钟内恢复服务器、房间和续局槽位；密码房重新询问密码，pending join 不保存密码或 token。

### Changed

- 客户端 MOD 与 lobby-service 同步升级到 `0.5.1`；v0.5.0 对端缺少预检能力时安全回退原加入流程。
- 多余 gameplay MOD 默认不选择禁用，只有用户选择并二次确认后才修改本机启用状态。
- Android、非 Steam 与 SteamAPI 不可用环境只显示手动处理项，不尝试自动下载。
- 服务器选择列表固定将测试节点 `101.35.217.99:8788` 排在第一位；声明 MOD 同步能力的节点显示“支持 0.5.1+ MOD 同步”。
- Steam 创意工坊条目改名为“游戏大厅”，说明改为面向玩家的分段功能介绍。

### Fixed

- 兼容已发布但 `/peers/metrics` 尚未携带 MOD 字段的协议 1 节点：客户端只在字段缺失时从 `/probe` 补充能力标识。
- 修复 MOD inventory 的 nullable dependency 与跨游戏版本 Workshop metadata 字段差异。
- 修复聊天 delivery timeout 的取消和释放竞态。

### Security and Compatibility

- 游戏版本不同始终硬拦截，不能通过 MOD 同步或 relaxed 继续绕过；普通非联机 MOD 不提示、不禁用、不影响加入。
- 自动获取仅使用 Steam Workshop；客户端和服务端都不会从房主或任意 URL 下载、托管或传输 DLL、PCK、ZIP。
- 原生 Steam provider 独立提供全部核心能力，不依赖 AutoModSubscriber，也不注册或覆盖其外部 UI handler。
- 同一客户端包继续兼容游戏 `0.107.1`、`0.108.0` 与 `0.109.0`。

## [0.5.0] - 2026-07-17

### Added

- 大厅服务器频道：节点级 ticket、WebSocket 网关、有界历史快照、限流和慢客户端保护。
- 房间富聊天：Emoji、卡牌 / 遗物 / 药水引用，以及 power / player 战斗引用。
- 房间 generation 隔离与能力协商；旧客户端自动接收有界 legacy 文本。
- `/server-admin` 六项聊天治理开关及 `SERVER_ADMIN_STATE_FILE` 持久化。
- 客户端大厅频道浅色侧栏、Emoji 面板、富文本草稿编辑器与物品选择交互。

### Changed

- 客户端 MOD 与 lobby-service 同步升级到 `0.5.0`，完整聊天能力要求两端配套更新。
- 发布验证在临时目录确定性构建客户端和服务端包，并检查显式文件清单与法律文件。
- 玩家说明、客户端安装说明、systemd / Docker 部署文档统一到 v0.5.0。

### Fixed

- 兼容游戏 `0.107.1`、`0.108.0` 与 `0.109.0` 的连接初始化和宝箱跳过签名变化；同一个 v0.5.0 客户端包可在三个游戏版本上使用。
- 加入房间时不再由 `test_relaxed` 忽略游戏版本差异；房主与客户端版本不同会在握手阶段直接提示并中止，避免进入黑屏运行场景。MOD 与 ModelDb 差异仍沿用 relaxed 兼容策略。
- 修复 Android 富文本输入框在每次输入或删除后重建控件，导致系统键盘反复重启和闪烁的问题。
- 宝箱补丁按目标隔离安装；`0.109.0` 使用游戏原生 nullable 跳过投票，旧版继续使用 legacy `-1` 兼容动作。
- 修复大厅频道标题对比度、消息深色块、输入框越界和多余预算小字。
- 修复房间聊天输入框缩成窄条或在多行富草稿下越界的问题，并隐藏输入区预算小字。
- 房间内的大厅频道消息不再触发未读角标、淡出唤醒或自动切换；频道页仍可手动查看。
- 修复 Godot 解析八位 SVG 颜色时 Emoji/Lucide 图标完全透明的问题。
- 修复异步按钮回调返回 `Task` 导致 Godot `Task -> Variant` 日志错误。
- 修复房间重发 generation、过期战斗引用、超长 fallback 和聊天生命周期中的竞态边界。

### Compatibility and Operations

- 客户端构建不固化游戏 `0.108.0` 新增的 `INetClientGameService` 或 `0.109.0` nullable 宝箱投票签名，并保留 `0.107.1` 运行时兼容。
- 保留 v0.4.0 与 v0.2.2 legacy 房间/控制通道回归覆盖；旧客户端不获得 v0.5.0 富聊天能力。
- 服务器频道历史仅保存在当前节点进程内，重启清空；房间聊天不保留历史；节点间不复制聊天。
- `SERVER_CHAT_ENABLED` 默认仍为 `false`。服主可在 env 或 `/server-admin` 中启用，并按需分阶段关闭 combat、Emoji/item、rich、room-v2。

## [0.4.0] - 2026-05-13

- 引入无母面板的去中心化 peer 网络、Cloudflare discovery 聚合与内置 seed peers。
- 移除 lobby-service 对 `SERVER_REGISTRY_*` 的运行时依赖。
- 完善客户端服务器选择、键盘/手柄导航、邀请快捷键和无障碍软桥接。

[0.5.2]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.2
[0.5.2-rc.1]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.2-rc.1
[0.5.1]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.1
[0.5.0]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.0
[0.4.0]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.4.0
