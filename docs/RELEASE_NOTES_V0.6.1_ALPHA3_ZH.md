# STS2 LAN Connect v0.6.1-alpha.3 发布说明（内部预览）

- 日期：2026-09-05
- 版本：`0.6.1-alpha.3`（客户端与 lobby-service 同步；tail 房间 `minimumClientVersion` 仍为 `0.6.1-alpha.1`）
- **本版为 alpha 预览：GitHub-only Pre-release，不更新 Steam 创意工坊。**
- 服务端代码与 alpha.2 相同，只对齐版本号；已部署 alpha.2 服务端的节点可不升级。

## 一、修复：存在提前初始化消息注册表的第三方 Mod 时，启动即误入“联机降级模式”

alpha.2 反馈（Windows 0.111.0）：启动即弹“联机协议补丁未能完整安装（通常与 RitsuLib 的补丁冲突）”，删除 RitsuLib 也无效。

根因与 RitsuLib 无关：该玩家的某个第三方 Mod 在 mod 初始化阶段就建好了游戏的消息注册表（`MessageTypes`），
本 Mod 的启动自检见注册表可用便去计算注册表指纹，指纹计算调用了游戏的 `AssemblyInfo.ModForType`，
而 `AssemblyInfo.Init()` 要到主菜单前的 `ExecuteEssential` 才执行，于是抛出无消息的 `InvalidOperationException`，
自检把它当成终局失败进入降级模式。

alpha.3 起：注册表可用但 `AssemblyInfo` 未就绪时自检同样挂起（不缓存、不降级），延后到首次建房/加入时复检；
指纹计算在 `AssemblyInfo` 未就绪时抛带明确文案的异常。`AssemblyInfo` 是 0.111 才有的类型，alpha.3 改经反射适配器访问，
保证 0.107.1（无该类型）上 mod 仍可加载；新增 MemberRef 黑名单契约测试防止回归。日志中该情况显示为
`native_bus: pending reason="message registry or AssemblyInfo not yet initialized (deferred to first tail session)"`。

## 二、修复：tail 拒绝码表补齐 0.6.1 的七个错误码

0.6.1 新增的 `lan_legacy_carrier_unsupported`、`lan_registry_fingerprint_required`、`lan_registry_fingerprint_mismatch`、
`lan_native_frame_invalid`、`lan_client_version_too_old`、`lan_type_id_mismatch`、`lan_extension_missing`
此前没有进入线上拒绝条目的编码表：房主端编码失败，拒绝条目附不上，加入者只看到原版“模组不匹配”。
alpha.3 起这些码按 11..17 编码（前 10 个码不变），加入者能看到具体原因（例如“扩展帧未在 2000ms 内到达”）。
旧客户端收到 11..17 会显示为通用协议失败 `unknown_tail_rejection_N`，不会崩溃。

## 三、仍未解决

- 0.111.0 上加入新协议房时，加入方 `LobbyJoinRequest` 的扩展帧为何未送达（`lan_extension_missing` 的上游原因）仍在排查；
  本版只是让错误不再被掩盖为“模组不匹配”，请测试者继续反馈日志。
- 兼容房在 0.111.0 上双方准备后黑屏的 Windows 触发条件仍未复现。

## 四、升级与回滚

- 客户端覆盖安装即可；lobby-service 与 alpha.2 功能相同，可不升级。
- 回滚到 alpha.2：客户端覆盖回 alpha.2。

## 五、发布前验收

- ✅ `scripts/verify-release.sh`（lobby-service、xUnit、GdUnit、ProtocolPlan，双次打包哈希一致）——见 GitHub Release 正文。
- ✅ 本机 0.111.0 新协议建房 / 0.107.1 兼容房建房冒烟——见 GitHub Release 正文。
