# 局内房间聊天 HUD 化 — 第 5 期实施计划：入口自适应

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让聊天入口按输入方式自适应：鼠标/键盘环境走纯视觉简约 HUD，触摸环境保留可点的气泡与发送按钮。

**Architecture:** 先做一个不碰 Godot 节点的纯逻辑单元 `LanConnectPointerModeTracker`（与 `LanConnectRoomOverlayFadeController`、`LanConnectChatInputRouter` 同构，可单测），再由浮层消费它决定气泡与发送按钮的可见性。

**Tech Stack:** Godot 4.5 + .NET 9；xUnit（纯逻辑）+ GdUnit4（场景行为）。

**Spec:** `docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md` §7

**Branch:** `feat/room-chat-hud-redesign`

---

## 测试基线（2026-07-27 实测）

| 套件 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| GdUnit | 299 | 0 | 0 |
| xUnit | 724 | 1 | **1** |

那个 xUnit 失败是 `LanConnectModInventoryBuilderTests.Runtime_metadata_matches_the_supported_version_inventory_contract`，来自 `main`、与本计划无关。

---

## 本期要解决的洞

第 4 期把标题栏拿掉之后，桌面端重开聊天只剩 `Enter` 与 `F8`。**安卓没有键盘**——触屏用户按了「收起」就再无任何可点的东西能把聊天叫回来。同理，若移除发送按钮，触屏用户将失去可点的发送入口。

用户已定的策略：**保留气泡，但加一个检测——只有检测到鼠标事件时才走纯视觉简约 HUD，否则一直保留气泡。** 发送按钮同策略。

---

## 必须规避的陷阱：`emulate_mouse_from_touch`

仓库内无任何 `input_devices/pointing/*` 显式配置，取 Godot 默认值（`emulate_mouse_from_touch` 默认**开**）。且本 mod 以 PCK 挂进 STS2 运行，真正生效的是**游戏自身**的 project settings，我方无权假定。

后果：安卓上每次触摸都会额外合成一个 `InputEventMouseButton`。若检测写成「见鼠标事件即切鼠标模式」，触屏用户首次点击气泡的瞬间就会被判为鼠标模式，气泡在其手指下被移除——正好造成本期要修的那个故障。

**解法是时序锁定，不是事件类型判断。**

---

## Task 1：`LanConnectPointerModeTracker`

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectPointerModeTracker.cs`
- Create: `sts2-lan-connect.Tests/Lobby/Chat/LanConnectPointerModeTrackerTests.cs`

纯逻辑，不持有 Godot 节点，接受单调时钟。照 `LanConnectRoomOverlayFadeController` 的形状写（它用 `ILanConnectMonotonicClock`，测试里有现成的 `FakeClock`）。

### 判定规则

```
enum LanConnectPointerMode { Touch, Mouse }
```

- **初值**：由构造参数给出（调用方传入平台探测结果）。**拿不准时默认 `Touch`**——多给一个入口永远安全。
- **进入 Touch**：任何触摸事件，立即，并开启 `TouchLockoutSeconds = 1.0` 锁定窗口。
- **进入 Mouse**：锁定窗口**之外**的真实鼠标移动/点击，或任何键盘事件。
- **锁定窗口内的一切鼠标事件一律丢弃**——这是对合成事件的解药。

### 非对称应用规则（安全阀）

| 方向 | 时机 | 理由 |
|---|---|---|
| Mouse → Touch（**新增**入口） | 立即 | 多一个入口永远安全 |
| Touch → Mouse（**移除**入口） | 挂起，直至「面板关闭 **且** 无拖动 **且** 无淡入淡出补间」 | 绝不在用户手指下移除入口 |

因此 tracker 需要区分**内部模式**与**已生效模式**：内部模式随事件即时变化，已生效模式在移除方向上要等安全时机。把这两者作为两个可查询的属性暴露出来。

- [ ] **Step 1: 写失败测试**，至少覆盖：
  1. 触摸后 200ms 到达的鼠标按钮事件**不**改变已生效模式（合成事件解药）。
  2. 触摸后 1.5s 到达的真实鼠标移动**改变**已生效模式（在安全时机下）。
  3. 键盘事件改变模式。
  4. 初值按构造参数取值。
  5. Mouse→Touch 立即生效，**即使面板开着、正在拖动、补间在跑**。
  6. Touch→Mouse 在面板开着时挂起；面板关闭后生效。拖动中、补间中同理。
  7. 挂起期间又收到触摸事件 → 挂起被取消，保持 Touch。
  8. 时钟回退/异常值不导致卡死（照 `LanConnectRoomOverlayFadeController` 对 `IsValidNow` 的处理）。

- [ ] **Step 2–4: 红 → 实现 → 绿**

- [ ] **Step 5: 提交**

---

## Task 2：浮层消费指针模式

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs`（发送按钮可见性）
- Create: `sts2-lan-connect.GdUnitTests/Chat/LanConnectPointerModeOverlayTests.cs`

