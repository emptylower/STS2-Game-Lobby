# v0.6.0-alpha.8 「大厅不显示」根因调查报告

- 调查时间：2026-08-23
- 仓库：`/Users/mac/Desktop/STS2-Game-Lobby`，HEAD `409d7aa`
- 输入证据：用户日志 `godot (2).log`（失败，alpha.8 + RitsuLib 0.5.14）、`godot.log` / `godot (1).log`（成功，alpha.1 + RitsuLib 0.5.12）
- 本报告只做诊断与方案设计，未修改任何产品代码。

---

## 1. 结论

**大厅不显示不是 alpha.8 引入的新缺陷，而是 v0.6.0-alpha.1 起就存在的 MOD 加载顺序竞态。**

当 RitsuLib 在 LAN Connect 之前初始化时，闭合泛型方法
`NetMessageBus.SerializeMessage<LobbyBeginRunMessage>` 已经被 RitsuLib 打上补丁。
LAN Connect 随后对同一个方法调用 `Harmony.Patch`，Harmony 抛
`InvalidProgramException`，7 个「必需」wire 补丁只成功 6 个，
`LanConnectSerializationPatches.Apply` 按 fail-closed 抛出，
`Entry.Init` 在第 6/10 阶段中止，第 9 阶段的大厅 UI 从未安装。

游戏仍然打印 `Finished mod initialization`，MOD 列表里也照常显示
`STS2 LAN Connect [sts2_lan_connect] (0.6.0-alpha.8)`，所以玩家看到的现象就是
「MOD 装好了，但没有大厅」。

---

## 2. 已在本机完整复现（不是推断）

用真实 `sts2.dll` + 游戏自带 `0Harmony.dll` 2.4.2.0 写了一个最小 harness
（`scratchpad/repro`，不在仓库内），只复刻两个 MOD 的补丁形状，**完全不加载 RitsuLib 程序集**：

| RitsuLib 侧形状 | LAN Connect 侧形状 | 顺序 | 结果 |
|---|---|---|---|
| `SerializePatch<TMessage>.Postfix`（泛型声明类型） | prefix，具体 message 参数 | lan-first | **OK** |
| 同上 | 同上 | ritsu-first | **FAIL** `InvalidProgramException` |
| 同上，去掉 `ref byte[] __result` | 同上 | ritsu-first | FAIL |
| 同上，去掉泛型 `message` 参数 | 同上 | ritsu-first | FAIL |
| **非泛型声明类型**，签名完全相同 | 同上 | ritsu-first | **OK** |
| 无 RitsuLib 补丁 | 同上 | — | OK |

再固定 RitsuLib 形状、改变 LAN Connect 侧形状：

| LAN Connect 侧 | 结果（ritsu-first） |
|---|---|
| prefix（具体参数 / 无 message 参数 / void 不跳过 / 泛型声明类型） | 全部 FAIL |
| transpiler | FAIL |
| postfix | FAIL |
| **改打非泛型的 `LobbyBeginRunMessage.Serialize`** | **OK** |

失败时的异常与用户日志逐帧一致：

```
System.InvalidProgramException: Common Language Runtime detected an invalid program.
   at HarmonyLib.PatchFunctions.UpdateWrapper(MethodBase original, PatchInfo patchInfo)
   at HarmonyLib.PatchProcessor.Patch()
```

**结论：LAN Connect 自己的补丁写法完全无关；触发条件只有一个——
目标闭合泛型方法上已经存在一个「声明在泛型类型上」的外部补丁方法。**

---

## 3. 精确机制（已观测，非推断）

Harmony 2.4.2 把已应用的补丁以 `(moduleGUID, metadataToken)` 存进 shared state，
再次 `Patch()` 同一方法时用 `Module.ResolveMethod(token)` 读回、重建 wrapper。
对**声明在泛型类型上**的补丁方法，这个往返会丢失类型实例化。探针实测：

```
交给 Harmony 的：
  SerializePatch`1[LobbyBeginRunMessage].Postfix
  ContainsGenericParameters = False   param[1] = LobbyBeginRunMessage
Harmony 读回来的：
  SerializePatch`1[TMessage].Postfix
  IsGenericTypeDefinition = True
  ContainsGenericParameters = True    param[1] = TMessage
（对照：声明在非泛型类型上的同签名补丁，读回完全正确）
```

