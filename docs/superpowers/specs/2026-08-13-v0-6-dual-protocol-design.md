# STS2 LAN Connect v0.6 双协议迁移设计

- **创建日期**：2026-08-13
- **状态**：Approved - RitsuLib presence 同质化语义，已完成独立终审（2026-08-14）
- **目标版本**：客户端与 lobby-service `0.6.0-alpha.1`
- **作者**：项目维护者 + 协作 brainstorming

## 1. 背景

LAN Connect 当前通过 Harmony 修改 STS2 原版多人消息中的位宽：

- 历史 `legacy_4p` 使用 `slotId=8-bit`、玩家列表长度 `3-bit`，用于兼容实际发布过的 `0.2.2` 客户端。
- 当前 `extended_8p` 使用 `slotId=4-bit`、玩家列表长度 `5-bit`，用于支持 5 人以上房间。

该方案要求消息发送端、接收端、Harmony 加载顺序以及所有修改相同消息的 MOD 对位宽保持完全一致。RC2 至 RC4 的现场反馈证明，这一要求在 RitsuLib、Windows Harmony wrapper 和 Android 泛型反射同时存在时不够稳定：

- RitsuLib 可能先为闭合泛型消息总线生成使用原版列表位宽的 wrapper。
- LAN Connect 随后修改消息类型方法，无法保证已经生成的外层 wrapper 使用相同位宽。
- 发送端和接收端位宽不一致会使后续 bitstream 整体错位，表现为黑屏、停留加载页或误导性的模型查找错误。
- 为组合两套补丁而反射、卸载和恢复 RitsuLib 私有 postfix，又引入了 Windows `InvalidProgramException` 和 Android 开放泛型方法形状差异。

`STS2-MultiplayerLimitBreak` 0.2.x 已采用更稳定的方向：原版消息主体保持原版位宽，完整扩展 roster 放入版本化消息尾。本设计采用这一协议思想，但由 LAN Connect 自己拥有最小 LAN protocol v1。无 RitsuLib 房间把它作为独立消息尾；全员 RitsuLib 房间通过 RitsuLib 的公开 typed-sidecar API承载同一逻辑容器，避免两个 MOD争夺同一消息尾的补丁所有权。

项目维护者已决定：

- 放弃 `0.2.x` 客户端兼容。
- 保留 `0.3.x-0.5.x` 客户端混房能力。
- 新协议首版正式支持 2-8 人。
- 新协议使用 LAN Connect 内置 Tail；房间冻结 RitsuLib presence，只有 presence 相同的客户端可以连接。
- 客户端与 lobby-service 同步升级为 `0.6.0-alpha.1`。

## 2. 目标与非目标

### 2.1 目标

1. 新协议路径不修改 STS2 原版 `slotId=2-bit` 和玩家列表长度 `3-bit`。
2. 新协议支持 2-8 人，并保留将来扩展到 16 人的编码空间，但首版不承诺 9-16 人。
3. 保留 `0.3.x-0.5.x` 客户端通过显式兼容房与 `0.6.x` 客户端混房。
4. `0.2.x` 客户端在签发 join ticket 前被明确拒绝，不再进入游戏握手。
5. `tail_v1` 房间内 RitsuLib presence 必须完全一致：有 RitsuLib 只能连接有 RitsuLib，无 RitsuLib 只能连接无 RitsuLib。
6. 不再依赖 RitsuLib 私有 Harmony postfix、patch owner、priority 或内部泛型类型；全员 RitsuLib 房间只使用公开 sidecar API。
7. 将不兼容从开局后的黑屏提前为建房或加入阶段的结构化错误。
8. 为 v0.6 两种载体建立字节级契约测试和真实跨平台联机门禁。

### 2.2 非目标

1. 不兼容 `0.2.x` 客户端及其 `8/3-bit` wire profile。
2. 不在 `alpha.1` 正式支持 9-16 人。
3. 不重写 STS2 的完整网络协议、传输层、动作同步或断线重连机制。
4. 不枚举、分类或协商 RitsuLib 网络扩展，也不承诺同为 RitsuLib 客户端时其版本或第三方扩展一定兼容；这些兼容性继续由原版 MOD inventory 与 RitsuLib 自身机制负责。
5. 不允许兼容模式与 RitsuLib 共存。
6. 不允许房间创建后在两种 wire profile 之间自动切换。
7. 不把 Harmony patch 枚举作为长期扩展服务发现机制。
8. 不捕获、冻结或发布 `0.3.x-0.5.x` 客户端流量 fixture；旧客户端真实互通不作为 `alpha.1` 测试或发布门禁。

## 3. 产品模式

建房时用户必须在两种模式中选择。默认选中兼容模式，房间创建后模式不可变。

| 模式 | 默认 | 支持客户端 | Wire 格式 | RitsuLib | 人数 |
|---|---|---|---|---|---:|
| 兼容模式 | 是 | `0.3.x-0.6.x` | 历史 `4/5-bit` | 禁止 | 2-8 |
| 0.6 新协议 | 否 | 仅 `0.6.x` | 原版 `2/3-bit` + LAN protocol v1（载体由 presence 冻结） | 同房 presence 必须一致 | 2-8 |

### 3.1 兼容模式

建议界面文案：

> **兼容旧版客户端（默认）**  
> 支持 LAN Connect 0.3-0.5 客户端加入。该模式使用历史多人协议，不支持 RitsuLib。

行为：

- profile 为 `compat_4_5_v1`。
- `slotId=4-bit`，玩家列表长度 `5-bit`。
- 4 人房也使用 `4/5-bit`，不再根据人数进入历史 `8/3-bit`。
- 房主检测到 RitsuLib 时不能创建兼容房。
- 启用 RitsuLib 的客户端不能加入兼容房。
- 旧 `0.3.x-0.5.x` 客户端创建的房间由服务端映射为该 profile。

### 3.2 0.6 新协议

建议界面文案：

> **0.6 新协议（支持 RitsuLib）**  
> 使用更稳定的扩展协议，仅支持 LAN Connect 0.6 或更高版本。低版本客户端无法加入。

行为：

- profile 为 `tail_v1`。
- 原版消息主体始终使用 STS2 原版位宽。
- 完整 roster 由 LAN protocol v1 携带。
- 无 RitsuLib 时使用独立 LAN Tail carrier；全员 RitsuLib 时使用公开 RitsuLib typed-sidecar carrier。
- 房主建房时冻结本地 RitsuLib presence；加入者 presence 不一致时在 ticket 前拒绝。
- LAN Connect 不校验 RitsuLib 版本或扩展集合；同为 RitsuLib 只表示允许进入后续原版/RitsuLib 兼容检查。
- `<0.6` 客户端在服务端签发 join ticket 前被拒绝。

## 4. 协议架构

### 4.0 实施前可行性门禁

