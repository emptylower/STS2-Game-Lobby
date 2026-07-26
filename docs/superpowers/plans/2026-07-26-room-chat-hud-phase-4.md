# 局内房间聊天 HUD 化 — 第 4 期实施计划：面板外壳与控件条

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把浮层从「金框实心面板 + 标题栏 + 双 tab」改成「无框半透明底板 + 一行极简控件条 + 独立输入浮条」，并让第 2 期建立的可读性契约首次真正上身。

**Architecture:** 先用离线截图靶场把描边宽度与静息底板透明度定稿（此前两值一直是暂定），再依次重构淡出目标、控件条、面板外壳。淡出目标的重构必须先做——控件条要与面板同生共死地淡出，而现有淡出只作用于 `_panelFrame`。

**Tech Stack:** Godot 4.5 + .NET 9；xUnit（纯逻辑）+ GdUnit4（场景行为与截图）。

**Spec:** `docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md` §5.1、§5.3、§6、§9

**Branch:** `feat/room-chat-hud-redesign`

---

## 测试基线（2026-07-26 实测）

| 套件 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| GdUnit | 284 | 0 | 0 |
| xUnit | 724 | 1 | **1** |

那个 xUnit 失败是 `LanConnectModInventoryBuilderTests.Runtime_metadata_matches_the_supported_version_inventory_contract`，来自 `main`、与本计划无关。`ReconnectUsesJitteredBackoffAndFreshProbeTicketTransport` 是已知计时抖动。

---

## 两处对 spec 的更正，先说清楚

### 1. 发送按钮本期不移除

spec §5.1 写了「移除发送按钮（`Enter` 即发送）」。**本期不执行这一条**，理由：

- `_sendButton` 在共用的 `BuildControls` 中构建，HUD 与大厅侧边栏共用，有 7 个测试文件依赖 `LanConnectConstants.ChatSendButtonName`。
- 更要紧的是**安卓没有物理键盘**。移除后触屏玩家将失去可点的发送入口——这与第 5 期要解决的「收起后无入口」是同一类缺陷。

因此发送按钮的去留**移到第 5 期**，由指针模式决定：鼠标模式隐藏，触摸模式保留。与已定的气泡策略一致。本期只把它的高度补到 44px 下限。

### 2. 描边与底板取值在本期定稿

spec §9 的验证项 1、2 原挂在第 2 期，但第 2 期没有把描边和底板施加到任何在渲染的控件上，实机无物可看。本期是它们第一次上身，因此定稿放在本期第一个任务，用离线截图靶场完成，不依赖真实游戏。

---

## 文件结构

| 文件 | 责任 | 动作 |
|---|---|---|
| `sts2-lan-connect.GdUnitTests/Chat/LanConnectHudLegibilityScreenshotTests.cs` | **新建**。把描边与底板渲染到 `SubViewport` 并导出 PNG，供人眼定稿 | 创建 |
| `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectHudLegibility.cs` | 可读性契约 | 修改：依据截图结论定稿取值 |
| `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs` | 浮层骨架 | 修改：淡出目标、控件条、面板外壳 |
| `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs` | 面板 | 修改：输入条独立化（仅 HUD 样式）、补齐触摸下限 |
| `sts2-lan-connect.GdUnitTests/Chat/LanConnectRoomChatShellTests.cs` | **新建**。外壳与控件条验收 | 创建 |

---

## Task 1：截图靶场与取值定稿

**Files:**
- Create: `sts2-lan-connect.GdUnitTests/Chat/LanConnectHudLegibilityScreenshotTests.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectHudLegibility.cs`（可能）

`OutlineSize = 3` 与 `RestPlateColor` 的 `alpha 0.35` 自建立起就是暂定值，spec §9 要求实机确认。真实游戏无法自动化，但**这两个问题里最关键的一个——中文 13px 配 3px 描边会不会糊笔画——完全不需要游戏**，用 Godot 自己的 `SubViewport` 渲染即可回答。

- [ ] **Step 1: 写截图靶场**

