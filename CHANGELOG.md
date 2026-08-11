# Changelog

本项目从 v0.5.0 起在此记录客户端 MOD 与 lobby-service 的公开版本变更。

## [Unreleased]

## [0.5.6-rc2] - 2026-08-11

客户端 `0.5.6-rc2` 测试候选发布；lobby-service 继续使用 `0.5.6-rc1`，本次不改变服务端 API、中继或控制协议。

### Fixed

- （客户端）修复 RitsuLib sidecar 在 LAN 大厅阶段使用尚未绑定的 `RunManager.NetService` 发送握手回执，导致回执实际未发出、房主开局黑屏而客机停在等待页的问题。
- （客户端）在托管加入与大厅运行期间持续驱动 RitsuLib 握手协商，使双方在准备和场景切换前完成 capability 交换；失败的加入流程会释放跟踪的网络服务。

### Compatibility

- 兼容桥通过反射按需启用，不新增对 RitsuLib 的编译或打包依赖；未安装 RitsuLib 时保持原行为。
- 已核对 RitsuLib `0.5.8` 的实际故障版本及当前 `0.5.10` 的相关方法结构。所有同房玩家应统一使用客户端 `0.5.6-rc2`。

## [0.5.6-rc1] - 2026-08-08

客户端与 lobby-service `0.5.6-rc1` 测试候选同步发布准备；两端版本特意对齐，便于玩家和服主核对 WireCache 签名与 binding-aware kick 支持。本版本不是正式版。

### Added

- （客户端）新增 `WireCacheSignatureV1`，对四张 ModelId net-id 表与四个编码位宽生成指纹；签名、位宽、表条目数及每个 MOD 的 `affects_gameplay` 标记写入调试报告和 `godot.log`。
- （服务端）建房方与加入方都提供有效签名时，在签发 join ticket 前拒绝真实线上编码不一致；签名缺失、格式非法或诊断失败一律 fail-open。
- （客户端）游戏握手在 join request 发出前再次检查签名；真实不一致即使在 relaxed 配置下也不能跳过，缺失或不可读仍允许加入。
- （客户端）续局来源改为 `lan` / `lobby` / 未知三态；未知来源显示一次性选择并持久化，旧 schema 中被错误写成 LAN 的存档会重新询问。
- （客户端/服务端）大厅新增与存档槽位分离的安装级 credential，以及绑定到当前控制占用者的 opaque kick handle。

### Changed

- （客户端）发布默认兼容配置由 `test_relaxed` 改为 `strict`，恢复原版 gameplay MOD / ID 数据库不一致检查；relaxed 只作为显式测试选项保留。
- （客户端/服务端）客户端与 lobby-service 版本同步为 `0.5.6-rc1`。客户端的新签名门禁和 binding-aware kick 需要配套服务支持。
- （客户端）旧 lobby-service 无法证明支持 binding-aware kick 时，不再发送可能误封存档槽位的服务端踢出请求；本地移出后明确告知房主目标未被封禁。

### Fixed

- 修复 `affects_gameplay: false` MOD 不进入原版 `idDatabaseHash`、却改变 ModelId net-id 表和位宽时，双方通过握手后黑屏或卡在等待页的问题。线上编码不同现在会在加入前明确拒绝。
- 修复 safe-load 给缺失绑定隐式写入 `hostChannel=lan`，以及存档修复删除绑定，导致续局回主菜单后无法发布房间的问题。
- 修复房主重开后队友看不到房间；该问题与续局绑定被误写/删除共用同一根因。
- 修复槽位接管后踢出当前占用者会永久拉黑原槽位主人，以及踢出通知可能按槽位路由到错误连接的问题。
- 修复列表绘制后发生槽位接管时，旧的踢出动作会跟随槽位落到新占用者的问题；服务端现在拒绝 stale binding handle，且不会改变封禁或连接状态。

### Known Limitations

- 旧版房主客户端仍可能在踢出后的 1.5 秒内短暂断开替代占用者；房主控制 WebSocket 丢失后该房间会禁用踢出。
- 踢出会使另一位玩家针对同一槽位的在途 ticket 失效；新建存档开始游戏后的局中重连仍不可用。

## [0.5.5] - 2026-07-31

客户端 `0.5.5` 正式版完成玩家验证并发布；lobby-service 协议没有变化，继续使用 `0.5.4`。

### Client Compatibility

