# v0.6.2 设计：native_bus 消息 ID 改为「按对端寻址」

状态：设计（2026-09-08）
目标版本：`0.6.2-alpha.1`
前置事故：跨端（PC ↔ 安卓）无法加入新协议房间，`lan_registry_fingerprint_mismatch` / HTTP 409

## 1. 事故与根因

### 1.1 现象

用户报告"手机只能和手机连、电脑只能和电脑连"。两份日志（Mac `godot.log` + 安卓 `sts2.log`，2026-09-08）双向复现：

```
# Mac 加入手机房 '我的橙汁啊'
POST /rooms/a0f13c27.../join -> 409
overlay: status -> 双方的联机消息注册表不一致（通常是 Mod 列表不同），无法使用新协议加入。

# 手机加入 PC 房 '你的橙汁啊'
POST /rooms/ab6d4b29.../join -> 409
（同一文案）
```

排除项（两端完全一致）：客户端 `0.6.1+8922e88`、游戏 `v0.111.0`、游戏程序集 `0.1.0+41cef1ea`、`BaseLib v3.4.5`、`RitsuLib 0.5.20`、wire_cache 签名 `wcv1:D5-qRxko7ywoZJWzaOM9Q49NNOWP1Jr2qXc_Nk204uU`。

### 1.2 直接触发源

安卓端多装了 `Map Enhance Mod`（mod id `sts2_map_mod` 0.8.3，程序集 `sts2.mapcolormod`），它注册了自定义 `INetMessage`，且其 ID 归位补丁在安卓上失败：

```
[ERROR] [Sts2MapMod] ApplyNetworkMessagePatches: failed to apply custom message wrapper patches.
  ArgumentException: Undefined target method for patch method
  Sts2MapMod.Patches.MapModCustomMessageCachePatch::MoveWrapperToStableMessageId()
[ERROR] [Sts2MapMod] Failed to validate Map Mod custom INetMessage ordering. Disabling custom message sync for safety.
```

### 1.3 结构性根因

`native_bus_v1`（0.6.1 引入）把协议容器放进一条自注册的 `INetMessage`，线格式为 `[typeId:1][senderId:8 LE][payload]`。而 `typeId` 是 **接收方本地表的下标**，由游戏 `ContentSorter` 对**本机当前全部已注册消息类型**排序后确定性分配。

确定性 ≠ 跨机器一致：同一份 MOD 集合算出同一张表；MOD 集合不同则表不同，同一个下标在两台机器上指向不同类型。

0.6.1 的 `LanConnectNativeBusSender` 写的是**自己**的 typeId，因此隐含了「两端表必须相同」的前提。为了防止错投，0.6.1 加了 registry fingerprint 主门禁（对全表取 SHA-256，不一致即拒绝签发 join ticket）。门禁是有效的——线上没有静默错投，只有拒绝连接——但代价是：**任何一端多装一个注册 `INetMessage` 的 MOD，就完全无法与对方进入新协议房间**。

这与 0.6.1 发布公告「不要求装同一批 MOD」的承诺相悖，也与历史行为相悖：`compat_4_5_v1` / `standalone_tail_v1` / `ritsulib_sidecar_v1` 三种旧载体都不进入消息注册表，因此跨平台一直可用（见 §1.4）。

### 1.4 为什么以前没有这个问题

| 版本 | 载体 | 容器投递方式 | 依赖 `MessageTypes` |
|---|---|---|---|
| 0.3 / 0.4 / 0.5 | `compat_4_5_v1` | 改原版消息位宽 | 否 |
| 0.5.x / 0.6.0 | `standalone_tail_v1` | 追加在原版消息序列化尾部 | 否 |
| 0.6.0 | `ritsulib_sidecar_v1` | RitsuLib typed sidecar API | 否 |
| **0.6.1** | **`native_bus_v1`** | **自注册 `INetMessage`** | **是** |

`native_bus_v1` 的选型本身是对的（见 v0.7 native-bus spec §1.2：tail 载体与 RitsuLib 的 36 字节 trailer 争夺同一缓冲区；sidecar 载体是无契约的运行时依赖）。**本设计不推翻该选型**，只移除它附带的「两端表必须相同」前提。

### 1.5 为什么用户看不到"MOD 列表不同"

我们面向用户的三个 MOD 检查全部按 `affectsGameplay` 过滤，因此对这次差异完全隐形：

