# STS2 LAN Connect v0.6.0-alpha.1 验收记录

## 范围

本记录覆盖 v0.6 当前客户端/服务端契约、standalone Tail、公开 Ritsu typed-sidecar、presence/readiness 门禁、兼容 profile、结构化错误、打包与跨平台联机。历史 `0.3.x-0.5.x` 客户端运行、抓包和 fixture 明确不在范围内。

## 自动化门禁

| 门禁 | 状态 | 证据 |
|---|---|---|
| lobby-service typecheck/tests | PASS | 2026-08-16 最终 `verify-release.sh`：604 pass / 0 fail |
| xUnit 全量 | PASS | 1084 pass / 1 skip / 0 fail |
| GdUnit 全量（真实 RitsuLib 程序集） | PASS | 348 pass / 0 fail；测试程序集显式传入官方 RitsuLib v0.5.12 DLL |
| 临时包 release gate | PASS | client SHA-256 `81db4e16720f7574d2180dd051aa1b85ebfec9a69643226b301331620154c076`；service SHA-256 `da8439f4726f1c7e46e31bab27d01bf42e91d01d195ff6431c8500903438f786` |
| 包内无 Ritsu/game/prototype DLL | PASS | 包内容 allowlist、installer dry-run、法律文件和 forbidden DLL 扫描通过 |

## 行为门禁

| 场景 | 状态 | 必需证据 |
|---|---|---|
| no-Ritsu host / no-Ritsu joiner | PASS（真实 Android / macOS） | macOS host 创建 `tail_v1` / `standalone_tail_v1` 房间，Android 获得 ticket、加入、ready，并与 host 同时进入 Neow；修复后的 begin-run 日志无 Tail block/disconnect |
| Ritsu host / Ritsu joiner | BLOCKED（外部依赖） | RitsuLib v0.5.12 在 Android `ApplySerializePatches` 初始化阶段无响应；LAN Connect 未 fork/修改 RitsuLib，生产路径保持 fail-closed |
| Ritsu host / no-Ritsu joiner | PASS（真实门禁 + 自动化） | Android no-Ritsu joiner 对 macOS Ritsu room 得到 `ritsulib_presence_mismatch` / “需要 RitsuLib”；服务端未签发 ticket 或占用 slot |
| no-Ritsu host / Ritsu joiner | PASS（服务端门禁） | 同上 |
| Ritsu sidecar unavailable | PASS（自动化门禁）/ BLOCKED（真实 all-Ritsu runtime） | create/join 均在 transport 前 fail-closed；Android 外部 Ritsu 初始化问题阻止真实 all-Ritsu carrier 运行 |
| direct-IP Tail intent | PASS | xUnit/GdUnit 客户端 direct-IP 协议测试包含 Tail local rejection 与 compat-only 入口 |
| direct-IP compat + local Ritsu | PASS | xUnit/GdUnit 客户端 direct-IP 协议测试包含 local Ritsu before transport rejection |
| compat v0.6 / v0.6 | PASS | xUnit `LanConnectSerializationPatchesCompatibilityTests` / dispatcher tests 覆盖固定 `4/5-bit` 与 Ritsu 禁止 |
| load join / running rejoin | PASS（自动化 runtime） | GdUnit runtime 测试覆盖 LoadJoin/Rejoin full roster restore；协议失败通过 structured rejection |

## 平台矩阵

| 组合 | no-Ritsu Tail | Ritsu Tail | mismatch | compat |
|---|---|---|---|---|
| Windows / Windows | WAIVED | WAIVED | WAIVED | 自动化构建/包门禁 PASS |
| Windows / Android | WAIVED | WAIVED | WAIVED | 自动化构建/包门禁 PASS |
| Windows / macOS | WAIVED | WAIVED | WAIVED | 自动化构建/包门禁 PASS |
| Android / macOS | PASS | BLOCKED（RitsuLib Android 初始化） | PASS（Ritsu host / no-Ritsu joiner）；反向自动化 PASS | 自动化 PASS |

## 2026-08-15 追加验证

证据目录：`artifacts/evidence/v0-6-dual-protocol-android-macos-20260815T091151Z/`。