创建 `LanConnectHudLegibilityScreenshotTests.cs`。参考仓库既有的截图能力（`LanConnectRoomRichScreenshotTests.cs`，以及 `docs/testing/STS2_LAN_CONNECT_V0.5.2_PHASE0_BASELINE_ZH.md` 记载的 `SubViewport` 真实渲染做法）。

渲染一张对照图，包含：

- 三种背景：深色 `#1e1b26`、中间调 `#8a7f63`、亮色 `#d9cba8`（模拟洞穴、火把区、米黄地图）。
- 每种背景上各放两组文本：**中文**（例如「房间 频道 引用 固定 收起」）与**英文**（`Room Channel Ref Pin Close`）。
- 每组各渲染三种描边宽度：`0`（对照）、`2`、`3`。
- 字号取 13px（控件条字号）。
- 另渲染一组「静息底板 + 文字」的样本，底板 alpha 取 `0.35` 与 `0.45` 两档，压在亮色背景上。

导出 PNG 到系统临时目录（**不入库**，沿用 Phase 0 的既有约定），并在测试输出里打印完整路径。

断言只需保证图片非空且尺寸正确——这个测试的产物是**图片本身**，不是断言。

- [ ] **Step 2: 运行并报出路径**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~LanConnectHudLegibilityScreenshotTests"
```

**把 PNG 的绝对路径报出来。** 由控制方（我）看图定稿，你不要凭空调数值。

- [ ] **Step 3: 依结论定稿**

拿到结论后：

- 若 3px 糊中文笔画 → `OutlineSize` 降至 2，并同步更新 `LanConnectHudLegibilityTests.Outline_is_dark_and_thick_enough_to_survive_a_bright_background` 的下限断言。
- 若 0.35 底板在亮背景下压不住 → `RestPlateColor` 的 alpha 抬到 0.45，并同步更新 `Rest_plate_is_translucent_enough_to_read_as_chrome_free` 的 `InRange` 区间。
- 若两者都合格 → 不改代码，只在 spec §9 记录结论。

- [ ] **Step 4: 回写 spec §9**

把验证项 1、2 从「待验证」改为实测结论与最终取值，附 PNG 的 SHA-256（图片本身不入库）。

- [ ] **Step 5: 提交**

---

## Task 2：淡出目标重构

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs`

控件条要与面板同生共死地淡出，但它在结构上位于 `_panelFrame` 之外，而现有淡出只对 `_panelFrame` 施加 alpha（`:1014`）。

**绝不能改成对 `_root` 施加 alpha**——那会连带把聊天气泡淡掉，破坏第 5 期要建立的「触摸模式下气泡常驻可点」约束。spec §5.3 明确禁止这条路径。

正确做法：新增一个包裹「面板框 + 控件条」而**不含**气泡与 `_fadeHint` 的中间容器，作为淡出对象。

- [ ] **Step 1: 写守卫测试**

在 `LanConnectRoomChatTabsTests.cs` 或新建文件中加：淡出完成后，`_panelFrame` 的有效 alpha 为 0，而 `_toggleButton`（气泡）的 alpha **不变**。

这条守卫在控件条尚未建立时就应成立（现状即如此），本任务的意义是**把这个隐性属性变成显式约束并锁住**，防止后续重构把淡出上提到 `_root`。

- [ ] **Step 2: 确认现状下通过，然后重构，再确认仍通过**

重构后 `TestState.PanelAlpha` 的取值来源可能要改（现在读 `_panelFrame.Modulate.A`）。若改动了它，把既有淡出测试逐条过一遍并说明。

- [ ] **Step 3: 全量验证 + 提交**

既有的 `LanConnectRoomOverlayFadeTests` 与 `LanConnectRoomChatFocusTests` 必须全绿。

---

