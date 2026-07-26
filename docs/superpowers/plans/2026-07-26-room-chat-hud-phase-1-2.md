# 局内房间聊天 HUD 化 — 第 1、2 期实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复局内聊天打开时不停在最新消息的缺陷，并建立"浮层控件在任意游戏背景下可读"的基建，为后续视觉改造铺路。

**Architecture:** 第 1 期是纯缺陷修复，不含任何视觉改动，可独立发版。根因（已实测复现）：面板在频道为空时完成布局，历史批量随后到达；`Refresh()` 与其 `CallDeferred` 补救都在容器重排之前读到空列表的 `MaxValue`，两次都把滚动条写成 `0`；而滚动条只连了 `value_changed`、没连 `changed`，所以重排完成、`MaxValue` 跳到真实值时无人重新贴底。缺陷随后经 `CaptureCurrentViewState` 自我固化，导致每次打开都卡在顶部。第 2 期新增一个无 Godot 依赖之外的小工具单元 `LanConnectHudLegibility`，集中承载描边、三态底板、焦点框、触摸目标下限四件事，供后续各期复用。

**Tech Stack:** Godot 4.5 + .NET 9；xUnit（纯逻辑）+ GdUnit4（场景行为）。

**Spec:** `docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md` §4、§6

**Branch:** `feat/room-chat-hud-redesign`

## 测试基线（2026-07-26 实测，勿用旧数字）

本计划初稿引用的 226 / 672 来自 2026-07-21 的 Phase 0 文档，**已过期**。分支上的实测基线是：

| 套件 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| GdUnit | 263 | 0 | 0 |
| xUnit | 696 | 1 | **1** |

那个 xUnit 失败是 `LanConnectModInventoryBuilderTests.Runtime_metadata_matches_the_supported_version_inventory_contract`（"runtime metadata contract has not been locked for this game version"），**来自 main、与本计划无关**，已通过 stash-and-rerun 确认。本计划的任何任务都不应试图修它，也不应因它而认为自己引入了回归。

各步骤中的绝对期望值请按「基线 + 本任务新增数」理解；数字对不上时以**失败数为 0**（xUnit 为「仅剩那一个既有失败」）作为判据。

---

## 范围说明

本计划只覆盖 spec §11 的**第 1、2 期**。第 3、4、5 期（消息行压平、面板外壳、指针模式自适应）另行成计划，原因是它们的关键取值依赖第 2 期的实机验证结果——描边宽度（3px 是否糊中文笔画）与静息底板透明度（0.35 是否压得住亮背景）确定之前，为后续各期写死数值等同于写占位符。

第 1 期与第 2 期之间无依赖，可分别合入。

---

## 文件结构

| 文件 | 责任 | 动作 |
|---|---|---|
| `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs` | 聊天面板渲染与滚动 | 修改：新增 `changed` 信号连接与回调 |
| `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs` | 局内浮层骨架 | 修改：打开/切房间时强制贴底；控件应用可读性契约 |
| `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectHudLegibility.cs` | **新建**。浮层可读性契约的唯一实现处：描边、三态底板、焦点框、触摸目标下限 | 创建 |
| `sts2-lan-connect.Tests/Lobby/Chat/LanConnectHudLegibilityTests.cs` | **新建**。纯逻辑部分单测 | 创建 |
| `sts2-lan-connect.GdUnitTests/Chat/LanConnectBasicChatPanelTests.cs` | 面板场景行为 | 修改：新增贴底与防回归用例 |
| `sts2-lan-connect.GdUnitTests/Chat/LanConnectRoomChatHudLegibilityTests.cs` | **新建**。浮层控件的描边/焦点/触摸目标验收 | 创建 |

`LanConnectHudLegibility` 单独成文件而非塞进 `LanConnectRoomChatOverlay.cs`（已 1538 行）的理由：它将被第 3、4、5 期的消息行、控件条、气泡共同复用，是跨文件的共享契约。

---

## 第 1 期：贴底缺陷修复

### Task 1: 空面板先布局、历史后到时仍停在最新消息

