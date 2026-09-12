# 实施任务书：v0.6.2-alpha.1 —— native_bus 消息 ID 改为「按对端寻址」

本文件是给执行者的**自包含**任务书。设计依据见
`docs/superpowers/specs/2026-09-08-v0-6-2-peer-addressed-type-id-design.md`（**动手前请完整读一遍**）。

仓库根目录：`/Users/mac/Desktop/STS2-Game-Lobby`

## 0. 背景一句话

`native_bus_v1` 的线格式是 `[typeId:1][senderId:8 小端][payload]`，其中 `typeId` 是**接收方本地消息表的下标**，由游戏按本机 MOD 集合排序分配。0.6.1 发送时写的是**自己**的下标，因此隐含「两端表必须相同」，并靠服务端的 registry fingerprint 门禁强制这一点——结果是任何一端多装一个注册 `INetMessage` 的 MOD（例如 `Map Enhance Mod`）就无法跨端联机。

本次改为：**发送时写对端声明的下标**，门禁随之撤除。

## 1. 硬性约束

- **不要执行 `git commit` / `git push` / `git tag`**。只改工作区文件。
- **不要碰** `releases/`、`tem/`、`.omo/`、`.zcode/`、`node_modules/`、任何 `*.pck`、`*.dll`。
- **不要改动** `compat_4_5_v1` 相关逻辑（carrier=none 路径）、位宽 transpiler、传输层补丁、配对屏障 / 分发屏障、`PacketWriter.Reset` pending 语义、capacity / gameplay / save 相关补丁。
- **不要新增错误码**。缺失/非法的对端 typeId 一律复用既有 `lan_type_id_mismatch`；服务端字段缺失复用既有 `lan_registry_fingerprint_required`。改动 `LanConnectRejectionCodec.cs` 的码表会破坏跨版本解码。
- 保持既有代码风格：中文注释、`internal` 可见性、现有命名习惯。
- 不要为了让测试通过而删除或弱化既有断言；确需修改断言时，在报告里逐条说明原因。

## 2. 任务 A —— 客户端协议层

### A1. 帧语义

`sts2-lan-connect/Scripts/Protocol/NativeBus/LanConnectNativeBusMessage.cs`

- 外层帧版本常量 `ver` 由 `1` 改为 `2`。
- `localTypeId` 字段语义改为「**本帧寻址到的 typeId**（= 接收方本机的 id）」，更新该文件与相关注释的说明。
- 解码时 `ver != 2` ⇒ 现有的 `invalidReason` 路径（最终映射为 `lan_native_frame_invalid`），不得静默接受。

### A2. 发送端

`sts2-lan-connect/Scripts/Protocol/NativeBus/LanConnectNativeBusSender.cs`

- `Send(...)` 增加参数 `int recipientTypeId`。
- 线头字节写 `checked((byte)recipientTypeId)`；帧内 `localTypeId` 同样写 `recipientTypeId`（两者必须相等）。
- `recipientTypeId` 不在 `0..255` ⇒ 抛 `LanConnectProtocolFailureMapper.FromLocalException("lan_type_id_mismatch", ...)`。
- `ResolveTypeId()`（本机 id）保留不动——接收路径与启动自检仍然依赖它。

### A3. 接收端

**不需要任何改动。** 请确认并在报告中说明这两处在新语义下依然正确：

- `Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs:725` 的 `__0[0] != (byte)ResolveTypeId()`
- `Scripts/Protocol/Patches/LanConnectTailMessageRuntime.cs:746` 的 `extension.LocalTypeId != (uint)ResolveTypeId()`

（新不变量：线头字节 == 帧内 localTypeId == 接收方本机 id。）

### A4. 对端 ID 的存放与透传

`sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessageRuntime.cs`

- `NativeFlow` 增加 `int PeerNativeBusTypeId`。
- `Binding.BindBidirectionalNativeFlow(localPeerId, remotePeerId, flowNonce)` 增加 `int peerNativeBusTypeId` 参数并存入两个方向的 flow。
- `PrepareHostNativeFlow(service, peerNetId, flowNonce)` 增加 `int peerNativeBusTypeId`。
- `EnsureClientNativeFlowBound()` 使用 binding 上记录的房主 typeId（见 A5）。
- `BeginBinding`（约 `:1433`）增加可空的 `int? peerNativeBusTypeId`，与既有 `byte[]? protocolFlowNonce` 同样处理。
- 两处 `LanConnectNativeBusSender.Send(...)` 调用点（约 `:504` 与 `:873`）改为传 `flow.PeerNativeBusTypeId`。
- 取不到有效对端 ID ⇒ 结构化失败 `lan_type_id_mismatch`，不得静默回退到本机 ID。

### A5. 大厅 / DTO 链路