## Task 3：控件条

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs`
- Create: `sts2-lan-connect.GdUnitTests/Chat/LanConnectRoomChatShellTests.cs`

用一行极简控件条取代标题栏（`:482-502`）与双 tab 栏（`:504-514`）：

`房间` `频道`（含 6px 未读红点） … `引用(⌗)` `固定(⇱)` `收起(✕)`

- 频道切换以高亮/暗淡区分当前流，取代原双 tab + 两个数字徽标。
- 每个控件调用 `LanConnectHudLegibility.ApplyHudButtonStyle(button, AccentColor)`——这是契约第一次真正上身。
- 控件条字号 13px。
- **所有控件常驻**（不因输入方式变化），参与 Task 2 建立的淡出容器。
- 未读徽标从两个数字改为一个 6px 圆点；圆点本身不是可点控件，无需满足触摸下限。

- [ ] **Step 1: 写失败测试**

新建 `LanConnectRoomChatShellTests.cs`，覆盖：

1. 标题栏的「聊天」`Label` 与「固定」「收起」旧按钮布局已不存在于原位置；控件条内存在对应功能的控件。
2. 控件条上每个按钮都有 `"focus"` 样式盒、`normal` 底板 alpha 等于 `LanConnectHudLegibility.RestPlateColor.A`、且短边 ≥ 44。
3. 频道切换仍可用：点「频道」后 `TestState.SelectedChannel` 变为 `Server`。
4. 未读时红点可见，无未读时不可见。
5. **`ChatPinButton` 等既有节点名保持不变**——`LanConnectRoomChatTabsTests` 与焦点链依赖它们。若必须改名，逐一列出并同步更新依赖方。

- [ ] **Step 2–4: 红 → 实现 → 绿**

- [ ] **Step 5: 全量验证**

重点确认 `LanConnectChatResolutionTests`（控件条比原「标题栏 + tab 栏」矮，理论上是**减**高度，但要确认）、`LanConnectRoomChatFocusTests`（焦点链变了）、`LanConnectRoomChatTabsTests`（tab 变成了控件条上的两个按钮，该文件多半要大改——逐条说明每处改动的依据）。

- [ ] **Step 6: 提交**

---

## Task 4：面板外壳与独立输入条

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs`

| 项 | 现状 | 目标 |
|---|---|---|
| 底色 | `(0.09,0.09,0.11,0.95)` | `(0.05,0.05,0.1,0.75)` |
| 边框 | 金色 1px | 无 |
| 圆角 | 10 | 4 |
| 输入区 | 面板内嵌 | 与消息底板间隔 10px 的独立浮条 |

**输入条独立化只在 `!UsesLobbyStyle` 时生效**——大厅侧边栏是 §2.2 非目标，一像素不许变，必须有守卫测试（照第 3 期 `Lobby_sidebar_style_keeps_its_bubble_and_timestamp_unchanged` 的写法）。

输入条自身的底色取 `(0.08,0.08,0.15,0.9)`、边框 `(0.3,0.35,0.5,0.6)`（对齐参考 mod）。占位符文案改为提示快捷键，走 localizer 中英两版，并同步 `LanConnectChatLocalizerTests.ExpectedKeys`。

- [ ] **Step 1–4: 红 → 实现 → 绿**（含侧边栏守卫）

- [ ] **Step 5: 全量验证 + 提交**

`LanConnectChatResolutionTests` 是重点：输入条脱离面板后总高度会变，可能需要调 `PanelHeight`。spec §6.4 记录余量为 22px。

---

