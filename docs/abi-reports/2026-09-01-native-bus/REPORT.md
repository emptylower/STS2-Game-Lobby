# native_bus_v1 双版本 ABI 对比报告（2026-09-01）

- 脚本：`scripts/abi-compare-sts2.sh`（显式参数：fixture 路径 + SHA-256 + ilspycmd 版本）
- fixture：`~/Desktop/STS2-fixtures/0.107.1-data/sts2.dll`
  SHA-256 `e7ceb80669bfaf5c8fccabaa126ae2bb283aba514be5b5b55612579cfd285f18`
- 本机：`sts2.dll` v0.111.0（`data_sts2_macos_arm64`，41cef1ea）
- 工具：ilspycmd 9.1.0.7988
- 结论：**依赖面不变量全部通过**；源级差异 11 组，逐项裁决如下，无需 spec 变更。

## 通过的不变量（两版本一致）

1. `NetMessageBus.TryDeserializeMessage(byte[], out INetMessage?, out ulong?)` 签名与未知 ID 非致命丢弃分支；
2. `ENetHost.SendMessageToClient(ulong, byte[], int, NetTransferMode, int)` 与 `ENetClient.SendMessageToHost(byte[], int, NetTransferMode, int)` 非泛型签名；
3. `NetHostGameService` / `NetClientGameService.OnPacketReceived(ulong, byte[], NetTransferMode, int)`；
4. 9 个矩阵消息的 `Serialize(PacketWriter)` / `Deserialize(PacketReader)`；
5. `MessageTypes.TryGetMessageType(int, out Type?)` 反射发现 API；
6. 入站 `mode = NetTransferMode.None`（`ENetConnectionExtension.TryService`，两版相同）。

## 裁决的差异组

| # | 差异 | 裁决 |
|---|------|------|
| 1 | **0.107.1 无 `ContentSorter`**；`NetTypeCache` 直接 `types.Sort(CompareOrdinal(t.Name))`。0.111.0 为六级排序（affectsGameplay → id → null-mod → mod id → FullName → assembly）。 | **可接受**。指纹门禁按"全表内容摘要"比对，同版本内 ID 确定性由该版本自身的排序算法保证；跨游戏版本房间被 strict game-version 检查隔离。0.111.0 独有的"原版区 ID 稳定"不变量在 0.107.1 退化为纯名字序，属游戏生态既有属性，由指纹门禁统一兜底。 |
| 2 | **0.107.1 无 `MessageTypes.Count` 属性**。 | **客户端修正已落实**：启动自检/指纹枚举改为从 id=0 起 `TryGetMessageType` 枚举到首个空洞，不依赖 Count。 |
| 3 | 0.107.1 未知 ID 为 `Log.Error` + 丢弃；0.111.0 为 modded warn-once + 丢弃。 | **可接受**：两版均不抛出、不破坏流；我们第 3 层 offset-9 捕获 postfix 在两版语义一致。 |
| 4 | 矩阵消息（InitialGameInfo/JoinResponse/LoadJoinResponse/PlayerJoined/BeginRun）、NetHostGameService、NetClientGameService、MessageTypes、NetTypeCache 的源级差异。 | **游戏演进的预期差异**（字段与实现细节变化）；native_bus_v1 依赖的全部补丁目标签名经不变量 1-6 验证两版一致。 |

## 发布阻断余项（未在本报告范围内）

- spec §6.2 真机 E2E 矩阵（macOS 房主 + Android AVD 客户端，11 行全部组合）为**发布门禁**，尚未执行。
- 评审时点最新版 RitsuLib 的源码核对（trailer 行为）随 E2E 第 3 行执行。