### 平台初值

`OS.HasFeature("mobile") || DisplayServer.IsTouchscreenAvailable()` 为真 → `Touch`。**先确认这两个 API 在 Godot 4.5 的 C# 绑定里的确切名称**，代码库此前从未用过它们。

### 事件喂给 tracker

浮层已有 `_Input` / `_UnhandledInput` 链路（见 `HandleUnhandledKey`、`OnDragHandleGuiInput`）。把事件类型摘要喂给 tracker，不要让 tracker 直接吃 `InputEvent`。

### 气泡

- 触摸模式：可见。形态保持现状即可（现有 `_toggleButton` + `ChatToggleUnreadBadge`），**本期不改它的外观**——把形态调整留给验收后按需处理，本期只做可见性逻辑。
- 鼠标模式：不渲染。`_fadeHint` 文案改为提示 `Enter`，走 localizer 中英两版并同步 `LanConnectChatLocalizerTests.ExpectedKeys`。

### 发送按钮

- 触摸模式：可见（现状）。
- 鼠标模式：隐藏。

**注意 `_sendButton` 与大厅侧边栏共用。** 侧边栏是 §2.2 非目标——侧边栏的发送按钮**永远可见**，不受指针模式影响。必须有守卫测试。

### 气泡不参与淡出

这是既有属性（淡出作用于 `RoomChatPanelFadeContainer`，气泡在其外），已由 `Fully_faded_panel_reaches_zero_effective_alpha_while_toggle_bubble_stays_opaque` 锁定。本期不得破坏它。

- [ ] **Step 1: 写失败测试**，至少覆盖：
  1. 触摸模式下收起面板后气泡可见且可点；鼠标模式下收起后气泡不可见而 `Enter` 仍能唤回。
  2. 鼠标模式下发送按钮隐藏；触摸模式下可见。
  3. **侧边栏守卫**：`LobbySidebar` 样式下发送按钮在两种指针模式下均可见。
  4. Touch→Mouse 在面板开着时不移除气泡；关闭后才移除。
  5. 淡出守卫仍通过。

- [ ] **Step 2–4: 红 → 实现 → 绿**

- [ ] **Step 5: 全量验证**

重点确认 `LanConnectRoomChatFocusTests`（焦点链含发送按钮，隐藏它会改变链路）、`LanConnectChatResolutionTests`、以及所有依赖 `ChatSendButtonName` 的既有测试——它们多半在默认（测试环境）指针模式下运行，若默认变成 Mouse 会导致按钮不可见而大面积变红。**若如此，测试夹具应显式指定指针模式，而不是把生产默认改成 Touch 来迁就测试。**

- [ ] **Step 6: 提交**

---

## 完成判据

- [ ] GdUnit 0 失败；xUnit 仅剩既有的那一个失败
- [ ] 合成鼠标事件不会在触摸设备上误切模式，且有测试锁定
- [ ] Touch→Mouse 的挂起规则有测试锁定
- [ ] 大厅侧边栏的发送按钮不受指针模式影响，且有守卫测试
- [ ] 气泡仍不参与淡出
