# STS2 LAN Connect v0.6 双协议迁移设计

- **创建日期**：2026-08-13
- **状态**：Approved - 已完成维护者确认与独立终审
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

`STS2-MultiplayerLimitBreak` 0.2.x 已采用更稳定的方向：原版消息主体保持原版位宽，完整扩展 roster 放入版本化消息尾。本设计采用这一协议思想，但由 LAN Connect 自己拥有最小 Tail 协议，不直接复制外部 MOD 的完整实现，也不依赖 RitsuLib 才能传输 LAN roster。

项目维护者已决定：

- 放弃 `0.2.x` 客户端兼容。
- 保留 `0.3.x-0.5.x` 客户端混房能力。
- 新协议首版正式支持 2-8 人。
- 新协议使用 LAN Connect 内置 Tail，并通过公开 API 与 RitsuLib 共存。
- 客户端与 lobby-service 同步升级为 `0.6.0-alpha.1`。

## 2. 目标与非目标

### 2.1 目标

1. 新协议路径不修改 STS2 原版 `slotId=2-bit` 和玩家列表长度 `3-bit`。
2. 新协议支持 2-8 人，并保留将来扩展到 16 人的编码空间，但首版不承诺 9-16 人。
3. 保留 `0.3.x-0.5.x` 客户端通过显式兼容房与 `0.6.x` 客户端混房。
4. `0.2.x` 客户端在签发 join ticket 前被明确拒绝，不再进入游戏握手。
5. RitsuLib 框架可在新协议房自由混装；联机关键扩展必须满足房间级 capability 一致性。
6. 不再依赖 RitsuLib 私有 Harmony postfix、patch owner、priority 或内部泛型类型。
7. 将不兼容从开局后的黑屏提前为建房或加入阶段的结构化错误。
8. 为新旧两条协议建立字节级契约测试和真实跨平台联机门禁。

### 2.2 非目标

1. 不兼容 `0.2.x` 客户端及其 `8/3-bit` wire profile。
2. 不在 `alpha.1` 正式支持 9-16 人。
3. 不重写 STS2 的完整网络协议、传输层、动作同步或断线重连机制。
4. 不承诺任意 RitsuLib 内容扩展都能缺失后安全运行。
5. 不允许兼容模式与 RitsuLib 共存。
6. 不允许房间创建后在两种 wire profile 之间自动切换。
7. 不把 Harmony patch 枚举作为长期扩展服务发现机制。

## 3. 产品模式

建房时用户必须在两种模式中选择。默认选中兼容模式，房间创建后模式不可变。

| 模式 | 默认 | 支持客户端 | Wire 格式 | RitsuLib | 人数 |
|---|---|---|---|---|---:|
| 兼容模式 | 是 | `0.3.x-0.6.x` | 历史 `4/5-bit` | 禁止 | 2-8 |
| 0.6 新协议 | 否 | 仅 `0.6.x` | 原版 `2/3-bit` + LAN Tail v1 | 支持 | 2-8 |

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
- 完整 roster 由 LAN Tail v1 携带。
- LAN Tail 不依赖 RitsuLib 是否安装。
- `<0.6` 客户端在服务端签发 join ticket 前被拒绝。

## 4. 协议架构

### 4.0 实施前可行性门禁

在修改生产协议前，必须先用真实 STS2、Harmony 和 RitsuLib 程序集完成一次独立原型。原型必须证明以下四种组合都能按同一个 `tail_v1` LAN roster 格式完成读写：

1. 发送端无 RitsuLib，接收端无 RitsuLib。
2. 发送端有 RitsuLib，接收端无 RitsuLib。
3. 发送端无 RitsuLib，接收端有 RitsuLib。
4. 发送端有 RitsuLib，接收端有 RitsuLib。

目标组合顺序固定为：

```text
vanilla body -> LAN Tail v1 -> optional RitsuLib tail
```

读路径必须先从 vanilla body 后读取并完整消费 LAN Tail，再允许 RitsuLib 从新的 reader cursor 读取其可选 tail。写路径必须先完成 LAN Tail，再允许 RitsuLib 追加。这个顺序必须通过 RitsuLib 公开、版本化的协作契约或公开的稳定 patch-order contract 保证，不能依赖私有方法形状、运行时枚举后猜测 priority，或卸载其他 owner 的 patch。

