# alpha.9 修复实现 · 验收审计与后续修复交接

- 编写时间：2026-08-23
- 基线：alpha.9 修复实现完成后的工作树（未提交），HEAD `409d7aa` + 未提交改动
- 本文对象：负责下一轮实现的 Agent
- 前置阅读（必读，本文不重复论证）：
  - 根因调查：`docs/STS2_LAN_CONNECT_ALPHA8_LOBBY_MISSING_RCA_ZH.md`
  - 原修复方案：`docs/STS2_LAN_CONNECT_ALPHA9_LOBBY_MISSING_FIX_PLAN_ZH.md`

---

## 0. 验收结论

F1 / F3 / F4 / F5 通过。**F2 不完整**：它把一个「大厅不显示」换成了一个更隐蔽的
「桌面无 RitsuLib 时开局消息缺 Tail 容器」。发布前必须先修 A1。

已独立复核通过的部分（复核者自己跑的，不是引用实现方的结论）：

| 项 | 结果 |
|---|---|
| `dotnet build` mod 项目 / GdUnitTests 项目 | 0 错误 0 警告 |
| `ProtocolPlanTests` | 12/12 通过 |
| `sts2-lan-connect.Tests` | 1123 通过 / 1 跳过 |
| F1 计数 | boundary 不再计入 `_patchedCount`，`requiredWirePatchCount = Targets.Count`（6），无 off-by-one |
| `protocol_patch_conflict` | `LanConnectProtocolFailure.Validate()` 只做长度校验、无白名单；`LanConnectProtocolFailureMapper.TailCodes` 只用于 wire 映射，新码不会误发上线 |
| 降级模式汇入点 | HostSubmenu→`StartLanHostAsync`、JoinFriendScreen→`DirectJoinFlow.JoinAsync`、续局→`PublishExistingHostToLobbyAsync`、overlay join 均已拦截 |
| F2 分支逻辑 | `isAndroid \|\| !preferLegacy` 三种组合均正确 |
| 被吞掉的补丁失败是否留残留 | **不留**。实测失败后 `PatchInfo` 仍只有外部 postfix（prefixes=0），后续 `UnpatchAll(ourId)` 安全 |

未能复核：GdUnit 套件（本机无 Godot 运行时 + `RITSULIB_ASSEMBLY`），仅验证其可编译。
Android 真机同样未复核。

---

## A1（阻断）桌面无 RitsuLib 时 begin-run 丢失 Tail 容器

### 事实

`SerializeBeginRunAtMessageBusPrefix`
（`sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs:405-429`）：

- `:428` `return false` —— 跳过 `NetMessageBus.SerializeMessage<T>` 原方法体；
- `:421` 自己调 `SerializeBeginRunBody(writer, message, listBitWidth)` 直接写字段，
  **从不调用 `message.Serialize(writer)`**。

而 F2 之后，begin-run 的 Tail 挂在**具体方法** `LobbyBeginRunMessage.Serialize(PacketWriter)`
上（`LanConnectTailMessagePatchPlan.cs:245` 的 `tail.android.serialize.lobby_begin_run`，
prefix `AndroidSerializeLobbyBeginRunPrefix` + postfix `AndroidConcreteSerializePostfix`）。

⇒ 该具体方法在 boundary 生效时**永远不会被调用** ⇒ begin-run 不写 Tail。

### 为什么 alpha.8 没这个问题

alpha.8 桌面把 Tail 钩在 `NetMessageBus.SerializeMessage<T>` —— **和 boundary 同一个方法**。
Harmony 的 `MethodCreator` 在 `skipOriginalLabel` 之后才 `AddPostfixes`
（反编译源码 `MethodCreator.cs:145-147`），所以 prefix 返回 false 时 postfix 照常执行，
Tail 仍被追加。F2 把钩子挪到另一个方法后，这条链断了。

### 为什么测试和真机都没抓到

- **没有任何测试同时应用 `LanConnectSerializationPatches.Apply()` 和 tail plan。**
  golden vector 测试只调 `ApplyForTesting`，不调序列化补丁；全仓库只有
  `LanConnectForeignPatchOwnerTests.cs:114` 调 `Apply`，而那条用例走的是 boundary **失败**分支。
- 真机复现是 **RitsuLib 在场**场景 → boundary 判为 `skipped_foreign_owner` → 具体路径完好 → 正常。
  恰好绕开了出问题的配置。
- 受影响的是 `beginRunMessageBusBoundary=patched`，即**桌面 + 无 RitsuLib**，最常见的用法。

### 要求的改法

把 boundary 的启用条件从「非 Android」改为「**当前使用 legacy generic 计划**」：