在修改生产客户端协议前，必须先用真实 STS2、Harmony 和 RitsuLib 程序集完成一次独立原型。原型必须经过真实 `NetMessageBus`/transport 与实际补丁链，证明两个允许组合能消费同一 LAN protocol v1 容器：

1. 房主无 RitsuLib、加入者无 RitsuLib：允许。
2. 房主有 RitsuLib、加入者有 RitsuLib：允许；LAN 容器只经公开 typed-sidecar 发送，原版消息后不得出现独立 LAN Tail。
两个禁止组合不进入 wire 原型。服务端 selection 落地后、Ritsu 客户端切片开始前，必须用真实 store/app 集成测试和 allocation/transport spies 证明房主有/客机无、房主无/客机有两个方向都在 ticket 前以 `ritsulib_presence_mismatch` 拒绝，且 slot/ticket/control/transport 计数均为 0。纯布尔相等测试不能作为该门禁证据。

纯 direct-IP 没有 lobby ticket，也没有能在两种载体之前安全运行的共同 presence 预检。`alpha.1` 因此不开放 direct-IP `tail_v1`：direct-IP 只允许 `compat_4_5_v1`，且本地检测到 RitsuLib 时在创建 transport 前拒绝。不得把载体超时猜测为结构化 presence mismatch。

无 RitsuLib 组合的顺序固定为：

```text
vanilla body -> standalone LAN Tail v1
```

在全员 RitsuLib 房间中，LAN Connect 不向原版消息追加任何字节，也不调用 `RitsuNetMessageTailExtensions.Write/Read`。它只把相同 LAN protocol container 包入 §4.2.1 的公开 typed-sidecar carrier，并在原版消息 handler 前完成配对与验证。无 RitsuLib 房间不得加载或调用任何 Ritsu API。

如果真实 RitsuLib 的公开 sidecar 契约不能在原版 handler 前稳定完成配对、authority 校验与 roster 恢复，则暂停 RitsuLib 同质房切片并回到设计评审。单边安装必须在大厅流 transport 前拒绝；`alpha.1` 不开放 direct-IP Tail。不得恢复 RC4 私有 postfix 桥，也不得回退到双 MOD争用消息尾的方案。

### 4.1 单一所有权

在 `tail_v1` 中，LAN Connect 是以下逻辑行为的唯一所有者：

- 原版 roster 投影。
- LAN protocol container 编码与解码。
- authoritative roster 验证与恢复。
- LAN protocol capability 声明。

无 RitsuLib 时 LAN Connect 独占 standalone Tail 的外层补丁。全员 RitsuLib 时 RitsuLib 独占 sidecar transport/patch，LAN Connect 只注册和发送自己的公开 typed message。LAN Connect 不拥有 RitsuLib 的容器或补丁；RitsuLib 不解释也不拥有 LAN roster 语义。

### 4.2 消息布局

```text
STS2 original message
|- vanilla body
|  |- at most four projected players
|  |- slotId: original 2 bits
|  `- player list count: original 3 bits
|- LAN protocol container v1
|  |- magic and bounded container header
|  |- selected wire protocol version
|  |- authoritative 2-8 player roster
|  |- frozen RitsuLib presence
|  `- bounded payload entries
`- carrier selected by frozen presence
   |- absent: standalone message tail
   `- present: RitsuLib typed sidecar
```

LAN protocol container 必须是自描述、有界和可跳过的容器。无 RitsuLib 时先把原版 writer 以零位补齐到下一个 byte boundary，再把下列 container bytes 直接追加到消息尾；全员 RitsuLib 时完全相同、从 magic 开始的 bytes 作为 §4.2.1 carrier frame 的 `container`，不包含 standalone 对齐位。schema 在任何生产切片开始前固定如下；数字均使用网络字节序，所有长度在分配前用 checked arithmetic 验证：

```text
magic[8] = ASCII "STSLAN01"
containerVersion: uint8 = 1
flags: uint8 = 0 (unknown bits are an error)
containerByteLength: uint32
sessionProtocolVersion: uint16
entryCount: uint16
entries[]:
  idByteLength: uint8
  id: UTF-8 bytes
  version: uint16
  flags: uint8 (bit 0 = critical; other bits must be zero)
  payloadByteLength: uint32
  payload: bytes
```

保留 entry：

- `lan.capabilities`：房间选定协议、客户端能力和冻结的 RitsuLib presence。
- `lan.roster`：authoritative roster。
- `lan.rejection`：与原版 `ClientConnectionFailedMessage` 配对的结构化拒绝原因。

三个 LAN 保留 entry 的 envelope `flags` 固定为 `1`（critical），entry version 均固定为 1。发送端必须按 entry ID 的原始 UTF-8 byte sequence 升序编码所有 entry；接收端允许 entry 以任意顺序出现，但必须拒绝重复 ID，并在验签、摘要和日志中按相同升序规范化。该规则固定 golden bytes，同时避免把输入顺序当作协议语义。

#### `lan.capabilities` payload v1

```text
recordKind: uint8

when recordKind = 1 (peer offer):
  lanProtocolMin: uint16
  lanProtocolMax: uint16
  clientVersionByteLength: uint8
  clientVersion: UTF-8 bytes
  flags: uint16 (bit 0 = RitsuLib present; bit 1 = public typed-sidecar carrier available; other bits must be zero)

when recordKind = 2 (session selection):
  selectedLanProtocolVersion: uint16
  carrier: uint8 (1 = standalone Tail, 2 = RitsuLib typed sidecar)
  flags: uint16 (bit 0 = room requires RitsuLib present; other bits must be zero)
```

限制：

- `recordKind` 只允许 1 或 2。
- 客户端版本最多 32 UTF-8 bytes，必须是规范化非空版本字符串。
- peer offer 与 session selection 的 RitsuLib presence/carrier 必须与 HTTP 冻结 selection 一致。presence=true 时 offer 必须同时声明 typed-sidecar available。
- `InitialGameInfoMessage`、三种成功 response、LAN rejection、player-joined 和 begin-run 使用 session selection。
- `ClientLobbyJoinRequestMessage`、`ClientLoadJoinRequestMessage` 和 `ClientRejoinRequestMessage` 使用 peer offer。
- 收到不适用于当前消息的 record kind 时拒绝消息。

#### `lan.roster` payload v1

```text
schemaVersion: uint8 = 1
snapshotKind: uint8 = 1 (full snapshot; other values are an error)
authorityPeerId: uint64
rosterRevision: uint32
playerCount: uint8
players[] in canonical order:
  playerId: uint64
  realSlotId: uint8
  vanillaPlayerBitLength: uint32
  vanillaPlayerBytes: ceil(vanillaPlayerBitLength / 8) bytes
```