如果真实 RitsuLib 的公开契约不能保证该顺序、cursor 传递和单边安装时的安全退化，则暂停 RitsuLib 自由混装切片并回到设计评审。不得通过恢复 RC4 私有 postfix 桥绕过门禁，也不得将不安全组合发布为 `alpha.1`。

### 4.1 单一所有权

在 `tail_v1` 中，LAN Connect 是以下行为的唯一所有者：

- 原版 roster 投影。
- LAN Tail envelope 编码与解码。
- authoritative roster 验证与恢复。
- LAN protocol capability 声明。

LAN Connect 不拥有 RitsuLib 的扩展容器，也不修改或卸载 RitsuLib 的 Harmony patch。RitsuLib 不拥有 LAN roster。

### 4.2 消息布局

```text
STS2 original message
|- vanilla body
|  |- at most four projected players
|  |- slotId: original 2 bits
|  `- player list count: original 3 bits
|- LAN Tail v1
|  |- magic and bounded container header
|  |- selected wire protocol version
|  |- authoritative 2-8 player roster
|  |- critical extension capabilities
|  `- bounded payload entries
`- optional RitsuLib tail
```

LAN Tail 必须是自描述、有界和可跳过的容器。`tail_v1` 的 wire schema 在任何生产切片开始前固定如下；数字均使用网络字节序，所有长度在分配前用 checked arithmetic 验证：

```text
zero padding to next byte boundary
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

- `lan.capabilities`：房间选定协议、客户端能力和关键扩展确定版本。
- `lan.roster`：authoritative roster。
- `lan.rejection`：原版 `ClientConnectionFailedMessage` 的 LAN Tail 结构化拒绝原因。

三个 LAN 保留 entry 的 envelope `flags` 固定为 `1`（critical），entry version 均固定为 1。发送端必须按 entry ID 的原始 UTF-8 byte sequence 升序编码所有 entry；接收端允许 entry 以任意顺序出现，但必须拒绝重复 ID，并在验签、摘要和日志中按相同升序规范化。该规则固定 golden bytes，同时避免把输入顺序当作协议语义。

#### `lan.capabilities` payload v1

```text
recordKind: uint8

when recordKind = 1 (peer offer):
  lanProtocolMin: uint16
  lanProtocolMax: uint16
  clientVersionByteLength: uint8
  clientVersion: UTF-8 bytes
  flags: uint16 (bit 0 = RitsuLib present; other bits must be zero)
  criticalExtensionCount: uint8
  criticalExtensions[] sorted by extension ID bytes:
    idByteLength: uint8
    id: UTF-8 bytes
    minVersion: uint16
    maxVersion: uint16

when recordKind = 2 (session selection):
  selectedLanProtocolVersion: uint16
  criticalExtensionCount: uint8
  criticalExtensions[] sorted by extension ID bytes:
    idByteLength: uint8
    id: UTF-8 bytes
    selectedVersion: uint16
```

限制：

- `recordKind` 只允许 1 或 2。
- 客户端版本最多 32 UTF-8 bytes，必须是规范化非空版本字符串。
- 关键扩展最多 16 个，ID 最多 64 UTF-8 bytes，不能为空或重复。
- min version 不得大于 max version。
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

该 entry 的 entry version 固定为 1，critical flag 必须为 1，仅允许出现在 `ClientConnectionFailedMessage` 的 LAN Tail：

```text
schemaVersion: uint8 = 1
reasonCode: uint16
requiredClientVersionByteLength: uint8
requiredClientVersion: UTF-8 bytes (0 length means not applicable)
relatedExtensionIdByteLength: uint8
relatedExtensionId: UTF-8 bytes (0 length means not applicable)
requiredExtensionVersion: uint16 (0 means not applicable)
detailByteLength: uint16
detail: UTF-8 bytes for diagnostics only
```

`reasonCode` 固定值：

```text
1 = client_update_required
2 = protocol_profile_unsupported
3 = ritsulib_not_allowed_in_compat_mode
4 = critical_extension_mismatch
5 = game_version_mismatch
6 = wire_cache_mismatch
7 = lan_tail_required
8 = lan_tail_malformed
9 = lan_protocol_version_mismatch
10 = critical_extension_unknown
```

限制：客户端版本最多 32 UTF-8 bytes，扩展 ID 最多 64 UTF-8 bytes，detail 最多 512 UTF-8 bytes。客户端只按 `reasonCode` 和结构化字段决定 UI；detail 不参与逻辑或兼容判断。未知 reason code 显示通用协议拒绝并记录原值，不继续加入。