## Task 5：补齐面板内低于触摸下限的控件

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs`

spec §6.4 已清点（`LanConnectBasicChatPanel` 有自己的 `CreateButton`，未接 `EnsureTouchTarget`）：

| 控件 | 当前 | 处理 |
|---|---|---|
| `_newMessagesButton` | `(0, 34)` | 提到下限 |
| `_emojiButton` | `(38, 42)` / 侧边栏 `(42, 42)` | 提到下限 |
| `_sendButton` | `(74, 42)` / 侧边栏 `(80, 42)` | 提到下限（**本期不移除**，见开头更正） |
| 侧边栏气泡行重试按钮 | `(64, 34)` | **不动**（§2.2 非目标） |

- [ ] **Step 1: 写失败测试**，断言前三个控件两轴均 ≥ `MinTouchTargetPixels`，并断言侧边栏重试按钮**仍为 34**（防越界守卫）。

- [ ] **Step 2–4: 红 → 实现 → 绿**

考虑把 `EnsureTouchTarget` 接进 `LanConnectBasicChatPanel` 自己的 `CreateButton`，从根上堵住这个口子——但注意它同时服务侧边栏，接之前先确认不会把侧边栏的控件也撑大；若会，就只在 `!UsesLobbyStyle` 时应用，并说明。

- [ ] **Step 5: 全量验证 + 提交**

---

## 完成判据

- [ ] GdUnit 0 失败；xUnit 仅剩既有的那一个失败
- [ ] 描边宽度与底板 alpha 已依截图定稿并回写 spec §9
- [ ] 淡出容器不含气泡，且有守卫测试锁定
- [ ] 大厅侧边栏的气泡、时间戳、左侧强调条、输入区内嵌形态一像素未变，且有守卫测试
- [ ] 控件条全部控件满足可读性契约（描边、三态底板、focus 框、44px）
- [ ] v0.5.1 线格式未改动

---

## 第 4 期实际落地记录（2026-07-27）

| 提交 | 内容 |
|---|---|
| `0cc56de` | 离线截图靶场（`SubViewport` 渲染，PNG 不入库） |
| `a3e6778` | 取值定稿：`OutlineSize` 3→2、`RestPlateColor` alpha 0.35→0.45 |
| `e6f1acf` | 淡出目标改为 `RoomChatPanelFadeContainer`，气泡与 `_fadeHint` 排除在外 |
| `e32a244` | 控件条取代标题栏与 tab 栏 |
| `17b528d` | 面板外壳扁平化 + 消息/输入双底板视觉分离 + HUD 占位符文案 |
| `07c07c9` | 面板内三个控件补齐 44px 触摸下限 |

**最终状态：** GdUnit 299 通过 / 0 失败。xUnit 724 通过 / 1 跳过 / 1 失败（既有的 `LanConnectModInventoryBuilderTests`）。

**垂直余量：** 控件条比原「标题栏 + tab 栏」矮 54px，body VBox 最小内容高 498 → 444，对 `PanelHeight = 520` 的余量从 22px 增至 **76px**。输入行增高 2px 被 `ApplyViewportBounds` 硬设的 `_chatPanel.CustomMinimumSize.Y = 390`（compact 180）完全吸收，未触及该余量。

### 需要留在记录里的三件事

1. **取值定稿的证据边界。** 截图靶场用的是仓库固定测试字体 `ark-pixel-10px-proportional-zh_cn.otf`，一个 10px 像素字体，**不是游戏实际字体**。它证明了「描边必需」和「0.45 优于 0.35」——这两条是合成层面的结论，与字体无关；它**没有**证明 3px 会不会糊游戏真实字体的中文笔画。降到 2 是基于「Godot 描边自字形边缘向外生长、13px 中文字腔仅 1–2px 宽」的推理 + 「2 已足够」的观测。**最终仍需在真实字体下看一眼**，属验收范围。

2. **唯一一次放宽容差。** `LanConnectRoomRichScreenshotTests.AssertDifferentialOwnership` 的边界 padding 由 1px 放宽至 2px。原因是圆角抗锯齿光晕：它一直存在，但此前压在近透明背景上未超噪声阈值，改为不透明输入底板后超了。论证过程包含 stash 确认改前通过、插桩定位、以 30px 内边距排除「边距/圆角邻近」解释。内部色差检查未动。

3. **`ServerUnreadDot` 是修了一个既有 bug。** 见 spec §5.3 第 3 条——频道未读此前在浮层上从未显示过。

### 遗留到第 5 期

- **发送按钮的去留。** 本期只补了它的触摸下限。移除会让安卓失去可点的发送入口，须由指针模式决定：鼠标模式隐藏，触摸模式保留——与气泡同策略。
