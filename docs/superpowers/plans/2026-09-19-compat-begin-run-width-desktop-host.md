# 桌面房主 + compat_4_5_v1：开局后客机解码乱码、房主永久等待（0.6.1 起的回归）

你对本次对话零上下文，本文件是全部信息。只改客户端 `sts2-lan-connect/` 与测试 `sts2-lan-connect.Tests/`，外加 `CHANGELOG.md`。

## 硬约束

- 不要 `git commit` / `git push` / 打 tag；不要改 `releases/**`、`lobby-service/**`；不要改版本号（保持 `0.6.3-alpha.1`，版本切换由人来做）。
- **不得改变 tail_v1（native_bus_v1）路径的任何线上字节与行为**：tail_v1 下原版位宽就是正确位宽，扩展帧由 `BusSerialize*Prefix`/postfix 产生，必须原样保留。
- **不得改变 Android 路径**：Android 挂的是具体 `T.Serialize`（gshared 无法编译闭合泛型包装），那条路径工作正常。
- 不要运行游戏、不要做 GUI 操作；实机双实例验证由人来做。
- 注释沿用周边中文风格。

## 现象（已在本机双实例 100% 复现，玩家 Windows↔Android 同样复现）

桌面端（Windows/macOS）0.6.1+ 作房主、协议选「兼容旧版 Mod」（compat_4_5_v1），客机加入、选人、准备都正常；房主点开始后：

- 客机：`ModelNotFoundException: Model id=CHARACTER.<乱码，如 SWIFT / IMBALANCED_POWER> not found`（栈在 `NetClientGameService.OnPacketReceived` → 仍在 `NCharacterSelectScreen`），随后 `SyncPlayerDataMessage` / `SyncRngMessage` “no message handlers are registered”，最后 `Disconnected from host`。
- 房主：`Embarking on a multiplayer run` 后永久停在 `[CombatStateSynchronizer] Waiting to receive all sync messages from all clients`（黑屏）。
- 对照：同版本 tail_v1 正常；PC 降级 0.6.0 + compat 正常；Android 作 compat 房主正常。

复现日志：房主日志里**没有** `lobby begin-run forced at message-bus boundary` 这行。

## 根因

compat 依赖对 `LobbyBeginRunMessage.Serialize`（及 JoinResponse、`StartRunLobbyPlayer`）的 transpiler 把原版位宽（`VanillaLobbyListBits` / `VanillaSlotIdBits`）换成 compat 位宽（日志 `compatSlotBits=4 compatLobbyBits=5`），见 `sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs`。

提交 a1974d3（native_bus_v1 迁移，0.6.1）之后，桌面端 tail 计划改为 Harmony 补丁 `NetMessageBus.SerializeMessage<T>` 的闭合实例化（`Protocol/Patches/LanConnectTailMessagePatchPlan.cs:201-219`，`useBusSerializeSeam = !IsAndroid`），其中包含 `SerializeMessage<LobbyBeginRunMessage>`。Harmony 的替换体是全优化的 DynamicMethod，RyuJIT 会把小结构体方法 `LobbyBeginRunMessage.Serialize` **按原始 IL 内联**进去——于是挂在 `LobbyBeginRunMessage.Serialize` 上的 compat transpiler 被整个绕过，房主按原版位宽写 begin-run，客机（transpiler 生效的 Deserialize）按 5 bit 读 → 全部错位。这与此前 RitsuLib 造成的同类问题机制完全相同，只是这次触发者是我们自己的补丁。

同一次提交还把原本专门防这个问题的 begin-run 边界 prefix 关掉了：`LanConnectSerializationPatches.Apply()` 里 `includeBeginRunMessageBusBoundary = false`，`ResolveRequiredPatchPlan` 里 `beginRunMessageBusSerialize = null`。prefix 本体 `SerializeBeginRunAtMessageBusPrefix`（约 389 行，`[HarmonyPriority(Priority.First)]`，手写 header + `SerializeBeginRunBody(writer, message, GetActiveLobbyListBitWidth())`，`return false`）仍在。

