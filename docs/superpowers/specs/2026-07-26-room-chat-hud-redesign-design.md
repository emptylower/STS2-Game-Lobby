# 局内房间聊天 HUD 化改造设计

- **创建日期**：2026-07-26
- **状态**：Design — 待 review
- **作者**：项目维护者 + 协作 brainstorming
- **参考基线**：`Shiroim/sts2_typing@3cc057fa613285a4a54a614ca2aacb90827a3d32`（2026-06-22，当前 `master` HEAD，与 v0.5.2 Phase 0 审计所钉 commit 一致）
- **影响范围**：仅客户端 mod 渲染层与交互层，**不改动 v0.5.1 线格式**

## 1. 背景

局内房间聊天浮层（`LanConnectRoomChatOverlay`）当前呈现为一个"聊天软件"形态：430×520 固定面板、金色 2px 描边、`alpha 0.95` 近实心底、标题栏、双 tab 频道栏，每条消息是一个带独立底色的气泡并附 `HH:mm` 时间戳。这套形态在大厅侧边栏语境下成立，但压在战斗画面上时占据视野过多、信息密度过低。

参考 mod `sts2_typing` 在同一游戏内解决了同一问题，采用的是"战斗 HUD 日志"形态：无边框半透明底板、消息为单行富文本、无时间戳、无气泡、输入条独立浮于下方。

**功能层面我们已经追平或超过参考 mod**（详见 §3.2），本次改造是纯粹的表现层减法 + 两处既有缺陷修复 + 一处新增的输入方式自适应。

## 2. 目标 / 非目标

### 2.1 目标

1. **视野占用下降**：静息态下浮层在战斗画面中的视觉重量显著低于当前，接近参考 mod。
2. **信息密度提升**：同等高度内可见消息条数提升（去气泡、去时间戳行、去 tab 栏）。
3. **贴底可靠**：打开面板 / 切频道 / 切房间 / 收到新消息时，视图稳定停在最新消息，不再需要手动下滑。
4. **任意背景下可读**：浮层文字与控件在游戏最亮场景（火把区、米黄地图）下仍满足可读性，不依赖背景是深色这一假设。
5. **安卓能力零损失**：现有触屏交互优化一项不减，且收起后始终存在可点击的重新打开入口。
6. **互通不破**：v0.5.1 客户端与本版本互通行为不变。

### 2.2 非目标

1. **大厅侧边栏聊天改版**：`LanConnectChatVisualStyle.LobbySidebar` 分支不在本次范围内，保持现状。
2. **协议 / 线格式变更**：不新增、不修改任何 `LanConnectChatSegment` 类型或 JSON 字段。
3. **合并双频道为单流**：房间与频道保持两条独立的流（brainstorming 中已否决合流方案）。
4. **表情集扩充 / 引用能力扩充**：本次不动 `LanConnectChatEmojiSet` 与 `LanConnectItemLinkCapture` 的能力边界。
5. **移除时间戳数据**：仅从常驻渲染中移除，数据保留并改由悬停/长按呈现。

## 3. 现状回顾

### 3.1 关键文件

| 关注点 | 位置 | 现状 |
|---|---|---|
| 局内浮层骨架 | `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs:429` | `BuildUi()` 构建 toggle 按钮 + 面板框 + 标题栏 + tab 栏 + 聊天面板 |
| 面板尺寸与配色常量 | 同上 `:40-52` | `PanelWidth 430` / `PanelHeight 520` / `TabWidth 142`；`PanelColor (0.09,0.09,0.11,0.95)`、`BorderColor (0.56,0.44,0.2,1)` |
| 消息行渲染 | `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs:1031` | `PanelContainer` 气泡 + metadata 行（昵称 + 时间戳）+ 内容 + 送达状态行 |
| 昵称配色 | 同上 `:1055` | 仅 `IsLocal ? AccentColor : TextStrongColor` 两色 |
| 时间戳 | 同上 `:1062` | 每条常驻 `HH:mm` |
| 富文本 span 组合 | 同上 `:1110` | `BuildMessageContent()` 已产出 `LanConnectRichMessageView`，可直接复用 |
| 滚动状态机 | 同上 `:999-1028`、`:1786-1857`、`:2253-2257` | 有 `IsAtBottom` / `BottomValue` / `新消息` 按钮，但贴底不生效（§4） |
| 淡出状态机 | `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectRoomOverlayFadeController.cs` | `IdleDelaySeconds = 5`、`NormalFadeDurationSeconds = 0.6`；`IsEligible` 在 `:153` |
| 淡出施加对象 | `LanConnectRoomChatOverlay.cs:1010` | **只作用于 `_panelFrame`**；`_toggleButton` 在 `topRow` 内、`_panelFrame` 之外，故不参与淡出 |
| 键位路由 | `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatInputRouter.cs` | `Enter` / `Esc` 分层 / `F8` 三态 / `Alt+R` 引用切换 |
| 按钮样式工厂 | `LanConnectRoomChatOverlay.cs:1499-1537` | 实心底 + 1px 描边；**未设 `"focus"` 样式盒** |