**Files:**
- Test: `sts2-lan-connect.GdUnitTests/Chat/LanConnectChatScrollPinningTests.cs`（复现用例，已在工作区）
- Modify: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs:740-742`（新增信号连接）、新增 `OnScrollRangeChanged` 方法

> **2026-07-26 修订：** 本任务的根因描述与红测试形态已被实测更正两次。当前版本是**已复现确认**的。
> 决定性形态是「**空面板先完成布局，历史批量随后到达**」——若 `Bind` 时消息已齐备，缺陷不显现，这是前两次未能复现的原因。
> 失败测试已由调试代理写好，位于工作区未提交的 `sts2-lan-connect.GdUnitTests/Chat/LanConnectChatScrollPinningTests.cs`（3 个用例，全部失败）。
> 详见 spec §4.2–§4.6。

- [ ] **Step 1: 确认失败测试已就位**

工作区应已存在 `sts2-lan-connect.GdUnitTests/Chat/LanConnectChatScrollPinningTests.cs`，含三个用例：
`History_arriving_after_the_empty_panel_settles_leaves_the_view_on_the_oldest_message`、
`Room_overlay_history_batch_leaves_the_view_on_the_oldest_message`、
`Closing_the_overlay_from_the_stuck_view_pins_every_later_open_to_the_oldest_message`。

若不存在，按 spec §4.4 的场景表自行补写；核心形态是：先 `Bind` 空频道并等布局稳定，**之后**再注入 40 条消息。

另有两个此前留下的守卫测试（均通过，非复现用例），保留：`LanConnectBasicChatPanelTests.cs` 中的 `Fresh_bind_with_overflowing_history_lands_on_the_newest_message`，`LanConnectRoomChatTabsTests.cs` 中的 `History_received_while_the_overlay_is_closed_opens_on_the_newest_message`。

<details>
<summary>原始（已作废）的 Step 1 测试代码，仅供追溯</summary>

在 `LanConnectBasicChatPanelTests.cs` 中，紧邻既有的 `Scrolled_up_panel_preserves_offset_and_exposes_new_message_action`（`:736`）之前插入：

```csharp
    [TestCase]
    public async Task Fresh_bind_with_overflowing_history_lands_on_the_newest_message()
    {
        LanConnectChatChannelState state = EnabledState();
        for (int index = 0; index < 40; index++)
        {
            state.AppendConfirmedForTests($"message-{index}", "A", $"message {index}", index + 1, false);
        }

        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel
        {
            CustomMinimumSize = new Vector2(480, 300)
        })!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        ScrollBar bar = FindNode<ScrollContainer>(panel, LanConnectConstants.ChatMessagesScrollName).GetVScrollBar();

        AssertThat(BottomValue(bar)).IsGreater(0d);
        AssertThat(bar.Value).IsEqual(BottomValue(bar));
        AssertThat(panel.TestState.IsAtBottom).IsTrue();
    }
```

`AssertThat(BottomValue(bar)).IsGreater(0d)` 不可省略：若内容未溢出，`BottomValue` 为 0，`bar.Value` 也为 0，断言会假性通过，测试变成空转。

</details>

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~LanConnectChatScrollPinningTests"
```

预期：3 个用例全部 FAIL，`bar.Value` 均为 `0`，而 `BottomValue(bar)` 分别为 2336 / 2241 / 3521。

- [ ] **Step 3: 连接 `changed` 信号**

在 `LanConnectBasicChatPanel.cs:740` 处，将：

```csharp
        _messagesScroll.GetVScrollBar().Connect(
            Godot.Range.SignalName.ValueChanged,
            Callable.From<double>(OnScrollChanged));
```

替换为：

```csharp
        ScrollBar messagesScrollBar = _messagesScroll.GetVScrollBar();
        messagesScrollBar.Connect(
            Godot.Range.SignalName.ValueChanged,
            Callable.From<double>(OnScrollChanged));
        messagesScrollBar.Connect(
            Godot.Range.SignalName.Changed,
            Callable.From(OnScrollRangeChanged));
```

- [ ] **Step 4: 实现回调**

在 `OnScrollChanged`（`:1786`）正下方新增：

```csharp
    private void OnScrollRangeChanged()
    {
        if (_suppressScrollChange ||
            _state == null ||
            _messagesScroll == null ||
            !GodotObject.IsInstanceValid(_messagesScroll) ||
            !_state.IsAtBottom)
        {
            return;
        }

        ScrollToBottomWithoutConsumingState();
    }
```

