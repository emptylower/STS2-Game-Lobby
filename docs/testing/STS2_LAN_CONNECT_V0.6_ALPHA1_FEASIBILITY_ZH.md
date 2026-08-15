# STS2 LAN Connect v0.6.0-alpha.1 可行性门禁

日期：2026-08-15。本门禁使用真实 RitsuLib v0.5.10 的导出 public API；不检查 extension inventory、message-tail owner、Harmony patch 或私有 resolver。

## 已通过的本地证据

冻结容器 `tail-probe-complete-v1.bin` 为 36 bytes，SHA-256 为 `cfc9097350801026775fd333fb19c6758becbffd4142f58bab0884f4231f5cfa`。`Standalone_carrier_round_trips_the_frozen_container` 经真实 `PacketWriter/PacketReader` 写入三位 vanilla body、补零到 byte 边界、写读容器后通过：container start 为 bit 8、end 为 bit 296，padding 全为零。

真实 v0.5.10 公开契约测试通过。所用 API 是 `RitsuLibSidecarMessageDescriptor<T>`、`RitsuLibSidecarTypedMessageRegistry.Register/Subscribe/SendToHost(INetGameService,...)/SendToPeer(INetGameService,...)`、`RitsuLibSidecarSessionManager.ObserveNetService`、`SetPeerReachabilityHint`、`CanSendToPeer` 和公开 session/reachability events。`SidecarCarrierProbe` 使用 required typed descriptor，且只接受真实 `INetGameService`；不调用 `RitsuNetMessageTailExtensions.Write/Read`，不使用 RC4 resolver transpiler。

```sh
DOTNET_ROOT=/Users/mac/.dotnet PATH=/Users/mac/.dotnet:$PATH \
  /Users/mac/.dotnet/dotnet test research/prototypes/v0.6-tail-ritsulib/Sts2TailPrototype.csproj -m:1 \
  -p:Sts2DataDir='/Users/mac/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64' \
  -p:RitsuLibAssembly="$RITSULIB_ASSEMBLY" \
  --filter 'FullyQualifiedName~Standalone_carrier|FullyQualifiedName~Public_api'
```

结果：2 passed。Android project 使用相同 STS2 managed input 构建为 0 warning/0 error；`artifacts/android-probe/` 仅有 `sts2_lan_v06_probe.dll` 和 `sts2_lan_v06_probe.json`。

## 外部门禁状态

完整 prototype 为 2 passed、1 expected BLOCKED failure。`Ritsu_sidecar_pairs_before_vanilla_handler` 需要 `STS2_RITSU_SIDECAR_PROBE_COMMAND`（真实双游戏进程 launcher）和可写 `STS2_RITSU_SIDECAR_PROBE_EVIDENCE`。当前环境没有二者。launcher 必须在受信 ticket/peer/flow binding 后调用 public `SetPeerReachabilityHint(Supported)`、`ObserveNetService`、`CanSendToPeer` 和 direct typed send，且证据必须证明 sidecar frame 在 vanilla handler 前配对、teardown 清回 `Unknown`、重用 peer ID 仍从 `Unknown` 开始。

Android/MuMu 也没有 `ANDROID_STS2_DATA_DIR`、serial、MOD/log path、官方解包 Ritsu package tree/hash 或 desktop pair launcher。`run_android_probe.sh` 会在任何输入缺失时在 mutation 前退出 BLOCKED；可用后它执行 standalone Android 与 host/client 两个 Android/desktop Ritsu sidecar pair，并由 `verify_android_probe.py` 验证五个新鲜 marker、flow nonce、inner container、barrier、hint cleanup 和 peer-ID reuse。

| Gate | Verdict | Evidence |
|---|---|---|
| Standalone carrier bytes/cursor | PASS | real PacketWriter/Reader path; raw offsets 8..296 |
| All-Ritsu sidecar reachability/barrier | BLOCKED | public typed API contract passes; real two-game launcher/evidence absent |
| Android standalone/sidecar runtime | BLOCKED | project build/staging passes; MuMu/device/package/desktop peer inputs absent |
| Private resolver bridge removal | BLOCKED | direct public `INetGameService` carrier source exists, but no real two-process runtime result |

RitsuLib homogeneous-room support is removed from the executable alpha scope pending design review; the RC4 private bridge and independent double-Tail remain prohibited.

Task 2 must separately prove pre-ticket Ritsu presence mismatch rejection before room/ticket/control side effects. That is downstream work, not Task 0 PASS evidence.