- 改 `LanConnectSerializationPatches.cs:216`
  `internal static bool ShouldPatchBeginRunMessageBusBoundary(bool isAndroid) => !isAndroid;`
  改成以「是否使用 `desktop_generic_v1`」为准的判定。计划选择是纯谓词、无副作用，
  可直接复用 `LanConnectTailPlanOverride.PreferLegacyDesktopGenericPlan`
  （`LanConnectTailMessagePatchPlan.cs:386`）与 `OperatingSystem.IsAndroid()`，
  与 `LanConnectTailMessagePatches.Apply`（`LanConnectTailMessagePatches.cs:105-108`）保持同一套逻辑。
  **注意执行顺序**：`LanConnectProtocolPatchDispatcher.cs:33-34` 先 `SerializationPatches.Apply()`
  再 `TailMessagePatches.Apply(harmony)`，所以此处不能依赖"tail plan 已解析"的状态，
  必须用谓词重算，或把计划选择提取成一个共享的纯函数。
- 保持 `:220-236` 的 `includeBeginRunMessageBusBoundary` 分支结构不变；
  跳过时的 `_beginRunBoundaryState` 需要新增一个取值（例如 `skipped_non_generic_plan`），
  不要复用 `skipped_android`，否则诊断日志会误导。

### 为什么这样安全

去掉 boundary 后，`SerializeMessage<T>` 正常执行：写 header（id 字节 + senderId = 72 bit）
→ 调 `message.Serialize(_writer)` → 位宽 transpiler 生效 → 具体 Tail prefix/postfix 生效。
这正是 Android 已验证的流程：

- 具体 prefix 要求 `writer.BitPosition == 72`
  （`LanConnectTailMessageRuntime.cs:1328-1340` `ValidateAndroidWriterHeader`，`HeaderBits = 72`），
  由 `SerializeMessage<T>` 的 header 写入满足。
- **Compat4x5V1 不会被误加 Tail**：具体 prefix 与 runtime 都在非 TailV1 时直接返回
  （`LanConnectTailMessagePatches.cs:541`、`LanConnectTailMessageRuntime.cs:214`
  均为 `if (!snapshot.IsActive || snapshot.Selection?.Profile != TailV1) return`），
  `__state` 为 null 时 postfix 立即 return（`LanConnectTailMessagePatches.cs:570-575`）。
- Compat4x5V1 的 5-bit 位宽改由 `LobbyBeginRunMessage.Serialize` 上的
  `TranspileBeginRunSerialize` 承担 —— 这个 transpiler 本来就是 6 个必需补丁之一，一直在。

**残留风险**：JIT 是否会把 `LobbyBeginRunMessage.Serialize`（71 IL 字节，低于 CoreCLR 100
字节内联候选阈值）内联进 `SerializeMessage<T>`，从而绕过 transpiler 与具体 Tail 钩子。
这正是调查报告 §11.3 的未决项。**实现时必须用下面的 A1-T2 实测，不要靠推理下结论。**

### 必须新增/修改的测试

- **A1-T1（新增，GdUnit）**：在 `LanConnectFullMessageGoldenVectorRuntimeTests` 中新增一条用例，
  **同时**执行 `LanConnectSerializationPatches.Apply()` 与默认（非泛型）tail plan，
  再跑现有全部 golden vector，逐字节比对。这条用例覆盖的组合在 alpha.8 与 alpha.9 都从未被测过。
  必须放 GdUnit 不能放 xUnit：`SerializeBeginRunAtMessageBusPrefix:422` 直接调 `Log.Info`
  （未走 `LogInfoSink`），在 xUnit 宿主里会段错误。
- **A1-T2（新增，GdUnit）**：断言 begin-run 走完整链路后 `LobbyBeginRunMessage.Serialize`
  确实被执行（例如用一个计数 postfix），以证伪 JIT 内联绕过。
- **A1-T3（修改）**：`sts2-lan-connect.Tests/Patches/LanConnectSerializationPatchesCompatibilityTests.cs:69-79`
  `Closed_generic_message_bus_boundary_is_disabled_only_for_android_gshared`
  —— 判定依据变了，用例名与 `InlineData` 都要改。

---

## A2（次要）`verify-release.sh` 的过滤器可能静默跑 0 个测试

实测：`dotnet test --filter` 匹配不到任何测试时**退出码为 0**：

```
dotnet test ...ProtocolPlanTests.csproj --filter "FullyQualifiedName~this_name_does_not_exist_anywhere"
→ 已通过!   EXIT=0
```

`scripts/verify-release.sh:154-164` 现在靠方法名子串 `legacy_desktop_generic_plan` 分两个
Godot 进程：