不会递归：`changed` 由 `min_value` / `max_value` / `page` / `step` 变化触发，而 `ScrollToBottomWithoutConsumingState()` 只写 `Value`（触发 `value_changed`），且其内部已用 `_suppressScrollChange` 包裹。

- [ ] **Step 5: 运行测试确认通过**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~LanConnectChatScrollPinningTests"
```

预期：3 个用例全部 PASS。

第三个用例（关闭 + 新消息 + 重开）之所以也会转绿，是因为首次贴底一旦正确，`CaptureCurrentViewState` 记录的就是正确的 at-bottom 位置，§4.2 描述的自我固化链条从源头断掉。**若前两个转绿而第三个仍红**，说明固化链条另有来源，停下来报告，不要额外打补丁。

**不要**改动 `:1019` 的 `CallDeferred`。spec §4.5 第 4 条已说明：`Range.changed` 覆盖了它失败的全部后果，改它属于对已被证伪模型的补丁。

- [ ] **Step 6: 运行全部 GdUnit 确认无回归**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

预期：0 失败（实测应为 266 通过 = 基线 263 + 工作区已有的 3 个复现用例 + 2 个守卫用例）。

特别确认这两个仍通过——它们锁定的是"非贴底位置必须被保留"，与本修复正交，是最可能被 `Range.changed` 误伤的地方：
- `Rebinding_from_empty_channel_restores_saved_non_bottom_offset_after_layout`
- `Scrolled_up_panel_preserves_offset_and_exposes_new_message_action`

- [ ] **Step 7: 提交**

```bash
git add sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs sts2-lan-connect.GdUnitTests/Chat/
git commit -m "fix: re-pin chat to newest message when the message list finishes sorting"
```

---

### Task 2: 上滑浏览时不得被强制拉回（防回归守卫）

**Files:**
- Test: `sts2-lan-connect.GdUnitTests/Chat/LanConnectBasicChatPanelTests.cs`

Task 1 的修复引入了一条"自动移动视口"的路径。这是比原缺陷更严重的回归风险：用户上滑读历史时被拉回底部会直接破坏可用性。本任务只加守卫测试，不改生产代码。

- [ ] **Step 1: 写守卫测试**

在 Task 1 新增的测试正下方插入：

```csharp
    [TestCase]
    public async Task Scrolled_up_reader_is_not_yanked_to_bottom_when_the_list_grows()
    {
        LanConnectChatChannelState state = EnabledState();
        for (int index = 0; index < 40; index++)
        {
            state.AppendConfirmedForTests($"message-{index}", "A", $"message {index}", index + 1, false);
        }

        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel
        {
            CustomMinimumSize = new Vector2(480, 300)
        })!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        ScrollBar bar = FindNode<ScrollContainer>(panel, LanConnectConstants.ChatMessagesScrollName).GetVScrollBar();
        double parked = Math.Max(1d, BottomValue(bar) / 2d);
        panel.SetScrollForTests(parked, atBottom: false);
        await runner.AwaitIdleFrame();

        state.AppendConfirmedForTests(
            "late",
            "A",
            "一条足够长的迟到消息，用来撑高列表并触发 changed 信号重新计算 max_value",
            100,
            false);
        await panel.RefreshForTests();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        AssertThat(bar.Value).IsEqual(parked);
        AssertThat(panel.TestState.IsAtBottom).IsFalse();
        AssertThat(panel.TestState.NewMessagesBelowCount).IsEqual(1);
    }
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~Scrolled_up_reader_is_not_yanked"
```

预期：PASS。本测试在 Task 1 修复后应直接通过（`OnScrollRangeChanged` 的 `!_state.IsAtBottom` 守卫拦住了拉回）。**若 FAIL，说明 Task 1 的守卫条件有漏，必须回到 Task 1 修正，不得放宽本测试。**

- [ ] **Step 3: 提交**

```bash
git add sts2-lan-connect.GdUnitTests/Chat/LanConnectBasicChatPanelTests.cs
git commit -m "test: guard against yanking a scrolled-up reader back to bottom"
```

---

### Task 3: 打开面板与切换房间时强制贴底

**Files:**
- Test: `sts2-lan-connect.GdUnitTests/Chat/LanConnectRoomChatTabsTests.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs:658`（`OpenPanel`）

**范围收窄说明（与 spec §4.3.3 的差异）：** spec 原文含"切频道时强制贴底"。实施时收窄为**仅打开面板与切换房间**，不含切频道。理由：`LanConnectBasicChatPanel.Bind`（`:390`）刻意不重置滚动状态，且 `Rebinding_from_empty_channel_restores_saved_non_bottom_offset_after_layout`（GdUnit `:779`）明确锁定了"频道各自记住浏览位置"这一行为。它是有意设计而非疏漏，强制贴底会构成回归。用户报告的问题是"打开时"，与频道切换无关。spec 需同步修订。

- [ ] **Step 1: 写失败测试**

在 `LanConnectRoomChatTabsTests.cs` 末尾（最后一个 `}` 之前）插入：

```csharp
    [TestCase]
    public async Task Reopening_the_overlay_forces_the_view_back_to_the_newest_message()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        LanConnectRoomChatOverlay overlay = fixture.Overlay;
        for (int index = 0; index < 40; index++)
        {
            fixture.State.Room.AppendConfirmedForTests(
                $"room-{index}", "A", $"room message {index}", index + 1, false);
        }
        await overlay.RefreshForTests();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();

        overlay.SelectChannelForTests(LanConnectChatChannel.Room);
        overlay.SetScrollForTests(0d, atBottom: false);
        await fixture.Runner.AwaitIdleFrame();
        AssertThat(fixture.State.Room.IsAtBottom).IsFalse();

        await overlay.CloseForTests();
        await fixture.Runner.AwaitIdleFrame();
        await overlay.OpenForTests();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.State.Room.IsAtBottom).IsTrue();
        AssertThat(fixture.State.Room.NewMessagesBelowCount).IsEqual(0);
    }
