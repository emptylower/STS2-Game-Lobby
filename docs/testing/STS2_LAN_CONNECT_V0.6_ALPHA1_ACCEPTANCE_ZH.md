# STS2 LAN Connect v0.6.0-alpha.1 验收记录

## 范围

本记录覆盖 v0.6 当前客户端/服务端契约、standalone Tail、公开 Ritsu typed-sidecar、presence/readiness 门禁、兼容 profile、结构化错误、打包与跨平台联机。历史 `0.3.x-0.5.x` 客户端运行、抓包和 fixture 明确不在范围内。

## 自动化门禁

| 门禁 | 状态 | 证据 |
|---|---|---|
| lobby-service typecheck/tests | PENDING | 待最终命令记录 |
| xUnit 全量 | PENDING | 待最终命令记录 |
| GdUnit 全量（真实 RitsuLib 程序集） | PENDING | 待最终命令记录 |
| 临时包 release gate | PENDING | 待最终命令记录 |
| 包内无 Ritsu/game/prototype DLL | PENDING | 待最终命令记录 |

## 行为门禁

| 场景 | 状态 | 必需证据 |
|---|---|---|
| no-Ritsu host / no-Ritsu joiner | PENDING | ticket、连接、ready、begin-run、首个同步状态 |
| Ritsu host / Ritsu joiner | PENDING | public sidecar reachability、frame/vanilla barrier、无 standalone Tail |
| Ritsu host / no-Ritsu joiner | PENDING | `ritsulib_presence_mismatch`，slot/ticket/control/transport 全为 0 |
| no-Ritsu host / Ritsu joiner | PENDING | `ritsulib_presence_mismatch`，slot/ticket/control/transport 全为 0 |
| Ritsu sidecar unavailable | PENDING | host/joiner 都在 transport 前结构化拒绝 |
| direct-IP Tail intent | PENDING | initializer 调用为 0 |
| direct-IP compat + local Ritsu | PENDING | initializer 调用为 0 |
| compat v0.6 / v0.6 | PENDING | 固定 `4/5-bit`、Ritsu 双端拒绝 |
| load join / running rejoin | PENDING | snapshot 恢复、协议失败不重试 |

## 平台矩阵

| 组合 | no-Ritsu Tail | Ritsu Tail | mismatch | compat |
|---|---|---|---|---|
| Windows / Windows | PENDING | PENDING | PENDING | PENDING |
| Windows / Android | PENDING | PENDING | PENDING | PENDING |
| Windows / macOS | PENDING | PENDING | PENDING | PENDING |
| Android / macOS | PENDING | PENDING | PENDING | PENDING |

## 结论

**NO-GO / PENDING**。只有自动化门禁、真实 Ritsu 程序集测试、Android 门禁和要求的跨平台矩阵全部 PASS 后，才能改为 GO。缺少外部设备或对应平台的证据必须保留为 PENDING，不得以桌面单元测试代替。