固定限制：

- 容器最大 256 KiB。
- entry 最多 32 个。
- 单 entry payload 最大 64 KiB。
- entry ID 最多 64 UTF-8 bytes，不能为空。
- roster 必须包含 2-8 名玩家。
- entry ID 不得重复。
- container 必须被恰好消费，不允许 LAN Tail 内出现未声明 trailing bytes。
- 对齐 padding 必须全为零。

版本一致性规则：

- 三种 request（`ClientLobbyJoinRequestMessage`、`ClientLoadJoinRequestMessage`、`ClientRejoinRequestMessage`）的 peer offer 尚未获准进入当前 session，`sessionProtocolVersion` 必须为 0。
- 其余 `tail_v1` 消息已经绑定房间 selection，`sessionProtocolVersion` 必须等于房间选定 LAN protocol；`alpha.1` 中固定为 1。
- `lan.capabilities` entry version 固定为 1；peer offer 的 `recordKind=1` 只能出现在 `sessionProtocolVersion=0` 的三种 request，session selection 的 `recordKind=2` 只能出现在非零且匹配房间 selection 的消息。
- `lan.roster` entry version 和其 payload 内 `schemaVersion` 都固定为 1，二者任一不为 1 即拒绝。
- session selection payload 中的 `selectedLanProtocolVersion` 必须等于容器 `sessionProtocolVersion`。
- 任一重复版本字段不一致时拒绝整条消息，不允许选择其中一个继续解析。

`containerByteLength` 从 `containerVersion` 开始计到最后一个 payload，便于先验证边界再读取 entry。未知非关键 entry 可按长度跳过；未知关键 entry、未知 container version、重复 ID、越界长度、非零 padding 或未完全消费均使整条消息无效。

LAN Tail 后可以存在由可行性门禁验证过的 RitsuLib tail；LAN reader 只能消费自己的 `containerByteLength`，不得吞掉后续容器。

### 4.3 房间协议与关键扩展版本选择

版本选择在创建房间时完成，之后冻结：

1. 房主提交 peer offer，包括 LAN protocol range 和完整的本地关键扩展 range 集合。
2. 服务端将房主 LAN range 与服务端允许的 `tail_v1` range求交，选择最高共同版本；无交集则拒绝创建。
3. `alpha.1` 的服务端允许范围固定为 `1..1`，因此选择结果只能是 1。
4. 服务端维护 `critical extension ID -> 已验证 min/max version` 白名单。对房主声明的每个关键扩展，计算 `房主 range ∩ 服务端已验证 range` 并选择最高共同版本；ID 不在白名单或无交集时拒绝创建。
5. 加入者必须声明完全相同的关键扩展 ID 集合，并且每个 range 都包含房间选定版本；缺失、额外的本地关键扩展或版本不覆盖均拒绝加入。
6. 只有被 RitsuLib 公开 API明确且可验证地分类为非关键的扩展，才不进入房间 selection 且不影响加入。关键扩展进入服务端版本白名单；API 无法分类、分类值未知或枚举不完整的扩展按未知关键扩展处理并拒绝。

房主不能直接指定 selection，只能提交 offer；服务端按上述确定性算法生成 selection。创建响应返回 selection，房主必须在建立游戏 host 前再次验证本地仍支持该 selection。

房间生命周期内 selection 不变：

- 房主 capability 改变时关闭房间，不重新协商。
- 房主断线时沿用当前项目的房间关闭语义；`alpha.1` 不新增 host migration。
- 房主重连或续局只能使用原 selection，不能重新选择版本。
- 服务端、房主和客机在 capability digest 中使用 canonical selection bytes，避免顺序差异。

Capability digest v1 的 canonical bytes 固定为：

```text
magic[8] = ASCII "LANSEL01"
schemaVersion: uint8 = 1
profile: uint8 (1 = compat_4_5_v1, 2 = tail_v1)
selectedLanProtocolVersion: uint16
maxPlayers: uint8
minimumClientVersionByteLength: uint8
minimumClientVersion: UTF-8 bytes
gameVersionByteLength: uint8
gameVersion: UTF-8 bytes
wireCacheSignatureByteLength: uint8
wireCacheSignature: lowercase ASCII bytes (0 length means unavailable)
criticalExtensionCount: uint8
criticalExtensions[] sorted by extension ID UTF-8 bytes:
  idByteLength: uint8
  id: UTF-8 bytes
  selectedVersion: uint16
```