```

`RoomChatFixture` 是 GdUnit 聊天测试的既有夹具（见 `LanConnectRoomChatFocusTests.cs`、`LanConnectRoomChatTabsTests.cs` 的用法），直接复用，不要另造浮层构造入口。

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~Reopening_the_overlay_forces"
```

预期：FAIL，`chat.Room.IsAtBottom` 为 `false`。

- [ ] **Step 3: 在 `OpenPanel` 中强制贴底**

`LanConnectRoomChatOverlay.cs:658`，将：

```csharp
        bool serverSelectable = chat.Server.Presentation != LanConnectServerChatPresentation.Unsupported;
        chat.OpenRoomOverlay(serverSelectable);
        SignalFadeActivity();
        RefreshFromSource();
```

替换为：

```csharp
        bool serverSelectable = chat.Server.Presentation != LanConnectServerChatPresentation.Unsupported;
        chat.OpenRoomOverlay(serverSelectable);
        ForceSelectedChannelToBottom(chat);
        SignalFadeActivity();
        RefreshFromSource();
```

并在 `OpenPanel` 正下方新增：

```csharp
    private static void ForceSelectedChannelToBottom(LanConnectDualChatState chat)
    {
        LanConnectChatChannelState selected = chat.SelectedChannel == LanConnectChatChannel.Room
            ? chat.Room
            : chat.Server;
        selected.SetScrollState(0d, atBottom: true);
    }
```

`SetScrollState(0d, atBottom: true)` 的 `atBottom: true` 会令 `Refresh()`（`:1001`）走贴底分支，随后由 Task 1 的 `changed` 回调在布局完成后落到真实底部；同时它会清空 `NewMessagesBelowCount`（见 `LanConnectChatChannelState.cs:533`），这正是打开面板时期望的语义。

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~Reopening_the_overlay_forces"
```

预期：PASS。

- [ ] **Step 5: 切换房间 —— 已满足，无需改动**

已核实：换房走 `LanConnectDualChatState.SetActiveRoom`（`LanConnectDualChatState.cs:43`），其中调用 `Room.ClearForContextChange()`；该方法在 `LanConnectChatChannelState.cs:1046-1047` 执行 `_scrollOffset = 0; _isAtBottom = true;`。**强制贴底语义已经成立，本步无代码改动。**

为防止后续重构静默移除该属性，在 `LanConnectChatChannelStateTests.cs` 中追加锁定测试：

```csharp
    [Fact]
    public void ClearForContextChangeResetsScrollToBottom()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        state.AppendConfirmedForTests("m-1", "A", "hello", 1, false);
        state.SetScrollState(240d, atBottom: false);

        state.ClearForContextChange();

        Assert.Equal(0d, state.ScrollOffset);
        Assert.True(state.IsAtBottom);
    }
