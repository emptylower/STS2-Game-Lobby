# 修复：与 RitsuLib 共存时 tail 容器生产 seam 被 JIT 内联绕过（后端 / 客户端协议逻辑）

## 根因（本机双实例已复现并做对照，勿重新调查）

- 两端都装 RitsuLib 0.5.18 时，房主日志里**没有** `sts2_lan_connect tail: concrete serialize prefix fired for InitialGameInfoMessage`，
  扩展帧从未产生；两端都不装 RitsuLib 时该行出现且加入成功（`已加入`）。
- 机制：RitsuLib 给 `NetMessageBus.SerializeMessage<InitialGameInfoMessage|LobbyBeginRunMessage|StateDivergenceMessage>`
  打 Harmony 补丁（`RitsuNetMessageBusTailPatches.SerializePatch<TMessage>`，见 project memory
  "sts2-harmony-generic-patch-poisoning"）。Harmony 为该实例化生成的替换体是**优化编译的 DynamicMethod**（非分层），
  会把小结构体 `InitialGameInfoMessage.Serialize(PacketWriter)` 直接内联进去；内联读取的是原始 IL，
  绕过了我们挂在 `T.Serialize` 上的 detour（`tail.serialize.*` 9 步）。无 RitsuLib 时 `SerializeMessage<T>` 走 Tier0 不内联，所以正常。
- 同一机制也解释 alpha.1 兼容房黑屏（`LobbyBeginRunMessage.Serialize` 上的 5-bit 转译器被内联绕过）。

## 已写好的失败测试（先跑确认红，再改）

`sts2-lan-connect.ProtocolPlanTests/LanConnectTailPatchPlanTests.cs`
`Desktop_serialize_steps_target_the_message_bus_SerializeMessage_instantiation`：非安卓平台上 9 个 `serialize` 步骤的
`Target` 必须是 `NetMessageBus.SerializeMessage<T>` 对应消息类型的**闭合实例化**。当前失败。

## 约束（必须遵守）

1. **安卓保持现状**：安卓 Mono/gshared 无法为闭合泛型目标生成 wrapper（`docs/STS2_LAN_CONNECT_V0.111_ANDROID_GSHARED_HANDOFF_ZH.md`），
   `OperatingSystem.IsAndroid()` 时仍用 `T.Serialize` 目标，`GenericTargetCount` 仍为 0。
2. **RitsuLib 先加载的情况**（安卓测试者日志 `ritsulib_patched_before_us=true` 的桌面等价场景）：RitsuLib 的泛型声明 postfix 已在
   `SerializeMessage<InitialGameInfoMessage|LobbyBeginRunMessage>` 上时，我们再 `harmony.Patch` 同一方法会因 wrapper 重建触发
   `InvalidProgramException`（memory 已验证）。对这两个类型要**逐类型 try/catch**：失败则回退到原 `T.Serialize` 目标，并用
   `Log.Warn` 明确写出“SerializeMessage<T> 已被 RitsuLib 的泛型补丁占用，回退到 Serialize 钩子；请把 STS2 LAN Connect 排在 RitsuLib 之前”，
   patch_diag 记录 `fallback=true`。不要吞掉其它异常（交接文档 §8：不能 catch 后表面成功）。
3. `LanConnectTailPatchPlan` 的自检（步数 16、id 唯一、禁 `SetBufferMessages`）保持；把“禁止泛型目标”改为**仅安卓禁止**，
   桌面允许且只允许 `serialize` 类别的 9 步是泛型实例化；`plan_success` 诊断行照常输出 `generic_target_count`。

## 要做的改动

### A. 补丁计划与 prefix 签名

`sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatchPlan.cs`、`LanConnectTailMessagePatches.cs`

- 桌面：`tail.serialize.<type>` 的 Target = `AccessTools.DeclaredMethod(typeof(NetMessageBus), "SerializeMessage").MakeGenericMethod(messageType)`
  （0.107.1 与 0.111.0 签名相同：`byte[] SerializeMessage<T>(ulong senderId, T message, out int length)`，fixture 已核对）。