数字使用网络字节序；版本字符串必须是规范化非空 UTF-8，最多 32 bytes；wire-cache signature 最多 64 ASCII bytes；关键扩展遵循 Tail capability 的数量/ID限制。Digest 是上述完整 bytes 的 SHA-256 lowercase hex。RitsuLib presence 是诊断字段，不进入 digest；关键扩展 selection 已表达实际 wire 依赖。客户端与服务端必须消费同一组 golden vectors。

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

矩阵中标记为必需的 entry 缺失、标记为禁止的 entry 出现或 record kind 不匹配时，整条消息无效。协议拒绝先按原版稳定 ID/body 编码 `ClientConnectionFailedMessage`（`disconnectionReason=ModMismatch`，`versionInfo` 使用当前 host 值），再追加完整 LAN Tail envelope；Tail 必须包含 session selection + rejection，禁止 roster 和其他 LAN entry。该类型 ID 是 STS2 内建 `INetMessageSubtypes` ID 34，不受单边安装其他 MOD 时的动态 subtype 数量影响。它只允许当前 transport host 发给仍处于三种 request 等待状态的单个 peer。客户端先完整读取原版 body，再验证 LAN Tail、host、selection 和请求阶段，完成对应等待任务为 `LanConnectProtocolException`，断开并禁止后续原版 response 完成成功路径。若 Tail 缺失，保留原版 `ClientConnectionFailedMessage` 行为，供 compat/非 LAN peer 使用。

目标 STS2 程序集已确认 `ClientLobbyJoinResponseMessage`、`ClientLoadJoinResponseMessage` 和 `ClientRejoinResponseMessage` 都没有稳定的 success/failure discriminator；后两者还要求在 Tail 前反序列化完整 `SerializableRun`。因此禁止伪造 rejected 原版 response，也禁止按 roster/rejection entry 是否存在反推原版 response 成败。普通、载入和运行中 join 的协议拒绝统一使用上述原版 failure message + LAN Tail；服务端 ticket 拒绝继续使用 HTTP structured error。

三种成功 response 的 full snapshot 使用当前 host authority 和 host session membership/slot binding table；该表包含当前连接状态，不等同于只含在线 peer 的 transport connection list。它们不表示新的 roster mutation：发送时必须使用 host 当前已提交的 `rosterRevision`；只有实际加入、离开或 slot 变化才能递增 revision。

客户端首次收到任一成功 response 且尚无已接受 snapshot 时，可执行一次 bootstrap：revision 必须大于 0，sender 必须是 transport host，selection/digest 必须匹配 ticket，当前加入者 ID/slot 必须匹配 ticket binding，vanilla projection 必须匹配 Tail，且 roster 必须与该 session state 的原版 authoritative carrier 一致（InLobby 使用 `playersInLobby`；InLoadedLobby 使用 `playersAlreadyConnected` 加当前获准加入者；Running 使用 `serializableRun` 的 multiplayer roster 加当前 rejoin binding）。满足后将该 revision 和 canonical roster 设为 baseline。已有 baseline 时只接受相同 revision + 完全相同 snapshot，或由已验证 membership mutation 产生的更大 revision；较小 revision、相同 revision 不同 roster、无 mutation 的更大 revision均拒绝。保存文件不持久化 roster revision；进程内 reconnect 可带旧 baseline，但仍按相同规则验证。

### 4.5 原版 roster 投影

发送原版 body 前：

1. 将 authoritative roster 按真实 slot 升序、再按 player ID 升序形成 canonical order，并取前四名玩家。
2. 按 canonical index 将四名玩家确定性映射到原版 slot `0..3`。
3. 不保留真实 slot；真实 slot 只存在于 LAN Tail authoritative roster。
4. 投影只用于原版消息主体，不修改 authoritative roster。
5. 对携带 `lan.roster` 的消息，接收端逐项验证投影 player ID、临时 slot 和 Tail roster 的确定性投影完全一致；任一差异均拒绝整条消息。

接收端先按原版格式读取投影，再读取 LAN Tail，并在进入原版 lobby/begin-run handler 前恢复完整 roster。

必须覆盖的首版消息面：