注意 JoinResponse 目前在 mac 上没被内联（加入成功），但这取决于 JIT 的内联决策，不可依赖。

## 要求

让桌面端在 **compat 活动 profile** 下，凡是「我们给 `SerializeMessage<T>` 打了 seam 补丁」且「`T.Serialize` 上有 compat 位宽 transpiler」的消息，其线上字节**不依赖 JIT 是否内联**，恒为 compat 位宽。至少覆盖 `LobbyBeginRunMessage`；请核对 `ClientLobbyJoinResponseMessage`（`TranspileJoinResponseSerialize`）是否同样处于该风险下，是则一并处理，并在汇报中说明 LoadJoinResponse / RejoinResponse 是否携带 lobby list（是否也需处理）。

设计自行决定，但必须满足：

1. 活动 profile 为 tail_v1（或非 compat）时：完全走现有路径，字节与现在逐位一致。最稳妥的做法是 prefix 里判定非 compat 就 `return true` 且不碰 writer。
2. compat 时：body 由我们显式按 `LanConnectProtocolProfiles.GetActiveLobbyListBitWidth()` 等写出（可复用 `SerializeBeginRunBody`），header（`ToId()` 字节 + senderId）与 `length`/`__result` 的产生方式与原版 `SerializeMessage<T>` 一致——**请反编译/阅读游戏的 `NetMessageBus.SerializeMessage<T>` 确认原版细节**（游戏程序集路径见 csproj 的 `Sts2DataDir`；可用 `~/.dotnet/tools/ilspycmd`，需 `DOTNET_ROOT`）。`StartRunLobbyPlayer.Serialize` 是经 `WriteList` 泛型间接调用的，确认它不在内联风险内；若无法确认，说明之。
3. 与同一方法上的 tail 补丁（`BusSerializeLobbyBeginRunPrefix` 及其 postfix/finalizer、`__state`）共存：compat 下跳过原方法体时，tail 的 postfix 不得抛错、不得追加扩展帧、不得留下脏的 pending 状态。请读 `LanConnectTailMessagePatches.cs` 的 `PrepareConcreteMessage` 与对应 postfix 确认 compat 下的行为，并用测试固定。
4. 补丁失败（例如外部 MOD 已占用该闭合实例化，见日志 `SerializeMessage<T> 已被 RitsuLib 的泛型补丁占用`）时的策略：compat 不支持 RitsuLib，可保持 best-effort，但必须打一条明确的 Warn，且 `beginRunMessageBusBoundary=` 诊断字段要如实反映状态（applied / failed / skipped_android）。
5. 更新 `Apply()` 与 `ResolveRequiredPatchPlan` 里那两段已经不成立的注释（“removed with the native_bus_v1 migration…”）。
6. 成功走 compat 强制路径时保留/打印 `lobby begin-run forced at message-bus boundary players=…, lobbyListBits=…, bodyBytes=…`（人会用它做实机验证的断言）。

## 测试

- 先写失败测试。现有相关测试先 grep：`BeginRunBoundaryStateForTesting`、`SerializeBeginRunBody`、`LanConnectSerializationPatches` 在 `sts2-lan-connect.Tests/` 与 `sts2-lan-connect.GdUnitTests/` 中的用法，沿用其风格。
- 至少覆盖：compat 位宽下 `SerializeBeginRunBody` 写出 → 用 compat 位宽读回一致（往返）；profile 判定函数在 tail_v1 / none 下返回「走原路径」；桌面计划包含 begin-run 边界目标而 Android 不包含。
- JIT 内联本身单测测不到，不要伪造；在汇报里直说哪些只能靠实机验证。

## 收尾

- `CHANGELOG.md`：在 `## [Unreleased]` 下新建 `### Fixed` 写一条（不要写进已发布的 `[0.6.3-alpha.1]` 小节）。
- 完成标准（贴结尾几行输出）：

```bash
dotnet build sts2-lan-connect/sts2_lan_connect.csproj -c Release
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --blame-hang-timeout 120s
```

- 汇报：改动文件、设计选择与理由、上面第 2/3 点的核实结论、测试结果、与本文不符的发现。