### 3.2 参考基线审计对照

| 维度 | `sts2_typing@3cc057fa` | 本 mod 现状 | 判定 |
|---|---|---|---|
| 面板宽度 | `PanelWidth 420f` | 430 | 相当 |
| 消息区高度 | `MessageAreaHeight 250f` | 390（`:520`） | 我方更高 |
| 底板配色 | `BgColor (0.05,0.05,0.1,0.75)` | `(0.09,0.09,0.11,0.95)` + 金框 | **需改造** |
| 输入条 | `InputBgColor (0.08,0.08,0.15,0.9)` + `InputBorderColor (0.3,0.35,0.5,0.6)`，独立浮条 | 面板内嵌 + 独立发送按钮 | **需改造** |
| 正文字号 | `FontSize 18` | 14（`:1133`）；元信息 11–12 | **需改造** |
| 空闲淡出 | `FadeDelay 5f` / `FadeDuration 0.6f` | `5` / `0.6` | 已一致 |
| 昵称配色 | `GetPlayerColor(senderId)` 按人区分 | 仅本地/远端两色 | **需改造** |
| 措辞（item） | 动词化：`shared a relic: [X]` | 段落拼接 | **需改造** |
| 措辞（power / target） | `pinged X's status: [Y]`，其 `PowerSegment` 自带 `CreatureName` / `CreatureColorHex` | 线格式**不携带**生物身份，无法产出该措辞 | **对方领先，且本期不补**（见 §5.5.1） |
| 引用入口 | 仅 `Alt + 左键`，键盘依赖 | `Alt+R` + `LanConnectReferenceModeSource.TouchButton` 触屏按钮 | **我方领先** |
| 消息内链接交互 | 仅 meta hover | 支持 `InputEventScreenTouch`（`LanConnectRichMessageView.cs:160`） | **我方领先** |
| 物品预览 | 仅 hover 浮现 | 支持 `Pinned` 固定预览（`LanConnectItemPreview.cs:700`） | **我方领先** |
| 浮层位置 | 固定右上 | 长按 `DragHoldSeconds 0.28f` 可拖动并持久化 | **我方领先** |
| 窄屏适配 | 无 | `_compactLayout` | **我方领先** |
| 送达状态 / 重试 | 无 | 有失败态与重试按钮 | **我方领先** |
| 双频道 | 单频道 | 房间 + 频道双流 | **我方领先** |

结论：**参考 mod 唯一领先的是视觉表达**。改造只取其视觉语言，不引入其能力约束。

## 4. 缺陷一：消息列表不贴底

### 4.1 现象

打开聊天时视图停在**最早**的消息，需手动下滑才能看到最新消息；且此时"N 条新消息"按钮不出现。局内浮层与大厅侧边栏两处皆有，桌面与安卓皆有，**每次打开都出现**。

### 4.2 根因（已实测复现，见 §4.4）

关键前提是**面板先在频道为空时完成布局，历史批量随后才到达**。这是生产环境的实际形态，也是本缺陷此前两次未能复现的原因——若 `Bind` 时消息就已齐备，首次布局即为满高，缺陷不显现。

1. 频道为空时滚动条稳定在 `max=19, page=19`，故 `BottomValue()`（`:2256`，即 `MaxValue - Page`）为 `0`。
2. 历史到达，`Refresh()` 塞入全部消息行后，`:1001` 立即执行 `bar.Value = atBottom ? BottomValue(bar) : scrollOffset`。此刻容器尚未重排，读到的仍是空列表的 `MaxValue`，于是写入 `0`。
3. `:1019` 的补救 `CallDeferred(() => ScrollToBottomWithoutConsumingState(binding.Generation))` **并非"晚一帧执行"，而是在同一帧的消息队列 flush 中执行，排在新增行链上的 `Container::queue_sort` / `Control::_update_minimum_size` 之前**。因此它读到的依然是 `max=19`，再次写入 `0`。
4. `:1016` 此前已提交 `_renderedRevision`，`_Process`（`:371`）不会再触发 `Refresh()`；而 `:740` 只连接了 `Range.ValueChanged`、**未连接 `Range.changed`**，所以两帧后 `MaxValue` 涨到真实值时无人重新贴底。`bar.Value` 永久停在 `0`。
5. `IsAtBottom` 始终为 `true`（无人调用 `SetScrollState`），而 `TrackIncoming`（`LanConnectChatChannelState.cs:1772`）要求 `_isVisible && !_isAtBottom` 才累计 `_newMessagesBelowCount`，故"新消息"按钮一并被抑制。