第一次 `Patch()` 用的是内存里的 `HarmonyMethod`，所以 RitsuLib 自己永远成功。
**第二次**——无论是谁、无论 prefix/postfix/transpiler——重建 wrapper 时会对着一个
带未绑定泛型参数的方法发射调用，生成非法 IL，CLR 在 `UpdateWrapper` 抛
`InvalidProgramException`。

也就是说：`RitsuNetMessageBusTailPatches.SerializePatch<TMessage>` 这个写法
**把它补过的方法「毒化」了——之后任何 MOD 再补这个方法都会死。**

对照实验：LAN Connect 去补一个 RitsuLib **没有**补过的闭合泛型
（`SerializeMessage<PlayerJoinedMessage>`）→ **成功**。
所以 M2「RitsuLib 的 `HarmonyInitSetterCompat` 反射导入器 meta-patch 破坏了后续所有 IL 生成」
这个假设不成立：M1 单独就足以完整复现，且失败日志里 RitsuLib meta-patch 之后
LAN Connect 的 6 个 transpiler 补丁全部成功。

---

## 4. 为什么顺序会变，以及用户的绕过为什么有效

反编译 `MegaCrit.Sts2.Core.Modding.ModManager` 得到（子代理证据，file:line 见调查记录）：

- 加载顺序 = 对 manifest `dependencies` 做 Kahn 拓扑排序，**同层用持久化顺序的下标做优先级**。
- 持久化顺序存在 `settings.save` → `mod_settings.mod_list`（有序数组 `{id, source, is_enabled}`）。
- **不在 `mod_list` 里的 MOD 优先级取 `999999999`**，即新装/新识别的 MOD 一律排到最后。
- **被禁用的 MOD 会被追加到列表末尾**，而且整份顺序每次启动都会回写 `settings.save`。

由此：

| 现象 | 解释 |
|---|---|
| 「每次装完都要关一下再开」 | 更新 MOD 后条目变化触发重排，LAN Connect 作为新条目排到 RitsuLib 后面 → 必坏。**这是在已有 MOD 档上首次安装的默认结果，不是小概率事件。** |
| 「把 rit 关掉打开游戏，再打开 rit 重启就好了」 | 禁用会把 RitsuLib 降到列表末尾并回写，下次启动 RitsuLib 排在 LAN Connect 之后 → LAN Connect 先补丁 → 成功。 |
| 也有用户一直正常 | 他们的 `mod_list` 里 LAN Connect 恰好在 RitsuLib 前面。 |
| 本地一直复现不了 | 本机 `settings.save` 的 `mod_list` 第 0 项就是 `sts2_lan_connect`（已确认）。 |

⚠️ **反向提醒**：关掉再打开「联机大厅」MOD 本身会把 LAN Connect 降到末尾，
只会让问题固化。正确口令是**只关 RitsuLib**。

日志实证：

| 日志 | 顺序 | 结果 |
|---|---|---|
| `godot (2).log` | `7: RitsuLib` … `26: STS2 LAN Connect` | `applied=6/7, failed=1` |
| `godot.log` | `20: STS2 LAN Connect` … `21: RitsuLib` | `applied=7, failed=0` |
| `godot (1).log` | 同上 | `applied=7, failed=0` |

---

## 5. 这是 v0.6 设计决策留下的缺口（回归）

- `0.5.6-rc4`（`67fea8a`）**已经修过这个问题**：`DetachRitsuBeginRunPostfix` 会先
  `Unpatch` 掉 RitsuLib 的 postfix、装上自己的 prefix、再手工调用对方的 postfix，
  回滚时还原。当时的代码注释写得很清楚：
  `// RitsuLib patches this closed generic before LAN Connect loads, which can inline the vanilla 3-bit body.`
