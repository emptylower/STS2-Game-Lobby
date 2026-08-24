# v0.6.0-alpha.9 修复方案：RitsuLib 先加载导致大厅不显示

- 编写时间：2026-08-23
- 仓库：`/Users/mac/Desktop/STS2-Game-Lobby`，基线 HEAD `409d7aa`
- 目标客户端版本：`0.6.0-alpha.9`
- lobby-service：保持 `0.6.0-alpha.6`，本方案不改服务端 API / DTO / 协议版本

---

## 0. 详细调查报告位置

> **本方案的全部结论、复现证据与机制推导，出自：**
>
> **`docs/STS2_LAN_CONNECT_ALPHA8_LOBBY_MISSING_RCA_ZH.md`**
> （仓库内绝对路径：`/Users/mac/Desktop/STS2-Game-Lobby/docs/STS2_LAN_CONNECT_ALPHA8_LOBBY_MISSING_RCA_ZH.md`）

阅读顺序建议：先读调查报告第 2、3、6 节（复现矩阵 / Harmony 机制 / 「字节上是空操作」的论证），
再读本方案。本方案不重复论证，只在每项修改里回指报告章节。

配套复现 harness（本次调查产出，尚未入库，建议按 F4 搬进仓库）：
- `scratchpad/repro`  —— 补丁顺序矩阵，秒级复现 `InvalidProgramException`
- `scratchpad/probe`  —— 证明 Harmony 读回补丁方法时丢失泛型实例化
- `scratchpad/bridge` —— 证明 detach → patch → re-attach 可行

---

## 1. 一句话根因

RitsuLib 用**声明在泛型类型上**的补丁方法（`SerializePatch<TMessage>.Postfix`）补了
`NetMessageBus.SerializeMessage<LobbyBeginRunMessage>`；Harmony 2.4.2 把已应用补丁按
`(moduleGUID, metadataToken)` 存盘，读回时会把它解析成**开放泛型**，导致**任何人第二次**
补同一方法都会生成非法 IL 抛 `InvalidProgramException`。
LAN Connect 把这个补丁列为「必需」，失败即 fail-closed 抛出，`Entry.Init` 停在第 6/10 阶段，
第 9 阶段的大厅 UI 从未安装。加载顺序由 `settings.save` → `mod_settings.mod_list` 决定，
更新 MOD 会把本 MOD 排到最后，因而"随机"复发。

（详见报告第 3、4 节。）

---

## 2. 目标与非目标

### 目标
1. RitsuLib 无论先于还是后于本 MOD 初始化，大厅 UI 都必须出现。
2. 协议补丁不完整时，必须仍然 fail-closed 地**拒绝联机**，但**不得**连带杀死整个 MOD。
3. 线上字节格式零变化；现有 golden vector 全部保持通过。
4. 这一类「外部 MOD 抢占同一 Harmony 目标」的故障要能被测试拦住，而不是靠用户日志考古。

### 非目标
- 不恢复 RC4 的 RitsuLib 私有 postfix detach/restore 桥
  （设计文档 `docs/superpowers/specs/2026-08-13-v0-6-dual-protocol-design.md:118/450/567` 明令禁止，
  且 `LanConnectSerializationPatchesCompatibilityTests.cs:82-101` 有源码级禁止清单）。
- 不改 manifest `dependencies`（报告第 8 节 F：会锁死失败方向 + 硬依赖，双重陷阱）。
- 不改服务端、不改协议版本、不动 Steam Workshop 二进制。
- 不修 RitsuLib（但 F6 会向上游提问题）。

---

## 3. 修复项

### F1（必须）把 begin-run message-bus boundary 从「必需」降级为「尽力而为」

- **文件**：`sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs`
- **当前行为**：
  - `:101-105` 调用 `TrySafeBeginRunPrefixPatch`
  - `:152-165` 失败时 `_failedCount++`
  - `:113-114` `requiredWirePatchCount = Targets.Count + (boundary == null ? 0 : 1)`
  - `:115-126` `_patchedCount != required || _failedCount != 0` → `RollBackIncompletePatches()` → `throw`