每个 `vanillaPlayerBytes` 是目标 STS2 API profile 的原版 player carrier `Serialize` 结果。编码前创建临时副本，将其 embedded slot 固定为该玩家 canonical index `% 4`；解码时使用相同 API profile 的原版 `Deserialize`，验证 embedded player ID 等于外层 `playerId`、embedded slot 等于 canonical index `% 4`，再把 `realSlotId` 写回。子序列化使用独立 reader/writer，不依赖外层 cursor。

限制：

- `playerCount` 必须为 2-8。
- `realSlotId` 必须为 `0..7`，player ID 和 real slot 都不得重复。
- 玩家按 real slot 升序、再按 player ID 升序排列。
- 每个原版 player 子 payload 最大 16 KiB，所有子 payload 合计仍受容器 256 KiB 上限约束。
- `vanillaPlayerBitLength` 必须大于零且不超过对应字节数组容量；最后一个 byte 中未使用的高位必须为零。
- 原版子 reader 必须恰好消费声明的 bit length，否则拒绝整个 roster。
- `authorityPeerId`、transport sender 和当前 host peer 必须一致。
- `rosterRevision` 对实际 membership/slot mutation 严格递增。首次成功 response 可按 §4.4 bootstrap；后续 current-state response 可重复当前 revision，但 snapshot 必须与已接受 baseline 完全一致。mutation snapshot 重复/倒退、current-state snapshot 倒退或同 revision 不同内容均拒绝。

该结构避免 LAN Connect 复制角色、解锁、难度和版本信息的内部 wire schema，同时仍通过外层真实 slot 和 identity 检查扩展到 8 人。游戏版本和 wire-cache signature 门禁保证双方使用相同的原版 player carrier schema。

#### `lan.rejection` payload v1

该 entry 的 entry version 固定为 1，critical flag 必须为 1，仅允许出现在与 `ClientConnectionFailedMessage` 配对的 LAN container：

```text
schemaVersion: uint8 = 1
reasonCode: uint16
requiredClientVersionByteLength: uint8
requiredClientVersion: UTF-8 bytes (0 length means not applicable)
requiredRitsuLibPresence: uint8 (0 = not applicable, 1 = must be absent, 2 = must be present)
detailByteLength: uint16
detail: UTF-8 bytes for diagnostics only
```

`reasonCode` 固定值：

```text
1 = client_update_required
2 = protocol_profile_unsupported
3 = ritsulib_not_allowed_in_compat_mode
4 = ritsulib_presence_mismatch
5 = game_version_mismatch
6 = wire_cache_mismatch
7 = lan_tail_required
8 = lan_tail_malformed
9 = lan_protocol_version_mismatch
10 = ritsulib_sidecar_unavailable
11 = reserved (must be treated as unknown protocol rejection)
```

限制：客户端版本最多 32 UTF-8 bytes，detail 最多 512 UTF-8 bytes；`requiredRitsuLibPresence` 只允许 0、1、2。客户端只按 `reasonCode` 和结构化字段决定 UI；detail 不参与逻辑或兼容判断。未知 reason code 显示通用协议拒绝并记录原值，不继续加入。

固定限制：

- 容器最大 256 KiB。
- entry 最多 32 个。
- 单 entry payload 最大 64 KiB。
- entry ID 最多 64 UTF-8 bytes，不能为空。
- roster 必须包含 2-8 名玩家。
- entry ID 不得重复。
- container 必须被恰好消费，不允许 LAN Tail 内出现未声明 trailing bytes。
- standalone carrier 的前置对齐 padding 最多 7 bits 且必须全为零；它不计入 container bytes 或 `containerByteLength`。sidecar carrier 没有这段 padding。

版本一致性规则：

- 三种 request（`ClientLobbyJoinRequestMessage`、`ClientLoadJoinRequestMessage`、`ClientRejoinRequestMessage`）的 peer offer 尚未获准进入当前 session，`sessionProtocolVersion` 必须为 0。
- 其余 `tail_v1` 消息已经绑定房间 selection，`sessionProtocolVersion` 必须等于房间选定 LAN protocol；`alpha.1` 中固定为 1。
- `lan.capabilities` entry version 固定为 1；peer offer 的 `recordKind=1` 只能出现在 `sessionProtocolVersion=0` 的三种 request，session selection 的 `recordKind=2` 只能出现在非零且匹配房间 selection 的消息。
- `lan.roster` entry version 和其 payload 内 `schemaVersion` 都固定为 1，二者任一不为 1 即拒绝。
- session selection payload 中的 `selectedLanProtocolVersion` 必须等于容器 `sessionProtocolVersion`。
- 任一重复版本字段不一致时拒绝整条消息，不允许选择其中一个继续解析。

`containerByteLength` 从 `containerVersion` 开始计到最后一个 payload，便于先验证边界再读取 entry。未知非关键 entry 可按长度跳过；未知关键 entry、未知 container version、重复 ID、越界长度或未完全消费均使整条消息无效。standalone carrier 还必须单独拒绝非零前置对齐 padding。

standalone carrier 中 LAN reader 只能消费自己的 `containerByteLength`，不得接受或吞掉后续私有容器。Ritsu-present selection 禁止使用 standalone carrier。

### 4.2.1 RitsuLib typed-sidecar carrier v1

全员 RitsuLib 房间使用公开 `RitsuLibSidecarTypedMessageRegistry` 注册一个 required descriptor：

```text
ModuleId = "sts2_lan_connect"
MessageKey = "protocol_v1"
Delivery = StableSync
Required = true
```

descriptor payload 固定为：

```text
carrierVersion: uint8 = 1
messageKind: uint8
flowNonce[16]: cryptographically random bytes
messageSequence: uint32
containerByteLength: uint32
container: exact LAN protocol container v1 bytes beginning with "STSLAN01"
```

`messageKind` 依 §4.4 的十种消息按表格顺序编号 `1..10`。`flowNonce` 由服务端为每张 join ticket 生成 16 个密码学随机字节，在 HTTP/control DTO 中编码为 32 位 lowercase hex，并分别交给 host 与 joiner；不得转成 JavaScript number。每个 `sender -> recipient -> flowNonce` 方向维护独立的 `messageSequence`，从 1 开始，为该方向每条携带 LAN container 的原版消息严格递增；不得回绕，耗尽时关闭 session。Ritsu sidecar sender ID、recipient/transport peer、flow nonce、message kind 和 sequence 必须共同匹配，重复、回退、跨 flow 或越权 frame 都拒绝。

sidecar frame 与对应原版消息形成一对：发送端必须先向目标 peer 提交 frame，再发送原版消息。sidecar stream 和原版消息 stream 各自必须保持发送顺序；只允许两条 stream 之间发生有界交错。接收端按该方向的下一个 `messageSequence` 给原版消息占位，并核对 frame 的 `messageKind`，不得用“同 kind 的任意待处理消息”猜测配对。每个 peer 最多缓存 16 对、每对 sidecar frame/container 最多 256 KiB、等待 5 秒；该限制不包含也不得触发复制完整原版 `SerializableRun` payload，原版对象仍由既有消息路径持有。只有 frame 和原版消息都到达且 selection/container/authority/roster 全部验证通过，才允许进入原版 handler；跨 stream 乱序可以在边界内配对，任一 stream 内乱序、跳号、超时、重复或冲突时断开对应 peer。协议拒绝同样以 sidecar `lan.rejection` frame 配对原版 `ClientConnectionFailedMessage`，不得在 Ritsu-present 消息后追加 standalone Tail。