**缺陷会自我固化，这是"每次打开都出现"的来源：** 关闭浮层触发 `LanConnectRoomChatOverlay.CaptureCurrentViewState`（`:826`），把错误位置记录为 `SetScrollState(0, atBottom: false)`。此后每次 `Refresh()` 都走 `atBottom == false` 分支写 `bar.Value = scrollOffset = 0`，而恢复分支 `:1021` 是 `else if (scrollOffset > 0)`——对 `0` 不成立，位置再也无法恢复。

### 4.3 已排除的假设

以下三条曾被写入本文档或据以实施，均已被实测证伪，保留在此以防重复走弯路：

| 假设 | 证伪证据 |
|---|---|
| 富文本排版需要多于一帧，`CallDeferred` 读到过期 `MaxValue` | 面板可见且 `Bind` 时消息已齐备的场景下，两帧后 `value=2336 == BottomValue=2336`，贴底正常 |
| 浮层默认关闭，`_panelFrame.Visible == false` defeat 布局，转可见时无人重新贴底 | 关闭→注入 40 条→打开，两帧后 `value=2241 == BottomValue=2241`，贴底正常 |
| 生产环境频繁重绑定使待执行的 `CallDeferred` 因 `_bindingGeneration` 守卫作废 | 两个 overlay 的绑定入口均有 `ReferenceEquals` 早退（`LanConnectRoomChatOverlay.cs:613`、`LanConnectLobbyOverlay.cs:1963`），`Bind` 仅在切频道或换 state 对象时触发；失败帧全程 `gen=1` 不变 |

`:1005` 的 `if (!IsBindingCurrent(binding)) return;` 早退确实会跳过 `:1017-1020` 的补救，属真实的潜在漏洞，但不是本缺陷的成因，且因该路径不提交 `_renderedRevision` 而不具粘性。§4.5 的修法顺带覆盖它。

### 4.4 复现

GdUnit 用例 `LanConnectChatScrollPinningTests`，三个场景全部复现：

| 场景 | `BottomValue` | `bar.Value` | `IsAtBottom` |
|---|---|---|---|
| 面板层：空面板先布局，随后注入 40 条历史 | 2336 | **0** | true |
| 房间浮层：首批历史 | 2241 | **0** | true |
| 房间浮层：关闭 + 20 条新消息 + 重开 | 3521 | **0** | false, offset 0 |

决定性的一帧的插桩轨迹：

```
Refresh gen=1 atBottom=True off=0 rebuild=True rows=40 pre(value=0,max=19,page=19,bottom=0)
  -> post value=0
DEFERRED-PIN gen=1 pre(value=0,max=19,page=19)
  -> post value=0
... 其后 5 帧无 Refresh ...
AFTER HISTORY: value=0 max=2554 page=218 bottom=2336 IsAtBottom=True below=0
```

`DEFERRED-PIN` 仍读到 `max=19`，是本缺陷的决定性证据：它与 `Refresh` 处于同一次 flush，早于容器重排。

### 4.5 修法