新增字段一律命名 `nativeBusTypeId`（JSON）/ `NativeBusTypeId`（C#），类型为可空整数，合法范围 `0..255`。

- `Scripts/Protocol/LanConnectProtocolOffer.cs`：`LanConnectProtocolOffer` 增加 `int? NativeBusTypeId`；`CreateCurrent()` 里用与现有 `RegistryFingerprint` **完全相同的 try/catch 形状**取 `LanConnectNativeBusSender.ResolveTypeId()`，失败留 null。
- `Scripts/Lobby/LanConnectLobbyModels.cs`：
  - `LobbyProtocolOfferDto` 增加 `NativeBusTypeId`，`FromValue` 透传。
  - `LobbyJoinRoomRequest` 增加顶层 `NativeBusTypeId`（与既有 `RegistryFingerprint` 并列）。
  - `LobbyJoinRoomResponse` 增加 `HostNativeBusTypeId`。
  - 控制通道 envelope 类（含 `ProtocolFlowNonce` 的那个，约 `:533`）增加 `PeerNativeBusTypeId`。
  - `LobbyProtocolSelectionDto` 增加 `NativeBusTypeId`。
- `Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs:229` 一带：`RequestTicketAsync` 里随 `RegistryFingerprint` 一起把 `NativeBusTypeId` 填进 join 请求。
- `Scripts/Lobby/LanConnectLobbyJoinFlow.cs` / `LanConnectLobbyManagedJoinFlow.cs`：加入侧把 join 响应里的 `HostNativeBusTypeId` 一路带到 `BeginBinding`，路径与既有 `GetProtocolFlowNonceBytes()` 完全平行。
- `Scripts/Lobby/LanConnectLobbyRuntime.cs:2398` 一带：房主侧从控制 envelope 读 `PeerNativeBusTypeId`，与 `flowNonce` 一起传给 `PrepareHostNativeFlow`；缺失或越界时走与现有 `InvalidDataException / LanConnectProtocolException` 相同的处理分支，**不要**默默继续。

### A6. 诊断

`sts2-lan-connect/Scripts/Protocol/NativeBus/LanConnectNativeBusStartupCheck.cs`

`LogDiagnostics` 目前只在 `Scripts/Entry.cs:52` 调用一次，而那时裁决恒为 `Pending`，导致 `native_bus: ready local_type_id=… registry_fingerprint=…` 从不出现在日志里。请在 `EnsureReadyOrThrow()` 得出**终局**裁决（非 Pending）后补调用一次 `LogDiagnostics`，且保证同一裁决只打印一次（用现有 `Sync` 锁 + 一个 bool 标志）。

## 3. 任务 B —— 服务端（lobby-service）

### B1. `src/protocol-capabilities.ts`

- `ProtocolOffer` 增加 `readonly nativeBusTypeId?: number | undefined`。
- `RoomProtocolSelection` 增加 `readonly nativeBusTypeId?: number | undefined`。
- 新增 `isValidNativeBusTypeId(value): value is number`：整数且 `0 <= value <= 255`。
- `selectRoomProtocol`：`profile === "tail_v1"` 时，`nativeBusTypeId` 非法或缺失 ⇒ 抛 `ProtocolContractError(409, "lan_registry_fingerprint_required", "创建新协议房间必须携带本机 native bus 消息 ID（0-255），请升级 LAN Connect。")`；合法则写进 selection。
- `native_bus_v1` 的 `minimumClientVersion` 由 `0.6.1-alpha.1` 改为 `0.6.2-alpha.1`。
- capability digest 的输入**不得**包含 `nativeBusTypeId`（与 `registryFingerprint` 一样是 selection 的独立字段，参与哈希会造成不必要的房间不兼容）。

### B2. `src/store.ts`

- `runTailJoinGates`：
  - **删除** `lan_registry_fingerprint_mismatch` 的抛出。指纹仍必须携带且格式合法（`lan_registry_fingerprint_required` 保留），不一致时不再拒绝——把双方值记录到 binding 供诊断。
  - 新增：join 的 `nativeBusTypeId` 非法或缺失 ⇒ `lan_registry_fingerprint_required`（文案说明是 native bus 消息 ID）。
  - `minimumClientVersion` 检查保持不变。
- **删除** `assertPreflightFingerprintAllowed` 方法（它只承担已被撤除的门禁）。
- `issueJoinTicket`：把加入者的 `nativeBusTypeId` 与 `protocolFlowNonce` 并列存进 ticket / binding（参考 `:533`、`:549`、`:561`、`:968`、`:1069` 的现有写法）。

### B3. `src/app.ts`

