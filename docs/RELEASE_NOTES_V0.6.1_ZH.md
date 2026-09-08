# STS2 LAN Connect v0.6.1 正式版发布说明

发布日期：2026-09-08

> 状态：**正式版（stable）**。客户端与 lobby-service 同步定为 `0.6.1`。
> 分发：GitHub Release（客户端 `sts2_lan_connect-release.zip` + 服务端 `sts2_lobby_service.zip`）。Steam 创意工坊「游戏大厅」条目（`3749766330`）目前仍停留在 `0.6.0`，是否同步更新另行处理。

`0.6.1` 是 `0.6.0` 之后的第一个正式版，收敛了 `0.6.1-alpha.1`~`alpha.5` 全部五个测试候选的改动。核心是把新协议房间的载体从「要求全员 RitsuLib 安装状态一致的 typed-sidecar」换成 **`native_bus_v1`**（游戏官方 MOD 消息注册通道），彻底消除与 RitsuLib 的运行时耦合；并修复了这次切换过程中暴露的联机加入、续局与存档问题。

## 一、新协议房间与 RitsuLib 彻底解耦（v0.6.1 的主线）

- `0.6.0` 的 `tail_v1` 房间虽然不要求装同一批 MOD，但仍依赖 RitsuLib 的公开 typed-sidecar API：有 RitsuLib 只能连有 RitsuLib，无 RitsuLib 只能连无 RitsuLib，混合组合在连接前就被拒绝。
- `0.6.1` 把 `tail_v1` 房间统一改成 **`native_bus_v1`** 载体：注册自定义 `INetMessage`（游戏官方 MOD 消息机制，与 BaseLib 同用），协议容器经原版 ch0 FIFO 投递，**完全不再关心是否安装 RitsuLib**，也不再要求双方状态一致。`0.5.18` 版 RitsuLib 那种「已装但 sidecar 不可用」的事故状态，现在照常建房 / 加入。
- 兼容模式 `compat_4_5_v1` 不受影响：固定 `4/5-bit` 编码，支持 2-8 人，继续禁止 RitsuLib。
- 新增 registry fingerprint（`sha256:v1`）主门禁，置于 wire-cache 预检查之前；`minimumClientVersion` 升至 `0.6.1-alpha.1`——旧 `0.6.0` 客户端加入新协议房间会被 `lan_registry_fingerprint_required` 拒绝并提示升级，新客户端加入旧载体房间被 `lan_legacy_carrier_unsupported` 拒绝。
- 联机协议前端措辞收敛为「兼容旧版 Mod」/「新协议」；新协议房间不再展示 RitsuLib 相关标签，加入门禁提示改为版本要求而非 MOD 组合要求。

## 二、加入失败与黑屏的根因修复（alpha.4）

- **根因**：`0.6.0` 上报的"进不去新协议房间"（握手后静默、10 秒被房主踢出，或双方准备后黑屏）都来自同一机制——RitsuLib 给 `NetMessageBus.SerializeMessage<InitialGameInfoMessage|LobbyBeginRunMessage|StateDivergenceMessage>` 打 Harmony 补丁后，替换体是优化编译的，会把小结构体 `T.Serialize` 内联进去，本 MOD 挂在该方法上的"容器生产"钩子被绕过，扩展帧从未产生。
- `native_bus_v1` 切换本身已从架构上消除这条依赖链；桌面平台的 9 个 tail 序列化钩子额外改挂 `NetMessageBus.SerializeMessage<T>` 闭合实例化本身（0.107.1 / 0.111.0 签名相同），不再受调用方是否内联影响。安卓因 gshared 限制保持 `T.Serialize`。
- 传输层待发扩展帧改为按内容前缀匹配（容忍第三方在发送前给包加 trailer，例如 RitsuLib 0.5.18 的 36 字节 NativeTrailer）；配对屏障超时改为定时触发，扩展帧缺失在 2 秒内以 `lan_extension_missing` 明确报错，不再沉默到房主 10 秒踢人。

## 三、启动自检与错误码补全（alpha.1 / alpha.3）

- 启动自检覆盖消息注册表 ≤256、byte 映射唯一、BaseLib 128/129 冲突检测；第三方 MOD 提前初始化消息注册表（此时 `AssemblyInfo` 尚未就绪）时自检改为挂起并延后至首次联机会话补跑，不再误判为终局失败并把整个会话打入联机降级模式。
- tail 拒绝码表补齐 `0.6.1` 全部七个新错误码（`lan_legacy_carrier_unsupported`、`lan_registry_fingerprint_required/mismatch`、`lan_client_version_too_old`、`lan_native_frame_invalid`、`lan_type_id_mismatch`、`lan_extension_missing`），运行时失败不再退化为原版"模组不匹配"；旧客户端收到新码会解码为通用协议失败，不会崩溃。
- 新增 `tail:` 诊断日志（矩阵消息扣住 / 扩展帧到达 / 待发登记 / 扩展帧发送 / native flow 绑定·激活·延迟），便于反馈定位。

## 四、存档、续局与 QuickSL 等第三方 MOD 兼容（alpha.5）

