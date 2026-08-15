# STS2 LAN Connect v0.6.0-alpha.1 发布说明

`0.6.0-alpha.1` 同步升级客户端与 lobby-service，引入显式房间协议选择，并移除 RC4 对 RitsuLib 私有 Harmony postfix 的卸载、调用和恢复桥。

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
- Android 真机、Windows 与跨平台矩阵必须在 acceptance 文档中取得 PASS 后才能把候选标记为 GO。
