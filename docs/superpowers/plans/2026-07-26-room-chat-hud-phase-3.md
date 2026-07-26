# 局内房间聊天 HUD 化 — 第 3 期实施计划：消息行

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把消息行从「气泡 + 元信息行 + 内容 + 状态行」四层结构压平成一行富文本，并加入每人稳定的昵称配色与动词化措辞。

**Architecture:** 三个改动彼此独立，先做两个纯逻辑单元（昵称配色、动词措辞），再由消息行渲染消费它们。`BuildMessageContent()` 已经产出 `LanConnectRichMessageView`，所以压平是「去掉外壳、把昵称并进富文本」，不是重写渲染。

**Tech Stack:** Godot 4.5 + .NET 9；xUnit（纯逻辑）+ GdUnit4（场景行为）。

**Spec:** `docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md` §5.2、§5.4、§5.5

**Branch:** `feat/room-chat-hud-redesign`

---

## 关键约束：不得波及大厅侧边栏

`BuildMessageRow`（`LanConnectBasicChatPanel.cs:1031`）**由局内浮层与大厅侧边栏共用**。`CreatePanelStyle`（`:2408`）里 `UsesLobbyStyle` 分支是一套刻意的视觉设计：本人消息左侧 3px 强调条、他人消息底部 1px 分隔线、各自的内边距。

spec §2.2 非目标第 1 条明确把大厅侧边栏改版排除在外。因此：

- **压平（Task 3）只在 `!UsesLobbyStyle` 时生效**，侧边栏走原路径。
- **昵称配色（Task 1）与动词措辞（Task 2）两侧都生效**——它们提升的是信息可读性，不改变侧边栏的版式，不构成"改版"。

任何让侧边栏丢掉气泡、时间戳或左侧强调条的改动都是越界，必须停下报告。

---

## 测试基线（2026-07-26 实测）

| 套件 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| GdUnit | 274 | 0 | 0 |
| xUnit | 704 | 1 | **1** |

那个 xUnit 失败是 `LanConnectModInventoryBuilderTests.Runtime_metadata_matches_the_supported_version_inventory_contract`，来自 `main`、与本计划无关，不要试图修它。`ReconnectUsesJitteredBackoffAndFreshProbeTicketTransport` 是已知的计时抖动，遇到就隔离重跑确认。

**已知会因本期改动变红、需要一并更新的既有测试：**

- `LanConnectRichMessageRenderingTests.cs:35` — 断言 `GetThemeFontSize("normal_font_size", "RichTextLabel") == 14`，Task 3 改为 16 后必红。这是预期的，改断言值即可，**不要为了让它绿而放弃字号改动**。
- 其余凡断言消息行内存在时间戳 `Label`、或断言 `ChatMessageRow*` 子节点数量的用例，同理。

---

## 文件结构

| 文件 | 责任 | 动作 |
|---|---|---|
| `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatNameColor.cs` | **新建**。sender id → 稳定昵称色，唯一实现处 | 创建 |
| `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatVerbPhrase.cs` | **新建**。segment → 动词短语本地化键，唯一实现处 | 创建 |
| `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatLocalizer.cs` | 文案表 | 修改：新增动词键的中英两版 |
| `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs` | 消息行渲染 | 修改：压平（仅 HUD 样式）、消费上述两个单元 |
| `sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatNameColorTests.cs` | **新建** | 创建 |
| `sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatVerbPhraseTests.cs` | **新建** | 创建 |
| `sts2-lan-connect.GdUnitTests/Chat/LanConnectFlatMessageRowTests.cs` | **新建**。压平后的行结构验收 | 创建 |

两个新单元独立成文件而非塞进 `LanConnectBasicChatPanel.cs`（已 2446 行）的理由：它们是纯逻辑、可脱离 Godot 单测，且后续期次的控件条与气泡也会消费昵称配色。

---