- `InitialGameInfoMessage`：房主 capability。
- `ClientLobbyJoinRequestMessage`：加入者 capability。
- `ClientLobbyJoinResponseMessage`：完整 lobby snapshot 或结构化拒绝。
- `ClientLoadJoinRequestMessage` / `ClientLoadJoinResponseMessage`：载入大厅 capability 重验、完整 roster 或结构化拒绝。
- `ClientRejoinRequestMessage` / `ClientRejoinResponseMessage`：运行中重连 capability 重验、完整 roster 或结构化拒绝。
- `PlayerJoinedMessage`：高 slot 玩家完整数据。
- `LobbyBeginRunMessage`：最终 authoritative roster。

原版 roster 投影和 body/Tail 一致性验证适用于所有携带 `lan.roster` 的成功 response、`PlayerJoinedMessage` 和 `LobbyBeginRunMessage`。`InitialGameInfoMessage` 和三种 request 不携带 roster，也不修改其原版 body；它们只按现有 STS2 消息语义验证 sender/receiver identity，并使用 `lan.capabilities` 完成协议协商。LAN rejection message 没有原版 roster/body，且不得让任何原版 response payload 进入成功 handler。

### 4.6 Authority 与一致性

- 只有当前 transport host 可以发送三种 response、带 LAN rejection Tail 的 `ClientConnectionFailedMessage`、player-joined 和 begin-run authoritative full roster snapshot。
- 三种 request 只能携带发送者自身 capability，不能携带 authoritative roster。
- sender ID 必须与当前连接 peer、消息内 player ID 和服务端签发的 slot 绑定一致。
- `PlayerJoinedMessage` 的新增 player ID 必须对应刚建立的连接，slot 必须是当前未占用 slot。
- vanilla 投影必须是 Tail authoritative roster 的确定性投影；Tail 与 body 冲突时不得选择其中一方继续运行。
- roster ID、slot、数量、当前连接表或房主身份任一不一致时，消息不得交给原版 handler，并断开产生无效消息的 peer。

### 4.7 结构化协议失败传播

客户端内部所有建房/加入入口使用同一个不可丢失的 `LanConnectProtocolFailure` 值，字段至少包含规范 reason code、required client version、related extension ID、required extension version 和仅供诊断的 detail。HTTP `LobbyErrorResponse`、Tail `lan.rejection` 和本地验证必须转换为该值；未知 code 仍保留原始 code 并按不可重试协议失败处理。

- `LanConnectProtocolException` 必须携带非空 `LanConnectProtocolFailure`，且永远不可作为 transport 候选重试条件。
- host create/publish 返回结构化 host attempt result；不得先转换为 `bool` 或普通字符串再交给 UI。
- lobby join 和 direct-IP join 返回结构化 join attempt result，其中协议失败字段与普通显示文本分离。
- mod preflight/ticket 结果必须保留服务端协议错误；不得在 `LobbyServiceException` 到 UI 之间丢失 details。
- managed join 的 InLobby、InLoadedLobby、Running 三条分支都将 Tail 拒绝或 codec/authority 失败转换为同一异常和值。
- direct/relay candidate 循环遇到协议失败立即停止，断开当前 transport 并释放 tentative lease；只有明确 transport timeout/unknown-network failure可以换候选。
- restart auto-rejoin 遇到协议失败必须清除 pending reconnect、显示一次结构化提示并停止轮询，直到用户重新发起加入或房间 selection/capability 发生变化。普通暂时性 room-not-found/transport failure 保留现有轮询。
- cancellation 保持 cancellation；无关异常保持 internal error，不能伪装成协议失败。

HTTP protocol error details 的 JSON/C# 镜像固定为以下可选字段：`requiredClientVersion: string?`、`relatedExtensionId: string?`、`requiredExtensionVersion: number?/int?`、`detail: string?`。规范 reason code 使用顶层 `LobbyErrorResponse.code`；未知 code 原样保留。服务端和客户端均执行与 Tail 相同的 32/64/512-byte 字段上限，extension version 必须为 `1..65535`。

### 4.8 Profile 不允许自动切换

游戏连接建立前必须确定 profile。原因是第一条游戏消息已经依赖正确 wire 格式，同一广播也不能同时让旧客户端按 `4/5-bit`、新客户端按 `2/3-bit + tail` 解码。

因此：

- profile 在建房时显式选择。
- profile 存入房间状态并随列表和 join response 返回。
- profile 在房间生命周期内不可变。
- 新客户端不能因旧客户端尝试加入而自动把 `tail_v1` 降级为兼容模式。

