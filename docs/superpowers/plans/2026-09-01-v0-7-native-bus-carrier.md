# STS2 LAN Connect v0.7 native_bus_v1 载体实施计划

- 日期：2026-09-01
- 状态：待实施
- **唯一权威设计**：[2026-08-31 v0.7 原生消息总线载体设计 spec（v12，已通过第十一轮 Codex 评审，APPROVE WITH CHANGES）](../specs/2026-08-31-v0-7-native-bus-carrier-design.md) —— 本计划只负责**排序与落点**，一切行为定义以 spec 为准；冲突时以 spec 为准。
- 目标版本：`0.7.0-alpha.1`（`minimumClientVersion` 同步取此值）

## 新会话必读（自包含背景，5 分钟）

1. **为什么要做**：v0.6 的 `ritsulib_sidecar_v1` 载体经反射绑定 RitsuLib typed sidecar API 投递协议容器；RitsuLib 0.5.14 重写其投递层（routed endpoints）后，0.5.18 实测 sidecar 协商停滞 → 加入请求永远无法送达 → 房主 10 秒 `LobbyJoinTimeout` 踢人（2026-08-30 用户反馈）。连续适配 0.5.10→0.5.18 证明是结构性成本。
2. **方案一句话**：弃用 tail/sidecar 双载体，注册自定义 `INetMessage`（游戏官方 mod 消息机制，BaseLib 同用），协议容器紧跟原版消息走原版 ch0 FIFO；RitsuLib 依赖归零。
3. **spec 已通过评审**：十一轮 Codex 评审（产物链在 `.omc/artifacts/ask/codex-*2026-08-31*.md`），最终 0 CRITICAL / 0 HIGH。spec 文档头的修订记录 v1→v12 是每轮发现的权威摘要。
4. **参考材料（只读，禁止提交）**：反编译游戏源 `tem/sts2-decompiled-v0.111.0/`（v0.111.0，41cef1ea）；0.107.1 fixture 在仓库外 `~/Desktop/STS2-fixtures/0.107.1-data/sts2.dll`；RitsuLib 源 `tem/STS2-RitsuLib/`（0.5.12）；事故日志 `tem/ritsulib-0.5.18-failure-client.log`；成功对照 `.omo/evidence/alpha6-final-ritsu-e2e-20260818/`。
5. **仓库规则**：先读根 `AGENTS.md` 与 `sts2-lan-connect/`、`Scripts/Lobby/` 子级 `AGENTS.md`。DTO 双端镜像（C# ↔ TS）必须同步改；中文文档保持中文；不编辑 `releases/`；TypeScript 严格 ESM（`node:` 导入 + `.js` 后缀）。

## Global Constraints（硬约束，违反即返工）

1. **类名冻结**：`LanConnectNativeBusMessage` 发布后永不改名/移动命名空间（`NetTypeCache` 按 `ContentSorter` 六级排序分配 ID）。
2. **禁止泛型补丁目标**：一切 Harmony 补丁目标必须是非泛型方法（`SendMessage<T>`/`SerializeMessage<T>` 禁止；Android gshared 历史教训）。
3. **禁止 `SetBufferMessages` 补丁**：该方法归 RitsuLib sync 补丁所有；补丁计划与测试必须断言无此目标。
4. **端序**：原版 9 字节线头 `[typeId:1][senderId:8]` 的 `senderId` 为**小端**（PacketWriter 实现）；我们的外层/内层新字段一律**大端**。
5. **ENet 入站 `mode` 恒为 `None`**（`ENetConnectionExtension.cs:39`）：入站可靠性判定只看 `channel == 0`，任何地方不得比较 mode。
6. **防护边界（反防御性编程）**：只实现 spec 已定义的机制；实施中发现"似乎该加"的防护，先回 spec 评审，不得现场发挥。
7. **实施期验证项**（v0.107 ABI 对比、最新版 RitsuLib 源码核对、真实缓冲流程）是**发布门禁**，不阻塞编码，但没跑完不得发布。
8. **纯 direct-IP 路径不动**：继续 compat-only（v0.6 既有非目标）；本计划的"直连"一律指大厅 direct 候选。

## Planned File Structure

### 客户端新增
```
sts2-lan-connect/Scripts/Protocol/NativeBus/LanConnectNativeBusMessage.cs      # 消息类型 + 外层帧（Task 1）
sts2-lan-connect/Scripts/Protocol/NativeBus/LanConnectNativeBusSender.cs       # 专用发送出口（Task 2）
sts2-lan-connect/Scripts/Protocol/NativeBus/LanConnectRegistryFingerprint.cs   # 指纹计算（Task 4）
scripts/abi-compare-sts2.sh                                                     # 双版本 ABI 对比（Task 8）
scripts/check-native-bus-migration.sh                                           # 零引用门禁（Task 6）
```