- `d2f82e3`（Tail v1 core）删掉了整套 bridge（-164 行），注释也一并删除。
- 设计文档 `docs/superpowers/specs/2026-08-13-v0-6-dual-protocol-design.md:20` 把
  「反射、卸载和恢复 RitsuLib 私有 postfix」列为**引入** `InvalidProgramException` 的原因之一，
  第 118/450/567 行明令禁止恢复该桥；
  `LanConnectSerializationPatchesCompatibilityTests.Production_serialization_source_contains_no_private_Ritsu_composition_bridge`
  用源码字符串匹配把 `DetachRitsuBeginRunPostfix`、`Harmony.GetPatchInfo` 等标记列为禁止项。

设计文档删桥的论证是（第 464 行）：`compat_4_5_v1` 明确禁止 RitsuLib，所以两者不共存，桥不需要。
**这个论证本身是对的，但只推导出「桥可以删」，没有推导出「补丁可以留着且仍然是必需项」。**
桥被删掉了，撞车的补丁却原样保留，并且仍然计入 `requiredWirePatchCount`——这就是缺口。

---

## 6. 关键事实：在会撞车的场景里，这个补丁在字节上是空操作

- `LanConnectProtocolProfiles.GetActiveLobbyListBitWidth()`：
  `TailV1 → VanillaLobbyListBits = 3`，`Compat4x5V1 → ExtendedLobbyListBits = 5`
  （`sts2-lan-connect/Scripts/LanConnectProtocolProfiles.cs:24-28`，`LanConnectConstants.cs:27,29`）
- `SerializeBeginRunAtMessageBusPrefix` 逐字节复刻原版
  `Reset / WriteByte(id) / WriteULong(senderId) / WriteList(players, bits) / WriteString(seed) / WriteList(modifiers) / WriteString(act1)`，
  只把位宽换成动态值。
- 所以在 **TailV1（唯一允许 RitsuLib 存在的档位）下，该 prefix 的输出与原版逐字节相同**；
  只有在 **Compat4x5V1** 下才写 5 bit——而 `compat_4_5_v1` 明确禁止 RitsuLib。

**推论：当 RitsuLib 持有该方法时，这个 prefix 的字节贡献恰好为零。
在这种情况下跳过它，按构造是字节安全的。**

它唯一的功能价值是防止 JIT 把 `LobbyBeginRunMessage.Serialize` 内联进闭合泛型实例，
从而绕过位宽 transpiler——而这只在位宽 ≠ 原版（即 Compat4x5V1）时才有意义。
（补充数据：`LobbyBeginRunMessage.Serialize` IL 71 字节、无 `AggressiveInlining`，
低于 CoreCLR 默认 100 字节内联候选阈值，所以内联风险真实存在但不确定。）

---

## 7. 影响面

### 7.1 修好 begin-run 还不够 —— 下一个目标会立刻撞上

桌面 tail plan（`LanConnectTailMessagePatchPlan.ResolveDesktopPatchPlan`）30 步里
**27 步是闭合泛型**：9× `NetMessageBus.SerializeMessage<T>`、
9× `NetHostGameService.SendMessage<T>`、9× `SendMessageToClientInternal<T>`。

RitsuLib 用 `SerializePatch<TMessage>` 补的三个类型是
`InitialGameInfoMessage`、`StateDivergenceMessage`、`LobbyBeginRunMessage`。
与 LAN Connect 的 9 个类型**重叠 2 个**：`InitialGameInfoMessage`、`LobbyBeginRunMessage`。

已实测：把 harness 换成补 `SerializeMessage<InitialGameInfoMessage>` → **同样 FAIL**。
所以只降级 begin-run boundary 的话，失败点会平移到
`tail.serialize.initial_game_info`（tail plan 的第 1 步），症状完全一样。

### 7.2 玩家实际失去的东西

`Entry.Init` 第 6 阶段抛出后，第 7–10 阶段全部没跑：
gameplay patches（难度/篝火/商人/宝箱/容量）、save-manager LAN 身份守卫、
BaseLib 未知角色存档守卫、join-screen 自动加入抑制、scene-ready hooks、
大厅 UI、房间聊天浮层。成功哨兵 `sts2_lan_connect initialized with ready hooks.` 在失败日志中为 0 次。

### 7.3 版本暴露范围

`SerializeBeginRunAtMessageBusPrefix` 从 `0.5.6-rc3` 就存在，bridge 在 `d2f82e3` 被删。
因此 **alpha.1 ~ alpha.8 全部暴露**，alpha.7 同样。alpha.8 只是恰好赶上用户量与更新触发的重排。