## 5. RitsuLib 共存

### 5.1 基本规则

- LAN roster tail 永远由 LAN Connect 编码和解码，并位于任何可选 RitsuLib tail 之前。
- 裸 RitsuLib 可以有或没有，不改变 LAN Tail framing。
- LAN Connect 只通过 RitsuLib 公开 API发现扩展能力并协调公开的 tail 顺序。
- 禁止反射 RitsuLib 私有 `SerializePatch<T>`、私有 postfix 或内部 Harmony owner。
- 禁止卸载、恢复或手工调用其他 MOD 的 Harmony patch。

### 5.2 自由混装边界

`tail_v1` 房间允许以下组合：

- 所有人都未安装 RitsuLib。
- 部分玩家安装裸 RitsuLib。
- 所有人都安装 RitsuLib。

RitsuLib 扩展按影响分类：

| 分类 | 行为 |
|---|---|
| 非联机关键、可安全忽略 | 允许缺失，未知 entry 可跳过 |
| 联机关键、影响运行或存档状态 | extension ID 和兼容版本范围必须满足全房一致性 |

只有当 RitsuLib 公开 API能够完整枚举当前注册的网络扩展，并明确分类其是否联机关键时，才允许 RitsuLib 与 `tail_v1` 共存。`alpha.1` 对完整枚举中的关键扩展使用 ID + 已验证版本范围白名单；明确分类为非关键的扩展可以不在白名单中。API 调用失败、无法证明枚举完整、分类未知，或关键扩展未列入白名单/版本无交集时，拒绝进入 `tail_v1`。白名单不能替代完整枚举与分类能力。

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

公开 room-list 在协议实施前必须确认是否携带可靠调用方版本。如果不能按版本返回变体，则列表 DTO保留旧客户端可解析的 `protocolProfile` 字段，并新增 0.6 可选的规范 profile/capability 字段；0.6 客户端优先读取新字段。真实 0.5.5 create/list/join/control fixture 是服务端模型改造的阻塞前置。

### 6.2 房间 capability

房间至少发布：

- protocol profile。
- 按 §4.3 算法冻结的 LAN protocol version。
- 最低客户端版本。
- 最大人数。
- RitsuLib framework presence（诊断字段，不单独决定兼容性）。
- 按 §4.3 算法冻结的 critical extension ID/version 集合。
- 游戏版本。
- wire-cache signature。

### 6.3 加入门禁顺序

服务端签发 join ticket 前按顺序检查：

1. profile 是否受加入客户端支持。
2. 客户端最低版本是否满足。
3. `compat_4_5_v1` 中双方是否均未启用 RitsuLib。
4. 加入者是否支持房间冻结的 LAN protocol version。
5. 加入者是否支持房间冻结的 critical extension 确定版本集合。
6. 游戏版本是否一致。
7. wire-cache signature 是否一致或满足既有明确的缺失策略。
8. 房间状态和人数是否允许加入。

`tail_v1` 客户端游戏握手必须重复验证 profile、选定 protocol 和关键扩展，避免旧服务端、缓存状态或加入后本地状态变化绕过门禁。`compat_4_5_v1` 中的 0.3-0.5 客户端不理解新 capability，使用服务端冻结 profile 加现有游戏版本/MOD inventory 握手。

### 6.4 错误码

至少提供：

- `client_update_required`
- `protocol_profile_unsupported`
- `ritsulib_not_allowed_in_compat_mode`
- `critical_extension_mismatch`
- `game_version_mismatch`
- `wire_cache_mismatch`
- `lan_tail_required`
- `lan_tail_malformed`
- `lan_protocol_version_mismatch`
- `critical_extension_unknown`

UI 必须显示面向用户的具体说明，不能全部退化为 `ModMismatch`、`InternalError` 或加入失败。握手期间返回结构化拒绝；游戏中收到 malformed、unauthorized 或不一致 Tail 时记录具体原因、断开对应 peer，并禁止把半恢复消息交给原版 handler。

## 7. 兼容性矩阵