## Task 1：每人稳定的昵称配色

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatNameColor.cs`
- Create: `sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatNameColorTests.cs`

现状是 `message.IsLocal ? AccentColor : TextStrongColor` 两色（`:1055`），四个人聊起来分不清谁是谁。

- [ ] **Step 1: 写失败测试**

创建 `sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatNameColorTests.cs`：

```csharp
using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatNameColorTests
{
    [Theory]
    [InlineData("Toadpole")]
    [InlineData("狂战士")]
    [InlineData("a")]
    [InlineData("")]
    public void Same_sender_always_gets_the_same_colour(string sender)
    {
        Color first = LanConnectChatNameColor.ForSender(sender, isLocal: false);
        Color second = LanConnectChatNameColor.ForSender(sender, isLocal: false);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("Toadpole")]
    [InlineData("狂战士")]
    [InlineData("")]
    [InlineData("a-very-long-display-name-that-someone-actually-used")]
    public void Every_colour_stays_inside_the_readable_band(string sender)
    {
        Color colour = LanConnectChatNameColor.ForSender(sender, isLocal: false);

        float lightness = colour.V * (1f - (colour.S / 2f));
        Assert.InRange(lightness, 0.60f, 0.84f);
        Assert.InRange(colour.S, 0.40f, 0.80f);
    }

    [Fact]
    public void Local_player_is_brighter_than_the_same_name_remote()
    {
        Color remote = LanConnectChatNameColor.ForSender("Toadpole", isLocal: false);
        Color local = LanConnectChatNameColor.ForSender("Toadpole", isLocal: true);

        Assert.NotEqual(remote, local);
        Assert.True(local.V > remote.V);
    }

    [Fact]
    public void Distinct_senders_are_visually_separable()
    {
        string[] senders = ["Alice", "Bob", "Carol", "Dave", "Erin", "Frank"];
        List<Color> colours = senders.Select(s => LanConnectChatNameColor.ForSender(s, isLocal: false)).ToList();

        for (int i = 0; i < colours.Count; i++)
        {
            for (int j = i + 1; j < colours.Count; j++)
            {
                Assert.True(
                    HueDistance(colours[i].H, colours[j].H) > 0.04f,
                    $"{senders[i]} and {senders[j]} are too close in hue");
            }
        }
    }

    private static float HueDistance(float a, float b)
    {
        float raw = Math.Abs(a - b);
        return Math.Min(raw, 1f - raw);
    }
}
```

`Assert.InRange` 的区间比 spec 的 `[0.62, 0.82]` / `[0.45, 0.75]` 各放宽 0.02–0.05，给浮点与色彩空间换算留余地；收窄区间会让测试变脆而不会让配色更好。

`Distinct_senders_are_visually_separable` 是本任务里最容易写成空转的一条：**六个名字必须真的散开**。若实现用的哈希把它们撞到相邻色相，这条会红——那说明色板划分不对，改实现，不要放宽阈值。

- [ ] **Step 2: 运行确认失败（类型不存在）**

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --filter "FullyQualifiedName~LanConnectChatNameColorTests"
```

- [ ] **Step 3: 实现**

创建 `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatNameColor.cs`。要求：

- `internal static Color ForSender(string senderName, bool isLocal)`。
- 用稳定哈希（**不要用 `string.GetHashCode()`**——.NET 的字符串哈希每进程随机化，同一玩家在不同会话会变色）。用 FNV-1a 或对 UTF-8 字节做简单累加即可。
- 把哈希映射到 HSV：色相取哈希落在 `[0,1)`，饱和度钳到 `[0.45, 0.75]`，明度钳到使最终亮度落在 `[0.62, 0.82]`。
- `isLocal` 时在同一色相上提亮一档（并保持仍在可读带内）。
- **避开 `AccentColor (0.88, 0.58, 0.17)` 的邻域**：该色是本地玩家与强调元素的既有用色，若某位远端玩家抽到相近色相就分不出来了。在色相环上排除 `AccentColor` 色相 ±0.04 的区间（把落入该区间的色相推开）。
- 空名字与 `null` 必须返回稳定的兜底色，不得抛异常。

具体的色相分桶方式由你决定，但必须让 Step 1 的六名测试通过而**不靠放宽阈值**。

- [ ] **Step 4: 运行确认通过**

- [ ] **Step 5: 提交**

```bash
git add sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatNameColor.cs sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatNameColorTests.cs
git commit -m "feat: give each chat participant a stable readable name colour"
```

---

## Task 2：动词化措辞

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatVerbPhrase.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatLocalizer.cs`
- Create: `sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatVerbPhraseTests.cs`

目标措辞（对照参考 mod `sts2_typing` 的 `shared a relic: [X]`）：

| segment | 中文 | English |
|---|---|---|
| `LanConnectItemRefSegment` `ItemType == "card"` | `分享了卡牌：` | `shared a card: ` |
| 同上 `"relic"` | `分享了遗物：` | `shared a relic: ` |
| 同上 `"potion"` | `分享了药水：` | `shared a potion: ` |
| `LanConnectPowerStateSegment` | `报点 {0} 的状态：` | `pinged {0}'s status: ` |
| `LanConnectTargetRefSegment` | `报点 {0}` | `pinged {0}` |

**纯客户端渲染层，不触碰线格式。** `ItemType` 是字符串（见 `LanConnectItemLinkCapture.cs:189-193`），既有的 `ItemCopyPlaceholder`（`LanConnectBasicChatPanel.cs:1251`）就是同形状的映射，照它的写法做。