### 客户端改造（详见各 Task；完整删/改/留清单以 spec §4.1 迁移矩阵为权威）
重点：`LanConnectTailMessagePatches.cs`（10 个 Serialize prefix 收缩 + transport prefix/postfix + TryDeserializeMessage 拆分）、`LanConnectTailMessageRuntime.cs`（删 sidecar 流、保 PrepareOutgoing/deferred）、`LanConnectWireCacheHandshakeGate`（改乘 native 载体）、`LanConnectCapabilityDigest.cs` / `LanConnectProtocolProfile.cs` / `LanConnectProtocolSelection.cs` / `LanConnectLobbyModels.cs`、`LanConnectLobbyRuntime.cs`、`LanConnectLobbyJoinFlow.cs`、`LanConnectLobbyOverlay.cs`。

### 服务端改造
`lobby-service/src/protocol-capabilities.ts`、`app.ts`、`store.ts`、`client-version.ts`、`protocol-errors.ts` 及对应 `*.test.ts`。

---

## Task 0：基线与分支

1. 通读 spec 全文 + 本计划 + 根/子 AGENTS.md。
2. 建分支：`git checkout -b v0-7-native-bus-carrier`。
3. 基线验证全绿再动工：`cd lobby-service && npm ci && npm run check && npm test`；客户端构建命令见 Execution Notes。

**验收**：两侧基线绿；分支就绪。

## Task 1：消息类型与外层帧编解码

新建 `LanConnectNativeBusMessage.cs`（行为定义见 spec §3.1，逐条照抄）：
- `INetMessage`：`ShouldBroadcast=false`、`Mode=Reliable`、`ShouldBuffer=true`、`LogLevel=VeryDebug`；
- payload：`[magic:2=0x4C 0x42][ver:1=1][localTypeId:4 大端][frameLen:4 大端][frame]`；`localTypeId = MessageTypes.TypeToId<LanConnectNativeBusMessage>()`；
- `Serialize`：发送侧 `frameLen > 65000` ⇒ 抛结构化异常（编码前拒绝）；
- `Deserialize`：**非抛出**。失败（magic/ver 不符、`frameLen` 越界、packet>66000）记 `InvalidReason`，只消费 `frameLen` 界定内字节，尾随内容（RitsuLib trailer 36 字节）忽略；`Frame`/`LocalTypeId`/`InvalidReason` 暴露给屏障。

单测（新工程文件，golden vector 落 `sts2-lan-connect/Tests/`，注意 xUnit 禁用 `Log.*`，用 sink 下沉）：
- 逐字节 golden vector（大端字段断言）；
- 尾随 0/1/30/36/随机字节 ⇒ 成功且忽略；
- `frameLen=65000` 过 / `65001` 拒 / 截断拒且不抛 / magic 错拒且不抛。

**验收**：单测绿；类名/命名空间冻结声明写入文件头注释。

## Task 2：发送链三级（全部非泛型）

按 spec §3.2：
1. **第一级**：改造 10 个具体消息 `Serialize` prefix——职责收缩为"调 `LanConnectTailMessageRuntime.PrepareOutgoing` 生产容器，挂 writer 键控 pending"；**不改写原版序列化字节**。
2. **第二级**：`ENetClient.SendMessageToHost` / `ENetHost.SendMessageToClient` 的 **prefix**——Harmony 优先级**先于 RitsuLib 发送 prefix**；按 buffer 引用从 writer 表解析 pending（必须在第三方替换 buffer 前建立关联），移入线程静态 `currentSend` 槽；**不注销**（宿主广播是一次序列化 + 同一 buffer 循环逐 peer，`NetHostGameService.cs:114-129`）；按 `(pending, recipient)` 消费集去重；保留 `PacketWriter.Reset` prefix（职责=清除该 writer 的 pending）。
3. **第三级**：postfix——入口第一行 `if (nativeSendReentry) return;`；从槽取容器 → 按接收方 stamp 逐 peer 序号 → 编码外层帧 → 调 `LanConnectNativeBusSender.Send`：置重入标志 → 手工拼线头 `[typeId:1][senderId:8 小端=NetService.NetId]` → 直接调 transport（Reliable/ch0）→ finally 清标志。
4. 两处 transport 补丁加 **finalizer**（异常清理 currentSend 与消费集）；host 无 peer / client `_peer==null` ⇒ 该 peer 结构化失败+断开（"未抛异常 ≠ 发送成功"）。
5. 拒绝路径（原 `PrepareRejection` 抑制语义）：原版照发，扩展帧携带拒绝码。

