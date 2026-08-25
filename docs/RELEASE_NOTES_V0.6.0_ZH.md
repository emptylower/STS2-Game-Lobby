# STS2 LAN Connect v0.6.0 正式版发布说明

发布日期：2026-08-25

> 状态：**正式版（stable）**。客户端与 lobby-service 同步定为 `0.6.0`。
> 分发：GitHub Release（客户端 `sts2_lan_connect-release.zip` + 服务端 `sts2_lobby_service.zip`）与 **Steam 创意工坊**（条目「游戏大厅」`3749766330`，已更新到 `0.6.0`；此前该条目停留在 `0.5.5`）。

`0.6.0` 是 `0.5.5` 之后的第一个正式版，收敛了 `0.5.6-rc1`~`rc4` 与 `0.6.0-alpha.1`~`alpha.9` 全部九个测试候选的改动。核心是**双协议房间**与**加入前线上编码校验**：把「装了不同 MOD / 装了 RitsuLib 就黑屏、卡等待页」从一类偶发故障，变成加入前可解释、可拒绝的明确结果。

## 一、双协议房间（v0.6 的主线）

- 建房时显式选择**兼容模式 `compat_4_5_v1`** 或 **0.6 新协议 `tail_v1`**；选择结果连同 carrier、RitsuLib presence、capability digest 在房间生命周期内冻结，不会中途改变。
- 兼容模式固定使用 `4/5-bit` 编码，支持 2-8 人，**不允许 RitsuLib**。
- `tail_v1` 保持原版 `2/3-bit` 消息主体，完整 roster 由 LAN protocol v1 携带；无 RitsuLib 房间使用 standalone carrier，全员 RitsuLib 房间只使用 RitsuLib 的**公开 typed-sidecar API**。
- RitsuLib presence 必须同质：有 RitsuLib 只能连有 RitsuLib，无 RitsuLib 只能连无 RitsuLib；混合组合在 ticket 与 transport 分配之前就被拒绝，并给出结构化错误。
- 本版不卸载、不直接调用、也不恢复 RitsuLib 的私有 Harmony 补丁（`0.5.6-rc4` 的私有 postfix 桥已删除），不维护 RitsuLib 分支。
- direct-IP 直连在 v0.6 只支持兼容模式；本地 Ritsu 或 Tail intent 会在创建 transport 前拒绝。

## 二、加入前线上编码校验（WireCacheSignatureV1）

- 加入前比较双方真实使用的四张 ModelId net-id 表与四个编码位宽。
- `affects_gameplay: false` 只表示该 MOD 不进入原版 `idDatabaseHash`，**不保证它不占用 ModelId**；新签名补上了这层检查。
- 真实签名不一致时，在 lobby-service 签发 ticket 前、以及游戏 join request 发出前两道关卡明确拒绝，不再进入后黑屏或一方卡在等待页。签名缺失或不可读时仍然允许加入（fail-open）。
- 发布默认兼容配置为 `strict`；显式 relaxed 只适用于普通 MOD 差异，不能跳过真实线上编码或游戏版本不一致。

## 三、「安装/更新后大厅不显示」修复（alpha.9 的核心）

