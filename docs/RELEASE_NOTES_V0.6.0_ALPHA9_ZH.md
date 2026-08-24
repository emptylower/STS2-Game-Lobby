# STS2 LAN Connect v0.6.0-alpha.9 「大厅不显示」修复候选说明

发布日期：2026-08-23

> 状态：**GitHub-only Pre-release**；**不更新 Steam 创意工坊**（Workshop 二进制与描述继续停留在 alpha.7，待真机四象限验收通过后再议）。Android 回归复测为 **PENDING**。

`0.6.0-alpha.9` 修复「安装/更新 MOD 后大厅入口不出现」。根因是自 `0.6.0-alpha.1` 起存在的 MOD 加载顺序竞态：当 RitsuLib 先于本 MOD 初始化时，它声明在泛型类型上的补丁方法会把 `NetMessageBus.SerializeMessage<LobbyBeginRunMessage>` 等闭合泛型目标「毒化」，本 MOD 再补同一方法时 Harmony 2.4.2 抛 `InvalidProgramException`，初始化在第 6/10 阶段中止，大厅 UI 从未安装。完整机制见 `docs/STS2_LAN_CONNECT_ALPHA8_LOBBY_MISSING_RCA_ZH.md`。

本版同时修复审查发现的派生缺陷：begin-run 的 message-bus boundary 前缀只在旧泛型计划下启用，避免桌面无 RitsuLib 时开局消息丢失 Tail 容器。

## 本轮候选改动

- **全平台默认改用 `non_generic_v2` 补丁计划**（即 alpha.8 的 Android 计划）：15 步全部非泛型，不再向 Harmony 注册任何闭合泛型目标，从机制上免疫「外部 MOD 抢占闭合泛型目标」这一类故障。线上字节格式零变化，golden vector 在两套计划下逐字节一致。
- begin-run 的 message-bus boundary 前缀降级为「尽力而为」：仅旧 `desktop_generic_v1` 计划仍会应用它；失败时记录 `beginRunMessageBusBoundary=skipped_foreign_owner` 与外部 owner，不再计入必需补丁数。
- 协议补丁失败不再杀死整个 MOD：进入降级模式，大厅 UI 照常安装可浏览，但建房 / 加入 / 续局发布一律拒绝，并用游戏原生弹窗（`protocol_patch_conflict`）告知原因与恢复方法。
- 启动诊断新增 `mod_load_order` 事件，记录 RitsuLib 是否先于本 MOD 打补丁；补丁失败事件附带该目标的外部 owner 列表。
- 紧急回滚：桌面端设置环境变量 `STS2_LAN_CONNECT_TAIL_PLAN=desktop_generic_v1` 可回到旧泛型计划。

## 不再需要旧版绕过方法

alpha.8 及更早版本的临时口令（只关 RitsuLib → 启动一次 → 再开 RitsuLib）在 alpha.9 **不再需要**。任意加载顺序下大厅都会正常出现。

## 兼容性与更新要求

- 客户端升级到 `0.6.0-alpha.9`；lobby-service 继续使用 `0.6.0-alpha.6`，已部署 alpha.6 的服主无需再次升级。
- 不改变服务端 API、DTO 或协议版本；同房所有成员必须统一 alpha.9。
- `compat_4_5_v1` 房间依旧拒绝 RitsuLib（`ritsulib_not_allowed_in_compat_mode`），该行为不变。
- macOS 与 Android 的 RitsuLib 路径继续要求官方 v0.5.13+。

## 验收

真机四象限（发布门禁）：

| 场景 | 期望 |
|---|---|
| 桌面 + 无 RitsuLib | `beginRunMessageBusBoundary=skipped_non_generic_plan`，开局消息带 Tail，可开局 |
| 桌面 + RitsuLib 先加载 | `skipped_foreign_owner`，大厅正常，可开局 |
| 桌面 + RitsuLib 后加载 | 可开局，无回归 |
| Android | `applied=15/15`、`generic_target_count=0`，冷启动 3 次回归——**PENDING** 待真实设备复测 |

## 发布门禁

- `RITSULIB_ASSEMBLY=<official-v0.5.14-dll> ./scripts/verify-release.sh` 全绿：ProtocolPlanTests（含外部 owner 毒化回归用例）、主单元测试套件、GdUnit 运行时套件。
- GdUnit 分两个进程运行：主套件与 legacy 泛型计划的 golden vector 用例各一次，脚本会断言两次调用实际执行的用例数，防止过滤器漂移导致空跑。
- 两套补丁计划与 6 项位宽 transpiler 同时应用时，全部 golden vector 逐字节一致。

## 下载校验

- `sts2_lan_connect-release.zip`: `5725194f5f75b509a9822d747de0144ff910e48bf093a5d8926471d3ea646929`
- 客户端运行时 `sts2_lan_connect.dll`: `7c1ea764ce4c7f8104a5d49cb5b9bf3dc31c6d337315fef27809e5e92799558d`
- 客户端运行时 `sts2_lan_connect.pck`: `b9907bd5a1e2afc1609d589fc3f43c9943f6f0900792ca68fafc154028720597`（与 alpha.8 相同：本轮全部改动都在 C# 代码，PCK 资源未变）
