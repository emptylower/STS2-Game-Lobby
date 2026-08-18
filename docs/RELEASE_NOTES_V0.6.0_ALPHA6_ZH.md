# STS2 LAN Connect v0.6.0-alpha.6 测试版说明

发布日期：2026-08-18

这是 Android 启动、“放弃多人存档”确认弹窗与活跃中继生命周期修复测试版。客户端和 lobby-service 均升级到 `0.6.0-alpha.6`；生产与自建服务端必须同步更新并重启。

## 用户反馈与原因

- Android 日志显示 LAN Connect 在安装 `NetMessageBus.SerializeMessage<LobbyBeginRunMessage>` 的闭合泛型 Harmony wrapper 时触发 gshared `BadImageFormatException`。兼容初始化随后回滚并中止，导致主页没有“联机大厅”入口。
- 该消息总线边界补丁用于桌面 RitsuLib 尾部保护。Android 的开始游戏序列化路径不需要它，但仍需要另外 6 个线上位宽补丁。
- “放弃多人存档”弹窗的选项区没有稳定的最小高度。在实际中文文案和较小视口下，“删除存档”按钮虽已布局为完整高度，可视区域却只剩一条红线。
- macOS 首次建房和第二次成功建房使用相同的 `tail_v1`、客户端版本、WireCache 签名和 capability digest。首次请求失败原因为 HTTP 响应提前结束，第二次相同请求返回 `201`；平台公告请求正常返回 `200`，不是协议摘要不一致。
- 用户提供的 Android 日志还显示 `RandomForeseer` 因依赖项 `STS2-RitsuLib` 未加载而被拒绝。该日志不能证明 Android 已成功加载最新稳定版 RitsuLib。
- 在启动器明确显示 RitsuLib v0.5.13 与 LAN Connect `2/2 Enabled` 后重新复现，Ritsu 的最后 3 个动态补丁会尝试编译 LAN Connect 注册的 `SerializePrefix<T>`。Android Mono 随即在 `method-to-ir.c:7969` 对该泛型补丁方法触发原生断言并终止进程，这才是“启用 Ritsu 后无法启动”的直接原因。
- 完整候选包联调中，Android 房主与 macOS 客机均成功收到 BeginRun 并进入涅奥，但 Android 的加载阶段超过房间心跳窗口。旧服务端将房间判为过期并立即释放仍承载游戏流量的 relay，随后 macOS 客机丢包断线；这是服务端中继生命周期问题，不是 Tail 序列化或 capability 协议不匹配。

## 本轮修复

- Android 跳过桌面专用的闭合泛型消息总线边界补丁，并保留 6 个必需的线上位宽补丁。启动日志会记录 `beginRunMessageBusBoundary=android_gshared_skip`，兼容初始化成功后主页可继续安装“联机大厅”入口。
- Tail 的 9 种出站消息各自使用非泛型具体 Harmony 前缀，不再向 Ritsu 动态补丁链暴露闭合泛型补丁方法。回归测试逐个读取 Harmony 注册信息并要求 `IsGenericMethod=false`、`ContainsGenericParameters=false`。
- 桌面端继续安装全部 7 个补丁，现有 RitsuLib 尾部保护行为不变。
- 补丁安装失败时先保存计数再回滚，诊断日志不再把真实失败误报为 `applied=0` / `failed=0`。
- 存档确认弹窗为选项滚动区设置最小可视高度，并暴露真实视口给布局回归测试；横屏和竖屏都必须完整显示危险按钮。
- 不对建房 POST 做盲目自动重试。响应被截断时，服务端可能已经成功创建房间；没有跨运行时幂等键时直接重试会产生重复或遗留房间。
- lobby-service 清理过期房间前检查 relay 的已认证房主状态。仍有活跃房主时保留房间与 relay；游戏流量停止后，relay 自身的空闲超时会清除端点，后续房间清理再释放端口，避免资源泄漏。
- Tail 房主建立控制通道时追加建房时冻结的 `clientVersion` 与 `capabilityDigest`，与加入者控制通道保持一致，避免服务端在 WebSocket 握手阶段拒绝房主并导致房间消息、玩家绑定不可用。

## RitsuLib 边界

- macOS 与 Android 均以官方 RitsuLib v0.5.13 为本轮唯一 Ritsu 测试版本。
- 最终候选客户端与服务端包已在 Android 和 macOS 均启用官方 RitsuLib v0.5.13 的环境中完成全 Ritsu 跨端建房、加入、准备、开局、持续连接与 SL 冷启动续局验收。
- LAN Connect 不卸载、不直接调用或恢复 RitsuLib 私有 Harmony postfix，也不维护 RitsuLib 分支。

## 发布验收

- 使用最终候选客户端包与完整 lobby-service 发布包启动本地环境，没有使用源码开发服务或生产服务器代替发布候选。
- macOS 通过 `--force-steam=off` 启动；Android 使用 Android Studio 管理的 Android 15 ARM64 虚拟设备，双方均加载官方 RitsuLib v0.5.13。
- 已完成主页入口、建房、发现、加入、ready、begin-run、进入地图、约 2 分钟持续中继，以及双方冷启动后的 SL 房间重发、原槽位重连和再次进入地图。
- 自动门禁再次通过：lobby-service 607 项、客户端 xUnit 1107 项（另有 1 项既有原型测试跳过）、GdUnit 356 项；发布脚本重新生成的运行时载荷与实测候选一致。

## 下载校验

- `sts2_lan_connect-release.zip`: `32224827e5a3e12e25173678c7fc381864bbef0b1703bfac38b9b708b9bb0bcf`
- `sts2_lobby_service.zip`: `31122a763b3aef4e95e06c3e353b9fcedc2bf2364e180cf8767bfa154e07b572`
- 客户端运行时 `sts2_lan_connect.dll`: `ec47ca8a84f4bcb406cbe6903e255f2426ec5c6856ae9c1350e8a823c639fc94`
- 客户端运行时 `sts2_lan_connect.pck`: `b9907bd5a1e2afc1609d589fc3f43c9943f6f0900792ca68fafc154028720597`

## 已知边界

- 本轮 macOS 端到端验收使用 `--force-steam=off` 的 ENet 路径；Steam 房主容量补丁未包含在本轮验证范围内，不宣称已经通过 Steam 房主容量验收。
- 这是 GitHub Pre-release 测试包，不替代稳定版。遇到异常时请保留双方从启动开始的完整日志。

## 更新要求

- 所有同房玩家统一安装客户端 `0.6.0-alpha.6`，删除旧 DLL/PCK 后完整重启游戏。
- lobby-service 必须同步升级到 `0.6.0-alpha.6` 并重启；仅更新客户端无法修复旧服务端误删活跃 relay 的问题。
- 若仍失败，请同时提交两端从游戏启动开始的完整 `godot.log`，并注明操作系统、游戏版本、RitsuLib 版本及是否实际加载成功。