- 9 个 prefix 改为 `(NetMessageBus __instance, ref <MessageType> message, out LanConnectNativePreparedMessage? __state)`：
  从 `__instance` 经现有 `NetMessageBusWriter` FieldInfo 取 `_writer`，其余逻辑同 `PrepareConcreteMessage`（投影后写回 `message`）。
  Postfix 仍为 `AndroidConcreteSerializePostfix(__state)`（此时 header 与 body 已写入，且**在** transport 调用之前——`SerializeMessage` 返回后
  `SendMessageToClientInternal` 才调 transport，语义与现在一致）。
- 保留现有 `T.Serialize` 版本的 prefix 作为安卓与回退路径（可复用同一 `PrepareConcreteMessage<T>`）。
- `PrefixPriority` 保持 `Priority.First + 100`。

### B. 运行时：prepare 时不再要求 header 已写入

`sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessageRuntime.cs`

- `TryPrepareConcreteOutgoing` 目前调用 `ValidateNativeWriterHeader(writer, binding, message)`（要求 typeId+sender 已写入）；
  新 seam 在 `_writer.Reset()` 之前触发，writer 里还是上一条消息的残留。改为：prepare 只校验 writer 是已绑定的 writer 与会话快照，
  header 校验统一放到 `CompleteConcreteOutgoing`（那里已经有 `requireSerializeBoundary:false` 的校验）。
- 安卓路径（`T.Serialize` prefix，header 已写入）走同一 prepare 也必须仍然正确。
- `AndroidWriterResetPrefix` 在 `SerializeMessage` 体内的 `Reset()` 会触发：确认它不会清掉刚 prepare 的 `__state`（prepare 的结果只在
  Harmony `__state` 里，`_pendingOutgoing` 在 Complete 时才登记——请核对并保持）。

### C. 测试

- `sts2-lan-connect.GdUnitTests/Protocol/LanConnectTailMessageRuntimeTests.cs` 等的 `SerializeMatrixMessage` 辅助方法：改为
  `Reset → TryPrepareConcreteOutgoing → 写 header → 投影消息.Serialize → CompleteConcreteOutgoing` 的顺序（模拟新 seam）；
  另加一个用例覆盖安卓顺序（header 先写再 prepare）仍通过。
- `sts2-lan-connect.ProtocolPlanTests`：
  - `Native_plan_has_the_stable_sixteen_step_non_generic_shape` 改名/改断言：桌面 `GenericTargetCount == 9`（仅 serialize 步），安卓 0。
  - `LanConnectForeignPatchOwnerTests.Generic_declared_foreign_postfix_does_not_break_plan_application`：RitsuLib 先占
    `SerializeMessage<LobbyBeginRun|InitialGameInfo>` 的场景改为断言：计划仍成功应用，这两个类型回退到 `T.Serialize` 目标并记录 warn，
    其余 7 个类型仍是 `SerializeMessage<T>` 目标；`Serialization_boundary_failure_is_not_fatal` 按新语义调整。
  - `LanConnectTailPatchFailureTests` 若依赖 target 名称，同步更新。
- 删除我临时加的 `sts2_lan_connect tail: concrete serialize prefix fired for ...` 日志（在 `PrepareConcreteMessage` 顶部）；
  其它 `tail:` 诊断行（holding / extension frame received / pending registered / sending native extension / native flow ...）保留。

### D. CHANGELOG

`## [Unreleased]` 的 `### Fixed` 末尾追加一条中文：桌面平台 tail 容器生产钩子改挂 `NetMessageBus.SerializeMessage<T>` 实例化，
避免与 RitsuLib 共存时小结构体 `Serialize` 被内联绕过；RitsuLib 先于本 Mod 加载时对 InitialGameInfo/LobbyBeginRun 回退旧钩子并告警。

不要改服务端、前端文案、Entry.cs。不要 git commit。

## 完成标准

```bash
export GODOT_BIN=/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot
dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj -m:1
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  --filter "FullyQualifiedName~Protocol"
```

三条全绿。完成后用简短中文汇报：改动文件、测试结果、RitsuLib 先加载时的回退行为如何验证。
