# LAN 与大厅续局通道分流设计

**日期：** 2026-07-23  
**状态：** 已批准  
**目标版本：** STS2 LAN Connect 后续修复版本

## 1. 背景

纯 LAN 创建和游戏大厅创建目前使用不同入口：

- `LanConnectHostFlow.StartLanHostAsync` 仅启动 ENet 主机；
- `LanConnectHostFlow.StartLobbyHostAsync` 启动 ENet 后将房间发布到大厅；
- `LanConnectContinueRunLobbyAutoPublisher` 在多人续局时只判断当前是否为 ENet 房主及大厅地址是否可用，不知道原存档由哪个入口创建。

因此，纯 LAN 存档第二次续局时也可能被自动发布为大厅房间。Issue #40 的日志显示，第一次纯 LAN 会话未创建大厅房间，第二次加载多人存档后却通过 `continue_save:NMultiplayerLoadGameScreen` 调用 `POST /rooms`，并创建“续局联机房间”。

本设计通过持久化房主创建通道解决该问题。

## 2. 目标与非目标

### 2.1 目标

- 纯 LAN 入口创建的存档在续局时不发布大厅房间；
- 大厅入口创建的存档在续局时继续自动发布大厅房间；
- 通道判断跨游戏重启保持有效；
- 客机加入、接受邀请和选择续局角色槽不修改本地存档的房主通道；
- 缺少通道字段的旧记录和无 binding 的旧存档继续按大厅存档处理，以保持现有兼容行为；
- 日志能够直接显示持久化通道、有效通道和续局决策。

### 2.2 非目标

- 不增加“手动发布到大厅”入口；
- 不支持在同一存档生命周期内将 `lan` 手动升级为 `lobby`；
- 不改变 saved-run slot、`desiredSavePlayerNetId` 或 `NotInSaveGame` 的角色身份校验规则；
- 不改变大厅服务 DTO 或服务端 API；
- 不改变中继、控制通道和心跳协议。

## 3. 核心不变量

1. 房主通道只由房主创建入口确定。
2. 纯 LAN 入口写入 `lan`；大厅入口写入 `lobby`。
3. 客机流程不创建、不覆盖房主通道记录。
4. 已显式记录的通道不会被普通保存事件更改。
5. 字段缺失、空值或未知值的有效通道均为 `lobby`。
6. 通道运行时状态必须绑定具体 `NetHostGameService` 实例，避免旧会话的延迟事件污染新会话。

## 4. 数据模型

在 `LanConnectSavedRoomBinding` 中增加：

```csharp
public string HostChannel { get; set; } = string.Empty;
```

增加集中定义与解析逻辑，例如：

```csharp
internal static class LanConnectHostChannels
{
    public const string Lan = "lan";
    public const string Lobby = "lobby";
}
```

解析规则：

| 持久化值 | 有效通道 |
| --- | --- |
| `lan` | `lan` |
| `lobby` | `lobby` |
| 缺失、空值 | `lobby` |
| 未知值 | `lobby`，同时记录 warning |

使用字符串而不是直接序列化枚举，避免旧 `config.json` 反序列化失败，并允许未来版本安全降级未知值。

`LanConnectConfig.CloneBinding`、规范化和 upsert 必须保留 `HostChannel`。规范化只能清理无效文本，不能把旧记录物理改写为 `lobby`；旧记录应在读取时按 `lobby` 解释，之后由成功的大厅房主保存路径自然写成显式 `lobby`。

## 5. Binding 语义

`LanConnectSavedRoomBinding` 扩展为多人存档的房主续局元数据。

大厅 binding 保存：

- `SaveKey`
- `RoomName`
- `Password`
- `GameMode`
- `HostChannel=lobby`
- 玩家签名和玩家名称

LAN binding 保存：

- `SaveKey`
- 可诊断的 LAN 房间名称
- 空密码
- `GameMode`
- `HostChannel=lan`
- 玩家签名和玩家名称