- （客户端）同一 DLL 现在可在旧版平铺握手结构与 `v0.110.x` 的 `PeerVersionInfo` 握手结构间运行时选择，普通加入、读档加入和运行中重连都会按当前游戏 ABI 填充请求。
- （客户端）扩容序列化补丁从实际 `playersInLobby` 元素类型解析旧 `LobbyPlayer` 或新 `StartRunLobbyPlayer`；六个线协议补丁必须完整生效，否则整组回滚并终止 MOD 初始化。
- 游戏版本仍要求房主与加入方完全一致；多版本兼容表示同一 MOD 包可分别运行在各游戏版本上，不允许不同游戏协议版本互联。

## [0.5.4] - 2026-07-28

客户端与 lobby-service `0.5.4` 正式版同步发布，新增 AI 语义审核及对应客户端交互。

### Added

- （服务端）新增三协议 AI 语义审核：OpenAI Responses、OpenAI Chat Completions 与 Anthropic Messages；支持严格 JSON Schema 和显式启用的提示词 JSON 回退。
- （服务端）AI 放行后立即写入 30 天精确消息缓存和 7 天限定语境缓存；永久白名单必须人工批准，人工拒绝会生成立即生效的永久黑名单。
- （服务端）房间名、玩家名、续局角色名与聊天消息统一进入语义审核；同一认证用户在同一频道 30 秒内拆分发送的最多 10 条短消息会组合分析，命中后撤回相关上下文。
- （服务端）管理面板新增 AI 配置、健康状态、人工复审、永久白名单和独立的永久黑名单管理入口。
- （客户端）新增“在审核中”投递状态、违禁词游戏原生弹窗，以及公共频道/房间频道跨消息撤回帧处理。

### Changed

- AI 不可用时，普通单条聊天继续安全降级为确定性打码；主动拆字规避路径保守拒绝，避免利用模型故障绕过过滤。
- 永久黑名单优先于永久白名单和安全缓存；撤销规则后立即恢复缓存/AI 审核决策链。
- 客户端与 lobby-service 构建版本同步为 `0.5.4`，不改变旧版 `room_chat` 的兼容行为。

### Security and Compatibility

- API Key 使用 `AI_MODERATION_CREDENTIAL_KEY` 提供的主密钥进行 AES-256-GCM 加密；管理 API 不返回明文 Key，日志不记录认证头或完整聊天原文。
- 新增待审与撤回帧均为向后兼容扩展；旧客户端可忽略未知帧并继续接收最终 ACK/错误，但无法撤回已经显示的跨消息上下文，建议与 `0.5.4` 服务端配套升级。

## [0.5.3] - 2026-07-27

服务端与客户端同步正式版，标签为 [`v0.5.3`](https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.3)：lobby-service 发布说明见 [`docs/RELEASE_NOTES_V0.5.3_ZH.md`](./docs/RELEASE_NOTES_V0.5.3_ZH.md)，客户端 `0.5.3` 发布说明见 [`docs/RELEASE_NOTES_V0.5.3_CLIENT_ZH.md`](./docs/RELEASE_NOTES_V0.5.3_CLIENT_ZH.md)。聊天与加入线协议不变，客户端与服务端各历史版本可交叉互通。

### Added