- **改法**：把 boundary 补丁的成败移出必需集合。
  - `requiredWirePatchCount` 恒等于 `patchPlan.Targets.Count`（即 6 个 transpiler）。
  - boundary 成功/失败都不参与 `_patchedCount` / `_failedCount` 的必需判定，
    单独用一个 `_beginRunBoundaryState` 字段记录 `patched | skipped_android | skipped_foreign_owner | failed`。
  - 失败时打一条结构化 `patch_diag`：目标全名、异常类型与指纹、`外部 owner 列表`。
  - 现有汇总日志 `:129-135` 增加 `beginRunMessageBusBoundary=<state>`。
- **为什么安全**（报告第 6 节，已在代码中核实）：
  - `LanConnectProtocolProfiles.cs:24-28` + `LanConnectConstants.cs:27,29`：
    `TailV1 → 3 bit`（与原版逐字节相同），`Compat4x5V1 → 5 bit`。
  - `compat_4_5_v1` 硬性拒绝 RitsuLib，客户端与服务端双向强制：
    `Scripts/Protocol/LanConnectProtocolFailure.cs:30`、
    `sts2-lan-connect.Tests/Lobby/LanConnectDirectJoinFlowTests.cs:69,85`、
    `lobby-service/src/protocol-compatibility.integration.test.ts:70`。
  - ⇒ 该补丁**只可能在 RitsuLib 在场时失败**，而 RitsuLib 在场必然是 TailV1，
    TailV1 下它的输出与原版逐字节相同。跳过它按构造不改变任何线上字节。
- **线上字节影响：无。**
- **实现约束**：用纯 `try/catch` 降级，**禁止引入 `Harmony.GetPatchInfo`**，
  否则会撞上 `LanConnectSerializationPatchesCompatibilityTests.cs:96` 的禁止清单。
  如果确实需要「外部 owner 列表」用于诊断，请复用
  `LanConnectProtocolPatchDispatcher.InspectPatchOwners`（`:162-191`，已在生产代码中）
  而不是在 `LanConnectSerializationPatches.cs` 里新写。
- **风险**：低。唯一理论风险是 Compat4x5V1 下 JIT 内联导致 3-bit 回归——
  但该路径下补丁必然成功（无人抢占），行为不变。

---

### F2（必须）桌面 tail plan 去闭合泛型

- **文件**：
  - `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs:105`
    （`Apply` → `ResolvePatchPlan(..., OperatingSystem.IsAndroid())`）
  - `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatchPlan.cs:115-128`
    （`ResolvePatchPlan` 的 `isAndroid ? Android : Desktop` 分支）
  - 同文件 `:46-47`（profile 常量）、`:60`（`expectedSteps = AndroidProfile ? 15 : 30`）、
    `:77`（`ValidateAndroidMethodsAreConcrete` 只对 Android 生效）
- **当前行为**：桌面用 `desktop_generic_v1`，30 步中 **27 步是闭合泛型**
  （9× `NetMessageBus.SerializeMessage<T>`、9× `NetHostGameService.SendMessage<T>`、
  9× `SendMessageToClientInternal<T>`）。其中 `InitialGameInfoMessage` 与 `LobbyBeginRunMessage`
  两个目标与 RitsuLib 重叠。
- **改法**：把现有 `android_non_generic_v2`（15 步，全非泛型）提升为**所有平台默认**。
  - 建议把 profile 常量改名为中性名（例如 `non_generic_v2`），保留 `desktop_generic_v1`
    作为可显式选择的回退分支（便于 A/B 对照与紧急回滚），但默认不再走它。
  - `ValidateAndroidMethodsAreConcrete()` 改为对新默认 profile 无条件生效，并断言
    `GenericTargetCount == 0`。
  - 若不愿一次到位，**最小变体**：仅在检测到 RitsuLib 时切到非泛型计划。
    但这样会留下「两条桌面路径」的长期维护成本，且 F4 的回归测试要覆盖两条，**不推荐**。
- **为什么必须**：报告第 7.1 节。只做 F1 的话，失败点会平移到 tail plan 第 1 步
  `tail.serialize.initial_game_info` —— 我已在 harness 上实测该目标同样 `FAIL`，
  症状与现在完全一样。
