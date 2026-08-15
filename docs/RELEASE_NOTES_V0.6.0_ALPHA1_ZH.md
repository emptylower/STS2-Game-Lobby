# STS2 LAN Connect v0.6.0-alpha.1 发布说明

`0.6.0-alpha.1` 候选同步升级客户端与 lobby-service，引入显式房间协议选择，并移除 RC4 对 RitsuLib 私有 Harmony postfix 的卸载、调用和恢复桥。

## 发布附件

- `sts2_lan_connect-release.zip`：客户端 MOD，SHA-256 `81db4e16720f7574d2180dd051aa1b85ebfec9a69643226b301331620154c076`。
- `sts2_lobby_service.zip`：Lobby 服务端，SHA-256 `da8439f4726f1c7e46e31bab27d01bf42e91d01d195ff6431c8500903438f786`。
- 本版本仅作为 GitHub Pre-release；客户端与 lobby-service 必须配套更新。

## 房间模式

- **兼容模式（默认，`compat_4_5_v1`）**：固定使用 `4/5-bit` wire profile，支持 2-8 人，不允许安装 RitsuLib 的客户端创建或加入。
- **0.6 新协议**：原版消息主体保持 `2/3-bit`，完整 roster 由 LAN protocol v1 携带。所有玩家都未安装 RitsuLib 时使用 standalone carrier；所有玩家都安装且公开 sidecar API 就绪时使用 Ritsu sidecar carrier。
- 房间创建后 profile、carrier、RitsuLib presence 和 capability digest 均冻结，不会因加入失败自动降级或切换。
- 有 RitsuLib 只能连接有 RitsuLib，无 RitsuLib 只能连接无 RitsuLib；两个方向的混合组合都会在 join ticket 和 transport 创建前拒绝。

## RitsuLib 边界

LAN Connect 只使用 RitsuLib 的公开 typed-sidecar、直接 `INetGameService` send 和 session reachability API。它不读取私有 patch owner，不调用 Ritsu Tail 的 Write/Read，不维护 RitsuLib 分支，也不比较 RitsuLib 版本或第三方扩展集合。

同为 RitsuLib 只满足 LAN Connect 的 presence 门禁；游戏版本、MOD inventory、RitsuLib 自身规则仍可能拒绝连接。公开 sidecar API存在但未就绪时，以 `ritsulib_sidecar_unavailable` fail closed。

## Direct IP

`alpha.1` 的纯 IP 直连只支持兼容模式。由于没有 lobby ticket 和可信 flow nonce，直连不会启用 Tail 或手工设置 Ritsu reachability hint；本地检测到 RitsuLib 时在创建 transport 前拒绝。

## 升级要求

- 客户端与 lobby-service 必须同步升级到 `0.6.0-alpha.1`。
- 升级后完整重启游戏；服务端升级时重启进程以清除内存中的旧房间。
- 同房玩家必须使用相同的 STS2 游戏版本。
- 历史 `0.3.x-0.5.x` 客户端真实互通、抓包和 fixture 不在本 alpha 的测试或发布门禁中；兼容 profile 仅由当前 v0.6 契约测试覆盖。

## 诊断

提交联机问题时请提供双方完整日志。关键字段包括 profile、carrier、selected protocol、capability digest、roster revision、检测到的 RitsuLib presence/readiness，以及 standalone cursor 或 sidecar frame/handler 配对时间。

## 已知限制

- direct-IP Tail 不开放。
- 同为 RitsuLib 不代表任意 RitsuLib 版本或扩展组合均兼容。
- Android / macOS 的 no-Ritsu Tail 已在 v0.111.0 与 Sts2MobileLauncher v0.1.9 环境完成真实建房、加入、准备和开局验证。
- RitsuLib v0.5.12 在当前 Android 启动器环境初始化其网络补丁时无响应，因此全 Ritsu Android 路径保持 fail-closed / NO-GO；LAN Connect 不维护或分发 RitsuLib 分支。
- Windows 专项实机验证按本轮维护者决定豁免；Windows 构建和包内容门禁仍保留。