单测：广播批次（一次序列化 + N 次单发 ⇒ N 个 peer 各得一条扩展帧）；RitsuLib 式 buffer 替换（新数组+新长度）下关联存活；自发送重入免疫；重复消费守卫；finalizer 清理；补丁清单断言（无泛型目标、无 SetBufferMessages）；宿主"接收后再广播"路径（`NetHostGameService.cs:186-194`，逐 peer 重新序列化）。

**验收**：单测绿；§6.1 生产链用例全绿。

## Task 3：接收链

按 spec §3.3/§3.4：
1. `OnPacketReceived`（host+client）**prefix**：线程静态记录 `(transportSenderId, mode, channel)`。
2. `NetMessageBus.SendMessageToAllHandlers` **prefix**（配对屏障）：
   - 按 `(传输层 sender, channel)` 分片 **hold** 矩阵消息；同 sender 的下一帧必须是 native 扩展帧；**其他 peer 消息允许先行**（原版跨 peer 本就无序）；
   - 配对成功：校验（kind+nonce+逐 peer 序号+§3.1 边界+InvalidReason）→ **先应用扩展语义，再**经 bypass 标志 deferred 分发原版 handler；
   - 2000ms 未配对 ⇒ `lan_extension_missing` 断开该 peer；
   - **上下文旁挂表**：`ConditionalWeakTable`（或 `ReferenceEqualityComparer` 字典）`message 实例 → (transportSenderId, channel)`；消息首次进入分发层时记录（此时仍在 OnPacketReceived 调用栈内），原版缓冲释放再次进入时查表恢复并**消费即删**，断开时清理。键必须是引用身份（矩阵含 struct，装箱相等性会串键）；
   - **不 patch `SetBufferMessages`**：缓冲期两帧同入原版 `_bufferedMessages`，原版按到达序释放，屏障在释放路径上正常 hold/配对；
   - 扩展帧仅接受 `channel == 0`（mode 不参与判定）。
3. `NetMessageBus.TryDeserializeMessage`：
   - **prefix**：首字节为 native ID 且 `length < 9` ⇒ 转 `lan_native_frame_invalid`（原版读 senderId 会越界）；
   - **postfix**：未知 ID 分支从 offset 9 查 `[magic][ver]`——**仅当外层帧完整解析通过**才报 `lan_type_id_mismatch`；仅前缀相似 ⇒ 维持原版"警告一次后丢弃"；
   - finalizer 清理。
4. 配对身份只认传输层 sender，**不信任** `overrideSenderId`。

单测：正常/乱序（防御性）/超时；缓冲开/关（含"矩阵 A 先到、普通 B 后到"顺序 + 双 peer 交错 + RitsuLib sync 共存）；`overrideSenderId` 伪造用例仍按传输 sender 配对；`None/ch0` 合法通过、`channel!=0` 拒；双 peer 等值 boxed struct 不串键；重入保护。

**验收**：单测绿。

## Task 4：registry fingerprint 与服务端门禁

按 spec §3.4/§5：
1. 客户端 `LanConnectRegistryFingerprint.cs`：`sha256:v1:<64 位小写 hex>`，全表按 id 升序，条目 `[id:4 大端][modIdLen:1][modId][flags:1(bit0=affectsGameplay)][asmLen:1][assemblyName][nameLen:2 大端][typeFullName UTF-8]`；C#/TS 共用 golden 向量。
2. 客户端：digest carrier 编码加 `NativeBusV1=3`；`ritsuLibVersion` **不参与 digest 哈希**（房间 DTO/preflight 可选信息字段）；`LanConnectLobbyModels.cs` DTO 加 `registryFingerprint`/`ritsuLibVersion` 与新错误码；`LanConnectProtocolFailureMapper` 补 known-code（五个新码）。
3. 服务端（注意：合同核心在 `protocol-capabilities.ts` + `app.ts`，**不是** `server.ts`）：
   - `protocol-capabilities.ts`：carrier 枚举加 `native_bus_v1`；`selectRoomProtocol` 对 tail_v1 一律 native（**完全忽略 Ritsu presence 与 sidecarAvailable**）；digest 编码 +3；`parseProtocolOffer` 放行 `registryFingerprint`（tail_v1 创建必填）/`ritsuLibVersion`（可选）；
   - `app.ts`：创建路径 fingerprint 必填且**房间分配前**校验拒绝；join（ticket 签发）门禁链顺序 `presence → carrier（旧载体=`lan_legacy_carrier_unsupported`）→ fingerprint（缺失/非法=`lan_registry_fingerprint_required`，不一致=`lan_registry_fingerprint_mismatch`，details 附双方摘要前 8 字符）→ minimumClientVersion（=`lan_client_version_too_old`，semver/prerelease `>=`）→ 既有 mod 列表`，整链**置于现有 wire-cache 预检查之前**；`/mod-preflight` 改只读投影（快速失败一律非 2xx 既有 error envelope，200 永不承载失败字段，不签发 ticket、不强制上述字段）；`/probe` 投影随新 minimumClientVersion 更新；
   - `store.ts`：`validateJoinCompatibility` 接入门禁；`client-version.ts` 加 semver/prerelease 比较；`protocol-errors.ts` 补错误码；
   - 兼容：compat_4_5_v1 房间**不走** fingerprint/minVersion 门禁（旧合同）。
