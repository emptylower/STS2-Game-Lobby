# STS2 LAN Connect v0.6.0-alpha.2 测试版说明

发布日期：2026-08-16

这是客户端修复测试版。客户端升级到 `0.6.0-alpha.2`；lobby-service 继续使用 `0.6.0-alpha.1`，无需重新部署服务端。同一房间的所有玩家仍应统一客户端、游戏版本和 RitsuLib 启用状态，更新后必须完整重启游戏。

## 本轮修复

- 修复全员启用 RitsuLib 的 `tail_v1` 房间在 ENet 握手成功后一直等待初始游戏信息的问题。客户端现在使用 ticket 分配的真实玩家 ID 初始化 sidecar；若房主控制绑定稍晚到达，首个游戏信息 sidecar 会暂存并在可信绑定完成后补发。
- 修复 SL/读档续局重新发布大厅房间时显示“联机协议不支持（`capability_digest_mismatch`）”的问题。续局现在完整复用存档中已经冻结的游戏版本和 WireCache 签名，不再混入当前运行时身份。
- 改进 LAN 调试建房和大厅建房弹窗：模式与协议选择项更大，适合触控；未解锁的每日挑战或自定义模式会保留当前有效选择并给出明确提示。

## 测试边界

- 兼容模式仍固定使用 `4/5-bit` 并禁止启用 RitsuLib；本版没有改变该协议规则。
- `tail_v1` 的 RitsuLib 房间仍要求所有参与者都启用 RitsuLib；有/无 RitsuLib 混合组合会在 ticket 和 transport 前拒绝。
- 提供的 Windows 日志显示 RitsuLib v0.5.12 已进入主菜单，实际复现的是联机首包等待。本版不宣称修复缺少异常堆栈的独立启动崩溃。
- 官方 RitsuLib v0.5.12 在当前 Android v0.111.0 环境初始化其自身网络补丁时仍可能黑屏；Android 玩家继续使用无 RitsuLib 路径。

## 验证结果

- 客户端 xUnit：1086 通过，1 个既有原型测试跳过。
- Godot/GdUnit：354 通过，包含真实 RitsuLib typed-sidecar 首包延迟绑定回归。
- lobby-service：TypeScript 检查通过，604 项测试通过。
- 发布包保持固定 12 文件白名单，不包含 RitsuLib DLL、测试文件、私有凭据或旧版 `mod_manifest.json`。