- full-message golden fixture gate：PASS。15 组 `tail-full-*-v1.bin/json` 覆盖当前 v0.111.0 的 10 个消息种类、3 个 InitialGameInfo failure/rejection 阶段，以及 BeginRun 2/4/5/8p。LAN container/entry bytes 独立手工编写；完整消息的 opaque vanilla body 由生产序列化器捕获，并通过独立 parser、byte-map、固定边界与 SHA-256 验证，测试不会重新生成 expected bytes。
- Android payload/import：PASS。v0.1.9 launcher 在 API 35 arm64 emulator 上导入本机 v0.111.0 payload，selected payload 为 `v0.111.0` / commit `41cef1ea`，compat target 为 `v0.111.0`。
- Android MOD 安装与启动：PASS（启动级）。最终包 `sts2_lan_connect-release.zip` 导入后，launcher status 显示 `sts2_lan_connect` `0.6.0-alpha.1` enabled；`android-launch-lan-connect-final-result.json` 为 `succeeded`，启动 `GodotApp`，45 秒日志窗口未见 `InvalidProgram` 或 fatal app crash。
- macOS 安装：PASS。`build-install-macos-final.log` 显示本机 Steam v0.111.0 app bundle 内 MOD 文件已验证，app bundle signature refreshed。
- Android / macOS 实际联机：PASS（no-Ritsu Tail）。使用 Sts2MobileLauncher v0.1.9、Android API 35 arm64 emulator、两端 v0.111.0 与同一 `0.6.0-alpha.1` DLL；完成 room ticket、InitialGameInfo、2 人 roster、双方 ready、LobbyBeginRun 和首个 Neow 同步游戏状态。
- Ritsu presence mismatch：PASS。macOS Ritsu host / Android no-Ritsu joiner 在 ticket 前得到结构化拒绝；反向由服务端分配副作用测试覆盖。
- Ritsu/Ritsu runtime carrier：BLOCKED（外部依赖）。RitsuLib v0.5.12 在 Android 自身网络 patch 初始化阶段无响应，早于 LAN Connect sidecar flow；不通过维护 RitsuLib 分支规避。
- Windows：WAIVED。按维护者明确决定不要求单独实机验证，构建和包门禁继续通过。

## 2026-08-16 发布复核

- RitsuLib 本地仓库已快进到官方 `v0.5.12`（commit `7eb1c68112166fdb1f08316616bb1c32eee66692`）；GitHub 单版本发布 ZIP SHA-256 为 `b7eed47d1570129ab028839a01eefc5721f4571eee2f49b07c2820e01853ebb8`，v0.111.0 DLL SHA-256 为 `7303da3eba870a68b6b76821c52d9f5b86e220a1464da2b3deef2007642be5f1`。
- macOS Ritsu 启动：PASS。framework 完整初始化，3 个动态消息补丁成功，游戏进入主菜单；这只证明启动，不冒充 macOS/macOS 双端联机。
- Android Ritsu 启动：BLOCKED。`RitsuNetMessageBusTailPatches.ApplySerializePatches` 调用 Harmony detour 时报告 `BUG: Unreferenced static string to 0: _initialize`，90 秒后仍为黑屏，未进入 sidecar flow。
- 最终完整 `verify-release.sh`：PASS。Lobby service 604/604；xUnit 1084 pass / 1 intentional skip；真实 Ritsu v0.5.12 程序集 GdUnit 348/348；客户端构建 0 warning / 0 error；双包 allowlist、法律文件与安装器 dry-run 全部通过。
- 本次发布已获维护者明确授权，客户端和 lobby-service 均有源码/版本更新，因此 GitHub Pre-release 必须同时附带两个 ZIP。

## 结论

**分路径结论**：no-Ritsu `tail_v1` 的实现、Android / macOS 真实联机和发布自动化均为 **GO**。presence mismatch 的大厅门禁为 **GO**。全 Ritsu Android 路径因 RitsuLib v0.5.12 自身初始化问题为 **NO-GO / EXTERNALLY BLOCKED**；LAN Connect 保持 fail-closed，且不承担 RitsuLib 分支维护。`v0.6.0-alpha.1` 只作为 GitHub Pre-release 发布，不改变 `v0.5.5` 的稳定版定位。