```

运行：

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --filter "FullyQualifiedName~ClearForContextChangeResetsScrollToBottom"
```

预期：PASS（该行为已存在，本测试是防回归锁）。

- [ ] **Step 6: 运行全部 GdUnit**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

预期：0 失败（基线 + 本任务新增 1）。

- [ ] **Step 7: 同步修订 spec**

编辑 `docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md` §4.3 第 3 条，将：

```
   - 切换频道（房间 ↔ 频道）
```

替换为：

```
   - （**不含**切换频道：频道各自保留浏览位置是有意设计，由 GdUnit
     `Rebinding_from_empty_channel_restores_saved_non_bottom_offset_after_layout` 锁定）
```

- [ ] **Step 8: 提交**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs sts2-lan-connect.GdUnitTests/Chat/LanConnectRoomChatTabsTests.cs sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatChannelStateTests.cs docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md
git commit -m "fix: force chat back to newest message when the overlay reopens"
```

---

### Task 4: 第 1 期收尾验证

- [ ] **Step 1: 运行两套测试**

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
```

预期：仅剩 `LanConnectModInventoryBuilderTests` 那一个既有失败，无新增失败。

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

预期：0 失败。

- [ ] **Step 2: 实机确认**

构建并安装，进入一局多人对局，在聊天中积累超过一屏的消息后关闭再打开浮层。

```bash
./scripts/build-sts2-lan-connect.sh --install --dry-run
```

确认项：打开时直接显示最新消息；上滑读历史时新消息到达不拉回视口且"新消息"按钮出现；点击该按钮回到底部。

第 1 期到此可独立合入，不含任何视觉改动。

---

## 第 2 期：HUD 可读性基建

### Task 5: `LanConnectHudLegibility` 纯逻辑部分

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectHudLegibility.cs`
- Create: `sts2-lan-connect.Tests/Lobby/Chat/LanConnectHudLegibilityTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `sts2-lan-connect.Tests/Lobby/Chat/LanConnectHudLegibilityTests.cs`：

```csharp
using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectHudLegibilityTests
{
    [Theory]
    [InlineData(68f, 36f, 68f, 44f)]
    [InlineData(104f, 36f, 104f, 44f)]
    [InlineData(132f, 44f, 132f, 44f)]
    [InlineData(0f, 0f, 44f, 44f)]
    [InlineData(56f, 56f, 56f, 56f)]
    public void Touch_targets_are_raised_to_the_floor_without_shrinking(
        float requestedX,
        float requestedY,
        float expectedX,
        float expectedY)
    {
        Vector2 result = LanConnectHudLegibility.EnsureTouchTarget(new Vector2(requestedX, requestedY));

        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
    }

    [Fact]
    public void Rest_plate_is_translucent_enough_to_read_as_chrome_free()
    {
        Assert.InRange(LanConnectHudLegibility.RestPlateColor.A, 0.30f, 0.40f);
        Assert.True(LanConnectHudLegibility.HoverPlateColor.A > LanConnectHudLegibility.RestPlateColor.A);
        Assert.True(LanConnectHudLegibility.PressedPlateColor.A > LanConnectHudLegibility.HoverPlateColor.A);
    }

    [Fact]
    public void Outline_is_dark_and_thick_enough_to_survive_a_bright_background()
    {
        Assert.True(LanConnectHudLegibility.OutlineColor.A >= 0.75f);
        Assert.True(LanConnectHudLegibility.OutlineColor.R <= 0.1f);
        Assert.True(LanConnectHudLegibility.OutlineColor.G <= 0.1f);
        Assert.True(LanConnectHudLegibility.OutlineColor.B <= 0.1f);
        Assert.True(LanConnectHudLegibility.OutlineSize >= 2);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --filter "FullyQualifiedName~LanConnectHudLegibilityTests"
```

预期：编译失败，`LanConnectHudLegibility` 不存在。

- [ ] **Step 3: 创建实现**

创建 `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectHudLegibility.cs`：