| 检查 | 实现 | 本次结果 |
|---|---|---|
| `ModList` | `LanConnectBuildInfo.cs:91` → `ModManager.GetGameplayRelevantModNameList()` | 两端相同 |
| mod-preflight 清单 | `LanConnectModInventoryBuilder.Build()`，根集合 = `mods.Where(AffectsGameplay)` | 两端 `localModCount=0` |
| wire_cache 签名 | ModelDb 内容 ID | 两端完全相同 |

`LanConnectRegistryFingerprint.Compute()` 是唯一不过滤 `affectsGameplay` 的检查。而 `Map Enhance Mod` 是 `affects_gameplay:false`。

## 2. 不变量（本设计确立，后续所有协议改动须遵守）

> **任何需要两端一致的线上取值，不得来自游戏的全局注册表或全局排序；只能来自 (a) 原版固定值，或 (b) 每连接协商值。**

## 3. 方案：按对端寻址（peer-addressed type id）

### 3.1 核心

`typeId` 是接收方本地表的下标，不是全局身份。因此**发送时应写接收方的下标，而不是自己的**。

```
今天：  A → B 写 A 自己的 id      ⇒ 必须 A.id == B.id ⇒ 需要全表指纹门禁
0.6.2： A → B 写 B 声明的 id      ⇒ 两表无需任何关系 ⇒ 门禁可撤
```

我方类型在两端**必然都存在**（进入 `tail_v1` 房间的前提就是双方都装了 LAN Connect），所以我方的注册行为本身不制造任何不对称；今天的不对称 100% 来自第三方 MOD 集合不同。改为按对端寻址后，第三方注册什么、增删改、跨不跨平台，都与本协议无关。

### 3.2 帧字段语义变更

外层帧布局不变：

```
[magic:2 = 0x4C 0x42][ver:1][localTypeId:4 BE][frameLen:4 BE][frame][尾随字节:忽略]
```

- `ver`：`1` → **`2`**。收到 `ver != 2` ⇒ `lan_native_frame_invalid`（结构化拒绝，不误读）。
- `localTypeId`：语义由「发送方自己的 id」改为 **「本帧寻址到的 id」= 接收方的 id**。

由此得到一条可自校验的不变量：

```
线头 packet[0]  ==  frame.localTypeId  ==  接收方本机 MessageTypes.TypeToId<LanConnectNativeBusMessage>()
```

**接收路径因此完全不需要改动**：
- `LanConnectTailMessagePatches.cs:725` 的 `__0[0] != (byte)ResolveTypeId()` 仍然正确；
- `LanConnectTailMessageRuntime.cs:746` 的 `extension.LocalTypeId != (uint)ResolveTypeId()` 仍然正确，只是含义从「你我编号不同」变为「你寻址错了」，沿用现有 `lan_type_id_mismatch`。

### 3.3 对端 ID 的取得路径（复用 protocolFlowNonce 已验证的轨道）

`protocolFlowNonce` 已经解决了「双方在首个扩展帧之前拿到同一份协商值」的问题，`nativeBusTypeId` 走完全相同的轨道，不新增通道：

| 方向 | 载体 | 现有对照 |
|---|---|---|
| 房主 → 服务端 | `POST /rooms` 的 protocol offer 内 `nativeBusTypeId`，冻结进 `ProtocolSelection` | `registryFingerprint` |
| 服务端 → 加入者 | join 响应新增 `hostNativeBusTypeId` | `protocolFlowNonce`（`app.ts:1391`） |
| 加入者 → 服务端 | `POST /rooms/:id/join` 顶层 `nativeBusTypeId` | `registryFingerprint` |
| 服务端 → 房主 | 控制通道 envelope 新增 `peerNativeBusTypeId`，与 `protocolFlowNonce` 同一条 | `app.ts:1444` / `app.ts:2115` |

时序与 `protocolFlowNonce` 一致，因此「首个扩展帧之前一定拿得到」这一性质是继承而来的，不需要重新论证。

`peerNativeBusTypeId` 必须加入 `app.ts:230` 的 `ReservedRelayedIdentityFields`，防止客户端经中继 envelope 伪造。

### 3.4 存放位置

对端 ID 是 per-peer 的（房主对不同客机可能不同），存放在既有的 `NativeFlow`（key 为 `(senderPeerId, recipientPeerId)`）上最自然：