1. 连接 `_messagesScroll.GetVScrollBar()` 的 `Range.changed` 信号。回调中若 `_state.IsAtBottom == true` 且 `_suppressScrollChange == false`，则重新执行 `ScrollToBottomWithoutConsumingState()`。容器完成重排、`MaxValue` 增大时该信号必然触发，因此这一条同时覆盖 §4.2 第 4 步与 §4.3 表中提到的 `:1005` 早退漏洞——它不依赖 `_bindingGeneration`，也不依赖任何特定的触发时机。
2. 新增**强制贴底时机**，无条件贴底且不读取历史 `ScrollOffset`：浮层由关闭态转为打开态。

   **「打开」有三条路径，必须全部覆盖**——只改 `OpenPanel` 会漏掉 F8，而 HUD 改造移除标题栏后桌面端只剩 `Enter` 与 `F8` 两个重开入口：

   | 路径 | 入口 | 处理 |
   |---|---|---|
   | 聊天按钮（`:463` → `TogglePanel`） | `OpenPanel` | 强制贴底 |
   | `Enter`（`OpenAndFocus`） | `OpenPanel` | 强制贴底 |
   | `F8`（`RouteF8` → `ShowOverlay`） | `ShowPanelPreservingSelection(forceBottom: true)` | 强制贴底 |
   | 插入物品/战斗引用（`TryInsertEntityAndFocus`） | `ShowPanelPreservingSelection(forceBottom: false)` | **不**强制——用户在编辑而非阅读 |

   `ShowPanelPreservingSelection` 的 `forceBottom` 参数**无默认值**，强制每个调用点显式表态。
   `forceBottom` 必须在 `chat.ShowRoomOverlayPreservingSelection(...)` 返回**之后**施加：频道选择在该调用后才最终确定，提前施加会贴错频道。

   F8 未覆盖时的表现比"保留位置"更糟，也是它必须修的原因：关闭时存下 `atBottom: false`；关闭期间到达的消息因 `TrackIncoming`（`LanConnectChatChannelState.cs:1763`）的 `!_isVisible` 分支不累计 `NewMessagesBelowCount`；F8 打开时 `MarkReadCore`（`:1737`）又清零 `UnreadCount`。结果是停在历史中间、下方有新消息，而未读徽标与"N 条新消息"按钮**同时缺失**。

   - **不含**切换频道：频道各自保留浏览位置是有意设计，由 GdUnit `Rebinding_from_empty_channel_restores_saved_non_bottom_offset_after_layout` 锁定。
   - **不含**切换房间：`LanConnectDualChatState.SetActiveRoom`（`:43`）已调用 `Room.ClearForContextChange()`，其中 `LanConnectChatChannelState.cs:1046-1047` 执行 `_scrollOffset = 0; _isAtBottom = true;`，语义已成立。

3. **发送成功后贴底。** 在 `SendDraftAsync` 的成功分支（非 `catch`）调用 `ScrollToBottom()`。此前 `ScrollToBottom` 只有"N 条新消息"按钮一个调用方，导致用户在上滑状态下发出的**自己的**消息落在屏幕外——而 `TrackIncoming` 对 `isLocal` 提前返回，连按钮都不会出现。失败的发送**不**贴底：失败消息留在原处，重试入口需要保持可见。
4. `IsNearBottom` 的 8px 容差（`:2253`）保持不变。
5. 不需要改动 `:1019` 的 `CallDeferred`。第 1 条已覆盖它失败的全部后果；改动它属于对已被证伪的模型的补丁。

### 4.6 约束

修法不得引入"用户主动上滑浏览历史时被强制拉回底部"的回归——这是比原缺陷更严重的体验问题。`OnScrollRangeChanged` 中的 `!_state.IsAtBottom` 守卫是承重的，不得放宽。

**不得把 `_scrollInteractionGeneration` 加进这个守卫。** 本文档早期版本要求过这一条，是错的：

- 该 token 回答的问题是"我在 await 期间用户滚动过吗"，所以 `RestoreSavedScrollOffsetAfterLayoutAsync`（`:1857`）需要它——它在 `:1027` 捕获决策后会让出控制权。`OnScrollRangeChanged` 全程同步、不让出，不存在决策过期的窗口。
- `OnScrollChanged`（`:1790`）在每次真实用户滚动时同步更新 `_state.IsAtBottom`，`!_state.IsAtBottom` 已经涵盖了它要防的情形。
- 决定性的是：**`_scrollInteractionGeneration` 从不重置**。一旦作为守卫，用户滚动一次就会永久关闭本面板的自动贴底——包括用户点击"N 条新消息"主动回到底部之后。

## 5. 视觉与信息架构改造

### 5.1 面板外壳

| 项 | 现状 | 目标 |
|---|---|---|
| 底色 | `(0.09,0.09,0.11,0.95)` | `(0.05,0.05,0.1,0.75)` |
| 边框 | 金色 1px（`CreatePanel` 传入 `BorderColor`） | 无边框 |
| 圆角 | 10 | 4 |
| 标题栏 | "聊天" + 固定 + 收起（`:482-502`） | 移除；控件迁至控件条（§5.3） |
| tab 栏 | 双 142px tab + 数字徽标（`:504-514`） | 移除；频道切换迁至控件条 |
| 输入区 | 面板内嵌，含独立"发送"按钮 | 独立浮条，与消息底板间隔 10px；移除发送按钮（`Enter` 即发送） |

### 5.2 消息行

将 `BuildMessageRow`（`:1031`）从"气泡 + 元信息行 + 内容 + 状态行"四层结构压平为**单个 `LanConnectRichMessageView`**：

