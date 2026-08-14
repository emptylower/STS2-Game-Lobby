# STS2 LAN Connect v0.6.0-alpha.1 可行性门禁

日期：2026-08-14。此文档记录 v0.6 在开始任何生产改动前的 RitsuLib/Tail 门禁。所有结论只适用于实际传入的目标版本程序集；不能以类型命名、反编译猜测、私有反射或 RC4 的 postfix 桥替代公开契约。

## 已验证的桌面证据

桌面命令以 macOS arm64 的 `sts2.dll`、`0Harmony.dll` 和本机提供的已构建 `STS2-RitsuLib.dll` 运行。Tail fixture 为 36 字节、SHA-256 `cfc9097350801026775fd333fb19c6758becbffd4142f58bab0884f4231f5cfa`。四个发送/接收安装组合均通过：LAN 原始字节不变，LAN 结束和 Ritsu 读取起点均为 bit 288。

RitsuLib 的公开 `RitsuNetMessageTailExtensions.RegisterBytes<T>`, `Write<T>` 与 `Read<T>` 可注册并调用，且其公开 XML 合约要求调用方在原版消息体之后各调用一次。导出的公开类型和成员中没有全部注册项的枚举器，公开注册参数也没有关键/非关键分类。因此不能安全决定未知 Ritsu 扩展的协商或拒绝规则。

现有 sidecar 的公开握手驱动方法可调用：`ObserveNetService(INetGameService)`、`TickHandshakeNegotiation()`、`RefreshAllReachabilityFromProviders()` 与 `TrySendClientHelloIfReachable(INetGameService)`。但旧桥通过 Harmony transpiler 改写 RitsuLib 的 `RunManager` 发送重载；没有公开 NetService resolver/override hook，故不能作为 v0.6 的公开替代方案。

## Android 状态

Android probe 源码、隔离项目、精确两文件 staging target、四进程运行脚本与严格 marker verifier 已完成。此工作区未提供 `ANDROID_STS2_DATA_DIR`、MuMu/ADB、设备 MOD/log 路径或已核验的 Android Ritsu release package，故无法执行设备构建、安装和四进程采集。该项为 BLOCKED，不可跳过。

| Gate | Verdict | Evidence |
|---|---|---|
| Tail order/cursor | PASS | desktop matrix: frozen 36-byte Tail; all LAN end/Ritsu start offsets = 288 |
| Four install combinations | PASS | `RitsuInteropMatrixTests`: false/false, true/false, false/true, true/true |
| Complete extension inventory | BLOCKED | no exported complete inventory API |
| Critical classification | BLOCKED | public `RegisterBytes` has id/version/callbacks only; no public critical classification |
| Android generic/Harmony | BLOCKED | device, matching Android data directory, official package hash and four runtime logs unavailable |
| Sidecar public replacement | BLOCKED | handshake drivers public, but replacement for RC4 RunManager resolver transpiler is not public |

RitsuLib free mixing is removed from the executable alpha scope pending design review; the RC4 private bridge remains prohibited.

## 复现和接受条件

桌面重跑：

```sh
DOTNET_ROOT=/Users/mac/.dotnet PATH=/Users/mac/.dotnet:$PATH \
  /Users/mac/.dotnet/dotnet test research/prototypes/v0.6-tail-ritsulib/Sts2TailPrototype.csproj -m:1 \
  -p:Sts2DataDir='/Users/mac/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64' \
  -p:RitsuLibAssembly="$RITSULIB_ASSEMBLY"
```

预期矩阵和 sidecar API 测试通过；public inventory/criticality gate 失败即为当前 BLOCKED 结论。Android 条件满足后，以 `run_android_probe.sh` 运行四个独立进程，并用 `verify_android_probe.py` 验证。with-Ritsu marker 必须包含加载的 manifest ID/version、`lib/<api>/STS2-RitsuLib.dll`、至少一个 Harmony owner/target 和每个 target 的 `IsGenericMethod`、`IsGenericMethodDefinition`、`ContainsGenericParameters`；任何 open generic、`InvalidProgramException`、Tail 哈希或 cursor 漂移都会阻断门禁。