- **根因**：`native_bus_v1` 载体在 `LanConnectProtocolCarrier.ToWireValue` 里缺少 wire 值映射，导致新协议房间房主**每次存档**都抛 `Unknown protocol carrier enum value 3`。
- 后果链：房间绑定从未写入，续局时无法判断上次是大厅还是 LAN，选"恢复大厅房间"被误判为兼容房并遭 RitsuLib 门禁拒绝；QuickSL 等第三方存档后置 MOD 的多人同步重载（会在房主侧先存档）被同一异常打断，客机随即被断线。
- 补上 wire 值映射；同时给存档事件处理器加了异常兜底（`LanConnectSaveEventGuard`）：MOD 内部持久化失败只记录 `save_binding: persist failed` 告警，不再把异常抛进原版 `SaveManager` / RitsuLib / JmcModLib 的存档管线，防止同类问题再次波及第三方 MOD。

## 五、版本与升级

| 组件 | 0.6.1 | 上一个正式版 |
|---|---|---|
| 客户端 MOD | `0.6.1` | `0.6.0` |
| lobby-service | `0.6.1` | `0.6.0` |

**玩家**

1. 下载 `sts2_lan_connect-release.zip`，用包内一键脚本或手动覆盖安装。
2. 安装或更新后必须**完整重启游戏**。
3. **同一房间所有成员必须统一使用客户端 `0.6.1` 及以上**。
4. 新协议房间不再要求是否安装 RitsuLib 一致；如果队友装了 RitsuLib 而你没装（或反过来），现在可以正常同房。
5. 兼容模式房间继续禁止 RitsuLib；direct-IP 直连仍只支持兼容模式。

**服主**

- lobby-service `0.6.1` 与 `0.6.0` **代码功能等价**（`0.6.1-alpha.2` 起服务端代码未变，只对齐版本号）；已部署 `0.6.0` 或任意 `0.6.1-alpha.x` 的节点可选升级，不升级不影响新协议房间的运行（协议行为由客户端与门禁逻辑共同决定，服务端字段早在 alpha.1 就已支持）。
- 本 Release 附带 `sts2_lobby_service.zip`，非 pre-release：已开启自动更新的节点会自动升级到 `0.6.1`。手动路径见 `docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`。

## 六、验收

**自动化发布门禁**

- `RITSULIB_ASSEMBLY=<官方 RitsuLib dll> ./scripts/verify-release.sh` 全绿：lobby-service 608/608，客户端主 xUnit 套件 1157 项（另 1 项既有原型测试跳过），独立 ProtocolPlanTests 11 项，GdUnit 运行时套件（使用真实 `sts2.dll` 与官方 RitsuLib 程序集）全部通过。
- 打包产物按显式白名单校验：公开包不含 `typing.dll`、游戏程序集、游戏图片 / 字体或除本 MOD 外的任何 PCK；两次独立打包哈希一致。

**真机双实例 E2E（本机，两端均装 RitsuLib 0.5.18，测试节点 sts2-test，0.111.0）**

| 场景 | 结果 |
|---|---|
| 新协议房间开局后存档 | 通过：绑定写入成功，`ProtocolCarrier=native_bus_v1`，无 `Failed to save run` |
| 房主保存并退出 → 读档多人游戏 | 通过：自动恢复大厅房间，无"无法确认大厅还是 LAN"提示，无 RitsuLib 拒绝 |
| 队友从游戏大厅重新加入续局房间 | 通过：接管原槽位，双方回到同一局 |
| QuickSL 1.8.0 + JmcModLib 1.9.0 多人快速 SL | 通过：`多人快速 SL 完成`，双方保持连接 |
| 0.111.0 新协议建房、0.107.1 兼容房建房 | 通过：mod 均正常加载 |

## 七、已知限制

- Steam 创意工坊「游戏大厅」条目暂未同步到 `0.6.1`，仍显示 `0.6.0`；GitHub Release 是当前获取 `0.6.1` 的唯一渠道。
- direct-IP 直连只支持兼容模式；LAN 建房 / 直连 / 仅 LAN 续局尚未支持新协议或 RitsuLib（计划在 `0.6.2` / alpha6 中处理）。
- 怪物目标引用（富聊天）仍未开放。
- 服务器频道聊天历史只存在于单个节点的进程内存中，重启即清空；房间聊天不保存历史。
- 历史 `0.3.x`-`0.5.x` 客户端与 `0.6.1` 的真实互通不在测试与发布门禁范围内。

## 八、下载校验

- `sts2_lan_connect-release.zip`: `424573f580560cfbac608bfaa9f06450ec3c454f486b926cf07a64bc13da76bd`
- 客户端运行时 `sts2_lan_connect.dll`: `93c4abb80df9ce6157b2080ec071b4d10b8e6d14cccdbd60564916851d13dd94`
- 客户端运行时 `sts2_lan_connect.pck`: `b9907bd5a1e2afc1609d589fc3f43c9943f6f0900792ca68fafc154028720597`（与 `0.6.0` 起历次版本相同：本项目历次改动都在 C# 代码与文档，PCK 资源未变）
- `sts2_lobby_service.zip`: `a5b68611355935ff2ee17a52ea253bde041d3fd503fc03a5ea5ee3ae7df9eb59`

## 九、反馈与交流

遇到问题、想反馈 bug 或参与测试，欢迎加群：

- **联机大厅 8 群：341498145**
- **测试群（要求会导出 log）：1093309523**

反馈时请尽量附上双方完整的 `godot.log` 与客户端内的本地调试报告；如果是同一台电脑双开测试，请把第二个实例切到不同的存档槽位，避免两个实例共用同一份多人存档互相覆盖。