4. 契约测试：创建正/反例；join 三类 fingerprint 错误分别触达；旧 0.6 客户端↔新房间/新客户端↔旧房间（明确错误+文案）；门禁顺序（多重失败返回首个）；`sha256:v1` 双端向量一致。

**验收**：服务端 `npm run check && npm test` 绿（含新契约用例）；DTO 双端镜像核对。

## Task 5：sidecar 门禁全链路解除（0.5.18 事故的正面回归）

逐项执行 spec §4.1 的五处解除：`LanConnectProtocolOffer.Validate`、`LanConnectCapabilitiesCodec`、`LanConnectProtocolSelection.cs:68-81`、`LanConnectTailMessageRuntime.cs:1217-1238`（ValidatePeerOffer/EnsureSidecarReady/TryEnsureRegistered）、`LanConnectTailMessagePatches.cs:709-739`、服务端 `assertJoinerCompatible`/`protocol-capabilities.ts:63-67,112-114`——tail_v1 全链路**完全忽略 `sidecarAvailable`**，只保留 presence 同质化。

契约测试：构造"RitsuLib 存在但 typed sidecar 不可用"（即 0.5.18 事故状态）⇒ tail_v1 创建、加入、运行 native 全部正常，全程不出现 `ritsulib_sidecar_unavailable`。

**验收**：上述测试绿。

## Task 6：迁移清理与零引用门禁

1. 按 spec §4.1 矩阵执行删除：4 个整文件（`LanConnectRitsuLibSidecarCarrier.cs`、`LanConnectSidecarPairingCache.cs`、`LanConnectHostSidecarActivationGate.cs`、`LanConnectRitsuLibLobbyCompatibility.cs`）、`RitsuSidecarENetClientConnectionInitializer`（回归 `ENetClientConnectionInitializer`）、runtime sidecar 流程、`player_control_binding` sidecar 分支（`LanConnectLobbyRuntime.cs:2392-2408, 3705-3727`）、桌面泛型 plan 条目（`LanConnectTailMessagePatchPlan.cs:175-210` 及回滚入口 379/`LanConnectSerializationPatches.cs:294`）、`LanConnectGameplayPatches.cs:29` 兼容组开关、`LanConnectLobbyManagedJoinFlow.cs:83,151,425`、`LanConnectLobbyRuntime.cs:524,960,1122,1422,1480,3523` 生命周期引用。
2. 改造：`LanConnectExternalCapabilityCollector`（保留 presence/version 探测，删 carrier 调用）；`LanConnectMultiplayerSaveCompatibility.cs:138`（selection 重建时**本机即时重算** fingerprint，不持久化旧值）；`LanConnectModPreflightCoordinator`（请求/响应 schema 与新错误码透传，relaxed 路径不承载门禁）；UI 文案（`LanConnectLobbyOverlay.cs:619-620, 6302-6336`）。
3. 内部枚举改中性名 `LegacyTailV1`/`LegacySidecarV1`；旧值仅以字面量 `"standalone_tail_v1"`/`"ritsulib_sidecar_v1"` 存在于 DTO 映射与拒绝分支。
4. 新建 `scripts/check-native-bus-migration.sh`：`rg` 固定符号表 `RitsuLibSidecar|SidecarPairing|HostSidecarActivationGate|RitsuSidecarENetClientConnectionInitializer|SubmitSidecarBeforeVanilla|LanConnectStandaloneTailCarrier|LanConnectRitsuLibLobbyCompatibility|SerializeMessage<|LanConnectTailPlanOverride|ResolveDesktopPatchPlan|ResolveGenericSerializeMessageMethod|desktop_generic_v1`——源码（除迁移说明与测试 fixture）零命中；旧 carrier 字面量只允许出现在识别拒绝分支。接入 CI/发布检查。