LAN binding 的房间名不参与大厅创建，仅供诊断及复用现有必填模型。建议使用稳定名称，例如“LAN 联机房间”，不依赖大厅上一次使用的房间名。

`PersistBinding` 应改为语义明确的房主持久化接口，并要求调用方传入经过校验的 `hostChannel`。内部只接受 `lan` 和 `lobby`，避免任意 source 字符串成为通道。

## 6. 创建入口与运行时状态

### 6.1 纯 LAN 创建

`StartLanHostAsync` 在 `StartENetHost` 成功后，向 `LanConnectLobbyRuntime` 注册：

- 当前 `NetHostGameService`；
- `HostChannel=lan`；
- 当前游戏模式；
- LAN 诊断房间名；
- 空密码。

此时新 run 通常尚未形成，无法生成稳定 `saveKey`，因此不能立即写配置。

### 6.2 大厅创建

`StartLobbyHostAsync` 只有在 `POST /rooms` 成功并 attach hosted session 后才建立 `lobby` 来源状态。创建失败时：

- 不写 binding；
- 不保留待持久化的 `lobby` 状态；
- 按现有逻辑断开 ENet。

`AttachHostedRoom` 持有完整的房间元数据，因此可继续作为大厅 binding 的权威来源。

### 6.3 运行时来源状态

复用 `LanConnectLobbyRuntime`，不创建新的全局 singleton。增加轻量房主来源状态，至少包含：

- `NetHostGameService` 引用；
- `HostChannel`；
- 房间名、密码、模式；
- 是否完成过成功持久化。

大厅 active session 优先作为来源；没有大厅 session 时，纯 LAN 来源状态允许现有 `SaveManager.Instance.Saved` 事件持久化 LAN binding。

注册新的房主服务时替换旧来源状态。来源状态只描述当前进程内房主会话，不替代持久化 binding。

## 7. 保存事件和持久化

`LanConnectLobbyRuntime.OnRunSaved` 的决策顺序：

1. 当前存在 active hosted lobby session：使用 session metadata 持久化 `lobby` binding；
2. 否则存在匹配当前房主网络服务的 LAN 来源状态：持久化 `lan` binding；
3. 否则不写任何通道记录。

写入前必须验证：

- 当前网络服务是 Host；
- 当前服务实例与已注册来源相同；
- 能成功加载当前多人存档并生成 `saveKey`。

存档尚不可读时保留来源状态，等待下一次 `Saved` 重试。成功后后续保存仍可更新玩家数量、签名和名称，但必须保持同一显式通道。

客机没有房主来源状态，也不满足 Host 校验，因此客机保存、接受邀请、IP 加入、大厅加入和 saved-run slot 选择都不会写入或覆盖 `HostChannel`。

## 8. 续局分流

`LanConnectContinueRunLobbyAutoPublisher` 在完成 host context 和 binding 解析后、调用大厅 API 前解析有效通道。

### 8.1 LAN 存档

有效通道为 `lan` 时：

- 将当前 screen 标记为已处理；
- 不调用 `PublishExistingHostToLobbyAsync`；
- 不发送 `POST /rooms`；
- 不创建 relay/control channel；
- 不显示“已自动恢复大厅房间”提示；
- 保留游戏当前已经建立的 ENet host；
- 输出 `skip_lan_origin` 决策日志。

LAN 玩家继续使用 LAN/IP 接入方式连接。

### 8.2 大厅存档

有效通道为 `lobby` 时保持现有行为：

- 构建 `LobbySavedRunInfo`；
- 自动创建大厅房间；
- attach hosted room；
- 启动控制通道和中继；
- 持久化显式 `HostChannel=lobby`；
- 显示大厅房间恢复成功提示。

### 8.3 旧存档兼容

以下情况按 `lobby` 处理：