- **线上字节影响：预期为零，且已有现成证据。**
  `sts2-lan-connect.GdUnitTests/Protocol/LanConnectFullMessageGoldenVectorRuntimeTests.cs:32-48`
  已经用 `[TestCase(true)] / [TestCase(false)]` 把**同一套 golden vector**
  分别跑过 Android 计划与桌面计划，在真实 Godot runtime + 真实 `sts2.dll` 上逐字节比对。
  也就是说「非泛型计划与泛型计划字节等价」这件事**已经被现有测试证明过**。
- **风险**：中。主要在 Android 与桌面的 JIT/内联行为差异，以及
  `LanConnectTailMessageRuntime` 的 writer 绑定在桌面并发场景下的表现。
  由 F5 的验证矩阵兜住。

---

### F3（必须）协议补丁失败不再杀死整个 MOD

- **文件**：
  - `sts2-lan-connect/Scripts/Entry.cs:27`（stage 6 `MultiplayerCompatibility`）
  - `sts2-lan-connect/Scripts/LanConnectMultiplayerCompatibility.cs:29-33`（catch 后 rethrow）
  - `sts2-lan-connect/Scripts/Diagnostics/LanConnectStartupDiagnostics.cs:182`
    （`ExceptionDispatchInfo.Capture(exception).Throw()`）
- **当前行为**：stage 6 抛出 → `RunStage` 原样重抛 → `Entry.Init` 中止 →
  stage 7-10（gameplay patches、scene-ready hooks、**大厅 UI**、房间聊天浮层）全部不执行。
- **改法**：引入**降级模式**，而不是中止。
  - `Entry.Init` 对 `MultiplayerCompatibility` 阶段单独捕获：失败时记录
    `LanConnectDegradedMode.Reason`，**继续执行 stage 7-10**。
  - 降级模式下 `LanConnectLobbyRuntime` 照常安装 UI，但建房 / 加入入口置为不可用，
    并直接显示原因文案（新增一条本地化 key，例如
    `protocol_patch_conflict` → 「与其他 MOD 的补丁冲突，联机功能不可用。
    请在 MOD 菜单中关闭 RitsuLib、启动一次游戏后再重新开启。」）。
  - `RunStage` 增加一个「允许降级」的重载，或由调用方决定是否 rethrow；
    不要把 `RunStage` 改成默认吞异常。
- **依据**：设计文档要求的 fail-closed 是**回滚补丁集并拒绝联机**，
  没有任何一条要求中止 MOD 初始化或移除大厅 UI。现在的全有全无是线性 stage 列表直接 rethrow 的实现产物
  （报告第 5 节）。
- **线上字节影响：无。** 降级模式下根本不允许进入联机流程。
- **风险**：低-中。必须确保降级模式**真的**挡住建房/加入，否则会把
  「不能联机」变成「用错误协议联机」，这比现状更糟。
  这一条要有专门测试（见 F4）。

---

### F4（必须）回归测试：外部 owner 抢占同一 Harmony 目标

- **文件**：新增到 `sts2-lan-connect.ProtocolPlanTests/`
  （建议 `LanConnectForeignPatchOwnerTests.cs`），可参考现有
  `LanConnectTailPatchFailureTests.cs:101,165` 已有的 `Harmony.GetPatchInfo` 外部 owner 断言写法。
- **用例**：
  1. `Generic_declared_foreign_postfix_does_not_break_plan_application`
     —— 先用一个**声明在泛型类型上**的外部 postfix（复刻 `SerializePatch<TMessage>` 形状）
     补 `NetMessageBus.SerializeMessage<LobbyBeginRunMessage>` 与 `<InitialGameInfoMessage>`，
     再执行本 MOD 的完整 patch plan，断言：不抛异常、`GenericTargetCount == 0`、
     外部 owner 的补丁仍在。
  2. `Serialization_boundary_failure_is_not_fatal`
     —— 同样前置条件下断言 `LanConnectSerializationPatches.Apply()` 不抛，
     且 6 个必需 transpiler 全部就位。
  3. `Degraded_mode_blocks_host_and_join`
     —— 断言降级模式下建房与加入均被拒绝，且拒绝码可被 UI 映射。
  4. 把 `scratchpad/probe` 的断言固化为一条：
     `Harmony_patch_info_roundtrip_loses_generic_instantiation`
     —— 这条是**环境断言**，Harmony 将来修了这个行为时会红，提醒可以简化 F2。