- 移除外层 `PanelContainer` 与 `CreatePanelStyle(message.IsLocal)`。
- 移除 `metadata` HBox 与其中的时间戳 Label（`:1062`）。
- 昵称作为首个彩色 span 压入富文本，其后接 `：`，再接 `BuildMessageContent()` 现有的 span 序列。`BuildMessageContent` 本身不改。
- 正文字号 14 → 16。参考 mod 取 18，但其面向拉丁文本；中文在 430px 宽度下 16 已足够且每行容纳更多字符。此值随 §9.1 一并实机确认。
- 送达失败态从独立行 + 64×34 按钮，收为行尾内联控件 `↻ 未送达`。**必须保持 `FocusMode = All` 且可由键盘触发**，不得因视觉收缩而丢失可达性。
- 时间戳改由悬停 tooltip 呈现；触摸模式下由长按呈现。

### 5.3 控件条

消息底板右上方外侧一行，常驻（不因输入方式变化）：

`房间` `频道`（含 6px 未读红点） … `引用(⌗)` `固定(⇱)` `收起(✕)`

- 频道切换以高亮/暗淡区分当前流，取代原双 tab + 双数字徽标。
- 每个控件遵守 §6 的可读性契约。
- **与面板同生共死地淡出。** 注意控件条在结构上位于 `_panelFrame` 之外，而现有淡出仅对 `_panelFrame` 施加 alpha（`:1010`）。因此需显式将控件条纳入淡出目标——**但不得改为对 `_root` 施加 alpha**，否则会连带把气泡淡掉，破坏 §7.6 的约束。正确做法是新增一个包裹「面板框 + 控件条」而**不含**气泡的中间容器作为淡出对象。

### 5.4 昵称配色

按 sender id 哈希到稳定色，取代 `:1055` 的两色方案。

- 在 HSL 空间生成，**L 钳制到 `[0.62, 0.82]`、S 钳制到 `[0.45, 0.75]`**。不钳制会出现深蓝/深紫昵称糊进 `alpha 0.75` 的深色底板。
- 本地玩家在此基础上额外提亮一档以保持自我识别。
- 同一 sender id 在同一房间内必须稳定，不受消息顺序影响。

### 5.5 动词化措辞

依据既有 segment 类型渲染动词短语，**纯客户端渲染层，不触碰线格式**：

| segment | 措辞 | 状态 |
|---|---|---|
| `LanConnectItemRefSegment` `ItemType == "card"` | `分享了卡牌：[名称]` | 已实现 |
| 同上 `"relic"` | `分享了遗物：[名称]` | 已实现 |
| 同上 `"potion"` | `分享了药水：[名称]` | 已实现 |
| 未知 `ItemType` | `分享了：[名称]`（`chat.verb.shared.item` 兜底） | 已实现 |
| `LanConnectPowerStateSegment` | ~~`报点 {生物} 的状态：`~~ | **不可实现** |
| `LanConnectTargetRefSegment` | ~~`报点 {生物}`~~ | **不可实现** |

措辞文案走既有 `LanConnectChatLocalizer`，不硬编码中文。中英两张表必须同步添加键；`LanConnectChatLocalizerTests.ExpectedKeys` 是全量键快照，加键时须一并更新。

### 5.5.1 为什么 power / target 的动词措辞不可实现

本文档早期版本（及配套 mockup）把 `报点 水蛭 的状态：[易伤 2]` 当作可做的目标，**这是错的**。调研结论：

1. **`LanConnectPowerStateSegment` 没有任何生物字段。** 其字段为 `ModelId`、`Amount`、`RoomSessionId`、`OwnerPlayerNetId`、`ApplierPlayerNetId`（`LanConnectChatProtocolModels.cs:315-320`），其中 owner/applier 是**玩家**而非怪物。捕获端 `LanConnectItemLinkCapture.cs:374` 与解析端 `LanConnectRoomCombatReferenceResolver.ResolvePower`（`:221-273`）都只使用能力自身的标题与数值。线格式里不存在"这个状态挂在哪只怪物身上"这一信息。
2. **`LanConnectTargetRefSegment` 的怪物分支在生产中无解析路径。** `"player"` 分支可经 `TryGetCurrentPeerName` 解析（这正是 `chat.target_expired` 兜底键的由来）；`"monster"` 在 `Resolve()` 的 switch 中**没有对应分支**，一律落到 `TargetExpired`。底层的 `LanConnectMonsterTargetIdPrototype` 自带常量 `MissingProofReason = "No verified STS2 network monster ID API"`，且在生产中以 `adapter: null` 构造（`LanConnectRoomCombatReferenceResolver.cs:57`），`IsEnabled` 恒为 false。