- 根因是自 `0.6.0-alpha.1` 起存在的 MOD 加载顺序竞态：RitsuLib 先初始化时，它声明在泛型类型上的补丁方法会「毒化」`NetMessageBus.SerializeMessage<T>` 这类闭合泛型目标，本 MOD 再补同一方法时 Harmony 2.4.2 抛 `InvalidProgramException`，初始化在第 6/10 阶段中止，大厅 UI 从未安装。完整机制见 `docs/STS2_LAN_CONNECT_ALPHA8_LOBBY_MISSING_RCA_ZH.md`。
- Tail 补丁**全平台默认使用 15 步 `non_generic_v2` 非泛型计划**，不再向 Harmony 注册任何闭合泛型目标，从机制上免疫这一类冲突。线上字节格式零变化：golden vector 在新旧两套计划下逐字节一致，6 项位宽 transpiler 同时应用时同样逐字节一致。
- `desktop_generic_v1` 保留为紧急回滚分支：桌面端设置环境变量 `STS2_LAN_CONNECT_TAIL_PLAN=desktop_generic_v1` 可回到旧泛型计划。
- 协议补丁失败不再杀死整个 MOD，而是进入**降级模式**：大厅 UI 照常安装、可浏览，建房 / 加入 / 续局发布一律拒绝，并用游戏原生弹窗（`protocol_patch_conflict`）说明原因与恢复步骤。
- 启动日志自查：`beginRunMessageBusBoundary=skipped_non_generic_plan`（无外部占用）或 `skipped_foreign_owner`（RitsuLib 先占）都是正常值。
- alpha.8 及更早版本的临时绕过口令（先关 RitsuLib → 启动一次 → 再开 RitsuLib）在 `0.6.0` **不再需要**。

## 四、续局、重开与身份

- SL/读档续局完整保留存档冻结的 profile、carrier、RitsuLib presence、WireCache 签名与 capability digest，不会重新协商成错误协议。
- 续局来源按 `lan` / `lobby` / 未知三态处理，未知存档只询问一次；safe-load 与存档修复不再误写或删除绑定。
- 房主端把大厅认证的玩家昵称同步到原生多人等待页和局内玩家列表，不再把客机显示为数字平台 ID。
- 自动 SL 后从房间管理点击「重开一局」，双方清理已断开的旧 `RunManager.NetService`，客机按原存档槽位自动加入房主重新发布的房间；即使多人子菜单已在当前进程中打开过，也会显式启动自动重连。
- 踢出使用与存档槽位分离的安装 credential 和当前占用者 binding handle，槽位接管后不会误封原主人；列表过期时服务端拒绝该操作。
- 「永久放弃多人存档」确认弹窗的按钮区保留最小可视高度，横竖屏都能完整显示两个选项。

## 五、Android 与稳定性

- Android Tail 使用固定 15 项非泛型补丁计划（即现在全平台默认的 `non_generic_v2`），不向 gshared 注册闭合泛型目标，避免 RitsuLib 动态补丁阶段触发 Mono `method-to-ir` 原生断言。
- lobby-service 不再因 Android 开局加载暂时超过房间心跳窗口而删除仍有活跃房主的中继；活跃中继由独立的空闲超时负责最终回收。
- Tail 房主控制通道会提交建房时冻结的客户端版本与 capability digest，房间消息和玩家控制绑定不再因协议身份缺失而失败。
- lobby-service 保留区分大小写的 Base64URL WireCache 签名，不再因大小写被改写而产生 `capability_digest_mismatch`。
- 启动诊断：`Entry.Init` 的 10 个阶段与每个稳定 patch ID 会写入私有 JSONL、原子哨兵和普通游戏 / logcat 日志，并新增 `mod_load_order` 事件记录外部 MOD 是否先于本 MOD 打补丁；补丁失败事件附带该目标的外部 owner 列表。诊断只保留最近 3 个 session 并按 64 MiB 上限清理。

## 六、版本与升级

| 组件 | 0.6.0 | 上一个正式版 |
|---|---|---|
| 客户端 MOD | `0.6.0` | `0.5.5` |
| lobby-service | `0.6.0` | `0.5.4` |

**玩家**

1. 下载 `sts2_lan_connect-release.zip`，用包内一键脚本或手动覆盖安装。
2. 安装或更新后必须**完整重启游戏**。
3. **同一房间所有成员必须统一使用客户端 `0.6.0`**，且游戏版本一致。
4. 使用 RitsuLib 时，macOS 与 Android 请统一安装官方 RitsuLib **v0.5.13 及以上**；不要继续使用 v0.5.12。
5. 客户端自动获取 MOD 只使用 Steam 创意工坊，不会从房主、服务端或任意 URL 下载 DLL / PCK / ZIP。

