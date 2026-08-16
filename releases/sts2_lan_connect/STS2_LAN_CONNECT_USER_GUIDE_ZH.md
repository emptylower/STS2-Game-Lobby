<div align="center">

**[中文](#中文) · [English](#english)**

</div>

---

<a name="中文"></a>

# STS2 LAN Connect 使用说明

当前客户端测试版为 `0.6.0-alpha.3`，lobby-service 继续使用 `0.6.0-alpha.1`。这不是正式版；同房玩家必须统一客户端与游戏版本，安装或更新后必须完整重启游戏。

## v0.6.0-alpha.3 双协议房间

- RitsuLib `tail_v1` 联机已修复真实玩家 ID 和首包/控制绑定竞态；若仍停在等待页，请提交双方完整日志。
- SL/读档续局会复用存档冻结的协议身份，不再因 `capability_digest_mismatch` 删除刚发布的房间。
- 建房默认选择兼容模式：固定 `4/5-bit`、2-8 人、不允许 RitsuLib。
- 0.6 新协议保持原版 `2/3-bit` 主体，以 LAN protocol v1 携带完整 roster；无 RitsuLib 使用 standalone carrier，全员 RitsuLib 使用公开 typed-sidecar carrier。
- 有 RitsuLib 只能连接有 RitsuLib，无 RitsuLib 只能连接无 RitsuLib；混合组合会在 ticket 与 transport 前拒绝。
- 本版不卸载、不直接调用也不恢复 RitsuLib 私有 Harmony postfix，不维护 RitsuLib 分支。
- 官方 RitsuLib v0.5.12 在当前 Android v0.111.0 环境初始化自身网络补丁时会黑屏；Android 玩家应保持 RitsuLib 禁用。本测试版已验证无 Ritsu 的 Android/macOS 真实开局，但不宣称全 Ritsu Android 联机可用。
- direct-IP 在 v0.6 测试系列中只支持兼容模式，本地 Ritsu 或 Tail intent 会在建立连接前拒绝。
- 历史客户端真实互通不在本 alpha 的测试门禁中。
- 加入前会比较双方实际使用的 ModelId 线上编码。内容 MOD 组合不同并改变 net-id 表或位宽时，现在会明确提示不一致并拒绝加入，而不是进入后黑屏或一方卡在等待页；这是预期行为。
- `affects_gameplay: false` 只表示该 MOD 不进入原版 `idDatabaseHash`，不保证它不会占用 ModelId。新签名会补上这层检查；签名缺失或读取失败时仍允许加入。
- 默认兼容配置已从测试用 relaxed 恢复为 strict。普通 MOD 差异的显式 relaxed 入口不能跳过真实线上编码或游戏版本不一致。
- 继续大厅存档或由房主重开时，房间绑定不会再被 safe-load/修复误写或删除。来源不明确的旧存档会询问一次“LAN 还是游戏大厅”，保存后不再重复询问。
- 槽位接管后的踢出会针对当前占用者，不再永久封禁槽位原主人。列表过期时服务端拒绝操作；旧服务端不支持安全结果时只本地移出，并提示该玩家仍可回来。
- 遇到加入失败、黑屏、等待页卡住或 MOD 误判，请同时提交两台机器各自的完整 `godot.log`。本版日志包含 WireCache 签名、四个位宽、表条目数和每个 MOD 的 `affects_gameplay` 标记。

## v0.5.5 游戏版本兼容

- 同一客户端包面向游戏 `0.107.1`、`0.109.0`、`0.109.1` 与 `0.110.x`，不需要按游戏版本下载不同 DLL。
- 同一房间内仍必须使用完全相同的游戏版本；这里的兼容表示客户端可分别加载在多个游戏版本上，不表示不同游戏版本可以互联。
- 兼容范围覆盖普通大厅加入、读档大厅加入、运行中重连，以及 Android `0.110.0` 的启动与加入流程。
- lobby-service 继续使用 `0.5.4`；同一房间的玩家应统一安装客户端 `0.5.5`。

## v0.5.4 AI 语义审核交互

- 消息进入服务端语义审核时，输入框上方显示“在审核中”，消息不会提前进入公共聊天栏。
- 审核通过后消息静默出现；审核拒绝时显示游戏原生“包含违禁词”提示。
- 服务端识别同一用户拆分发送的违规短句后，会撤回相关公共频道或房间消息。
- 房间名、用户名或续局角色名未通过审核时，创建/加入流程显示游戏原生敏感词提示，不展示网络错误细节。
- 新审核帧向后兼容，但旧客户端无法删除已经显示的拆字上下文，建议客户端与 lobby-service 同时升级到 `0.5.4`。

## v0.5.3 续局通道拆分与聊天 HUD

- 多人存档现在记住创建入口：纯 LAN 创建的存档续局时不再被自动发布到公共大厅；大厅创建的存档续局行为不变，仍自动恢复房间。
- 纯 LAN 存档续局时，房主在续局等待页点击「续局身份码」，把与角色/玩家名一一对应的单条 `STS2LANRESUME:` 码发给队友；队友在手动 LAN/IP 加入页粘贴该码即可回到自己的槽位。身份码不要互换，一次粘贴多条会被拒绝。
- 「永久放弃多人存档」前会自动把存档备份到 `user://sts2_lan_connect/save-backups/`，备份失败则拒绝删除；并修复了与 BaseLib 共存时多人存档被误改名 `.corrupt` 的问题。
- 修复安卓打开「加入好友」页自动发起调试直连、必须等满超时才能操作的问题。
- 局内聊天改为扁平半透明 HUD：单行富文本消息、按玩家稳定配色、收到新消息自动浮现、打开时稳定停在最新消息。
- v0.5.3 只更新客户端，与 lobby-service 0.5.1/0.5.2/0.5.3 及 v0.5.1+ 客户端均可互通；游戏加载目标为 `0.107.1`、`0.109.0` 与 `0.109.1`。

## v0.5.2 一次性引用与原生预览

- Android 点击聊天输入区旁的“引用”按钮，桌面按 `Alt+R`，可进入或取消一次性引用模式；成功引用一个支持对象后自动退出，并把焦点交回真实文本输入位置。
- 原有桌面 `Alt+左键` 直接引用继续保留；点击不支持区域不会吞掉正常游戏操作，成功捕获会消费点击以避免同时出牌或触发物品。
- 文字、Emoji 和引用使用单一行内富文本控件自然换行；卡牌、遗物、药水和 Power 使用游戏原生预览，动态 Power 说明按实际状态生成。
- Android 点击消息引用打开固定预览，点击外部、`Esc` 或关闭按钮退出；不再依赖桌面悬停。
- v0.5.2 只更新客户端，继续兼容 lobby-service 0.5.1 和 v0.5.1 聊天协议；游戏加载目标为 `0.107.1` 与 `0.109.0`。

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
2. 填写房间名，选择类型和联机协议，可选填密码；最大人数支持 2-8 人，默认 8 人
3. 默认 `兼容旧版客户端（默认）` 支持 LAN Connect `0.3-0.5` 加入且禁止 RitsuLib；`0.6 新协议（RitsuLib 状态必须一致）` 仅支持 `0.6+`
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
- **一次性入口**：Android 点击输入区旁的“引用”按钮；桌面按 `Alt+R`。成功点击一个卡牌、遗物、药水、状态或玩家后自动退出，并回到文字输入位置继续输入。
- **桌面直达**：原有 `Alt+左键` 继续保留。卡牌、遗物、药水可发到服务器或房间频道；战斗状态和玩家只允许房间频道；怪物目标引用仍未开放。
- **取消与失败**：再次点击引用按钮、`Esc`、切频道、关闭聊天或离开房间都会取消。点击不支持区域时保持 armed，且不吞掉原本的游戏操作。
- **编辑与查看**：引用和普通文字自然混排，可用方向键、选择、粘贴、`Backspace` 和 `Delete` 编辑。桌面可悬停或点击查看原生预览；Android 点击打开固定预览，再点外部、`Esc` 或关闭按钮退出。

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
- 纯 LAN 存档不会自动发布到公共大厅；房主在多人菜单载入该存档后，可在续局等待页点击 `续局身份码`
- 房主应按角色/玩家名把对应的单条 `STS2LANRESUME:` 身份码发给每位队友。队友在手动 LAN/IP 加入页填写地址，并把该码粘贴到 `旧存档续局身份码` 输入框；不要互换身份码
- 新游戏或同一台设备后续普通直连无需填写续局码：客户端会复用安装级 LAN 身份，网络超时只会用同一个身份再试一次
- 房主点击 `重开一局` 后，会短暂断开并自动回到多人续局载入页面
- 队友会自动回主菜单并按自己的 `desiredSavePlayerNetId` 轮询重连同一续局房间
- 若自动重连超时，可在 `游戏大厅` 手动加入作为兜底
- 如续局仍有空闲角色槽位，加入方会先看到角色选择，再进入联机
- 安装 BaseLib 及依赖它的角色/观战 MOD 时，多人存档不会再被误判损坏改名为 `.corrupt`；若日志出现 `save_manager: blocked corrupt-save rename` 说明防护已生效
- 安卓打开手动 LAN/IP 加入页不再自动发起本机调试直连，无需等待超时报错即可直接填写地址加入
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
  - `compat_4_5_v1` 固定使用历史 `4/5-bit`，支持 2-8 人，禁止 RitsuLib
  - `tail_v1` 固定使用原版 `2/3-bit` 主体和 LAN protocol v1；无 RitsuLib 使用 standalone carrier，有 RitsuLib 必须全员一致并等待公开 sidecar gate
  - 客户端实际日志 / 调试报告会同时记录 `compatibilityProfile`、`connectionStrategy`、`effectiveMaxPlayers`、`publishedProtocolProfile`、`carrier` 和 `capabilityDigest`
- MOD 内置 2-8 人支持；房间人数不再决定协议，建房时选择的协议在房间生命周期内冻结
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
- `0.2.x` 客户端不再支持；`0.3-0.5` 只能加入兼容房，`tail_v1` 房间要求 `0.6+`

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
- 旧存档续局必须使用房主提供、与你的角色/玩家名对应的单条续局身份码；粘贴多条时客户端会拒绝连接，避免误占其他槽位
- `永久放弃多人存档` 会先把原始 `current_run_mp.save` 备份到 `user://sts2_lan_connect/save-backups/`；读取或备份失败时不会删除存档

---

<a name="english"></a>

# STS2 LAN Connect User Guide

The current client candidate is `0.6.0-alpha.3`; lobby-service remains on `0.6.0-alpha.1`. This is not a final release. Every player must use the same client and game version and fully restart after updating.

## v0.6.0-alpha.3 Dual-Protocol Rooms

- The RitsuLib `tail_v1` flow now uses the real player ID and tolerates the first game packet arriving before its control binding.
- Continue-run publication reuses the protocol identity frozen in the save and no longer deletes the new room with `capability_digest_mismatch`.
- Compat is the default: fixed `4/5-bit`, 2-8 players, and no RitsuLib.
- Tail v1 preserves the vanilla `2/3-bit` body and carries the full roster in LAN protocol v1. No-Ritsu rooms use standalone Tail; all-Ritsu rooms use the public typed-sidecar API.
- Ritsu-present peers can connect only to Ritsu-present peers; Ritsu-absent peers can connect only to Ritsu-absent peers. Mixed presence is rejected before ticket and transport allocation.
- The RC4 private-postfix bridge is removed. LAN Connect does not detach, invoke, or restore private RitsuLib Harmony patches.
- Direct IP is compat-only throughout the v0.6 prerelease series. Historical-client interoperability is outside this release gate.
- The join flow now compares the actual ModelId wire encoding. Content-MOD sets that change net-id tables or bit widths are intentionally rejected before a black screen or stuck waiting room.
- `affects_gameplay: false` only excludes a MOD from vanilla `idDatabaseHash`; it does not guarantee that the MOD takes no ModelIds. The new signature covers that gap and remains fail-open when unavailable.
- The shipped profile is `strict` again. Explicit relaxed handling for ordinary MOD differences cannot bypass a genuine wire-signature or game-version mismatch.
- Safe load and repair preserve continue-run bindings. Ambiguous legacy saves ask once whether they came from LAN or the game lobby.
- A kick after slot takeover targets the current occupant instead of banning the original owner. Stale list actions are rejected; old services only permit a guarded local removal and report that no ban occurred.
- For join failures, black screens, stuck waiting rooms, or MOD false positives, provide the complete `godot.log` from both machines. This build prints the WireCache signature, four bit widths, table counts, and every MOD's `affects_gameplay` flag.

## v0.5.5 Game Compatibility

- One client package targets game versions `0.107.1`, `0.109.0`, `0.109.1`, and `0.110.x`; separate DLLs are not required.
- Every player in a room must still use the exact same game version. Compatibility means the client can load on each supported version separately, not that different game versions can play together.
- Compatibility covers normal lobby joins, loaded-run joins, in-run reconnects, and Android `0.110.0` startup/join flows.
- Keep lobby-service on `0.5.4`, and use client `0.5.5` for every participant in the same room.

## v0.5.4 Semantic Moderation UX

- A message under server-side semantic review shows a compact Reviewing indicator and is not displayed in public chat early.
- Approved messages appear silently; rejected content uses a native game sensitive-content popup.
- When the service detects a prohibited phrase split across short messages, related public or room context is removed through redaction frames.
- Rejected room names, player names, and continue-run character names use native feedback instead of exposing transport errors.
- New moderation frames are backward-compatible, but old clients cannot remove already-visible split-message context; pairing the `0.5.4` client and lobby service is recommended.

## v0.5.3 Continue-Channel Split and Chat HUD

- Multiplayer saves now remember their origin: saves created over plain LAN are no longer auto-published to the public lobby on resume; lobby-origin saves keep the existing auto-restore behavior.
- When resuming a LAN-origin save, the host taps "Resume Code" on the waiting page and shares the per-character single `STS2LANRESUME:` code with each teammate; teammates paste it on the manual LAN/IP join page to reclaim their own slots. Do not swap codes, and pasting multiple codes at once is rejected.
- Abandoning a multiplayer save now backs it up to `user://sts2_lan_connect/save-backups/` first, and the deletion is refused if the backup fails. Also fixed LAN saves being renamed `.corrupt` when BaseLib is installed.
- Fixed Android's Join Friend screen auto-dialing a debug connection and blocking until the timeout.
- In-run chat is now a flat translucent HUD: single-line rich-text messages, stable per-player name colors, auto-appear on new messages, and the view stays pinned to the latest message.
- v0.5.3 is client-only and interoperates with lobby-service 0.5.1/0.5.2/0.5.3 and v0.5.1+ clients. Supported game loading targets are `0.107.1`, `0.109.0`, and `0.109.1`.

## v0.5.2 One-Shot References and Native Previews

- Tap the Reference button beside the composer on Android, or press `Alt+R` on desktop, to arm or cancel one-shot reference mode. One successful capture exits the mode and restores focus to the real text input.
- The original desktop `Alt+left-click` path remains available. Unsupported clicks preserve the normal game action, while successful captures consume the click to prevent playing a card or triggering an item at the same time.
- Text, Emoji, and references flow through one inline rich-text control. Cards, relics, potions, and Powers use native game previews with dynamic Power context.
- Android opens a pinned preview by tapping a message reference and closes it by tapping outside, pressing `Esc`, or using the close button.
- v0.5.2 is client-only and remains compatible with lobby-service 0.5.1 and the v0.5.1 chat protocol. Supported game loading targets are `0.107.1` and `0.109.0`.

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
2. Enter a room name, choose a room type and protocol, and optionally set a password; max players supports 2-8 and defaults to 8
3. The default compat mode supports LAN Connect `0.3-0.5` and forbids RitsuLib; Tail v1 requires `0.6+` and matching RitsuLib presence
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
- **One-shot entry**: tap the Reference button on Android or press `Alt+R` on desktop. Capturing one card, relic, potion, power, or player exits the mode and restores the text caret.
- **Desktop direct path**: the existing `Alt+left-click` shortcut remains available. Items work in server or room chat; combat powers and players are room-chat only. Monster targets remain disabled.
- **Cancel and failure**: the button, `Esc`, channel changes, closing chat, or leaving the room cancels the mode. Unsupported clicks keep it armed and do not consume the normal game action.
- **Editing and viewing**: references flow inline with text and support arrows, selection, paste, `Backspace`, and `Delete`. Desktop supports hover and pinned click previews; Android taps open a pinned preview that closes via outside tap, `Esc`, or the close button.

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
- Pure-LAN saves are not published to the public lobby. After the host loads that save from the multiplayer menu, the waiting screen provides `Resume Identity Codes`
- The host should send each teammate exactly one `STS2LANRESUME:` line matching that player's character/name. The teammate enters the host address on manual LAN/IP Join and pastes that line into the old-save resume-code field
- New runs and later ordinary joins on the same installation do not need a resume code: the client reuses one installation-level LAN identity and retries a transport timeout once with that same identity
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
  - `compat_4_5_v1` always uses fixed historical `4/5-bit`, supports 2-8 players, and forbids RitsuLib
  - `tail_v1` keeps the vanilla `2/3-bit` body and LAN protocol v1; no-Ritsu rooms use standalone carrier, while Ritsu rooms require homogeneous presence and the public sidecar gate
  - Client runtime logs and debug reports record `compatibilityProfile`, `connectionStrategy`, `effectiveMaxPlayers`, `publishedProtocolProfile`, `carrier`, and `capabilityDigest`
- MOD supports 2-8 players natively; player count no longer selects the wire protocol, and the selected protocol is frozen for the room lifetime
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
- `0.2.x` clients are no longer supported; `0.3-0.5` clients can only join compat rooms, while `tail_v1` requires `0.6+`

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
- Old-save resumes require exactly one code matching your character/player name; the client rejects multiple pasted slot codes instead of guessing
- `Permanently Abandon Multiplayer Save` first backs up the original `current_run_mp.save` under `user://sts2_lan_connect/save-backups/`; a read or backup failure blocks deletion