### 7.4 Android

Android 已经跳过 begin-run boundary，且 `android_non_generic_v2` 的 15 个目标**全部非泛型**，
天然免疫本问题。RitsuLib 唯一与之重叠的是非泛型的 `NetMessageBus.TryDeserializeMessage`，
其补丁方法声明在非泛型类 `RitsuNetMessageBusTailDeserializePatch` 上 —— 实测这种形状不会毒化目标。

---

## 8. 修复方案

### 必须做

**A. 把 begin-run message-bus boundary 从「必需」降级为「尽力而为」**
- 文件：`sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs`
- 改法：`TrySafeBeginRunPrefixPatch` 失败时不计入 `_failedCount`，且不把它算进
  `requiredWirePatchCount`；失败时打一条明确的 `patch_diag`（含目标、外部 owner、异常指纹）。
- 为什么安全：见第 6 节 —— 只有 RitsuLib 在场时才会失败，而 RitsuLib 在场必然是 TailV1，
  TailV1 下该 prefix 与原版逐字节相同；Compat4x5V1 禁止 RitsuLib，那里没人抢这个方法，补丁必然成功。
- **线上字节影响：无。**
- 实现提示：用纯 `try/catch` 降级，**不要引入 `Harmony.GetPatchInfo`**，
  否则会撞上现有禁止清单测试（`LanConnectSerializationPatchesCompatibilityTests.cs:92-96`）。

**B. 桌面 tail plan 去闭合泛型**
- 文件：`sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatchPlan.cs`
- 改法：把 alpha.8 已实现并已过测试的 `android_non_generic_v2` 提升为所有平台的默认计划
  （或至少在检测到 RitsuLib 时选用），去掉 27 个闭合泛型目标。
- 为什么必须：没有这一条，A 只会把失败点平移到 `tail.serialize.initial_game_info`。
- **线上字节影响：需要用现有 golden vector 逐字节验证**。Android 已经跑通同一套计划，
  风险主要在「桌面 JIT 内联行为与 Mono 不同」这一点上，必须用 golden vector 兜住。
- 风险：这是本方案里最大的一块；如果不愿在 alpha.9 就动，退而求其次是
  「仅在检测到 RitsuLib 时切换到非泛型计划」，把变更面收窄。

**C. 解耦「协议补丁失败」与「整个 MOD 死掉」**
- 文件：`sts2-lan-connect/Scripts/Entry.cs`、`Scripts/Diagnostics/LanConnectStartupDiagnostics.cs`
- 改法：`multiplayer_compatibility` 阶段失败时进入**降级模式**——继续安装大厅 UI 与聊天，
  但禁止建房/加入，并在大厅入口直接显示原因（例如「与 RitsuLib 的补丁顺序冲突，请重启游戏」）。
- 依据：设计文档里的 fail-closed 要求是**回滚补丁集、拒绝联机**，
  没有任何一条要求中止 MOD 初始化或移除大厅 UI。现在的全有全无是线性 stage 列表直接 rethrow 的实现产物。
- **线上字节影响：无。**

### 建议做

**D. 与 RitsuLib 作者协调根治**
`RitsuNetMessageBusTailPatches.SerializePatch<TMessage>` 改成非泛型声明类型
（每个消息类型一个具体 patch 类，或用一个非泛型类 + 具体签名）。
实测同签名但非泛型声明的 postfix **不会**毒化目标。这对他们几乎零成本，
且能同时解决 CHANGELOG 里记录过的反方向 Android Mono assert。

**E. 顺序自检 + 明确诊断**
启动时检测本 MOD 关心的目标上是否已存在外部 owner，写进 `patch_diag`，
并把「LAN Connect 与 RitsuLib 的相对加载顺序」一并记录，让下次不用靠日志考古。

**F. 不要在 manifest 里声明依赖**
`sts2_lan_connect.json` 加 `"dependencies": [{"id": "STS2-RitsuLib"}]` 是双重陷阱：
拓扑排序会把依赖方排在**被依赖方之后**——正好锁死失败方向；
而且缺失依赖会让 MOD 直接 `ModLoadState.Failed`，没装 RitsuLib 的用户全部打不开。

