# STS2 LAN Connect v0.6.0-alpha.5 测试版说明

发布日期：2026-08-17

这是客户端与 lobby-service 的 WireCache capability digest 修复测试版。所有玩家应统一升级客户端并完整重启游戏；自建服务应同步升级到 `0.6.0-alpha.5`。alpha.5 客户端兼容 `0.6.0-alpha.4` 服务端在本次问题中产生的旧式摘要，便于服务端滚动升级。

## 问题原因

- alpha.4 服务端已停止改写建房响应里的 `wireCacheSignature`，但 capability digest 的长度前缀编码路径仍会将签名转为小写。
- Base64URL 内容区分大小写。服务端因此返回“原始混合大小写签名 + 按小写签名计算的摘要”，客户端按响应字段重新计算后必然得到不同结果，并显示“联机协议不支持（capability_digest_mismatch）”。
- 之前的跨运行时 fixture 使用全小写的 `aabb`，无法暴露这条大小写路径；alpha.4 的实机客户端与最终 alpha.4 服务包也没有作为同一组候选产物完成发布前端到端复测。

## 本轮修复

- lobby-service 计算 capability digest 时完整保留 WireCache 签名大小写，使响应字段与摘要使用同一份 canonical 数据。
- alpha.5 客户端除 canonical 摘要外，仅额外接受 alpha.4 服务端按同一响应签名小写化后得到的旧式摘要；这项兼容不会放宽其他协议字段或任意摘要不一致。
- 跨运行时 digest fixture 新增真实混合大小写签名，并分别固定兼容协议与 0.6 新协议的预期摘要。
- 保留 alpha.4 的 SL 协议绑定完整持久化修复，续局继续复用存档冻结的 profile、carrier、RitsuLib presence、WireCache 签名与 capability digest。

## 本地验证

- macOS 使用 `--force-steam=off` 启动 alpha.5 客户端，连接本地 alpha.5 完整 lobby-service。
- `compat_4_5_v1` 普通建房成功，服务端保留真实混合大小写签名并返回 canonical digest。
- `tail_v1` 普通建房成功，服务端保留真实混合大小写签名并返回 canonical digest。
- 从真实多人存档执行 SL/读档后成功重新发布房间，没有再出现“联机协议不支持”。
- macOS 安装官方 RitsuLib v0.5.12 时复现启动后黑屏；升级到官方 v0.5.13 后游戏完整启动，并通过 Ritsu `tail_v1` 建房。服务端确认 `ritsulib_sidecar_v1`，客户端会话绑定为 `Host, netId=1`。

## 发布与复测要求

- 本版先在本地完整服务端完成端到端验证，再生成客户端与服务端 release 包；生产服务器只在 GitHub release 发布后升级，不属于发布前测试环境。
- 删除旧版客户端 DLL/PCK 后安装 alpha.5，不要只覆盖其中一个文件；所有参与者完整退出并重启游戏。
- macOS 的 RitsuLib 玩家应统一使用官方 v0.5.13；不要继续使用已复现启动黑屏的 v0.5.12。Android Ritsu v0.5.13 尚未完成实机验证，本版不宣称支持。
- 自建 lobby-service 必须同步升级 alpha.5 并重启。若仍失败，请同时提交客户端从启动开始的完整 `godot.log` 和服务端日志。