```csharp
using Godot;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// 浮在游戏画面之上的控件必须满足的可读性契约。
/// 局内背景不可预测（洞穴、火把亮区、米黄地图、战斗特效），不得假定背景为深色。
/// 详见 docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md §6。
/// </summary>
internal static class LanConnectHudLegibility
{
    internal const int OutlineSize = 3;
    internal const int MinTouchTargetPixels = 44;

    internal static readonly Color OutlineColor = new(0f, 0f, 0f, 0.8f);
    internal static readonly Color RestPlateColor = new(0.04f, 0.04f, 0.07f, 0.35f);
    internal static readonly Color HoverPlateColor = new(0.04f, 0.04f, 0.07f, 0.7f);
    internal static readonly Color PressedPlateColor = new(0.04f, 0.04f, 0.07f, 0.85f);

    internal static Vector2 EnsureTouchTarget(Vector2 requested) => new(
        Mathf.Max(requested.X, MinTouchTargetPixels),
        Mathf.Max(requested.Y, MinTouchTargetPixels));
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --filter "FullyQualifiedName~LanConnectHudLegibilityTests"
```

预期：新增的 7 个用例全部通过，套件无新增失败。

- [ ] **Step 5: 提交**

```bash
git add sts2-lan-connect/Scripts/Lobby/Chat/LanConnectHudLegibility.cs sts2-lan-connect.Tests/Lobby/Chat/LanConnectHudLegibilityTests.cs
git commit -m "feat: add HUD legibility contract constants and touch-target floor"
```

---