| 房主 | 客机 | 模式 | 结果 |
|---|---|---|---|
| >=0.6 且支持选定协议 | >=0.6 且支持选定协议 | `tail_v1` | 允许 |
| 0.6 + 裸 RitsuLib | 0.6 无 RitsuLib | `tail_v1` | 允许 |
| 0.6 | 0.3-0.5 | `tail_v1` | 拒绝并提示更新 |
| 0.6 | 0.3-0.5 | `compat_4_5_v1` | 允许 |
| 0.6 + RitsuLib | 0.3-0.5 | `compat_4_5_v1` | 拒绝 |
| 0.3-0.5 | 0.6 | 旧客户端创建的兼容房 | 允许 |
| 0.2.x | 任意 | 任意 | 拒绝并提示版本过旧 |
| 0.6 | 0.6 | 关键扩展不兼容 | 拒绝 |

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
- RitsuLib sidecar 大厅握手兼容需单独重新评估；若它仍依赖私有 API，则不自动继承到新设计。

### 8.3 新增

- LAN Tail v1 codec。
- 原版 roster 投影与恢复。
- LAN/Ritsu capability collector。
- 新 profile 的建房 UI和提示。
- 服务端错误码对应的客户端提示。
- 字节级 golden vector 测试工具。

## 9. 测试与发布门禁

### 9.1 字节级契约测试

必须有独立 known-good golden bytes，而不是用实现算法生成 expected value：

- 2、3、4、5、6、7、8 人 roster。
- slot `0..7`。
- 高 slot 玩家位于原版投影前四项。
- LAN Tail encode/decode round-trip。
- 无 tail、截断、错误 magic、未知 container version。
- 重复 player ID、重复 slot、越界 slot、数量超限。
- 未知非关键 entry 跳过。
- 未知关键 entry 拒绝。
- RitsuLib 有/无时 LAN roster payload 完全相同。
- 原版 body 在 2-8 人情况下始终保持原版 `2/3-bit`。
- 十种 STS2 消息类型各自的 known-good bytes（Initial、三种 request、三种成功 response、ClientConnectionFailed、PlayerJoined、BeginRun）；三种 request-stage LAN rejection 分支分别覆盖。
- LAN Tail 后跟真实 RitsuLib tail 的顺序、cursor 和单边安装组合。
- 最大 container/entry/payload 边界、整数溢出、截断和累计长度超限。
- 重复 entry/capability ID、零长度 critical entry、合法 Tail 后的非法 trailing garbage。
- 非 host authoritative roster、body/Tail roster 不一致和 ticket 后 capability 改变。
- LoadJoin/Rejoin request offer 不匹配、LAN rejection、response authority/session membership/revision 不一致、首次 bootstrap 和重复当前 snapshot 的幂等接受。
- HTTP、本地、普通 join、load join、rejoin、direct join 和 restart auto-rejoin 的结构化失败保持及不可重试行为。

### 9.2 历史兼容测试

- 固定 `0.3.x-0.5.x` 的 `4/5-bit` fixture。
- 0.6 兼容模式可以双向读取 fixture。
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
- `tail_v1` 单边裸 RitsuLib。
- `tail_v1` 双边 RitsuLib。
- `compat_4_5_v1` 的 0.6 <-> 0.5.5。
- 兼容模式中 RitsuLib 被拒绝。
- 2、4、5、8 人作为所有平台 smoke 的主边界；3、6、7 人至少分别在一个跨平台组合中完成建房、加入、准备和开局。

成功标准是所有参与端进入游戏状态同步，不是只完成建房或加入。

### 9.4 原子初始化

客户端启动时安装一套原子 dispatcher patch，不在建房/加入过程中动态 unpatch/repatch。dispatcher 在游戏连接建立前读取并冻结 session profile；连接存在期间 profile 不可修改，session 完全关闭后才可重置。

原子要求：

- 兼容 profile 的 `4/5-bit` patch 不是完整集合时 fail-closed。
- Tail profile 的投影、tail writer、tail reader和 handler restore 不是完整集合时 fail-closed。
- dispatcher 任一必需入口失败时整体回滚；不得留下任何 profile 的部分 patch。
- 日志必须输出 active profile、patch owner、protocol version 和 capability digest。

## 10. 发布策略

- 客户端：`v0.6.0-alpha.1`。
- lobby-service：`v0.6.0-alpha.1`。
- 两者同步发布和部署。
- 保留 `v0.5.5` 作为稳定客户端下载。
- `v0.5.6-rc2` 至 `rc4` 标记为已知 RitsuLib 兼容实验版本，不再推荐。
- `alpha.1` 只发布为 GitHub Pre-release。
- `alpha.1` 的目标是协议与跨平台验证，不承诺直接晋升正式版。
- 所有同房玩家仍必须使用相同 STS2 游戏版本。