- **注意**：这些用例必须放在 `ProtocolPlanTests`（不依赖 Godot runtime），
  才能进 `scripts/verify-release.sh` 的默认门禁。

---

### F5（建议）诊断增强

- **文件**：`sts2-lan-connect/Scripts/Diagnostics/LanConnectStartupDiagnostics.cs`
  与 `LanConnectProtocolPatchDispatcher.cs:162-191`
- **改法**：
  - 在 `session/begin` 附近新增一条 `event=mod_load_order` 事件，
    记录本 MOD 与 RitsuLib / BaseLib 的相对加载顺序（可由已加载程序集与
    `Harmony.GetAllPatchedMethods()` 的 owner 推断，不读取任何私有 API）。
  - 每个 patch 失败事件附带该目标当前的**外部 owner 列表**（复用 `InspectPatchOwners`）。
  - 降级模式在普通游戏日志里打一条醒目的单行摘要，便于用户直接截图反馈。
- **理由**：这次故障 alpha.8 已经有完整诊断子系统，但用户看到的仍然只是「没有大厅」，
  而定位仍然要靠比对三份日志的 MOD 排序。把「顺序」和「谁抢了这个方法」写进日志，
  下次是秒级定位。
- **线上字节影响：无。**

---

### F6（建议）向 RitsuLib 上游提修复

`tem/STS2-RitsuLib/src/Networking/MessageExtensions/RitsuNetMessageBusTailPatches.cs`
里的 `private static class SerializePatch<TMessage>` 改成非泛型声明类型
（每个消息类型一个具体 patch 类，或一个非泛型类 + 具体签名）。

harness 已实测：**同签名但声明在非泛型类上的 postfix 不会毒化目标**，
两个 MOD 任意顺序都能共存。这对 RitsuLib 几乎零成本，并且能同时消除
CHANGELOG 里记录过的反方向 Android Mono assert。

这是唯一的根治项，但不能作为 alpha.9 的前置依赖 —— F1/F2/F3 必须让本 MOD 在
上游不改的情况下也能正常工作。

---

## 4. 需要同步修改的现有测试

| 文件:行 | 现有断言 | 因哪项修改 | 处理 |
|---|---|---|---|
| `sts2-lan-connect.ProtocolPlanTests/LanConnectTailPatchPlanTests.cs:15` | `plan.Profile == DesktopProfile` | F2 | 改为新默认 profile |
| 同上 `:20` | `plan.GenericTargetCount > 0` | F2 | 改为 `== 0` |
| `sts2-lan-connect.Tests/Packaging/LanConnectPackageContentTests.cs:311-312` | 打包产物文本须含两个 profile 字符串 | F2 | 按新命名更新 |
| `sts2-lan-connect.GdUnitTests/Protocol/LanConnectFullMessageGoldenVectorRuntimeTests.cs:32-48` | 两个 `TestCase` 分别跑两套计划 | F2 | **保留**，它正是 F2 的字节等价证据 |
| `sts2-lan-connect.Tests/Patches/LanConnectSerializationPatchesCompatibilityTests.cs:69-79` | boundary 仅在 Android 关闭 | F1 | **不改**（F1 保留该判定，只改「是否必需」） |
| 同上 `:82-101` | 禁止 detach 桥标记，含 `Harmony.GetPatchInfo` | F1 | **不改**，F1 必须绕开它 |

---

## 5. 验证方案

### 5.1 自动门禁
```bash
RITSULIB_ASSEMBLY=<official-v0.5.14-dll> ./scripts/verify-release.sh
```
必须包含：`ProtocolPlanTests` 全绿（含 F4 新用例）、
`LanConnectFullMessageGoldenVectorRuntimeTests` 两个 `TestCase` 均逐字节通过。