---

## 9. 验证方案

1. **单元/集成回归（能拦住这次的那一条）**
   在 `sts2-lan-connect.ProtocolPlanTests` 加用例：先用一个**声明在泛型类型上**的
   外部 owner postfix 补 `NetMessageBus.SerializeMessage<LobbyBeginRunMessage>` 与
   `<InitialGameInfoMessage>`，再执行本 MOD 的完整 patch plan，断言
   （a）不抛异常，（b）大厅 UI 安装路径仍然可达，（c）线上字节仍与 golden vector 一致。
   现有 `LanConnectTailPatchFailureTests` 已有外部 owner 保留断言，可直接扩展。
2. **本机 harness**：`scratchpad/repro` 与 `scratchpad/probe` 已能在秒级复现与证伪，
   可直接搬成仓库内测试。
3. **真机复现**：编辑 `settings.save` 的 `mod_settings.mod_list`，把 `STS2-RitsuLib`
   移到 `sts2_lan_connect` 前面，启动即可稳定复现；调换回来即恢复。
4. **端到端**：无 Ritsu / 全员 Ritsu 两种房间，各在两种加载顺序下各跑一次。

---

## 10. 现在可以告诉用户什么（无需新版本）

**绕过方法（请照这个顺序，不要关联机大厅 MOD）：**
1. 在 MOD 菜单里**只关掉 RitsuLib**，保持联机大厅 MOD 开启。
2. 启动一次游戏，进到主菜单即可，然后退出。
3. 重新开启 RitsuLib。
4. 再启动游戏，大厅入口就会出现。

原因：禁用会把 RitsuLib 排到加载顺序末尾，让联机大厅 MOD 先完成初始化。
**注意：关掉再打开「联机大厅」MOD 本身会让问题固定化，请不要这么做。**

**进阶（会改文件的用户）**：直接编辑
`%APPDATA%\SlayTheSpire2\...\settings.save`，在 `mod_settings.mod_list` 数组里
把 `sts2_lan_connect` 挪到 `STS2-RitsuLib` 之前。

**仍然失败的用户，请收集：**
- `%APPDATA%\SlayTheSpire2\logs\godot.log`（含 `sts2_lan_connect patch_diag:` 行）
- `settings.save` 里的 `mod_settings` 段（可只截这一段）
- alpha.8 的诊断目录 `user://sts2_lan_connect/diagnostics/<utc>-<session>/`（含 `startup.jsonl`；
  若启用了 Harmony DEBUG 还会有 `harmony.log` 与 `dmd/` 转储）

---

## 11. 仍然不确定的地方

1. **桌面 27 个 tail 闭合泛型在 Ritsu-first 下的完整行为未在真机观测**——真机失败发生得更早，
   从没跑到 tail plan。已在 harness 上证明重叠的 2 个会失败，其余 18 个
   （`SendMessage<T>` / `SendMessageToClientInternal<T>`）RitsuLib 0.5.12 不碰，
   但 0.5.14 是否新增未知。
2. **RitsuLib 0.5.14 源码未获取**（仓库只 vendored 0.5.12）。
   已装的 0.5.14 程序集在
   `~/Library/Application Support/Steam/steamapps/workshop/content/2868840/3747602295/lib/0.111.0/STS2-RitsuLib.dll`，
   可反编译比对 `RitsuNetMessageBusTailPatches` 与 `HarmonyInitSetterCompat` 是否有变化。
   两份日志里三条 dynamic patch 的形状一致，倾向于没变，但未验证。
3. **JIT 是否真的会内联 `LobbyBeginRunMessage.Serialize`** 未实测
   （本机无法在游戏进程外执行 `PacketWriter`，GodotSharp 原生初始化会 SIGSEGV）。
   这决定 Compat4x5V1 下 boundary patch 是否真的必要；不影响第 8 节 A 的安全性论证。
4. **失败日志与成功日志来自不同机器**（不同 MOD 集、alpha.8 vs alpha.1、RitsuLib 0.5.14 vs 0.5.12、
   不同安装布局）。顺序相关性因此不是受控 A/B——但第 2 节的本机复现已经在**单一变量**下
   独立证明了因果，所以这个混杂不影响结论。
