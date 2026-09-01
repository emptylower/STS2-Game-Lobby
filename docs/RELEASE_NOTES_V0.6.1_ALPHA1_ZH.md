# STS2 LAN Connect v0.6.1-alpha.1 发布说明（内部预览）

- 日期：2026-09-01
- 版本：`0.6.1-alpha.1`（客户端与服务端同步；`minimumClientVersion` 同值）
- **本版为 alpha 预览：协议载体全面切换 native_bus_v1，未经完整真机 E2E 矩阵验收前不对外发布。**

## 一、为什么换载体：RitsuLib 0.5.18 联机失败的结构性修复

v0.6 的 `ritsulib_sidecar_v1` 载体经反射把协议容器交给 RitsuLib 传输。RitsuLib 0.5.14 重写投递层后，
0.5.18 实测 sidecar 协商停滞 → 加入请求永远无法送达 → 房主 10 秒 `LobbyJoinTimeout` 踢人。连续适配
0.5.10 → 0.5.18 证明这是结构性成本：API 不变、行为会变，而对方的测试矩阵不包含我们。

v0.6.1 起，本 MOD 注册自定义 `INetMessage` 消息类型（游戏官方 mod 消息机制，BaseLib 同用），协议容器
紧跟原版消息、走原版 ch0 FIFO——**对 RitsuLib 的运行时依赖归零**，有无 RitsuLib 的房间走同一条代码路径。

## 二、必须全员升级 0.6.1

**tail_v1（"0.6 新协议"）房间要求房主与所有加入者使用 0.6.1-alpha.1 及以上。** 服务端在加入工单签发处
强制执行（无法绕过）：

| 场景 | 错误码 | 含义 |
|------|--------|------|
| 旧 0.6 客户端加入新房间 | `lan_registry_fingerprint_required` | 请升级 LAN Connect（携带本机 MOD 消息注册表指纹后重试） |
| 双端 MOD 消息注册表不一致 | `lan_registry_fingerprint_mismatch` | 双端 MOD 集合不同，请统一后重试（details 附双方摘要前 8 位） |
| 新客户端加入旧载体房间 | `lan_legacy_carrier_unsupported` | 该房间由旧版创建，请房主升级后重建房间 |
| 客户端版本过旧 | `lan_client_version_too_old` | 请升级至房间要求的版本 |
| 运行时帧异常 | `lan_native_frame_invalid` / `lan_type_id_mismatch` / `lan_extension_missing` | 结构化断开并给出明确原因，替代过去的无声超时 |

`compat_4_5_v1`（兼容旧版）房间沿用旧合同，不受上述门禁影响。

## 三、其他修复

- **RitsuLib 共存正面回归**：Ritsu 存在但 sidecar 不可用（0.5.18 事故状态）不再阻止建房/加入；
- `LobbyJoinTimeout` 列入可重试原因：中继候选失败后继续尝试直连候选（本次事故中 3 个直连候选从未被尝试）；
- 修复 0.111.0 上 roster 位投影静默失效的预存缺陷（玩家载荷由 class 变 struct 后按值写入器不可见）；
- 启动自检：消息注册表 ≤256 且 byte 映射唯一、与 BaseLib（128/129）无冲突，异常时明确报错并拒绝启用
  native 载体（不崩溃），诊断行输出本地 typeId 与注册表指纹。

## 四、升级与回滚

- 客户端覆盖安装即可；服务端部署 `lobby-service` 0.6.1-alpha.1 并重启。
- 回滚：卸载 0.6.1-alpha.1 客户端、重装 0.6.0；0.6.0 无法加入 0.6.1 创建的 tail 房间（错误码见上表）。

## 五、发布前验收（进行中）

- ✅ 客户端/服务端全量单测、契约测试、golden vector（native 前缀不变 + 容器迁移扩展帧）；
- ✅ 双版本 ABI 对比（0.107.1 ↔ 0.111.0，`docs/abi-reports/2026-09-01-native-bus/REPORT.md`）；
- ✅ 迁移零引用门禁（`scripts/check-native-bus-migration.sh`）；
- ⏳ 真机 E2E 矩阵（spec §6.2 全部 11 行：macOS 房主 + Android 客户端，含 0.5.18 事故回归行）——**发布阻断**。