- [ ] **Step 1: 先调研，再写测试**

`LanConnectPowerStateSegment` 与 `LanConnectTargetRefSegment` 的 `{0}`（生物名）从哪里来，本计划**没有确定**。请先读 `LanConnectChatProtocolModels.cs` 里这两个 record 的字段、以及 `LanConnectRoomCombatReferenceResolver.cs` / `LanConnectPowerHoverTipResolver.cs`，确认：

- 生物名是随 segment 传来的，还是要靠 resolver 在本地解析？
- 解析不到时（对局已结束、目标已消失）现有代码怎么兜底？`chat.target_expired` 键已存在，说明有先例。

**把结论写进你的报告。** 若生物名在渲染时不可靠获取，就**只实现 item_ref 的三条**，power/target 两条留空并说明原因——不要为了凑齐而编造一个拿不到真实数据的实现。

- [ ] **Step 2: 写失败测试**

创建 `sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatVerbPhraseTests.cs`，覆盖：

- 三种 `ItemType` 各自产出正确的中英文动词短语。
- 未知 `ItemType`（例如 `"widget"`）走兜底，不抛异常、不产出裸键名。
- 中英两种 locale 都有对应文案（`LanConnectChatLocalizer.Get` 在缺键时返回键名本身，所以断言"结果不等于键名"即可捕获漏配）。
- 若 Step 1 确认 power/target 可做，一并覆盖 `{0}` 的填充。

参照 `sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatLocalizerTests.cs` 的既有写法。

- [ ] **Step 3: 运行确认失败**

- [ ] **Step 4: 实现**

- 在 `LanConnectChatLocalizer.cs` 的 English 与 SimplifiedChinese 两张表里各加对应键。键名沿用既有风格（`chat.` 前缀 + 点分），例如 `chat.verb.shared.card`。**两张表必须同步加，漏一张会让该 locale 显示裸键名。**
- 创建 `LanConnectChatVerbPhrase.cs`，提供 segment → 本地化键的映射，以及一个接受 localizer 与 locale 产出成品短语的方法。

- [ ] **Step 5: 运行确认通过**

- [ ] **Step 6: 提交**

```bash
git add sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatVerbPhrase.cs sts2-lan-connect/Scripts/Lobby/Chat/LanConnectChatLocalizer.cs sts2-lan-connect.Tests/Lobby/Chat/LanConnectChatVerbPhraseTests.cs
git commit -m "feat: phrase shared references as verbs in chat"
```

---