- （服务端）大厅敏感词过滤：词库快照自 [konsheng/Sensitive-lexicon](https://github.com/konsheng/Sensitive-lexicon)（MIT，49,172 词随包分发）。聊天消息命中时等量 `*` 打码（大厅与房间，广播/历史统一打码版）；房间名、玩家昵称、续局槽位名、MOD 预检名等名称类字段命中时拒绝（`400` +「包含敏感词内容，请修改后重试」）。匹配带归一化（全半角/大小写/符号剔除/重复压缩）与间隙感知 ASCII 词边界，防插空格/符号绕过且不误伤 `have`/`standard` 等正常英文。
- （服务端）管理面板新增「敏感词过滤」开关（默认开，热切换并持久化）与状态展示（词数/打码数/拒绝数）；词库加载失败 fail-open 不阻断启动。
- （服务端）新环境变量 `SENSITIVE_FILTER_ENABLED`、`SENSITIVE_LEXICON_DIR`（仅首次启动种子值）。
- （客户端）LAN 与大厅续局通道拆分（issue #40）：多人存档本地绑定新增持久化 `HostChannel`（`lan` / `lobby`）。纯 LAN 创建的存档续局时不再自动发布到公共大厅（日志 `decision=skip_lan_origin`），大厅创建的存档保持自动恢复房间行为；缺失/空值/未知通道一律按 `lobby` 处理，旧存档行为不变。
- （客户端）LAN 续局身份码：房主在续局等待页点击「续局身份码」，把与角色/玩家名一一对应的单条 `STS2LANRESUME:` 码发给队友，队友在手动 LAN/IP 加入页粘贴即可回到自己的槽位；一次粘贴多条会被拒绝。新游戏与普通直连使用安装级 LAN 身份，超时以同一身份重试一次。
- （客户端）「永久放弃多人存档」前自动把 `current_run_mp.save` 备份到 `user://sts2_lan_connect/save-backups/`，备份失败则拒绝删除，并同步清理对应房间绑定。
- （客户端）局内聊天 HUD 化改造：扁平半透明外壳与细控制条、单行富文本消息、按参与者稳定配色、44px 触达目标、指针模式自适应（触屏保留气泡入口与发送按钮）、收到新消息自动浮现。

### Changed

- （客户端）续局通道判断前移到大厅端点预检之前，LAN 存档续局不再发起 `POST /rooms`、不建中继/控制通道；诊断日志新增 `bindingHostChannel` / `effectiveHostChannel` / 续局 `decision`。
- （客户端）进入大厅时剪贴板含 `STS2LANRESUME:` 码会显示使用指引而非静默忽略。

### Fixed

- （服务端）Dockerfile 补充打包 `lexicon/`（此前 Docker 镜像缺词库会 fail-open 不过滤）。
- （客户端）修复安装 BaseLib（及依赖它的角色/观战 MOD）后多人存档必现损坏的问题：MOD 加载顺序把本 MOD 排在 BaseLib 之前时，原有的 BaseLib 存档守卫从未生效，BaseLib 用 Steam 身份校验 LAN 存档失败后会把 `current_run_mp.save` 改名为 `.corrupt`。守卫现在会在 BaseLib 程序集加载时自动补挂（issue #40）。
- （客户端）新增游戏侧兜底防护：`RunSaveManager.RenameBrokenMultiplayerRunSave` 被拦截，只要存档能用任一 LAN 身份正常 canonicalize 就拒绝改名毁档，防止任何探测路径误毁有效的多人存档。
- （客户端）修复安卓（无 Steam 平台）打开“加入好友”页面时，游戏本体自动向 `127.0.0.1:33771` 发起调试直连、必须等满 ENet 超时报错才能操作的问题；现在仅在显式传入 `fastmp` 命令行参数时才保留该开发者行为（issue #40）。
- （客户端）修复 LAN safe load 误把大厅存档绑定迁移为 `lan` 通道，导致大厅续局不再发布房间、一键重开失效的问题。
- （客户端）修复局内聊天打开/切频道/收消息时视图停在最早消息、不贴底的问题，「N 条新消息」按钮恢复工作；恢复受邀频道聊天与表情选择器；回车发送优先于竞争输入钩子；发送失败态触屏可见可点。
- （客户端）存档完整性与加入页守卫补丁不再因检测到 RMP MOD 而被跳过。
- （客户端）修复存档诊断日志里 `mpSaveUpdatedAt` 因 `user://` 虚拟路径始终显示 `<missing>` 的问题，并让诊断不再触碰被 BaseLib 补丁过的 `HasMultiplayerRunSave` getter。

### Security and Compatibility

- 客户端 `0.5.3` 与服务端 `0.5.1` / `0.5.2` / `0.5.3` 均兼容；`HostChannel` 仅存于客户端本地配置，不改变服务端 API、中继/控制/心跳协议；聊天与加入线格式不变，与 v0.5.1+ 客户端互通不受影响。
- 客户端以游戏 `0.107.1`、`0.109.0`、`0.109.1` 为加载兼容目标；`0.108.0` 不在适配范围内。

## [0.5.2] - 2026-07-22

正式客户端标签为 [`v0.5.2`](https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.2)，并在 GitHub Release 完成后同步更新 Steam Workshop。lobby-service 继续使用 `0.5.1`。

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

[0.5.3]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.3
[0.5.2]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.2
[0.5.2-rc.1]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.2-rc.1
[0.5.1]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.1
[0.5.0]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.5.0
[0.4.0]: https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.4.0