要补齐必须扩展线格式，而这是 §2.2 非目标第 2 条明确排除的（会破坏与 v0.5.1 客户端的互通）。因此本期实现三条 item 措辞，power/target 维持现状，并由 `LanConnectChatVerbPhraseTests` 锁定其返回 `null`，防止后续有人"顺手补上"一个拿不到真实数据的实现——那会在玩家面前渲染成 `报点  的状态：`。

**这同时更正了 §3.2 对照表的一处误判**：参考 mod 在 power 措辞上领先，根因不是"措辞写法"而是**它的线格式携带 `CreatureName` / `CreatureColorHex` 而我们的不携带**。那是数据能力差异，不是表现层差异。

## 6. HUD 可读性契约

适用于**所有**可能压在游戏画面之上的控件。局内背景不可预测（洞穴地板、火把亮区、米黄地图羊皮纸、战斗特效闪光），不得假定背景为深色。

1. **文字描边，而非底色。** 所有此类 `Label` / `Button` / `RichTextLabel` 设置 `font_outline_color = rgba(0,0,0,0.8)` 与 `outline_size = 3`（初值，随 §9.1 实机确认）。Godot 的描边自字形边缘向外生长并渲染于字形之下，不侵蚀笔画。**代码库当前无任何 `outline_size` / `font_outline_color` 用法，此为新引入手段。**
2. **静息底板 0.35，而非全透明。** 控件 `normal` 样式盒 `BgColor = rgba(10,10,18,0.35)`；`hover` 0.70 + 强调色描边；`pressed` 0.85 + 强调色描边。既保证亮背景下的对比度，又不读成常驻 chrome。
3. **补齐 `"focus"` 样式盒。** `LanConnectRoomChatOverlay.CreateButton`（`:1499`）当前未设 focus 样式盒，与仓库其余面板普遍采用的 2px 强调色约定不一致。去除按钮实心底后，此项从可选变为必需。
4. **触摸目标下限 44×44。** 可见字号可为 13px，但 `CustomMinimumSize` 在触摸轴不低于 44，差额以透明 padding 补足。**顺带修复既有问题**：`固定`(104×36)、`收起`(68×36)、频道 tab(×40) 当前均低于此下限。

   **`LanConnectBasicChatPanel` 内仍低于下限的控件（2026-07-26 清点，待 §5.1 外壳改造时一并处理）：**

   注意该类有**自己的** `CreateButton`，与 `LanConnectRoomChatOverlay.CreateButton` 不是同一个，后者已接入 `EnsureTouchTarget` 而前者没有——所以契约在面板内部并未自动生效。

   | 控件 | 当前尺寸 | 状态 |
   |---|---|---|
   | `_newMessagesButton` | `(0, 34)` | 两轴均低于下限 |
   | `_emojiButton` | `(38, 42)` / 侧边栏 `(42, 42)` | 两轴均低于下限 |
   | `_sendButton` | `(74, 42)` / 侧边栏 `(80, 42)` | Y 轴低于下限；§5.1 计划移除该按钮 |
   | `_referenceButton` | `(44, 44)` | 达标 |
   | 侧边栏气泡行的重试按钮 | `(64, 34)` | Y 轴低于下限，但属 §2.2 非目标，**不动** |

   HUD 扁平行的重试按钮已在第 3 期修正为 `(44, 44)`。

   **垂直余量（2026-07-26 实测）：** 三者提到 44 之后，面板 body 的 `VBoxContainer` 最小内容高为 `44 + 44 + 390 + 2×10 = 498px`，而 `PanelHeight = 520f`，**余量 22px**。`_panelFrame` 的实际高度由 `ApplyViewportBounds()` 独立设定，聊天面板（`SizeFlagsVertical = ExpandFill`）吸收差额，所以外框未移动、`LanConnectChatResolutionTests` 未破。§5.1 重做外壳时若再增加约 20px 以上的固定高度，`PanelHeight` 就必须同步上调。
5. **图标依赖第 2 条兜底。** `LanConnectLucideIconLoader.Get(name, size, color)` 仅做重着色，不支持描边；静息底板即为其可读性保障。若实机验证不足，退路是同一图标绘制两遍（黑色副本偏移 1px 垫底）。
6. **消息底板不得低于 `alpha 0.6`。** 正文依赖底板而非描边保证可读，故底板透明度有下限。

淡出过程中整层 alpha 同比例下降，描边与字形一起淡出，不会出现"字仍在、底板已消失"的中间态。

## 7. 入口自适应（指针模式）

### 7.1 问题

