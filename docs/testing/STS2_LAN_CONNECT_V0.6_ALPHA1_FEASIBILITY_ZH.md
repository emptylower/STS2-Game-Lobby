# STS2 LAN Connect v0.6.0-alpha.1 可行性门禁

日期：2026-08-16。本门禁使用官方 RitsuLib v0.5.12 的导出 public API，并以 Sts2MobileLauncher v0.1.9、Android API 35 arm64 emulator、Android/macOS 游戏 v0.111.0 完成运行时复测；不检查 extension inventory、message-tail owner、Harmony patch owner 或私有 resolver。

## 已通过的本地证据

冻结容器 `tail-probe-complete-v1.bin` 为 36 bytes，SHA-256 为 `cfc9097350801026775fd333fb19c6758becbffd4142f58bab0884f4231f5cfa`。`Standalone_carrier_round_trips_the_frozen_container` 经真实 `PacketWriter/PacketReader` 写入三位 vanilla body、补零到 byte 边界、写读容器后通过：container start 为 bit 8、end 为 bit 296，padding 全为零。

真实 v0.5.12 公开契约测试通过。所用 API 是 `RitsuLibSidecarMessageDescriptor<T>`、`RitsuLibSidecarTypedMessageRegistry.Register/Subscribe/SendToHost(INetGameService,...)/SendToPeer(INetGameService,...)`、`RitsuLibSidecarSessionManager.ObserveNetService`、`SetPeerReachabilityHint`、`CanSendToPeer` 和公开 session/reachability events。`SidecarCarrierProbe` 使用 required typed descriptor，且只接受真实 `INetGameService`；不调用 `RitsuNetMessageTailExtensions.Write/Read`，不使用 RC4 resolver transpiler。

```sh
DOTNET_ROOT=/Users/mac/.dotnet PATH=/Users/mac/.dotnet:$PATH \
  /Users/mac/.dotnet/dotnet test research/prototypes/v0.6-tail-ritsulib/Sts2TailPrototype.csproj -m:1 \
  -p:Sts2DataDir='/Users/mac/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64' \
  -p:RitsuLibAssembly="$RITSULIB_ASSEMBLY" \
  --filter 'FullyQualifiedName~Standalone_carrier|FullyQualifiedName~Public_api'
```

结果：2 passed。Android project 使用相同 STS2 managed input 构建为 0 warning/0 error；`artifacts/android-probe/` 仅有 `sts2_lan_v06_probe.dll` 和 `sts2_lan_v06_probe.json`。

## 跨平台运行时结果

- **macOS + RitsuLib v0.5.12：启动 PASS。** 官方 v0.111.0 单版本包完成全部 framework patch group，Ritsu 的 3 个动态消息补丁成功安装，`Shared framework initialization complete` 与 LAN Connect runtime ready 均出现，游戏进入主菜单。
- **Android + RitsuLib v0.5.12：启动 BLOCKED。** 同一官方 v0.111.0 DLL（SHA-256 `7303da3eba870a68b6b76821c52d9f5b86e220a1464da2b3deef2007642be5f1`）完成 Ritsu 的静态 patch group 后，在 `RitsuNetMessageBusTailPatches.ApplySerializePatches` 调用 Harmony detour 时报告 `BUG: Unreferenced static string to 0: _initialize`。90 秒后仍为黑屏，未出现 framework initialization complete、主菜单或 LAN sidecar session。
- **Android/macOS 无 Ritsu：联机 PASS。** 双方使用 v0.111.0 与 LAN Connect v0.6.0-alpha.1，完成 ticket、InitialGameInfo、2 人 roster、双方 ready、LobbyBeginRun 和首个 Neow 同步状态。
- Android 的 Ritsu 失败发生在第三方 framework 初始化阶段，早于 LAN Connect sidecar flow；本项目不通过 fork、私有补丁或捆绑改版 RitsuLib 绕过。

| Gate | Verdict | Evidence |
|---|---|---|
| Standalone carrier bytes/cursor | PASS | real PacketWriter/Reader path；raw offsets 8..296；Android/macOS no-Ritsu 实际联机 |
| Ritsu public typed-sidecar contract | PASS | 官方 v0.5.12 程序集自动化契约 + macOS framework/runtime 启动 |
| All-Ritsu sidecar reachability/barrier | BLOCKED | Android 的 Ritsu framework 在创建 sidecar session 前初始化失败，无法执行双端消息门禁 |
| Android standalone runtime | PASS | v0.111.0 + launcher v0.1.9 与 macOS 完成真实开局 |
| Android Ritsu runtime | BLOCKED | `ApplySerializePatches` / Harmony detour 阶段黑屏 |
| Private resolver bridge removal | PASS | 生产代码与发布包不再包含 RC4 私有 bridge；只保留 public `INetGameService` carrier |
| Ritsu presence mismatch | PASS | 真实 Ritsu host/no-Ritsu joiner 与反向服务端副作用测试均在 ticket/transport 前拒绝 |

## 发布边界

- no-Ritsu `tail_v1`、`compat_4_5_v1` 和 presence mismatch 门禁可进入 `v0.6.0-alpha.1` Pre-release。
- 全 Ritsu 的 Android 联机能力不得在本测试版中宣称可用；当 public sidecar 未初始化或 readiness 不成立时，客户端继续以 `ritsulib_sidecar_unavailable` fail closed。
- macOS 启动成功不等于 macOS/macOS 双端联机已经通过；该组合仍需后续真实双进程验证。
- RC4 私有 bridge 与 independent double-Tail 继续禁止，LAN Connect 不维护或分发 RitsuLib 分支。