## Task 3：消息行压平（仅 HUD 样式）

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs:1031`（`BuildMessageRow`）
- Create: `sts2-lan-connect.GdUnitTests/Chat/LanConnectFlatMessageRowTests.cs`
- Modify: `sts2-lan-connect.GdUnitTests/Chat/LanConnectRichMessageRenderingTests.cs`（字号断言）

**再次强调：只在 `!UsesLobbyStyle` 时压平。** 大厅侧边栏走原路径，一像素都不许变。

目标结构（HUD 样式）：

- 去掉外层 `PanelContainer` 与 `CreatePanelStyle(message.IsLocal)`。
- 去掉 `metadata` HBox 及其中的时间戳 `Label`（`:1062`）。
- 昵称作为**首个彩色 span** 压入富文本，颜色取 `LanConnectChatNameColor.ForSender(...)`，其后接 `：`，再接 `BuildMessageContent()` 现有的 span 序列。`BuildMessageContent` 本身不改。
- 正文字号 14 → 16。
- 送达失败态从独立行 + 64×34 按钮，收成行尾内联控件。**必须保持 `FocusMode = All`、可由键盘 Tab 到达并回车触发**——视觉收缩不得牺牲可达性。
- 时间戳改为悬停 tooltip（`TooltipText`）；触摸下的长按呈现留到后续期次，本期不做。

- [ ] **Step 1: 写失败测试**

创建 `sts2-lan-connect.GdUnitTests/Chat/LanConnectFlatMessageRowTests.cs`，至少覆盖：

1. **HUD 样式下消息行内不存在时间戳文本。** 注入一条已知时间的消息，断言行内所有 `Label` 的文本都不匹配 `HH:mm` 形状。
2. **HUD 样式下不存在气泡 `PanelContainer`。**
3. **昵称与内容在同一个 `LanConnectRichMessageView` 里**，且昵称 span 的颜色等于 `LanConnectChatNameColor.ForSender(sender, isLocal)`。
4. **大厅侧边栏样式下结构不变**——构造 `ChatVisualStyle = LanConnectChatVisualStyle.LobbySidebar` 的面板，断言气泡 `PanelContainer` 与时间戳 `Label` 仍在。**这条是防越界的守卫，必须有。**
5. **失败消息的重试控件仍可聚焦、仍可触发。**

参照 `LanConnectBasicChatPanelTests.cs` 与 `LanConnectRichMessageRenderingTests.cs` 的既有夹具写法（`EnabledState()`、`AppendConfirmedForTests`、`FindNode<T>`、`ISceneRunner.Load`）。

- [ ] **Step 2: 运行确认失败**

- [ ] **Step 3: 实现**

按上面的目标结构改 `BuildMessageRow`。保持既有的 `row.Name = $"ChatMessageRow{index}"` 命名不变——多处测试与焦点恢复逻辑依赖它。

- [ ] **Step 4: 运行确认通过**

- [ ] **Step 5: 更新因字号改动而变红的既有断言**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

把变红的既有用例逐一过一遍。对每一个，判断它是**因为断言了旧的实现细节**（改断言）还是**因为行为真的坏了**（改实现）。**逐条在报告里说明你的判断依据**——这一步最容易把真回归当成"预期内的断言更新"糊弄过去。

- [ ] **Step 6: 全量验证**

```bash
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
```

GdUnit 期望 0 失败。xUnit 期望仅剩那一个既有失败。

**重点确认这些仍通过**（第 1、2 期的成果，最容易被消息行重构误伤）：
- `LanConnectChatScrollPinningTests` 全部三条
- `Scrolled_up_reader_is_not_yanked_to_bottom_when_the_list_grows`
- `LanConnectChatResolutionTests` 全套（字号 14→16 会让消息行变高，可能撑破布局；若失败，按 spec §6.4 记录的 22px 余量判断是否需要上调 `PanelHeight`，并在报告里写清楚）

- [ ] **Step 7: 提交**

```bash
git add sts2-lan-connect/Scripts/Lobby/Chat/LanConnectBasicChatPanel.cs sts2-lan-connect.GdUnitTests/Chat/
git commit -m "feat: flatten HUD chat message rows into single rich-text lines"
```

---

## 完成判据

- [ ] GdUnit 0 失败
- [ ] xUnit 仅剩 `LanConnectModInventoryBuilderTests` 那一个既有失败
- [ ] 大厅侧边栏的气泡、时间戳、左侧强调条一像素未变，且有守卫测试锁定
- [ ] v0.5.1 线格式未改动——动词措辞纯渲染层
- [ ] 昵称配色在同一房间内对同一 sender 稳定，且六名测试未靠放宽阈值通过

---

## 第 3 期实际落地记录（2026-07-26）

| 提交 | 内容 |
|---|---|
| `2d1c5fc` | `LanConnectChatNameColor`：FNV-1a → 16 色相分桶 + 互质置换，最小色相间隔数学保证 ≥0.0625；避开 `AccentColor` ±0.04 |
| `6cd055c` | `LanConnectChatVerbPhrase` + 中英本地化键；**仅 card/relic/potion 三条** |
| `77c6256` | spec 更正：power/target 措辞不可实现的根因 |
| `47bad39` | 消息行压平（仅 HUD 样式），字号 14→16，昵称并入富文本，时间戳转 tooltip |
| `31976e3` | 修正压平引入的两处缺陷：重试按钮低于触摸下限、失败原因触屏不可见 |

**最终状态：** GdUnit 284 通过 / 0 失败。xUnit 724 通过 / 1 跳过 / 1 失败（既有的 `LanConnectModInventoryBuilderTests`）。

### 两处需要后续注意的决定

1. **动词措辞只覆盖 item 三类。** power/target 因线格式不携带生物身份而不可实现，详见 spec §5.5.1。已有测试锁定其返回 `null`，防止后续补上一个拿不到真实数据的实现。
2. **区分中英分隔符的逻辑写在 `LanConnectBasicChatPanel` 内部**（`IsChineseLocale()` / `PlainMessageSeparator()`），而非 `LanConnectChatLocalizer`。当时的取舍是避免为两个标点新增本地化键并动到 `ExpectedKeys` 全量快照。代价是 locale 判断逻辑逸出了 localizer——若后续再出现同类需求，应当收回 localizer 统一处理。

### 压平引入又修掉的两个缺陷，值得记住的教训

`47bad39` 把重试按钮设成 `(44, 28)` 并把失败原因整个移进 tooltip。前者违反第 2 期刚建立的 44px 触摸下限——因为 `LanConnectBasicChatPanel` 有自己的 `CreateButton`，没接 `EnsureTouchTarget`；后者在安卓上直接让"为什么发送失败"不可见。

**同一期建立的契约，在下一个任务里就被绕过了。** 契约若只存在于文档和一个未被普遍调用的 helper 里，就挡不住这类事。§6.4 的控件清单即为此而记。