对 `PlayerJoinedMessage`、`LobbyBeginRunMessage` 等 host 广播，host 必须先按每个接收 peer 各自的 `flowNonce` 和发送序号提交一份 sidecar frame；所有 frame 的 inner container bytes 必须相同。只有全部目标 peer 的 frame 提交成功后才发送一次原版广播。任一 peer 缺少有效 flow binding、sidecar 不可达或 frame 提交失败时，不得向该 peer 放行原版 handler；实现可以在广播前关闭失败 peer，或取消整次广播并关闭房间，但不得发送一个共享 nonce 的 sidecar frame，也不得让部分 peer把未配对广播交给原版逻辑。

LAN Connect 必须通过公开、直接接受 `INetGameService` 的 sidecar overload 驱动握手与发送；不得 transpile RitsuLib 的 `RunManager` resolver，不得读取私有 registration/patch owner，也不得调用 `RitsuNetMessageTailExtensions.Write/Read`。Ritsu 的 native-trailer discovery 需要先有一条原版 packet，不能作为首个 LAN flow 的唯一 bootstrap。大厅流必须在 host/joiner 都验证 ticket、`protocolFlowNonce`、transport peer binding 和冻结 selection 后，才调用公开 `RitsuLibSidecarSessionManager.SetPeerReachabilityHint(peerId, Supported)`；随后 `ObserveNetService` 并确认 `CanSendToPeer`，再发送首个 frame。hint 只代表该张受信 ticket 的 sidecar bootstrap，不替代 LAN container、sender、nonce 或后续 Ritsu handshake 验证；ticket 取消、peer 断开、session/epoch 切换时必须设置回 `Unknown` 并清除本地 flow state，禁止跨 session 复用。纯 direct-IP 没有受信 ticket binding，因此不得设置该 hint。Task 0 必须在真实双进程中证明这条完全公开的 bootstrap 能在首个 LAN flow 消息前建立 reachability，并证明 handler barrier 不会泄漏未验证的原版消息。

### 4.3 房间协议与 RitsuLib presence 选择

版本选择在创建房间时完成，之后冻结：

1. 房主提交 peer offer，包括 LAN protocol range 和本地 RitsuLib presence。
2. 服务端将房主 LAN range 与服务端允许的 `tail_v1` range求交，选择最高共同版本；无交集则拒绝创建。
3. `alpha.1` 的服务端允许范围固定为 `1..1`，因此选择结果只能是 1。
4. 服务端将房主 RitsuLib presence 原样冻结进 selection；房主不能要求服务端改写它。
5. presence=false 时 carrier 固定为 `standalone_tail_v1`；presence=true 且公开 typed-sidecar 可用时 carrier 固定为 `ritsulib_sidecar_v1`。RitsuLib 存在但 sidecar 不可用时拒绝建房。
6. 加入者必须声明与 selection 完全相同的 RitsuLib presence 且支持冻结 carrier；不一致时在分配 slot、签发 ticket 或建立 transport 前拒绝。
7. LAN Connect 不读取 RitsuLib 版本、注册表或扩展 metadata，也不据此放宽或收紧加入门禁。

房主不能直接指定 selection，只能提交 offer；服务端按上述确定性算法生成 selection。创建响应返回 selection，房主必须在建立游戏 host 前再次验证本地仍支持该 selection。

房间生命周期内 selection 不变：

- 房主 capability 改变时关闭房间，不重新协商。
- 房主断线时沿用当前项目的房间关闭语义；`alpha.1` 不新增 host migration。
- 房主重连或续局只能使用原 selection，不能重新选择版本。
- 服务端、房主和客机在 capability digest 中使用 canonical selection bytes，避免顺序差异。
- 每次 join 使用独立 `protocolFlowNonce`；它绑定 ticket/control/sidecar 配对但不属于房间 selection，也不进入 capability digest。

Capability digest v1 的 canonical bytes 固定为：

```text
magic[8] = ASCII "LANSEL01"
schemaVersion: uint8 = 1
profile: uint8 (1 = compat_4_5_v1, 2 = tail_v1)
selectedLanProtocolVersion: uint16
carrier: uint8 (0 = compat/no LAN carrier, 1 = standalone Tail, 2 = RitsuLib typed sidecar)
maxPlayers: uint8
minimumClientVersionByteLength: uint8
minimumClientVersion: UTF-8 bytes
gameVersionByteLength: uint8
gameVersion: UTF-8 bytes
wireCacheSignatureByteLength: uint8
wireCacheSignature: lowercase ASCII bytes (0 length means unavailable)
flags: uint8 (bit 0 = RitsuLib present; other bits must be zero)
```

数字使用网络字节序；版本字符串必须是规范化非空 UTF-8，最多 32 bytes；wire-cache signature 最多 64 ASCII bytes。Digest 是上述完整 bytes 的 SHA-256 lowercase hex。RitsuLib presence 和 carrier 都是强制 selection 字段并进入 digest；客户端与服务端必须消费 compat、standalone 和 Ritsu-sidecar golden vectors。

### 4.4 消息 entry 矩阵

| 消息 | `lan.capabilities` | `lan.roster` | 规则 |
|---|---|---|---|
| `InitialGameInfoMessage` | 必需：session selection | 禁止 | 房主向候选客机声明已冻结的房间协议 |
| `ClientLobbyJoinRequestMessage` | 必需：peer offer | 禁止 | 客机只声明自身能力，不得发送 roster |
| `ClientLobbyJoinResponseMessage` | 必需：session selection | 必需：full snapshot | 原版类型只表示成功；禁止 `lan.rejection` |
| `ClientLoadJoinRequestMessage` | 必需：peer offer | 禁止 | `InLoadedLobby` 客机重新声明自身能力，不得发送 roster/rejection |
| `ClientLoadJoinResponseMessage` | 必需：session selection | 必需：full snapshot | 原版类型只表示成功；host 发送当前完整 roster；禁止 `lan.rejection` |
| `ClientRejoinRequestMessage` | 必需：peer offer | 禁止 | `Running` 客机重新声明自身能力，不得发送 roster/rejection |
| `ClientRejoinResponseMessage` | 必需：session selection | 必需：full snapshot | 原版类型只表示成功；host 发送当前完整 roster；禁止 `lan.rejection` |
| `ClientConnectionFailedMessage` | 必需：session selection | 禁止 | 必需且仅允许 `lan.rejection`；host 对三种 request 的协议拒绝使用该原版稳定类型 |
| `PlayerJoinedMessage` | 必需：session selection | 必需：full snapshot | `alpha.1` 不定义 delta；每次发送递增 revision 的完整 roster |
| `LobbyBeginRunMessage` | 必需：session selection | 必需：full snapshot | 提供开局前最终 authoritative roster |