移除标题栏后，收起面板的入口只剩 `Enter` / `F8`。**安卓无键盘**，触屏用户按下 `收起` 后将失去全部重新打开入口。

### 7.2 决策

保留聊天气泡作为入口，但**仅在触摸模式下渲染**；检测到鼠标/键盘输入时走纯视觉简约 HUD，不渲染气泡。

### 7.3 必须规避的陷阱：`emulate_mouse_from_touch`

仓库内无任何 `input_devices/pointing/*` 显式配置，取 Godot 默认值（`emulate_mouse_from_touch` 默认为**开**）。且本 mod 以 PCK 挂载进 STS2 运行，真正生效的是**游戏自身的** project settings，我方无权假定。

后果：安卓上每次触摸都会额外合成一个 `InputEventMouseButton`。若检测写作"见鼠标事件即切鼠标模式"，则触屏用户首次点击气泡的瞬间即被判为鼠标模式，气泡在其手指下被移除——正好造成本节要规避的故障。

解法为**时序锁定**而非事件类型判断（见 §7.4）。

### 7.4 `LanConnectPointerModeTracker`

新增纯逻辑单元，与 `LanConnectRoomOverlayFadeController`、`LanConnectChatInputRouter` 同构：不持有 Godot 节点，接受输入事件摘要与单调时钟，返回模式，可独立单测。

```
enum LanConnectPointerMode { Touch, Mouse }
```

判定规则：

- **初值**：`OS.HasFeature("mobile") || DisplayServer.IsTouchscreenAvailable()` 为真 → `Touch`，否则 `Mouse`。**拿不准时默认给入口。**
- **进入 Touch**：任何 `InputEventScreenTouch` / `InputEventScreenDrag`，立即生效，并开启 `TouchLockoutSeconds = 1.0` 锁定窗口。
- **进入 Mouse**：锁定窗口**之外**的真实 `InputEventMouseMotion`（`Relative` 非零）或 `InputEventMouseButton`，或任何 `InputEventKey`。
- **锁定窗口内的一切鼠标事件一律丢弃**——此即对合成事件的解药。

### 7.5 非对称应用规则

| 方向 | 时机 | 理由 |
|---|---|---|
| Mouse → Touch（**新增**气泡） | 立即 | 多一个入口永远安全 |
| Touch → Mouse（**移除**气泡） | 挂起，直至「面板处于关闭态 **且** 无拖动进行中 **且** 无淡入淡出补间在跑」 | 绝不在用户手指下移除入口 |

### 7.6 气泡形态

- 触摸模式：56×56 圆形，`message-circle` 图标 + 未读数徽标；沿用现有长按 `0.28s` 拖动与 `LanConnectConfig.RoomChatOverlayOffset` 偏移持久化。
- 鼠标模式：不渲染；`_fadeHint` 文案改为「按 Enter 聊天」，该提示自身参与淡出。
- **气泡不参与淡出**。此为现状**已成立**的隐性属性（淡出仅作用于 `_panelFrame`，见 `:1010`；`_toggleButton` 在其之外）。本设计将其提升为显式约束并加测试锁定，防止后续重构将淡出上提至 `_root` 从而静默移除安卓唯一入口。

## 8. 明确不动的清单

以下行为在改造前后必须逐项等价，任何差异均视为回归：

- `Enter` 开启并聚焦 / `Shift+Enter` 换行 / `Esc` 分层关闭 / `F8` 三态循环 / `Alt+R` 引用切换（`LanConnectChatInputRouter` 不改）
- `LanConnectRoomOverlayFadeController.IsEligible` 的全部淡出抑制条件（未读、悬停、输入焦点、预览打开、拖动中、模态可见、有未送达消息）
- 引用模式与 `LanConnectReferenceModeSource.TouchButton` 触屏入口
- 点击消息内链接打开固定预览（`LanConnectItemPreview.Pinned`）
- 长按 `0.28s` 拖动浮层与偏移持久化
- `_compactLayout` 窄屏自适应
- 送达状态机与重试语义（仅改视觉呈现，不改状态转移）
- v0.5.1 线格式与跨版本互通
- 大厅侧边栏聊天（`LanConnectChatVisualStyle.LobbySidebar`）

## 9. 风险与待实机验证项

以下三项**纸面无法确定**，实施期间必须以实机截图确认，不得按本文档数值直接定稿：

