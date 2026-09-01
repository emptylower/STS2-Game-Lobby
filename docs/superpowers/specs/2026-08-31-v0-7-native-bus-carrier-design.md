# STS2 LAN Connect v0.7 原生消息总线载体（native_bus_v1）设计

- 日期：2026-08-31
- 作者：STS2 LAN Connect 维护者
- 状态：**已通过评审（APPROVE WITH CHANGES，第十一轮，产物 `.omc/artifacts/ask/codex-...23-40-29-257Z.md`）**；v12 为收尾修订（落评审所列 1 MEDIUM + 2 LOW 文字澄清，无设计变更）
- 前置设计：[2026-08-13 v0.6 双协议迁移设计](./2026-08-13-v0-6-dual-protocol-design.md)
- 关联 issue：[BAKAOLC/STS2-RitsuLib#102](https://github.com/BAKAOLC/STS2-RitsuLib/issues/102)

### 修订记录

- v2（2026-08-31）：吸收第一轮评审（额度中断，恢复件 `tem/codex-review-recovered-v0.7.md`）7 项发现。
- v3（2026-08-31）：吸收第二轮完整评审：registry fingerprint 前置（C-2）、preparation seam 生产链（C-3）、矩阵补齐（H1）、传输层 sender 上下文（H4）、缓冲规则（H5）、ABI 扩面（H6）、E2E 扩项（H7）、帧格式细化（M3）、preflight 接入（M4）、digest 语义（M5）、优先级措辞（M2）。
- v4（2026-08-31）：吸收第三轮评审（产物 `.omc/artifacts/ask/codex-...14-54-58-552Z.md`），维护者裁定了阻断边界（见 §6.0）：① 专用 native send helper + 重入标志 + 手工线头拼装，封死递归与发送出口缺口（C1）；② fingerprint 校验强制挂在 **join ticket 签发路径**（唯一必经点，客户端 preflight 降为 UX，不承担门禁）（C2）；③ pending 关联改 prefix→postfix 同线程上下文传递，不依赖 buffer 引用（RitsuLib 发送 prefix 会替换 buffer 数组）（H1）；④ `SetBufferMessages` 落实补丁点与队列归属（H2）；⑤ 桌面泛型 patch plan 列入删除矩阵、CI 零引用改为固定符号表（H3）；⑥ 未知 ID 捕获定义 `__0` 注入与 offset-9 检查（H5）；⑦ E2E 第 9 项改为验证服务端门禁不可绕过、缓冲开关并入第 10 项（H6）；⑧ 外层上限与内层编码统一（M1）；⑨ 修正 `ContentSorter` 为多级排序的事实（Q1）。v0.107.1 ABI diff / 全流程证据、最新版 RitsuLib 源码核对为**采纳但实施期验证**，不作为 spec 阻断项。
- v5（2026-08-31）：吸收第四轮评审（产物 `.omc/artifacts/ask/codex-...15-41-38-921Z.md`）：① **广播批次 pending**：宿主广播是"一次序列化、同一 buffer 循环逐 peer 发送"（`NetHostGameService.cs:114-129`，不经 `ENetHost.SendMessageToAll`），pending 改为 writer 键控**不随首 peer 注销**、按 (pending, peer) 消费集去重、下次序列化时自然覆盖（C3）；② 线头端序明确：原版 9 字节头 `senderId` 为**小端**（PacketWriter 实现），外层/内层新字段保持大端，附逐字节 golden vector（H7）；③ `LanConnectNativeBusMessage` 补齐完整合同（字段、非抛出 Deserialize + InvalidReason），坏帧不再炸穿原版接收循环（M2/H8）；④ 两处 transport 补丁加 finalizer 清理 currentSend，host 无 peer / client 断连视为该 peer 结构化失败（H9）；⑤ 释放顺序修正为"扩展语义先应用，再调原版 handler"，并统一到达序（H10）；⑥ compat 模式与全局类型注册的矛盾给出不变量论证与 compat 全流程验收（H11）；⑦ 零引用符号表扩充（H3）；⑧ fingerprint 的 JSON 契约、缺失字段错误码、错误优先级与 minimumClientVersion 取值（M4）；⑨ 启动自检扩展为 `MessageTypes.Count ≤ 256` 且 byte 映射唯一（M3）；⑩ 上限给出精确常量与边界向量（M1）；⑪ E2E #8 限定 preflight 生效路径（M5）；⑫ ABI 脚本接受显式 fixture 路径与 SHA-256（M6）；⑬ `ContentSorter` 排序键补全（Q1：affectsGameplay → `type.Name` → null-mod → mod ID → FullName → assembly）。
- v6（2026-08-31）：吸收第五轮评审（产物 `.omc/artifacts/ask/codex-...16-20-20-323Z.md`）：① **pending 生命周期封口**：`PacketWriter` 长期持有同一 buffer 且 `Reset()` 只归零位置，下一条非矩阵消息会误命中残留 pending——恢复 v0.6 的 `PacketWriter.Reset` prefix 补丁，职责为清除该 writer 的 pending（C3 复发修正）；② 已知 native ID 的 <9 字节包在原版读取 senderId 时越界——`TryDeserializeMessage` prefix 增加一条前置检查转 `lan_native_frame_invalid`（H8）；③ `SetBufferMessages(false)` 改由屏障 **prefix 接管**（跳过原版释放体），按全局到达序合并 flush 原版队列与屏障队列（H10）；④ compat 第三方消息位移风险改为精确约束陈述 + 8b 实测升级（H11，见 §2.2.2）；⑤ fingerprint 条目补全身份字段（owning mod / affectsGameplay / assembly），删除"未来升级 SHA-256"表述（H12）；⑥ 错误码合同统一为两种并固定权威字段位置，补 `/probe` 投影行（M4）；⑦ 零引用符号表补入泛型回滚入口（H3）；⑧ §1.3 排序键与 §7 统一为完整六键（Q1）；⑨ trailer 精确为 36 字节（0.5.12 布局）并加入向量（M1）；⑩ 文档头版本号与 Android legacy 测试行修正（L1）。

- v7（2026-08-31）：吸收第六轮评审（产物 `.omc/artifacts/ask/codex-...17-05-32-190Z.md`），本轮以**删机制**为主：① 配对屏障重写为纯分发层"hold 一帧"——矩阵消息与扩展帧同走原版 `_bufferedMessages` 单一队列，到达序由原版释放天然保证，**删除自建队列与 `SetBufferMessages` 补丁**，结构性消除与 RitsuLib sync 补丁的双重所有权冲突（N1+H10 一并解决且更简）；② compat 验收口径改为诚实陈述：LAN Connect 自身流程必须正常，第三方 dummy 消息三种表现均为文档化可接受结果，不承诺第三方消息正常（H11）；③ fingerprint/minimumClientVersion 门禁显式限定 `tail_v1` 房间，compat 沿用旧合同，并固定与 wire-cache 预检查的先后顺序（M4）；④ 迁移矩阵补 preflight 协调器/错误映射/存档绑定/DTO 四行合同（N2）；⑤ 旧枚举内部改中性命名、CI 扫描只匹配 C# 标识符，消除与零引用门禁的矛盾（N3）；⑥ 生产链测试补宿主"接收后再广播"路径。

- v8（2026-08-31）：吸收第七轮评审（产物 `.omc/artifacts/ask/codex-...17-36-49-847Z.md`），本轮全部为**残留矛盾与口径修正**，无设计变更：① 迁移矩阵删除 v6 残留的"新增 SetBufferMessages 补丁"行，显式禁止该目标并要求补丁清单断言（C1）；② §3.3 删除"全局到达序天然保证"过强声明，改为**背靠背相邻性不变量** + 超时守卫兜底的准确表述（H1）；③ 8b 拆分为不碰撞（LAN 流程正常）/ 碰撞（结构化断开即预期结果）两个子场景（H2）；④ E2E #9 的错误码与 JSON 合同对齐（缺失/非法→required，不一致→mismatch），preflight 语义统一为"UX 快速失败、从不阻断"，旧载体房间定稳定错误码 `lan_legacy_carrier_unsupported`（H3）；⑤ 补 join/preflight 字段级合同、错误 details、存档绑定重算规则与 mapper 新码（M1）；⑥ §3.5 与 §4.1 的旧枚举命名统一为中性成员 + 字符串字面量（M2）；⑦ 迁移矩阵补 GameplayPatches/ManagedJoinFlow/LobbyRuntime 六处生命周期引用分类（M3）。

- v9（2026-08-31）：吸收第八轮评审（产物 `.omc/artifacts/ask/codex-...18-28-03-395Z.md`）：① **入站可靠性判定修正**：ENet 入站 `mode` 恒为 `None`（`ENetConnectionExtension.cs:39`），扩展帧可靠性判定改为仅 `channel == 0`，事实表/ABI 断言同步（C2）；② **配对语义按 sender 分片**：宿主单总线跨 peer 交错下，"相邻性"仅在同一传输层 sender 内成立，屏障按 `(senderId, channel)` hold、其他 peer 消息允许先行（与原版跨 peer 无序一致），测试用例同步（H1）；③ §5 表内 preflight 行改为"只读投影/快速失败"，快速失败结论走现有 HTTP error envelope（H3/M1 残留）；④ compat 验收口径限定"无 native-ID 碰撞组合"并引用 8b-2（H2 残留）。

- v10（2026-09-01）：吸收第九轮评审（产物 `.omc/artifacts/ask/codex-...19-21-29-749Z.md`，CRITICAL 已清零）：① **传输上下文旁挂表**：补齐原版缓冲释放路径丢失 `(transportSenderId, channel)` 的合同缺口——屏障在消息首次进入分发层时旁挂 `message 实例 → (transportSenderId, channel)`，释放时查表恢复并消费即删；配对分片与 ch0 校验用恢复的传输上下文（H1，评审明示属合同补齐而非新增防护）；② preflight 快速失败统一为非 2xx 既有 error envelope，200 永不承载失败字段（M1）；③ §6.1 残留的"非 Reliable/ch0"测试措辞改为"channel != 0，mode 不参与 ENet 入站判定"并加合法 `None/ch0` 用例（M2）；④ E2E 第 5 行参数化为"无 Ritsu/最新版 × 中继/直连"（L1）。

- v11（2026-09-01）：吸收第十轮评审（产物 `.omc/artifacts/ask/codex-...19-46-55-274Z.md`，0 CRITICAL）：① **sidecar 可用性门禁全链路解除清单**：矩阵逐项列出 Offer.Validate/CapabilitiesCodec/runtime EnsureSidecarReady 一族/TailMessagePatches/服务端 assertJoinerCompatible 五处，tail_v1 完全忽略 `sidecarAvailable`，并新增"Ritsu 存在但 sidecar 不可用 → native 全流程正常"的正面回归测试（0.5.18 事故场景，H1）；② 旁挂表键明确为**引用身份**（struct 装箱相等性会串键）并加等值 boxed struct 用例（M-CTX）；③ 未知 ID 捕获仅在外层帧**完整解析通过**时才升级为 mismatch，前缀相似维持原版丢弃（第三方不被误伤，M-MAGIC）；④ 创建侧 fingerprint 必填、房间分配前拒绝、杜绝无指纹房间（M-FP）；⑤ E2E 补 v0.111 无 Ritsu 直连行（L）。

- v12（2026-09-01，收尾）：第十一轮裁决 **APPROVE WITH CHANGES**（0 CRITICAL / 0 HIGH，十个问题全部通过）。落评审所列澄清：① `TryDeserializeMessage` 矩阵行拆分 prefix（<9 字节已知 ID 前置拦截）/ postfix（未知 ID 分支捕获）+ finalizer（M1）；② 纯 direct-IP 显式归为 compat-only 既有非目标，"直连"一律指大厅 direct 候选（L1）；③ ID 确定性补"mod 类型须正确关联 owning mod"前提，null-mod 退化路径由 fingerprint 安全拒绝（L2）。

## 1. 背景

### 1.1 触发事件：RitsuLib 0.5.18 联机失败（2026-08-30 用户反馈）

用户报告"联机时打开 RitsuLib 就无法联机"。诊断结论（完整证据链见 `.omo/evidence/` 与本次调试记录）：

- 环境：游戏 v0.111.0（41cef1ea），LAN Connect 0.6.0 stable，RitsuLib 0.5.18，双端启用 RitsuLib，经大厅 UDP 中继加入 `tail_v1` + `ritsulib_sidecar_v1` 房间。
- 现象：ENet 连接与原版游戏握手全部正常（模型哈希一致）；RitsuLib sidecar 协商中，客户端收到房主握手（opcode 16）后发出 `EndpointCatalog`（opcode 23，0.5.14 新增）与自身握手，房主侧从此完全静默；typed 流量从未出现；客户端等待 `InitialGameInfoMessage` 无果，房主 10 秒 `ClientResponseTimeout` 到期，以 `LobbyJoinTimeout` 断开。
- 根因：RitsuLib 0.5.14 重写 typed sidecar 投递层（routed endpoints + 协商确认 + 传输层发送方认证）。我们的载体依赖其运行时行为，API 未变但行为漂移导致投递停滞。LAN Connect 0.6.0 的验收矩阵只覆盖 RitsuLib 0.5.13（alpha.6 E2E 通过）。
- 放大缺陷：能力摘要只记录 `RitsuLibPresent` 布尔值（不含版本），版本异构通过全部预检后以最不透明的超时暴露；`LobbyJoinTimeout` 不在加入重试白名单，中继候选失败后 3 个直连候选未被尝试。

已向 RitsuLib 上游提交 issue #102（bug 报告 + 公开共存契约请求）。

### 1.2 结构性问题：为什么"再适配一次"不是答案

v0.6 的 `ritsulib_sidecar_v1` 载体通过**反射**绑定 RitsuLib 公开 typed sidecar API，将我们的协议容器交由 RitsuLib 传输。该选择解决了"尾部所有权"冲突（`standalone_tail_v1` 把容器追加在原版消息序列化尾部，与 RitsuLib 的消息尾部扩展争夺同一缓冲区），但引入了无契约的运行时依赖：RitsuLib 每次发版，其传输内部行为都可能静默漂移，而其测试矩阵不包含我们。0.5.10 → 0.5.12 → 0.5.13 → 0.5.18 的连续适配（见 git 历史 `fix(client): bridge RitsuLib begin-run serialization patch` 等系列）证明这是结构性成本，不是一次性事故。

### 1.3 转机：反编译确认游戏自带官方扩展点

对 sts2.dll v0.111.0（反编译产物在 `tem/sts2-decompiled-v0.111.0/`，仅本地参考，禁止提交）与 v0.107.1 的反编译确认：

| 事实 | 出处（v0.111.0） |
|---|---|
| mod 实现 `INetMessage` 的类型会被自动登记进游戏消息 ID 表 | `MessageTypes.cs:16-17`（`ReflectionHelper.GetSubtypesInMods<INetMessage>()`） |
| ID 由 `ContentSorter` 多级排序确定性分配（依次比较 affectsGameplay → `type.Name` → null-mod 排序 → mod ID → `Type.FullName` → assembly name）；双端完整 registry 一致 ⇒ ID 一致（**前提**：所有 mod 消息类型均正确关联 owning mod——未关联（null-mod）的同名类型在排序中会退化并列 `ContentSorter.cs:44,101`，可能因反射顺序不同而 ID 漂移；该异常路径由 fingerprint 主门禁**安全拒绝**，不会静默错配，v12 补 L2） | `NetTypeCache.cs` 构造器、`ContentSorter.cs:77-118` |
| 消息线格式 `[typeId:byte][senderId:ulong][payload]`，由原版 `NetService.SendMessage` 投递 | `NetMessageBus.cs:43-51` |
| mod 模式下未知 typeId 警告一次后静默丢弃，不破坏流 | `NetMessageBus.cs:59-71` |
| 原版消息只用 ch0（Reliable）/ch1（Unreliable），同通道 FIFO 有序 | `NetTransferModeExtensions.cs`、`NetClientGameService.cs:93` |
| 通道号收发两端原样透传不过滤 | `ENetHost.cs:221`、`ENetClient.cs:188` |
| **ENet 入站 `mode` 恒为 `NetTransferMode.None`**（`TryService` 不把可靠性标志映射回 mode）——入站方向不能用 mode 判可靠性，只能用 channel | `ENetConnectionExtension.cs:39`、`ENetHost.cs:221`、`ENetClient.cs:188` |
| BaseLib 已用同一机制注册自定义消息（ID 128/129），生产验证可行 | 运行日志（v0.111.0 用户环境） |
| v0.107.1 存在相同的 `MessageTypes`/`NetTypeCache` API | `STS2-fixtures/0.107.1-data/sts2.dll` 反编译 |

结论：游戏消息总线本身就是 mod 的官方载体。我们不需要自建运输（tail / sidecar），注册自己的消息类型即可。

## 2. 目标与非目标

### 2.1 目标

产品总目标（维护者确认，2026-08-31）：**正式版与测试版两个游戏版本都可运行本 MOD，尽可能避免与其他 MOD 冲突，与 RitsuLib 共存（适配），并在保证其他功能正常的前提下实现版本更新。** 本设计将其展开为：

1. 双游戏版本运行：v0.111.x（public-beta）与 v0.107.1（正式线）同一二进制均可加载并联机。依赖面与验证方式见 §4.2.3、§6.1（ABI 对比测试）。
2. 协议容器投递不再依赖 RitsuLib 的任何运行时行为、API 或传输通道；有无 RitsuLib 的房间走**同一条**容器投递代码路径（单一载体）。
3. 与 RitsuLib 共存：RitsuLib 存在时全部功能正常——共存靠"零交叠"而非"逐版本适配"；残余交叠面显式枚举并纳入 E2E（§4.2、§6.2）。**这是目标而非已证明事实**：本地核对依据是 0.5.12 源码与 0.5.13/0.5.18 的运行日志，实施门禁必须包含评审时点官方最新版 RitsuLib 的源码核对或黑盒 E2E（§6.2 矩阵第 3 项）。
4. MOD 冲突面最小化：相对 v0.6 净删除多类 Harmony 补丁（§4.1），保留/新增补丁均为非泛型、单点、带显式优先级；游戏更新敏感面见 §4.2.3。
5. 任何协议失配表现为**秒级、带明确错误码的结构化失败**，绝不复现 30 秒无声超时。
6. 版本更新安全：位宽 transpiler、compat_4_5_v1 兼容模式、capacity/gameplay/save 等既有功能零语义变更（§2.2 非目标反向保障）；E2E 矩阵作为发布阻断兜底（§6.2）。

### 2.2 非目标

1. 不改变位宽 transpiler 语义（slotId 2→4、lobbyList 3→5 保留，与 RMP 线级兼容不变）。
2. 不改变 `compat_4_5_v1` 旧兼容模式（carrier=none 路径不动）。**与全局类型注册的关系（v5 修正 H11）**：`MessageTypes` 反射注册是全局的，新增本类型在任何 profile 下都会进入 registry；安全性依赖 `ContentSorter` 的**首排序键 affectsGameplay**——原版消息（gameplay 区）在任意 mod 组合下 ID 稳定，本类型落入非 gameplay 尾区，仅影响其他非 gameplay mod 类型的相对位置。因此：compat 房间**永不发送 native 帧**（0.6 旧客户端对其表现为"未知 ID 警告后丢弃"，可容忍）；compat 加入**不要求 fingerprint 与 minimumClientVersion**（旧客户端无法计算；compat 房间沿用旧合同，门禁以 gameplay mod 列表 + presence 既有规则为准——**fingerprint/minVersion 门禁仅适用于 `tail_v1` 房间**，v7 修正 M4）；该不变量列入 §6.1 ABI 向量测试（验证原版区 ID 在有无本类型时一致）与 §6.2 compat 全流程验收。**精确约束（v6 修正 H11）**：本类型的引入会使"双端 LAN Connect 版本不同（0.6↔0.7）且双端装有同一**会实际发送网络消息的第三方非 gameplay mod**"的组合中，该第三方消息 ID 位移——0.7→0.6 方向表现为第三方消息被对端当未知 ID 丢弃+警告；0.6→0.7 方向若恰好落入本类型 ID 则被我们的 magic 校验拒绝（`lan_native_frame_invalid` 结构化断开，不崩溃），否则落入其他已知类型（由该类型自行消化）。这是游戏 mod 消息机制的**既有生态属性**（任何注册 `INetMessage` 的 mod，包括 BaseLib，都造成同类位移），本设计不新增可消除手段。**验收口径（v9 修正 H2 残留）**：在**无 native-ID 碰撞**的 compat 组合中，LAN Connect 自身流程（加入/开局/局内/SL）必须正常；碰撞组合（§6.2 8b-2）的预期结果即结构化断开；第三方 dummy 消息的三种可能表现——被对端未知 ID 丢弃+警告、落入本类型被 magic 校验结构化拒绝、落入其他第三方已知类型（由该类型的 Deserialize/handler 自行消化，其异常属于该 mod 的既有暴露面，handler 层异常由总线捕获记录 `NetMessageBus.cs:95-104`）——均为**文档化的可接受结果**，本设计不为其兜底、也不承诺"全流程正常"。缓解策略是发布后尽快收敛全员至 0.7+（native 房间已由版本门禁强制）。
3. 不修改 lobby-service 的房间模型 / 工单 / 中继架构。
4. 不试图与 RitsuLib 的 sidecar 生态互通（我们不再是它的消费者）。
5. 不解决"混合 Ritsu 存在"房间。**注意（v2 修正）**：本 mod 与 RitsuLib 的 manifest 均为 `affects_gameplay=false`，原版握手明确**允许**非 gameplay mod 集合不同（`HandshakeManager.cs:135` 的允许路径，E2E 日志中已有该警告实证），因此混合存在**必须**由我们自己的服务端 presence 同质化预检拒绝（v0.6 已实现，保留），不能依赖游戏握手的 ModMismatch。

## 3. 协议设计：`native_bus_v1`

### 3.1 载体模型与外层帧格式

定义唯一的自定义消息类型（v5 补全合同）：

```csharp
public sealed class LanConnectNativeBusMessage : INetMessage
{
    // ShouldBroadcast = false：目标地址由我们显式控制（单发或逐个发送）。
    // Mode = NetTransferMode.Reliable（ch0，与原版大厅消息同通道，保证成对有序）。
    // ShouldBuffer = true（跟随 NetMessageBus.SetBufferMessages 语义，释放规则见 §3.3）。
    // LogLevel = VeryDebug。

    // Deserialize 契约（H8）：非抛出。读取失败（magic/ver 不符、frameLen 越界、超上限）
    // 一律记入 InvalidReason 并返回，剩余字节交由 PacketReader 跳到包尾；
    // 由配对屏障/第 2 层校验读取 InvalidReason 后统一转为 lan_native_frame_invalid
    // 结构化失败并断开——坏帧绝不炸穿原版 TryDeserializeMessage 接收循环。
    public string? InvalidReason { get; private set; }
    public byte[]? Frame { get; private set; }   // 仅 [frameLen] 界定内的字节，尾随内容忽略
    public uint LocalTypeId { get; private set; }
    public void Serialize(PacketWriter writer);  // 写入下方外层帧
    public void Deserialize(PacketReader reader);
}
```

**硬性规则：类名一经发布永不更改。** `NetTypeCache` 经 `ContentSorter` 多级排序分配 ID（排序键含 `Type.FullName`），改名/移动命名空间 = 换 ID = 与所有已发布版本断连。

外层帧格式（v5 精确化。**端序拆分（H7）**：原版 9 字节线头 `[typeId:1][senderId:8]` 中的 `senderId` 由 `PacketWriter.WriteULong` 写出，为**小端**——手工拼装线头必须与原版一致用小端；我们的外层与内层新字段统一**大端**）：

```text
LanConnectNativeBusMessage payload:
  [magic:2]               // 0x4C 0x42（"LB"），大端无关
  [ver:1]                 // = 1
  [localTypeId:4]         // 本端 TypeToId<LanConnectNativeBusMessage>()，大端
  [frameLen:4]            // frame 精确字节数，大端
  [frame:frameLen]        // 现有 LanConnectSidecarFrame 编码（自身头部 26 字节，内部自定端序沿用）
  [尾随字节：忽略]         // 只消费到 frameLen；尾随内容（RitsuLib native trailer，0.5.12 实测布局 36 字节）忽略
```

**精确边界（M1，常量固化进代码与测试）**：

- 发送侧：`frameLen ≤ 65000`（编码前检查拒绝；实际最大载荷 begin-run roster 量级 1–2 KiB）；
- 接收侧：`frameLen ≤ 收到的 packet 长度 − 20`（9 字节原版头 + 11 字节外层头），且 packet 总长 ≤ 66000（为 36 字节 trailer 预留余量）；
- 边界 golden vector：`frameLen = 65000`（过）、`65001`（拒）、packet 带/不带 36 字节尾随（均过且忽略）、截断包（拒、不抛）。

消息种类枚举（`LanConnectSidecarMessageKind` 1–10）不变：InitialGameInfo、LobbyJoinRequest/Response、LoadJoinRequest/Response、RejoinRequest/Response、ConnectionFailed、PlayerJoined、LobbyBeginRun。

### 3.2 发送侧：preparation seam + prefix/postfix 上下文传递 + 专用发送出口（v4 重写）

发送链三级，**全部为非泛型补丁目标**（规避 Mono gshared；泛型 `SendMessage<T>` / `SerializeMessage<T>` 明确不作补丁目标）：

**第一级：容器生产 seam（保留 v0.6 的 10 个具体消息 `Serialize` prefix，职责收缩）。**
矩阵消息序列化时，prefix 调用现有 runtime 生产容器（offer/roster/拒绝码/nonce），挂到 **writer 键控 pending**（复用 v0.6 `PacketWriter → pending` 机制）。此级不读取、不改写原版序列化字节。

**第二级：transport prefix（先于第三方捕获原始关联；广播批次语义，v5 修正 C3）。**
宿主广播的实际路径是 `NetHostGameService.SendMessage<T>` **一次序列化后复用同一个 `bytes` 循环逐 peer 调 `SendMessageToClient`**（`NetHostGameService.cs:114-129`，不经 `ENetHost.SendMessageToAll`）。因此 pending 挂在 writer 键控表上**不随首个 peer 注销**：

- prefix（Harmony 优先级先于 RitsuLib 发送 prefix）按 buffer 引用从 writer 表解析 pending（此刻 buffer 仍是 serializer 原数组；RitsuLib 对**本次调用**生成的替换数组不影响下一次调用仍见到原数组），连同接收方移入线程静态 currentSend 槽；
- **消费去重**：postfix 以 `(pending, recipient)` 消费集去重——同一 pending 可服务广播循环中的每个 peer 各一次，重复消费同一 peer 视为协议错误；
- **生命周期（v6 修正 C3 复发）**：`PacketWriter` 长期持有同一 buffer 且 `Reset()` 只归零位置——若 pending 仅按 buffer 引用存留到"下一次矩阵序列化"，期间的**非矩阵**消息（同一 writer、同一 buffer）会误命中残留 pending。因此恢复 v0.6 的 `PacketWriter.Reset` prefix 补丁（职责单一：清除该 writer 的 pending），叠加"下一次矩阵序列化自然覆盖"；广播批次内（无 Reset 介入）同一 pending 继续服务全部 peer。

**第三级：transport postfix + 专用 native 发送出口（同方法 postfix + finalizer，v5 补 H9）。**

- 入口第一行：`if (nativeSendReentry) return;`——**结构性递归免疫**。
- 从 currentSend 槽取容器 → 按接收方 stamp 逐 peer 序号（宿主侧每 peer 独立 flow 计数；客户端对 host 单计数）→ 编码外层帧（§3.1）→ 调 `LanConnectNativeBusSender.Send(...)`：
  1. 置线程静态 `nativeSendReentry = true`；
  2. 手工拼装原版线头：`[typeId:1][senderId:8 小端 = NetService.NetId][payload = 外层帧]`（即 `NetMessageBus.SerializeMessage` 线格式，不经过泛型 serializer）；
  3. 以该 transport 实例直接调用 `SendMessageToHost` / `SendMessageToClient`（Reliable / ch0）；
  4. `finally` 清除重入标志。
- **异常与空 peer 语义（H9）**：两处 transport 补丁均带 **finalizer**（异常路径清理 currentSend 槽与消费集，避免断连竞态残留）；host 侧目标 peer 不存在（`SendMessageToClient` 仅记日志返回）、client 侧 `_peer == null` 时，postfix 将该次发送视为该 peer 的结构化失败并断开——**"未抛异常"不等于"发送成功"**。
- 顺序：postfix 在原版字节入队后同步执行；广播循环逐 peer 产生 `[原版][扩展]`，同一 peer 的 ch0 FIFO 保证顺序。
- 失败语义：第一级生产失败 ⇒ 沿用现有结构化失败路径；第三级失败 ⇒ 该 peer 结构化失败 + 断开，不静默丢帧。
- v0.6"替换/抑制原版消息"的场景（拒绝路径）改为：原版照发，扩展帧携带拒绝码，接收端以后到的扩展帧为准。
- Android：三级均沿用 v0.6 已验证的具体非泛型形态，不新增泛型包装器。

### 3.3 接收侧：传输上下文捕获 + 总线配对屏障（v4 补认证上下文与缓冲契约）

**上下文捕获（H4）**：patch `NetHostGameService.OnPacketReceived` / `NetClientGameService.OnPacketReceived` 的 **prefix**（非泛型，v0.6 已有同点位 receive 补丁），把**传输层** `(senderId, mode, channel)` 存入当次分发的线程上下文。配对层的对端身份**只取传输层 senderId**（ENet peer→netId 的映射由 `ENetHost.cs:187-221` 建立，客户端侧恒为 host=1），**不信任** `NetMessageBus` 的 `overrideSenderId`（`NetMessageBus.cs:72` 允许包内字段覆盖，见 `NetClientGameService.cs:122`）。扩展帧仅接受 `channel == 0`——**不能用 `mode` 判定**：ENet 入站 `mode` 恒为 `None`（§1.3，v9 修正 C2）；channel==0 即 ENet 可靠有序通道。

**配对屏障（v7 简化重写：纯分发层，零缓冲补丁）**：patch `NetMessageBus.SendMessageToAllHandlers` prefix，**不引入任何自有队列、不 patch `SetBufferMessages`**——矩阵消息与扩展帧都是原版总线消息（`ShouldBuffer=true`），缓冲期一同进入原版 `_bufferedMessages`，`SetBufferMessages(false)` 由原版按到达序统一释放（`NetMessageBus.cs:107-125`）。**顺序与配对语义（v9 修正 H1，按 sender 分片）**：发送端在 ch0 上**背靠背**发出 `[矩阵消息][扩展帧]`（§3.2）。宿主把**所有 peer** 的包送入同一 `NetMessageBus`（`NetHostGameService.cs:142`），因此"相邻"只在**同一传输层 sender 内**成立：屏障按 `(传输层 senderId, channel)` 分片 hold——该 sender 的下一帧必须是其扩展帧；**其他 peer 的消息允许在其间先行执行**（原版本身就不提供跨 peer 顺序保证，此举与原版语义一致，非违规）；该 sender 的扩展帧缺失 ⇒ 2000ms 超时守卫兜底为 `lan_extension_missing` 断开该 peer（此时连接已终止，期间其他消息先行执行不构成语义问题）：

- 分发矩阵消息 ⇒ 暂存（hold，不下发 handler），等待同 sender 的下一帧；
- 下一帧为 `LanConnectNativeBusMessage` ⇒ 校验配对（kind + nonce + 逐 peer 序号 + §3.1 边界与 `InvalidReason`），**先应用扩展语义**（roster/拒绝码等恢复 projection），**再**经 bypass 标志直接分发暂存的原版消息到 handler（顺序与现网 `LanConnectTailMessageRuntime` 一致：原版 handler 必须看到已恢复的 projection）；
- **传输上下文旁挂表（v10 修正 H1：补齐缓冲释放路径的配对合同）**：原版 `_bufferedMessages` 只保存 `(INetMessage, senderId)` 且释放时该 senderId 可能已被包内 `overrideSenderId` 覆盖（`NetMessageBus.cs:35,80,120`），无 channel——屏障在消息**首次进入分发层**时（含缓冲期；此时仍处于 `OnPacketReceived` 同步调用栈内，线程静态上下文可用）为矩阵与扩展消息在旁挂表记录 `message 实例 → (transportSenderId, channel)`——**键必须为引用身份**（`ConditionalWeakTable` 或 `ReferenceEqualityComparer`：矩阵消息含值类型 struct，装箱后默认相等性会把两个 peer 的等值消息合并成同一键，v11 修正 M-CTX）；原版缓冲释放再次进入分发层时查表恢复传输上下文并**消费即删**（表项另在会话断开时清理）。配对分片与 ch0 校验一律使用恢复后的传输上下文，不使用包内 senderId。此表只挂元数据、不持有消息，非自有队列；
- **超时守卫**：暂存后 2000ms 未配对 ⇒ `lan_extension_missing` 结构化失败并断开（缓冲期间消息停留在原版队列，不进入 hold，计时自然不启动，无需感知缓冲状态；缓冲释放后正常走 hold/配对）；
- **与 RitsuLib 的所有权（v7 删除 N1 冲突源）**：我们不再触碰 `SetBufferMessages`（RitsuLib 的 sync 补丁是该方法的既有所有者之一），对 `SendMessageToAllHandlers` 仅做"hold 一帧"的瞬时屏障，不持久持有任何队列——两个 mod 的补丁在该方法上无状态竞争；
- Harmony 优先级显式声明：低于 RitsuLib 接收补丁（使其先处理**其自身 magic 的 envelope**；native trailer 不在其处理范围，由 §3.1 长度边界忽略——两者是不同机制，见 §4.2.1）。叠加顺序写入启动诊断。

### 3.4 ID 一致性：服务端 ticket 门禁为主，客户端三层为辅（v4 重写）

**前提**：本 mod 与 RitsuLib 均为 `affects_gameplay=false`，原版握手允许非 gameplay mod 集合不同（§2.2.5）——双端非 gameplay mod 集合不同时 ID 可能漂移，且漂移后的首字节可能落在另一**已知**消息 ID 上（如 BaseLib 的 128/129），被原版直接实例化进错误 handler（`NetMessageBus.cs:58-75`），既进不了未知 ID 分支、也到不了我们 handler 内的检查。因此客户端侧任何检查都不能作为主门禁。

**第 1 层（主门禁，服务端强制）：registry fingerprint 挂在 join ticket 签发路径。**

- 算法固定为 `sha256:v1:<64 位小写 hex>`：对 `MessageTypes` 全表按 id 升序，每项编码 `[id:4 大端][modIdLen:1][modId UTF-8][flags:1（bit0=affectsGameplay）][asmLen:1][assemblyName UTF-8][nameLen:2 大端][typeFullName UTF-8]` 后串联取 SHA-256——身份字段覆盖 `ContentSorter` 的全部排序键来源，杜绝"同 FullName 不同 assembly"的同指纹不同表（v6 修正 H12）；跨平台确定性，C#/TypeScript 双端实现共用测试向量。
- 房间创建时房主 fingerprint 冻结进 ProtocolSelection（服务端持久化）。
- **`POST /rooms/:id/join`（ticket 签发）强制比对**：join 请求必须携带 `registryFingerprint`，与房间冻结值不一致 ⇒ 拒签 ticket（`lan_registry_fingerprint_mismatch`，附双方摘要前 8 字节）。这是进入运输层前的**唯一必经点**——客户端跳过 preflight、mod sync 关闭、旧客户端等一切路径都无法绕过（拿不到 ticket 就无法连接）。客户端 `/mod-preflight` 上的同项校验仅作快速失败 UX，不承担门禁职责，无需逐一封堵其旁路。

**第 2 层（运行时完整性）**：每条扩展帧携带 `localTypeId`，接收端与本地 `TypeToId<T>()` 比对，不一致 ⇒ `lan_type_id_mismatch` 结构化失败。

**第 3 层（未知 ID 捕获）**：复用 `NetMessageBus.TryDeserializeMessage` 补丁位，经 Harmony 参数注入 `__0`（`packetBytes`）；在两处前置转结构化失败：① 包长 <9 且首字节为本类型 ID（原版会在读取 senderId 时越界抛出，先拦截 ⇒ `lan_native_frame_invalid`，v6 修正 H8）；② 在"未知 typeId 丢弃"分支从 **offset 9**（1 字节 typeId + 8 字节 senderId 之后）检查我方 `[magic][ver]` 特征——**且仅当外层帧完整解析通过**（magic/ver/长度边界/`localTypeId` 在 byte 范围内全部合法）才升级为 `lan_type_id_mismatch`；仅前缀相似但解析失败的一律维持原版"警告一次后丢弃"（v11 修正 M-MAGIC：第三方消息 payload 恰以 `4C 42 01` 开头时不得被误伤断开）。**明确边界**：已知 ID 碰撞不在本层能力范围内，由第 1 层在门前终结。

**第 4 层（服务端 mod 门禁）**：加入门禁比对 gameplay-relevant mod 列表（现有）+ Ritsu presence 同质化（§5）。

**启动自检**：启动时检查 `MessageTypes.Count ≤ 256`（消息表达 257 项时原版 `WriteByte((byte)id)` 会取模碰撞，`NetMessageBus.cs:46-47`）且全表 id→byte 映射唯一、本类型 ID 不与 BaseLib 消息 ID 冲突；异常 ⇒ 拒绝启用 native 载体并输出诊断（不崩溃、明确报错）。

### 3.5 wire cache 与能力摘要

- `wcv1` wire-cache 签名握手机制保留，改乘新载体（首条扩展帧携带）。
- 能力摘要（`LanConnectCapabilityDigest`）编码升级：carrier 字段新增 `NativeBusV1 = 3`；旧载体在内部枚举中改用中性成员名 `LegacyTailV1` / `LegacySidecarV1`（v8 与 §4.1 扫描规则统一），旧值仅以线上字符串字面量 `"standalone_tail_v1"` / `"ritsulib_sidecar_v1"` 存在于 DTO 映射与识别拒绝分支（`lan_legacy_carrier_unsupported`）。
- **registry fingerprint 与 `ritsuLibVersion` 均不参与 digest 哈希（M5）**：fingerprint 作为 ProtocolSelection 的独立字段持久化（服务端可比对）；`ritsuLibVersion` 为房间 DTO/preflight 上的可选信息性字段（缺失=unknown，仅诊断展示与预检 UX），二者若参与哈希会造成不必要的房间不兼容。
- 预检对旧载体房间返回明确错误："该房间使用旧版协议载体，请升级 LAN Connect"。

### 3.6 相关小修（随本版本）

- `LanConnectJoinRetryPolicy.IsRetryableReason` 加入 `LobbyJoinTimeout`：中继候选失败后继续尝试直连候选（本次事故中 3 个直连候选从未被尝试）。
- LobbyJoinTimeout 的用户文案补充指引（检查双方 mod 版本一致性）。

## 4. 与 RitsuLib 的关系

### 4.1 迁移矩阵（v3 补齐）

实施前以 `rg -l "Sidecar|sidecar|RitsuLib|Carrier|ProtocolSelection"` 对 `sts2-lan-connect/` 全量核对，逐文件归入四类（**删 / 改 / 留 / 诊断保留**）。已核对项：

| 文件 / 项 | 处置 |
|---|---|
| `Scripts/Protocol/Capabilities/LanConnectRitsuLibSidecarCarrier.cs` | **删**（反射绑定） |
| `Scripts/Protocol/Capabilities/LanConnectSidecarPairingCache.cs` | **删**（跨传输配对由 §3.3 屏障替代） |
| `Scripts/Lobby/LanConnectHostSidecarActivationGate.cs` | **删** |
| `Scripts/Lobby/LanConnectRitsuLibLobbyCompatibility.cs` | **删**（已是空壳日志） |
| `LanConnectLobbyJoinFlow.RitsuSidecarENetClientConnectionInitializer` | **删**，统一回归 `ENetClientConnectionInitializer` |
| `LanConnectTailMessageRuntime` 的 sidecar 流绑定 / 激活 / pending 延迟投递 | **删**（保留 binding 骨架、`PrepareOutgoing` 容器生产与 deferred 注入复用，见 §3.2 第一级） |
| `LanConnectLobbyRuntime.cs:2392-2408`（`player_control_binding` sidecar 准备分支）、`:3705-3727`（host sidecar flow 激活/清理） | **删**（控制通道其余职责不变） |
| `LanConnectExternalCapabilityCollector.cs:21-22`（调用 sidecar carrier） | **改**：保留 Ritsu presence/version 探测（诊断用），剥离 carrier 调用 |
| `LanConnectProtocolSelection.cs:68-81`（Ritsu present ⇒ carrier 必须 sidecar 且 sidecar available） | **改**：carrier 一律 `NativeBusV1`；tail_v1 **完全忽略 `sidecarAvailable`**，只保留 Ritsu presence 同质化（v11 修正 H1） |
| `LanConnectProtocolOffer.Validate` / `LanConnectCapabilitiesCodec` 中的 sidecar 可用性校验 | **删**：tail_v1 offer 不再携带/校验 sidecar 可用性（v11） |
| `LanConnectTailMessageRuntime.cs:1217-1238`（`ValidatePeerOffer`/`EnsureSidecarReady`/`TryEnsureRegistered` 一族） | **删**：native 载体不经过 sidecar 就绪检查（v11） |
| `LanConnectTailMessagePatches.cs:709-739`（sidecar 分支） | **删**（v11） |
| 服务端 `assertJoinerCompatible` / `protocol-capabilities.ts:63-67,112-114`（按 sidecar 可用性选载体/拒绝） | **改**：tail_v1 一律 native，忽略 sidecar 可用性（v11） |
| `LanConnectProtocolProfile.cs:9-14, 59-74` | **改**：枚举/矩阵加 `NativeBusV1` |
| `LanConnectCreateRoomIntent.cs:31-35`（拒绝 Ritsu present 但 sidecar unavailable） | **删**该门禁分支 |
| `LanConnectCapabilitiesCodec.cs:203` 一带（capabilities 编码中的旧 carrier 值） | **改**：新增 `native_bus_v1` 编码并保留旧值识别 |
| `LanConnectMultiplayerSaveCompatibility.cs:138` 一带（存档恢复路径的 sidecar/carrier 分支） | **改**：按新载体语义重写，存档键与既有房间绑定不变 |
| `LanConnectLobbyManagedJoinFlow.cs:83` 一带（join 追踪中的 sidecar 引用） | **改**：移除 sidecar 状态，保留 join 生命周期追踪 |
| `LanConnectLobbyOverlay.cs:619-620, 6302-6336`（"RitsuLib 状态必须一致 / sidecar 可用"文案与逻辑） | **改**：新载体语义 |
| tail plan：android_concrete_serialize 10 个 prefix | **改**：保留点位，职责收缩为 §3.2 第一级容器生产（不改写原版字节） |
| tail plan：transport 前缀 4 个（host/client send） | **改**：同点位改为 §3.2 第二级 postfix 补发 |
| tail plan：`TryDeserializeMessage`（旧 postfix 配对用途） | **改**：拆分为两职责——**prefix** 处理已知 native ID 且长度 <9 的包（§3.4 第 3 层①，转 `lan_native_frame_invalid`）+ **postfix** 仅处理原版返回 false 的未知 ID 分支（第 3 层②，`__0` 注入 + offset-9 检查）；并保留异常清理 finalizer（v12 修正 M1：消除 prefix/postfix 表述矛盾） |
| **桌面端泛型 patch plan 条目**（`LanConnectTailMessagePatchPlan.cs:175-210` 一带的 `SerializeMessage<T>` / `SendMessage<T>` 目标） | **删**：由 §3.2 三级非泛型链整体替代（Android 具体前缀与桌面路径统一，不再保留桌面泛型分支） |
| 新增：`ENetClient/ENetHost` send 的 prefix（§3.2 第二级） | **新增**：非泛型、单点、带显式优先级。**明确禁止**新增 `NetMessageBus.SetBufferMessages` 补丁（§3.3，v8 修正 C1：v6 矩阵行的残留与 §3.3 矛盾）；补丁计划与 §6.1 测试断言补丁清单中无该目标 |
| tail plan：receive 前缀 2 个（OnPacketReceived） | **改**：保留点位，用途换为 §3.3 传输上下文捕获 |
| tail plan：writer_reset patch | **留**（v6 恢复）：职责改为清除该 writer 的 pending（§3.2 生命周期） |
| 位宽 transpiler 6 个、capacity / gameplay / save 守卫 / join_screen / RMP 外部检测补丁 | **留**，语义不变 |
| 诊断/启动日志中 sidecar 就绪行 | **改**：换为 native_bus 就绪行（本地 typeId、registry fingerprint、补丁叠加顺序） |
| 相关 `*.test.ts` / 测试 C# 工程 | **改**：随合同更新，删除 sidecar 专用用例 |
| `LanConnectModPreflightCoordinator.cs:144-153, 214-229` | **改**：preflight 请求/响应 schema 增补 fingerprint 字段与 native 错误码透传；relaxed 路径不再承载门禁语义（v7 补 N2） |
| `LanConnectProtocolFailureMapper.cs:60-63` 一带 | **改**：known-code 映射补 `lan_registry_fingerprint_required` / `lan_registry_fingerprint_mismatch` / `lan_native_frame_invalid` / `lan_client_version_too_old`（v7 补 N2） |
| `LanConnectMultiplayerSaveRoomBinding.cs:47-63, 206-214` 一带 | **改**：存档恢复重建 selection 时重算 fingerprint（本机 registry 即时计算，不持久化旧值）（v7 补 N2） |
| `LanConnectLobbyModels.cs:273-296, 400-469` | **改**：DTO 增补 `registryFingerprint` / `ritsuLibVersion` 与新错误码字段（v7 补 N2） |
| `LanConnectGameplayPatches.cs:29` 一带（sidecar 检测的兼容组开关） | **改**：Ritsu presence 探测保留为诊断输入，删除 sidecar 分支开关（v8 补 M3） |
| `LanConnectLobbyManagedJoinFlow.cs:83, 151, 425` 一带（join 追踪/初始化器的 sidecar 引用） | **改**：删除 sidecar 状态与初始化器分支，保留生命周期追踪骨架（v8 补 M3） |
| `LanConnectLobbyRuntime.cs:524, 960, 1122, 1422, 1480, 3523` 一带（注册、tick、attach/release、session 字段中的 sidecar 引用） | **改**：逐项删除 sidecar 注册/tick/字段；Ritsu presence 诊断与控制通道其余职责不变（v8 补 M3） |

**纯 direct-IP 范围（v12 补 L1）**：纯 IP 直连流程（`LanConnectDirectJoinFlow`）**继续 compat-only 并维持 v0.6 既有非目标**——本地检测到 Ritsu 时仍按 `ritsulib_not_allowed_in_compat_mode` 拒绝；本 spec 与 E2E 中的"直连"一律指**大厅 direct 候选**（有 ticket/fingerprint 门禁），不含纯 IP 直连，不因目标 3（Ritsu 共存）改变此边界。

**实施门禁（零引用检查）**：完成后对固定符号表 `RitsuLibSidecar | SidecarPairing | HostSidecarActivationGate | RitsuSidecarENetClientConnectionInitializer | SubmitSidecarBeforeVanilla | LanConnectStandaloneTailCarrier | LanConnectRitsuLibLobbyCompatibility | SerializeMessage< | LanConnectTailPlanOverride | ResolveDesktopPatchPlan | ResolveGenericSerializeMessageMethod | desktop_generic_v1` 在 `sts2-lan-connect/` 源码执行 `rg`，除迁移说明与测试 fixture 外必须零命中；内部枚举成员改用中性命名（如 `LegacySidecarV1`），旧值仅以**线上字符串字面量** `"ritsulib_sidecar_v1"` / `"standalone_tail_v1"` 形式存在于 DTO 映射与"识别并拒绝"分支；CI 扫描只匹配 C# 标识符，字符串字面量不触发（v7 修正 N3，消除保留命名与零引用门禁的自相矛盾），作为 CI 检查固化（固定符号表，不做允许列表机制）。

### 4.2 残余交互面（预期内、需 E2E 验证）

1. **RitsuLib native trailer（v3 措辞修正）**：0.5.12 源码事实——trailer 在**发送侧**向非 sidecar magic 的 app packet 追加 36 字节标记（0.5.12 布局 12+2+2+4+4+12）（`RitsuLibSidecarNativeTrailerSendPatch.cs:28-30`）；接收侧只**观察**该标记作 reachability evidence（`RitsuLibSidecarNativeTrailerEvidence.cs`、`RitsuLibSidecarNetReceivePatch.cs:45`），**不剥离**。注意区分两件事：其 receive pipeline 仅对其**自身 magic envelope** suppress vanilla（`RitsuLibSidecarReceivePipeline.cs:39-47`）；native trailer 是另一机制，会留在包尾由我们 §3.1 的长度边界忽略。该行为同时确认 ch0 消息不会被吞。最新版行为以 §6.2 第 3 项实施门禁核验为准。
2. Harmony 补丁叠加顺序（`SendMessageToAllHandlers`、两个 transport send 点、两个 OnPacketReceived 点）：显式声明优先级并在启动诊断输出叠加顺序。
3. **游戏更新敏感面（v3 扩面）**：不止三处 API——实际依赖面为：① `INetMessage` 接口与 `MessageTypes` 反射发现；② `NetMessageBus`（`SerializeMessage` 线格式、`TryDeserializeMessage` 未知 ID 分支、`SendMessageToAllHandlers`、`SetBufferMessages`）；③ `ENetClient.SendMessageToHost` / `ENetHost.SendMessageToClient(ToAll)` 签名与逐 peer 广播行为；④ 两个 `OnPacketReceived` 签名；⑤ `PacketReader/PacketWriter` 基础 API；⑥ 矩阵 10 类消息的具体 `Serialize/Deserialize` 签名（位宽 transpiler 已依赖）。上述全部纳入 §6.1 双版本 ABI 对比测试与 §6.2 第 5 项全流程验收。

## 5. 服务端变更（v4：ticket 门禁链）

服务端协议合同核心文件是 `lobby-service/src/protocol-capabilities.ts` 与 `app.ts`（`server.ts` 仅启动入口）。逐处变更：

| 文件 | 现状 | 变更 |
|---|---|---|
| `protocol-capabilities.ts:5` | `ProtocolCarrier` 仅三种旧值 | 增加 `"native_bus_v1"`，创建房间仅接受 `native_bus_v1`（或 compat 模式 `none`） |
| `protocol-capabilities.ts:63-70` | `selectRoomProtocol` 按 Ritsu presence 选 sidecar/standalone | tail_v1 一律 `native_bus_v1`，不消费 presence |
| `protocol-capabilities.ts:127-140` | digest carrier 编码仅 0/1/2 | 增加 `NativeBusV1 = 3` |
| `protocol-capabilities.ts:149-165` | `parseProtocolOffer` 字段白名单 | 放行 `registryFingerprint`（创建侧必填、进 ProtocolSelection 持久化；与 join 顶层字段语义一致，v6 统一权威字段）与 `ritsuLibVersion`（可选、不进 digest 哈希） |
| `app.ts:635-659, 742-753, 835, 2837-2845` | 创建/加入/preflight 路径 | 接入下述门禁链；错误码经 `protocol-errors.ts` |
| `store.ts` join 路径（`assertJoinCompatible`，:576-648） | 无 minimumClientVersion 比较 | **新增门禁**（见门禁链第 4 步） |
| `client-version.ts:33-56` | 仅 generation 分类 + 相等性 | 新增 semver/prerelease 比较函数（`>=` 语义，支持预发布标签） |
| `/probe` 投影（`app.ts:500` 一带） | 仍广告旧 tail 最低版本 | 随 native `minimumClientVersion` 一并更新（v6 补 Q8） |
| mod-preflight 路径（`store.ts:657`、`app.ts:835` 一带） | 不携带 protocol offer、不调用 join compatibility | **只读投影/快速失败**（v9 修正 H3 残留）：不签发 ticket、不强制 fingerprint/minimumClientVersion；快速失败一律为非 2xx 的既有 error envelope（`{code,message,details}`，`app.ts:1250`），不新增成功 DTO 字段（v10 修正 M1：消除 200-承载-失败的字段二义） |

**统一加入门禁链（仅 `tail_v1` 房间；顺序固定，逐级明确错误码；第 3/4 步在 ticket 签发路径强制执行，且置于现有 wire-cache 预检查（`app.ts:765-767`）之前——同一请求多重失败时按本链顺序返回首个错误；compat 房间不走本链）：**

1. Ritsu presence 同质化（既有）⇒ `ritsulib_presence_mismatch`；
2. carrier 合法性（新房间必须 `native_bus_v1`；旧载体房间 ⇒ 稳定错误码 `lan_legacy_carrier_unsupported`，文案指引升级）；
3. **registry fingerprint 比对**：`POST /rooms/:id/join`（ticket 签发）必须携带客户端 `registryFingerprint`，与房间冻结值比对，不一致 ⇒ 拒签 ticket `lan_registry_fingerprint_mismatch`（附双方摘要前 8 字节）。`/mod-preflight` 上的同项校验仅为快速失败 UX，非门禁（客户端任何路径拿不到 ticket 即无法连接，无需封堵 preflight 旁路）；
4. `minimumClientVersion`（semver 比较，同在 ticket 签发路径）⇒ `lan_client_version_too_old`（附房主要求的版本号）；
5. 既有 gameplay mod 列表校验（保持最后，复用现有错误码）。

**JSON 契约（M4，可实现粒度）**：`POST /rooms/:id/join` 请求体新增必填字段 `registryFingerprint: string`（格式 `sha256:v1:<64 位小写 hex>`，非法格式与缺失同等处理）；房间 ProtocolSelection 持久化 `registryFingerprint`（创建时由房主 offer 提供，同格式校验）。错误处理：字段缺失/格式非法 ⇒ `lan_registry_fingerprint_required`；格式合法但不一致 ⇒ `lan_registry_fingerprint_mismatch`（details 附双方摘要前 8 字节）；两者均拒签 ticket。错误优先级按门禁链 1→5，首个失败即返回。`minimumClientVersion` 取值 = 本发布版本号（首个为 `0.7.0-alpha.1`）。旧 0.6 客户端的 join 请求不含该字段 ⇒ 命中 `lan_registry_fingerprint_required`，错误文案指引升级（与 §6.2 第 6 行验收对应）。

**字段级合同（v8 补 N2 残留，双端镜像的精确定义）**：
- `POST /rooms` 创建请求（tail_v1）：`registryFingerprint: string` **必填**，缺失或格式非法 ⇒ `lan_registry_fingerprint_required`，**在房间分配之前拒绝**（不得沿用旧 optional fallback，杜绝"无冻结指纹房间"流入后续 join 门禁，v11 修正 M-FP）；
- `POST /rooms/:id/join` 请求体新增：`registryFingerprint: string`（必填，`sha256:v1:<64 hex 小写>`）；
- `POST /rooms/:id/mod-preflight` 请求体新增：`registryFingerprint: string`（可选，UX 用）；快速失败一律返回**非 2xx 的既有 error envelope**（`{code,message,details}`）；200 响应永不承载协议失败字段（v10 修正 M1，删除二义表述）；
- 错误响应 details：`expectedFingerprintPrefix` / `receivedFingerprintPrefix`（各 8 字符，仅 `mismatch` 时返回）；
- 存档绑定（`LanConnectMultiplayerSaveRoomBinding`）：selection 重建时**本机即时重算** fingerprint 并随下一次房间发布上传，持久化文件不存旧值；
- 客户端 `LanConnectProtocolFailureMapper` known-code 新增：`lan_registry_fingerprint_required`、`lan_registry_fingerprint_mismatch`、`lan_legacy_carrier_unsupported`、`lan_native_frame_invalid`、`lan_client_version_too_old`。

**minimumClientVersion 门禁落点**：`validateJoinCompatibility`（store.ts）调用 `client-version.ts` 新比较函数。契约测试覆盖：创建侧缺 fingerprint（拒）/ 合法（过），新客户端→新房间（过）、旧 0.6 客户端→新房间（拒 + 版本号）、新客户端→旧 0.6 房间（拒 + 升级提示）、fingerprint 缺失与格式非法（`lan_registry_fingerprint_required`，同一码两种情形）、一致（过）、不一致（`lan_registry_fingerprint_mismatch`）——**全文档唯一错误合同**：两种错误码；权威字段为 join 请求体顶层 `registryFingerprint`（创建侧 offer 中的同名字段持久化进 ProtocolSelection，两处若同时出现必须相等，否则按缺失处理）。

客户端镜像同步：`LanConnectLobbyModels.cs`（DTO：carrier、registryFingerprint、ritsuLibVersion）、`LanConnectCapabilityDigest.cs`（编码）、offer/selection 构造（本地 fingerprint 计算）、对应双侧测试。

## 6. 测试与发布门禁

### 6.0 阻断边界（维护者裁定，v4）

区分两类事项，避免评审与实施混同：

- **spec 阻断项**：设计层面的正确性缺口（发送出口/递归、门禁路径、配对身份、缓冲语义、合同完整性等）——必须在 spec 内闭环，本轮已全部处置。
- **实施期验证项（采纳但不阻断 spec）**：只能用真实构建与运行证明的证据，包括 v0.107.1 的 ABI diff 与全流程 E2E、评审时点最新版 RitsuLib 的 trailer 源码核对、真实缓冲开关流程。它们保留为**发布阻断**（§6.2），不作为 spec 定稿的前置条件；spec 中对其只承诺验证方式与通过标准。

同时约束：本设计的防护机制以"最简机制封住已论证的失败路径"为限（重入标志、长度边界、单一服务端门禁点），**不叠加**未对应真实失败模式的额外护栏（多层校验链、允许列表机制、限额队列等）；评审如需增加防护，须先指出其针对的具体失败路径。

### 6.1 单元 / 契约测试

- 帧编解码 golden vector（复用 sidecar 帧 vector + 新外层 magic/ver/localTypeId/frameLen，**逐字节级**：含原版 9 字节线头 `senderId` 小端断言与外层字段大端断言；附 §3.1 边界向量：frameLen 65000/65001、±36 字节尾随、截断包、<9 字节已知 ID 包）。
- **trailer 留尾容忍**：payload 后追加 0/1/30/随机字节，反序列化必须成功且忽略尾随内容。
- **生产链（v5 批次语义）**：第一级 prefix 产容器 → 第二级 prefix 于 buffer 被第三方替换**前**建立关联（构造 RitsuLib 式 buffer 替换用例：新数组+新长度，关联必须存活）→ **同一 pending 服务广播循环全部 peer（C3 核心用例：一次序列化 + N 次 SendMessageToClient，N 个 peer 各得一条扩展帧）**、`(pending, peer)` 消费集去重、下次序列化覆盖旧 pending → 第三级 postfix 逐 peer 序号 stamp、`nativeSendReentry` 重入免疫（扩展帧自发送用例直接返回）；finalizer 清理（发送异常后无残留）；host 无 peer / client 断连 ⇒ 该 peer 结构化失败；补丁清单断言（无新增泛型 wrapper 目标；**无 `SetBufferMessages` 补丁**）；宿主"接收后再广播"路径（`NetHostGameService.cs:186-194`，逐 peer 重新序列化）单独覆盖。
- 配对屏障：正常配对、乱序（防御性）、超时 2000ms 触发 `lan_extension_missing`；**`SetBufferMessages` 开/关两态**：缓冲期矩阵+扩展同入原版队列，关闭后按原版到达序成对分发；**跨 peer 交错用例**：peer A 的矩阵消息被 hold 期间，peer B 的普通消息先行执行属预期（与原版跨 peer 无序一致），A 的扩展帧到达后成对释放；RitsuLib sync 补丁共存用例；重入保护；**等值 boxed struct 用例**：两个 peer 发送内容相同的空 struct 矩阵消息，旁挂表不得串键（v11 M-CTX）。
- **传输上下文**：配对采用传输层 sender（构造 `overrideSenderId` 不一致用例，必须仍按传输 sender 配对）；`channel != 0` 的扩展帧 ⇒ 结构化失败；**mode 不参与 ENet 入站判定**（入站恒为 None，§1.3），合法的 `None/ch0` 帧必须通过（v10 修正 M2 测试措辞残留）。
- **sidecar 门禁解除回归（v11，H1 正面验收）**：构造"RitsuLib 存在但 typed sidecar 不可用"（即 0.5.18 事故状态）——tail_v1 房间的创建、加入、运行 native 载体必须全部正常，全链路不得出现 `ritsulib_sidecar_unavailable`。
- **ID 门禁**：fingerprint 算法测试向量（C#/TS 双端一致）；ticket 签发路径比对（一致/不一致/缺失必填字段）；送达路径 localTypeId 不一致 ⇒ `lan_type_id_mismatch`；未知 ID 丢弃路径 offset-9 magic 捕获：完整解析通过 ⇒ `lan_type_id_mismatch`，仅前缀相似 ⇒ 维持原版丢弃+警告（第三方不被误伤）；启动自检（ID 超 byte 范围/与 BaseLib 冲突 ⇒ 拒启用）。
- digest 编码变更的前后兼容（旧 digest 拒绝路径）。
- 服务端门禁链五步的契约测试（各错误码 + 顺序）。
- **双版本 ABI 对比测试（H6/M6）**：脚本接受**显式 fixture 路径与 SHA-256、工具版本**作为参数（fixture 位于仓库外 `~/Desktop/STS2-fixtures`，不可硬编码仓库相对路径），对 0.107.1 与本机 v0.111.0 sts2.dll 反编译比对（含**入站 mode 恒为 None** 的事实断言，v9 修正 C2）：`NetTypeCache` 排序键与顺序稳定性（含 §2.2.2 的原版区 ID 稳定不变量——有无本类型时原版消息 ID 必须一致）、`TryDeserializeMessage` 未知 ID 分支行为、§4.2.3 依赖面全部签名；差异输出为文档并阻断发布（若有差异需先裁决方案）。
- **compat 全流程契约测试（H11）**：compat 房间双端（0.7 ↔ 0.6 模拟）不发 native 帧；未知 ID 容忍路径按预期警告丢弃。
- Android 独立 Godot 进程跑非泛型补丁清单相关用例（沿用现有测试基建约束；legacy 泛型计划用例随计划删除，v6 修正 L1）。

### 6.2 真实平台 E2E 矩阵（发布阻断）

| # | 房间组合 | 路径 | 端 | 必须通过 |
|---|---|---|---|---|
| 1 | 无 RitsuLib | 中继 | macOS 房主 + Android 客户端 | 加入→开始→局内→SL 重开 |
| 2 | 全员 RitsuLib 0.5.13 | 中继 | 同上 | 同上 |
| 3 | 全员 RitsuLib 最新（≥0.5.18，**评审时点官方最新版**，实施前源码核对 trailer 行为） | 中继 | 同上 | 同上（本次事故场景回归；trailer 全链路留尾验证） |
| 4 | 全员 RitsuLib 最新 | 直连 | 同上 | 同上 |
| 4b | 无 RitsuLib | **直连（大厅 direct 候选，非纯 IP 直连）** | 同 1 | 同 1（v11 补 L：v0.111 无 Ritsu 直连组合；纯 IP 直连边界见 §4.1 v12）
| 5 | **无 Ritsu / 最新版 Ritsu** × **中继 / 直连**（复用第 3/4 行组合） | 相应 | v0.107.1 双端 | **全流程**：加入→开始→局内→SL 重开（v10 参数化，消除"任意"的不可复现性） |
| 6 | 新客户端 → 旧 0.6.0 房间；旧 0.6.0 客户端 → 新房间 | — | — | 明确错误码，无崩溃 |
| 7 | RitsuLib 混合存在 | — | — | 我方服务端 presence 预检拒绝（§2.2.5） |
| 8 | 双端非 gameplay mod 集合不同（如一端多装 BaseLib 或测试用 dummy 消息类型），**preflight 生效路径上** | 中继 | 同 1 | preflight 快速拒绝（UX 层）；所有旁路（跳过 preflight / mod sync 关闭 / 伪造响应）由第 9 行的 ticket 门禁兜底 |
| 8b-1 | **compat_4_5_v1 全流程**（0.7 ↔ 0.6 双向，dummy 第三方消息 mod 的 ID **不与**本类型碰撞） | 中继 | 同 1 | LAN Connect 流程正常且无 native 帧发出；dummy 消息被丢弃+警告或落入其他第三方类型（文档化结果） |
| 8b-2 | 同上，但 dummy 消息 ID **恰好与**本类型碰撞（测试构建可控构造） | 中继 | 同 1 | 表现为我们的 `lan_native_frame_invalid` 结构化断开（**按设计**，v8 修正 H2：断开即预期结果，不再同时要求流程正常）；无未捕获崩溃 |
| 9 | **ticket 门禁不可绕过**：fingerprint 缺失/格式非法/不一致三类 join 请求（含跳过 preflight、mod sync 关闭、伪造 preflight 响应等路径） | — | 同 1 | 分别拒签 `lan_registry_fingerprint_required` / `lan_registry_fingerprint_required` / `lan_registry_fingerprint_mismatch`（与 §5 JSON 合同完全一致，v8 修正 H3），无法进入传输层 |
| 10 | 3+ 客户端同时在线（含广播 PlayerJoined/BeginRun），**期间触发 `SetBufferMessages` 开/关** | 中继 | 1 房主 + 3 客户端 | 每 peer 各自成对、序号独立、无跨 peer 串扰；缓冲开关无重复 handler、无死锁、无超时误报 |
| 11 | 并发发送压力 + 注入发送失败 | 中继 | 同 1 | 失败转为结构化错误，无静默丢帧、无死锁 |

通过标准：全程无 `LobbyJoinTimeout`；扩展帧成对到达率 100%；RitsuLib 存在时其自身 sidecar 协商（无论成败）不产生任何影响。

### 6.3 诊断

- 启动诊断新增：`native_bus` 就绪行、本地 typeId、registry fingerprint、补丁叠加顺序、启动自检结果。
- 连接事件新增 phase：`lan_extension_missing` / `lan_type_id_mismatch` / `lan_registry_fingerprint_mismatch` / `lan_native_frame_invalid` / `lan_client_version_too_old`。

## 7. 风险与开放问题

1. **类型排序依赖（Q1 事实补全）**：`ContentSorter` 实际排序键为 affectsGameplay → `type.Name` → null-mod 排序 → mod ID → `Type.FullName` → assembly name（`ContentSorter.cs:77-118`）。因此"只追加新类型"**不能**保证不移动既有 ID（字母序可前插）；ID 一致性的真正保证是 §3.4 的 ticket fingerprint 主门禁，"永不改名/删改命名空间"仅是降低自家漂移的纪律。**确定性前提（v12 补 L2）**：mod 消息类型须正确关联 owning mod，null-mod 同名类型属异常输入（排序退化），由 fingerprint 门禁安全拒绝。启动自检（Count ≤ 256 + byte 唯一）兜底。
2. **RitsuLib trailer 行为随版本变化**：0.5.12 事实为"追加不剥离"；若未来版本改变长度/位置/语义，自描述长度边界 + §6.2 第 3 项（评审时点最新版源码核对）兜底；帧尾 checksum 仍作为备选强化手段，不默认启用。
3. **`SendMessageToAllHandlers` prefix 的缓冲**改变原版消息处理时机（延后一帧/缓冲期延后）：矩阵消息原本就经过我们的 deferred 注入，语义等价；`SetBufferMessages` 交互已显式定义（§3.3）并纳入必测。
4. **开放问题**：fingerprint 计算需覆盖全表（含其他 mod 的消息类型），表大时的计算成本预期可忽略（启动一次）；指纹即 SHA-256；若未来排序键扩展，按 v6 的身份字段规则同步扩展编码并以新版本号（`sha256:v2:`）递进。
5. **发布节奏**：建议 v0.7.0-alpha.1 起切换；0.6.x 维护分支不做载体迁移，仅回移植 §3.6 两个小修。

## 8. 参考

- 本次故障诊断记录与 RitsuLib 0.5.14 变更说明（GitHub Releases）：routed sidecar endpoints、typed 消息优先 confirmed routed endpoint、"host relay paths trusting client-provided sender identities" 修复。
- 反编译事实源：`tem/sts2-decompiled-v0.111.0/`、`STS2-fixtures/0.107.1-data/sts2.dll`（版权代码，仅本地参考，禁止提交）。
- RitsuLib 本地源（0.5.12，将更新至最新版用于交互面核对）：`tem/STS2-RitsuLib/`。
- 第一轮评审恢复件：`tem/codex-review-recovered-v0.7.md`；第二轮完整评审产物：`.omc/artifacts/ask/codex-...14-22-50-075Z.md`。