- `Binding.BindBidirectionalNativeFlow(localPeerId, remotePeerId, flowNonce, peerNativeBusTypeId)`
- `NativeFlow.PeerNativeBusTypeId`
- 两处 `LanConnectNativeBusSender.Send(...)` 调用点（`LanConnectTailMessageRuntime.cs:504`、`:873`）已经持有 `recipientPeerId` 与 `flow`，直接取 `flow.PeerNativeBusTypeId` 传入。

发送时若对端 ID 缺失 ⇒ 结构化失败 `lan_type_id_mismatch`（不新增错误码，避免动 `LanConnectRejectionCodec` 的跨版本解码表）。

### 3.5 门禁改动

- **撤除** `store.ts` `runTailJoinGates` 中的 `lan_registry_fingerprint_mismatch` 抛出；改为把双方指纹记录在 binding 上供诊断。
- **撤除** `store.ts` `assertPreflightFingerprintAllowed` 及其在 `app.ts:859` 的调用（该函数只做门禁，不再需要）。
- **保留** `lan_registry_fingerprint_required`：`tail_v1` 创建与加入仍必须携带格式合法的指纹（成本为零，且是诊断的唯一来源）。
- **新增必填** `nativeBusTypeId`（整数 0..255）：`tail_v1` 创建与加入缺失或越界 ⇒ `lan_registry_fingerprint_required`（复用同码，文案区分）。
- `native_bus_v1` 的 `minimumClientVersion`：`0.6.1-alpha.1` → **`0.6.2-alpha.1`**。0.6.1 客户端加入 0.6.2 房间将得到明确的升级提示，而不是在 `ver` 校验处才失败。

### 3.6 诊断改动（本次事故暴露）

1. `LanConnectNativeBusStartupCheck.LogDiagnostics` 目前只在 `Entry.cs:52` 调用一次，而彼时 `AssemblyInfo` 必然未就绪、裁决恒为 `pending`，导致 `native_bus: ready local_type_id=… registry_fingerprint=…` **从不出现在任何日志里**。需在 `EnsureReadyOrThrow` 补跑得出终局裁决后再调用一次。
2. 指纹保留为诊断值：`lan_type_id_mismatch` 与握手失败时，日志同时打印本机 typeId、对端声明 typeId、本机指纹前缀。

## 4. 明确的非目标

1. 不改 `compat_4_5_v1`（carrier=none 路径完全不动）。
2. 不改载体选型：继续注册自己的 `INetMessage`，继续走原版消息总线，继续与 RitsuLib 零交叠。
3. 不改传输层、配对屏障、分发屏障、`PacketWriter.Reset` pending 语义。
4. 不改位宽 transpiler、capacity / gameplay / save 相关补丁。
5. 不做自有 ENet 通道（方案 C）：配对屏障依赖同通道 FIFO 背靠背（`BarrierKey(SenderPeerId, Channel)`，`LanConnectTailMessageRuntime.cs:722` 显式拒绝非 0 通道），换通道需把「相邻即配对」改为按 `flowNonce + sequence` 显式关联；在本设计落地后其边际收益很小，不在 0.6.2 范围内。
6. 不做「借用原版消息 ID」：会把「投递错 ⇒ 未知 ID 无害丢弃」（`NetMessageBus.cs:59-71`）降级为「投递错 ⇒ 原版拿垃圾反序列化一个真实类型」，是拿安全换独立，已否决。
7. 不对任何具体第三方 MOD 做适配。
8. LAN 建房 / LAN 直连 / 仅 LAN 续局仍为 compat-only（0.6.1 既有非目标，不变）。

## 5. 兼容性

| 组合 | 结果 |
|---|---|
| 0.6.2 ↔ 0.6.2，MOD 集合不同（含跨平台） | **正常**（本设计的目标） |
| 0.6.2 ↔ 0.6.1，`tail_v1` | 服务端 `minimumClientVersion=0.6.2-alpha.1` 拒绝，提示升级 |
| 0.6.1 帧误达 0.6.2 客户端 | `ver=1` ⇒ `lan_native_frame_invalid`，结构化拒绝 |
| 0.6.2 客户端 ↔ 0.6.1 服务端 | 旧服务端仍执行指纹门禁；行为与 0.6.1 相同（不回归） |
| compat_4_5_v1 房间 | 完全不受影响 |
| 第三方 MOD 之间的消息 ID 位移 | 既有生态属性，本设计既不改善也不恶化 |