### Task 6: 描边与三态样式盒

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectHudLegibility.cs`
- Test: `sts2-lan-connect.GdUnitTests/Chat/LanConnectRoomChatHudLegibilityTests.cs`（新建）

- [ ] **Step 1: 写失败测试**

创建 `sts2-lan-connect.GdUnitTests/Chat/LanConnectRoomChatHudLegibilityTests.cs`：

```csharp
using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomChatHudLegibilityTests
{
    [TestCase]
    public void Text_outline_is_applied_to_labels()
    {
        Label label = AutoFree(new Label { Text = "房间" })!;

        LanConnectHudLegibility.ApplyTextOutline(label);

        AssertThat(label.HasThemeColorOverride("font_outline_color")).IsTrue();
        AssertThat(label.GetThemeColor("font_outline_color").A)
            .IsEqual(LanConnectHudLegibility.OutlineColor.A);
        AssertThat(label.GetThemeConstant("outline_size"))
            .IsEqual(LanConnectHudLegibility.OutlineSize);
    }

    [TestCase]
    public void Text_outline_is_applied_to_buttons()
    {
        Button button = AutoFree(new Button { Text = "收起" })!;

        LanConnectHudLegibility.ApplyTextOutline(button);

        AssertThat(button.GetThemeConstant("outline_size"))
            .IsEqual(LanConnectHudLegibility.OutlineSize);
    }

    [TestCase]
    public void Hud_button_carries_all_four_state_styleboxes_including_focus()
    {
        Button button = AutoFree(new Button { Text = "固定" })!;

        LanConnectHudLegibility.ApplyHudButtonStyle(button, new Color(0.88f, 0.58f, 0.17f, 1f));

        AssertThat(button.HasThemeStyleboxOverride("normal")).IsTrue();
        AssertThat(button.HasThemeStyleboxOverride("hover")).IsTrue();
        AssertThat(button.HasThemeStyleboxOverride("pressed")).IsTrue();
        AssertThat(button.HasThemeStyleboxOverride("focus")).IsTrue();

        StyleBoxFlat normal = (StyleBoxFlat)button.GetThemeStylebox("normal");
        StyleBoxFlat hover = (StyleBoxFlat)button.GetThemeStylebox("hover");
        StyleBoxFlat focus = (StyleBoxFlat)button.GetThemeStylebox("focus");

        AssertThat(normal.BgColor.A).IsEqual(LanConnectHudLegibility.RestPlateColor.A);
        AssertThat(hover.BgColor.A).IsEqual(LanConnectHudLegibility.HoverPlateColor.A);
        AssertThat(focus.BorderWidthTop).IsEqual(2);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~LanConnectRoomChatHudLegibilityTests"
```

预期：编译失败，`ApplyTextOutline` / `ApplyHudButtonStyle` 不存在。

- [ ] **Step 3: 实现两个方法**

在 `LanConnectHudLegibility.cs` 的 `EnsureTouchTarget` 之后追加：

```csharp
    internal static void ApplyTextOutline(Control control)
    {
        control.AddThemeColorOverride("font_outline_color", OutlineColor);
        control.AddThemeConstantOverride("outline_size", OutlineSize);
    }

    internal static void ApplyHudButtonStyle(Button button, Color accent)
    {
        button.AddThemeStyleboxOverride("normal", Plate(RestPlateColor, Colors.Transparent, 0));
        button.AddThemeStyleboxOverride("hover", Plate(HoverPlateColor, accent, 1));
        button.AddThemeStyleboxOverride("pressed", Plate(PressedPlateColor, accent, 1));
        button.AddThemeStyleboxOverride("hover_pressed", Plate(PressedPlateColor, accent, 1));
        button.AddThemeStyleboxOverride("focus", Plate(RestPlateColor, accent, 2));
        button.CustomMinimumSize = EnsureTouchTarget(button.CustomMinimumSize);
        button.FocusMode = Control.FocusModeEnum.All;
        ApplyTextOutline(button);
    }

    private static StyleBoxFlat Plate(Color background, Color border, int borderWidth) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = borderWidth,
        BorderWidthTop = borderWidth,
        BorderWidthRight = borderWidth,
        BorderWidthBottom = borderWidth,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 10,
        ContentMarginTop = 6,
        ContentMarginRight = 10,
        ContentMarginBottom = 6
    };
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~LanConnectRoomChatHudLegibilityTests"
```

预期：新增的 3 个用例全部通过。

- [ ] **Step 5: 提交**

```bash
git add sts2-lan-connect/Scripts/Lobby/Chat/LanConnectHudLegibility.cs sts2-lan-connect.GdUnitTests/Chat/LanConnectRoomChatHudLegibilityTests.cs
git commit -m "feat: add HUD text outline and four-state button styling"
```

---

### Task 7: 浮层现有控件接入契约

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs:1499`（`CreateButton`）、`:494`（固定按钮尺寸）、`:501`（收起按钮尺寸）

本任务只补齐既有缺口（缺 focus 样式盒、两个按钮低于 44px 下限），**不改变现有配色与外观**。视觉改造留到第 4 期。

- [ ] **Step 1: 写失败测试**

在 `LanConnectRoomChatHudLegibilityTests.cs` 末尾（最后一个 `}` 之前）插入：

```csharp
    [TestCase]
    public async Task Overlay_controls_meet_the_touch_target_floor_and_expose_focus()
    {
        LanConnectRoomChatOverlay overlay = AutoFree(new LanConnectRoomChatOverlay())!;
        using ISceneRunner runner = ISceneRunner.Load(overlay, autoFree: true);
        await runner.AwaitIdleFrame();

        Button pin = (Button)overlay.FindChild("ChatPinButton", recursive: true, owned: false);

        AssertThat(pin.CustomMinimumSize.Y)
            .IsGreaterEqual(LanConnectHudLegibility.MinTouchTargetPixels);
        AssertThat(pin.HasThemeStyleboxOverride("focus")).IsTrue();
    }
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~Overlay_controls_meet_the_touch_target_floor"
```

预期：FAIL。`CustomMinimumSize.Y` 为 36；无 focus 样式盒。

- [ ] **Step 3: 在 `CreateButton` 中补 focus 样式盒与触摸下限**

`LanConnectRoomChatOverlay.cs:1499`，在 `button.AddThemeColorOverride("font_color", TextStrongColor);` 之前插入两行：

```csharp
        button.AddThemeStyleboxOverride("focus", CreateButtonStyle(background, AccentColor));
        button.CustomMinimumSize = LanConnectHudLegibility.EnsureTouchTarget(button.CustomMinimumSize);
```

`CreateButtonStyle` 现有边框宽度为 1；焦点态需要 2px 才符合仓库其余面板的既定约定。因此同时将 `CreateButtonStyle`（`:1520`）改为接受边框宽度参数：

```csharp
    private static StyleBoxFlat CreateButtonStyle(Color background, Color border, int borderWidth = 1) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = borderWidth,
        BorderWidthTop = borderWidth,
        BorderWidthRight = borderWidth,
        BorderWidthBottom = borderWidth,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 10,
        ContentMarginTop = 7,
        ContentMarginRight = 10,
        ContentMarginBottom = 7
    };
```

并把上面插入的 focus 那行改为：

```csharp
        button.AddThemeStyleboxOverride("focus", CreateButtonStyle(background, AccentColor, borderWidth: 2));
```

- [ ] **Step 4: 修正两个按钮的显式尺寸**

`:494`，将 `_pinButton.CustomMinimumSize = new Vector2(104, 36);` 改为：

```csharp
        _pinButton.CustomMinimumSize = new Vector2(104, LanConnectHudLegibility.MinTouchTargetPixels);
```

`:501`，将 `closeButton.CustomMinimumSize = new Vector2(68, 36);` 改为：

```csharp
        closeButton.CustomMinimumSize = new Vector2(68, LanConnectHudLegibility.MinTouchTargetPixels);
```

`CreateButton` 中的 `EnsureTouchTarget` 在构造时执行，而这两处赋值在其之后，故需显式改写；不可依赖 `EnsureTouchTarget` 兜底。

- [ ] **Step 5: 运行测试确认通过**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1 --filter "FullyQualifiedName~Overlay_controls_meet_the_touch_target_floor"
```

预期：PASS。

- [ ] **Step 6: 运行全部 GdUnit 确认布局无回归**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

预期：0 失败。**重点确认 `LanConnectChatResolutionTests` 全部通过**——按钮增高 8px 可能挤压 430×520 面板内的布局，该测试套断言各控件矩形落在视口内。若失败，说明面板高度需相应上调，在 `PanelHeight`（`:41`）中补足差额并记录。

- [ ] **Step 7: 提交**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs sts2-lan-connect.GdUnitTests/Chat/LanConnectRoomChatHudLegibilityTests.cs
git commit -m "fix: give room chat buttons a focus stylebox and a 44px touch floor"
```

---

### Task 8: 实机取值定稿

本任务无自动化测试，产出是**两个数值的最终定稿**与一组截图证据。第 3、4、5 期的计划依赖本任务结论。

- [ ] **Step 1: 构建并安装**

```bash
./scripts/build-sts2-lan-connect.sh --install
```

- [ ] **Step 2: 桌面截图取证**

在 1920×1080 下进入对局，分别在以下三种背景前打开聊天浮层并截图：深色洞穴地面、火把/篝火亮区、地图界面（米黄羊皮纸）。

判定：控件文字在三种背景下均清晰可辨；`outline_size = 3` 未使中文笔画粘连。

- [ ] **Step 3: 安卓截图取证**

```bash
adb devices
```

若输出为空，**不得以桌面模拟截图代替安卓验收结论**（沿用 v0.5.2 Phase 0 既有约定）。有设备时在竖屏下重复 Step 2 的三种背景。

- [ ] **Step 4: 定稿两个数值**

依据截图结论，在 `LanConnectHudLegibility.cs` 中定稿：

- `OutlineSize`：笔画粘连则降至 `2`；清晰则保持 `3`。
- `RestPlateColor` 的 alpha：亮背景下压不住则由 `0.35f` 抬至 `0.45f`。

若调整了任一数值，同步更新 `LanConnectHudLegibilityTests.Rest_plate_is_translucent_enough_to_read_as_chrome_free` 的 `Assert.InRange` 区间与 `Outline_is_dark_and_thick_enough_to_survive_a_bright_background` 的下限，使测试继续锁定定稿值。

- [ ] **Step 5: 记录结论**

编辑 `docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md` §9，把验证项 1、2 从"待验证"改为记录实测结论与最终取值，保留截图的 SHA-256（截图本身按 Phase 0 约定存放于系统临时目录，不入库）。

- [ ] **Step 6: 提交**

```bash
git add sts2-lan-connect/Scripts/Lobby/Chat/LanConnectHudLegibility.cs sts2-lan-connect.Tests/Lobby/Chat/LanConnectHudLegibilityTests.cs docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md
git commit -m "chore: finalize HUD outline width and rest plate alpha from device testing"
```

---

## 完成判据

- [ ] `dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj` — 除既有的 `LanConnectModInventoryBuilderTests` 外无失败
- [ ] `dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1` — 0 失败
- [ ] 实机：打开聊天直接停在最新消息
- [ ] 实机：上滑读历史时不被新消息拉回
- [ ] 实机：三种背景下控件文字均可辨
- [ ] `OutlineSize` 与 `RestPlateColor` 已依据实机结论定稿并写回 spec §9

以上全部满足后，第 3、4、5 期方可开始写计划。