- 第二次调用（`~legacy_desktop_generic_plan`）一旦方法改名或 adapter 处理方式不同，
  会**绿着跑空** —— 而 F2 的字节等价证据全靠这一次；
- 第一次调用（`!~legacy_desktop_generic_plan`）同样脆：名字变了就会把 legacy 用例放回同进程，
  重新引入 JIT 内联污染。

**改法**：改用 GdUnit 的 category/trait 分组，或在脚本里断言每次调用实际执行的用例数
（解析输出中的通过计数，为 0 则 `exit 1`）。

---

## A3（次要）命名与语义已经对不上

- `LanConnectTailMessagePatches.ApplyForTesting(harmony, isAndroid)` 内部是
  `preferLegacyDesktopGenericPlan: !isAndroid`（`LanConnectTailMessagePatches.cs:114-118`），
  GdUnit 又传 `isAndroid: !forceLegacyDesktopGenericPlan` —— 双重取反。
  结果 `isAndroid` 这个参数名已不表示"是不是 Android"。
  以后有人写 `ApplyForTesting(h, isAndroid: false)` 想测"桌面生产行为"，拿到的是 legacy 计划。
  **建议改成显式传 `LanConnectTailPatchPlan` 或传枚举，去掉 bool。**
- profile 常量已改名 `DefaultProfile`，但 `ILanConnectAndroidTailMessageRuntime`、
  `LanConnectAndroidPreparedMessage`、`LanConnectAndroidTransportState`、
  `AndroidConcreteSerializePostfix`、`AndroidSerializeLobbyBeginRunPrefix`、
  `tail.android.*` step id 仍带 Android 字样，而它们现在是全平台默认路径。
  建议统一去掉 Android 前缀（step id 变更需同步 A4 的文档与打包断言）。

---

## A4（次要）文档与代码已经不一致，且被测试锁死

以下文件仍写着「非 Android 环境继续使用 30 项 `desktop_generic_v1` 路径」，现在是错的：

- `README.md:47,49,245,247`
- `CHANGELOG.md:13`
- `docs/CLIENT_RELEASE_README_ZH.md:40,241`
- `docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md:17,18,311,312`
- `docs/RELEASE_NOTES_V0.6.0_ALPHA8_ZH.md:14,41`

而 `sts2-lan-connect.Tests/Packaging/LanConnectPackageContentTests.cs:307-314` 正在
**强制**这些文档包含 `android_non_generic_v2` / `desktop_generic_v1` / `0.6.0-alpha.8` / `PENDING`。
现在是绿的（因为文档没动），但代码说的已是另一回事。

**改法**：与原方案第 6 节的发布步骤一起做 —— 升版本号到 `0.6.0-alpha.9`、更新上述文档描述
新的全平台 `non_generic_v2` 默认，并同步更新该打包断言。**不要单独改文档而不改断言，会红。**

---

## 验收标准

1. `RITSULIB_ASSEMBLY=<official-v0.5.14-dll> ./scripts/verify-release.sh` 全绿，
   且脚本能证明两次 GdUnit 调用各自实际执行了用例（A2 修完后）。
2. A1-T1 / A1-T2 通过：boundary 与非泛型 tail plan 同时生效时，全部 golden vector 逐字节一致。
3. 真机四象限全部手工验收（**这次不能只测 RitsuLib 在场**）：

   | 场景 | 期望 |
   |---|---|
   | 桌面 + 无 RitsuLib | begin-run 带 Tail，可开局 —— **本轮回归点** |
   | 桌面 + RitsuLib 先加载 | `skipped_foreign_owner`，可开局 |
   | 桌面 + RitsuLib 后加载 | 可开局 |
   | Android | 回归通过（非泛型计划现为共用路径） |

4. 不得回归：`compat_4_5_v1` 仍拒绝 RitsuLib；无 RitsuLib 的 compat 房仍按 5-bit 编码互通。

---

## 明确不要做的事

- 不要恢复 RC4 的 RitsuLib 私有 postfix detach/restore 桥
  （设计文档 `docs/superpowers/specs/2026-08-13-v0-6-dual-protocol-design.md:118/450/567`；
  `LanConnectSerializationPatchesCompatibilityTests.cs:82-101` 有源码级禁止清单）。
- 不要在 `LanConnectSerializationPatches.cs` 里引入 `Harmony.GetPatchInfo`
  （同上禁止清单第 96 行）；需要 owner 信息就走
  `LanConnectProtocolPatchDispatcher.GetExternalPatchOwners`。
- 不要给 `sts2_lan_connect.json` 加 `dependencies`（会锁死失败顺序 + 对无 RitsuLib 用户硬失败）。
- 不要改线上字节格式。任何改动都必须让现有 golden vector 原样通过。