**验收**：零引用脚本绿；客户端构建通过；全量测试绿。

## Task 7：启动自检与诊断

1. 启动自检：`MessageTypes.Count ≤ 256` 且全表 id→byte 映射唯一且本类型 ID 不与 BaseLib 冲突；异常 ⇒ 拒启用 native 载体并输出诊断（明确报错，不崩溃）。
2. 启动诊断行：`native_bus` 就绪、本地 typeId、registry fingerprint、补丁叠加顺序（含与 RitsuLib 的先后）。
3. connection-event 新 phase：`lan_extension_missing` / `lan_type_id_mismatch` / `lan_registry_fingerprint_mismatch` / `lan_native_frame_invalid` / `lan_client_version_too_old`。

**验收**：诊断行可见；自检正反例测试绿。

## Task 8：实施期验证门禁（发布阻断）

1. `scripts/abi-compare-sts2.sh`：接受**显式 fixture 路径 + SHA-256 + ilspycmd 版本**参数，对比 0.107.1 与本机 0.111.0：`ContentSorter` 六级排序键、未知 ID 分支行为、**入站 mode 恒 None**、§4.2.3 依赖面全部签名、原版区 ID 在有无本类型时稳定；差异输出文档，有差异先回 spec 裁决再继续。
2. 真机 E2E 矩阵（spec §6.2 全部行，macOS 房主 + Android AVD 客户端，手法沿用 `.omo/evidence/alpha6-final-ritsu-e2e-20260818/`）：重点行 3（全员最新版 Ritsu 走中继——0.5.18 事故回归）、行 5（v0.107.1 无 Ritsu/最新 Ritsu × 中继/直连全流程）、8b-1/8b-2（compat 含 dummy 第三方消息）、9（ticket 门禁不可绕过）、10（3+ 客户端 + 缓冲开关）、11（并发+注错）。
3. 测试基建注意（踩坑记录）：xUnit 中 `Log.*` 必崩（用 sink）；legacy 泛型相关用例必须在独立 Godot 进程跑；无窗口真机验证用 `steam_appid.txt` CWD + `--headless` 查 `godot.log`。

**验收**：ABI 文档无未裁决差异；E2E 矩阵全绿且全程无 `LobbyJoinTimeout`、扩展帧成对率 100%。

## Task 9：版本、打包、发布

1. 版本 `0.7.0-alpha.1`（客户端+服务端同步），`minimumClientVersion` 同值。
2. 中文发布说明 `docs/RELEASE_NOTES_V0.7.0_ALPHA1_ZH.md`（含"必须全员升级 0.7"说明与旧房间/旧客户端错误码表）。
3. 打包按 `scripts/build-sts2-lan-connect.sh` / `package-sts2-lan-connect.sh`；发前核对：包内含 `sts2_lan_connect.json`、Windows bat/ps1、macOS sh/command、唯一 `sts2_lan_connect-release.zip`、`lobby-defaults.json` 指向预期公共大厅。
4. Definition of Done：两侧测试全绿 + Task 8 门禁全绿 + 零引用门禁绿 + E2E 矩阵绿。

## Execution Notes

- 构建命令（本机 dotnet/Godot 不在 PATH）：
  ```
  export PATH="/Users/mac/.dotnet:$PATH" && export DOTNET_ROOT="/Users/mac/.dotnet"
  DOTNET_BIN=/Users/mac/.dotnet/dotnet \
  GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
  bash scripts/build-sts2-lan-connect.sh
  ```
- 服务端：`cd lobby-service && npm run check && npm test`。
- 提交风格沿用仓库 conventional commits（`fix(client):` / `feat(protocol):` 等），每 Task 至少一个独立提交，便于回溯。
- **禁止提交**：`tem/`（反编译与 RitsuLib 源为版权/第三方代码）、`.omo/evidence/` 评审产物按仓库现状保持未跟踪。
- 实施中若发现 spec 疑似缺陷：先查 spec 修订记录与对应评审产物确认意图，确属缺陷则小步修订 spec（追加修订记录条目）再继续，不得静默偏离。
- 完成定义之外的一切"顺手改进"（重构、重命名、格式化）一律不做——迁移分支只做迁移。