- binding 存在但 `HostChannel` 字段缺失；
- binding 的 `HostChannel` 为空或未知；
- 完全没有 binding。

这保留旧大厅续局行为，但旧版本已经产生的纯 LAN 存档仍可能自动发布一次。从修复版本开始新创建并成功保存的 LAN 存档会稳定分流。

## 9. 生命周期与清理

运行时房主来源状态在以下情况清理：

- 对应 host 网络服务断开；
- host 启动失败；
- 注册新的房主服务；
- runtime `_ExitTree`。

清理运行时状态不能删除持久化 binding，否则退出游戏后无法判断续局通道。

放弃多人存档时，应在删除存档前取得 `saveKey`，并同步调用 `LanConnectConfig.RemoveSaveRoomBinding(saveKey)`。如果无法加载存档或无法生成 key，仍执行原有删除流程，并记录 binding 未清理的原因。

## 10. 诊断与日志

`LanConnectSaveDiagnostics` 增加：

- `bindingHostChannel`：原始持久化值或 `<missing>`；
- `effectiveHostChannel`：解析后的 `lan` 或 `lobby`；
- 可选当前运行时房主来源通道。

续局日志至少包含：

- `saveKey`；
- `storedBinding`；
- `persistedHostChannel`；
- `effectiveHostChannel`；
- `decision=publish` 或 `decision=skip_lan_origin`。

旧字段缺失按正常兼容路径记录，不作为错误。未知非空值记录 warning 后按 `lobby` 处理。

## 11. 错误处理

- LAN 来源注册失败：ENet 主机仍可运行，但记录 error；不能静默假定已保护续局。
- 首次保存暂不可读：记录原因并等待下一次保存，不把通道降级为 `lobby`。
- 大厅发布失败：保持现有断开/提示行为，不创建错误的 `lobby` binding。
- 未知通道：按 `lobby` 降级并记录 warning，避免旧存档失去自动续局能力。
- 客机路径发现既有本地 binding：只读，不修改通道。

## 12. 测试策略

### 12.1 单元测试

- `lan` 解析为 `lan`；
- `lobby` 解析为 `lobby`；
- 缺失、空值按 `lobby`；
- 未知值按 `lobby`；
- clone/upsert 保留通道；
- 持久化接口拒绝非法通道；
- 既有显式通道不会被普通保存事件意外切换；
- 客机网络服务不能持久化房主通道；
- LAN 续局决策为 skip，大厅和旧存档决策为 publish。

### 12.2 集成流程

1. 纯 LAN 新局首次保存后产生 `HostChannel=lan`；
2. 重启后加载该存档，不出现 `POST /rooms`；
3. 大厅新局首次保存后产生 `HostChannel=lobby`；
4. 重启后加载大厅存档，仍自动创建大厅房间；
5. 旧 binding 无通道字段时仍自动发布；
6. 无 binding 旧存档仍自动发布；
7. 客机接受邀请并保存时不新增或覆盖本地房主通道；
8. 大厅创建失败不留下 `lobby` binding；
9. 放弃存档同步删除对应 binding。

## 13. Issue #40 验收标准

1. 使用修复版本从纯 LAN 入口创建房间；
2. LAN 客机正常进入并开始游戏；
3. 游戏至少完成一次多人存档保存；
4. 房主退出并从多人续局重新开启；
5. 日志显示 `effectiveHostChannel=lan` 和 `decision=skip_lan_origin`；
6. 客户端不发送 `POST /rooms`；
7. 大厅列表不出现自动创建的“续局联机房间”；
8. 原 LAN 玩家仍可通过 LAN/IP 方式加入；
9. 对照测试中，大厅入口创建的存档继续自动恢复大厅房间。

## 14. 发布方式

- 在独立修复分支实现；
- 不修改 `releases/` 镜像目录；
- 完成客户端构建和相关测试后提交实现；
- 推送修复分支到远程仓库，供后续审查和合并。