矩阵中标记为必需的 entry 缺失、标记为禁止的 entry 出现或 record kind 不匹配时，整条消息无效。协议拒绝先构造完整 LAN container（必须包含 session selection + rejection，禁止 roster 和其他 LAN entry），再按冻结 carrier 发送：standalone 模式追加到原版 `ClientConnectionFailedMessage`；Ritsu-sidecar 模式先发送配对 frame，再发送原版 failure message。原版 body 固定为 `disconnectionReason=ModMismatch`、`versionInfo` 使用当前 host 值。该类型 ID 是 STS2 内建 `INetMessageSubtypes` ID 34。它只允许当前 transport host 发给仍处于三种 request 等待状态的单个 peer。客户端只有在 container、host、selection、carrier 和请求阶段全部验证后，才完成对应等待任务为 `LanConnectProtocolException`，随后断开并禁止成功 response 路径。container 缺失时保留原版 failure 行为，供 compat/非 LAN peer 使用。

目标 STS2 程序集已确认 `ClientLobbyJoinResponseMessage`、`ClientLoadJoinResponseMessage` 和 `ClientRejoinResponseMessage` 都没有稳定的 success/failure discriminator；后两者还要求在 LAN container 前反序列化完整 `SerializableRun`。因此禁止伪造 rejected 原版 response，也禁止按 roster/rejection entry 是否存在反推原版 response 成败。普通、载入和运行中 join 的协议拒绝统一使用上述原版 failure message + 对应 carrier 的 LAN container；服务端 ticket 拒绝继续使用 HTTP structured error。

三种成功 response 的 full snapshot 使用当前 host authority 和 host session membership/slot binding table；该表包含当前连接状态，不等同于只含在线 peer 的 transport connection list。它们不表示新的 roster mutation：发送时必须使用 host 当前已提交的 `rosterRevision`；只有实际加入、离开或 slot 变化才能递增 revision。

客户端首次收到任一成功 response 且尚无已接受 snapshot 时，可执行一次 bootstrap：revision 必须大于 0，sender 必须是 transport host，selection/digest 必须匹配 ticket，当前加入者 ID/slot 必须匹配 ticket binding，vanilla projection 必须匹配 LAN container，且 roster 必须与该 session state 的原版 authoritative carrier 一致（InLobby 使用 `playersInLobby`；InLoadedLobby 使用 `playersAlreadyConnected` 加当前获准加入者；Running 使用 `serializableRun` 的 multiplayer roster 加当前 rejoin binding）。满足后将该 revision 和 canonical roster 设为 baseline。已有 baseline 时只接受相同 revision + 完全相同 snapshot，或由已验证 membership mutation 产生的更大 revision；较小 revision、相同 revision 不同 roster、无 mutation 的更大 revision均拒绝。保存文件不持久化 roster revision；进程内 reconnect 可带旧 baseline，但仍按相同规则验证。

### 4.5 原版 roster 投影

发送原版 body 前：

1. 将 authoritative roster 按真实 slot 升序、再按 player ID 升序形成 canonical order，并取前四名玩家。
2. 按 canonical index 将四名玩家确定性映射到原版 slot `0..3`。
3. 不保留真实 slot；真实 slot 只存在于 LAN container authoritative roster。
4. 投影只用于原版消息主体，不修改 authoritative roster。
5. 对携带 `lan.roster` 的消息，接收端逐项验证投影 player ID、临时 slot 和 LAN container roster 的确定性投影完全一致；任一差异均拒绝整条消息。

接收端先按原版格式读取投影，再从冻结 carrier 取得配对 LAN container，并在进入原版 lobby/begin-run handler 前恢复完整 roster。

必须覆盖的首版消息面：

- `InitialGameInfoMessage`：房主 capability。
- `ClientLobbyJoinRequestMessage`：加入者 capability。
- `ClientLobbyJoinResponseMessage`：完整 lobby snapshot 或结构化拒绝。
- `ClientLoadJoinRequestMessage` / `ClientLoadJoinResponseMessage`：载入大厅 capability 重验、完整 roster 或结构化拒绝。
- `ClientRejoinRequestMessage` / `ClientRejoinResponseMessage`：运行中重连 capability 重验、完整 roster 或结构化拒绝。
- `PlayerJoinedMessage`：高 slot 玩家完整数据。
- `LobbyBeginRunMessage`：最终 authoritative roster。

原版 roster 投影和 body/container 一致性验证适用于所有携带 `lan.roster` 的成功 response、`PlayerJoinedMessage` 和 `LobbyBeginRunMessage`。`InitialGameInfoMessage` 和三种 request 不携带 roster，也不修改其原版 body；它们只按现有 STS2 消息语义验证 sender/receiver identity，并使用 `lan.capabilities` 完成协议协商。LAN rejection message 没有原版 roster/body，且不得让任何原版 response payload 进入成功 handler。

### 4.6 Authority 与一致性

- 只有当前 transport host 可以发送三种 response、带 LAN rejection container 的 `ClientConnectionFailedMessage`、player-joined 和 begin-run authoritative full roster snapshot。
- 三种 request 只能携带发送者自身 capability，不能携带 authoritative roster。
- sender ID 必须与当前连接 peer、消息内 player ID 和服务端签发的 slot 绑定一致。
- `PlayerJoinedMessage` 的新增 player ID 必须对应刚建立的连接，slot 必须是当前未占用 slot。
- vanilla 投影必须是 LAN container authoritative roster 的确定性投影；container 与 body 冲突时不得选择其中一方继续运行。
- roster ID、slot、数量、当前连接表或房主身份任一不一致时，消息不得交给原版 handler，并断开产生无效消息的 peer。

### 4.7 结构化协议失败传播

客户端内部所有建房/加入入口使用同一个不可丢失的 `LanConnectProtocolFailure` 值，字段至少包含规范 reason code、required client version、required RitsuLib presence 和仅供诊断的 detail。HTTP `LobbyErrorResponse`、任一 carrier 中的 `lan.rejection` 和本地验证必须转换为该值；未知 code 仍保留原始 code 并按不可重试协议失败处理。