- `/rooms` 创建：protocol offer 的字段白名单增加 `nativeBusTypeId` 并解析为整数。
- `/rooms/:id/join`：请求体白名单增加顶层 `nativeBusTypeId`；join 响应增加 `hostNativeBusTypeId`（取自房间 selection），写法参考 `:1391` 的 `protocolFlowNonce`。
- 控制通道推给房主的 envelope（`:1444` 与 `:2115` 两处，与 `protocolFlowNonce` 同一对象）增加 `peerNativeBusTypeId`。
- `ReservedRelayedIdentityFields`（约 `:230`）增加 `"peerNativeBusTypeId"` 与 `"nativeBusTypeId"`，防止经中继 envelope 伪造。
- `/rooms/:id/mod-preflight`：**删除** 对 `assertPreflightFingerprintAllowed` 的调用（约 `:859`）。`registryFingerprint` 字段本身仍保留在白名单里（忽略即可，不再据此失败）。

## 4. 任务 C —— 版本号与文档

版本一律 `0.6.2-alpha.1`：

- `sts2-lan-connect/sts2_lan_connect.csproj`：`Version` / `AssemblyVersion`(`0.6.2.0`) / `FileVersion`(`0.6.2.0`) / `InformationalVersion`
- `sts2-lan-connect/sts2_lan_connect.json` 的 `version`
- `lobby-service/package.json` 的 `version`
- `sts2-lan-connect.Tests/Packaging/LanConnectPackageContentTests.cs` 与 `lobby-service/src/package-content.test.ts` 里的版本钉

> ⚠️ 这两个测试文件里存在**正则转义**形式的版本钉，例如
> `/<Version>0\.6\.1<\/Version>/`、`/v0\.6\.1/`。
> 纯文本替换 `0.6.1` **匹配不到**它们。请逐个搜索 `0\.6\.1` 与 `0.6.1` 两种形式并全部更新。

文档：

- `CHANGELOG.md` 新增 `## [0.6.2-alpha.1] - 2026-09-08` 小节（Added / Changed / Fixed / Compatibility），风格照抄既有 `[0.6.1]` 小节。
- 新建 `docs/RELEASE_NOTES_V0.6.2_ALPHA1_ZH.md`，内容基于设计文档的 §1、§3、§5，标注为 pre-release。校验和一栏留 `<待打包后填写>`，不要自行编造。
- **不要**改动 `README.md`、用户指南、部署指南等"当前正式版"文档——0.6.2-alpha.1 是预发布版，正式版仍是 0.6.1。

## 5. 任务 D —— 测试

新增/更新测试（放进既有对应测试工程与文件，沿用既有命名风格）：

C#（`sts2-lan-connect.Tests`）
1. `LanConnectProtocolOffer.CreateCurrent()` 携带 `NativeBusTypeId`；注册表不可用时为 null。
2. `LanConnectNativeBusSender.Send` 写入的线头字节 == 传入的 `recipientTypeId`，且帧内 `localTypeId` 与之相等。
3. 帧编码 `ver == 2`；解码 `ver == 1` 的帧被判定为非法（`InvalidReason` 非空）。
4. `recipientTypeId` 越界（<0 或 >255）⇒ 抛 `lan_type_id_mismatch`。
5. 现有的 registry fingerprint 计算测试全部保留（该值仍作为诊断使用），不要删。

TypeScript（`lobby-service/src`）
6. `tail_v1` 创建缺 `nativeBusTypeId` / 越界 ⇒ `lan_registry_fingerprint_required`；合法 ⇒ 冻结进 selection。
7. join 缺 `nativeBusTypeId` ⇒ 拒绝；**指纹与房间冻结值不一致时不再拒绝**（这是本次的核心回归测试，务必新增）。
8. join 响应包含 `hostNativeBusTypeId`。
9. 控制 envelope 包含 `peerNativeBusTypeId`，且 `sanitizeRelayedControlEnvelope` 会剥除它。
10. `minimumClientVersion` 为 `0.6.2-alpha.1`；0.6.1 客户端加入 ⇒ 426 `lan_client_version_too_old`。

既有测试里针对 `lan_registry_fingerprint_mismatch` 门禁行为的断言需要相应改写（改为断言"不再拒绝"），请在报告中列出你改了哪些。

## 6. 完成判据（必须全绿）

按顺序执行并把结果贴进报告：

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby/lobby-service && npm run check && npm run test
dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj -m:1
cd /Users/mac/Desktop/STS2-Game-Lobby && dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
```

`sts2-lan-connect.GdUnitTests` **不要**运行（需要真实游戏程序集，由调用者在本机单独跑）。

## 7. 报告要求（简短中文）

1. 改动文件清单（按任务 A/B/C/D 分组）。
2. 三条测试命令各自的通过数 / 失败数。
3. 你改写或删除的**既有**测试断言，逐条给出原因。
4. 任何你认为设计文档有问题、或实现中被迫偏离任务书的地方——**不要自行扩大改动范围**，写进报告由调用者裁决。
