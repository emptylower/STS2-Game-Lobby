# 修复：与 RitsuLib 0.5.18 共存时新协议房间无法加入（房主 10 秒 LobbyJoinTimeout）（后端 / 客户端协议逻辑）

## 根因（已用本机双实例复现并做对照实验，勿重新调查）

- 复现：同一台 Mac 跑两个 0.111.0 实例（都装 RitsuLib 0.5.18，alpha.3），A 建新协议房，B 加入 → 握手成功后双方静默，
  10 秒后房主 `Disconnecting player ... did not respond to the initial game join message within 10000ms` → 加入方 `LobbyJoinTimeout`。
  与 Windows 测试者日志 `godot (9).log` 完全一致。
- 对照：两端都移除 RitsuLib，同样流程加入成功（房主 `Received ClientLobbyJoinRequestMessage`）。
- 机制：RitsuLib 0.5.18 `RitsuLibSidecarNativeTrailerSendPatch` 是挂在 `ENetHost.SendMessageToClient` / `ENetClient.SendMessageToHost`
  上的 **Prefix(ref byte[] bytes, ref int length)**，会给每个原版包**换成一个加长 36 字节（trailer）的新数组**。
  我们的 `tail.transport.host/client` 前缀（`LanConnectTailMessagePatches.AndroidHostTransportPrefix` →
  `LanConnectTailMessageRuntime.BeginNativeTransport`）用 `ResolvePendingTransportContext(buffer)`
  **按数组引用相等**找 pending，`ValidateTransportBuffer` 再要求**引用相等且长度精确相等**。
  当 RitsuLib 的前缀先运行，我们看到的是新数组 → pending 解析为 null → 扩展帧永远不发；
  加入方的配对屏障把 InitialGameInfo 扣住等扩展帧，而屏障超时清扫只在“下一条消息到达时”触发
  （`SweepExpiredBarrierHolds` 仅在 `TryEnterNativeDispatch` 里调用），没有后续消息就永远沉默。

## 已写好的失败测试（先跑确认红，再改）

`sts2-lan-connect.GdUnitTests/Protocol/LanConnectTailMessageRuntimeTests.cs`
`Third_party_send_prefix_that_copies_and_extends_the_buffer_still_emits_the_extension_frame`：
把 pending 的 buffer 复制到一个加长 36 字节的新数组后调用 `BeginNativeTransport`，期望解析到 pending 并发出扩展帧。当前失败（context 为 null）。

## 要做的改动

### A. pending 匹配改为“内容匹配”，不再依赖数组引用（必做）

`sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessageRuntime.cs`

1. `ResolvePendingTransportContext(byte[] buffer)` 改为 `ResolvePendingTransportContext(byte[] buffer, int length)`：
   优先仍按引用相等匹配（零成本快路径）；若无，则匹配满足
   `length >= context.Length && buffer.AsSpan(0, context.Length).SequenceEqual(context.Buffer.AsSpan(0, context.Length))`
   的 pending（即“以原包为前缀、后面可有第三方 trailer”）。同一 owner 下若有多个候选，取唯一匹配，多于一个则视为异常（抛 `InvalidDataException`，走现有 `AbortActiveBinding`）。
2. `ValidateTransportBuffer(context, buffer, length)`：去掉 `ReferenceEquals(context.Buffer, buffer) || context.Length != length` 的硬要求，
   改为 `length >= context.Length` 且前 `context.Length` 字节逐字节相等；`ComputeHeaderFingerprint` 继续按 `context.Length` 计算并比对（不要把 trailer 算进指纹）。
3. `LanConnectNativePendingOutgoing` 若有 `Buffer`/`Length` 以外依赖引用相等的地方（grep `ReferenceEquals(context.Buffer` / `ReferenceEquals(.*Buffer`），一并改为内容匹配。
4. 在 `LanConnectTailMessagePatchPlan.cs` 给 `tail.transport.host` / `tail.transport.client` 两步的 **Prefix** 显式设置高优先级
   （`HarmonyLib.Priority.High` 或更高，例如 `Priority.First`），Postfix/Finalizer 保持现状；这样即使第三方也加前缀，我们大概率先看到原始数组。
   注意 `LanConnectTailMessagePatchPlan` 有对步数/字段的自检断言（`Steps.Count != expectedSteps` 等），改优先级不要破坏它；如有 ProtocolPlanTests 的黄金断言需同步更新。

### B. 配对屏障超时改为定时触发（必做，让失败在 2 秒内以 lan_extension_missing 暴露，而不是等房主 10 秒踢人）

`LanConnectTailMessageRuntime.cs`（`Binding` 内）：`HoldMatrixMessage` 放入 hold 时，若当前没有待触发的清扫，则安排一次延迟清扫：
`BarrierHoldTimeout + 50ms` 后在**消息分发所在的同步上下文**（Godot 主线程）调用 `SweepExpiredBarrierHolds(DateTimeOffset.UtcNow)`。
实现建议：捕获 `SynchronizationContext.Current`（Godot 提供 `GodotSynchronizationContext`），
`Task.Delay(...).ContinueWith(_ => ctx.Post(_ => Sweep(), null))`；若 `SynchronizationContext.Current` 为 null（测试环境），直接在 `Task.Delay` 续体里加锁调用（`Sweep` 内部已加锁，`FailPeerForExpiredHold` 自带 try/catch）。
绑定 `MarkTerminated`/Unbind 时要取消或让定时清扫成为空操作（hold 表已清空即可）。
为它补一个 GdUnit 测试：hold 一条矩阵消息后不再送任何包，`await Task.Delay(BarrierHoldTimeout + 500ms)`，断言对端被 `lan_extension_missing` 拒绝（参考现有 `LanConnectTailMessageRuntimeTests` 里屏障相关用例的断言方式）。

### C. CHANGELOG

`CHANGELOG.md` 的 `## [Unreleased]` 下新增 `### Fixed`，两条中文：
- 与 RitsuLib 0.5.18 共存时新协议房间无法加入（房主 10 秒 LobbyJoinTimeout）：其 NativeTrailer 发送前缀会替换原版包的字节数组，本 Mod 的传输层按数组引用匹配待发扩展帧导致扩展帧从未发出；改为内容匹配并提高前缀优先级。
- 配对屏障超时改为定时触发，扩展帧缺失在 2 秒内以 `lan_extension_missing` 明确报错。

不要改服务端、前端文案、Entry.cs。不要 git commit。

## 完成标准

```bash
export GODOT_BIN=/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  --filter "FullyQualifiedName~LanConnectTailMessageRuntimeTests|FullyQualifiedName~GoldenVector|FullyQualifiedName~TailMessageBus"
dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj -m:1
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
```

三条全绿。完成后用简短中文汇报：改动文件、测试结果、优先级取值、定时清扫的线程模型。