- `LanConnectProtocolException` 必须携带非空 `LanConnectProtocolFailure`，且永远不可作为 transport 候选重试条件。
- host create/publish 返回结构化 host attempt result；不得先转换为 `bool` 或普通字符串再交给 UI。
- lobby join 和 compat-only direct-IP join 返回结构化 join attempt result，其中协议失败字段与普通显示文本分离。
- mod preflight/ticket 结果必须保留服务端协议错误；不得在 `LobbyServiceException` 到 UI 之间丢失 details。
- managed join 的 InLobby、InLoadedLobby、Running 三条分支都将 carrier 拒绝或 codec/authority 失败转换为同一异常和值。
- direct/relay candidate 循环遇到协议失败立即停止，断开当前 transport 并释放 tentative lease；只有明确 transport timeout/unknown-network failure可以换候选。
- restart auto-rejoin 遇到协议失败必须清除 pending reconnect、显示一次结构化提示并停止轮询，直到用户重新发起加入或房间 selection/capability 发生变化。普通暂时性 room-not-found/transport failure 保留现有轮询。
- cancellation 保持 cancellation；无关异常保持 internal error，不能伪装成协议失败。

HTTP protocol error details 的 JSON/C# 镜像固定为以下可选字段：`requiredClientVersion: string?`、`requiredRitsuLibPresent: boolean?`、`detail: string?`。规范 reason code 使用顶层 `LobbyErrorResponse.code`；未知 code 原样保留。服务端和客户端均执行与 LAN container 相同的 32/512-byte 字段上限。

### 4.8 Profile 不允许自动切换

游戏连接建立前必须确定 profile 和 carrier。原因是第一条游戏消息已经依赖正确 wire 格式，同一广播也不能同时让旧客户端按 `4/5-bit`、standalone 客户端按 `2/3-bit + Tail`、Ritsu 客户端按 vanilla + sidecar barrier 解码。

因此：

- profile 在建房时显式选择。
- profile 存入房间状态并随列表和 join response 返回。
- profile/carrier 在房间生命周期内不可变。
- 新客户端不能因旧客户端尝试加入而自动把 `tail_v1` 降级为兼容模式。

## 5. RitsuLib 共存

### 5.1 基本规则

- LAN protocol container 永远由 LAN Connect 编码和解码；Ritsu-present 房间只把它作为公开 typed-sidecar payload。
- 房间冻结一个 RitsuLib presence；所有参与者必须与该值一致。
- LAN Connect 只通过 RitsuLib 公开 typed-sidecar API发送/接收自己的 descriptor，不发现或读取其他扩展能力。
- 禁止反射 RitsuLib 私有 `SerializePatch<T>`、私有 postfix 或内部 Harmony owner。
- 禁止卸载、恢复或手工调用其他 MOD 的 Harmony patch；禁止在 Ritsu-present selection 中追加 standalone LAN Tail。

### 5.2 Presence 同质化边界

`tail_v1` 房间只允许以下组合：

- 所有人都未安装 RitsuLib。
- 所有人都安装 RitsuLib。

房主和加入者 presence 不一致时，服务端和本地 preflight 都必须拒绝；大厅流必须在 ticket 前拒绝。presence 只表示 RitsuLib 框架是否被检测到，不表示 RitsuLib 版本或扩展集合兼容。每个 Ritsu-present participant 还必须在本地确认公开 typed-sidecar descriptor、直接 `INetGameService` send overload 和 session reachability 可用，否则以 `ritsulib_sidecar_unavailable` fail closed。LAN Connect 不维护 RitsuLib 分支、不建立扩展白名单，也不访问其私有注册表；同为 RitsuLib 的客户端仍可能被原版 MOD mismatch、RitsuLib 自身门禁或后续游戏握手拒绝。纯 direct-IP Tail 不在 `alpha.1` 范围内。

### 5.3 兼容模式

`compat_4_5_v1` 明确禁止 RitsuLib。这样可删除 RC4 的私有 postfix 桥，同时保留 `0.3.x-0.5.x` 的历史 wire 互通。

## 6. 服务端模型与门禁

### 6.1 Profile 模型

规范化后的服务端模型：

```ts
type ProtocolProfile = "compat_4_5_v1" | "tail_v1";
```

旧值迁移：

| 旧值 | 0.6 服务端行为 |
|---|---|
| `legacy_4p` | 仅作为离线 fixture 识别；部署时终止/清除现存房间，不再进入运行时房间状态 |
| `extended_8p` | 映射为 `compat_4_5_v1` |
| 缺失 profile | 仅对已知 `0.3.x-0.5.x` 请求映射为兼容模式；其他请求拒绝 |
| 未知 profile | 明确拒绝，不得自动回退 |

0.6 服务部署时终止或清除现存 `legacy_4p` 房间。所有 `<0.3.0` create/join/control 请求返回 `client_update_required`；房间列表可继续公开，但不得向 0.2.x 签发控制会话或 join ticket。

旧客户端 DTO 投影按 API入口显式处理：

| 调用方 | 版本信号 | 服务端内部 profile | 返回给调用方的 profile |
|---|---|---|---|
| 0.3-0.5 create/join/control | 请求中的客户端/mod 版本 | `compat_4_5_v1` | `extended_8p` |
| 0.6+ create/join/control | 请求中的 capability + 客户端版本 | 规范 profile | `compat_4_5_v1` 或 `tail_v1` |
| 无可靠版本信号 | 无 | 不创建、不签票 | `client_update_required` |

公开 room-list 保留旧客户端可解析的 `protocolProfile` 字段，并新增 0.6 可选的规范 profile/capability 字段；0.6 客户端优先读取新字段。旧字段投影由当前 DTO 契约单元测试覆盖，不依赖历史客户端抓包。

### 6.2 房间 capability

房间至少发布：

- protocol profile。
- 按 §4.3 算法冻结的 LAN protocol version。
- 按 §4.3 算法冻结的 carrier。
- 最低客户端版本。
- 最大人数。
- RitsuLib framework presence（冻结的强制兼容字段）。
- 游戏版本。
- wire-cache signature。
- 每张 join ticket/control binding 的 32-hex `protocolFlowNonce`（不进入 room list）。

### 6.3 加入门禁顺序

服务端签发 join ticket 前按顺序检查：

1. profile 是否受加入客户端支持。
2. 客户端最低版本是否满足。
3. `compat_4_5_v1` 中双方是否均未启用 RitsuLib；`tail_v1` 中加入者 presence 是否与房间冻结值完全相同。
4. 加入者是否支持房间冻结的 LAN protocol version 和 carrier；Ritsu-present 时本地 sidecar readiness 必须为真。
5. 游戏版本是否一致。
6. wire-cache signature 是否一致或满足既有明确的缺失策略。
7. 房间状态和人数是否允许加入。

`tail_v1` 客户端游戏握手必须重复验证 profile、选定 protocol、carrier 和 RitsuLib presence，避免旧服务端、缓存状态或加入后本地状态变化绕过门禁。Ritsu-sidecar carrier 还必须在首个协议 frame 前验证 required descriptor reachability。`compat_4_5_v1` 中的 0.3-0.5 客户端不理解新 capability，使用服务端冻结 profile 加现有游戏版本/MOD inventory 握手。

### 6.4 错误码

至少提供：