1. **中文描边宽度**：13px 中文 + 3px 描边在游戏实际字体下是否糊笔画。需 1920×1080 桌面与安卓竖屏各取一张截图确认。不合格则降至 2px 或提升字号。
2. **静息底板 0.35 的充分性**：在游戏最亮场景下是否足以压住背景。不足则抬至 0.45，或对图标启用 §6.5 的双绘退路。
   一并复核 `LanConnectHudLegibility.Plate()` 里烤死的 5px 圆角与 10/6px 内边距——它们不在本清单原始范围内，但同属实机才能定的观感参数。
3. **锁定窗口时长**：`TouchLockoutSeconds = 1.0` 是否足以覆盖 STS2 实际的合成事件延迟。若 STS2 自行调整过 pointing 设置，此值需相应调整。

其他风险：

- **贴底修复的反向回归**：见 §4.6 约束。用户上滑浏览历史时被强制拉回底部，比原缺陷更严重。
- **昵称配色与既有强调色冲突**：哈希色板需避开 `AccentColor (0.88,0.58,0.17)` 邻域，否则本地玩家与某位远端玩家难以区分。

## 10. 验收与测试

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

### 10.1 xUnit（纯逻辑）

- `LanConnectPointerModeTracker`：触摸后 200ms 到达的合成鼠标事件**不**改变模式；1.5s 后到达的真实鼠标移动**改变**模式；键盘事件改变模式；初值随平台特征取值。
- 非对称应用规则：Touch→Mouse 在面板打开态下挂起、在关闭态下生效。
- 昵称配色：同一 sender id 稳定；生成色的 L/S 落在钳制区间内；色板与 `AccentColor` 的距离超过阈值。
- 动词化措辞：各 segment 类型经 `LanConnectChatLocalizer` 产出预期文案；未知类型走既有 fallback。
- 线格式回归：沿用 v0.5.2 Phase 0 的混合 `text/emoji/item_ref/power_state/target_ref` 字节级 round-trip fixture，断言无变化。

### 10.2 GdUnit（场景行为）

- 贴底：**空面板先布局、历史随后到达**时视图停在底部（本缺陷的决定性形态）；关闭后重开仍停在底部（验证不再自我固化）；切频道后**保留**各自浏览位置；用户上滑后新消息**不**强制拉回且"新消息"按钮出现。
- 触摸目标：控件条全部控件与气泡的 `Rect2` 短边 ≥ 44。
- 焦点：`CreateButton` 产出的按钮具备可见 focus 样式盒；`Tab` 可达行尾重试控件并可由回车触发。
- 入口：鼠标模式下收起后无可见入口且 `Enter` 可唤回；触摸模式下收起后气泡可见且可点。
- 淡出：淡出完成后 `_panelFrame` alpha 为 0 而气泡 alpha **不变**。

### 10.3 截图对比

桌面 1920×1080 与安卓竖屏各取改造前后对照，覆盖：深色战斗背景、米黄地图背景、有未读态、输入态。安卓项需真机，`adb devices` 为空时不得以模拟截图代替验收结论（沿用 Phase 0 的既有约定）。

## 11. 分期实施建议

各期独立可验证、可单独回滚：

> **2026-07-26 排期更正：** 初版把"实机定稿描边宽度与底板透明度"放在第 2 期验收，这是错的。第 2 期只建立 `LanConnectHudLegibility` 这个单元并补齐 focus 框与触摸下限，**描边与底板色并未施加到任何在渲染的控件上**，实机没有可看的东西。
>
> 正确的依赖关系是：**第 3 期不依赖这两个值**（消息行压平、昵称配色、动词化措辞都发生在 `alpha 0.75` 的消息底板之上），可以立即开始；**只有第 4 期需要**，因为控件条和面板外壳才是描边与静息底板真正上身的地方。
>
> 因此：§9 的验证项 1、2 移到第 4 期验收；第 3 期可与之并行。验证项 1（中文描边是否糊笔画）也可以提前用离线截图靶场回答，不必等游戏。

| 期 | 内容 | 验收 |
|---|---|---|
| 1 | 贴底缺陷修复（§4） | GdUnit 贴底用例；不含任何视觉改动，可独立发版 |
| 2 | HUD 可读性契约基建（§6）：描边工具函数、三态样式盒、focus 样式盒、44×44 下限 | GdUnit 焦点与触摸目标用例；实机验证项 1、2 |
| 3 | 消息行压平 + 昵称配色 + 动词化措辞（§5.2 / 5.4 / 5.5） | xUnit 措辞与配色用例；线格式 round-trip |
| 4 | 面板外壳与控件条（§5.1 / 5.3） | 截图对比 |
| 5 | 指针模式自适应与气泡（§7） | xUnit 时序用例；实机验证项 3；安卓真机 |

第 1 期与其余各期无依赖，建议优先合入。