### 5.2 强制失败顺序的真机复现
编辑 `settings.save` 的 `mod_settings.mod_list`，把 `STS2-RitsuLib` 移到
`sts2_lan_connect` **之前**，然后冷启动。

| 场景 | 修复前 | 修复后期望 |
|---|---|---|
| RitsuLib 先 / 本 MOD 后 | `applied=6/7`，无大厅 | 全部补丁就位，大厅正常，可联机 |
| 本 MOD 先 / RitsuLib 后 | 正常 | 保持正常（不得回归） |
| 无 RitsuLib | 正常 | 保持正常 |
| RitsuLib + compat 模式 | 拒绝 | 仍以 `ritsulib_not_allowed_in_compat_mode` 拒绝 |
| 人为注入不可恢复的补丁失败 | 整个 MOD 死 | 大厅可见、建房/加入被挡、UI 显示原因 |

### 5.3 端到端
无 Ritsu 房 / 全员 Ritsu 房，各在**两种加载顺序**下各跑一次：
建房 → 加入 → 开始游戏 → 自动 SL 重开 → 断线重连。
macOS + Android 各一轮（Android 计划本次变成共用路径，必须复测）。

---

## 6. 发布

- 客户端源码、manifest、打包断言、GitHub 文档升到 `0.6.0-alpha.9`。
- release notes 需明确写：本版修复「安装/更新后大厅不显示」，
  并说明**旧版的绕过办法（关 RitsuLib 再开）在新版不再需要**。
- 记录 ZIP/DLL/PCK 的 SHA-256，创建 `v0.6.0-alpha.9` GitHub Pre-release。
- lobby-service 不动。Steam Workshop 二进制在 alpha.9 真机验收通过前不更新。

---

## 7. 回滚

F2 风险最高。把 `desktop_generic_v1` 分支保留在代码里（只是不再是默认），
一旦桌面出现字节或时序问题，改一处 profile 选择即可退回旧路径，
此时 F1 + F3 仍然生效 —— 即「大厅至少不会消失」这个底线不依赖 F2。

---

## 8. 给用户的临时说明（alpha.9 发布前）

> 只关掉 **RitsuLib**（不要关联机大厅 MOD）→ 启动一次游戏到主菜单 → 退出 →
> 重新开启 RitsuLib → 再启动，大厅入口就会出现。
>
> 注意：关掉再打开「联机大厅」MOD 本身会让问题固定下来，请不要这么做。

仍然失败的用户请收集：`godot.log`、`settings.save` 的 `mod_settings` 段、
以及 `user://sts2_lan_connect/diagnostics/<utc>-<session>/`。

---

## 9. 遗留风险与待确认

1. **RitsuLib 0.5.14 源码未获取**（仓库只 vendored 0.5.12）。
   已装程序集在
   `~/Library/Application Support/Steam/steamapps/workshop/content/2868840/3747602295/lib/0.111.0/STS2-RitsuLib.dll`。
   **动手前建议先反编译比对** `RitsuNetMessageBusTailPatches` 与 `HarmonyInitSetterCompat`，
   确认 0.5.14 没有新增闭合泛型目标 —— 这会影响 F2 的覆盖面判断。
2. 桌面 18 个 `SendMessage<T>` / `SendMessageToClientInternal<T>` 目标 RitsuLib 0.5.12 不碰，
   0.5.14 未知（同上）。
3. JIT 是否真的会内联 `LobbyBeginRunMessage.Serialize`（71 IL 字节，低于 CoreCLR 100 字节候选阈值）
   未实测 —— 本机无法在游戏进程外执行 `PacketWriter`（GodotSharp 原生初始化 SIGSEGV）。
   这只影响「F1 是否可以更激进地直接删掉 boundary」，不影响 F1 当前设计的安全性。
4. F3 的降级模式需要一次产品决策：降级时大厅是「可见但联机置灰」还是「可见且可浏览房间列表但不能加入」。
   本方案按前者写，若要后者需重新界定 fail-closed 边界。