- `client_update_required`
- `protocol_profile_unsupported`
- `ritsulib_not_allowed_in_compat_mode`
- `ritsulib_presence_mismatch`
- `ritsulib_sidecar_unavailable`
- `game_version_mismatch`
- `wire_cache_mismatch`
- `lan_tail_required`
- `lan_tail_malformed`
- `lan_protocol_version_mismatch`

UI 必须显示面向用户的具体说明，不能全部退化为 `ModMismatch`、`InternalError` 或加入失败。握手期间返回结构化拒绝；游戏中收到 malformed、unauthorized 或不一致 container/carrier 时记录具体原因、断开对应 peer，并禁止把半恢复消息交给原版 handler。

## 7. 兼容性矩阵

| 房主 | 客机 | 模式 | 结果 |
|---|---|---|---|
| >=0.6 且支持选定协议 | >=0.6 且支持选定协议 | `tail_v1` | 允许 |
| 0.6 + RitsuLib + sidecar ready | 0.6 + RitsuLib + sidecar ready | `tail_v1` / `ritsulib_sidecar_v1` | 允许进入后续 MOD/RitsuLib 检查 |
| 0.6 无 RitsuLib | 0.6 无 RitsuLib | `tail_v1` / `standalone_tail_v1` | 允许 |
| 0.6 + RitsuLib 但 sidecar 不可用 | 任意 | `tail_v1` | 本地/建房前拒绝 `ritsulib_sidecar_unavailable` |
| 0.6 + RitsuLib | 0.6 无 RitsuLib | `tail_v1` | ticket 前拒绝 presence mismatch |
| 0.6 无 RitsuLib | 0.6 + RitsuLib | `tail_v1` | ticket 前拒绝 presence mismatch |
| 0.6 | 0.3-0.5 | `tail_v1` | 拒绝并提示更新 |
| 0.6 | 0.3-0.5 | `compat_4_5_v1` | 允许 |
| 0.6 + RitsuLib | 0.3-0.5 | `compat_4_5_v1` | 拒绝 |
| 0.3-0.5 | 0.6 | 旧客户端创建的兼容房 | 允许 |
| 0.2.x | 任意 | 任意 | 拒绝并提示版本过旧 |

纯 direct-IP 在 `alpha.1` 只允许 `compat_4_5_v1` 且双方无 RitsuLib；`tail_v1` 选项不展示、不自动尝试，也不从失败中切换 profile。

## 8. 客户端变更边界

### 8.1 删除或废弃

- `legacy_4p` 运行时 profile。
- `Legacy4pSlotIdBits = 8` 的运行路径。
- `0.2.2` 版本推断与重试逻辑。
- RC4 的 RitsuLib begin-run 私有 postfix detach/bridge/restore。
- 未知 profile 回退到扩展 profile 的行为。

历史常量可短期保留在 fixture 或迁移测试中，但不得进入新运行时选择。

### 8.2 保留

- 当前 `4/5-bit` transpiler，限定在 `compat_4_5_v1`。
- 现有房间作用域 profile 设置机制。
- 现有容量、玩法布局和难度补丁，按 2-8 人目标继续使用。
- RitsuLib sidecar 的公开 typed-message、direct `INetGameService` send 和 session reachability API；旧私有 resolver transpiler 不保留。

### 8.3 新增

- carrier-neutral LAN protocol container v1 codec 与 standalone Tail carrier。
- RitsuLib typed-sidecar carrier、配对缓存和 handler barrier。
- 原版 roster 投影与恢复。
- RitsuLib presence detector 与冻结 selection 校验。
- 新 profile 的建房 UI和提示。
- 服务端错误码对应的客户端提示。
- 字节级 golden vector 测试工具。

## 9. 测试与发布门禁

### 9.1 字节级契约测试

必须有独立 known-good golden bytes，而不是用实现算法生成 expected value：

- 2、3、4、5、6、7、8 人 roster。
- slot `0..7`。
- 高 slot 玩家位于原版投影前四项。
- LAN container encode/decode round-trip，以及 standalone/sidecar 两种 carrier 的 identical-container 断言。
- 无 tail、截断、错误 magic、未知 container version。
- 重复 player ID、重复 slot、越界 slot、数量超限。
- 未知非关键 entry 跳过。
- 未知关键 entry 拒绝。
- RitsuLib 有/无时 LAN roster payload 完全相同。
- 原版 body 在 2-8 人情况下始终保持原版 `2/3-bit`。
- 十种 STS2 消息类型各自的 known-good bytes（Initial、三种 request、三种成功 response、ClientConnectionFailed、PlayerJoined、BeginRun）；三种 request-stage LAN rejection 分支分别覆盖。
- 全员 RitsuLib 时 LAN container 只出现在 typed-sidecar frame，原版消息字节与 vanilla fixture 完全一致，且 handler barrier 在配对验证前不放行。
- 只有有效 ticket/peer/nonce binding 可以设置 public reachability hint；ticket 取消、disconnect 和 epoch 切换都清除 hint/cache/counter，复用 peer ID 从 `Unknown` 开始。
- 两个 RitsuLib presence mismatch 方向都在 ticket 前结构化拒绝，且不建立游戏 transport。
- direct-IP `tail_v1` 在 UI 和本地 flow 创建 transport 前被拒绝；compat+本地 Ritsu 同样本地拒绝。
- 最大 container/entry/payload 边界、整数溢出、截断和累计长度超限。
- 重复 entry ID、非法 capability flags、合法 Tail 后的非法 trailing garbage。
- 非 host authoritative roster、body/container roster 不一致和 ticket 后 capability/carrier 改变。
- LoadJoin/Rejoin request offer 不匹配、LAN rejection、response authority/session membership/revision 不一致、首次 bootstrap 和重复当前 snapshot 的幂等接受。
- HTTP、本地、普通 join、load join、rejoin、compat direct join 和 restart auto-rejoin 的结构化失败保持及不可重试行为。

### 9.2 兼容 profile 运行时测试

- 不运行真实 `0.3.x-0.5.x` 客户端，也不捕获或维护其 fixture。
- 以 v0.6 当前 DTO 和 codec 测试固定 `compat_4_5_v1` 的 `4/5-bit` 行为与 legacy API 投影。
- `0.2.x` 请求明确被服务端和客户端拒绝。
- 生产运行时不再选择 `8/3-bit`。
- `extended_8p` 房间状态迁移为兼容模式。

### 9.3 真实平台矩阵

至少覆盖：

- Windows <-> Windows。
- Windows <-> Android。
- Windows <-> macOS。
- Android <-> macOS。
- `tail_v1` 无 RitsuLib。
- `tail_v1` 全员 RitsuLib。
- `tail_v1` 两个方向的 RitsuLib presence mismatch ticket 拒绝。
- Ritsu-present host/joiner 任一 sidecar readiness 失败时的 pre-transport 拒绝。
- `compat_4_5_v1` 的 0.6 <-> 0.6。
- 兼容模式中 RitsuLib 被拒绝。
- 2、4、5、8 人作为所有平台 smoke 的主边界；3、6、7 人至少分别在一个跨平台组合中完成建房、加入、准备和开局。