## 11. 实施切片

实施计划应按端到端垂直切片拆分，而不是先写完所有 codec 再补 UI和测试：

1. **Profile/门禁切片**：服务端和客户端识别新 profile，0.2.x 明确拒绝；此阶段隐藏 `tail_v1` 建房选项并限制内部 prototype 为最多 4 人。
2. **普通 Join response 切片**：原版四人投影 + LAN Tail 完整 snapshot，可通过 golden bytes 验证。
3. **载入/重连 response 切片**：LoadJoin/Rejoin capability 重验、当前完整 snapshot、幂等 revision 和结构化拒绝。
4. **Player joined 切片**：高 slot 玩家占位、恢复和 fail-closed。
5. **Begin-run 切片**：最终 authoritative roster 恢复并完成真实双端开局。
6. **RitsuLib 切片**：通过 §4.0 可行性门禁后，接入公开 API capability、自由混装和关键扩展门禁。
7. **兼容房切片**：0.6 <-> 0.5.5，RitsuLib 明确拒绝。
8. **UI/发布切片**：建房选项、错误提示、文档、跨平台矩阵和 alpha 包。

每个切片必须保持测试绿色，并能够在 fresh context 中独立验证。

## 12. 风险与缓解

| 风险 | 缓解 |
|---|---|
| STS2 不保证 vanilla reader 忽略尾部字节 | 在目标游戏版本上建立原版 reader fixture 和真实双端测试 |
| Android 对动态闭合泛型 Harmony patch 仍有限制 | 尽量 patch 稳定非泛型 handler；必须动态闭合的入口进行 Android 真机启动门禁 |
| RitsuLib 公开 API不足以完整枚举或协调 tail 顺序 | 不发布自由混装支持，回到设计评审；禁止私有 postfix 桥 |
| 旧客户端无法识别新服务端 profile 字符串 | 服务端按客户端版本投影旧 DTO，并建立真实旧客户端 fixture |
| 两套 profile 增加维护成本 | 兼容 profile 只保留 0.3-0.5 所需的完整旧路径，不再增加新能力或 Ritsu 兼容 |
| 房间误发布 profile 导致首包错位 | profile 建房后不可变，服务端和客户端握手双重验证 |
| 0.5.6 RC 用户继续使用已知失败版本 | Release 和文档明确标记不推荐，稳定入口仍指向 0.5.5 |

## 13. 验收标准

设计实施完成必须同时满足：

1. 生产代码不存在 `legacy_4p=8/3-bit` 的可选运行路径。
2. `tail_v1` 不修改 STS2 原版玩家字段位宽，并通过 LAN/Ritsu 四种混装组合可行性门禁。
3. `compat_4_5_v1` 可与真实 `0.5.5` 双向加入并开局。
4. `tail_v1` 在 Windows、Android、macOS 组合中完成 2-8 人开局和同步。
5. 裸 RitsuLib 可自由混装，LAN roster bytes 不变。
6. 关键 Ritsu 扩展不兼容时，在 join ticket 前被拒绝。
7. 0.2.x 客户端得到明确更新提示，不能进入游戏握手。
8. 所有 golden vectors、服务测试、客户端单测和 Godot 测试通过。
9. 客户端与 lobby-service 均以 `0.6.0-alpha.1` Pre-release 发布。

## 14. 实施前阻塞研究

以下研究必须在对应切片开始前完成，不能在实现中临时猜测：

- 使用真实程序集证明 §4.0 的 LAN/Ritsu tail 顺序、cursor 和四种混装组合；这是 RitsuLib 切片的阻塞门禁。
- 确认 RitsuLib 公开 API能否完整枚举网络扩展并提供稳定的顺序协作契约。
- 捕获真实 `0.5.5` create/list/join/control DTO fixture，确认服务端 profile 投影方案。
- RitsuLib sidecar 大厅握手桥是否仍需要，以及能否改为公开 API。
- `alpha.1` 验证后，兼容模式的长期保留期限。

`tail_v1` schema 本身已在 §4.2 固定；实施计划只允许补充 golden bytes 和 capability digest 的算法，不得改变字段顺序、长度类型、对齐或边界限制。任何 wire schema 变更必须回到设计评审并提升 protocol version。