## 6. 验收

**自动化**

- `cd lobby-service && npm run check && npm run test`
- `dotnet test sts2-lan-connect.ProtocolPlanTests/... -m:1`
- `dotnet test sts2-lan-connect.Tests/... -m:1`
- `RITSULIB_ASSEMBLY=<官方 dll> ./scripts/verify-release.sh`（含 GdUnit 与打包白名单）

**新增测试点**

1. offer 携带 `nativeBusTypeId`；注册表不可用时为 null 且创建被服务端拒绝。
2. 发送端写入的线头字节 == 传入的 `recipientTypeId`，且 `frame.localTypeId` 与之相等。
3. `ver=2` 编码；解码 `ver=1` ⇒ `lan_native_frame_invalid`。
4. `NativeFlow` 携带 `PeerNativeBusTypeId`；缺失时发送抛 `lan_type_id_mismatch`。
5. 服务端：`tail_v1` 创建缺 `nativeBusTypeId` ⇒ 拒绝；join 缺失 ⇒ 拒绝；**指纹不一致不再拒绝**；join 响应含 `hostNativeBusTypeId`；控制 envelope 含 `peerNativeBusTypeId` 且在 sanitize 列表中。
6. 打包内容测试版本钉为 `0.6.2-alpha.1`。

**真机双实例 E2E（发布阻断）**

| 场景 | 期望 |
|---|---|
| 两端 typeId 不同（人为在一端多装一个注册 `INetMessage` 的 MOD）建房 + 加入 | 通过 |
| 两端 typeId 相同 | 通过（回归） |
| 开局 / 局内 / 存档 / 续局 / QuickSL 多人 SL | 通过（回归） |
| 0.6.1 客户端加入 0.6.2 房间 | 明确的版本升级提示，非超时 |
| compat 房间全流程 | 通过（回归） |

## 7. 影响文件（实施索引）

**客户端**

- `Scripts/Protocol/NativeBus/LanConnectNativeBusSender.cs` — `Send` 增加 `recipientTypeId`；线头与帧字段均写该值
- `Scripts/Protocol/NativeBus/LanConnectNativeBusMessage.cs` — `ver` 1→2；`localTypeId` 语义注释
- `Scripts/Protocol/NativeBus/LanConnectNativeBusStartupCheck.cs` — 终局裁决后补打诊断
- `Scripts/Protocol/LanConnectProtocolOffer.cs` — 新增 `NativeBusTypeId`
- `Scripts/Protocol/Patches/LanConnectTailMessageRuntime.cs` — `NativeFlow.PeerNativeBusTypeId`；`BindBidirectionalNativeFlow` / `PrepareHostNativeFlow` / `EnsureClientNativeFlowBound` 透传；两处 `Send` 调用点取值
- `Scripts/Lobby/LanConnectLobbyModels.cs` — DTO：offer、join 请求/响应、控制 envelope
- `Scripts/Lobby/LanConnectLobbyRuntime.cs:2398` 一带 — 控制 envelope 取 `peerNativeBusTypeId` 并透传给 `PrepareHostNativeFlow`
- `Scripts/Lobby/LanConnectLobbyJoinFlow.cs` / `LanConnectLobbyManagedJoinFlow.cs` — 加入侧把 `hostNativeBusTypeId` 带进 binding
- `Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs` — join 请求透传

**服务端**

- `src/protocol-capabilities.ts` — `ProtocolOffer` / `RoomProtocolSelection` 新字段与校验；`minimumClientVersion`
- `src/store.ts` — 创建校验、`issueJoinTicket` 存储、`runTailJoinGates` 撤除 mismatch 抛出、删除 `assertPreflightFingerprintAllowed`
- `src/app.ts` — `/rooms` 与 `/rooms/:id/join` 字段白名单、join 响应、控制 envelope、`ReservedRelayedIdentityFields`、删除 preflight 门禁调用

**版本与文档**

- `sts2-lan-connect/sts2_lan_connect.csproj`、`sts2-lan-connect/sts2_lan_connect.json`、`lobby-service/package.json` → `0.6.2-alpha.1`
- `sts2-lan-connect.Tests/Packaging/LanConnectPackageContentTests.cs`、`lobby-service/src/package-content.test.ts` 版本钉
- `CHANGELOG.md`、`docs/RELEASE_NOTES_V0.6.2_ALPHA1_ZH.md`