成功标准是所有参与端进入游戏状态同步，不是只完成建房或加入。

### 9.4 原子初始化

客户端启动时安装一套原子 dispatcher patch，不在建房/加入过程中动态 unpatch/repatch。dispatcher 在游戏连接建立前读取并冻结 session profile/carrier；连接存在期间不可修改，session 完全关闭后才可重置。

原子要求：

- 兼容 profile 的 `4/5-bit` patch 不是完整集合时 fail-closed。
- Tail profile 的投影、container codec、standalone carrier、sidecar barrier 和 handler restore 不是完整集合时 fail-closed。
- dispatcher 任一必需入口失败时整体回滚；不得留下任何 profile 的部分 patch。
- 日志必须输出 active profile、patch owner、protocol version 和 capability digest。

## 10. 发布策略

- 客户端：`v0.6.0-alpha.1`。
- lobby-service：`v0.6.0-alpha.1`。
- 两者同步发布和部署。
- 历史版本下载不受本次实现影响，但不属于 `alpha.1` 验收矩阵。
- `v0.5.6-rc2` 至 `rc4` 标记为已知 RitsuLib 兼容实验版本，不再推荐。
- `alpha.1` 只发布为 GitHub Pre-release。
- `alpha.1` 的目标是协议与跨平台验证，不承诺直接晋升正式版。
- 所有同房玩家仍必须使用相同 STS2 游戏版本。

## 11. 实施切片

实施计划应按端到端垂直切片拆分，而不是先写完所有 codec 再补 UI和测试：

1. **Profile/门禁切片**：服务端和客户端识别新 profile，0.2.x 明确拒绝；此阶段隐藏 `tail_v1` 建房选项并限制内部 prototype 为最多 4 人。
2. **普通 Join response 切片**：原版四人投影 + LAN container 完整 snapshot，可通过 golden bytes 验证。
3. **载入/重连 response 切片**：LoadJoin/Rejoin capability 重验、当前完整 snapshot、幂等 revision 和结构化拒绝。
4. **Player joined 切片**：高 slot 玩家占位、恢复和 fail-closed。
5. **Begin-run 切片**：最终 authoritative roster 恢复并完成真实双端开局。
6. **RitsuLib 切片**：通过 §4.0 可行性门禁后，接入 presence/carrier 冻结、同质门禁和全员 RitsuLib 的公开 typed-sidecar barrier。
7. **兼容房切片**：v0.6 固定 `4/5-bit` 与 legacy DTO 投影，RitsuLib 明确拒绝。
8. **UI/发布切片**：建房选项、错误提示、文档、跨平台矩阵和 alpha 包。

每个切片必须保持测试绿色，并能够在 fresh context 中独立验证。

## 12. 风险与缓解

| 风险 | 缓解 |
|---|---|
| STS2 不保证 vanilla reader 忽略尾部字节 | 在目标游戏版本上建立原版 reader fixture 和真实双端测试 |
| Android 对动态闭合泛型 Harmony patch 仍有限制 | 尽量 patch 稳定非泛型 handler；必须动态闭合的入口进行 Android 真机启动门禁 |
| RitsuLib 公开 sidecar API无法在 handler 前稳定配对 LAN container | 不发布 RitsuLib 房支持，回到设计评审；禁止私有 postfix 桥或独立双 Tail |
| 同为 RitsuLib 但版本或第三方扩展不兼容 | LAN Connect 仅保证 presence 同质；保留原版 MOD inventory 和 RitsuLib 自身门禁，并在文档/UI 明示此边界 |
| 旧客户端无法识别新服务端 profile 字符串 | 服务端继续投影旧 DTO；该行为仅由当前版本契约测试覆盖，不作为历史客户端互通保证 |
| 两套 profile 增加维护成本 | 兼容 profile 只保留 0.3-0.5 所需的完整旧路径，不再增加新能力或 Ritsu 兼容 |
| 房间误发布 profile/carrier 导致首包错位 | profile/carrier 建房后不可变，服务端和客户端握手双重验证 |
| 0.5.6 RC 用户继续使用已知失败版本 | Release 和文档明确标记不推荐，稳定入口仍指向 0.5.5 |

## 13. 验收标准

设计实施完成必须同时满足：

1. 生产代码不存在 `legacy_4p=8/3-bit` 的可选运行路径。
2. `tail_v1` 不修改 STS2 原版玩家字段位宽；无 RitsuLib 使用 standalone carrier，全员 RitsuLib 使用 typed-sidecar carrier；两个允许组合通过真实双进程可行性门禁，两个 mismatch 方向在大厅流 transport 前拒绝。
3. `compat_4_5_v1` 在 v0.6 双端固定使用 `4/5-bit`，正确投影 legacy DTO，并在任一端检测到 RitsuLib 时于 transport 前拒绝。
4. `tail_v1` 在 Windows、Android、macOS 组合中完成 2-8 人开局和同步。
5. `tail_v1` 房间的 RitsuLib presence/carrier 全员一致，内层 LAN container bytes 不因 carrier 改变；Ritsu-present 原版消息后没有 standalone LAN Tail。
6. 任一 RitsuLib presence mismatch 在大厅 join ticket 前得到结构化拒绝；纯 direct-IP `tail_v1` 在本地 transport 创建前被拒绝。
7. 0.2.x 客户端得到明确更新提示，不能进入游戏握手。
8. 所有 golden vectors、服务测试、客户端单测和 Godot 测试通过。
9. 客户端与 lobby-service 均以 `0.6.0-alpha.1` Pre-release 发布。

## 14. 实施前阻塞研究

以下研究必须在对应切片开始前完成，不能在实现中临时猜测：

- 使用真实程序集和真实双进程证明 §4.0 的 standalone/typed-sidecar 两个允许组合、sidecar frame/vanilla handler barrier，以及两个 mismatch 方向在大厅流的 transport 前拒绝；这是 RitsuLib 切片的阻塞门禁。
- 确认 RitsuLib 公开 typed-sidecar/direct `INetGameService` API能否在首个 LAN flow 前建立 reachability 并稳定配对；不再要求扩展 inventory API或 Tail owner 协作。
- 公开 sidecar carrier 是否能完全替代旧 resolver transpiler，并覆盖普通 join、load join、rejoin、player joined 与 begin-run。
- `alpha.1` 验证后，兼容模式的长期保留期限。

`tail_v1` schema 本身已在 §4.2 固定；实施计划只允许补充 golden bytes 和 capability digest 的算法，不得改变字段顺序、长度类型、对齐或边界限制。任何 wire schema 变更必须回到设计评审并提升 protocol version。