**服主**

- lobby-service `0.6.0` 与 `0.6.0-alpha.6` **代码无功能差异**，只对齐版本号；已经手动部署 alpha.6 的节点属于可选升级。
- 仍在 `0.5.4` 的节点建议升级：`0.6` 客户端的双协议、capability digest 与 binding-aware kick 需要 0.6 服务端支持。
- 本 Release 附带 `sts2_lobby_service.zip`，且不是 pre-release，因此**已开启自动更新的节点会自动升级到 `0.6.0`**（服务端自动更新只接受非 pre-release 且带该资产的 Release）。手动路径见 `docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`。
- 顺带说明：`0.6.0-alpha.x` 这种带预发布后缀的版本号不满足服务端自动更新的 `x.y.z` 格式，`0.6.0` 之后该检查恢复正常。

## 七、验收

**真机验收（维护者本轮实测，全部通过）**

| 场景 | 结果 |
|---|---|
| 桌面 Windows | 通过：大厅正常出现，建房 / 加入 / 开局正常 |
| 桌面 macOS | 通过 |
| Android | 通过 |
| 含官方 RitsuLib 的联机（任意加载顺序） | 通过：大厅正常出现，不再需要旧版绕过办法 |

**自动化发布门禁**

- `RITSULIB_ASSEMBLY=<官方 RitsuLib dll> ./scripts/verify-release.sh` 全绿：lobby-service 检查与测试、独立 ProtocolPlanTests（含外部 owner 毒化回归）、客户端主 xUnit 套件、GdUnit 运行时套件（使用真实 `sts2.dll` 与官方 RitsuLib 程序集）。
- GdUnit 分两个进程运行：主套件与 legacy 泛型计划的 golden vector 用例各一次，脚本断言两次调用实际执行的用例数，防止过滤器漂移导致空跑。
- 打包产物按显式白名单校验：公开包不含 `typing.dll`、游戏程序集、游戏图片 / 字体或除本 MOD 外的任何 PCK。

## 八、已知限制

- 历史 `0.3.x`-`0.5.x` 客户端与 `0.6.0` 的真实互通不在测试与发布门禁范围内；请全员升级。
- direct-IP 直连只支持兼容模式。
- 怪物目标引用（富聊天）仍未开放。
- 服务器频道聊天历史只存在于单个节点的进程内存中，重启即清空；房间聊天不保存历史。聊天昵称来自未验证的客户端 session，只能展示，不能作为身份凭据。
- 踢出会使另一位玩家针对同一槽位的在途 ticket 失效；新建存档开始游戏后的局中重连仍不可用。

## 九、下载校验

- `sts2_lan_connect-release.zip`: `19caeb9ffd8d8364b05c11363989b0187ebf706a12707332930646268b6385ea`
- 客户端运行时 `sts2_lan_connect.dll`: `e9256eae69d593974f10201d0ba399ab23f5d141ea7f658e91442762ddac602a`
- 客户端运行时 `sts2_lan_connect.pck`: `b9907bd5a1e2afc1609d589fc3f43c9943f6f0900792ca68fafc154028720597`（与 alpha.8/alpha.9 相同：本轮全部改动都在 C# 代码与文档，PCK 资源未变）
- `sts2_lobby_service.zip`: `bd21466164580ed5da44cd2a02b78e740d3efc52e983ab2d145ca22d5569f87e`

## 十、反馈与交流

遇到问题、想反馈 bug 或参与测试，欢迎加群：

- **联机大厅 8 群：341498145**
- **测试群（要求会导出 log）：1093309523**

反馈时请尽量附上双方完整的 `godot.log` 与客户端内的本地调试报告；Android 请按使用说明中的取证步骤提供 launcher 日志与 `adb logcat`。
