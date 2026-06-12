# Draft: 联机大厅无障碍适配（兼容 say-the-spire2 盲人辅助模组）

## 背景（来自 issue）
- 盲人玩家使用 say-the-spire2 辅助模组（https://github.com/emptylower/say-the-spire2）
- 该模组导航方式疑似"模拟手柄"，在联机大厅模组中几乎无法使用
- 受影响流程：创建房间、加入房间、选择服务器

## Requirements (来自用户原话)
1. **最大期望：直接兼容** — 键盘可导航整个大厅 UI，且能被正常朗读
2. **快捷键** — 同意房间邀请、打开聊天栏
3. **邀请链接自动弹窗** — 复制邀请链接后进入联机大厅时直接弹出加入弹窗，无需先点击任意服务器
4. **替代创建房间方式** — 修改配置文件创建房间，或支持手柄导航的原生窗口

## Research Findings

### say-the-spire2 架构（librarian，基于 bradjrenshaw/say-the-spire2 @ f37f2e3）
- **朗读链路**: `UIManager.Update()` → `BuildFocusAnnouncement(element)` → `SpeechManager.Output()`；引擎链 Prism → Tolk → SAPI → 剪贴板（UI/UIManager.cs#L57-105, Speech/SpeechManager.cs#L16-22）
- **不走 Godot 原生无障碍树**：显式屏蔽 WM_GETOBJECT 禁用 AccessKit（Patches/DisableBuiltinAccessibility.cs）
- **导航 = Godot focus 系统 + ui_up/down/accept 动作分发**，不是自定义光标。键盘事件重映射，手柄输入轮询（Input/InputManager.cs#L300-389, L609-638）
- **朗读触发条件**：必须有人调用 `UIManager.SetFocusedControl(control, element)`。两条现有路径：
  1. Harmony patch 游戏自有控件类 `NClickableControl.RefreshFocus`（Patches/FocusHooks.cs#L203-225）
  2. 各 Screen wrapper 手动 `FocusEntered += ...`（UI/Screens/GameScreen.cs#L53-58）
- **关键 gap**：我们的大厅用的是原生 Godot Button/LineEdit，不是 NClickableControl，也没被任何 Screen wrapper 注册 → **即使获得焦点也不会被朗读**（静默）
- **有通用 proxy 回退**（ProxyFactory/ProxyButton/ProxyTextInput 可泛读 Label 文本），但前提是焦点事件能到达 UIManager
- **无正式第三方扩展 API**；`ScreenManager.ResolveElement` 只认已注册控件
- 语言 C#/.NET 9，入口 `[ModInitializer]` ModEntry，与本模组同构 → 反射软集成可行
- **最小契约**：FocusMode=All + FocusEntered 接入 UIManager + 有意义的 Label/节点名 + focus 顺序

### 大厅 UI 现状（explore）
- **整体鼠标优先**：LanConnectLobbyOverlay.cs 无任何 _Input/ui_accept/FocusNeighbor/FocusMode 设置
- **房间卡片是 PanelContainer + GuiInput 双击** → 完全不可聚焦，键盘无法选房（CreateRoomCard ~L2207）
- 标准控件（Button/LineEdit/OptionButton/SpinBox/CheckButton）默认可 Tab 聚焦，Enter 提交已存在于创建房间/密码/聊天
- **服务器选择器**（LanConnectServerSelectionDialog）：服务器行是 Button（可聚焦），支持 Escape 关闭；入口 patch Patches.MultiplayerSubmenu.OnLobbyPressed L169-196 **总是先弹服务器选择**
- **邀请检测已存在**：`CheckClipboardForInviteCode()` 用 DisplayServer.ClipboardGet + LanConnectInviteCode.TryDecode，overlay 显示时自动弹邀请确认框 → 需求③的真正问题是服务器选择器挡在 overlay 前面
- **聊天已存在**：LanConnectRoomChatOverlay，toggle Button + LineEdit + Enter 发送
- 对话框是自建 Control shell（CreateDialogShell），多数无 Escape 关闭，焦点管理不一致
- 配置 LanConnectConfig.cs JSON 持久化，已有多个布尔项，新增 accessibility 标志很自然
- 公告轮播（LobbyAnnouncementCarousel）显式 FocusMode=None

## 技术结论（待用户确认方向后写入决策）
①的实现需要两层：
- **A 层（被动，无依赖）**: 全 UI focus 化 — 房间卡片改可聚焦、FocusMode/焦点邻居/对话框焦点管理/Escape 统一。对所有用户都有益（手柄/键盘党）
- **B 层（软桥接）**: 启动时反射检测 say-the-spire2 程序集 → 存在则对我们所有控件 FocusEntered 时反射调用其 `UIManager.SetFocusedControl`，并提供中文朗读文本。无该模组时零开销
- 备选：给 say-the-spire2 提 PR 加通用 FocusOwner 监听（不可控，作为补充而非依赖）
③ = 把剪贴板邀请检测提前到大厅入口 patch，检测到有效邀请码则跳过服务器选择器直入确认弹窗

## 已确认决策（用户）
- **A + B 都做**：A 层全 UI focus 化 + B 层反射软桥接朗读（say-the-spire2 不存在时零开销）
- **快捷键用推荐默认**：接受邀请 = F7（弹窗内同时支持 Enter），聊天 toggle = F8 或 T；实现时验证与游戏键位无冲突，留配置化空间
- **朗读语言：中文**，与 UI 文案一致
- ④（配置文件创建房间/原生窗口）→ 因①可行，**排除出本计划**
- emptylower fork 与上游 0 差异，反射桥接以上游程序集结构为准

## 测试策略决策（用户已确认）
- **基建现状**：sts2-lan-connect/ 客户端无任何测试基建（lobby-service 有 node test，但本次不涉及）
- **用户要求**：尽可能引入测试框架，建立完整测试链路与测试计划
- **动机**：没有真实盲人测试者，需要靠测试尽快定位问题
- **分层方案**：
  1. **纯逻辑层（xUnit/NUnit, net9.0 类库）**：焦点顺序计算、朗读文本生成（中文文案拼装）、邀请码解析/跳过服务器选择的判定逻辑、快捷键路由判定 —— 把这些从 Overlay 抽成可测纯类
  2. **Godot 集成层（GdUnit4 headless 候选）**：focus 链遍历、对话框焦点落点、控件 FocusMode 检查 —— 待 librarian 验证 GdUnit4 对 Godot 4.5 + .NET 9 headless 的兼容性
  3. **桥接契约层（静态验证）**：克隆 say-the-spire2 源码，对反射目标（UIManager.SetFocusedControl 等签名）做静态比对测试，防上游 API 漂移
  4. **运行时诊断日志**：桥接探测/焦点导航日志，便于真实环境排障
- **Agent QA**：build 脚本编译 + headless 测试运行 + 日志验证，所有任务无人工干预验收

## Open Questions
- (无 — 全部澄清完毕)

## Scope Boundaries (用户已确认：方案 A)
- INCLUDE:
  - ① 键盘导航 + 可朗读（主攻方向，核心解法）
  - ② 快捷键：接受房间邀请 / 打开聊天栏（配套）
  - ③ 邀请链接复制后进大厅自动弹出加入弹窗（配套）
- CONDITIONAL:
  - ④ 配置文件创建房间 / 原生窗口 — 仅当①不可行或不充分时作为兜底，计划中视研究结果决定是否纳入
- EXCLUDE: (待研究结果后细化)

## Technical Decisions
- 优先级策略：① 为主，②③ 为配套独立功能，④ 兜底（用户选 A）
- (待定：focus 接入方式、快捷键键位、邀请链接检测机制)
